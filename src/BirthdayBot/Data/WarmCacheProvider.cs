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
        return Task.FromResult(result);
    }

    public async Task UpdateAsync(ulong guildId, ulong userId, Instant expiresAt, string json) {
        using var db = BotDatabaseContext.New();
        var entry = db.WarmCache
            .Where(x => x.GuildId == guildId && x.UserId == userId)
            .SingleOrDefault();
        if (entry is null) {
            entry = new WarmCacheItem {
                GuildId = guildId,
                UserId = userId,
                ExpiresAt = expiresAt,
                Data = json
            };
            db.WarmCache.Add(entry);
        } else {
            entry.ExpiresAt = expiresAt;
            entry.Data = json;
        }
        await db.SaveChangesAsync();

    }
    
    public async Task RemoveGuildAsync(ulong guildId) {
        using var db = BotDatabaseContext.New();
        await db.WarmCache
            .Where(x => x.GuildId == guildId)
            .ExecuteDeleteAsync();
    }

    public async Task RemoveUserAsync(ulong guildId, ulong userId) {
        using var db = BotDatabaseContext.New();
        await db.WarmCache
            .Where(x => x.GuildId == guildId && x.UserId == userId)
            .ExecuteDeleteAsync();
    }
}
