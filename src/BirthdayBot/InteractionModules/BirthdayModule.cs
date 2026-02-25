using System.Text;
using BirthdayBot.Data;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace BirthdayBot.InteractionModules;

[Group("birthday", HelpCmdBirthday)]
[CommandContextType(InteractionContextType.Guild)]
public class BirthdayModule : BBModuleBase {
    public const string HelpCmdBirthday = "Commands relating to birthdays.";
    public const string HelpCmdSetDate = "Sets or updates your birthday.";
    public const string HelpCmdSetZone = "Sets or updates your time zone if your birthday is already set.";
    public const string HelpCmdRemove = "Removes your birthday information from this bot.";
    public const string HelpCmdGet = "Gets a user's birthday.";
    public const string HelpCmdNearest = "Get a list of users who recently had or will have a birthday.";
    private const string ErrAddOnly = ":x: You may not edit a birthday or time zone after it has been added.";

    [Group("set", "Subcommands for setting birthday information.")]
    public class SubCmdsBirthdaySet : BBModuleBase {
        [SlashCommand("date", HelpCmdSetDate)]
        public async Task CmdSetBday([Summary(description: HelpOptDate)] string date,
                                     [Summary(description: HelpOptZone), Autocomplete<TzAutocompleteHandler>] string? zone = null) {
            // IMPORTANT: If editing here, reflect changes as needed in BirthdayOverrideModule.
            var guild = ((SocketTextChannel)Context.Channel).Guild.GetConfigOrNew(DbContext);
            if (guild.IsNew) DbContext.GuildConfigurations.Add(guild); // Satisfy foreign key constraint
            var user = ((SocketGuildUser)Context.User).GetUserEntryOrNew(DbContext);
            if (user.IsNew) DbContext.UserEntries.Add(user);

            if (guild.AddOnly) {
                if (!user.IsNew) {
                    if (((SocketGuildUser)Context.User).GuildPermissions.ManageGuild) {
                        // Don't enforce if user has Manage Guild permission
                    } else {
                        await RespondAsync(ErrAddOnly, ephemeral: true).ConfigureAwait(false);
                    }
                }
            }

            LocalDate indate;
            try {
                indate = ParseDate(date);
            } catch (FormatException e) {
                // Our parse method's FormatException has its message to send out to Discord.
                await RespondAsync(e.Message, ephemeral: true).ConfigureAwait(false);
                return;
            }

            DateTimeZone? inzone = null;
            if (zone != null) {
                try {
                    inzone = ParseTimeZone(zone);
                } catch (FormatException e) {
                    await ReplyAsync(e.Message).ConfigureAwait(false);
                    return;
                }
            }

            user.BirthDate = indate;
            user.TimeZone = inzone ?? user.TimeZone;
            user.LastProcessed = Instant.MinValue; // always reset on update
            await DbContext.SaveChangesAsync();

            var response = $":white_check_mark: Your birthday has been set to **{FormatDate(indate)}**";
            if (inzone != null) response += $" at time zone **{inzone}**";
            response += ".";
            if (user.TimeZone == null)
                response += "\n-# Tip: The `/birthday set timezone` command ensures your birthday is recognized on time!";
            await RespondAsync(response, ephemeral: IsEphemeralSet()).ConfigureAwait(false);
        }

        [SlashCommand("timezone", HelpCmdSetZone)]
        public async Task CmdSetZone([Summary(description: HelpOptZone), Autocomplete<TzAutocompleteHandler>] string zone) {
            var user = ((SocketGuildUser)Context.User).GetUserEntryOrNew(DbContext);
            if (user.IsNew) {
                await RespondAsync(":x: You must set a birthday first.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            if (Context.Guild.GetConfigOrNew(DbContext).AddOnly) {
                if (user.TimeZone is not null) {
                    if (((SocketGuildUser)Context.User).GuildPermissions.ManageGuild) {
                        // Don't enforce if user has Manage Guild permission
                    } else {
                        await RespondAsync(ErrAddOnly, ephemeral: true).ConfigureAwait(false);
                        return;
                    }
                }
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
            await RespondAsync($":white_check_mark: Your time zone has been set to **{newzone}**.",
                               ephemeral: IsEphemeralSet()).ConfigureAwait(false);
        }
    }

    [SlashCommand("remove", HelpCmdRemove)]
    public async Task CmdRemove() {
        var query = await DbContext.UserEntries
            .Where(e => e.GuildId == Context.Guild.Id && e.UserId == Context.User.Id)
            .ExecuteDeleteAsync();
        if (query != 0) {
            await RespondAsync(":white_check_mark: Your birthday in this server has been removed.");
        } else {
            await RespondAsync(":white_check_mark: Your birthday is not registered.", ephemeral: IsEphemeralSet())
                .ConfigureAwait(false);
        }
    }

    [SlashCommand("get", "Gets a user's birthday.")]
    public async Task CmdGetBday([Summary(description: "Optional: The user's birthday to look up.")] SocketGuildUser? user = null) {
        Cache.Update(user);

        var isSelf = user is null;
        if (isSelf) user = (SocketGuildUser)Context.User;

        var targetdata = user!.GetUserEntryOrNew(DbContext);

        if (targetdata.IsNew) {
            if (isSelf) await RespondAsync(":x: You do not have your birthday registered.", ephemeral: true).ConfigureAwait(false);
            else await RespondAsync(":x: The given user does not have their birthday registered.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await RespondAsync($"{Common.FormatName(user!, false)}: `{FormatDate(targetdata.BirthDate)}`" +
            (targetdata.TimeZone == null ? "" : $" - {targetdata.TimeZone}")).ConfigureAwait(false);
    }

    // "Recent and upcoming birthdays"
    // The 'recent' bit removes time zone ambiguity and spares us from extra time zone processing here
    // TODO stop being lazy
    [SlashCommand("show-nearest", HelpCmdNearest)]
    public async Task CmdShowNearest() {
        var deferred = await RefreshCacheAsync(Cache.FilterMissingWithinDays(15)).ConfigureAwait(false);

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
        output.AppendLine("Recent and upcoming birthdays:");
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
            output.Append($"● `{FormatDate(results.First().BirthDate)}`: ");
            foreach (var item in names) {
                // If the output is starting to fill up, send out this message and prepare a new one.
                if (output.Length > 800) {
                    await OutputAsync(output.ToString()).ConfigureAwait(false);
                    output.Clear();
                    first = true;
                    output.Append($"● `{FormatDate(results.First().BirthDate)}`: ");
                }

                if (first) first = false;
                else output.Append(", ");
                output.Append(item);
            }
        }

        if (resultCount == 0)
            await OutputAsync(
                "There are no recent or upcoming birthdays (within the last 7 days and/or next 14 days).")
                .ConfigureAwait(false);
        else
            await OutputAsync(output.ToString()).ConfigureAwait(false);
    }
}
