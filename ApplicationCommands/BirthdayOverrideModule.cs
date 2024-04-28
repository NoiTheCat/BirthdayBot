﻿using BirthdayBot.Data;
using Discord.Interactions;
using static BirthdayBot.Common;

namespace BirthdayBot.ApplicationCommands;
[Group("override", HelpCmdOverride)]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[EnabledInDm(false)]
public class BirthdayOverrideModule : BotModuleBase {
    public const string HelpCmdOverride = "Commands to set options for other users.";
    const string HelpOptOvTarget = "The user whose data to modify.";

    // Note that these methods have largely been copied from BirthdayModule. Changes there should be reflected here as needed.
    // TODO possible to use a common base class for shared functionality instead?

    [SlashCommand("set-birthday", "Set a user's birthday on their behalf.")]
    public async Task OvSetBirthday([Summary(description: HelpOptOvTarget)] SocketGuildUser target,
                                    [Summary(description: HelpOptDate)] string date) {
        int inmonth, inday;
        try {
            (inmonth, inday) = ParseDate(date);
        } catch (FormatException e) {
            // Our parse method's FormatException has its message to send out to Discord.
            await RespondAsync(e.Message, ephemeral: true).ConfigureAwait(false);
            return;
        }

        using var db = new BotDatabaseContext();
        var user = target.GetUserEntryOrNew(db);
        if (user.IsNew) db.UserEntries.Add(user);
        user.BirthMonth = inmonth;
        user.BirthDay = inday;
        try {
            await db.SaveChangesAsync();
        } catch (Microsoft.EntityFrameworkCore.DbUpdateException e)
            when (e.InnerException is Npgsql.PostgresException ex && ex.SqlState == Npgsql.PostgresErrorCodes.ForeignKeyViolation) {
            await RespondAsync(BirthdayModule.ErrNotSetFk);
            return;
        }

        await RespondAsync($":white_check_mark: {FormatName(target, false)}'s birthday has been set to " +
            $"**{FormatDate(inmonth, inday)}**.").ConfigureAwait(false);
    }

    [SlashCommand("set-timezone", "Set a user's time zone on their behalf.")]
    public async Task OvSetTimezone([Summary(description: HelpOptOvTarget)] SocketGuildUser target,
                                    [Summary(description: HelpOptZone)] string zone) {
        using var db = new BotDatabaseContext();

        var user = target.GetUserEntryOrNew(db);
        if (user.IsNew) {
            await RespondAsync($":x: {FormatName(target, false)} does not have a birthday set.")
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
        await db.SaveChangesAsync();
        await RespondAsync($":white_check_mark: {FormatName(target, false)}'s time zone has been set to " +
            $"**{newzone}**.").ConfigureAwait(false);
    }

    [SlashCommand("remove-birthday", "Remove a user's birthday information on their behalf.")]
    public async Task OvRemove([Summary(description: HelpOptOvTarget)] SocketGuildUser target) {
        using var db = new BotDatabaseContext();
        var user = target.GetUserEntryOrNew(db);
        if (!user.IsNew) {
            db.UserEntries.Remove(user);
            await db.SaveChangesAsync();
            await RespondAsync($":white_check_mark: {FormatName(target, false)}'s birthday in this server has been removed.")
                .ConfigureAwait(false);
        } else {
            await RespondAsync($":white_check_mark: {FormatName(target, false)}'s birthday is not registered.")
                .ConfigureAwait(false);
        }
    }
}
