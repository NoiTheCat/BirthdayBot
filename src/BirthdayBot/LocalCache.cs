using NoiPublicBot;
using WorldTime.Data;

namespace WorldTime;

public class LocalCache(ShardInstance shard) : NoiPublicBot.Cache.UserCache<BotDatabaseContext>(shard) {
    protected override List<ulong> GetCacheMissingUsers(BotDatabaseContext context, ulong guildId) {
        var local = GetEntriesForGuild(guildId, true)
            .Select(e => e.UserId)
            .ToList();
        var remote = context.UserEntries
            .Where(e => e.GuildId == guildId)
            .Select(e => e.UserId)
            .ToList();
        return [.. remote.Except(local)];
    }
}
