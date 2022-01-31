using BirthdayBot.BackgroundServices;
using BirthdayBot.Data;
using Discord.Net;
using static BirthdayBot.TextCommands.CommandsCommon;

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

    #region Event handling
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
                    channel.SendMessageAsync(TextCommands.CommandsCommon.InternalError).Wait();
                } catch (HttpException) { } // Fail silently
            }
        }
    }
    #endregion
}
