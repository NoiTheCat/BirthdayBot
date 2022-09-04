global using Discord;
global using Discord.WebSocket;
using BirthdayBot.BackgroundServices;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

namespace BirthdayBot;
/// <summary>
/// More or less the main class for the program. Handles individual shards and provides frequent
/// status reports regarding the overall health of the application.
/// </summary>
class ShardManager : IDisposable {
    /// <summary>
    /// Number of seconds between each time the status task runs, in seconds.
    /// </summary>
    private const int StatusInterval = 90;

    /// <summary>
    /// Number of concurrent shard startups to happen on each check.
    /// This value is also used in <see cref="DataRetention"/>.
    /// </summary>
    public const int MaxConcurrentOperations = 4;

    /// <summary>
    /// Amount of time without a completed background service run before a shard instance
    /// is considered "dead" and tasked to be removed.
    /// </summary>
    private static readonly TimeSpan DeadShardThreshold = new(0, 20, 0);

    /// <summary>
    /// A dictionary with shard IDs as its keys and shard instances as its values.
    /// When initialized, all keys will be created as configured. If an instance is removed,
    /// a key's corresponding value will temporarily become null instead of the key/value
    /// pair being removed.
    /// </summary>
    private readonly Dictionary<int, ShardInstance?> _shards;

    private readonly Task _statusTask;
    private readonly CancellationTokenSource _mainCancel;

    internal Configuration Config { get; }

    public ShardManager(Configuration cfg) {
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Log($"Birthday Bot v{ver!.ToString(3)} is starting...");

        Config = cfg;

        // Allocate shards based on configuration
        _shards = new Dictionary<int, ShardInstance?>();
        for (var i = Config.ShardStart; i < (Config.ShardStart + Config.ShardAmount); i++) {
            _shards.Add(i, null);
        }

        // Start status reporting thread
        _mainCancel = new CancellationTokenSource();
        _statusTask = Task.Factory.StartNew(StatusLoop, _mainCancel.Token,
                                            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void Dispose() {
        _mainCancel.Cancel();
        _statusTask.Wait(10000);
        if (!_statusTask.IsCompleted)
            Log("Warning: Main thread did not cleanly finish up in time. Continuing...");

        Log("Shutting down all shards...");
        var shardDisposes = new List<Task>();
        foreach (var item in _shards) {
            if (item.Value == null) continue;
            shardDisposes.Add(Task.Run(item.Value.Dispose));
        }
        if (!Task.WhenAll(shardDisposes).Wait(30000)) {
            Log("Warning: Not all shards terminated cleanly after 30 seconds. Continuing...");
        }

        Log($"Uptime: {Program.BotUptime}");
    }

    private void Log(string message) => Program.Log(nameof(ShardManager), message);

    /// <summary>
    /// Creates and sets up a new shard instance.
    /// </summary>
    private async Task<ShardInstance> InitializeShard(int shardId) {
        var clientConf = new DiscordSocketConfig() {
            ShardId = shardId,
            TotalShards = Config.ShardTotal,
            LogLevel = LogSeverity.Info,
            DefaultRetryMode = RetryMode.Retry502 | RetryMode.RetryTimeouts,
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMembers,
            SuppressUnknownDispatchWarnings = true,
            LogGatewayIntentWarnings = false
        };
        var services = new ServiceCollection()
            .AddSingleton(s => new ShardInstance(this, s))
            .AddSingleton(s => new DiscordSocketClient(clientConf))
            .AddSingleton(s => new InteractionService(s.GetRequiredService<DiscordSocketClient>()))
            .BuildServiceProvider();
        var newInstance = services.GetRequiredService<ShardInstance>();
        await newInstance.StartAsync();

        return newInstance;
    }

    public int? GetShardIdFor(ulong guildId) {
        foreach (var sh in _shards.Values) {
            if (sh == null) continue;
            if (sh.DiscordClient.GetGuild(guildId) != null) return sh.ShardId;
        }
        return null;
    }

    private async Task StatusLoop() {
        try {
            while (!_mainCancel.IsCancellationRequested) {
                Log($"Uptime: {Program.BotUptime}");

                // Iterate through shards, create report on each
                var shardStatuses = new StringBuilder();
                var nullShards = new List<int>();
                var deadShards = new List<int>();
                for (var i = 0; i < _shards.Count; i++) {
                    shardStatuses.Append($"Shard {i:00}: ");

                    if (_shards[i] == null) {
                        shardStatuses.AppendLine("Inactive.");
                        nullShards.Add(i);
                        continue;
                    }

                    var shard = _shards[i]!;
                    var client = shard.DiscordClient;
                    shardStatuses.Append($"{Enum.GetName(typeof(ConnectionState), client.ConnectionState)} ({client.Latency:000}ms).");
                    shardStatuses.Append($" Guilds: {client.Guilds.Count}.");
                    shardStatuses.Append($" Background: {shard.CurrentExecutingService ?? "Idle"}");
                    var lastRun = DateTimeOffset.UtcNow - shard.LastBackgroundRun;
                    if (lastRun > DeadShardThreshold / 3) {
                        // Formerly known as a 'slow' shard
                        shardStatuses.Append($", heartbeat {Math.Floor(lastRun.TotalMinutes):00}m ago.");
                    } else {
                        shardStatuses.Append('.');
                    }
                    
                    shardStatuses.AppendLine();

                    if (lastRun > DeadShardThreshold) {
                        shardStatuses.AppendLine($"Shard {i:00} marked for disposal.");
                        deadShards.Add(i);
                    }
                }
                Log(shardStatuses.ToString().TrimEnd());

                // Remove dead shards
                foreach (var dead in deadShards) {
                    _shards[dead]!.Dispose();
                    _shards[dead] = null;
                }

                // Start null shards, a few at at time
                var startAllowance = MaxConcurrentOperations;
                foreach (var id in nullShards) {
                    if (startAllowance-- > 0) {
                        _shards[id] = await InitializeShard(id);
                    } else break;
                }

                await Task.Delay(StatusInterval * 1000, _mainCancel.Token);
            }
        } catch (TaskCanceledException) { }
    }
}
