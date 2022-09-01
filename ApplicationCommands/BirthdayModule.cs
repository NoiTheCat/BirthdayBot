using BirthdayBot.Data;
using Discord.Interactions;
using System.Text;

namespace BirthdayBot.ApplicationCommands;
[RequireGuildContext]
[Group("birthday", HelpCmdBirthday)]
public class BirthdayModule : BotModuleBase {
    public const string HelpCmdBirthday = "Commands relating to birthdays.";
    public const string HelpCmdSetDate = "Sets or updates your birthday.";
    public const string HelpCmdSetZone = "Sets or updates your time zone if your birthday is already set.";
    public const string HelpCmdRemove = "Removes your birthday information from this bot.";
    public const string HelpCmdGet = "Gets a user's birthday.";
    public const string HelpCmdNearest = "Get a list of users who recently had or will have a birthday.";
    public const string HelpCmdExport = "Generates a text file with all known and available birthdays.";
    public const string ErrNotSetFk = $":x: The bot has not yet been set up. Please configure a birthday role."; // foreign key violation

    // Note that these methods have largely been copied to BirthdayOverrideModule. Changes here should be reflected there as needed.

    [Group("set", "Subcommands for setting birthday information.")]
    public class SubCmdsBirthdaySet : BotModuleBase {
        [SlashCommand("date", HelpCmdSetDate)]
        public async Task CmdSetBday([Summary(description: HelpOptDate)] string date,
                                     [Summary(description: HelpOptZone)] string? zone = null) {
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

            using var db = new BotDatabaseContext();
            var user = ((SocketGuildUser)Context.User).GetUserEntryOrNew(db);
            if (user.IsNew) db.UserEntries.Add(user);
            user.BirthMonth = inmonth;
            user.BirthDay = inday;
            user.TimeZone = inzone;
            try {
                await db.SaveChangesAsync();
            } catch (Microsoft.EntityFrameworkCore.DbUpdateException e)
                when (e.InnerException is Npgsql.PostgresException ex && ex.SqlState == Npgsql.PostgresErrorCodes.ForeignKeyViolation) {
                await RespondAsync(ErrNotSetFk);
                return;
            }

            await RespondAsync($":white_check_mark: Your birthday has been set to **{FormatDate(inmonth, inday)}**" +
                (inzone == null ? "" : $" at time zone **{inzone}**") + ".").ConfigureAwait(false);
        }

        [SlashCommand("timezone", HelpCmdSetZone)]
        public async Task CmdSetZone([Summary(description: HelpOptZone)] string zone) {
            using var db = new BotDatabaseContext();

            var user = ((SocketGuildUser)Context.User).GetUserEntryOrNew(db);
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
            await db.SaveChangesAsync();
            await RespondAsync($":white_check_mark: Your time zone has been set to **{newzone}**.").ConfigureAwait(false);
        }
    }

    [SlashCommand("remove", HelpCmdRemove)]
    public async Task CmdRemove() {
        using var db = new BotDatabaseContext();
        var user = ((SocketGuildUser)Context.User).GetUserEntryOrNew(db);
        if (!user.IsNew) {
            db.UserEntries.Remove(user);
            await db.SaveChangesAsync();
            await RespondAsync(":white_check_mark: Your birthday in this server has been removed.");
        } else {
            await RespondAsync(":white_check_mark: Your birthday is not registered.")
                .ConfigureAwait(false);
        }
    }

    [SlashCommand("get", "Gets a user's birthday.")]
    public async Task CmdGetBday([Summary(description: "Optional: The user's birthday to look up.")] SocketGuildUser? user = null) {
        using var db = new BotDatabaseContext();

        var isSelf = user is null;
        if (isSelf) user = (SocketGuildUser)Context.User;

        var targetdata = user!.GetUserEntryOrNew(db);

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

    [RequireBotModerator]
    [SlashCommand("export", HelpPfxModOnly + HelpCmdExport)]
    public async Task CmdExport([Summary(description: "Specify whether to export the list in CSV format.")] bool asCsv = false) {
        if (!await HasMemberCacheAsync(Context.Guild)) {
            await RespondAsync(MemberCacheEmptyError, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var bdlist = GetSortedUserList(Context.Guild);

        var filename = "birthdaybot-" + Context.Guild.Id;
        Stream fileoutput;
        if (asCsv) {
            fileoutput = ListExportCsv(Context.Guild, bdlist);
            filename += ".csv";
        } else {
            fileoutput = ListExportNormal(Context.Guild, bdlist);
            filename += ".txt.";
        }
        await RespondWithFileAsync(fileoutput, filename, text: $"Exported {bdlist.Count} birthdays to file.");
    }

    #region Listing helper methods
    /// <summary>
    /// Fetches all guild birthdays and places them into an easily usable structure.
    /// Users currently not in the guild are not included in the result.
    /// </summary>
    private static List<ListItem> GetSortedUserList(SocketGuild guild) {
        using var db = new BotDatabaseContext();
        var query = from row in db.UserEntries
                        where row.GuildId == (long)guild.Id
                        orderby row.BirthMonth, row.BirthDay
                        select new {
                            UserId = (ulong)row.UserId,
                            Month = row.BirthMonth,
                            Day = row.BirthDay,
                            Zone = row.TimeZone
                        };

        var result = new List<ListItem>();
        foreach (var row in query) {
            var guildUser = guild.GetUser(row.UserId);
            if (guildUser == null) continue; // Skip user not in guild

            result.Add(new ListItem() {
                BirthMonth = row.Month,
                BirthDay = row.Day,
                DateIndex = DateIndex(row.Month, row.Day),
                UserId = guildUser.Id,
                DisplayName = Common.FormatName(guildUser, false),
                TimeZone = row.Zone
            });
        }
        return result;
    }

    private static Stream ListExportNormal(SocketGuild guild, IEnumerable<ListItem> list) {
        // Output: "● Mon-dd: (user ID) Username [ - Nickname: (nickname)]"
        var result = new MemoryStream();
        var writer = new StreamWriter(result, Encoding.UTF8);

        writer.WriteLine("Birthdays in " + guild.Name);
        writer.WriteLine();
        foreach (var item in list) {
            var user = guild.GetUser(item.UserId);
            if (user == null) continue; // User disappeared in the instant between getting list and processing
            writer.Write($"● {Common.MonthNames[item.BirthMonth]}-{item.BirthDay:00}: ");
            writer.Write(item.UserId);
            writer.Write(" " + user.Username + "#" + user.Discriminator);
            if (user.Nickname != null) writer.Write(" - Nickname: " + user.Nickname);
            if (item.TimeZone != null) writer.Write(" | Time zone: " + item.TimeZone);
            writer.WriteLine();
        }
        writer.Flush();
        result.Position = 0;
        return result;
    }

    private static Stream ListExportCsv(SocketGuild guild, IEnumerable<ListItem> list) {
        // Output: User ID, Username, Nickname, Month-Day, Month, Day
        var result = new MemoryStream();
        var writer = new StreamWriter(result, Encoding.UTF8);

        // Conforming to RFC 4180; with header
        writer.Write("UserId,Username,Nickname,MonthDayDisp,Month,Day,TimeZone");
        writer.Write("\r\n"); // crlf line break is specified by the standard
        foreach (var item in list) {
            var user = guild.GetUser(item.UserId);
            if (user == null) continue; // User disappeared in the instant between getting list and processing
            writer.Write(item.UserId);
            writer.Write(',');
            writer.Write(CsvEscape(user.Username + "#" + user.Discriminator));
            writer.Write(',');
            if (user.Nickname != null) writer.Write(user.Nickname);
            writer.Write(',');
            writer.Write($"{Common.MonthNames[item.BirthMonth]}-{item.BirthDay:00}");
            writer.Write(',');
            writer.Write(item.BirthMonth);
            writer.Write(',');
            writer.Write(item.BirthDay);
            writer.Write(',');
            writer.Write(item.TimeZone);
            writer.Write("\r\n");
        }
        writer.Flush();
        result.Position = 0;
        return result;
    }

    private static string CsvEscape(string input) {
        var result = new StringBuilder();
        result.Append('"');
        foreach (var ch in input) {
            if (ch == '"') result.Append('"');
            result.Append(ch);
        }
        result.Append('"');
        return result.ToString();
    }

    private static int DateIndex(int month, int day) {
        var dateindex = 0;
        // Add month offsets
        if (month > 1) dateindex += 31; // Offset January
        if (month > 2) dateindex += 29; // Offset February (incl. leap day)
        if (month > 3) dateindex += 31; // etc
        if (month > 4) dateindex += 30;
        if (month > 5) dateindex += 31;
        if (month > 6) dateindex += 30;
        if (month > 7) dateindex += 31;
        if (month > 8) dateindex += 31;
        if (month > 9) dateindex += 30;
        if (month > 10) dateindex += 31;
        if (month > 11) dateindex += 30;
        dateindex += day;
        return dateindex;
    }

    private struct ListItem {
        public int DateIndex;
        public int BirthMonth;
        public int BirthDay;
        public ulong UserId;
        public string DisplayName;
        public string? TimeZone;
    }
    #endregion
}