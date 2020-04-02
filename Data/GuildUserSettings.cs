using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace BirthdayBot.Data
{
    /// <summary>
    /// Representation of a user's birthday settings within a guild.
    /// Instances are held and managed by <see cref="="GuildStateInformation"/>.
    /// </summary>
    class GuildUserSettings
    {
        private int _month;
        private int _day;
        private string _tz;

        public ulong GuildId { get; }
        public ulong UserId { get; }

        /// <summary>
        /// Month of birth as a numeric value. Range 1-12.
        /// </summary>
        public int BirthMonth { get { return _month; } }
        /// <summary>
        /// Day of birth as a numeric value. Ranges between 1-31 or lower based on month value.
        /// </summary>
        public int BirthDay { get { return _day; } }

        public string TimeZone { get { return _tz; } }
        public bool IsKnown { get { return _month != 0 && _day != 0; } }

        /// <summary>
        /// Creates a data-less instance without any useful information.
        /// Calling <see cref="UpdateAsync(int, int, int)"/> will create a real database enty
        /// </summary>
        public GuildUserSettings(ulong guildId, ulong userId)
        {
            GuildId = guildId;
            UserId = userId;
        }

        // Called by GetGuildUsersAsync. Double-check ordinals when changes are made.
        private GuildUserSettings(DbDataReader reader)
        {
            GuildId = (ulong)reader.GetInt64(0);
            UserId = (ulong)reader.GetInt64(1);
            _month = reader.GetInt32(2);
            _day = reader.GetInt32(3);
            if (!reader.IsDBNull(4)) _tz = reader.GetString(4);
        }

        /// <summary>
        /// Updates user with given information.
        /// NOTE: If there exists a tz value and the update contains none, the old tz value is retained.
        /// </summary>
        public async Task UpdateAsync(int month, int day, string newtz, Database dbconfig)
        {
            // TODO note from rewrite: huh? why are we doing this here?
            var inserttz = newtz ?? TimeZone;

            using (var db = await dbconfig.OpenConnectionAsync())
            {
                // Will do a delete/insert instead of insert...on conflict update. Because lazy.
                using (var t = db.BeginTransaction())
                {
                    await DoDeleteAsync(db);
                    using (var c = db.CreateCommand())
                    {
                        c.CommandText = $"insert into {BackingTable} "
                            + "(guild_id, user_id, birth_month, birth_day, time_zone) values "
                            + "(@Gid, @Uid, @Month, @Day, @Tz)";
                        c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)GuildId;
                        c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = (long)UserId;
                        c.Parameters.Add("@Month", NpgsqlDbType.Numeric).Value = month;
                        c.Parameters.Add("@Day", NpgsqlDbType.Numeric).Value = day;
                        var p = c.Parameters.Add("@Tz", NpgsqlDbType.Text);
                        if (inserttz != null) p.Value = inserttz;
                        else p.Value = DBNull.Value;
                        c.Prepare();
                        await c.ExecuteNonQueryAsync();
                    }
                    await t.CommitAsync();
                }
            }

            // We didn't crash! Get the new values stored locally.
            _month = month;
            _day = day;
            _tz = inserttz;
        }

        /// <summary>
        /// Deletes information of this user from the backing database.
        /// The corresponding object reference should ideally be discarded after calling this.
        /// </summary>
        public async Task DeleteAsync(Database dbconfig)
        {
            using (var db = await dbconfig.OpenConnectionAsync())
            {
                await DoDeleteAsync(db);
            }
        }

        // Shared between UpdateAsync and DeleteAsync
        private async Task DoDeleteAsync(NpgsqlConnection dbconn)
        {
            using (var c = dbconn.CreateCommand())
            {
                c.CommandText = $"delete from {BackingTable} "
                    + "where guild_id = @Gid and user_id = @Uid";
                c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)GuildId;
                c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = (long)UserId;
                c.Prepare();
                await c.ExecuteNonQueryAsync();
            }
        }

        #region Database
        public const string BackingTable = "user_birthdays";

        internal static void SetUpDatabaseTable(NpgsqlConnection db)
        {
            using (var c = db.CreateCommand())
            {
                c.CommandText = $"create table if not exists {BackingTable} ("
                    + $"guild_id bigint not null references {GuildStateInformation.BackingTable}, "
                    + "user_id bigint not null, "
                    + "birth_month integer not null, "
                    + "birth_day integer not null, "
                    + "time_zone text null, "
                    + "PRIMARY KEY (guild_id, user_id)"
                    + ")";
                c.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Gets all known birthday records from the specified guild. No further filtering is done here.
        /// </summary>
        internal static IEnumerable<GuildUserSettings> GetGuildUsersAsync(Database dbsettings, ulong guildId)
        {
            using (var db = dbsettings.OpenConnectionAsync().GetAwaiter().GetResult())
            {
                using (var c = db.CreateCommand())
                {
                    // Take note of ordinals for use in the constructor
                    c.CommandText = "select guild_id, user_id, birth_month, birth_day, time_zone "
                        + $"from {BackingTable} where guild_id = @Gid";
                    c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)guildId;
                    c.Prepare();
                    using (var r = c.ExecuteReader())
                    {
                        var result = new List<GuildUserSettings>();
                        while (r.Read())
                        {
                            result.Add(new GuildUserSettings(r));
                        }
                        return result;
                    }
                }
            }
        }
        #endregion
    }
}
