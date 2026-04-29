using Discord;
using Discord.Interactions;
using NoiPublicBot;
using static BirthdayBot.Localization.CommandsEnUS.Help;

namespace BirthdayBot.InteractionModules;

[CommandContextType(InteractionContextType.Guild, InteractionContextType.BotDm)]
public class HelpModule : BBModuleBase {
    private bool? _isGuild;

    // This is the only command that can be invoked outside a guild.
    // Need custom logic to determine whether to use guild or user-specific locale.
    private Func<string, string> LR {
        get {
            if (!_isGuild.HasValue) _isGuild = Context.Channel is not IDMChannel;
            return _isGuild.Value ? key => LRg(key) : key => LRu(key);
        }
    }
    private Func<string, string> LC {
        get {
            if (!_isGuild.HasValue) _isGuild = Context.Channel is not IDMChannel;
            return _isGuild.Value ? key => LCg(key) : key => LCu(key);
        }
    }

    [SlashCommand(Name, Description)]
    public async Task CmdHelp() {
#if DEBUG
        var ver = "I'm a Debug build";
#else
        var ver = "v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
#endif
        var (reg, mod) = BuildHelpMessage();
        var result = new EmbedBuilder()
            .WithAuthor(LR("help.headerMain"))
            .WithFooter($"Birthday Bot {ver} - Shard {Shard.ShardId:00} up {Instance.BotUptime}",
                Context.Client.CurrentUser.GetAvatarUrl())
            .WithDescription(LR("help.top"))
            .AddField(LR("help.headerRegCmds"), reg)
            .AddField(LR("help.headerModCmds"), mod)
            .Build();
        await RespondAsync(text: _isGuild!.Value ? null : LR("help.warnDM") , embed: result).ConfigureAwait(false);
    }

    private (string reg, string mod) BuildHelpMessage() {
        // Note: This may not work for more international groups...
        // TODO Find a way to grab the slash command name directly from Discord, place them here. Will need a different type of formatting
        var RegularCommandsField = $"""
            `/{LC("birthday.name")}` - {LC("birthday.description")}
            ` ⤷{LC("birthday.get.name")}` - {LC("birthday.get.description")}
            ` ⤷{LC("birthday.show-nearest.name")}` - {LC("birthday.show-nearest.description")}
            ` ⤷{LC("birthday.set.name")} {LC("birthday.set.date.name")}` - {LC("birthday.set.date.description")}
            ` ⤷{LC("birthday.set.name")} {LC("birthday.set.timezone.name")}` - {LC("birthday.set.timezone.description")}
            ` ⤷{LC("birthday.remove.name")}` - {LC("birthday.remove.description")}
            """;
        var ModCommandsField = $"""
            `/{LC("config.name")}` - {LC("config.description")}
            ` ⤷{LC("config.add-only.name")}` - {LC("config.add-only.description")}
            ` ⤷{LC("config.check.name")}` - {LC("config.check.description")}
            ` ⤷{LC("config.announce.name")}` - {LC("config.announce.description")}
            `  ⤷ `{LR("help.seeAlso")} `/{LC("config.name")} {LC("config.announce.name")} {LC("config.announce.help.name")}`.
            ` ⤷{LC("config.birthday-role.name")}` - {LC("config.birthday-role.description")}
            ` ⤷{LC("config.private-confirms.name")}` - {LC("config.private-confirms.description")}
            `/{LC("export-birthdays.name")}` - {LC("export-birthdays.description")}
            `/{LC("override.name")}` - {LC("override.description")}
            ` ⤷{LC("override.set-birthday.name")}`, `⤷{LC("override.set-timezone.name")}`, `⤷{LC("override.remove-birthday.name")}`
            {LR("help.warnEmptyParam")}
            """;
        return (RegularCommandsField, ModCommandsField);
    }
}
