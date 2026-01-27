using Discord;
using Discord.Interactions;
using NoiPublicBot;

namespace WorldTime.InteractionModules;

public class HelpCommand : WTModuleBase {
    internal const string HelpHelp = "Displays a list of available bot commands.";
    internal const string HelpList = "Shows the current time for all recently active known users.";
    internal const string HelpSet = "Adds or updates your time zone to the bot.";
    internal const string HelpRemove = "Removes your time zone information from this bot.";

    [SlashCommand("help", HelpHelp)]
    [CommandContextType(InteractionContextType.Guild, InteractionContextType.BotDm)]
    public async Task CmdHelp() {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
        await RespondAsync(embed: new EmbedBuilder() {
            Title = "Help & About",
            Description =
                $"World Time v{version}\n"
                + $"-# Shard {Shard.ShardId:00} - {Instance.BotUptime}\n\n"
                + "This bot is provided for free, without any paywalled 'premium' features. "
                + "If you've found this bot useful, please consider contributing via the "
                + "bot author's page on Ko-fi: https://ko-fi.com/noithecat.",
            Footer = new EmbedFooterBuilder() {
                IconUrl = Context.Client.CurrentUser.GetAvatarUrl(),
                Text = "World Time"
            }
        }.AddField(inline: false, name: "Commands", value:
            $"""
            `/help` - {HelpHelp}
            `/list` - {HelpList}
            `/set` - {HelpSet}
            `/remove` - {HelpRemove}
            """
        ).AddField(inline: false, name: "Admin commands", value:
            $"""
            `/config use-12hour` - {ConfigCommands.HelpUse12}
            `/config private-confirms` - {ConfigCommands.HelpPrivateConfirms}
            `/set-for` - {ConfigCommands.HelpSetFor}
            `/remove-for` - {ConfigCommands.HelpRemoveFor}
            """
        ).AddField(inline: false, name: "Zones", value:
            "This bot accepts zone names from the IANA Time Zone Database (a.k.a. Olson Database). " +
            "A useful tool to determine yours can be found at: https://zones.arilyn.cc/"
        ).Build());
    }
}
