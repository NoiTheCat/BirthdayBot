namespace BirthdayBot.ApplicationCommands;
/// <summary>
/// An instance-less class meant to handle incoming submitted modals.
/// </summary>
static class ModalResponder {
    private delegate Task Responder(SocketModal modal, SocketGuildChannel channel,
                                    Dictionary<string, SocketMessageComponentData> data);

    internal static async Task DiscordClient_ModalSubmitted(ShardInstance inst, SocketModal arg) {
        Responder handler = arg.Data.CustomId switch {
            ConfigModule.SubCmdsConfigAnnounce.ModalCidAnnounce => ConfigModule.SubCmdsConfigAnnounce.CmdSetMessageResponse,
            _ => DefaultHandler
        };

        var data = arg.Data.Components.ToDictionary(k => k.CustomId);

        if (arg.Channel is not SocketGuildChannel channel) {
            inst.Log(nameof(ModalResponder), $"Modal of type `{arg.Data.CustomId}` but channel data unavailable. " +
                $"Sender ID {arg.User.Id}, name {arg.User}.");
            await arg.RespondAsync(":x: Invalid request. Are you trying this command from a channel the bot can't see?")
                .ConfigureAwait(false);
            return;
        }

        try {
            inst.Log(nameof(ModalResponder), $"Modal of type `{arg.Data.CustomId}` at {channel.Guild}!{arg.User}.");
            await handler(arg, channel, data).ConfigureAwait(false);
        } catch (Exception e) {
            inst.Log(nameof(ModalResponder), $"Unhandled exception. {e}");
            await arg.RespondAsync(ShardInstance.InternalError);
        }
    }

    private static async Task DefaultHandler(SocketModal modal, SocketGuildChannel channel,
                                             Dictionary<string, SocketMessageComponentData> data)
        => await modal.RespondAsync(":x: ...???");
}
