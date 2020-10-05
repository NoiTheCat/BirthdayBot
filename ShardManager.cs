using BirthdayBot.UserInterface;
using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
        /// Array indexes correspond to shard IDs. Lock on itself when modifying.
        /// </summary>
        private readonly ShardInstance[] _shards;

        // Commonly used command handler instances
        private readonly Dictionary<string, CommandHandler> _dispatchCommands;
        private readonly UserCommands _cmdsUser;
        private readonly ListingCommands _cmdsListing;
        private readonly HelpInfoCommands _cmdsHelp;
        private readonly ManagerCommands _cmdsMods;

        // Watchdog stuff
        private readonly Task _watchdogTask;
        private readonly CancellationTokenSource _watchdogCancel;
        
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

            // Start shards
            _shards = new ShardInstance[Config.ShardCount];
            for (int i = 0; i < _shards.Length; i++)
                InitializeShard(i).Wait();

            // Start watchdog
            _watchdogCancel = new CancellationTokenSource();
            _watchdogTask = Task.Factory.StartNew(WatchdogLoop, _watchdogCancel.Token,
                                                  TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void Dispose()
        {
            Log("Captured cancel key. Shutting down shard status watcher...");
            _watchdogCancel.Cancel();
            _watchdogTask.Wait(5000);
            if (!_watchdogTask.IsCompleted)
                Log("Warning: Shard status watcher has not ended in time. Continuing...");

            Log("Shutting down all shards...");
            var shardDisposes = new List<Task>();
            foreach (var shard in _shards)
            {
                if (shard == null) continue;
                shardDisposes.Add(Task.Run(shard.Dispose));
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
        /// Shuts down and removes an instance with equivalent ID if already exists.
        /// </summary>
        private async Task InitializeShard(int shardId)
        {
            ShardInstance newInstance;
            lock (_shards)
            {
                Task disposeOldShard;
                if (_shards[shardId] != null)
                    disposeOldShard = Task.Run(_shards[shardId].Dispose);
                else
                    disposeOldShard = Task.CompletedTask;

                var clientConf = new DiscordSocketConfig()
                {
                    LogLevel = LogSeverity.Debug, // TODO adjust after testing
                    AlwaysDownloadUsers = true, // TODO set to false when more stable to do so
                    DefaultRetryMode = Discord.RetryMode.RetryRatelimit,
                    MessageCacheSize = 0,
                    ShardId = shardId,
                    TotalShards = Config.ShardCount,
                    ExclusiveBulkDelete = true // we don't use these, but it's best to configure here
                };
                var newClient = new DiscordSocketClient(clientConf);
                newInstance = new ShardInstance(this, newClient, _dispatchCommands);

                disposeOldShard.Wait();
                _shards[shardId] = newInstance;
            }
            await newInstance.Start();
        }

        private async Task WatchdogLoop()
        {
            try
            {
                while (!_watchdogCancel.IsCancellationRequested)
                {
                    Log($"Bot uptime: {Common.BotUptime}");

                    // Gather statistical information within the lock
                    var guildCounts = new int[_shards.Length];
                    var connScores = new int[_shards.Length];
                    var lastRuns = new DateTimeOffset[_shards.Length];
                    ulong? botId = null;
                    lock (_shards)
                    {
                        for (int i = 0; i < _shards.Length; i++)
                        {
                            var shard = _shards[i];
                            if (shard == null) continue;

                            guildCounts[i] = shard.DiscordClient.Guilds.Count;
                            connScores[i] = shard.ConnectionScore;
                            lastRuns[i] = shard.LastBackgroundRun;
                            botId ??= shard.DiscordClient.CurrentUser?.Id;
                        }
                    }

                    // Guild count
                    var guildCountSum = guildCounts.Sum();
                    Log($"Currently in {guildCountSum} guilds.");
                    if (botId.HasValue)
                        await SendExternalStatistics(guildCountSum, botId.Value, _watchdogCancel.Token);

                    // Connection scores and worker health display
                    var now = DateTimeOffset.UtcNow;
                    for (int i = 0; i < connScores.Length; i++)
                    {
                        var dur = now - lastRuns[i];
                        var lastRunDuration = $"Last run: {Math.Floor(dur.TotalMinutes):00}m{dur.Seconds:00}s ago";

                        Log($"Shard {i:00}: Score {connScores[i]:+0000;-0000} - " + lastRunDuration);
                    }

                    // 120 second delay
                    await Task.Delay(120 * 1000, _watchdogCancel.Token);
                }
            }
            catch (TaskCanceledException) { }
        }

        #region Statistical reporting
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Send statistical information to external services.
        /// </summary>
        private async Task SendExternalStatistics(int count, ulong botId, CancellationToken token)
        {
            // TODO protect against exceptions
            var dbotsToken = Config.DBotsToken;
            if (dbotsToken != null)
            {
                const string dBotsApiUrl = "https://discord.bots.gg/api/v1/bots/{0}/stats";
                const string Body = "{{ \"guildCount\": {0} }}";
                var uri = new Uri(string.Format(dBotsApiUrl, botId));

                var post = new HttpRequestMessage(HttpMethod.Post, uri);
                post.Headers.Add("Authorization", dbotsToken);
                post.Content = new StringContent(string.Format(Body, count), Encoding.UTF8, "application/json");

                await Task.Delay(80); // Discord Bots rate limit for this endpoint is 20 per second
                await _httpClient.SendAsync(post, token);
                Log("Discord Bots: Count sent successfully.");
            }
        }
        #endregion
    }
}
