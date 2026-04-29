using System.Text;
using BirthdayBot.Data;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using static BirthdayBot.Localization.CommandsEnUS.Birthday;

namespace BirthdayBot.InteractionModules;

[Group(Name, Description)]
[CommandContextType(InteractionContextType.Guild)]
public class BirthdayModule : BBModuleBase {
    [Group(Set.Name, Set.Description)]
    public class SubCmdsBirthdaySet : BBModuleBase {
        [SlashCommand(Set.Date.Name, Set.Date.Description)]
        public async Task CmdSetBday(
            [Summary(description: Set.Date.Day.Description)] string day,
            [Summary(description: Set.Date.Month.Description)] MonthName month,
            [Summary(description: Set.Date.Zone.Description), Autocomplete<TzAutocompleteHandler>] string? zone = null)
        {
            // IMPORTANT: If editing here, reflect changes as needed in BirthdayOverrideModule.

            // Add-only check
            var guild = ((SocketTextChannel)Context.Channel).Guild.GetConfigOrNew(DbContext);
            if (guild.IsNew) DbContext.GuildConfigurations.Add(guild); // Satisfy foreign key constraint
            var user = ((SocketGuildUser)Context.User).GetUserEntryOrNew(DbContext);
            if (user.IsNew) DbContext.UserEntries.Add(user);
            if (guild.AddOnly) {
                if (!user.IsNew) {
                    if (((SocketGuildUser)Context.User).GuildPermissions.ManageGuild) {
                        // Don't enforce if user has Manage Guild permission
                    } else {
                        await RespondAsync(LRu("birthday.errAddOnly"), ephemeral: true).ConfigureAwait(false);
                    }
                }
            }

            if (!TryParseDate(day, month, out var indate)) {
                await RespondAsync(LRu("errParseDate"), ephemeral: true).ConfigureAwait(false);
                return;
            }
            DateTimeZone? inzone = null;
            if (zone != null && !TryParseZone(zone, out inzone)) {
                await ReplyAsync(LRg("errParseZone")).ConfigureAwait(false);
                return;
            }

            user.BirthDate = indate.Value;
            user.TimeZone = inzone ?? user.TimeZone;
            user.LastProcessed = Instant.MinValue; // always reset on update
            await DbContext.SaveChangesAsync();

            var withZoneResponse = inzone != null ? LRg("birthday.set.date.withZone", inzone) : string.Empty;
            var response = LRg("birthday.set.date.success", DateFormat(indate.Value, GuildLocale), withZoneResponse);
            // TODO make hint configurable (on/off, default on)
            if (user.TimeZone == null) response += "\n" + LRg("birthday.set.date.tzHint");
            await RespondAsync(response, ephemeral: IsEphemeralSet()).ConfigureAwait(false);
        }

        [SlashCommand(Set.Timezone.Name, Set.Timezone.Description)]
        public async Task CmdSetZone(
            [Summary(description: Set.Timezone.Zone.Description), Autocomplete<TzAutocompleteHandler>] string zone)
        {
            var user = ((SocketGuildUser)Context.User).GetUserEntryOrNew(DbContext);
            if (user.IsNew) {
                await RespondAsync(LRg("birthday.set.zone.errNoBirthday"), ephemeral: true).ConfigureAwait(false);
                return;
            }

            if (Context.Guild.GetConfigOrNew(DbContext).AddOnly) {
                if (user.TimeZone is not null) {
                    if (((SocketGuildUser)Context.User).GuildPermissions.ManageGuild) {
                        // Don't enforce if user has Manage Guild permission
                    } else {
                        await RespondAsync(LRu("birthday.errAddOnly"), ephemeral: true).ConfigureAwait(false);
                        return;
                    }
                }
            }

            if (!TryParseZone(zone, out var newzone)) {
                await RespondAsync(LRu("errParseZone"), ephemeral: true).ConfigureAwait(false);
                return;
            }
            user.TimeZone = newzone;
            user.LastProcessed = Instant.MinValue; // always reset on update
            await DbContext.SaveChangesAsync();
            await RespondAsync(LRg("birthday.set.zone.success", newzone), ephemeral: IsEphemeralSet()).ConfigureAwait(false);
        }
    }

    [SlashCommand(Remove.Name, Remove.Description)]
    public async Task CmdRemove() {
        var query = await DbContext.UserEntries
            .Where(e => e.GuildId == Context.Guild.Id && e.UserId == Context.User.Id)
            .ExecuteDeleteAsync();
        if (query != 0) {
            await RespondAsync(LRg("birthday.remove.success")).ConfigureAwait(false);
        } else {
            await RespondAsync(LRg("birthday.remove.noData"), ephemeral: IsEphemeralSet()).ConfigureAwait(false);
        }
    }

    [SlashCommand(Get.Name, Get.Description)]
    public async Task CmdGetBday([Summary(description: Get.User.Description)] SocketGuildUser? user = null) {
        Cache.Update(user);

        var isSelf = user is null;
        if (isSelf) user = (SocketGuildUser)Context.User;

        var targetdata = user!.GetUserEntryOrNew(DbContext);

        if (targetdata.IsNew) {
            if (isSelf) await RespondAsync(LRg("birthday.get.noData1p"), ephemeral: true).ConfigureAwait(false);
            else await RespondAsync(LRg("birthday.get.noData3p"), ephemeral: true).ConfigureAwait(false);
            return;
        }

        await RespondAsync($"{Common.FormatName(user!, false)}: `{DateFormat(targetdata.BirthDate, GuildLocale, abbreviated: false)}`" +
            (targetdata.TimeZone == null ? string.Empty : $" - {targetdata.TimeZone}")).ConfigureAwait(false);
    }

    // "Recent and upcoming birthdays"
    // The 'recent' bit removes time zone ambiguity and spares us from extra time zone processing here
    // TODO stop being lazy
    [SlashCommand(ShowNearest.Name, ShowNearest.Description)]
    public async Task CmdShowNearest() {
        var deferred = await RefreshCacheAsync(CacheFilters.MissingWithinDays(15)).ConfigureAwait(false);

        var servertz = DbContext.GuildConfigurations.Where(c => c.GuildId == Context.Guild.Id).SingleOrDefault()?.GuildTimeZone;
        servertz ??= DateTimeZone.Utc;
        var today = SystemClock.Instance.GetCurrentInstant().InZone(servertz).LocalDateTime.Date;
        var search = new DateOnly(2000, today.Month, today.Day).DayOfYear - 8;
        if (search <= 0) search = 366 - Math.Abs(search);

        var query = GetAllKnownUsers(Context.Guild.Id);

        // TODO pagination instead of this workaround
        var useFollowup = false;
        // First output is shown as an interaction response, followed then as followup messages
        Task OutputAsync(string msg) {
            if (!useFollowup) {
                useFollowup = true;
                if (deferred) return ModifyOriginalResponseAsync(response => response.Content = msg);
                else return RespondAsync(msg);
            } else {
                return FollowupAsync(msg);
            }
        }

        var output = new StringBuilder();
        var resultCount = 0;
        output.AppendLine(LRg("birthday.nearest.header"));
        for (var count = 0; count <= 21; count++) { // cover 21 days total (7 prior, current day, 14 upcoming)
            // oh I guess we sort as we go. what was I thinking?
            var results = query.Where(i => i.BirthDate.DayOfYear == search);

            // push up search by 1 now, in case we back out early
            search += 1;
            if (search > 366) search = 1; // wrap to beginning of year

            if (!results.Any()) continue; // back out early
            resultCount += results.Count();

            // Build sorted name list
            var names = new List<string>();
            foreach (var item in results) {
                names.Add(item.DisplayName);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);

            var first = true;
            output.AppendLine();
            output.Append($"● `{DateFormat(results.First().BirthDate, GuildLocale, abbreviated: true)}`: ");
            foreach (var item in names) {
                // If the output is starting to fill up, send out this message and prepare a new one.
                if (output.Length > 800) {
                    await OutputAsync(output.ToString()).ConfigureAwait(false);
                    output.Clear();
                    first = true;
                    output.Append($"● `{DateFormat(results.First().BirthDate, GuildLocale, abbreviated: true)}`: ");
                }

                if (first) first = false;
                else output.Append(", ");
                output.Append(item);
            }
        }

        if (resultCount == 0)
            await OutputAsync(LRg("birthday.nearest.notFound")).ConfigureAwait(false);
        else
            // we fell through from the above loop
            await OutputAsync(output.ToString()).ConfigureAwait(false);
    }
}
