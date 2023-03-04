using BirthdayBot.Data;
using Microsoft.EntityFrameworkCore;

namespace BirthdayBot.BackgroundServices;
/// <summary>
/// Proactively fills the user cache for guilds in which any birthday data already exists.
/// </summary>
class AutoUserDownload : BackgroundService {
    public AutoUserDownload(ShardInstance instance) : base(instance) { }

    private static readonly HashSet<ulong> _failedDownloads = new();

    public override async Task OnTick(int tickCount, CancellationToken token) {
        // Take action if a guild's cache is incomplete...
        var incompleteCaches = ShardInstance.DiscordClient.Guilds.Where(g => !g.HasAllMembers).Select(g => g.Id).ToHashSet();
        // ...and if the guild contains any user data
        IEnumerable<ulong> mustFetch;
        try {
            await DbConcurrentOperationsLock.WaitAsync(token);
            using var db = new BotDatabaseContext();
            lock (_failedDownloads)
                mustFetch = db.UserEntries.AsNoTracking()
                    .Where(e => incompleteCaches.Contains(e.GuildId))
                    .Select(e => e.GuildId)
                    .Distinct()
                    .Where(e => !_failedDownloads.Contains(e))
                    .ToList();
        } finally {
            try {
                DbConcurrentOperationsLock.Release();
            } catch (ObjectDisposedException) { }
        }
        
        var processed = 0;
        var processStartTime = DateTimeOffset.UtcNow;
        foreach (var item in mustFetch) {
            // May cause a disconnect in certain situations. Cancel all further attempts until the next pass if it happens.
            if (ShardInstance.DiscordClient.ConnectionState != ConnectionState.Connected) break;

            var guild = ShardInstance.DiscordClient.GetGuild(item);
            if (guild == null) continue; // A guild disappeared...?

            const int singleTimeout = 500;
            var dl = guild.DownloadUsersAsync(); // We're already on a seperate thread, no need to use Task.Run (?)
            dl.Wait(singleTimeout * 1000, token);
            if (!dl.IsCompletedSuccessfully) {
                Log($"Giving up on {guild.Id} after {singleTimeout} seconds. (Total members: {guild.MemberCount})");
                lock (_failedDownloads) _failedDownloads.Add(guild.Id);
            }
            processed++;

            if (Math.Abs((DateTimeOffset.UtcNow - processStartTime).TotalMinutes) > 2) {
                Log("Break time!");
                // take a break (don't get killed by ShardManager for taking too long due to quantity)
                break;
            }
        }

        if (processed > 20) Log($"Explicit user list request processed for {processed} guild(s).");
    }
}
