using BirthdayBot.Data;
using Discord.Interactions;

namespace BirthdayBot.ApplicationCommands;

[RequireContext(ContextType.Guild)]
[RequireBotModerator]
[Group("override", HelpCmdOverride)]
public class BirthdayOverrideModule : BotModuleBase {
    public const string HelpCmdOverride = "Commands to set options for other users.";
    const string HelpOptOvTarget = "The user whose data to modify.";

    // Note that these methods have largely been copied from BirthdayModule. Changes there should be reflected here as needed.
    // TODO possible to use a common base class for shared functionality instead?

    [SlashCommand("set-birthday", HelpPfxModOnly + "Set a user's birthday on their behalf.")]
    public async Task OvSetBirthday([Summary(description: HelpOptOvTarget)]SocketGuildUser target,
                                    [Summary(description: HelpOptDate)]string date) {
        int inmonth, inday;
        try {
            (inmonth, inday) = ParseDate(date);
        } catch (FormatException e) {
            // Our parse method's FormatException has its message to send out to Discord.
            await RespondAsync(e.Message, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var user = await target.GetConfigAsync().ConfigureAwait(false);
        await user.UpdateAsync(inmonth, inday, user.TimeZone).ConfigureAwait(false);

        await RespondAsync($":white_check_mark: {Common.FormatName(target, false)}'s birthday has been set to " +
            $"**{FormatDate(inmonth, inday)}**.").ConfigureAwait(false);
    }

    [SlashCommand("set-timezone", HelpPfxModOnly + "Set a user's time zone on their behalf.")]
    public async Task OvSetTimezone([Summary(description: HelpOptOvTarget)]SocketGuildUser target,
                                    [Summary(description: HelpOptZone)]string zone) {
        var user = await target.GetConfigAsync().ConfigureAwait(false);
        if (!user.IsKnown) {
            await RespondAsync($":x: {Common.FormatName(target, false)} does not have a birthday set.")
                .ConfigureAwait(false);
            return;
        }

        string inzone;
        try {
            inzone = ParseTimeZone(zone);
        } catch (FormatException e) {
            await RespondAsync(e.Message, ephemeral: true).ConfigureAwait(false);
            return;
        }
        await user.UpdateAsync(user.BirthMonth, user.BirthDay, inzone).ConfigureAwait(false);
        await RespondAsync($":white_check_mark: {Common.FormatName(target, false)}'s time zone has been set to " +
            $"**{inzone}**.").ConfigureAwait(false);
    }

    [SlashCommand("remove-birthday", HelpPfxModOnly + "Remove a user's birthday information on their behalf.")]
    public async Task OvRemove([Summary(description: HelpOptOvTarget)]SocketGuildUser target) {
        var user = await target.GetConfigAsync().ConfigureAwait(false);
        if (user.IsKnown) {
            await user.DeleteAsync().ConfigureAwait(false);
            await RespondAsync($":white_check_mark: {Common.FormatName(target, false)}'s birthday in this server has been removed.")
                .ConfigureAwait(false);
        } else {
            await RespondAsync($":white_check_mark: {Common.FormatName(target, false)}'s birthday is not registered.")
                .ConfigureAwait(false);
        }
    }
}
