using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using NoiPublicBot.Cache;

namespace WorldTime.InteractionModules;

[Group("config", "Configuration commands for World Time.")]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[CommandContextType(InteractionContextType.Guild)]
public class ConfigCommands : WTModuleBase {
    internal const string HelpUse12 = "Sets whether to use the 12-hour (AM/PM) format in time zone listings.";
    internal const string HelpSetFor = "Sets/updates time zone for a given user.";
    internal const string HelpRemoveFor = "Removes time zone for a given user.";
    internal const string HelpPrivateConfirms = "Sets whether to make set/update confirmations visible only to the user.";

    internal const string HelpBool = "True to enable, False to disable.";

    [SlashCommand("use-12hour", HelpUse12)]
    public async Task Cmd12Hour([Summary(description: HelpBool)] bool setting) {
        var gs = GetGuildConf(Context.Guild.Id);
        gs.Use12HourTime = setting;
        await DbContext.SaveChangesAsync().ConfigureAwait(false);
        await RespondAsync($":white_check_mark: Time listing set to **{(setting ? "AM/PM" : "24 hour")}** format.",
            ephemeral: gs.EphemeralConfirm).ConfigureAwait(false);
    }

    [SlashCommand("private-confirms", HelpPrivateConfirms)]
    public async Task PrivateConfirmations([Summary(description: HelpBool)] bool setting) {
        var gs = GetGuildConf(Context.Guild.Id);
        gs.EphemeralConfirm = setting;
        await DbContext.SaveChangesAsync().ConfigureAwait(false);
        await RespondAsync($":white_check_mark: Private confirmations **{(setting ? "enabled" : "disabled")}**.",
            ephemeral: false).ConfigureAwait(false); // Always show this confirmation despite setting
    }

    [SlashCommand("set-for", HelpSetFor)]
    public async Task CmdSetFor([Summary(description: "The user whose time zone to modify.")] SocketGuildUser user,
                                 [Summary(description: "The new time zone to set.")] string zone) {
        Cache.Update(UserInfo.CreateFrom(user));
        
        var newtz = ParseTimeZone(zone);
        if (newtz == null) {
            await RespondAsync(ErrInvalidZone, ephemeral: GetEphemeralConfirm()).ConfigureAwait(false);
            return;
        }

        await UpdateDbUserAsync(user, newtz).ConfigureAwait(false);
        await RespondAsync($":white_check_mark: Time zone for **{user}** set to **{newtz}**.").ConfigureAwait(false);
    }

    [SlashCommand("remove-for", HelpRemoveFor)]
    public async Task CmdRemoveFor([Summary(description: "The user whose time zone to remove.")] SocketGuildUser user) {
        Cache.Update(UserInfo.CreateFrom(user));
        
        if (await DeleteDbUserAsync(user).ConfigureAwait(false)) {
            await RespondAsync($":white_check_mark: Removed zone information for {user}.").ConfigureAwait(false);
        } else {
            await RespondAsync($":white_check_mark: No time zone is set for {user}.",
                ephemeral: GetEphemeralConfirm()).ConfigureAwait(false);
        }
    }
}
