using System.Collections.Concurrent;
using System.Net.Sockets;
using Discord.Net;
using Microsoft.EntityFrameworkCore;
using WorldTime.Data;

namespace WorldTime.Caching;

// Handles requests for refreshing guild caches. To avoid duplicate work, ongoing jobs are tracked here.
// If any duplicate requests arrive, they are given the appropriate ongoing fetch task.
class Coordinator(ShardInstance parent) {
    // Discord limits to 50 requests per second per connection for all communications, not just this.
    // Tune as needed. This value always stays hardcoded.
    const int MaxConcurrentRequests = 25;

    // Time to delay sending out a request, in milliseconds.
    // Consider batch and concurrent limits when adjusting.
    const int JitterMin = 250;
    const int JitterMax = 2000;
    const int RequestBatchSize = 20;

    private static readonly SemaphoreSlim _downloadGate = new(MaxConcurrentRequests);

    // Dictionary of guild ID -> lazy task of RefreshInternal
    private readonly ConcurrentDictionary<ulong, Lazy<Task>> _runners = new();

    private ShardInstance Shard { get; } = parent;

    public Task RequestGuildRefreshAsync(BotDatabaseContext ctx, ulong guildId) {
        var missing = GetCacheMissingUsers(ctx, guildId);
        if (missing.Count == 0) return Task.CompletedTask;
        return _runners.GetOrAdd(guildId, new Lazy<Task>(() =>
            RefreshInternalAsync(guildId, missing, CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    // Guild-specific search. Returns all IDs in database not also in current cache.
    private List<ulong> GetCacheMissingUsers(BotDatabaseContext context, ulong guildId) {
        var local = Shard.Cache.GetEntriesForGuild(guildId, true)
            .Select(e => e.UserId)
            .ToList();
        var remote = context.UserEntries
            .Where(e => e.GuildId == guildId)
            .Select(e => e.UserId)
            .ToList();
        return [.. remote.Except(local)];
    }

    // Directly called by background task. Not at all useful to anyone else.
    public async Task BackgroundRefreshShardTask(
        Dictionary<ulong, List<ulong>> missing, SemaphoreSlim concurrent, CancellationToken token) {
        var enqueued = _runners.Keys.ToHashSet();

        // Set up and monitor one job at a time. Setting up all fetch tasks at once chokes manual requests.
        // We may instead get an ongoing task. This is fine - we wait on it, same as if it originated here.
        // The goal is responsiveness for requests originating by direct user action.
        foreach (var (guildId, users) in missing) {
            await concurrent.WaitAsync(token);
            try {
                if (Shard.DiscordClient.GetGuild(guildId) is null) continue; // situation may have changed
                if (users.Count == 0) continue; // uncommon, but it does happen

                var newtask = _runners.GetOrAdd(guildId,
                    new Lazy<Task>(() => RefreshInternalAsync(guildId, users, token),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
                await newtask.ConfigureAwait(false);
            } finally {
                concurrent.Release();
            }
            await Task.Yield();
        }
    }

    // Takes a guild/user list and runs it in batches until done or cancelled.
    // This returns the task for all guild requests to be awaited on.
    private async Task RefreshInternalAsync(ulong guildId, IEnumerable<ulong> users, CancellationToken token) {
        try {
            foreach (var chunk in users.Chunk(RequestBatchSize)) {
                await Task.Yield();
                if (token.IsCancellationRequested) return;
                if (Shard.DiscordClient.ConnectionState != ConnectionState.Connected) break;
                await RetrieveGuildUserBatchAsync(guildId, chunk, token).ConfigureAwait(false);
            }
        } finally {
            _runners.TryRemove(guildId, out _);
        }
    }

    // Assumes caller has already organized users in batches
    private Task RetrieveGuildUserBatchAsync(ulong g, IReadOnlyList<ulong> users, CancellationToken token) {
        var tasks = users.Select(async u => {
            await _downloadGate.WaitAsync(token).ConfigureAwait(false);
            try {
                await Task.Delay(Program.JitterSource.Value!.Next(JitterMin, JitterMax)).ConfigureAwait(false);

                var incoming = await Shard.DiscordClient.Rest.GetGuildUserAsync(
                    g, u, new RequestOptions { CancelToken = token }).ConfigureAwait(false);
                if (incoming is not null) {
                    Shard.Cache.Update(UserInfo.CreateFrom(incoming));
                } else {
                    Shard.Cache.Update(UserInfo.NullFrom(g, u));
                }
            } catch (HttpException ex) when ((int)ex.HttpCode >= 500) {
                // Discord-side transient failure
            } catch (TaskCanceledException) {
                // Timeout or cancellation
            } catch (HttpRequestException) {
                // DNS, TLS, connection reset, etc
            } catch (IOException ex) when (ex.InnerException is SocketException) {
                // Broken pipe, connection reset
            } catch (SocketException) {
                // Low-level network failure
            } finally {
                // Other exception types are extraordinary in this context and will propagate
                _downloadGate.Release();
            }
        });
        return Task.WhenAll(tasks);
    }
}
