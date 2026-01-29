using BirthdayBot.Data;
using NoiPublicBot;

namespace BirthdayBot;

public class LocalCache(ShardInstance shard) : NoiPublicBot.Cache.UserCache<BotDatabaseContext>(shard) {
    protected override List<ulong> GetCacheMissingUsers(BotDatabaseContext context, ulong guildId) {
        #error not yet fully implemented
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
