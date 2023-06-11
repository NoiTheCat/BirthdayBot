using BirthdayBot.Data;
using Microsoft.EntityFrameworkCore;

namespace BirthdayBot.BackgroundServices;
/// <summary>
/// Proactively fills the user cache for guilds in which any birthday data already exists.
/// </summary>
class AutoUserDownload : BackgroundService {
    public AutoUserDownload(ShardInstance instance) : base(instance) { }

    private static readonly HashSet<ulong> _failedDownloads = new();
    private static readonly TimeSpan _singleDlTimeout = ShardManager.DeadShardThreshold / 3;

    public override async Task OnTick(int tickCount, CancellationToken token) {
        // Take action if a guild's cache is incomplete...
        var incompleteCaches = Shard.DiscordClient.Guilds
            .Where(g => !g.HasAllMembers)
            .Select(g => g.Id)
            .ToHashSet();
        // ...and if the guild contains any user data
        IEnumerable<ulong> mustFetch;
        try {
            await ConcurrentSemaphore.WaitAsync(token);
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
                ConcurrentSemaphore.Release();
            } catch (ObjectDisposedException) { }
        }
        
        var processed = 0;
        var processStartTime = DateTimeOffset.UtcNow;
        foreach (var item in mustFetch) {
            // May cause a disconnect in certain situations. Make no further attempts until the next pass if it happens.
            if (Shard.DiscordClient.ConnectionState != ConnectionState.Connected) break;

            var guild = Shard.DiscordClient.GetGuild(item);
            if (guild == null) continue; // A guild disappeared...?

            await Task.Delay(200, CancellationToken.None); // Delay a bit (reduces the possibility of hanging, somehow).
            processed++;
            var dl = guild.DownloadUsersAsync();
            dl.Wait((int)_singleDlTimeout.TotalMilliseconds / 2, token);
            if (dl.IsFaulted) {
                Log("Exception thrown by download task: " + dl.Exception);
                break;
            } else if (!dl.IsCompletedSuccessfully) {
                Log($"Task for guild {guild.Id} is unresponsive. Skipping guild. Members: {guild.MemberCount}. Name: {guild.Name}.");
                lock (_failedDownloads) _failedDownloads.Add(guild.Id);
                continue;
            }

            // Prevent unnecessary disconnections by ShardManager if we're taking too long
            if (DateTimeOffset.UtcNow - processStartTime > _singleDlTimeout) break;
        }

        if (processed > 10) Log($"Member list downloads handled for {processed} guilds.");
    }
}
