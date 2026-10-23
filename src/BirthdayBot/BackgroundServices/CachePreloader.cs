using BirthdayBot.Data;
using Microsoft.Extensions.DependencyInjection;
using NoiPublicBot.BackgroundServices;
using NoiPublicBot.Common.UserCache;

namespace BirthdayBot.BackgroundServices;

// Maintains the cache ready for any imminent birthdays to be processed
public sealed class CachePreloader : BackgroundService {
    public override async Task OnTick(int tickCount, CancellationToken token) {
        var db = BotDatabaseContext.New();
        var cache = Shard.LocalServices.GetRequiredService<UserCache<BotDatabaseContext>>();
        await cache.BackgroundRefreshWholeShardAsync(db, CacheFilters.PrepareBackground(), token).ConfigureAwait(false);
    }
}
