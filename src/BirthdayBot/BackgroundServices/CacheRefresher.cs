using Microsoft.EntityFrameworkCore;
using NoiPublicBot.BackgroundServices;
using WorldTime.Data;

namespace WorldTime.BackgroundServices;

// Replaces the old AutoUserDownload, working very closely with the cache coordinator class
// to gradually keep the user cache filled and refreshed in the background.
public sealed class CacheRefresher : BgCacheRefresherBase<LocalCache, BotDatabaseContext> {
    protected override Dictionary<ulong, List<ulong>> GetWholeShardDownloadList(LocalCache cache) {
        using var db = BotDatabaseContext.New();

        var guilds = Shard.DiscordClient.Guilds.Select(g => g.Id);

        var dbUsers = db.UserEntries.AsNoTracking()
            .Where(u => guilds.Contains(u.GuildId))
            .Select(v => new { v.GuildId, v.UserId })
            .GroupBy(g => g.GuildId)
            .ToDictionary(k => k.Key, v => v.Select(g => g.UserId).ToList());

        var result = new Dictionary<ulong, List<ulong>>();
        foreach (var (guild, remoteEntries) in dbUsers) {
            // Including null entries in this fetch; backing off on retrying missing entries until they expire
            var localEntries = cache.GetEntriesForGuild(guild, true).Select(e => e.UserId);
            result[guild] = [.. remoteEntries.Except(localEntries)];
        }
        return result;
    }
}
