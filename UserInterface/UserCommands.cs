using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BirthdayBot.UserInterface
{
    internal class UserCommands : CommandsCommon
    {
        public UserCommands(BirthdayBot inst, Configuration db) : base(inst, db) { }

        public override IEnumerable<(string, CommandHandler)> Commands
            => new List<(string, CommandHandler)>()
            {
                ("set", CmdSet),
                ("zone", CmdZone),
                ("remove", CmdRemove),
                ("when", CmdWhen)
            };

        /// <summary>
        /// Parses date parameter. Strictly takes dd-MMM or MMM-dd only. Eliminates ambiguity over dd/mm vs mm/dd.
        /// </summary>
        /// <returns>Tuple: month, day</returns>
        /// <exception cref="FormatException">Thrown for any parsing issue. Reason is expected to be sent to Discord as-is.</exception>
        private (int, int) ParseDate(string dateInput)
        {
            // Not doing DateTime.Parse. Setting it up is rather complicated, and it's probably case sensitive.
            // Admittedly, doing it the way it's being done here probably isn't any better.
            var m = Regex.Match(dateInput, @"^(?<day>\d{1,2})-(?<month>[A-Za-z]{3})$");
            if (!m.Success)
            {
                // Flip the fields around, try again
                m = Regex.Match(dateInput, @"^(?<month>[A-Za-z]{3})-(?<day>\d{1,2})$");
                if (!m.Success) throw new FormatException(GenericError);
            }
            int day;
            try
            {
                day = int.Parse(m.Groups["day"].Value);
            }
            catch (FormatException)
            {
                throw new Exception(GenericError);
            }
            var monthVal = m.Groups["month"].Value;
            int month;
            var dayUpper = 31; // upper day of month check
            switch (monthVal.ToLower())
            {
                case "jan":
                    month = 1;
                    break;
                case "feb":
                    month = 2;
                    dayUpper = 29;
                    break;
                case "mar":
                    month = 3;
                    break;
                case "apr":
                    month = 4;
                    dayUpper = 30;
                    break;
                case "may":
                    month = 5;
                    break;
                case "jun":
                    month = 6;
                    dayUpper = 30;
                    break;
                case "jul":
                    month = 7;
                    break;
                case "aug":
                    month = 8;
                    break;
                case "sep":
                    month = 9;
                    dayUpper = 30;
                    break;
                case "oct":
                    month = 10;
                    break;
                case "nov":
                    month = 11;
                    dayUpper = 30;
                    break;
                case "dec":
                    month = 12;
                    break;
                default:
                    throw new FormatException(":x: Invalid month name. Use a three-letter month abbreviation.");
            }
            if (day == 0 || day > dayUpper) throw new FormatException(":x: The date you specified is not a valid calendar date.");

            return (month, day);
        }

        private async Task CmdSet(string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser)
        {
            // Requires one parameter. Optionally two.
            if (param.Length < 2 || param.Length > 3)
            {
                await reqChannel.SendMessageAsync(GenericError);
                return;
            }

            int bmonth, bday;
            string btz = null;
            try
            {
                var res = ParseDate(param[1]);
                bmonth = res.Item1;
                bday = res.Item2;
                if (param.Length == 3) btz = ParseTimeZone(param[2]);
            }
            catch (FormatException ex)
            {
                // Our parse methods' FormatException has its message to send out to Discord.
                reqChannel.SendMessageAsync(ex.Message).Wait();
                return;
            }

            // Parsing successful. Update user information.
            bool known; // Extra detail: Bot's response changes if the user was previously unknown.
            try
            {
                var user = Instance.GuildCache[reqChannel.Guild.Id].GetUser(reqUser.Id);
                known = user.IsKnown;
                await user.UpdateAsync(bmonth, bday, btz, BotConfig.DatabaseSettings);
            }
            catch (Exception ex)
            {
                Program.Log("Error", ex.ToString());
                reqChannel.SendMessageAsync(":x: An unknown error occurred. The bot owner has been notified.").Wait();
                // TODO webhook report
                return;
            }
            if (known)
            {
                await reqChannel.SendMessageAsync(":white_check_mark: Your information has been updated.");
            }
            else
            {
                await reqChannel.SendMessageAsync(":white_check_mark: Your birthday has been recorded.");
            }
        }

        private async Task CmdZone(string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser)
        {
            if (param.Length != 2)
            {
                await reqChannel.SendMessageAsync(GenericError);
                return;
            }

            string btz = null;
            var user = Instance.GuildCache[reqChannel.Guild.Id].GetUser(reqUser.Id);
            if (!user.IsKnown)
            {
                await reqChannel.SendMessageAsync(":x: Can't set your time zone if your birth date isn't registered.");
                return;
            }

            try
            {
                btz = ParseTimeZone(param[1]);
            }
            catch (Exception ex)
            {
                reqChannel.SendMessageAsync(ex.Message).Wait();
                return;
            }
            await user.UpdateAsync(user.BirthMonth, user.BirthDay, btz, BotConfig.DatabaseSettings);

            await reqChannel.SendMessageAsync($":white_check_mark: Your time zone has been updated to **{btz}**.");
        }

        private async Task CmdRemove(string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser)
        {
            // Parameter count check
            if (param.Length != 1)
            {
                await reqChannel.SendMessageAsync(ExpectedNoParametersError);
                return;
            }

            // Extra detail: Send a notification if the user isn't actually known by the bot.
            bool known;
            var g = Instance.GuildCache[reqChannel.Guild.Id];
            known = g.GetUser(reqUser.Id).IsKnown;
            // Delete database and cache entry
            await g.DeleteUserAsync(reqUser.Id);
            if (!known)
            {
                await reqChannel.SendMessageAsync(":white_check_mark: I don't have your information. Nothing to remove.");
            }
            else
            {
                await reqChannel.SendMessageAsync(":white_check_mark: Your information has been removed.");
            }
        }

        private async Task CmdWhen(string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser)
        {
            // Requires a parameter
            if (param.Length == 1)
            {
                await reqChannel.SendMessageAsync(GenericError);
                return;
            }

            var search = param[1];
            if (param.Length == 3)
            {
                // param maxes out at 3 values. param[2] might contain part of the search string (if name has a space)
                search += " " + param[2];
            }

            SocketGuildUser searchTarget = null;

            ulong searchId = 0;
            if (!TryGetUserId(search, out searchId)) // ID lookup
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
                await reqChannel.SendMessageAsync(BadUserError);
                return;
            }

            var users = Instance.GuildCache[reqChannel.Guild.Id].Users;
            var searchTargetData = users.FirstOrDefault(u => u.UserId == searchTarget.Id);
            if (searchTargetData == null)
            {
                await reqChannel.SendMessageAsync("The given user does not exist or has not set a birthday.");
                return;
            }

            string result = Common.FormatName(searchTarget, false);
            result += ": ";
            result += $"`{searchTargetData.BirthDay:00}-{Common.MonthNames[searchTargetData.BirthMonth]}`";
            result += searchTargetData.TimeZone == null ? "" : $" - `{searchTargetData.TimeZone}`";

            await reqChannel.SendMessageAsync(result);
        }
    }
}
