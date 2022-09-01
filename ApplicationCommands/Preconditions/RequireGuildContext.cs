using Discord.Interactions;

namespace BirthdayBot.ApplicationCommands;
/// <summary>
/// Implements the included precondition from Discord.Net, requiring a guild context while using our custom error message.<br/><br/>
/// Combining this with <see cref="RequireBotModeratorAttribute"/> is redundant. If possible, only use the latter instead.
/// </summary>
class RequireGuildContextAttribute : RequireContextAttribute {
    public const string Error = "Command not received within a guild context.";
    public const string Reply = ":x: This command is only available within a server.";

    public override string ErrorMessage => Error;

    public RequireGuildContextAttribute() : base(ContextType.Guild) { }
}