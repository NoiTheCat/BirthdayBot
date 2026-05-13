using Discord.WebSocket;
using NoiPublicBot;
using static BirthdayBot.Localization.StringProviders;

namespace BirthdayBot.InteractionModules;

/// <summary>
/// An instanceless class meant to handle incoming submitted modals.
/// </summary>
static class ModalResponder {
    private delegate Task Responder(SocketModal modal, SocketGuildChannel channel,
                                    Dictionary<string, SocketMessageComponentData> data);

    internal static async Task DiscordClient_ModalSubmitted(ShardInstance inst, SocketModal arg) {
        Responder handler = arg.Data.CustomId switch {
            ConfigModule.SubCmdsConfigAnnounce.ModFormidAnnounce => ConfigModule.SubCmdsConfigAnnounce.CmdSetMessageResponse,
            _ => DefaultHandler
        };

        var data = arg.Data.Components.ToDictionary(k => k.CustomId);

        if (arg.Channel is not SocketGuildChannel channel) {
            Log($"Modal of type `{arg.Data.CustomId}` but channel data unavailable. Sender ID {arg.User.Id}, name {arg.User}.");
            await arg.RespondAsync(Responses[arg.GuildLocale]["errGeneric"])
                .ConfigureAwait(false);
            return;
        }

        try {
            Log($"Modal of type `{arg.Data.CustomId}` at {channel.Guild}!{arg.User}.");
            await handler(arg, channel, data).ConfigureAwait(false);
        } catch (Exception e) {
            Log( $"Unhandled exception. {e}");
            await arg.RespondAsync(Responses[arg.GuildLocale]["errGeneric"]);
        }
    }

    private static async Task DefaultHandler(SocketModal modal, SocketGuildChannel channel,
                                             Dictionary<string, SocketMessageComponentData> data)
        => await modal.RespondAsync(Responses[modal.GuildLocale]["errGeneric"]);

    private static void Log(string msg) {
        Instance.Log(nameof(ModalResponder), msg);
    }
}
