using BirthdayBot.BackgroundServices;
using BirthdayBot.UserInterface;
using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static BirthdayBot.UserInterface.CommandsCommon;

namespace BirthdayBot
{
    /// <summary>
    /// The highest level part of this bot:
    /// Starts up, looks over, and manages shard instances while containing common resources
    /// and providing common functions for all existing shards.
    /// </summary>
    class ShardManager : IDisposable
    {
        /// <summary>
        /// Number of seconds between each time the manager's watchdog task runs, in seconds.
        /// </summary>
        private const int WatchdogInterval = 90;

        /// <summary>
        /// Number of shards allowed to be destroyed before forcing the program to close.
        /// </summary>
        private const int MaxDestroyedShards = 10; // TODO make configurable

        /// <summary>
        /// Number of concurrent shard startups to happen on each check.
        /// This value is also used in <see cref="DataRetention"/>.
        /// </summary>
        public const int MaxConcurrentOperations = 5;

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
        private readonly Dictionary<int, ShardInstance> _shards;

        // Commonly used command handler instances
        private readonly Dictionary<string, CommandHandler> _dispatchCommands;
        private readonly UserCommands _cmdsUser;
        private readonly ListingCommands _cmdsListing;
        private readonly HelpInfoCommands _cmdsHelp;
        private readonly ManagerCommands _cmdsMods;

        // Watchdog stuff
        private readonly Task _watchdogTask;
        private readonly CancellationTokenSource _watchdogCancel;
        private int _destroyedShards = 0;
        
        internal Configuration Config { get; }

        public ShardManager(Configuration cfg)
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            Log($"Birthday Bot v{ver.ToString(3)} is starting...");

            Config = cfg;

            // Command handler setup
            _dispatchCommands = new Dictionary<string, CommandHandler>(StringComparer.OrdinalIgnoreCase);
            _cmdsUser = new UserCommands(cfg);
            foreach (var item in _cmdsUser.Commands) _dispatchCommands.Add(item.Item1, item.Item2);
            _cmdsListing = new ListingCommands(cfg);
            foreach (var item in _cmdsListing.Commands) _dispatchCommands.Add(item.Item1, item.Item2);
            _cmdsHelp = new HelpInfoCommands(cfg);
            foreach (var item in _cmdsHelp.Commands) _dispatchCommands.Add(item.Item1, item.Item2);
            _cmdsMods = new ManagerCommands(cfg, _cmdsUser.Commands);
            foreach (var item in _cmdsMods.Commands) _dispatchCommands.Add(item.Item1, item.Item2);

            _shards = new Dictionary<int, ShardInstance>();
            // Create only the specified shards as needed by this instance
            for (int i = Config.ShardStart; i < (Config.ShardStart + Config.ShardAmount); i++)
            {
                _shards.Add(i, null);
            }

            // Start watchdog
            _watchdogCancel = new CancellationTokenSource();
            _watchdogTask = Task.Factory.StartNew(WatchdogLoop, _watchdogCancel.Token,
                                                  TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void Dispose()
        {
            _watchdogCancel.Cancel();
            _watchdogTask.Wait(5000);
            if (!_watchdogTask.IsCompleted)
                Log("Warning: Shard status watcher has not ended in time. Continuing...");

            Log("Shutting down all shards...");
            var shardDisposes = new List<Task>();
            foreach (var item in _shards)
            {
                if (item.Value == null) continue;
                shardDisposes.Add(Task.Run(item.Value.Dispose));
            }
            if (!Task.WhenAll(shardDisposes).Wait(60000))
            {
                Log("Warning: All shards did not properly stop after 60 seconds. Continuing...");
            }

            Log($"Shutdown complete. Bot uptime: {Common.BotUptime}");
        }

        private void Log(string message) => Program.Log(nameof(ShardManager), message);

        /// <summary>
        /// Creates and sets up a new shard instance.
        /// </summary>
        private async Task<ShardInstance> InitializeShard(int shardId)
        {
            ShardInstance newInstance;

            var clientConf = new DiscordSocketConfig()
            {
                ShardId = shardId,
                TotalShards = Config.ShardTotal,
                LogLevel = LogSeverity.Info,
                DefaultRetryMode = RetryMode.RetryRatelimit,
                MessageCacheSize = 0, // not needed at all
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.GuildMessages
            };
            var newClient = new DiscordSocketClient(clientConf);
            newInstance = new ShardInstance(this, newClient, _dispatchCommands);
            await newInstance.StartAsync().ConfigureAwait(false);

            return newInstance;
        }

        private async Task WatchdogLoop()
        {
            try
            {
                while (!_watchdogCancel.IsCancellationRequested)
                {
                    Log($"Bot uptime: {Common.BotUptime}");

                    // Iterate through shard list, extract data
                    var guildInfo = new Dictionary<int, (int, int, TimeSpan, string)>();
                    var now = DateTimeOffset.UtcNow;
                    var nullShards = new List<int>();
                    foreach (var item in _shards)
                    {
                        if (item.Value == null)
                        {
                            nullShards.Add(item.Key);
                            continue;
                        }
                        var shard = item.Value;

                        var guildCount = shard.DiscordClient.Guilds.Count;
                        var connScore = shard.ConnectionScore;
                        var lastRun = now - shard.LastBackgroundRun;
                        var lastExec = shard.CurrentExecutingService ?? "null";

                        guildInfo[item.Key] = (guildCount, connScore, lastRun, lastExec);
                    }

                    // Process info
                    var guildCounts = guildInfo.Select(i => i.Value.Item1);
                    var guildTotal = guildCounts.Sum();
                    var guildAverage = guildCounts.Any() ? guildCounts.Average() : 0;
                    Log($"Currently in {guildTotal} guilds. Average shard load: {guildAverage:0.0}.");

                    // Health report
                    var goodShards = new List<int>();
                    var badShards = new List<int>(); // shards with low connection score OR long time since last work
                    var deadShards = new List<int>(); // shards to destroy and reinitialize
                    foreach (var item in guildInfo)
                    {
                        var connScore = item.Value.Item2;
                        var lastRun = item.Value.Item3;

                        if (lastRun > new TimeSpan(0, 10, 0) || connScore < ConnectionStatus.StableScore)
                        {
                            badShards.Add(item.Key);

                            // Consider a shard dead after a long span without background activity
                            if (lastRun > DeadShardThreshold)
                                deadShards.Add(item.Key);
                        }
                        else
                        {
                            goodShards.Add(item.Key);
                        }
                    }
                    string statusDisplay(IEnumerable<int> list, bool detailedInfo)
                    {
                        if (!list.Any()) return "--";
                        var result = new StringBuilder();
                        foreach (var item in list)
                        {
                            result.Append(item.ToString("00") + " ");
                            if (detailedInfo)
                            {
                                result.Remove(result.Length - 1, 1);
                                result.Append($"[{guildInfo[item].Item2:+0;-0}");
                                result.Append($" {Math.Floor(guildInfo[item].Item3.TotalSeconds):000}s");
                                result.Append($" {guildInfo[item].Item4}] ");
                            }
                        }
                        if (result.Length > 0) result.Remove(result.Length - 1, 1);
                        return result.ToString();
                    }
                    Log("Stable shards: " + statusDisplay(goodShards, false));
                    if (badShards.Count > 0) Log("Unstable shards: " + statusDisplay(badShards, true));
                    if (deadShards.Count > 0) Log("Shards to be restarted: " + statusDisplay(deadShards, false));
                    if (nullShards.Count > 0) Log("Inactive shards: " + statusDisplay(nullShards, false));

                    // Remove dead shards
                    foreach (var dead in deadShards) {
                        // TODO investigate - has this been hanging here?
                        _shards[dead].Dispose();
                        _shards[dead] = null;
                        _destroyedShards++;
                    }
                    if (Config.QuitOnFails && _destroyedShards > MaxDestroyedShards)
                    {
                        Program.ProgramStop();
                    }
                    else
                    {
                        // Start up any missing shards
                        int startAllowance = MaxConcurrentOperations;
                        foreach (var id in nullShards)
                        {
                            // To avoid possible issues with resources strained over so many shards starting at once,
                            // initialization is spread out by only starting a few at a time.
                            if (startAllowance-- > 0)
                            {
                                _shards[id] = await InitializeShard(id).ConfigureAwait(false);
                            }
                            else break;
                        }
                    }

                    // All done for now
                    await Task.Delay(WatchdogInterval * 1000, _watchdogCancel.Token).ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException) { }
        }
    }
}
