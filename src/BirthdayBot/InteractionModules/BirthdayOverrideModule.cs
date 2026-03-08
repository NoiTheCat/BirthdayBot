using BirthdayBot.Data;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using static BirthdayBot.Common;
using static BirthdayBot.Localization.CommandsEnUS.Override;

namespace BirthdayBot.InteractionModules;

[Group(Name, Description)]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[CommandContextType(InteractionContextType.Guild)]
public class BirthdayOverrideModule : BBModuleBase {
    public const string HelpCmdOverride = "Commands to set options for other users.";
    [SlashCommand(SetBirthday.Name, SetBirthday.Description)]
    public async Task OvSetBirthday(
        [Summary(description: SetBirthday.Target.Description)] SocketGuildUser target,
        [Summary(description: SetBirthday.Date.Description)] string date)
    {
        Cache.Update(target);

        // IMPORTANT: If editing here, reflect changes as needed in BirthdayModule.
        LocalDate indate;
        try {
            indate = ParseDate(date);
        } catch (FormatException e) {
            // Our parse method's FormatException has its message to send out to Discord.
            await RespondAsync(e.Message, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var guild = ((SocketTextChannel)Context.Channel).Guild.GetConfigOrNew(DbContext);
        if (guild.IsNew) DbContext.GuildConfigurations.Add(guild); // Satisfy foreign key constraint
        var user = target.GetUserEntryOrNew(DbContext);
        if (user.IsNew) DbContext.UserEntries.Add(user);
        user.BirthDate = indate;
        user.LastProcessed = Instant.MinValue; // always reset on update
        await DbContext.SaveChangesAsync();

        await RespondAsync($":white_check_mark: {FormatName(target, false)}'s birthday has been set to " +
            $"**{FormatDate(indate)}**.").ConfigureAwait(false);
    }

    [SlashCommand(SetTimezone.Name, SetTimezone.Description)]
    public async Task OvSetTimezone(
        [Summary(description: SetTimezone.Target.Description)] SocketGuildUser target,
        [Summary(description: SetTimezone.Zone.Description), Autocomplete<TzAutocompleteHandler>] string zone)
    {
        Cache.Update(target);
        var user = target.GetUserEntryOrNew(DbContext);
        if (user.IsNew) {
            await RespondAsync($":x: {FormatName(target, false)} must have a birthday set first.")
                .ConfigureAwait(false);
            return;
        }

        DateTimeZone newzone;
        try {
            newzone = ParseTimeZone(zone);
        } catch (FormatException e) {
            await RespondAsync(e.Message, ephemeral: true).ConfigureAwait(false);
            return;
        }
        user.TimeZone = newzone;
        user.LastProcessed = Instant.MinValue; // always reset on update
        await DbContext.SaveChangesAsync();
        await RespondAsync($":white_check_mark: {FormatName(target, false)}'s time zone has been set to " +
            $"**{newzone}**.").ConfigureAwait(false);
    }

    [SlashCommand(RemoveBirthday.Name, RemoveBirthday.Description)]
    public async Task OvRemove([Summary(description: RemoveBirthday.Target.Description)] SocketGuildUser target) {
        Cache.Update(target);

        var query = await DbContext.UserEntries
            .Where(e => e.GuildId == Context.Guild.Id && e.UserId == target.Id)
            .ExecuteDeleteAsync();
        if (query != 0) {
            await RespondAsync($":white_check_mark: {FormatName(target, false)}'s birthday in this server has been removed.")
                .ConfigureAwait(false);
        } else {
            await RespondAsync($":white_check_mark: {FormatName(target, false)}'s birthday is not registered.")
                .ConfigureAwait(false);
        }
    }
}
