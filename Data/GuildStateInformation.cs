using Discord.WebSocket;
using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;

namespace BirthdayBot.Data
{
    /// <summary>
    /// Holds various pieces of state information for a guild the bot is operating in.
    /// Includes, among other things, a copy of the guild's settings and a list of all known users with birthdays.
    /// </summary>
    class GuildStateInformation
    {
        private readonly Database _db;
        private ulong? _bdayRole;
        private ulong? _announceCh;
        private ulong? _modRole;
        private string _tz;
        private bool _moderated;
        private string _announceMsg;
        private string _announceMsgPl;
        private bool _announcePing;
        private readonly Dictionary<ulong, GuildUserSettings> _userCache;

        public ulong GuildId { get; }
        public OperationStatus OperationLog { get; set; }

        /// <summary>
        /// Gets a list of cached registered user information.
        /// </summary>
        public IEnumerable<GuildUserSettings> Users {
            get {
                var items = new List<GuildUserSettings>();
                lock (this)
                {
                    foreach (var item in _userCache.Values) items.Add(item);
                }
                return items;
            }
        }

        /// <summary>
        /// Gets the guild's designated Role ID.
        /// </summary>
        public ulong? RoleId { get { lock (this) { return _bdayRole; } } }

        /// <summary>
        /// Gets the designated announcement Channel ID.
        /// </summary>
        public ulong? AnnounceChannelId { get { lock (this) { return _announceCh; } } }

        /// <summary>
        /// Gets the guild's default time zone.
        /// </summary>
        public string TimeZone { get { lock (this) { return _tz; } } }

        /// <summary>
        /// Gets whether the guild is in moderated mode.
        /// </summary>
        public bool IsModerated { get { lock (this) { return _moderated; } } }

        /// <summary>
        /// Gets the designated moderator role ID.
        /// </summary>
        public ulong? ModeratorRole { get { lock (this) { return _modRole; } } }

        /// <summary>
        /// Gets the guild-specific birthday announcement message.
        /// </summary>
        public (string, string) AnnounceMessages { get { lock (this) { return (_announceMsg, _announceMsgPl); } } }

        /// <summary>
        /// Gets whether to ping users in the announcement message instead of displaying their names.
        /// </summary>
        public bool AnnouncePing { get { lock (this) { return _announcePing; } } }

        // Called by LoadSettingsAsync. Double-check ordinals when changes are made.
        private GuildStateInformation(DbDataReader reader, Database dbconfig)
        {
            _db = dbconfig;

            OperationLog = new OperationStatus();

            GuildId = (ulong)reader.GetInt64(0);
            if (!reader.IsDBNull(1))
            {
                _bdayRole = (ulong)reader.GetInt64(1);
            }
            if (!reader.IsDBNull(2)) _announceCh = (ulong)reader.GetInt64(2);
            _tz = reader.IsDBNull(3) ? null : reader.GetString(3);
            _moderated = reader.GetBoolean(4);
            if (!reader.IsDBNull(5)) _modRole = (ulong)reader.GetInt64(5);
            _announceMsg = reader.IsDBNull(6) ? null : reader.GetString(6);
            _announceMsgPl = reader.IsDBNull(7) ? null : reader.GetString(7);
            _announcePing = reader.GetBoolean(8);

            // Get user information loaded up.
            var userresult = GuildUserSettings.GetGuildUsersAsync(dbconfig, GuildId);
            _userCache = new Dictionary<ulong, GuildUserSettings>();
            foreach (var item in userresult)
            {
                _userCache.Add(item.UserId, item);
            }
        }

        /// <summary>
        /// Gets user information from th is guild. If the user doesn't exist in the backing database,
        /// a new instance is created which is capable of adding the user to the database.
        /// </summary>
        /// <remarks>
        /// For users with the Known property set to false, be sure to call
        /// <see cref="GuildUserSettings.DeleteAsync(Database)"/> if the resulting object is otherwise unused.
        /// </remarks>
        public GuildUserSettings GetUser(ulong userId)
        {
            lock (this)
            {
                if (_userCache.ContainsKey(userId))
                {
                    return _userCache[userId];
                }

                // No result. Create a blank entry and add it to the list,
                // in case it gets updated and then referenced later.
                var blank = new GuildUserSettings(GuildId, userId);
                _userCache.Add(userId, blank);
                return blank;
            }
        }

        /// <summary>
        /// Deletes the user from the backing database. Drops the locally cached entry.
        /// </summary>
        public async Task DeleteUserAsync(ulong userId)
        {
            GuildUserSettings user = null;
            lock (this)
            {
                if (!_userCache.TryGetValue(userId, out user))
                {
                    return;
                }
                _userCache.Remove(userId);
            }
            await user.DeleteAsync(_db);
        }

        /// <summary>
        /// Checks if the given user is blocked from issuing commands.
        /// If the server is in moderated mode, this always returns true.
        /// Does not check if the user is a manager.
        /// </summary>
        public async Task<bool> IsUserBlockedAsync(ulong userId)
        {
            if (IsModerated) return true;

            // Block list is not cached, thus doing a database lookup
            // TODO cache block list?
            using (var db = await _db.OpenConnectionAsync())
            {
                using (var c = db.CreateCommand())
                {
                    c.CommandText = $"select * from {BackingTableBans} "
                        + "where guild_id = @Gid and user_id = @Uid";
                    c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)GuildId;
                    c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = (long)userId;
                    c.Prepare();
                    using (var r = await c.ExecuteReaderAsync())
                    {
                        if (await r.ReadAsync()) return true;
                        return false;
                    }
                }
            }
        }

        /// <summary>
        /// Checks if the given user is a moderator either by having the Manage Server permission or
        /// being in the designated moderator role.
        /// </summary>
        public bool IsUserModerator(SocketGuildUser user)
        {
            if (user.GuildPermissions.ManageGuild) return true;
            lock (this)
            {
                if (ModeratorRole.HasValue)
                {
                    if (user.Roles.Where(r => r.Id == ModeratorRole.Value).Count() > 0) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Adds the specified user to the block list, preventing them from issuing commands.
        /// </summary>
        public async Task BlockUserAsync(ulong userId)
        {
            // TODO cache block list?
            using (var db = await _db.OpenConnectionAsync())
            {
                using (var c = db.CreateCommand())
                {
                    c.CommandText = $"insert into {BackingTableBans} (guild_id, user_id) "
                        + "values (@Gid, @Uid) "
                        + "on conflict (guild_id, user_id) do nothing";
                    c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)GuildId;
                    c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = (long)userId;
                    c.Prepare();
                    await c.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task UnbanUserAsync(ulong userId)
        {
            // TODO cache block list?
            using (var db = await _db.OpenConnectionAsync())
            {
                using (var c = db.CreateCommand())
                {
                    c.CommandText = $"delete from {BackingTableBans} where "
                        + "guild_id = @Gid and user_id = @Uid";
                    c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)GuildId;
                    c.Parameters.Add("@Uid", NpgsqlDbType.Bigint).Value = (long)userId;
                    c.Prepare();
                    await c.ExecuteNonQueryAsync();
                }
            }
        }

        public void UpdateRole(ulong roleId)
        {
            lock (this)
            {
                _bdayRole = roleId;
                UpdateDatabase();
            }
        }

        public void UpdateAnnounceChannel(ulong? channelId)
        {
            lock (this)
            {
                _announceCh = channelId;
                UpdateDatabase();
            }
        }

        public void UpdateTimeZone(string tzString)
        {
            lock (this)
            {
                _tz = tzString;
                UpdateDatabase();
            }
        }

        public void UpdateModeratedMode(bool isModerated)
        {
            lock (this)
            {
                _moderated = isModerated;
                UpdateDatabase();
            }
        }

        public void UpdateModeratorRole(ulong? roleId)
        {
            lock (this)
            {
                _modRole = roleId;
                UpdateDatabase();
            }
        }

        public void UpdateAnnounceMessage(string message, bool plural)
        {
            lock (this)
            {
                if (plural) _announceMsgPl = message;
                else _announceMsg = message;

                UpdateDatabase();
            }
        }

        public void UpdateAnnouncePing(bool value)
        {
            lock (this)
            {
                _announcePing = value;
                UpdateDatabase();
            }
        }

        #region Database
        public const string BackingTable = "settings";
        public const string BackingTableBans = "banned_users";

        internal static void SetUpDatabaseTable(NpgsqlConnection db)
        {
            using (var c = db.CreateCommand())
            {
                c.CommandText = $"create table if not exists {BackingTable} ("
                    + "guild_id bigint primary key, "
                    + "role_id bigint null, "
                    + "channel_announce_id bigint null, "
                    + "time_zone text null, "
                    + "moderated boolean not null default FALSE, "
                    + "moderator_role bigint null, "
                    + "announce_message text null, "
                    + "announce_message_pl text null, "
                    + "announce_ping boolean not null default FALSE"
                    + ")";
                c.ExecuteNonQuery();
            }
            using (var c = db.CreateCommand())
            {
                c.CommandText = $"create table if not exists {BackingTableBans} ("
                    + $"guild_id bigint not null references {BackingTable}, "
                    + "user_id bigint not null, "
                    + "PRIMARY KEY (guild_id, user_id)"
                    + ")";
                c.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Retrieves an object instance representative of guild settings for the specified guild.
        /// If settings for the given guild do not yet exist, a new value is created.
        /// </summary>
        internal async static Task<GuildStateInformation> LoadSettingsAsync(Database dbsettings, ulong guild)
        {
            using (var db = await dbsettings.OpenConnectionAsync())
            {
                using (var c = db.CreateCommand())
                {
                    // Take note of ordinals for use in the constructor
                    c.CommandText = "select guild_id, role_id, channel_announce_id, time_zone, "
                        + " moderated, moderator_role, announce_message, announce_message_pl, announce_ping "
                        + $"from {BackingTable} where guild_id = @Gid";
                    c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)guild;
                    c.Prepare();
                    using (var r = await c.ExecuteReaderAsync())
                    {
                        if (await r.ReadAsync())
                        {
                            return new GuildStateInformation(r, dbsettings);
                        }
                    }
                }

                // If we got here, no row exists. Create it.
                using (var c = db.CreateCommand())
                {
                    c.CommandText = $"insert into {BackingTable} (guild_id) values (@Gid)";
                    c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (long)guild;
                    c.Prepare();
                    await c.ExecuteNonQueryAsync();
                }
            }

            // New row created. Try this again.
            return await LoadSettingsAsync(dbsettings, guild);
        }

        /// <summary>
        /// Updates the backing database with values from this instance
        /// This is a non-asynchronous operation. That may be bad.
        /// </summary>
        private void UpdateDatabase()
        {
            using (var db = _db.OpenConnectionAsync().GetAwaiter().GetResult())
            {
                using (var c = db.CreateCommand())
                {
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
                    c.Parameters.Add("@Gid", NpgsqlDbType.Bigint).Value = (ulong)GuildId;
                    NpgsqlParameter p;

                    p = c.Parameters.Add("@RoleId", NpgsqlDbType.Bigint);
                    if (RoleId.HasValue) p.Value = (long)RoleId.Value;
                    else p.Value = DBNull.Value;

                    p = c.Parameters.Add("@ChannelId", NpgsqlDbType.Bigint);
                    if (_announceCh.HasValue) p.Value = (long)_announceCh.Value;
                    else p.Value = DBNull.Value;

                    p = c.Parameters.Add("@TimeZone", NpgsqlDbType.Text);
                    if (_tz != null) p.Value = _tz;
                    else p.Value = DBNull.Value;

                    c.Parameters.Add("@Moderated", NpgsqlDbType.Text).Value = _moderated;

                    p = c.Parameters.Add("@ModRole", NpgsqlDbType.Bigint);
                    if (ModeratorRole.HasValue) p.Value = (long)ModeratorRole.Value;
                    else p.Value = DBNull.Value;

                    p = c.Parameters.Add("@AnnounceMsg", NpgsqlDbType.Text);
                    if (_announceMsg != null) p.Value = _announceMsg;
                    else p.Value = DBNull.Value;

                    p = c.Parameters.Add("@AnnounceMsgPl", NpgsqlDbType.Text);
                    if (_announceMsgPl != null) p.Value = _announceMsgPl;
                    else p.Value = DBNull.Value;

                    c.Parameters.Add("@AnnouncePing", NpgsqlDbType.Boolean).Value = _announcePing;

                    c.Prepare();
                    c.ExecuteNonQuery();
                }
            }
        }
        #endregion
    }
}
