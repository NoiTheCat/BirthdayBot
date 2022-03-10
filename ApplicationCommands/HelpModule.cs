using Discord.Interactions;

namespace BirthdayBot.ApplicationCommands;

public class HelpModule : BotModuleBase {
    private const string RegularCommandsField =
        $"`/birthday` - {BirthdayModule.HelpCmdBirthday}\n" +
        $"` ⤷get` - {BirthdayModule.HelpCmdGet}\n" +
        $"` ⤷show-nearest` - {BirthdayModule.HelpCmdNearest}\n" +
        $"` ⤷set date` - {BirthdayModule.HelpCmdSetDate}\n" +
        $"` ⤷set timezone` - {BirthdayModule.HelpCmdSetZone}\n" +
        $"` ⤷remove` - {BirthdayModule.HelpCmdRemove}";
    private const string ModCommandsField =
        $"`/birthday export` - {BirthdayModule.HelpCmdExport}\n" +
        $"`/config` - {ConfigModule.HelpCmdConfig}\n" +
        $"` ⤷check` - {ConfigModule.HelpCmdCheck}\n" +
        $"` ⤷announce` - {ConfigModule.HelpCmdAnnounce}\n" +
        $"`  ⤷` See also: `/config announce help`.\n" +
        $"` ⤷role` - {ConfigModule.HelpCmdRole}\n" +
        $"` ⤷set-birthday-role`, `⤷set-moderator-role`\n" +
        $"`/override` - {BirthdayOverrideModule.HelpCmdOverride}\n" +
        $"` ⤷set-birthday`, `⤷set-timezone`, `⤷remove`\n" +
        "**Caution:** Skipping optional parameters may __remove__ their configuration.";

    [SlashCommand("help", "Show an overview of available commands.")]
    public async Task CmdHelp() {
        const string DMWarn = "Please note that this bot works in servers only. " +
            "The bot will not respond to any of the following commands within a DM.";

        string ver =
#if DEBUG
            "DEBUG flag set";
#else
            "v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
#endif
        var result = new EmbedBuilder()
            .WithAuthor("Help & About")
            .WithFooter($"Birthday Bot {ver} - Shard {Shard.ShardId:00} up {Program.BotUptime}",
                Context.Client.CurrentUser.GetAvatarUrl())
            .WithDescription("Thank you for using Birthday Bot!\n" +
                "Support, data policy, more info: https://noithecat.dev/bots/BirthdayBot\n\n" +
                "This bot is provided for free, without any paywalls or exclusive paid features. If this bot has been useful to you, " +
                "please consider making a small contribution via the author's Ko-fi: https://ko-fi.com/noithecat.")
            .AddField("Commands", RegularCommandsField)
            .AddField("Moderator commands", ModCommandsField)
            .Build();
        await RespondAsync(text: (Context.Channel is IDMChannel ? DMWarn : null), embed: result).ConfigureAwait(false);
    }
}
