using BirthdayBot.Data;
using Discord.Interactions;
using static BirthdayBot.Common;

namespace BirthdayBot.ApplicationCommands;
[Group("override", HelpCmdOverride)]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[CommandContextType(InteractionContextType.Guild)]
public class BirthdayOverrideModule : BotModuleBase {
    public const string HelpCmdOverride = "Commands to set options for other users.";
    const string HelpOptOvTarget = "The user whose data to modify.";

    [SlashCommand("set-birthday", "Set a user's birthday on their behalf.")]
    public async Task OvSetBirthday([Summary(description: HelpOptOvTarget)] SocketGuildUser target,
                                    [Summary(description: HelpOptDate)] string date) {
        // IMPORTANT: If editing here, reflect changes as needed in BirthdayModule.
        var guild = ((SocketTextChannel)Context.Channel).Guild.GetConfigOrNew(DbContext);
        if (guild.IsNew) DbContext.GuildConfigurations.Add(guild); // Satisfy foreign key constraint
        var user = target.GetUserEntryOrNew(DbContext);
        if (user.IsNew) DbContext.UserEntries.Add(user);

        int inmonth, inday;
        try {
            (inmonth, inday) = ParseDate(date);
        } catch (FormatException e) {
            // Our parse method's FormatException has its message to send out to Discord.
            await RespondAsync(e.Message, ephemeral: true).ConfigureAwait(false);
            return;
        }

        user.BirthMonth = inmonth;
        user.BirthDay = inday;
        await DbContext.SaveChangesAsync();

        await RespondAsync($":white_check_mark: {FormatName(target, false)}'s birthday has been set to " +
            $"**{FormatDate(inmonth, inday)}**.").ConfigureAwait(false);
    }

    [SlashCommand("set-timezone", "Set a user's time zone on their behalf.")]
    public async Task OvSetTimezone([Summary(description: HelpOptOvTarget)] SocketGuildUser target,
                                    [Summary(description: HelpOptZone), Autocomplete<TzAutocompleteHandler>] string zone) {
        var user = target.GetUserEntryOrNew(DbContext);
        if (user.IsNew) {
            await RespondAsync($":x: {FormatName(target, false)} must have a birthday set first.")
                .ConfigureAwait(false);
            return;
        }

        string newzone;
        try {
            newzone = ParseTimeZone(zone);
        } catch (FormatException e) {
            await RespondAsync(e.Message, ephemeral: true).ConfigureAwait(false);
            return;
        }
        user.TimeZone = newzone;
        await DbContext.SaveChangesAsync();
        await RespondAsync($":white_check_mark: {FormatName(target, false)}'s time zone has been set to " +
            $"**{newzone}**.").ConfigureAwait(false);
    }

    [SlashCommand("remove-birthday", "Remove a user's birthday information on their behalf.")]
    public async Task OvRemove([Summary(description: HelpOptOvTarget)] SocketGuildUser target) {
        var user = target.GetUserEntryOrNew(DbContext);
        if (!user.IsNew) {
            DbContext.UserEntries.Remove(user);
            await DbContext.SaveChangesAsync();
            await RespondAsync($":white_check_mark: {FormatName(target, false)}'s birthday in this server has been removed.")
                .ConfigureAwait(false);
        } else {
            await RespondAsync($":white_check_mark: {FormatName(target, false)}'s birthday is not registered.")
                .ConfigureAwait(false);
        }
    }
}
