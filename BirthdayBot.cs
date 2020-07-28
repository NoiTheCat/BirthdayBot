using BirthdayBot.Data;
using BirthdayBot.UserInterface;
using Discord;
using Discord.Net;
using Discord.Webhook;
using Discord.WebSocket;
using System;
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

        private readonly BackgroundServiceRunner _worker;
        
        internal Configuration Config { get; }
        internal DiscordShardedClient DiscordClient { get; }
        internal DiscordWebhookClient LogWebhook { get; }

        /// <summary>
        /// Prepares the bot connection and all its event handlers
        /// </summary>
        public BirthdayBot(Configuration conf, DiscordShardedClient dc)
        {
            Config = conf;
            DiscordClient = dc;
            LogWebhook = new DiscordWebhookClient(conf.LogWebhook);

            _worker = new BackgroundServiceRunner(this);

            // Command dispatch set-up
            _dispatchCommands = new Dictionary<string, CommandHandler>(StringComparer.OrdinalIgnoreCase);
            _cmdsUser = new UserCommands(this, conf);
            foreach (var item in _cmdsUser.Commands) _dispatchCommands.Add(item.Item1, item.Item2);
            _cmdsListing = new ListingCommands(this, conf);
            foreach (var item in _cmdsListing.Commands) _dispatchCommands.Add(item.Item1, item.Item2);
            _cmdsHelp = new HelpInfoCommands(this, conf);
            foreach (var item in _cmdsHelp.Commands) _dispatchCommands.Add(item.Item1, item.Item2);
            _cmdsMods = new ManagerCommands(this, conf, _cmdsUser.Commands, _worker.BirthdayUpdater.SingleProcessGuildAsync);
            foreach (var item in _cmdsMods.Commands) _dispatchCommands.Add(item.Item1, item.Item2);

            // Register event handlers
            DiscordClient.ShardConnected += SetStatus;
            DiscordClient.MessageReceived += Dispatch;
        }

        /// <summary>
        /// Does some more basic initialization and then connects to Discord
        /// </summary>
        public async Task Start()
        {
            await Database.DoInitialDatabaseSetupAsync();

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

        private async Task SetStatus(DiscordSocketClient shard) => await shard.SetGameAsync(CommandPrefix + "help");

        public async Task PushErrorLog(string source, string message)
        {
            // Attempt to report instance logging failure to the reporting channel
            try
            {
                EmbedBuilder e = new EmbedBuilder()
                {
                    Footer = new EmbedFooterBuilder() { Text = source },
                    Timestamp = DateTimeOffset.UtcNow,
                    Description = message
                };
                await LogWebhook.SendMessageAsync(embeds: new Embed[] { e.Build() });
            }
            catch
            {
                return; // Give up
            }
        }

        private async Task Dispatch(SocketMessage msg)
        {
            if (!(msg.Channel is SocketTextChannel channel)) return;
            if (msg.Author.IsBot || msg.Author.IsWebhook) return;
            if (((IMessage)msg).Type != MessageType.Default) return;
            var author = (SocketGuildUser)msg.Author;

            // Limit 3:
            // For all cases: base command, 2 parameters.
            // Except this case: "bb.config", subcommand name, subcommand parameters in a single string
            var csplit = msg.Content.Split(" ", 3, StringSplitOptions.RemoveEmptyEntries);
            if (csplit.Length > 0 && csplit[0].StartsWith(CommandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Determine if it's something we're listening for.
                if (!_dispatchCommands.TryGetValue(csplit[0].Substring(CommandPrefix.Length), out CommandHandler command)) return;

                // Load guild information here
                var gconf = await GuildConfiguration.LoadAsync(channel.Guild.Id, false);

                // Ban check
                if (!gconf.IsBotModerator(author)) // skip check if user is a moderator
                {
                    if (await gconf.IsUserBlockedAsync(author.Id)) return; // silently ignore
                }

                // Execute the command
                try
                {
                    Program.Log("Command", $"{channel.Guild.Name}/{author.Username}#{author.Discriminator}: {msg.Content}");
                    await command(csplit, gconf, channel, author);
                }
                catch (Exception ex)
                {
                    if (ex is HttpException) return;
                    Program.Log("Error", ex.ToString());
                    try
                    {
                        channel.SendMessageAsync(":x: An unknown error occurred. It has been reported to the bot owner.").Wait();
                        // TODO webhook report
                    }
                    catch (HttpException)
                    {
                        // Fail silently.
                    }
                }
            }
        }
    }
}
