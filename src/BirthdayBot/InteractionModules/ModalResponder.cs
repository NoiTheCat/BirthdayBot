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
        var log = inst.Log.ForContext("Source", nameof(ModalResponder));
        Responder handler = arg.Data.CustomId switch {
            ConfigModule.SubCmdsConfigAnnounce.ModFormidAnnounce => ConfigModule.SubCmdsConfigAnnounce.CmdSetMessageResponse,
            _ => DefaultHandler
        };

        var data = arg.Data.Components.ToDictionary(k => k.CustomId);

        if (arg.Channel is not SocketGuildChannel channel) {
            log.Warning("Got {ModalId}, but channel data unavailable. Guild: {GuildId} User: {UserId}",
                arg.Data.CustomId, arg.GuildId, arg.User.Id);
            await arg.RespondAsync(Responses[arg.GuildLocale]["errGeneric"]).ConfigureAwait(false);
            return;
        }

        try {
            log.Information("Received {ModalId} at {GuildId}!{UserId}.", arg.Data.CustomId, arg.GuildId, arg.User.Id);
            await handler(arg, channel, data).ConfigureAwait(false);
        } catch (Exception e) {
            log.Error(e, "Modal handler threw an exception.");
            await arg.RespondAsync(Responses[arg.GuildLocale]["errGeneric"]).ConfigureAwait(false);
        }
    }

    private static async Task DefaultHandler(SocketModal modal, SocketGuildChannel channel,
                                             Dictionary<string, SocketMessageComponentData> data)
        => await modal.RespondAsync(Responses[modal.GuildLocale]["errGeneric"]).ConfigureAwait(false);
}
