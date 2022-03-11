using BirthdayBot.Data;
using Discord.Interactions;

namespace BirthdayBot.ApplicationCommands;

// Contains preconditions used by our interaction modules.

/// <summary>
/// Precondition requiring the executing user be considered a bot moderator.
/// That is, they must either have the Manage Server permission or be a member of the designated bot moderator role.
/// </summary>
class RequireBotModeratorAttribute : PreconditionAttribute {
    public const string FailMsg = "User did not pass the mod check.";
    public const string Reply = ":x: You must be a moderator to use this command.";

    public override string ErrorMessage => FailMsg;

    public override async Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services) {
        if (context.User is not SocketGuildUser user) {
            return PreconditionResult.FromError("Failed due to non-guild context.");
        }

        if (user.GuildPermissions.ManageGuild) return PreconditionResult.FromSuccess();
        var gconf = await ((SocketGuild)context.Guild).GetConfigAsync().ConfigureAwait(false);
        if (gconf.ModeratorRole.HasValue && user.Roles.Any(r => r.Id == gconf.ModeratorRole.Value))
            return PreconditionResult.FromSuccess();

        return PreconditionResult.FromError(FailMsg);
    }
}
