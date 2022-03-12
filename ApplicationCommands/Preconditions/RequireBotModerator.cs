using BirthdayBot.Data;
using Discord.Interactions;

namespace BirthdayBot.ApplicationCommands;

/// <summary>
/// Precondition requiring the executing user be recognized as a bot moderator.<br/>
/// A bot moderator has either the Manage Server permission or is a member of the designated bot moderator role.
/// </summary>
class RequireBotModeratorAttribute : PreconditionAttribute {
    public const string Error = "User did not pass the mod check.";
    public const string Reply = ":x: You must be a moderator to use this command.";

    public override string ErrorMessage => Error;

    public override async Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services) {
        // A bot moderator can only exist in a guild context, so we must do this check.
        // This check causes this precondition to become a functional equivalent to RequireGuildContextAttribute...
        if (context.User is not SocketGuildUser user) return PreconditionResult.FromError(RequireGuildContextAttribute.Error);

        if (user.GuildPermissions.ManageGuild) return PreconditionResult.FromSuccess();
        var gconf = await ((SocketGuild)context.Guild).GetConfigAsync().ConfigureAwait(false);
        if (gconf.ModeratorRole.HasValue && user.Roles.Any(r => r.Id == gconf.ModeratorRole.Value))
            return PreconditionResult.FromSuccess();

        return PreconditionResult.FromError(Error);
    }
}