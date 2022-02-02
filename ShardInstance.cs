using BirthdayBot.ApplicationCommands;
using BirthdayBot.BackgroundServices;
using BirthdayBot.Data;
using Discord.Net;
using static BirthdayBot.ApplicationCommands.BotApplicationCommand;
using static BirthdayBot.TextCommands.CommandsCommon;

namespace BirthdayBot;

/// <summary>
/// Single shard instance for Birthday Bot. This shard independently handles all input and output to Discord.
/// </summary>
class ShardInstance : IDisposable {
    private readonly ShardManager _manager;
    private readonly ShardBackgroundWorker _background;
    private readonly Dictionary<string, CommandHandler> _textDispatch;
    private readonly IEnumerable<BotApplicationCommand> _slashCmdHandlers;

    public DiscordSocketClient DiscordClient { get; }
    public int ShardId => DiscordClient.ShardId;
    /// <summary>
    /// Returns a value showing the time in which the last background run successfully completed.
    /// </summary>
    public DateTimeOffset LastBackgroundRun => _background.LastBackgroundRun;
    /// <summary>
    /// Returns the name of the background service currently in execution.
    /// </summary>
    public string? CurrentExecutingService => _background.CurrentExecutingService;
    public Configuration Config => _manager.Config;

    public const string InternalError = ":x: An unknown error occurred. If it persists, please notify the bot owner.";

    /// <summary>
    /// Prepares and configures the shard instances, but does not yet start its connection.
    /// </summary>
    public ShardInstance(ShardManager manager, DiscordSocketClient client,
                         Dictionary<string, CommandHandler> textCmds, IEnumerable<BotApplicationCommand> appCmdHandlers) {
        _manager = manager;
        _textDispatch = textCmds;
        _slashCmdHandlers = appCmdHandlers;

        DiscordClient = client;
        DiscordClient.Log += Client_Log;
        DiscordClient.Ready += Client_Ready;
        DiscordClient.MessageReceived += Client_MessageReceived;
        DiscordClient.SlashCommandExecuted += DiscordClient_SlashCommandExecuted;

        // Background task constructor begins background processing immediately.
        _background = new ShardBackgroundWorker(this);
    }

    /// <summary>
    /// Starts up this shard's connection to Discord and background task handling associated with it.
    /// </summary>
    public async Task StartAsync() {
        await DiscordClient.LoginAsync(TokenType.Bot, Config.BotToken).ConfigureAwait(false);
        await DiscordClient.StartAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Does all necessary steps to stop this shard, including canceling background tasks and disconnecting.
    /// </summary>
    public void Dispose() {
        DiscordClient.Log -= Client_Log;
        DiscordClient.Ready -= Client_Ready;
        DiscordClient.MessageReceived -= Client_MessageReceived;

        _background.Dispose();
        DiscordClient.LogoutAsync().Wait(5000);
        DiscordClient.StopAsync().Wait(5000);
        DiscordClient.Dispose();
        Log(nameof(ShardInstance), "Shard instance disposed.");
    }

    public void Log(string source, string message) => Program.Log($"Shard {ShardId:00}] [{source}", message);

    private Task Client_Log(LogMessage arg) {
        // Suppress certain messages
        if (arg.Message != null) {
            // These warnings appear often as of Discord.Net v3...
            if (arg.Message.StartsWith("Unknown Dispatch ") || arg.Message.StartsWith("Unknown Channel")) return Task.CompletedTask;
            switch (arg.Message) // Connection status messages replaced by ShardManager's output
            {
                case "Connecting":
                case "Connected":
                case "Ready":
                case "Disconnecting":
                case "Disconnected":
                case "Resumed previous session":
                case "Failed to resume previous session":
                case "Discord.WebSocket.GatewayReconnectException: Server requested a reconnect":
                    return Task.CompletedTask;
            }

            Log("Discord.Net", $"{arg.Severity}: {arg.Message}");
        }

        if (arg.Exception != null) Log("Discord.Net exception", arg.Exception.ToString());

        return Task.CompletedTask;
    }

    /// <summary>
    /// Registers all available slash commands.
    /// Additionally, sets the shard's status to display the help command.
    /// </summary>
    private async Task Client_Ready() {
        await DiscordClient.SetGameAsync(CommandPrefix + "help");

#if !DEBUG
        // Update our commands here, only when the first shard connects
        if (ShardId != 0) return;
#endif
        var commands = new List<ApplicationCommandProperties>();
        foreach (var source in _slashCmdHandlers) {
            commands.AddRange(source.GetCommands());
        }
#if !DEBUG
        // Remove any unneeded/unused commands
        var existingcmdnames = cmds.Select(c => c.Name.Value).ToHashSet();
        foreach (var gcmd in await DiscordClient.GetGlobalApplicationCommandsAsync()) {
            if (!existingcmdnames.Contains(gcmd.Name)) {
                Log("Command registration", $"Found registered unused command /{gcmd.Name} - sending removal request");
                await gcmd.DeleteAsync();
            }
        }
        // And update what we have
        Log("Command registration", $"Bulk updating {cmds.Length} global command(s)");
        await DiscordClient.BulkOverwriteGlobalApplicationCommandsAsync(cmds).ConfigureAwait(false);
#else
        // Debug: Register our commands locally instead, in each guild we're in
        foreach (var g in DiscordClient.Guilds) {
            await g.DeleteApplicationCommandsAsync().ConfigureAwait(false);
            await g.BulkOverwriteApplicationCommandAsync(commands.ToArray()).ConfigureAwait(false);
        }

        foreach (var gcmd in await DiscordClient.GetGlobalApplicationCommandsAsync()) {
            Program.Log("Command registration", $"Found global command /{gcmd.Name} and we're DEBUG - sending removal request");
            await gcmd.DeleteAsync();
        }
#endif
    }

    /// <summary>
    /// Determines if the incoming message is an incoming command, and dispatches to the appropriate handler if necessary.
    /// </summary>
    private async Task Client_MessageReceived(SocketMessage msg) {
        if (msg.Channel is not SocketTextChannel channel) return;
        if (msg.Author.IsBot || msg.Author.IsWebhook) return;
        if (((IMessage)msg).Type != MessageType.Default) return;
        var author = (SocketGuildUser)msg.Author;

        // Limit 3:
        // For all cases: base command, 2 parameters.
        // Except this case: "bb.config", subcommand name, subcommand parameters in a single string
        var csplit = msg.Content.Split(" ", 3, StringSplitOptions.RemoveEmptyEntries);
        if (csplit.Length > 0 && csplit[0].StartsWith(CommandPrefix, StringComparison.OrdinalIgnoreCase)) {
            // Determine if it's something we're listening for.
            if (!_textDispatch.TryGetValue(csplit[0][CommandPrefix.Length..], out CommandHandler? command)) return;

            // Load guild information here
            var gconf = await GuildConfiguration.LoadAsync(channel.Guild.Id, false);

            // Ban check
            if (!gconf!.IsBotModerator(author)) // skip check if user is a moderator
            {
                if (await gconf.IsUserBlockedAsync(author.Id)) return; // silently ignore
            }

            // Execute the command
            try {
                Log("Command", $"{channel.Guild.Name}/{author.Username}#{author.Discriminator}: {msg.Content}");
                await command(this, gconf, csplit, channel, author);
            } catch (Exception ex) {
                if (ex is HttpException) return;
                Log("Command", ex.ToString());
                try {
                    channel.SendMessageAsync(InternalError).Wait();
                } catch (HttpException) { } // Fail silently
            }
        }
    }

    /// <summary>
    /// Dispatches to the appropriate slash command handler while catching any exceptions that may occur.
    /// </summary>
    private async Task DiscordClient_SlashCommandExecuted(SocketSlashCommand arg) {
        SocketGuildChannel? rptChannel = arg.Channel as SocketGuildChannel;
        string rpt = "";
        if (rptChannel != null) rpt += rptChannel.Guild.Name + "!";
        rpt += arg.User;
        var rptId = rptChannel?.Guild.Id ?? arg.User.Id;
        var logLine = $"/{arg.CommandName} at {rpt}; { (rptChannel != null ? "Guild" : "User") } ID {rptId}.";

        // Specific reply for DM messages
        if (rptChannel == null) {
            // TODO do not hardcode message
            // TODO figure out appropriate message
            Log("Command", logLine + " Sending default reply.");
            await arg.RespondAsync("don't dm me").ConfigureAwait(false);
            return;
        }

        // Determine handler to use
        CommandResponder? handler = null;
        foreach (var source in _slashCmdHandlers) {
            handler = source.GetHandlerFor(arg.CommandName);
            if (handler != null) break;
        }
        
        if (handler == null) { // Handler not found
            Log("Command", logLine + " Unknown command.");
            await arg.RespondAsync("Oops, that command isn't supposed to be there... Please try something else.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        var gconf = await GuildConfiguration.LoadAsync(rptChannel.Guild.Id, false);
        // Blocklist/moderated check
        if (!gconf!.IsBotModerator((SocketGuildUser)arg.User)) // Except if moderator
        {
            if (await gconf.IsUserBlockedAsync(arg.User.Id)) {
                Log("Command", logLine + " Blocked per guild policy.");
                await arg.RespondAsync(AccessDeniedError, ephemeral: true).ConfigureAwait(false);
                return;
            }
        }

        // Execute the handler
        try {
            await handler(this, gconf, arg).ConfigureAwait(false);
            Log("Command", logLine);
        } catch (Exception e) when (e is not HttpException) {
            Log("Command", $"{logLine} {e}");
        }
    }
}
