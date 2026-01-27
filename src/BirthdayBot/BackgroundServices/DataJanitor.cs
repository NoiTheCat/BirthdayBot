using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoiPublicBot;
using NoiPublicBot.BackgroundServices;
using WorldTime.Data;

namespace WorldTime.BackgroundServices;

// Keeps track of known existing users. Removes old unused data
class DataJanitor : BackgroundService {
    private readonly int ProcessInterval;
    private static readonly SemaphoreSlim _dbGate = new(3);

    // Amount of days without updates before data is considered stale and up for deletion.
    const int StaleUserThreashold = 90;

    public DataJanitor()
        => ProcessInterval = 10_800 / Instance.UserConfig.BackgroundInterval; // Process about once every two hours

    public override async Task OnTick(int tickCount, CancellationToken token) {
        if (tickCount % ProcessInterval != 0) return;

        await _dbGate.WaitAsync(token);
        try {
#if DEBUG
            // splitting this out as a separate method this way prevents from accidentally removing a
            // 'using' statement up above for the millionth time...
            await DebugBumpAsync(token);
            #pragma warning disable IDE0051
#else
            await RemoveStaleEntriesAsync(token);
#endif
        } finally {
            try {
                _dbGate.Release();
            } catch (ObjectDisposedException) { }
        }
    }

    private async Task RemoveStaleEntriesAsync(CancellationToken token) {
        using var db = BotDatabaseContext.New();

        // Update guild users
        var now = DateTimeOffset.UtcNow;
        var cache = Shard.LocalServices.GetRequiredService<LocalCache>();
        var updatedUsers = 0;
        foreach (var guild in Shard.DiscordClient.Guilds) {
            var local = cache.GetEntriesForGuild(guild.Id, false)
                .Select(e => e.UserId).ToList();

            foreach (var queue in local.Chunk(1000)) {
                updatedUsers += await db.UserEntries
                    .Where(gu => gu.GuildId == guild.Id)
                    .Where(gu => local.Contains(gu.UserId))
                    .ExecuteUpdateAsync(upd => upd.SetProperty(p => p.LastSeen, now), token);
            }
        }
        Log($"Refreshed {updatedUsers} users.");

        // And let go of old data
        var staleUserCount = await db.UserEntries
            .Where(gu => now - TimeSpan.FromDays(StaleUserThreashold) > gu.LastSeen)
            .ExecuteDeleteAsync(token);
        if (staleUserCount != 0) Log($"Discarded {staleUserCount} users across the whole database.");
    }

    private async Task DebugBumpAsync(CancellationToken token) {
        using var db = BotDatabaseContext.New();
        var now = DateTimeOffset.UtcNow;
        await db.UserEntries.ExecuteUpdateAsync(upd => upd.SetProperty(p => p.LastSeen, now), token);
        Log("DEBUG: Extended TTL of existing entries.");
    }
}
