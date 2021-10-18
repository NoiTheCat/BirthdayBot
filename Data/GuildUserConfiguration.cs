using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace BirthdayBot.Data;

/// <summary>
/// Represents configuration for a guild user as may exist in the database.
/// </summary>
class GuildUserConfiguration {
    public ulong GuildId { get; }
    public ulong UserId { get; }

    /// <summary>
    /// Month of birth as a numeric value. Range 1-12.
    /// </summary>
    public int BirthMonth { get; private set; }
    /// <summary>
    /// Day of birth as a numeric value. Ranges between 1-31 or lower based on month value.
    /// </summary>
    public int BirthDay { get; private set; }

    public string TimeZone { get; private set; }
    public bool IsKnown { get { return BirthMonth != 0 && BirthDay != 0; } }

    /// <summary>
    /// Creates a new, data-less instance without a corresponding database entry.
    /// Calling <see cref="UpdateAsync(int, int, int)"/> will create a real database enty
    /// </summary>
    private GuildUserConfiguration(ulong guildId, ulong userId) {
        GuildId = guildId;
        UserId = userId;
    }

    // Called by GetGuildUsersAsync. Double-check ordinals when changes are made.
    private GuildUserConfiguration(DbDataReader reader) {
        GuildId = (ulong)reader.GetInt64(0);
        UserId = (ulong)reader.GetInt64(1);
        BirthMonth = reader.GetInt32(2);
        BirthDay = reader.GetInt32(3);
        if (!reader.IsDBNull(4)) TimeZone = reader.GetString(4);
    }

    /// <summary>
    /// Updates user with given information.
    /// </summary>
    public async Task UpdateAsync(int month, int day, string newtz) {
        using (var db = await Database.OpenConnectionAsync().ConfigureAwait(false)) {
            using var c = db.CreateCommand();
            c.CommandText = $"insert into {BackingTable} "
                + "(guild_id, user_id, birth_month, birth_day, time_zone) values "
                + "(@Gid, @Uid, @Month, @Day, @Tz) "
                + "on conflict (guild_id, user_id) do update "
                + "set birth_month = EXCLUDED.birth_month, birth_day = EXCLUDED.birth_day, time_zone = EXCLUDED.time_zone";
            c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)GuildId;
            c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = (long)UserId;
            c.Parameters.Add("@Month", NpgsqlDbType.Numeric).Value = month;
            c.Parameters.Add("@Day", NpgsqlDbType.Numeric).Value = day;
            var tzp = c.Parameters.Add("@Tz", NpgsqlDbType.Text);
            if (newtz != null) tzp.Value = newtz;
            else tzp.Value = DBNull.Value;
            c.Prepare();
            await c.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        // Database update succeeded; update instance values
        BirthMonth = month;
        BirthDay = day;
        TimeZone = newtz;
    }

    /// <summary>
    /// Deletes information of this user from the backing database.
    /// The corresponding object reference should ideally be discarded after calling this.
    /// </summary>
    public async Task DeleteAsync() {
        using var db = await Database.OpenConnectionAsync().ConfigureAwait(false);
        using var c = db.CreateCommand();
        c.CommandText = $"delete from {BackingTable} "
            + "where guild_id = @Gid and user_id = @Uid";
        c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)GuildId;
        c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = (long)UserId;
        c.Prepare();
        await c.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    #region Database
    public const string BackingTable = "user_birthdays";
    // Take note of ordinals for use in the constructor
    private const string SelectFields = "guild_id, user_id, birth_month, birth_day, time_zone";

    internal static async Task DatabaseSetupAsync(NpgsqlConnection db) {
        using var c = db.CreateCommand();
        c.CommandText = $"create table if not exists {BackingTable} ("
            + $"guild_id bigint not null references {GuildConfiguration.BackingTable} ON DELETE CASCADE, "
            + "user_id bigint not null, "
            + "birth_month integer not null, "
            + "birth_day integer not null, "
            + "time_zone text null, "
            + "last_seen timestamptz not null default NOW(), "
            + "PRIMARY KEY (guild_id, user_id)" // index automatically created with this
            + ")";
        await c.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Attempts to retrieve a user's configuration. Returns a new, updateable instance if none is found.
    /// </summary>
    public static async Task<GuildUserConfiguration> LoadAsync(ulong guildId, ulong userId) {
        using var db = await Database.OpenConnectionAsync().ConfigureAwait(false);
        using var c = db.CreateCommand();
        c.CommandText = $"select {SelectFields} from {BackingTable} where guild_id = @Gid and user_id = @Uid";
        c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)guildId;
        c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = (long)userId;
        c.Prepare();

        using var r = c.ExecuteReader();
        if (await r.ReadAsync().ConfigureAwait(false)) return new GuildUserConfiguration(r);
        else return new GuildUserConfiguration(guildId, userId);
    }

    /// <summary>
    /// Gets all known user configuration records associated with the specified guild.
    /// </summary>
    public static async Task<IEnumerable<GuildUserConfiguration>> LoadAllAsync(ulong guildId) {
        using var db = await Database.OpenConnectionAsync().ConfigureAwait(false);
        using var c = db.CreateCommand();
        c.CommandText = $"select {SelectFields} from {BackingTable} where guild_id = @Gid";
        c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)guildId;
        c.Prepare();

        using var r = await c.ExecuteReaderAsync().ConfigureAwait(false);
        var result = new List<GuildUserConfiguration>();
        while (await r.ReadAsync().ConfigureAwait(false)) result.Add(new GuildUserConfiguration(r));
        return result;
    }
    #endregion
}
