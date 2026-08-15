using BirthdayBot.Data;
using Microsoft.Extensions.DependencyInjection;
using NoiPublicBot.BackgroundServices;
using NoiPublicBot.Common.UserCache;

namespace BirthdayBot.BackgroundServices;

// Maintains the cache ready for any imminent birthdays to be processed
public sealed class CachePreloader : BackgroundService {
    private static readonly SemaphoreSlim _concurrentBackgroundRefresh = new(1);

    public override async Task OnTick(int tickCount, CancellationToken token) {
        var db = BotDatabaseContext.New();
        var cache = Shard.LocalServices.GetRequiredService<UserCache<BotDatabaseContext>>();
        await _concurrentBackgroundRefresh.WaitAsync(token).ConfigureAwait(false);
        try {
            await cache.BackgroundRefreshWholeShardAsync(db, CacheFilters.Background(), token).ConfigureAwait(false);
        } finally {
            _concurrentBackgroundRefresh.Release();
        }
    }
}
