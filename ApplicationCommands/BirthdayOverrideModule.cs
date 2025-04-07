using BirthdayBot.BackgroundServices;
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

    [SlashCommand("announce-birthday", "Immediately announce a user's birthday to the configured channel.")]
    public async Task OvAnnounceBirthday([Summary(description: HelpOptOvTarget)] SocketGuildUser target) {
        //verify the user actually has a birthday entry
        GuildConfig settings;
        using (var db = new BotDatabaseContext()) {
            var entry = target.GetUserEntryOrNew(db);
            if (entry.IsNew) {
                await RespondAsync(
                    $":x: {FormatName(target, false)} doesn’t have a birthday set.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            settings = await db.GuildConfigurations
                                .FindAsync(Context.Guild.Id)
                                .ConfigureAwait(false)
                                ?? throw new InvalidOperationException("No guild configuration found.");
        }

        try {
            await BirthdayRoleUpdate.AnnounceBirthdaysAsync(
                    settings, Context.Guild, [target])
                .ConfigureAwait(false);

            await RespondAsync(
                $":white_check_mark: Birthday announcement sent for " + $"{FormatName(target, false)}!",
                ephemeral: true).ConfigureAwait(false);
        } catch (Discord.Net.HttpException hex)
                when (hex.DiscordCode is DiscordErrorCode.MissingPermissions
                                    or DiscordErrorCode.InsufficientPermissions) {
            await RespondAsync(
                ":warning: I don’t have permission to send messages in the configured channel.",
                ephemeral: true).ConfigureAwait(false);
        }
        // Tell the invoker we're done
        await RespondAsync($":white_check_mark: Announced {FormatName(target, false)}'s birthday.", ephemeral: true)
            .ConfigureAwait(false);
    }
}
