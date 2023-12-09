using BirthdayBot.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace BirthdayBot.BackgroundServices;
/// <summary>
/// Automatically removes database information for guilds that have not been accessed in a long time.
/// </summary>
class DataRetention : BackgroundService {
    private readonly int ProcessInterval;

    // Amount of days without updates before data is considered stale and up for deletion.
    const int StaleGuildThreshold = 180;
    const int StaleUserThreashold = 360;

    public DataRetention(ShardInstance instance) : base(instance) {
        ProcessInterval = 21600 / Shard.Config.BackgroundInterval; // Process about once per six hours
    }

    public override async Task OnTick(int tickCount, CancellationToken token) {
        // Run only a subset of shards each time, each running every ProcessInterval ticks.
        if ((tickCount + Shard.ShardId) % ProcessInterval != 0) return;

        try {
            await ConcurrentSemaphore.WaitAsync(token);
            await RemoveStaleEntriesAsync();
        } finally {
            try {
                ConcurrentSemaphore.Release();
            } catch (ObjectDisposedException) { }
        }
    }

    private async Task RemoveStaleEntriesAsync() {
        using var db = new BotDatabaseContext();
        var now = DateTimeOffset.UtcNow;

        // Update guilds
        var localGuilds = Shard.DiscordClient.Guilds.Select(g => g.Id).ToList();
        var updatedGuilds = await db.GuildConfigurations
            .Where(g => localGuilds.Contains(g.GuildId))
            .ExecuteUpdateAsync(upd => upd.SetProperty(p => p.LastSeen, now));

        // Update guild users
        var updatedUsers = 0;
        foreach (var guild in Shard.DiscordClient.Guilds) {
            var localUsers = guild.Users.Select(u => u.Id).ToList();
            updatedUsers += await db.UserEntries
                .Where(gu => gu.GuildId == guild.Id)
                .Where(gu => localUsers.Contains(gu.UserId))
                .ExecuteUpdateAsync(upd => upd.SetProperty(p => p.LastSeen, now));
        }

        // And let go of old data
        var staleGuildCount = await db.GuildConfigurations
            .Where(g => localGuilds.Contains(g.GuildId))
            .Where(g => now - TimeSpan.FromDays(StaleGuildThreshold) > g.LastSeen)
            .ExecuteDeleteAsync();
        var staleUserCount = await db.UserEntries
            .Where(gu => localGuilds.Contains(gu.GuildId))
            .Where(gu => now - TimeSpan.FromDays(StaleUserThreashold) > gu.LastSeen)
            .ExecuteDeleteAsync();
            
        // Build report
        var resultText = new StringBuilder();
        resultText.Append($"Updated {updatedGuilds} guilds, {updatedUsers} users.");
        if (staleGuildCount != 0 || staleUserCount != 0) {
            resultText.Append(" Discarded ");
            if (staleGuildCount != 0) {
                resultText.Append($"{staleGuildCount} guilds");
                if (staleUserCount != 0) resultText.Append(", ");
            }
            if (staleUserCount != 0) {
                resultText.Append($"{staleUserCount} users");
            }
            resultText.Append('.');
        }
        Log(resultText.ToString());
    }
}
