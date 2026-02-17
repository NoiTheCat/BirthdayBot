using BirthdayBot.Data;
using Microsoft.Extensions.DependencyInjection;
using NoiPublicBot.BackgroundServices;

namespace BirthdayBot.BackgroundServices;

// Replaces the old AutoUserDownload, working very closely with the cache coordinator class
// to maintain the cache ready for any imminent birthdays to be processed.
public sealed class CachePreloader : BackgroundService {
    private static readonly SemaphoreSlim _concurrentBackgroundRefresh = new(1);

    public override async Task OnTick(int tickCount, CancellationToken token) {
        var db = BotDatabaseContext.New();
        var cache = Shard.LocalServices.GetRequiredService<LocalCache>();
        await _concurrentBackgroundRefresh.WaitAsync(token);
        try {
            await cache.BackgroundRefreshWholeShardAsync(db, cache.FilterBackground(), token);
        } finally {
            _concurrentBackgroundRefresh.Release();
        }
    }
}
