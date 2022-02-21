using BirthdayBot.Data;
using System.Text;

namespace BirthdayBot.ApplicationCommands;

internal class QueryCommands : BotApplicationCommand {
    public const string HelpBirthdayFor = "Gets a user's birthday.";
    public const string HelpListAll = "Show a full list of all known birthdays.";
    public const string HelpRecentUpcoming = "Get a list of users who recently had or will have a birthday.";

    public override IEnumerable<ApplicationCommandProperties> GetCommands() => new ApplicationCommandProperties[] {
            new SlashCommandBuilder()
                .WithName("birthday")
                .WithDescription(HelpBirthdayFor)
                .AddOption("user", ApplicationCommandOptionType.User, "The user whose birthday to check.", isRequired: false)
                .Build(),
            new SlashCommandBuilder()
                .WithName("recent")
                .WithDescription(HelpRecentUpcoming)
                .Build(),
            new SlashCommandBuilder()
                .WithName("upcoming")
                .WithDescription(HelpRecentUpcoming)
                .Build(),
            new SlashCommandBuilder()
                .WithName("list-all")
                .WithDescription(HelpPfxModOnly + HelpRecentUpcoming)
                .AddOption("as-csv", ApplicationCommandOptionType.Boolean, "Whether to output the list in CSV format.")
                .Build(),
        };
    public override CommandResponder? GetHandlerFor(string commandName) => commandName switch {
        "birthday-for" => CmdBirthdayFor,
        "recent" => CmdRecentUpcoming,
        "upcoming" => CmdRecentUpcoming,
        "list-all" => CmdListAll,
        _ => null,
    };

    private async Task CmdBirthdayFor(ShardInstance instance, GuildConfiguration gconf, SocketSlashCommand arg) {
        var searchtarget = arg.Data.Options.FirstOrDefault()?.Value as SocketGuildUser ?? (SocketGuildUser)arg.User;
        var targetdata = await GuildUserConfiguration.LoadAsync(gconf.GuildId, searchtarget.Id);

        if (!targetdata.IsKnown) {
            await arg.RespondAsync($"{Common.FormatName(searchtarget, false)} does not have their birthday registered.");
            return;
        }
        await arg.RespondAsync($"{Common.FormatName(searchtarget, false)}: " +
            $"`{targetdata.BirthDay:00}-{Common.MonthNames[targetdata.BirthMonth]}`" +
            (targetdata.TimeZone == null ? "" : $" - {targetdata.TimeZone}")).ConfigureAwait(false);
    }

    // "Recent and upcoming birthdays"
    // The 'recent' bit removes time zone ambiguity and spares us from extra time zone processing here
    // TODO stop being lazy
    private async Task CmdRecentUpcoming(ShardInstance instance, GuildConfiguration gconf, SocketSlashCommand arg) {
        var guild = ((SocketGuildChannel)arg.Channel).Guild;
        if (!await HasMemberCacheAsync(guild).ConfigureAwait(false)) {
            await arg.RespondAsync(MemberCacheEmptyError, ephemeral: true);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var search = DateIndex(now.Month, now.Day) - 8; // begin search 8 days prior to current date UTC
        if (search <= 0) search = 366 - Math.Abs(search);

        var query = await GetSortedUsersAsync(guild).ConfigureAwait(false);

        // TODO pagination instead of this workaround
        bool hasOutputOneLine = false;
        // First output is shown as an interaction response, followed then as regular channel messages
        async Task doOutput(string msg) {
            if (!hasOutputOneLine) {
                await arg.RespondAsync(msg).ConfigureAwait(false);
                hasOutputOneLine = true;
            } else {
                await arg.Channel.SendMessageAsync(msg).ConfigureAwait(false);
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
            await arg.RespondAsync(
                "There are no recent or upcoming birthdays (within the last 7 days and/or next 21 days).")
                .ConfigureAwait(false);
        else
            await doOutput(output.ToString()).ConfigureAwait(false);
    }

    private async Task CmdListAll(ShardInstance instance, GuildConfiguration gconf, SocketSlashCommand arg) {
        var guild = ((SocketGuildChannel)arg.Channel).Guild;
        // For now, we're restricting this command to moderators only. This may turn into an option later.
        if (!gconf.IsBotModerator((SocketGuildUser)arg.User)) {
            // Do not add detailed usage information to this error message.
            await arg.RespondAsync(":x: Only bot moderators may use this command.").ConfigureAwait(false);
            return;
        }

        if (!await HasMemberCacheAsync(guild)) {
            await arg.RespondAsync(MemberCacheEmptyError).ConfigureAwait(false);
            return;
        }

        // Check for CSV option
        var useCsv = arg.Data.Options.FirstOrDefault()?.Value as bool? ?? false;

        var bdlist = await GetSortedUsersAsync(guild).ConfigureAwait(false);

        var filepath = Path.GetTempPath() + "birthdaybot-" + guild.Id;
        string fileoutput;
        if (useCsv) {
            fileoutput = ListExportCsv(guild, bdlist);
            filepath += ".csv";
        } else {
            fileoutput = ListExportNormal(guild, bdlist);
            filepath += ".txt.";
        }
        await File.WriteAllTextAsync(filepath, fileoutput, Encoding.UTF8).ConfigureAwait(false);

        try {
            await arg.RespondWithFileAsync(filepath, "birthdaybot-" + guild.Id + (useCsv ? ".csv" : ".txt"),
                $"Exported {bdlist.Count} birthdays to file.",
                null, false, false, null, null, null, null);
        } catch (Exception ex) {
            Program.Log("Listing", ex.ToString());
        } finally {
            File.Delete(filepath);
        }
    }

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

    private string ListExportNormal(SocketGuild guild, IEnumerable<ListItem> list) {
        // Output: "● Mon-dd: (user ID) Username [ - Nickname: (nickname)]"
        var result = new StringBuilder();
        result.AppendLine("Birthdays in " + guild.Name);
        result.AppendLine();
        foreach (var item in list) {
            var user = guild.GetUser(item.UserId);
            if (user == null) continue; // User disappeared in the instant between getting list and processing
            result.Append($"● {Common.MonthNames[item.BirthMonth]}-{item.BirthDay:00}: ");
            result.Append(item.UserId);
            result.Append(" " + user.Username + "#" + user.Discriminator);
            if (user.Nickname != null) result.Append(" - Nickname: " + user.Nickname);
            result.AppendLine();
        }
        return result.ToString();
    }

    private string ListExportCsv(SocketGuild guild, IEnumerable<ListItem> list) {
        // Output: User ID, Username, Nickname, Month-Day, Month, Day
        var result = new StringBuilder();

        // Conforming to RFC 4180; with header
        result.Append("UserId,Username,Nickname,MonthDayDisp,Month,Day");
        result.Append("\r\n"); // crlf line break is specified by the standard
        foreach (var item in list) {
            var user = guild.GetUser(item.UserId);
            if (user == null) continue; // User disappeared in the instant between getting list and processing
            result.Append(item.UserId);
            result.Append(',');
            result.Append(CsvEscape(user.Username + "#" + user.Discriminator));
            result.Append(',');
            if (user.Nickname != null) result.Append(user.Nickname);
            result.Append(',');
            result.Append($"{Common.MonthNames[item.BirthMonth]}-{item.BirthDay:00}");
            result.Append(',');
            result.Append(item.BirthMonth);
            result.Append(',');
            result.Append(item.BirthDay);
            result.Append("\r\n");
        }
        return result.ToString();
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
}
