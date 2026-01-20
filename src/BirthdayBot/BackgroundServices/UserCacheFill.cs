using Microsoft.EntityFrameworkCore;
using WorldTime.Data;

namespace WorldTime.BackgroundServices;

// Replaces the old AutoUserDownload, working very closely with the cache coordinator class
// to gradually keep the user cache filled and refreshed in the background.
class UserCacheFill(ShardInstance instance) : BackgroundService(instance) {
    // The entire background cache fill process attempts to not compete with any potential manual
    // requests for user data in all sorts of ways. Here, the number of background fetch tasks
    // run in parallel across shards is limited.
    private static readonly SemaphoreSlim _concurrentBackgroundRefreshTasks = new(1);

    public override async Task OnTick(int tickCount, CancellationToken token) {
        // Fetching a handful of randomly selected guilds at a time so that this completes and other tasks can work.
        // Otherwise, this method can end up holding up other background steps for 5+ hours.
        var missingFromCache = BuildShardDownloadList().Shuffle().Take(5).ToDictionary();
        await Shard.Fetcher.BackgroundRefreshShardTask(missingFromCache, _concurrentBackgroundRefreshTasks, token);
        Shard.Cache.Sweep();
    }

    private Dictionary<ulong, List<ulong>> BuildShardDownloadList() {
        var opts = new DbContextOptionsBuilder<BotDatabaseContext>();
        ShardManager.BuildSqlOptions(opts);
        using var db = new BotDatabaseContext(opts.Options);

        var guilds = Shard.DiscordClient.Guilds.Select(g => g.Id);

        var dbUsers = db.UserEntries.AsNoTracking()
            .Where(u => guilds.Contains(u.GuildId))
            .Select(v => new { v.GuildId, v.UserId })
            .GroupBy(g => g.GuildId)
            .ToDictionary(k => k.Key, v => v.Select(g => g.UserId).ToList());

        var result = new Dictionary<ulong, List<ulong>>();
        foreach (var (guild, remoteEntries) in dbUsers) {
            // Including null entries; backing off on retrying missing entries until they expire
            var localEntries = Shard.Cache.GetEntriesForGuild(guild, true).Select(e => e.UserId);
            result[guild] = [.. remoteEntries.Except(localEntries)];
        }
        return result;
    }
}
