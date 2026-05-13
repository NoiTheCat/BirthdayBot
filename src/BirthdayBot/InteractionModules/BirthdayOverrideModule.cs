using BirthdayBot.Data;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using static BirthdayBot.Localization.CommandsEnUS.Override;

namespace BirthdayBot.InteractionModules;

[Group(Name, Description)]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[CommandContextType(InteractionContextType.Guild)]
public class BirthdayOverrideModule : BBModuleBase {
    [SlashCommand(SetBirthday.Name, SetBirthday.Description)]
    public async Task OvSetBirthday(
        [Summary(description: SetBirthday.Target.Description)] SocketGuildUser target,
        [Summary(description: SetBirthday.Day.Description)] string day,
        [Summary(description: SetBirthday.Month.Description)] MonthName month)
    {
        var cachedTarget = Cache.Update(target);
        // IMPORTANT: If editing here, reflect changes as needed in BirthdayModule.

        if (!TryParseDate(day, month, out var indate)) {
            await RespondAsync(LRu("errParseDate"), ephemeral: true).ConfigureAwait(false);
            return;
        }

        var guild = ((SocketTextChannel)Context.Channel).Guild.GetConfigOrNew(DbContext);
        if (guild.IsNew) DbContext.GuildConfigurations.Add(guild); // Satisfy foreign key constraint
        var user = target.GetUserEntryOrNew(DbContext);
        if (user.IsNew) DbContext.UserEntries.Add(user);
        user.BirthDate = indate.Value;
        user.LastProcessed = Instant.MinValue; // always reset on update
        await DbContext.SaveChangesAsync();

        await RespondAsync(LRg("override.bdaySuccess", cachedTarget.FormatName(), DateFormat(indate.Value, GuildLocale)))
            .ConfigureAwait(false);
    }

    [SlashCommand(SetTimezone.Name, SetTimezone.Description)]
    public async Task OvSetTimezone(
        [Summary(description: SetTimezone.Target.Description)] SocketGuildUser target,
        [Summary(description: SetTimezone.Zone.Description), Autocomplete<TzAutocompleteHandler>] string zone)
    {
        var cachedTarget = Cache.Update(target);

        var user = target.GetUserEntryOrNew(DbContext);
        if (user.IsNew) {
            await RespondAsync(LRg("override.tzNoBirthday", cachedTarget.FormatName())).ConfigureAwait(false);
            return;
        }

        if (!TryParseZone(zone, out var newzone)) {
            await RespondAsync(LRu("errParseZone"), ephemeral: true).ConfigureAwait(false);
            return;
        }
        user.TimeZone = newzone;
        user.LastProcessed = Instant.MinValue; // always reset on update
        await DbContext.SaveChangesAsync();
        await RespondAsync(LRg("override.tzSuccess", cachedTarget.FormatName(), newzone)).ConfigureAwait(false);
    }

    [SlashCommand(RemoveBirthday.Name, RemoveBirthday.Description)]
    public async Task OvRemove([Summary(description: RemoveBirthday.Target.Description)] SocketGuildUser target) {
        var cachedTarget = Cache.Update(target);

        var query = await DbContext.UserEntries
            .Where(e => e.GuildId == Context.Guild.Id && e.UserId == target.Id)
            .ExecuteDeleteAsync();
        if (query != 0) {
            await RespondAsync(LRg("override.delSuccess", cachedTarget.FormatName())).ConfigureAwait(false);
        } else {
            await RespondAsync(LRg("override.delNoBirthday", cachedTarget.FormatName())).ConfigureAwait(false);
        }
    }
}
