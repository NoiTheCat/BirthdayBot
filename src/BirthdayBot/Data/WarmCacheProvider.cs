using Microsoft.EntityFrameworkCore;
using NodaTime;
using NoiPublicBot.Common.UserCache;

namespace BirthdayBot.Data;

class WarmCacheProvider : IWarmCacheProvider {
    public Task<string?> GetAsync(ulong guildId, ulong userId) {
        using var db = BotDatabaseContext.New();
        var result = db.WarmCache.AsNoTracking()
            .Where(x => x.GuildId == guildId && x.UserId == userId)
            .Where(x => x.ExpiresAt > SystemClock.Instance.GetCurrentInstant())
            .Select(c => c.Data)
            .SingleOrDefault();
        Console.WriteLine((result is null ? "miss" : "hit") + ": " + userId);
        return Task.FromResult(result);
    }

    public async Task UpdateAsync(ulong guildId, ulong userId, Instant expiresAt, string json) {
        using var db = BotDatabaseContext.New();
        var entry = db.WarmCache
            .Where(x => x.GuildId == guildId && x.UserId == userId)
            .SingleOrDefault();
        if (entry is null) {
            Console.WriteLine("update: add " + userId);
            entry = new WarmCacheItem {
                GuildId = guildId,
                UserId = userId,
                ExpiresAt = expiresAt,
                Data = json
            };
            db.WarmCache.Add(entry);
        } else {
            Console.WriteLine("update: upd " + userId);
            entry.ExpiresAt = expiresAt;
            entry.Data = json;
        }
        await db.SaveChangesAsync();
        
    }

    public async Task RemoveAsync(ulong guildId, ulong userId) {
        Console.WriteLine("remove " + userId);
        using var db = BotDatabaseContext.New();
        await db.WarmCache
            .Where(x => x.GuildId == guildId && x.UserId == userId)
            .ExecuteDeleteAsync();
    }
}
