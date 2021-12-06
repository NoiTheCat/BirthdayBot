using BirthdayBot.Data;
using NpgsqlTypes;
using System.Text;

namespace BirthdayBot.BackgroundServices;

/// <summary>
/// Automatically removes database information for guilds that have not been accessed in a long time.
/// </summary>
class DataRetention : BackgroundService {
    private static readonly SemaphoreSlim _updateLock = new(ShardManager.MaxConcurrentOperations);
    const int ProcessInterval = 5400 / ShardBackgroundWorker.Interval; // Process about once per hour and a half
    // Amount of days without updates before data is considered stale and up for deletion.
    const int StaleGuildThreshold = 180;
    const int StaleUserThreashold = 360;

    public DataRetention(ShardInstance instance) : base(instance) { }

    public override async Task OnTick(int tickCount, CancellationToken token) {
        // On each tick, run only a set group of guilds, each group still processed every ProcessInterval ticks.
        if ((tickCount + ShardInstance.ShardId) % ProcessInterval != 0) return;

        try {
            // A semaphore is used to restrict this work being done concurrently on other shards
            // to avoid putting pressure on the SQL connection pool. Clearing old database information
            // ultimately is a low priority among other tasks.
            await _updateLock.WaitAsync(token).ConfigureAwait(false);
        } catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) {
            // Caller does not expect the exception that SemaphoreSlim throws...
            throw new TaskCanceledException();
        }
        try {
            // Build a list of all values across all guilds to update
            var updateList = new Dictionary<ulong, List<ulong>>();
            foreach (var g in ShardInstance.DiscordClient.Guilds) {
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
            using (var tUpdate = db.BeginTransaction()) {
                foreach (var item in updateList) {
                    var guild = item.Key;
                    var userlist = item.Value;

                    pUpdateG.Value = (long)guild;
                    updatedGuilds += await cUpdateGuild.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);

                    pUpdateGU_g.Value = (long)guild;
                    foreach (var userid in userlist) {
                        pUpdateGU_u.Value = (long)userid;
                        updatedUsers += await cUpdateGuildUser.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                await tUpdate.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            var resultText = new StringBuilder();
            resultText.Append($"Updated {updatedGuilds} guilds, {updatedUsers} users.");

            // Deletes both guild and user data if it hasn't been seen for over the threshold defined at the top of this file
            // Expects referencing tables to have 'on delete cascade'
            int staleGuilds, staleUsers;
            using (var tRemove = db.BeginTransaction()) {
                using (var c = db.CreateCommand()) {
                    c.CommandText = $"delete from {GuildConfiguration.BackingTable}" +
                        $" where (now() - interval '{StaleGuildThreshold} days') > last_seen";
                    staleGuilds = await c.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                }
                using (var c = db.CreateCommand()) {
                    c.CommandText = $"delete from {GuildUserConfiguration.BackingTable}" +
                        $" where (now() - interval '{StaleUserThreashold} days') > last_seen";
                    staleUsers = await c.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                }
                await tRemove.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            if (staleGuilds != 0 || staleUsers != 0) {
                resultText.Append(" Discarded ");
                if (staleGuilds != 0) {
                    resultText.Append($"{staleGuilds} guilds");
                    if (staleUsers != 0) resultText.Append(", ");
                }
                if (staleUsers != 0) {
                    resultText.Append($"{staleUsers} users");
                }
                resultText.Append('.');
            }
            Log(resultText.ToString());
        } finally {
            _updateLock.Release();
        }
    }
}
