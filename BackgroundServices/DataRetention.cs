using BirthdayBot.Data;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BirthdayBot.BackgroundServices
{
    /// <summary>
    /// Data retention adherence service:
    /// Automatically removes database information for guilds that have not been accessed in a long time.
    /// </summary>
    class DataRetention : BackgroundService
    {
        private static readonly SemaphoreSlim _updateLock = new SemaphoreSlim(ShardManager.MaxConcurrentOperations);
        const int ProcessInterval = 3600 / ShardBackgroundWorker.Interval; // Process about once per hour
        private int _tickCount = -1;

        public DataRetention(ShardInstance instance) : base(instance) { }

        public override async Task OnTick(CancellationToken token)
        {
            if ((++_tickCount + ShardInstance.ShardId * 3) % ProcessInterval != 0)
            {
                // Do not process on every tick.
                // Stagger processing based on shard ID, to not choke the background processing task.
                return;
            }

            try
            {
                // A semaphore is used to restrict this work being done concurrently on other shards
                // to avoid putting pressure on the SQL connection pool. Updating this is a low priority.
                await _updateLock.WaitAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
            {
                // Calling thread does not expect the exception that SemaphoreSlim throws...
                throw new TaskCanceledException();
            }
            try
            {
                // Build a list of all values to update
                var updateList = new Dictionary<ulong, List<ulong>>();
                foreach (var g in ShardInstance.DiscordClient.Guilds)
                {
                    // Get list of IDs for all users who exist in the database and currently exist in the guild
                    var userList = GuildUserConfiguration.LoadAllAsync(g.Id);
                    var guildUserIds = from gu in g.Users select gu.Id;
                    var savedUserIds = from cu in await userList.ConfigureAwait(false) select cu.UserId;
                    var existingCachedIds = savedUserIds.Intersect(guildUserIds);
                    updateList[g.Id] = existingCachedIds.ToList();
                }

                using var db = await Database.OpenConnectionAsync().ConfigureAwait(false);

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
                int updatedGuilds = 0;
                int updatedUsers = 0;
                foreach (var item in updateList)
                {
                    var guild = item.Key;
                    var userlist = item.Value;

                    pUpdateG.Value = (long)guild;
                    updatedGuilds += await cUpdateGuild.ExecuteNonQueryAsync().ConfigureAwait(false);

                    pUpdateGU_g.Value = (long)guild;
                    foreach (var userid in userlist)
                    {
                        pUpdateGU_u.Value = (long)userid;
                        updatedUsers += await cUpdateGuildUser.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }
                var resultText = new StringBuilder();
                resultText.Append($"Updated {updatedGuilds} guilds, {updatedUsers} users.");

                // Delete all old values - expects referencing tables to have 'on delete cascade'
                using var t = db.BeginTransaction();
                int staleGuilds, staleUsers;
                using (var c = db.CreateCommand())
                {
                    // Delete data for guilds not seen in 4 weeks
                    c.CommandText = $"delete from {GuildConfiguration.BackingTable} where (now() - interval '28 days') > last_seen";
                    staleGuilds = await c.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                using (var c = db.CreateCommand())
                {
                    // Delete data for users not seen in 8 weeks
                    c.CommandText = $"delete from {GuildUserConfiguration.BackingTable} where (now() - interval '56 days') > last_seen";
                    staleUsers = await c.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                if (staleGuilds != 0 || staleUsers != 0)
                {
                    resultText.Append(" Discarded ");
                    if (staleGuilds != 0)
                    {
                        resultText.Append($"{staleGuilds} guilds");
                        if (staleUsers != 0) resultText.Append(", ");
                    }
                    if (staleUsers != 0) 
                    {
                        resultText.Append($"{staleUsers} standalone users");
                    }
                    resultText.Append('.');
                }
                t.Commit();
                Log(resultText.ToString());
            }
            finally
            {
                _updateLock.Release();
            }
        }
    }
}
