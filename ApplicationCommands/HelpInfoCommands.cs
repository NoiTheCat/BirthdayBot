using BirthdayBot.Data;

namespace BirthdayBot.ApplicationCommands;

internal class HelpInfoCommands : BotApplicationCommand {
    private static readonly ApplicationCommandProperties[] _commands;

    static HelpInfoCommands() {
        _commands = new ApplicationCommandProperties[] {
            new SlashCommandBuilder()
                .WithName("help").WithDescription("attempts to get help").Build(),
            new SlashCommandBuilder()
                .WithName("not-help").WithDescription("you're not getting help here").Build()
        };
    }

    public override IEnumerable<ApplicationCommandProperties> GetCommands() {
        return _commands;
    }
    public override CommandResponder? GetHandlerFor(string commandName) {
        switch (commandName) {
            case "help":
                return CmdHelp;
            default:
                return null;
        }
    }

    private async Task CmdHelp(ShardInstance instance, GuildConfiguration gconf, SocketSlashCommand arg) {
        await arg.RespondAsync("i am help. is this help?");
    }
}
