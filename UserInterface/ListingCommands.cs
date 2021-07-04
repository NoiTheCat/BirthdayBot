using BirthdayBot.Data;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BirthdayBot.UserInterface
{
    /// <summary>
    /// Commands for listing upcoming and all birthdays.
    /// </summary>
    internal class ListingCommands : CommandsCommon
    {
        public ListingCommands(Configuration db) : base(db) { }

        public override IEnumerable<(string, CommandHandler)> Commands
            => new List<(string, CommandHandler)>()
            {
                ("list", CmdList),
                ("upcoming", CmdUpcoming),
                ("recent", CmdUpcoming),
                ("when", CmdWhen)
            };

        #region Documentation
        public static readonly CommandDocumentation DocList =
            new CommandDocumentation(new string[] { "list" }, "Exports all birthdays to a file."
                + " Accepts `csv` as an optional parameter.", null);
        public static readonly CommandDocumentation DocUpcoming =
            new CommandDocumentation(new string[] { "recent", "upcoming" }, "Lists recent and upcoming birthdays.", null);
        public static readonly CommandDocumentation DocWhen =
            new CommandDocumentation(new string[] { "when" }, "Displays the given user's birthday information.", null);
        #endregion

        private async Task CmdWhen(ShardInstance instance, GuildConfiguration gconf,
                                   string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser)
        {
            if (!Common.HasMostMembersDownloaded(reqChannel.Guild))
            {
                instance.RequestDownloadUsers(reqChannel.Guild.Id);
                await reqChannel.SendMessageAsync(UsersNotDownloadedError);
                return;
            }

            // Requires a parameter
            if (param.Length == 1)
            {
                await reqChannel.SendMessageAsync(ParameterError, embed: DocWhen.UsageEmbed).ConfigureAwait(false);
                return;
            }

            var search = param[1];
            if (param.Length == 3)
            {
                // param maxes out at 3 values. param[2] might contain part of the search string (if name has a space)
                search += " " + param[2];
            }

            SocketGuildUser searchTarget = null;

            if (!TryGetUserId(search, out ulong searchId)) // ID lookup
            {
                // name lookup without discriminator
                foreach (var searchuser in reqChannel.Guild.Users)
                {
                    if (string.Equals(search, searchuser.Username, StringComparison.OrdinalIgnoreCase))
                    {
                        searchTarget = searchuser;
                        break;
                    }
                }
            }
            else
            {
                searchTarget = reqChannel.Guild.GetUser(searchId);
            }
            if (searchTarget == null)
            {
                await reqChannel.SendMessageAsync(BadUserError, embed: DocWhen.UsageEmbed).ConfigureAwait(false);
                return;
            }

            var searchTargetData = await GuildUserConfiguration.LoadAsync(reqChannel.Guild.Id, searchId).ConfigureAwait(false);
            if (!searchTargetData.IsKnown)
            {
                await reqChannel.SendMessageAsync("I do not have birthday information for that user.").ConfigureAwait(false);
                return;
            }

            string result = Common.FormatName(searchTarget, false);
            result += ": ";
            result += $"`{searchTargetData.BirthDay:00}-{Common.MonthNames[searchTargetData.BirthMonth]}`";
            result += searchTargetData.TimeZone == null ? "" : $" - `{searchTargetData.TimeZone}`";

            await reqChannel.SendMessageAsync(result).ConfigureAwait(false);
        }

        // Creates a file with all birthdays.
        private async Task CmdList(ShardInstance instance, GuildConfiguration gconf,
                                   string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser)
        {
            // For now, we're restricting this command to moderators only. This may turn into an option later.
            if (!gconf.IsBotModerator(reqUser))
            {
                // Do not add detailed usage information to this error message.
                await reqChannel.SendMessageAsync(":x: Only bot moderators may use this command.").ConfigureAwait(false);
                return;
            }

            if (!Common.HasMostMembersDownloaded(reqChannel.Guild))
            {
                instance.RequestDownloadUsers(reqChannel.Guild.Id);
                await reqChannel.SendMessageAsync(UsersNotDownloadedError);
                return;
            }

            bool useCsv = false;
            // Check for CSV option
            if (param.Length == 2)
            {
                if (param[1].ToLower() == "csv") useCsv = true;
                else
                {
                    await reqChannel.SendMessageAsync(":x: That is not available as an export format.", embed: DocList.UsageEmbed)
                        .ConfigureAwait(false);
                    return;
                }
            }
            else if (param.Length > 2)
            {
                await reqChannel.SendMessageAsync(ParameterError, embed: DocList.UsageEmbed).ConfigureAwait(false);
                return;
            }

            var bdlist = await GetSortedUsersAsync(reqChannel.Guild).ConfigureAwait(false);

            var filepath = Path.GetTempPath() + "birthdaybot-" + reqChannel.Guild.Id;
            string fileoutput;
            if (useCsv)
            {
                fileoutput = ListExportCsv(reqChannel, bdlist);
                filepath += ".csv";
            }
            else
            {
                fileoutput = ListExportNormal(reqChannel, bdlist);
                filepath += ".txt.";
            }
            await File.WriteAllTextAsync(filepath, fileoutput, Encoding.UTF8).ConfigureAwait(false);

            try
            {
                await reqChannel.SendFileAsync(filepath, $"Exported {bdlist.Count} birthdays to file.").ConfigureAwait(false);
            }
            catch (Discord.Net.HttpException)
            {
                reqChannel.SendMessageAsync(":x: Unable to send list due to a permissions issue. Check the 'Attach Files' permission.").Wait();
            }
            catch (Exception ex)
            {
                Program.Log("Listing", ex.ToString());
                reqChannel.SendMessageAsync(InternalError).Wait();
                // TODO webhook report
            }
            finally
            {
                File.Delete(filepath);
            }
        }

        // "Recent and upcoming birthdays"
        // The 'recent' bit removes time zone ambiguity and spares us from extra time zone processing here
        private async Task CmdUpcoming(ShardInstance instance, GuildConfiguration gconf,
                                       string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser)
        {
            if (!Common.HasMostMembersDownloaded(reqChannel.Guild))
            {
                instance.RequestDownloadUsers(reqChannel.Guild.Id);
                await reqChannel.SendMessageAsync(UsersNotDownloadedError);
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var search = DateIndex(now.Month, now.Day) - 8; // begin search 8 days prior to current date UTC
            if (search <= 0) search = 366 - Math.Abs(search);

            var query = await GetSortedUsersAsync(reqChannel.Guild).ConfigureAwait(false);

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

                if (results.Count() == 0) continue; // back out early
                resultCount += results.Count();

                // Build sorted name list
                var names = new List<string>();
                foreach (var item in results)
                {
                    names.Add(item.DisplayName);
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);

                var first = true;
                output.AppendLine();
                output.Append($"● `{Common.MonthNames[results.First().BirthMonth]}-{results.First().BirthDay:00}`: ");
                foreach (var item in names)
                {
                    // If the output is starting to fill up, send out this message and prepare a new one.
                    if (output.Length > 800)
                    {
                        await reqChannel.SendMessageAsync(output.ToString()).ConfigureAwait(false);
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
                await reqChannel.SendMessageAsync(
                    "There are no recent or upcoming birthdays (within the last 7 days and/or next 21 days).")
                    .ConfigureAwait(false);
            else
                await reqChannel.SendMessageAsync(output.ToString()).ConfigureAwait(false);
        }

        /// <summary>
        /// Fetches all guild birthdays and places them into an easily usable structure.
        /// Users currently not in the guild are not included in the result.
        /// </summary>
        private async Task<List<ListItem>> GetSortedUsersAsync(SocketGuild guild)
        {
            using var db = await Database.OpenConnectionAsync();
            using var c = db.CreateCommand();
            c.CommandText = "select user_id, birth_month, birth_day from " + GuildUserConfiguration.BackingTable
                + " where guild_id = @Gid order by birth_month, birth_day";
            c.Parameters.Add("@Gid", NpgsqlTypes.NpgsqlDbType.Bigint).Value = (long)guild.Id;
            c.Prepare();
            using var r = await c.ExecuteReaderAsync();
            var result = new List<ListItem>();
            while (await r.ReadAsync())
            {
                var id = (ulong)r.GetInt64(0);
                var month = r.GetInt32(1);
                var day = r.GetInt32(2);

                var guildUser = guild.GetUser(id);
                if (guildUser == null) continue; // Skip user not in guild

                result.Add(new ListItem()
                {
                    BirthMonth = month,
                    BirthDay = day,
                    DateIndex = DateIndex(month, day),
                    UserId = guildUser.Id,
                    DisplayName = Common.FormatName(guildUser, false)
                });
            }
            return result;
        }

        private string ListExportNormal(SocketGuildChannel channel, IEnumerable<ListItem> list)
        {
            // Output: "● Mon-dd: (user ID) Username [ - Nickname: (nickname)]"
            var result = new StringBuilder();
            result.AppendLine("Birthdays in " + channel.Guild.Name);
            result.AppendLine();
            foreach (var item in list)
            {
                var user = channel.Guild.GetUser(item.UserId);
                if (user == null) continue; // User disappeared in the instant between getting list and processing
                result.Append($"● {Common.MonthNames[item.BirthMonth]}-{item.BirthDay:00}: ");
                result.Append(item.UserId);
                result.Append(" " + user.Username + "#" + user.Discriminator);
                if (user.Nickname != null) result.Append(" - Nickname: " + user.Nickname);
                result.AppendLine();
            }
            return result.ToString();
        }

        private string ListExportCsv(SocketGuildChannel channel, IEnumerable<ListItem> list)
        {
            // Output: User ID, Username, Nickname, Month-Day, Month, Day
            var result = new StringBuilder();

            // Conforming to RFC 4180; with header
            result.Append("UserId,Username,Nickname,MonthDayDisp,Month,Day");
            result.Append("\r\n"); // crlf line break is specified by the standard
            foreach (var item in list)
            {
                var user = channel.Guild.GetUser(item.UserId);
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

        private string CsvEscape(string input)
        {
            var result = new StringBuilder();
            result.Append('"');
            foreach (var ch in input)
            {
                if (ch == '"') result.Append('"');
                result.Append(ch);
            }
            result.Append('"');
            return result.ToString();
        }

        private int DateIndex(int month, int day)
        {
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

        private struct ListItem
        {
            public int DateIndex;
            public int BirthMonth;
            public int BirthDay;
            public ulong UserId;
            public string DisplayName;
        }
    }
}
