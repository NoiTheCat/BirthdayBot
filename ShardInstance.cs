using BirthdayBot.BackgroundServices;
using BirthdayBot.Data;
using Discord.Net;
using static BirthdayBot.UserInterface.CommandsCommon;

namespace BirthdayBot;

/// <summary>
/// Single shard instance for Birthday Bot. This shard independently handles all input and output to Discord.
/// </summary>
class ShardInstance : IDisposable {
    private readonly ShardManager _manager;
    private readonly ShardBackgroundWorker _background;
    private readonly Dictionary<string, CommandHandler> _dispatchCommands;

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

    /// <summary>
    /// Prepares and configures the shard instances, but does not yet start its connection.
    /// </summary>
    public ShardInstance(ShardManager manager, DiscordSocketClient client, Dictionary<string, CommandHandler> commands) {
        _manager = manager;
        _dispatchCommands = commands;

        DiscordClient = client;
        DiscordClient.Log += Client_Log;
        DiscordClient.Ready += Client_Ready;
        DiscordClient.MessageReceived += Client_MessageReceived;

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
    /// Does all necessary steps to stop this shard. This method may block for a few seconds as it waits
    /// for the process to finish, but will force its disposal after at most 30 seconds.
    /// </summary>
    public void Dispose() {
        // Unsubscribe from own events
        DiscordClient.Log -= Client_Log;
        DiscordClient.Ready -= Client_Ready;
        DiscordClient.MessageReceived -= Client_MessageReceived;

        _background.Dispose();
        try {
            if (!DiscordClient.LogoutAsync().Wait(15000))
                Log("Instance", "Warning: Client has not yet logged out. Continuing cleanup.");
        } catch (Exception ex) {
            Log("Instance", "Warning: Client threw an exception when logging out: " + ex.Message);
        }
        try {
            if (!DiscordClient.StopAsync().Wait(5000))
                Log("Instance", "Warning: Client has not yet stopped. Continuing cleanup.");
        } catch (Exception ex) {
            Log("Instance", "Warning: Client threw an exception when stopping: " + ex.Message);
        }

        var clientDispose = Task.Run(DiscordClient.Dispose);
        if (!clientDispose.Wait(10000)) Log("Instance", "Warning: Client is hanging on dispose. Will continue.");
        else Log("Instance", "Shard instance disposed.");
    }

    public void Log(string source, string message) => Program.Log($"Shard {ShardId:00}] [{source}", message);

    public void RequestDownloadUsers(ulong guildId) => _background.UserDownloader.RequestDownload(guildId);

    #region Event handling
    private Task Client_Log(LogMessage arg) {
        // TODO revise this some time, filters might need to be modified by now
        // Suppress certain messages
        if (arg.Message != null) {
            if (arg.Message.StartsWith("Unknown Dispatch ") || arg.Message.StartsWith("Unknown Channel")) return Task.CompletedTask;
            switch (arg.Message) // Connection status messages replaced by ShardManager's output
            {
                case "Connecting":
                case "Connected":
                case "Ready":
                case "Failed to resume previous session":
                case "Resumed previous session":
                case "Disconnecting":
                case "Disconnected":
                case "WebSocket connection was closed":
                case "Server requested a reconnect":
                    return Task.CompletedTask;
            }
            //if (arg.Message == "Heartbeat Errored") {
            //    // Replace this with a custom message; do not show stack trace
            //    Log("Discord.Net", $"{arg.Severity}: {arg.Message} - {arg.Exception.Message}");
            //    return Task.CompletedTask;
            //}

            Log("Discord.Net", $"{arg.Severity}: {arg.Message}");
        }

        if (arg.Exception != null) Log("Discord.Net", arg.Exception.ToString());

        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets the shard's status to display the help command.
    /// </summary>
    private async Task Client_Ready() => await DiscordClient.SetGameAsync(CommandPrefix + "help");

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
            if (!_dispatchCommands.TryGetValue(csplit[0][CommandPrefix.Length..], out CommandHandler? command)) return;

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
                    channel.SendMessageAsync(":x: An unknown error occurred. It has been reported to the bot owner.").Wait();
                    // TODO webhook report
                } catch (HttpException) { } // Fail silently
            }
        }
    }
    #endregion
}
