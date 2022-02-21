using BirthdayBot.Data;

namespace BirthdayBot.ApplicationCommands;

internal class HelpCommands : BotApplicationCommand {
    private static readonly EmbedFieldBuilder _helpEmbedRegCommandsField;
    private static readonly EmbedFieldBuilder _helpEmbedModCommandsField;

    static HelpCommands() {
        _helpEmbedRegCommandsField = new EmbedFieldBuilder() {
            Name = "Commands",
            Value = $"`/set-birthday` - {RegistrationCommands.HelpSet}\n" +
                $"`/set-timezone` - {RegistrationCommands.HelpZone}\n" +
                $"`/remove-timezone` - {RegistrationCommands.HelpZoneDel}\n" +
                $"`/remove-birthday` - {RegistrationCommands.HelpDel}\n" +
                $"`/birthday` - {QueryCommands.HelpBirthdayFor}\n" +
                $"`/recent`, `/upcoming` - {QueryCommands.HelpRecentUpcoming}"
                
        };
        _helpEmbedModCommandsField = new EmbedFieldBuilder() {
            Name = "Moderator commands",
            Value =
                $"`/config` - {ModCommands.HelpConfig}\n" +
                $"`/announce` - {ModCommands.HelpConfAnnounce}\n" +
                $"`/blocking` - {ModCommands.HelpConfBlocking}\n" +
                $"`/list-all` - {QueryCommands.HelpListAll}\n" +
                $"`/override` - {RegistrationOverrideCommands.HelpOverride}\n" +
                $"See also: `/config help`, `/announce help`, `/blocking help`."
        };
    }

    public override IEnumerable<ApplicationCommandProperties> GetCommands() => new ApplicationCommandProperties[] {
            new SlashCommandBuilder()
                .WithName("help").WithDescription("Show an overview of available commands.").Build()
        };
    public override CommandResponder? GetHandlerFor(string commandName) => commandName switch {
        "help" => CmdHelp,
        _ => null,
    };

    private async Task CmdHelp(ShardInstance instance, GuildConfiguration gconf, SocketSlashCommand arg) {
        string ver =
#if DEBUG
            "DEBUG flag set";
#else
            "v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
#endif
        var result = new EmbedBuilder()
            .WithAuthor("Help & About")
            .WithFooter($"Birthday Bot {ver} - Shard {instance.ShardId:00} up {Program.BotUptime}",
                instance.DiscordClient.CurrentUser.GetAvatarUrl())
            .WithDescription("Thank you for using Birthday Bot!\n" +
                "Support, data policy, etc: https://noithecat.dev/bots/BirthdayBot\n" +
                "This bot is provided for free, without any paywalls or exclusive paid features. If this bot has been useful to you, " +
                "please consider taking a look at the author's Ko-fi: https://ko-fi.com/noithecat.")
            .AddField(_helpEmbedRegCommandsField)
            .AddField(_helpEmbedModCommandsField)
            .Build();
        await arg.RespondAsync(embed: result);
    }
}
