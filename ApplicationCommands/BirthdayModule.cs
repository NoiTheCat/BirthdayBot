using BirthdayBot.Data;
using Discord.Interactions;
using System.Text;

namespace BirthdayBot.ApplicationCommands;

[Group("birthday", "Commands relating to birthdays.")]
public class BirthdayModule : BotModuleBase {
    public const string HelpExport = "Generates a text file with all known and available birthdays.";
    public const string HelpGet = "Gets a user's birthday.";
    public const string HelpRecentUpcoming = "Get a list of users who recently had or will have a birthday.";
    public const string HelpRemove = "Removes your birthday information from this bot.";

    [Group("set", "Subcommands for setting birthday information.")]
    public class SubCmdsBirthdaySet : BotModuleBase {
        public const string HelpSetBday = "Sets or updates your birthday.";
        public const string HelpSetZone = "Sets or updates your time zone, when your birthday is already set.";
        
        [SlashCommand("date", HelpSetBday)]
        public async Task CmdSetBday([Summary(description: HelpOptDate)] string date,
                                     [Summary(description: HelpOptPfxOptional + HelpOptZone)] string? zone = null) {
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

            var user = await Context.GetGuildUserConfAsync().ConfigureAwait(false);
            await user.UpdateAsync(inmonth, inday, inzone ?? user.TimeZone).ConfigureAwait(false);

            await RespondAsync($":white_check_mark: Your birthday has been set to **{FormatDate(inmonth, inday)}**" +
                (inzone == null ? "" : $", with time zone {inzone}") + ".");
        }

        [SlashCommand("zone", HelpSetZone)]
        public async Task CmdSetZone([Summary(description: HelpOptZone)] string zone) {
            var user = await Context.GetGuildUserConfAsync().ConfigureAwait(false);
            if (!user.IsKnown) {
                await RespondAsync(":x: You must first set your birthday to use this command.", ephemeral: true).ConfigureAwait(false);
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
            await RespondAsync($":white_check_mark: Your time zone has been set to **{inzone}**.").ConfigureAwait(false);
        }
    }

    [SlashCommand("remove", HelpRemove)]
    public async Task CmdRemove() {
        var user = await Context.GetGuildUserConfAsync().ConfigureAwait(false);
        if (user.IsKnown) {
            await user.DeleteAsync().ConfigureAwait(false);
            await RespondAsync(":white_check_mark: Your information for this server has been removed.");
        } else {
            await RespondAsync(":white_check_mark: This bot already does not have your birthday for this server.");
        }
    }

    [SlashCommand("get", "Gets a user's birthday.")]
    public async Task CmdGetBday([Summary(description: "Optional: The user's birthday to look up.")] SocketGuildUser? user = null) {
        var self = user is null;
        if (self) user = (SocketGuildUser)Context.User;
        var targetdata = await GuildUserConfiguration.LoadAsync(Context.Guild.Id, user!.Id).ConfigureAwait(false);

        if (!targetdata.IsKnown) {
            if (self) await RespondAsync(":x: You do not have your birthday registered.", ephemeral: true).ConfigureAwait(false);
            else await RespondAsync(":x: The given user does not have their birthday registered.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await RespondAsync($"{Common.FormatName(user, false)}: `{FormatDate(targetdata.BirthMonth, targetdata.BirthDay)}`" +
            (targetdata.TimeZone == null ? "" : $" - {targetdata.TimeZone}")).ConfigureAwait(false);
    }

    // "Recent and upcoming birthdays"
    // The 'recent' bit removes time zone ambiguity and spares us from extra time zone processing here
    // TODO stop being lazy
    [SlashCommand("show-nearest", HelpRecentUpcoming)]
    public async Task CmdShowNearest() {
        if (!await HasMemberCacheAsync(Context.Guild).ConfigureAwait(false)) {
            await RespondAsync(MemberCacheEmptyError, ephemeral: true);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var search = DateIndex(now.Month, now.Day) - 8; // begin search 8 days prior to current date UTC
        if (search <= 0) search = 366 - Math.Abs(search);

        var query = await GetSortedUsersAsync(Context.Guild).ConfigureAwait(false);

        // TODO pagination instead of this workaround
        bool hasOutputOneLine = false;
        // First output is shown as an interaction response, followed then as regular channel messages
        async Task doOutput(string msg) {
            if (!hasOutputOneLine) {
                await RespondAsync(msg).ConfigureAwait(false);
                hasOutputOneLine = true;
            } else {
                await Context.Channel.SendMessageAsync(msg).ConfigureAwait(false);
            }
        }

        var output = new StringBuilder();
        var resultCount = 0;
        output.AppendLine("Recent and upcoming birthdays:");
        for (int count = 0; count <= 21; count++) // cover 21 days total (7 prior, current day, 14 upcoming)
        {
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

    [SlashCommand("export", HelpPfxModOnly + HelpExport)]
    public async Task CmdExport([Summary(description: "Specify whether to export the list in CSV format.")] bool asCsv = false) {
        // For now, we're restricting this command to moderators only. This may turn into an option later.
        if (!(await Context.GetGuildConfAsync()).IsBotModerator((SocketGuildUser)Context.User)) {
            // Do not add detailed usage information to this error message.
            await RespondAsync(":x: Only bot moderators may use this command.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!await HasMemberCacheAsync(Context.Guild)) {
            await RespondAsync(MemberCacheEmptyError).ConfigureAwait(false);
            return;
        }

        var bdlist = await GetSortedUsersAsync(Context.Guild).ConfigureAwait(false);

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
    private static async Task<List<ListItem>> GetSortedUsersAsync(SocketGuild guild) {
        using var db = await Database.OpenConnectionAsync();
        using var c = db.CreateCommand();
        c.CommandText = "select user_id, birth_month, birth_day from " + GuildUserConfiguration.BackingTable
            + " where guild_id = @Gid order by birth_month, birth_day";
        c.Parameters.Add("@Gid", NpgsqlTypes.NpgsqlDbType.Bigint).Value = (long)guild.Id;
        c.Prepare();
        using var r = await c.ExecuteReaderAsync();
        var result = new List<ListItem>();
        while (await r.ReadAsync()) {
            var id = (ulong)r.GetInt64(0);
            var month = r.GetInt32(1);
            var day = r.GetInt32(2);

            var guildUser = guild.GetUser(id);
            if (guildUser == null) continue; // Skip user not in guild

            result.Add(new ListItem() {
                BirthMonth = month,
                BirthDay = day,
                DateIndex = DateIndex(month, day),
                UserId = guildUser.Id,
                DisplayName = Common.FormatName(guildUser, false)
            });
        }
        return result;
    }

    private Stream ListExportNormal(SocketGuild guild, IEnumerable<ListItem> list) {
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
            writer.WriteLine();
        }
        writer.Flush();
        result.Position = 0;
        return result;
    }

    private Stream ListExportCsv(SocketGuild guild, IEnumerable<ListItem> list) {
        // Output: User ID, Username, Nickname, Month-Day, Month, Day
        var result = new MemoryStream();
        var writer = new StreamWriter(result, Encoding.UTF8);

        // Conforming to RFC 4180; with header
        writer.Write("UserId,Username,Nickname,MonthDayDisp,Month,Day");
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
    }
    #endregion
}