using BirthdayBot.ApplicationCommands;
using BirthdayBot.BackgroundServices;
using BirthdayBot.Data;
using Discord.Interactions;
using Discord.Net;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using static BirthdayBot.TextCommands.CommandsCommon;

namespace BirthdayBot;

/// <summary>
/// Single shard instance for Birthday Bot. This shard independently handles all input and output to Discord.
/// </summary>
public sealed class ShardInstance : IDisposable {
    private readonly ShardManager _manager;
    private readonly ShardBackgroundWorker _background;
    private readonly Dictionary<string, CommandHandler> _textDispatch;
    private readonly InteractionService _interactionService;
    private readonly IServiceProvider _services;

    internal DiscordSocketClient DiscordClient { get; }
    public int ShardId => DiscordClient.ShardId;
    /// <summary>
    /// Returns a value showing the time in which the last background run successfully completed.
    /// </summary>
    internal DateTimeOffset LastBackgroundRun => _background.LastBackgroundRun;
    /// <summary>
    /// Returns the name of the background service currently in execution.
    /// </summary>
    internal string? CurrentExecutingService => _background.CurrentExecutingService;
    internal Configuration Config => _manager.Config;

    public const string InternalError = ":x: An unknown error occurred. If it persists, please notify the bot owner.";

    /// <summary>
    /// Prepares and configures the shard instances, but does not yet start its connection.
    /// </summary>
    internal ShardInstance(ShardManager manager, IServiceProvider services, Dictionary<string, CommandHandler> textCmds) {
        _manager = manager;
        _services = services;
        _textDispatch = textCmds;

        DiscordClient = _services.GetRequiredService<DiscordSocketClient>();
        DiscordClient.Log += Client_Log;
        DiscordClient.Ready += Client_Ready;
        DiscordClient.MessageReceived += Client_MessageReceived;

        _interactionService = _services.GetRequiredService<InteractionService>();
        DiscordClient.InteractionCreated += DiscordClient_InteractionCreated;
        _interactionService.SlashCommandExecuted += InteractionService_SlashCommandExecuted;
        DiscordClient.ModalSubmitted += modal => { return ModalResponder.DiscordClient_ModalSubmitted(this, modal); };

        // Background task constructor begins background processing immediately.
        _background = new ShardBackgroundWorker(this);
        Log(nameof(ShardInstance), "Instance created.");
    }

    /// <summary>
    /// Starts up this shard's connection to Discord and background task handling associated with it.
    /// </summary>
    public async Task StartAsync() {
        await _interactionService.AddModulesAsync(Assembly.GetExecutingAssembly(), _services).ConfigureAwait(false);
        await DiscordClient.LoginAsync(TokenType.Bot, Config.BotToken).ConfigureAwait(false);
        await DiscordClient.StartAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Does all necessary steps to stop this shard, including canceling background tasks and disconnecting.
    /// </summary>
    public void Dispose() {
        _background.Dispose();
        DiscordClient.LogoutAsync().Wait(5000);
        DiscordClient.Dispose();
        _interactionService.Dispose();
        Log(nameof(ShardInstance), "Instance disposed.");
    }

    internal void Log(string source, string message) => Program.Log($"Shard {ShardId:00}] [{source}", message);

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
        // TODO get rid of this eventually? or change it to something fun...
        await DiscordClient.SetGameAsync("/help");

#if !DEBUG
        // Update slash/interaction commands
        if (ShardId == 0) {
            await _interactionService.RegisterCommandsGloballyAsync(true).ConfigureAwait(false);
            Log(nameof(ShardInstance), "Updated global command registration.");
        }
#else
        // Debug: Register our commands locally instead, in each guild we're in
        foreach (var g in DiscordClient.Guilds) {
            await _interactionService.RegisterCommandsToGuildAsync(g.Id, true).ConfigureAwait(false);
            Log(nameof(ShardInstance), $"Updated DEBUG command registration in guild {g.Id}.");
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

    // Slash command preparation and invocation
    private async Task DiscordClient_InteractionCreated(SocketInteraction arg) {
        var context = new SocketInteractionContext(DiscordClient, arg);

        try {
            await _interactionService.ExecuteCommandAsync(context, _services).ConfigureAwait(false);
        } catch (Exception e) {
            Log(nameof(DiscordClient_InteractionCreated), $"Unhandled exception. {e}");
            // TODO when implementing proper application error logging, see here
            if (arg.Type == InteractionType.ApplicationCommand) {
                if (arg.HasResponded) await arg.ModifyOriginalResponseAsync(prop => prop.Content = InternalError);
                else await arg.RespondAsync(InternalError);
            }
        }
    }

    // Slash command logging and failed execution handling
    private async Task InteractionService_SlashCommandExecuted(SlashCommandInfo info, IInteractionContext context, IResult result) {
        string sender;
        if (context.Guild != null) {
            sender = $"{context.Guild}!{context.User}";
        } else {
            sender = $"{context.User} in non-guild context";
        }
        var logresult = $"{(result.IsSuccess ? "Success" : "Fail")}: `/{info}` by {sender}.";

        if (result.Error != null) {
            // Additional log information with error detail
            logresult += " " + Enum.GetName(typeof(InteractionCommandError), result.Error) + ": " + result.ErrorReason;

            // Specific responses to errors, if necessary
            if (result.Error == InteractionCommandError.UnmetPrecondition) {
                string errReply = result.ErrorReason switch {
                    RequireBotModeratorAttribute.Error => RequireBotModeratorAttribute.Reply,
                    EnforceBlockingAttribute.FailBlocked => EnforceBlockingAttribute.ReplyBlocked,
                    EnforceBlockingAttribute.FailModerated => EnforceBlockingAttribute.ReplyModerated,
                    RequireGuildContextAttribute.Error => RequireGuildContextAttribute.Reply,
                    _ => result.ErrorReason
                };
                await context.Interaction.RespondAsync(errReply, ephemeral: true).ConfigureAwait(false);
            } else {
                // Generic error response
                // TODO when implementing proper application error logging, see here
                var ia = context.Interaction;
                if (ia.HasResponded) await ia.ModifyOriginalResponseAsync(p => p.Content = InternalError).ConfigureAwait(false);
                else await ia.RespondAsync(InternalError).ConfigureAwait(false);
            }
        }

        Log("Command", logresult);
    }
}
