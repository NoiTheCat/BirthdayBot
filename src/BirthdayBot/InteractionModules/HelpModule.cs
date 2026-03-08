using Discord;
using Discord.Interactions;
using NoiPublicBot;
using static BirthdayBot.Localization.CommandsEnUS.Help;

namespace BirthdayBot.InteractionModules;

[CommandContextType(InteractionContextType.Guild, InteractionContextType.BotDm)]
public class HelpModule : BBModuleBase {
    private const string TopMessage =
        "Thank you for using Birthday Bot!\n" +
        "Support, data policy, more info: https://noithecat.dev/bots/BirthdayBot\n\n" +
        "This bot is provided for free, without any paywalls or exclusive paid features. If this bot has been useful to you, " +
        "please consider making a small contribution via the author's Ko-fi: https://ko-fi.com/noithecat.";
    private const string RegularCommandsField =
        $"`/birthday` - {BirthdayModule.HelpCmdBirthday}\n" +
        $"` ⤷get` - {BirthdayModule.HelpCmdGet}\n" +
        $"` ⤷show-nearest` - {BirthdayModule.HelpCmdNearest}\n" +
        $"` ⤷set date` - {BirthdayModule.HelpCmdSetDate}\n" +
        $"` ⤷set timezone` - {BirthdayModule.HelpCmdSetZone}\n" +
        $"` ⤷remove` - {BirthdayModule.HelpCmdRemove}";
    private const string ModCommandsField =
        $"`/config` - {ConfigModule.HelpCmdConfig}\n" +
        $"` ⤷add-only` - {ConfigModule.HelpAddOnly}\n" +
        $"` ⤷check` - {ConfigModule.HelpCmdCheck}\n" +
        $"` ⤷announce` - {ConfigModule.HelpCmdAnnounce}\n" +
        $"`  ⤷` See also: `/config announce help`.\n" +
        $"` ⤷birthday-role` - {ConfigModule.HelpCmdBirthdayRole}\n" +
        $"` ⤷private-confirms` - {ConfigModule.HelpPrivateConfirms}\n" +
        $"`/export-birthdays` - {ExportModule.HelpCmdExport}\n" +
        $"`/override` - {BirthdayOverrideModule.HelpCmdOverride}\n" +
        $"` ⤷set-birthday`, `⤷set-timezone`, `⤷remove`\n" +
        "**Caution:** Skipping optional parameters __removes__ their configuration.";

    [SlashCommand(Name, Description)]
    public async Task CmdHelp() {
        const string DMWarn = "Please note that this bot works in servers only. " +
            "The bot will not respond to any other commands within a DM.";
#if DEBUG
        var ver = "DEBUG flag set";
#else
        var ver = "v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
#endif
        var result = new EmbedBuilder()
            .WithAuthor("Help & About")
            .WithFooter($"Birthday Bot {ver} - Shard {Shard.ShardId:00} up {Instance.BotUptime}",
                Context.Client.CurrentUser.GetAvatarUrl())
            .WithDescription(TopMessage)
            .AddField("Commands", RegularCommandsField)
            .AddField("Moderator commands", ModCommandsField)
            .Build();
        await RespondAsync(text: Context.Channel is IDMChannel ? DMWarn : null, embed: result).ConfigureAwait(false);
    }
}
