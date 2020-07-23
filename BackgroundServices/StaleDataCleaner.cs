using BirthdayBot.Data;
using NpgsqlTypes;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BirthdayBot.BackgroundServices
{
    /// <summary>
    /// Automatically removes database information for guilds that have not been accessed in a long time.
    /// </summary>
    class StaleDataCleaner : BackgroundService
    {
        public StaleDataCleaner(BirthdayBot instance) : base(instance) { }

        public override async Task OnTick()
        {
            // Build a list of all values to update
            var updateList = new Dictionary<ulong, List<ulong>>();
            foreach (var g in BotInstance.DiscordClient.Guilds)
            {
                var existingUsers = new List<ulong>();
                updateList[g.Id] = existingUsers;

                // Get list of IDs for all users who exist in the database and currently exist in the guild
                var savedUserIds = from cu in await GuildUserConfiguration.LoadAllAsync(g.Id) select cu.UserId;
                var guildUserIds = from gu in g.Users select gu.Id;
                var existingCachedIds = savedUserIds.Intersect(guildUserIds);
            }

            using var db = await Database.OpenConnectionAsync();

            // Statement for updating last_seen in guilds
            var cUpdateGuild = db.CreateCommand();
            cUpdateGuild.CommandText = $"update {GuildConfiguration.BackingTable} set last_seen = now() "
                + "where guild_id = @Gid";
            var pUpdateG = cUpdateGuild.Parameters.Add("@Gid", NpgsqlDbType.Bigint);
            cUpdateGuild.Prepare();

            // Statement for updating last_seen in guild users
            var cUpdateGuildUser = db.CreateCommand();
            cUpdateGuildUser.CommandText = $"update {GuildUserConfiguration.BackingTable} set last_seen = now() "
                + "where guild_id = @Gid and user_id = @Uid";
            var pUpdateGU_g = cUpdateGuildUser.Parameters.Add("@Gid", NpgsqlDbType.Bigint);
            var pUpdateGU_u = cUpdateGuildUser.Parameters.Add("@Uid", NpgsqlDbType.Bigint);
            cUpdateGuildUser.Prepare();

            // Do actual updates
            foreach (var item in updateList)
            {
                var guild = item.Key;
                var userlist = item.Value;

                pUpdateG.Value = (long)guild;
                await cUpdateGuild.ExecuteNonQueryAsync();

                pUpdateGU_g.Value = (long)guild;
                foreach (var userid in userlist)
                {
                    pUpdateGU_u.Value = (long)userid;
                    await cUpdateGuildUser.ExecuteNonQueryAsync();
                }
            }

            // Delete all old values - expects referencing tables to have 'on delete cascade'
            using var t = db.BeginTransaction();
            int staleGuilds, staleUsers;
            using (var c = db.CreateCommand())
            {
                // Delete data for guilds not seen in 4 weeks
                c.CommandText = $"delete from {GuildConfiguration.BackingTable} where (now() - interval '28 days') > last_seen";
                staleGuilds = c.ExecuteNonQuery();
            }
            using (var c = db.CreateCommand())
            {
                // Delete data for users not seen in 8 weeks
                c.CommandText = $"delete from {GuildUserConfiguration.BackingTable} where (now() - interval '56 days') > last_seen";
                staleUsers = c.ExecuteNonQuery();
            }
            Log($"Will remove {staleGuilds} guilds, {staleUsers} users.");
            t.Commit();
        }
    }
}
