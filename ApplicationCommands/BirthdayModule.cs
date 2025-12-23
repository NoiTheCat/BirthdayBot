using BirthdayBot.Data;
using Discord.Interactions;
using System.Text;

namespace BirthdayBot.ApplicationCommands;
[Group("birthday", HelpCmdBirthday)]
[CommandContextType(InteractionContextType.Guild)]
public class BirthdayModule : BotModuleBase {
    public const string HelpCmdBirthday = "Commands relating to birthdays.";
    public const string HelpCmdSetDate = "Sets or updates your birthday.";
    public const string HelpCmdSetZone = "Sets or updates your time zone if your birthday is already set.";
    public const string HelpCmdRemove = "Removes your birthday information from this bot.";
    public const string HelpCmdGet = "Gets a user's birthday.";
    public const string HelpCmdNearest = "Get a list of users who recently had or will have a birthday.";

    [Group("set", "Subcommands for setting birthday information.")]
    public class SubCmdsBirthdaySet : BotModuleBase {
        [SlashCommand("date", HelpCmdSetDate)]
        public async Task CmdSetBday([Summary(description: HelpOptDate)] string date,
                                     [Summary(description: HelpOptZone), Autocomplete<TzAutocompleteHandler>] string? zone = null) {
            // IMPORTANT: If editing here, reflect changes as needed in BirthdayOverrideModule.
            int inmonth, inday;
            try {
                (inmonth, inday) = ParseDate(date);
            } catch (FormatException e) {
                // Our parse method's FormatException has its message to send out to Discord.
                await RespondAsync(e.Message, ephemeral: true).ConfigureAwait(false);
                return;
            }

            string? inzone = null;
            if (zone != null) {
                try {
                    inzone = ParseTimeZone(zone);
                } catch (FormatException e) {
                    await ReplyAsync(e.Message).ConfigureAwait(false);
                    return;
                }
            }

            var guild = ((SocketTextChannel)Context.Channel).Guild.GetConfigOrNew(DbContext);
            if (guild.IsNew) DbContext.GuildConfigurations.Add(guild); // Satisfy foreign key constraint
            var user = ((SocketGuildUser)Context.User).GetUserEntryOrNew(DbContext);
            if (user.IsNew) DbContext.UserEntries.Add(user);
            user.BirthMonth = inmonth;
            user.BirthDay = inday;
            user.TimeZone = inzone ?? user.TimeZone;
            await DbContext.SaveChangesAsync();

            var response = $":white_check_mark: Your birthday has been set to **{FormatDate(inmonth, inday)}**";
            if (inzone != null) response += $" at time zone **{inzone}**";
            response += ".";
            if (user.TimeZone == null)
                response += "\n(Tip: The `/birthday set timezone` command ensures your birthday is recognized just in time!)";
            await RespondAsync(response, ephemeral: IsEphemeralSet()).ConfigureAwait(false);
        }

        [SlashCommand("timezone", HelpCmdSetZone)]
        public async Task CmdSetZone([Summary(description: HelpOptZone), Autocomplete<TzAutocompleteHandler>] string zone) {
            var user = ((SocketGuildUser)Context.User).GetUserEntryOrNew(DbContext);
            if (user.IsNew) {
                await RespondAsync(":x: You do not have a birthday set.", ephemeral: true).ConfigureAwait(false);
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
            await RespondAsync($":white_check_mark: Your time zone has been set to **{newzone}**.",
                               ephemeral: IsEphemeralSet()).ConfigureAwait(false);
        }
    }

    [SlashCommand("remove", HelpCmdRemove)]
    public async Task CmdRemove() {
        var user = ((SocketGuildUser)Context.User).GetUserEntryOrNew(DbContext);
        if (!user.IsNew) {
            DbContext.UserEntries.Remove(user);
            await DbContext.SaveChangesAsync().ConfigureAwait(false);
            await RespondAsync(":white_check_mark: Your birthday in this server has been removed.", ephemeral: IsEphemeralSet())
                .ConfigureAwait(false);
        } else {
            await RespondAsync(":white_check_mark: Your birthday is not registered.", ephemeral: IsEphemeralSet())
                .ConfigureAwait(false);
        }
    }

    [SlashCommand("get", "Gets a user's birthday.")]
    public async Task CmdGetBday([Summary(description: "Optional: The user's birthday to look up.")] SocketGuildUser? user = null) {
        var isSelf = user is null;
        if (isSelf) user = (SocketGuildUser)Context.User;

        var targetdata = user!.GetUserEntryOrNew(DbContext);

        if (targetdata.IsNew) {
            if (isSelf) await RespondAsync(":x: You do not have your birthday registered.", ephemeral: true).ConfigureAwait(false);
            else await RespondAsync(":x: The given user does not have their birthday registered.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await RespondAsync($"{Common.FormatName(user!, false)}: `{FormatDate(targetdata.BirthMonth, targetdata.BirthDay)}`" +
            (targetdata.TimeZone == null ? "" : $" - {targetdata.TimeZone}")).ConfigureAwait(false);
    }

    // "Recent and upcoming birthdays"
    // The 'recent' bit removes time zone ambiguity and spares us from extra time zone processing here
    // TODO stop being lazy
    [SlashCommand("show-nearest", HelpCmdNearest)]
    public async Task CmdShowNearest() {
        if (!await HasMemberCacheAsync(Context.Guild).ConfigureAwait(false)) {
            await RespondAsync(MemberCacheEmptyError, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var search = DateIndex(now.Month, now.Day) - 8; // begin search 8 days prior to current date UTC
        if (search <= 0) search = 366 - Math.Abs(search);

        var query = GetSortedUserList(Context.Guild);

        // TODO pagination instead of this workaround
        var hasOutputOneLine = false;
        // First output is shown as an interaction response, followed then as regular channel messages
        async Task doOutput(string msg) {
            if (!hasOutputOneLine) {
                await RespondAsync(msg).ConfigureAwait(false);
                hasOutputOneLine = true;
            } else {
                await ReplyAsync(msg).ConfigureAwait(false);
            }
        }

        var output = new StringBuilder();
        var resultCount = 0;
        output.AppendLine("Recent and upcoming birthdays:");
        for (var count = 0; count <= 21; count++) { // cover 21 days total (7 prior, current day, 14 upcoming)
            var results = from item in query
                          where item.DateIndex == search
                          select item;

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
            output.Append($"● `{Common.MonthNames[results.First().BirthMonth]}-{results.First().BirthDay:00}`: ");
            foreach (var item in names) {
                // If the output is starting to fill up, send out this message and prepare a new one.
                if (output.Length > 800) {
                    await doOutput(output.ToString()).ConfigureAwait(false);
                    output.Clear();
                    first = true;
                    output.Append($"● `{Common.MonthNames[results.First().BirthMonth]}-{results.First().BirthDay:00}`: ");
                }

                if (first) first = false;
                else output.Append(", ");
                output.Append(item);
            }
        }

        if (resultCount == 0)
            await RespondAsync(
                "There are no recent or upcoming birthdays (within the last 7 days and/or next 14 days).")
                .ConfigureAwait(false);
        else
            await doOutput(output.ToString()).ConfigureAwait(false);
    }
}