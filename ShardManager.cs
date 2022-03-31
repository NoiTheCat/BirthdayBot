global using Discord;
global using Discord.WebSocket;
using BirthdayBot.BackgroundServices;
using BirthdayBot.TextCommands;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using static BirthdayBot.TextCommands.CommandsCommon;

namespace BirthdayBot;

/// <summary>
/// More or less the main class for the program. Handles individual shards and provides frequent
/// status reports regarding the overall health of the application.
/// </summary>
class ShardManager : IDisposable {
    /// <summary>
    /// Number of seconds between each time the status task runs, in seconds.
    /// </summary>
    private const int StatusInterval = 60;

    /// <summary>
    /// Number of shards allowed to be destroyed before the program may close itself, if configured.
    /// </summary>
    private const int MaxDestroyedShards = 10; // TODO make configurable

    /// <summary>
    /// Number of concurrent shard startups to happen on each check.
    /// This value is also used in <see cref="DataRetention"/>.
    /// </summary>
    public const int MaxConcurrentOperations = 4;

    /// <summary>
    /// Amount of time without a completed background service run before a shard instance
    /// is considered "dead" and tasked to be removed. A fraction of this value is also used
    /// to determine when a shard is "slow".
    /// </summary>
    private static readonly TimeSpan DeadShardThreshold = new(0, 20, 0);

    /// <summary>
    /// A dictionary with shard IDs as its keys and shard instances as its values.
    /// When initialized, all keys will be created as configured. If an instance is removed,
    /// a key's corresponding value will temporarily become null instead of the key/value
    /// pair being removed.
    /// </summary>
    private readonly Dictionary<int, ShardInstance?> _shards;

    private readonly Dictionary<string, CommandHandler> _textCommands;

    private readonly Task _statusTask;
    private readonly CancellationTokenSource _mainCancel;
    private int _destroyedShards = 0;

    internal Configuration Config { get; }

    public ShardManager(Configuration cfg) {
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Log($"Birthday Bot v{ver!.ToString(3)} is starting...");

        Config = cfg;

        // Command handler setup
        _textCommands = new Dictionary<string, CommandHandler>(StringComparer.OrdinalIgnoreCase);
        var cmdsUser = new UserCommands(cfg);
        foreach (var item in cmdsUser.Commands) _textCommands.Add(item.Item1, item.Item2);
        var cmdsListing = new ListingCommands(cfg);
        foreach (var item in cmdsListing.Commands) _textCommands.Add(item.Item1, item.Item2);
        var cmdsHelp = new TextCommands.HelpInfoCommands(cfg);
        foreach (var item in cmdsHelp.Commands) _textCommands.Add(item.Item1, item.Item2);
        var cmdsMods = new ManagerCommands(cfg, cmdsUser.Commands);
        foreach (var item in cmdsMods.Commands) _textCommands.Add(item.Item1, item.Item2);

        // Allocate shards based on configuration
        _shards = new Dictionary<int, ShardInstance?>();
        for (int i = Config.ShardStart; i < (Config.ShardStart + Config.ShardAmount); i++) {
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
        ShardInstance newInstance;

        var clientConf = new DiscordSocketConfig() {
            ShardId = shardId,
            TotalShards = Config.ShardTotal,
            LogLevel = LogSeverity.Info,
            DefaultRetryMode = RetryMode.Retry502 | RetryMode.RetryTimeouts,
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.GuildMessages
        };
        var services = new ServiceCollection()
            .AddSingleton(s => new ShardInstance(this, s, _textCommands))
            .AddSingleton(s => new DiscordSocketClient(clientConf))
            .AddSingleton(s => new InteractionService(s.GetRequiredService<DiscordSocketClient>()))
            .BuildServiceProvider();
        newInstance = services.GetRequiredService<ShardInstance>();
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

    #region Status checking and display
    private struct GuildStatusData {
        public int GuildCount;
        public TimeSpan LastTaskRunTime;
        public string? ExecutingTask;
    }

    private string StatusDisplay(IEnumerable<int> guildList, Dictionary<int, GuildStatusData> guildInfo, bool showDetail) {
        if (!guildList.Any()) return "--";
        var result = new StringBuilder();
        foreach (var item in guildList) {
            result.Append(item.ToString("00") + " ");
            if (showDetail) {
                result.Remove(result.Length - 1, 1);
                result.Append($"[{Math.Floor(guildInfo[item].LastTaskRunTime.TotalSeconds):000}s");
                if (guildInfo[item].ExecutingTask != null)
                    result.Append($" {guildInfo[item].ExecutingTask}");
                result.Append("] ");
            }
        }
        if (result.Length > 0) result.Remove(result.Length - 1, 1);
        return result.ToString();
    }

    private async Task StatusLoop() {
        try {
            while (!_mainCancel.IsCancellationRequested) {
                Log($"Bot uptime: {Program.BotUptime}");

                // Iterate through shard list, extract data
                var guildInfo = new Dictionary<int, GuildStatusData>();
                var now = DateTimeOffset.UtcNow;
                var nullShards = new List<int>();
                foreach (var item in _shards) {
                    if (item.Value == null) {
                        nullShards.Add(item.Key);
                        continue;
                    }
                    var shard = item.Value;

                    guildInfo[item.Key] = new GuildStatusData {
                        GuildCount = shard.DiscordClient.Guilds.Count,
                        LastTaskRunTime = now - shard.LastBackgroundRun,
                        ExecutingTask = shard.CurrentExecutingService
                    };
                }

                // Process info
                var guildCounts = guildInfo.Select(i => i.Value.GuildCount);
                var guildTotal = guildCounts.Sum();
                var guildAverage = guildCounts.Any() ? guildCounts.Average() : 0;
                Log($"Currently in {guildTotal} guilds. Average shard load: {guildAverage:0.0}.");

                // Health report
                var goodShards = new List<int>();
                var badShards = new List<int>(); // shards with low connection score OR long time since last work
                var deadShards = new List<int>(); // shards to destroy and reinitialize
                foreach (var item in guildInfo) {
                    var lastRun = item.Value.LastTaskRunTime;

                    if (lastRun > DeadShardThreshold / 3) {
                        badShards.Add(item.Key);

                        // Consider a shard dead after a long span without background activity
                        if (lastRun > DeadShardThreshold)
                            deadShards.Add(item.Key);
                    } else {
                        goodShards.Add(item.Key);
                    }
                }
                Log("Online: " + StatusDisplay(goodShards, guildInfo, false));
                if (badShards.Count > 0) Log("Slow: " + StatusDisplay(badShards, guildInfo, true));
                if (deadShards.Count > 0) Log("Dead: " + StatusDisplay(deadShards, guildInfo, false));
                if (nullShards.Count > 0) Log("Offline: " + StatusDisplay(nullShards, guildInfo, false));

                // Remove dead shards
                foreach (var dead in deadShards) {
                    _shards[dead]!.Dispose();
                    _shards[dead] = null;
                    _destroyedShards++;
                }
                if (Config.QuitOnFails && _destroyedShards > MaxDestroyedShards) {
                    Environment.ExitCode = (int)Program.ExitCodes.DeadShardThreshold;
                    Program.ProgramStop();
                } else {
                    // Start up any missing shards
                    int startAllowance = MaxConcurrentOperations;
                    foreach (var id in nullShards) {
                        // To avoid possible issues with resources strained over so many shards starting at once,
                        // initialization is spread out by only starting a few at a time.
                        if (startAllowance-- > 0) {
                            _shards[id] = await InitializeShard(id).ConfigureAwait(false);
                        } else break;
                    }
                }

                await Task.Delay(StatusInterval * 1000, _mainCancel.Token).ConfigureAwait(false);
            }
        } catch (TaskCanceledException) { }
    }
    #endregion
}
