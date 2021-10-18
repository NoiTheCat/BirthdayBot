using Discord.WebSocket;
using Npgsql;
using NpgsqlTypes;
using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;

namespace BirthdayBot.Data;

/// <summary>
/// Represents guild-specific configuration as exists in the database.
/// Updating any property requires a call to <see cref="UpdateAsync"/> for changes to take effect.
/// </summary>
class GuildConfiguration {
    /// <summary>
    /// Gets this configuration's corresponding guild ID.
    /// </summary>
    public ulong GuildId { get; }

    /// <summary>
    /// Gets or sets the guild's designated usable role ID.
    /// Updating this value requires a call to <see cref="UpdateAsync"/>.
    /// </summary>
    public ulong? RoleId { get; set; }

    /// <summary>
    /// Gets or sets the announcement channel ID.
    /// Updating this value requires a call to <see cref="UpdateAsync"/>.
    /// </summary>
    public ulong? AnnounceChannelId { get; set; }

    /// <summary>
    /// Gets or sets the guild's default time zone ztring.
    /// Updating this value requires a call to <see cref="UpdateAsync"/>.
    /// </summary>
    public string TimeZone { get; set; }

    /// <summary>
    /// Gets or sets the guild's moderated mode setting.
    /// Updating this value requires a call to <see cref="UpdateAsync"/>.
    /// </summary>
    public bool IsModerated { get; set; }

    /// <summary>
    /// Gets or sets the guild's corresponding bot moderator role ID.
    /// Updating this value requires a call to <see cref="UpdateAsync"/>.
    /// </summary>
    public ulong? ModeratorRole { get; set; }

    /// <summary>
    /// Gets or sets the guild-specific birthday announcement message.
    /// Updating this value requires a call to <see cref="UpdateAsync"/>.
    /// </summary>
    public (string, string) AnnounceMessages { get; set; }

    /// <summary>
    /// Gets or sets the announcement ping setting.
    /// Updating this value requires a call to <see cref="UpdateAsync"/>.
    /// </summary>
    public bool AnnouncePing { get; set; }

    // Called by Load. Double-check ordinals when changes are made.
    private GuildConfiguration(DbDataReader reader) {
        GuildId = (ulong)reader.GetInt64(0);
        if (!reader.IsDBNull(1)) RoleId = (ulong)reader.GetInt64(1);
        if (!reader.IsDBNull(2)) AnnounceChannelId = (ulong)reader.GetInt64(2);
        TimeZone = reader.IsDBNull(3) ? null : reader.GetString(3);
        IsModerated = reader.GetBoolean(4);
        if (!reader.IsDBNull(5)) ModeratorRole = (ulong)reader.GetInt64(5);
        string announceMsg = reader.IsDBNull(6) ? null : reader.GetString(6);
        string announceMsgPl = reader.IsDBNull(7) ? null : reader.GetString(7);
        AnnounceMessages = (announceMsg, announceMsgPl);
        AnnouncePing = reader.GetBoolean(8);
    }

    /// <summary>
    /// Checks if the given user exists in the block list.
    /// If the server is in moderated mode, this always returns true.
    /// </summary>
    public async Task<bool> IsUserBlockedAsync(ulong userId) {
        if (IsModerated) return true;

        using var db = await Database.OpenConnectionAsync().ConfigureAwait(false);
        using var c = db.CreateCommand();
        c.CommandText = $"select * from {BackingTableBans} "
            + "where guild_id = @Gid and user_id = @Uid";
        c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)GuildId;
        c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = (long)userId;
        c.Prepare();
        using var r = await c.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await r.ReadAsync().ConfigureAwait(false)) return false;
        return true;
    }

    /// <summary>
    /// Adds the specified user to the block list corresponding to this guild.
    /// </summary>
    public async Task BlockUserAsync(ulong userId) {
        using var db = await Database.OpenConnectionAsync().ConfigureAwait(false);
        using var c = db.CreateCommand();
        c.CommandText = $"insert into {BackingTableBans} (guild_id, user_id) "
            + "values (@Gid, @Uid) "
            + "on conflict (guild_id, user_id) do nothing";
        // There is no validation on whether the requested user is even in the guild. will this be a problem?
        c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)GuildId;
        c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = (long)userId;
        c.Prepare();
        await c.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the specified user from the block list corresponding to this guild.
    /// </summary>
    /// <returns>True if a user has been removed, false if the requested user was not in this list.</returns>
    public async Task<bool> UnblockUserAsync(ulong userId) {
        using var db = await Database.OpenConnectionAsync().ConfigureAwait(false);
        using var c = db.CreateCommand();
        c.CommandText = $"delete from {BackingTableBans} where "
            + "guild_id = @Gid and user_id = @Uid";
        c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)GuildId;
        c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = (long)userId;
        c.Prepare();
        var result = await c.ExecuteNonQueryAsync().ConfigureAwait(false);
        return result != 0;
    }

    /// <summary>
    /// Checks if the given user can be considered a bot moderator.
    /// Checks for either the Manage Guild permission or if the user is within a predetermined role.
    /// </summary>
    public bool IsBotModerator(SocketGuildUser user)
        => user.GuildPermissions.ManageGuild || (ModeratorRole.HasValue && user.Roles.Any(r => r.Id == ModeratorRole.Value));

    #region Database
    public const string BackingTable = "settings";
    public const string BackingTableBans = "banned_users";

    internal static async Task DatabaseSetupAsync(NpgsqlConnection db) {
        using (var c = db.CreateCommand()) {
            c.CommandText = $"create table if not exists {BackingTable} ("
                + "guild_id bigint primary key, "
                + "role_id bigint null, "
                + "channel_announce_id bigint null, "
                + "time_zone text null, "
                + "moderated boolean not null default FALSE, "
                + "moderator_role bigint null, "
                + "announce_message text null, "
                + "announce_message_pl text null, "
                + "announce_ping boolean not null default FALSE, "
                + "last_seen timestamptz not null default NOW()"
                + ")";
            await c.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        using (var c = db.CreateCommand()) {
            c.CommandText = $"create table if not exists {BackingTableBans} ("
                + $"guild_id bigint not null references {BackingTable} ON DELETE CASCADE, "
                + "user_id bigint not null, "
                + "PRIMARY KEY (guild_id, user_id)"
                + ")";
            await c.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Fetches guild settings from the database. If no corresponding entry exists, it will be created.
    /// </summary>
    /// <param name="nullIfUnknown">
    /// If true, this method shall not create a new entry and will return null if the guild does
    /// not exist in the database.
    /// </param>
    public static async Task<GuildConfiguration> LoadAsync(ulong guildId, bool nullIfUnknown) {
        using (var db = await Database.OpenConnectionAsync().ConfigureAwait(false)) {
            using (var c = db.CreateCommand()) {
                // Take note of ordinals for the constructor
                c.CommandText = "select guild_id, role_id, channel_announce_id, time_zone, "
                    + " moderated, moderator_role, announce_message, announce_message_pl, announce_ping "
                    + $"from {BackingTable} where guild_id = @Gid";
                c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)guildId;
                c.Prepare();
                using var r = await c.ExecuteReaderAsync().ConfigureAwait(false);
                if (await r.ReadAsync().ConfigureAwait(false)) return new GuildConfiguration(r);
            }
            if (nullIfUnknown) return null;

            // If we got here, no row exists. Create it with default values.
            using (var c = db.CreateCommand()) {
                c.CommandText = $"insert into {BackingTable} (guild_id) values (@Gid)";
                c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)guildId;
                c.Prepare();
                await c.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
        // With a new row created, try this again
        return await LoadAsync(guildId, nullIfUnknown).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates values on the backing database with values from this object instance.
    /// </summary>
    public async Task UpdateAsync() {
        using var db = await Database.OpenConnectionAsync().ConfigureAwait(false);
        using var c = db.CreateCommand();
        c.CommandText = $"update {BackingTable} set "
            + "role_id = @RoleId, "
            + "channel_announce_id = @ChannelId, "
            + "time_zone = @TimeZone, "
            + "moderated = @Moderated, "
            + "moderator_role = @ModRole, "
            + "announce_message = @AnnounceMsg, "
            + "announce_message_pl = @AnnounceMsgPl, "
            + "announce_ping = @AnnouncePing "
            + "where guild_id = @Gid";
        c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)GuildId;
        NpgsqlParameter p;

        p = c.Parameters.Add("@RoleId", NpgsqlDbType.Bigint);
        if (RoleId.HasValue) p.Value = (long)RoleId.Value;
        else p.Value = DBNull.Value;

        p = c.Parameters.Add("@ChannelId", NpgsqlDbType.Bigint);
        if (AnnounceChannelId.HasValue) p.Value = (long)AnnounceChannelId.Value;
        else p.Value = DBNull.Value;

        p = c.Parameters.Add("@TimeZone", NpgsqlDbType.Text);
        if (TimeZone != null) p.Value = TimeZone;
        else p.Value = DBNull.Value;

        c.Parameters.Add("@Moderated", NpgsqlDbType.Boolean).Value = IsModerated;

        p = c.Parameters.Add("@ModRole", NpgsqlDbType.Bigint);
        if (ModeratorRole.HasValue) p.Value = (long)ModeratorRole.Value;
        else p.Value = DBNull.Value;

        p = c.Parameters.Add("@AnnounceMsg", NpgsqlDbType.Text);
        if (AnnounceMessages.Item1 != null) p.Value = AnnounceMessages.Item1;
        else p.Value = DBNull.Value;

        p = c.Parameters.Add("@AnnounceMsgPl", NpgsqlDbType.Text);
        if (AnnounceMessages.Item2 != null) p.Value = AnnounceMessages.Item2;
        else p.Value = DBNull.Value;

        c.Parameters.Add("@AnnouncePing", NpgsqlDbType.Boolean).Value = AnnouncePing;

        c.Prepare();
        c.ExecuteNonQuery();
    }
    #endregion
}
