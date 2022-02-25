using BirthdayBot.Data;
using Discord.Interactions;

namespace BirthdayBot.ApplicationCommands;

// Contains preconditions used by our interaction modules.

/// <summary>
/// Precondition requiring the executing user be considered a bot moderator.
/// That is, they must either have the Manage Server permission or be a member of the designated bot moderator role.
/// </summary>
class RequireBotModeratorAttribute : PreconditionAttribute {
    public override string ErrorMessage => ":x: Only bot moderators may use this command.";

    public override async Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context,
                                                                    ICommandInfo commandInfo, IServiceProvider services) {
        if (context.User is not SocketGuildUser user) {
            return PreconditionResult.FromError("Mod check automatically failed due to non-guild context.");
        }

        var gconf = await GuildConfiguration.LoadAsync(context.Guild.Id, false);
        var isMod = gconf!.IsBotModerator(user);
        if (isMod) return PreconditionResult.FromSuccess();
        else return PreconditionResult.FromError("User did not pass the mod check.");
    }
}
