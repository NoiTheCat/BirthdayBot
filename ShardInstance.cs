using BirthdayBot.ApplicationCommands;
using BirthdayBot.BackgroundServices;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace BirthdayBot;
/// <summary>
/// Single shard instance for Birthday Bot. This shard independently handles all input and output to Discord.
/// </summary>
public sealed class ShardInstance : IDisposable {
    private readonly ShardManager _manager;
    private readonly ShardBackgroundWorker _background;
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
    internal ShardInstance(ShardManager manager, IServiceProvider services) {
        _manager = manager;
        _services = services;

        DiscordClient = _services.GetRequiredService<DiscordSocketClient>();
        DiscordClient.Log += Client_Log;
        DiscordClient.Ready += Client_Ready;

        _interactionService = _services.GetRequiredService<InteractionService>();
        DiscordClient.InteractionCreated += DiscordClient_InteractionCreated;
        _interactionService.SlashCommandExecuted += InteractionService_SlashCommandExecuted;
        DiscordClient.ModalSubmitted += modal => { return ModalResponder.DiscordClient_ModalSubmitted(this, modal); };

        // Background task constructor begins background processing immediately.
        _background = new ShardBackgroundWorker(this);
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
    }

    internal void Log(string source, string message) => Program.Log($"Shard {ShardId:00}] [{source}", message);

    private Task Client_Log(LogMessage arg) {
        // Suppress certain messages
        if (arg.Message != null) {
            if (!_manager.Config.LogConnectionStatus) {
                switch (arg.Message) {
                    case "Connecting":
                    case "Connected":
                    case "Ready":
                    case "Disconnecting":
                    case "Disconnected":
                    case "Resumed previous session":
                    case "Failed to resume previous session":
                    case "Serializer Error": // The exception associated with this log appears a lot as of v3.2-ish
                        return Task.CompletedTask;
                }
            }
            Log("Discord.Net", $"{arg.Severity}: {arg.Message}");
        }

        if (arg.Exception != null) {
            if (!_manager.Config.LogConnectionStatus) {
                if (arg.Exception is GatewayReconnectException || arg.Exception.Message == "WebSocket connection was closed")
                    return Task.CompletedTask;
            }

            Log("Discord.Net exception", $"{arg.Exception.GetType().FullName}: {arg.Exception.Message}");
        }

        return Task.CompletedTask;
    }

    private async Task Client_Ready() {
#if !DEBUG
        // Update slash/interaction commands
        if (ShardId == 0) {
            await _interactionService.RegisterCommandsGloballyAsync(true);
            Log(nameof(ShardInstance), "Updated global command registration.");
        }
#else
        // Debug: Register our commands locally instead, in each guild we're in
        if (DiscordClient.Guilds.Count > 5) {
            Program.Log(nameof(ShardInstance), "Are you debugging in production?! Skipping DEBUG command registration.");
            return;
        } else {
            foreach (var g in DiscordClient.Guilds) {
                await _interactionService.RegisterCommandsToGuildAsync(g.Id, true).ConfigureAwait(false);
                Log(nameof(ShardInstance), $"Updated DEBUG command registration in guild {g.Id}.");
            }
        }
#endif
    }

    // Slash command preparation and invocation
    private async Task DiscordClient_InteractionCreated(SocketInteraction arg) {
        var context = new SocketInteractionContext(DiscordClient, arg);

        try {
            await _interactionService.ExecuteCommandAsync(context, _services).ConfigureAwait(false);
        } catch (Exception e) {
            Log(nameof(DiscordClient_InteractionCreated), $"Unhandled exception. {e}");
            if (arg.Type == InteractionType.ApplicationCommand) {
                if (arg.HasResponded) await arg.ModifyOriginalResponseAsync(prop => prop.Content = InternalError);
                else await arg.RespondAsync(InternalError);
            }
        }
    }

    // Slash command logging and failed execution handling
    private Task InteractionService_SlashCommandExecuted(SlashCommandInfo info, IInteractionContext context, IResult result) {
        string sender;
        if (context.Guild != null) sender = $"{context.Guild}!{context.User}";
        else sender = $"{context.User} in non-guild context";
        var logresult = $"{(result.IsSuccess ? "Success" : "Fail")}: `/{info}` by {sender}.";

        if (result.Error != null) {
            // Additional log information with error detail
            logresult += " " + Enum.GetName(typeof(InteractionCommandError), result.Error) + ": " + result.ErrorReason;
        }

        Log("Command", logresult);
        return Task.CompletedTask;
    }
}
