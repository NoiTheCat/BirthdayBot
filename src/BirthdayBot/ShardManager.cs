global using Discord;
global using Discord.WebSocket;
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
        _shards = [];
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
            LogGatewayIntentWarnings = false,
            FormatUsersInBidirectionalUnicode = false
        };
        var services = new ServiceCollection()
            .AddSingleton(s => new ShardInstance(this, s))
            .AddSingleton(s => new DiscordSocketClient(clientConf))
            .AddSingleton(s => new InteractionService(s.GetRequiredService<DiscordSocketClient>()))
            .BuildServiceProvider();
        var newInstance = services.GetRequiredService<ShardInstance>();
        await newInstance.StartAsync().ConfigureAwait(false);

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
                var startAllowance = Config.ShardStartInterval;

                // Iterate through shards, create report on each
                var shardStatuses = new StringBuilder();
                foreach (var i in _shards.Keys) {
                    shardStatuses.Append($"Shard {i:00}: ");

                    if (_shards[i] == null) {
                        if (startAllowance > 0) {
                            shardStatuses.AppendLine("Started.");
                            _shards[i] = await InitializeShard(i).ConfigureAwait(false);
                            startAllowance--;
                        } else {
                            shardStatuses.AppendLine("Awaiting start.");
                        }
                        continue;
                    }

                    var shard = _shards[i]!;
                    var client = shard.DiscordClient;
                    // TODO look into better connection checking options. ConnectionState is not reliable.
                    shardStatuses.Append($"{Enum.GetName(typeof(ConnectionState), client.ConnectionState)} ({client.Latency:000}ms).");
                    shardStatuses.Append($" G: {client.Guilds.Count:0000},");
                    shardStatuses.Append($" U: {client.Guilds.Sum(s => s.Users.Count):000000},");
                    shardStatuses.Append($" BG: {shard.CurrentExecutingService ?? "Idle"}");
                    var lastRun = DateTimeOffset.UtcNow - shard.LastBackgroundRun;
                    shardStatuses.Append($" since {Math.Floor(lastRun.TotalMinutes):00}m{lastRun.Seconds:00}s ago.");
                    shardStatuses.AppendLine();
                }
                Log(shardStatuses.ToString().TrimEnd());
                Log($"Uptime: {Program.BotUptime}");

                await Task.Delay(Config.StatusInterval * 1000, _mainCancel.Token).ConfigureAwait(false);
            }
        } catch (TaskCanceledException) { }
    }
}
