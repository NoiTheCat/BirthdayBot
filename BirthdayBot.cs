using BirthdayBot.Data;
using BirthdayBot.UserInterface;
using Discord;
using Discord.Net;
using Discord.Webhook;
using Discord.WebSocket;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using static BirthdayBot.UserInterface.CommandsCommon;

namespace BirthdayBot
{
    class BirthdayBot
    {
        private readonly Dictionary<string, CommandHandler> _dispatchCommands;
        private readonly UserCommands _cmdsUser;
        private readonly ListingCommands _cmdsListing;
        private readonly HelpInfoCommands _cmdsHelp;
        private readonly ManagerCommands _cmdsMods;

        private BackgroundServiceRunner _worker;
        
        internal Configuration Config { get; }
        internal DiscordShardedClient DiscordClient { get; }
        internal ConcurrentDictionary<ulong, GuildStateInformation> GuildCache { get; }
        internal DiscordWebhookClient LogWebhook { get; }

        public BirthdayBot(Configuration conf, DiscordShardedClient dc)
        {
            Config = conf;
            DiscordClient = dc;
            LogWebhook = new DiscordWebhookClient(conf.LogWebhook);
            GuildCache = new ConcurrentDictionary<ulong, GuildStateInformation>();

            _worker = new BackgroundServiceRunner(this);

            // Command dispatch set-up
            _dispatchCommands = new Dictionary<string, CommandHandler>(StringComparer.OrdinalIgnoreCase);
            _cmdsUser = new UserCommands(this, conf);
            foreach (var item in _cmdsUser.Commands) _dispatchCommands.Add(item.Item1, item.Item2);
            _cmdsListing = new ListingCommands(this, conf);
            foreach (var item in _cmdsListing.Commands) _dispatchCommands.Add(item.Item1, item.Item2);
            _cmdsHelp = new HelpInfoCommands(this, conf);
            foreach (var item in _cmdsHelp.Commands) _dispatchCommands.Add(item.Item1, item.Item2);
            _cmdsMods = new ManagerCommands(this, conf, _cmdsUser.Commands);
            foreach (var item in _cmdsMods.Commands) _dispatchCommands.Add(item.Item1, item.Item2);

            // Register event handlers
            DiscordClient.JoinedGuild += LoadGuild;
            DiscordClient.GuildAvailable += LoadGuild;
            DiscordClient.LeftGuild += DiscardGuild;
            DiscordClient.ShardConnected += SetStatus;
            DiscordClient.MessageReceived += Dispatch;
        }

        public async Task Start()
        {
            await DiscordClient.LoginAsync(TokenType.Bot, Config.BotToken);
            await DiscordClient.StartAsync();

            _worker.Start();

            await Task.Delay(-1);
        }

        /// <summary>
        /// Called only by CancelKeyPress handler.
        /// </summary>
        public async Task Shutdown()
        {
            await _worker.Cancel();
            await DiscordClient.LogoutAsync();
            DiscordClient.Dispose();
        }

        private async Task LoadGuild(SocketGuild g)
        {
            if (!GuildCache.ContainsKey(g.Id))
            {
                var gi = await GuildStateInformation.LoadSettingsAsync(Config.DatabaseSettings, g.Id);
                GuildCache.TryAdd(g.Id, gi);
            }
        }

        private Task DiscardGuild(SocketGuild g)
        {
            GuildCache.TryRemove(g.Id, out _);
            return Task.CompletedTask;
        }

        private async Task SetStatus(DiscordSocketClient shard)
        {
            await shard.SetGameAsync(CommandPrefix + "help");
        }

        private async Task Dispatch(SocketMessage msg)
        {
            if (msg.Channel is IDMChannel) return;
            if (msg.Author.IsBot) return;
            // TODO determine message type (pin, join, etc)

            // Limit 3:
            // For all cases: base command, 2 parameters.
            // Except this case: "bb.config", subcommand name, subcommand parameters in a single string
            var csplit = msg.Content.Split(" ", 3, StringSplitOptions.RemoveEmptyEntries);
            if (csplit.Length > 0)
            {
                var channel = (SocketTextChannel)msg.Channel;
                var author = (SocketGuildUser)msg.Author;

                // Determine if it's something we're listening for.
                // Doing this first before the block check because a block check triggers a database query.
                CommandHandler command = null;
                if (!_dispatchCommands.TryGetValue(csplit[0].Substring(CommandPrefix.Length), out command)) return;

                // Ban check
                var gi = GuildCache[channel.Guild.Id];
                // Skip ban check if user is a manager
                if (!gi.IsUserModerator(author))
                {
                    if (gi.IsUserBlockedAsync(author.Id).GetAwaiter().GetResult()) return;
                }

                // Execute the command
                try
                {
                    Program.Log("Command", $"{channel.Guild.Name}/{author.Username}#{author.Discriminator}: {msg.Content}");
                    await command(csplit, channel, author);
                }
                catch (Exception ex)
                {
                    if (ex is HttpException) return;
                    Program.Log("Error", ex.ToString());
                    try
                    {
                        channel.SendMessageAsync(":x: An unknown error occurred. It has been reported to the bot owner.").Wait();
                    } catch (HttpException ex2)
                    {
                        // Fail silently.
                    }
                }

                // Immediately check for role updates in the invoking guild
                // TODO be smarter about when to call this
                await _worker.BirthdayUpdater.SingleUpdateFor(channel.Guild);
            }
        }
    }
}
