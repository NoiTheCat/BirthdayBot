using BirthdayBot.Data;
using System.Text;

namespace BirthdayBot.BackgroundServices;

/// <summary>
/// Automatically removes database information for guilds that have not been accessed in a long time.
/// </summary>
class DataRetention : BackgroundService {
    private static readonly SemaphoreSlim _updateLock = new(ShardManager.MaxConcurrentOperations);
    const int ProcessInterval = 5400 / ShardBackgroundWorker.Interval; // Process about once per hour and a half
    // Amount of days without updates before data is considered stale and up for deletion.
    const int StaleGuildThreshold = 180;
    const int StaleUserThreashold = 360;

    public DataRetention(ShardInstance instance) : base(instance) { }

    public override async Task OnTick(int tickCount, CancellationToken token) {
        // On each tick, run only a set group of guilds, each group still processed every ProcessInterval ticks.
        if ((tickCount + ShardInstance.ShardId) % ProcessInterval != 0) return;

        using var db = new BotDatabaseContext();
        var now = DateTimeOffset.UtcNow;
        int updatedGuilds = 0, updatedUsers = 0;

        foreach (var guild in ShardInstance.DiscordClient.Guilds) {
            // Update guild, fetch users from database
            var dbGuild = db.GuildConfigurations.Where(s => s.GuildId == (long)guild.Id).FirstOrDefault();
            if (dbGuild == null) continue;
            dbGuild.LastSeen = now;
            updatedGuilds++;

            // Update users
            var localIds = guild.Users.Select(u => (long)u.Id);
            var dbSavedIds = db.UserEntries.Where(e => e.GuildId == (long)guild.Id).Select(e => e.UserId);
            var usersToUpdate = localIds.Intersect(dbSavedIds).ToHashSet();
            foreach (var user in db.UserEntries.Where(e => e.GuildId == (long)guild.Id && usersToUpdate.Contains(e.UserId))) {
                user.LastSeen = now;
                updatedUsers++;
            }
        }

        // And let go of old data
        var staleGuilds = db.GuildConfigurations.Where(s => now - TimeSpan.FromDays(StaleGuildThreshold) > s.LastSeen);
        var staleUsers = db.UserEntries.Where(e => now - TimeSpan.FromDays(StaleUserThreashold) > e.LastSeen);
        int staleGuildCount = staleGuilds.Count(), staleUserCount = staleUsers.Count();
        db.GuildConfigurations.RemoveRange(staleGuilds);
        db.UserEntries.RemoveRange(staleUsers);

        await db.SaveChangesAsync(CancellationToken.None);
            
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
