using BirthdayBot.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace BirthdayBot.BackgroundServices;
/// <summary>
/// Automatically removes database information for guilds that have not been accessed in a long time.
/// </summary>
class DataRetention : BackgroundService {
    const int ProcessInterval = 5400 / ShardBackgroundWorker.Interval; // Process about once per hour and a half

    // Amount of days without updates before data is considered stale and up for deletion.
    const int StaleGuildThreshold = 180;
    const int StaleUserThreashold = 360;

    public DataRetention(ShardInstance instance) : base(instance) { }

    public override async Task OnTick(int tickCount, CancellationToken token) {
        // On each tick, run only a set group of guilds, each group still processed every ProcessInterval ticks.
        if ((tickCount + ShardInstance.ShardId) % ProcessInterval != 0) return;

        try {
            await DbConcurrentOperationsLock.WaitAsync(token);
            await RemoveStaleEntriesAsync();
        } finally {
            try {
                DbConcurrentOperationsLock.Release();
            } catch (ObjectDisposedException) { }
        }
    }

    private async Task RemoveStaleEntriesAsync() {
        using var db = new BotDatabaseContext();
        var now = DateTimeOffset.UtcNow;

        // Update guilds
        var localGuilds = ShardInstance.DiscordClient.Guilds.Select(g => g.Id).ToList();
        var updatedGuilds = await db.GuildConfigurations
            .Where(g => localGuilds.Contains(g.GuildId))
            .ExecuteUpdateAsync(upd => upd.SetProperty(p => p.LastSeen, now));

        // Update guild users
        var updatedUsers = 0;
        foreach (var guild in ShardInstance.DiscordClient.Guilds) {
            var localUsers = guild.Users.Select(u => u.Id).ToList();
            updatedUsers += await db.UserEntries
                .Where(gu => gu.GuildId == guild.Id)
                .Where(gu => localUsers.Contains(gu.UserId))
                .ExecuteUpdateAsync(upd => upd.SetProperty(p => p.LastSeen, now));
        }

        // And let go of old data
        var staleGuildCount = await db.GuildConfigurations
            .Where(g => now - TimeSpan.FromDays(StaleGuildThreshold) > g.LastSeen)
            .ExecuteDeleteAsync();
        var staleUserCount = await db.UserEntries
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
