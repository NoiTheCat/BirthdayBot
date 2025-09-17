using BirthdayBot.Data;
using Microsoft.EntityFrameworkCore;

namespace BirthdayBot.BackgroundServices;
/// <summary>
/// Proactively fills the user cache for guilds in which any birthday data already exists.
/// </summary>
class AutoUserDownload : BackgroundService {
    private static readonly TimeSpan RequestTimeout = ShardManager.DeadShardThreshold / 3;

    private readonly HashSet<ulong> _skippedGuilds = [];

    public AutoUserDownload(ShardInstance instance) : base(instance)
        => Shard.DiscordClient.Disconnected += OnDisconnect;
    ~AutoUserDownload() => Shard.DiscordClient.Disconnected -= OnDisconnect;

    private Task OnDisconnect(Exception ex) {
        _skippedGuilds.Clear();
        return Task.CompletedTask;
    }

    public override async Task OnTick(int tickCount, CancellationToken token) {
        // Take action if a guild's cache is incomplete...
        var incompleteCaches = Shard.DiscordClient.Guilds
            .Where(g => !g.HasAllMembers)
            .Select(g => g.Id)
            .ToHashSet();
        // ...and if the guild contains any user data
        HashSet<ulong> mustFetch;
        try {
            await ConcurrentSemaphore.WaitAsync(token);
            using var db = new BotDatabaseContext();
            mustFetch = [.. db.UserEntries.AsNoTracking()
                .Where(e => incompleteCaches.Contains(e.GuildId))
                .Select(e => e.GuildId)
                .Where(e => !_skippedGuilds.Contains(e))];
        } finally {
            try {
                ConcurrentSemaphore.Release();
            } catch (ObjectDisposedException) { }
        }

        var processed = 0;
        var processStartTime = DateTimeOffset.UtcNow;
        foreach (var item in mustFetch) {
            // Take break from processing to avoid getting killed by ShardManager
            if (DateTimeOffset.UtcNow - processStartTime > RequestTimeout) break;

            // We're useless if not connected
            if (Shard.DiscordClient.ConnectionState != ConnectionState.Connected) break;

            var guild = Shard.DiscordClient.GetGuild(item);
            if (guild == null) continue; // A guild disappeared...?

            processed++;

            await Task.Delay(200, CancellationToken.None); // Delay a bit (reduces the possibility of hanging, somehow).
            var dl = guild.DownloadUsersAsync();
            try {
                dl.Wait((int)RequestTimeout.TotalMilliseconds / 2, token);
            } catch (Exception) { }
            if (token.IsCancellationRequested) return; // Skip all reporting, error logging on cancellation

            if (dl.IsFaulted) {
                Log("Exception thrown by download task: " + dl.Exception);
                break;
            } else if (!dl.IsCompletedSuccessfully) {
                Log($"Task unresponsive, will skip (ID {guild.Id}, with {guild.MemberCount} members).");
                _skippedGuilds.Add(guild.Id);
                continue;
            }
        }

        if (processed > 10) Log($"Member list downloads handled for {processed} guilds.");
    }
}
