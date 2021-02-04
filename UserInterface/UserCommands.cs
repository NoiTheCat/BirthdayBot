using BirthdayBot.Data;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BirthdayBot.UserInterface
{
    internal class UserCommands : CommandsCommon
    {
        public UserCommands(Configuration db) : base(db) { }

        public override IEnumerable<(string, CommandHandler)> Commands
            => new List<(string, CommandHandler)>()
            {
                ("set", CmdSet),
                ("zone", CmdZone),
                ("remove", CmdRemove)
            };

        #region Date parsing
        const string FormatError = ":x: Unrecognized date format. The following formats are accepted, as examples: "
                + "`15-jan`, `jan-15`, `15 jan`, `jan 15`, `15 January`, `January 15`.";

        private static readonly Regex DateParse1 = new Regex(@"^(?<day>\d{1,2})[ -](?<month>[A-Za-z]+)$", RegexOptions.Compiled);
        private static readonly Regex DateParse2 = new Regex(@"^(?<month>[A-Za-z]+)[ -](?<day>\d{1,2})$", RegexOptions.Compiled);

        /// <summary>
        /// Parses a date input.
        /// </summary>
        /// <returns>Tuple: month, day</returns>
        /// <exception cref="FormatException">
        /// Thrown for any parsing issue. Reason is expected to be sent to Discord as-is.
        /// </exception>
        private (int, int) ParseDate(string dateInput)
        {
            var m = DateParse1.Match(dateInput);
            if (!m.Success)
            {
                // Flip the fields around, try again
                m = DateParse2.Match(dateInput);
                if (!m.Success) throw new FormatException(FormatError);
            }

            int day, month;
            string monthVal;
            try
            {
                day = int.Parse(m.Groups["day"].Value);
            }
            catch (FormatException)
            {
                throw new Exception(FormatError);
            }
            monthVal = m.Groups["month"].Value;

            int dayUpper; // upper day of month check
            (month, dayUpper) = GetMonth(monthVal);

            if (day == 0 || day > dayUpper) throw new FormatException(":x: The date you specified is not a valid calendar date.");

            return (month, day);
        }

        /// <summary>
        /// Returns information for a given month input.
        /// </summary>
        /// <param name="input"></param>
        /// <returns>Tuple: Month value, upper limit of days in the month</returns>
        /// <exception cref="FormatException">
        /// Thrown on error. Send out to Discord as-is.
        /// </exception>
        private (int, int) GetMonth(string input)
        {
            switch (input.ToLower())
            {
                case "jan":
                case "january":
                    return (1, 31);
                case "feb":
                case "february":
                    return (2, 29);
                case "mar":
                case "march":
                    return (3, 31);
                case "apr":
                case "april":
                    return (4, 30);
                case "may":
                    return (5, 31);
                case "jun":
                case "june":
                    return (6, 30);
                case "jul":
                case "july":
                    return (7, 31);
                case "aug":
                case "august":
                    return (8, 31);
                case "sep":
                case "september":
                    return (9, 30);
                case "oct":
                case "october":
                    return (10, 31);
                case "nov":
                case "november":
                    return (11, 30);
                case "dec":
                case "december":
                    return (12, 31);
                default:
                    throw new FormatException($":x: Can't determine month name `{input}`. Check your spelling and try again.");
            }
        }
        #endregion

        #region Documentation
        public static readonly CommandDocumentation DocSet =
            new CommandDocumentation(new string[] { "set (date) [zone]" }, "Registers your birth date. Time zone is optional.",
                $"`{CommandPrefix}set jan-31`, `{CommandPrefix}set 15-aug America/Los_Angeles`.");
        public static readonly CommandDocumentation DocZone =
            new CommandDocumentation(new string[] { "zone (zone)" }, "Sets your local time zone. "
                + $"See also `{CommandPrefix}help-tzdata`.", null);
        public static readonly CommandDocumentation DocRemove =
            new CommandDocumentation(new string[] { "remove" }, "Removes your birthday information from this bot.", null);
        #endregion

        private async Task CmdSet(ShardInstance instance, GuildConfiguration gconf,
                                  string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser)
        {
            // Requires *some* parameter.
            if (param.Length < 2)
            {
                await reqChannel.SendMessageAsync(ParameterError, embed: DocSet.UsageEmbed).ConfigureAwait(false);
                return;
            }

            // Date format accepts spaces, and then we look for an additional parameter.
            // This is weird. Gotta compensate.
            var fullinput = "";
            for (int i = 1; i < param.Length; i++)
            {
                fullinput += " " + param[i];
            }
            fullinput = fullinput[1..];
            // Attempt to get last "parameter"; check if it's a time zone value
            string timezone = null;
            var fli = fullinput.LastIndexOf(' ');
            if (fli != -1)
            {
                var tzstring = fullinput[(fli+1)..];
                try
                {
                    timezone = ParseTimeZone(tzstring);
                    // If we got here, last parameter was indeed a time zone. Trim it away for what comes next.
                    fullinput = fullinput[0..fli];
                }
                catch (FormatException) { } // Was not a time zone name. Do nothing.
            }

            int bmonth, bday;
            try
            {
                (bmonth, bday) = ParseDate(fullinput);
            }
            catch (FormatException ex)
            {
                // Our parse method's FormatException has its message to send out to Discord.
                reqChannel.SendMessageAsync(ex.Message, embed: DocSet.UsageEmbed).Wait();
                return;
            }

            // Parsing successful. Update user information.
            bool known; // Extra detail: Bot's response changes if the user was previously unknown.
            try
            {
                var user = await GuildUserConfiguration.LoadAsync(gconf.GuildId, reqUser.Id).ConfigureAwait(false);
                known = user.IsKnown;
                await user.UpdateAsync(bmonth, bday, timezone).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Program.Log("Error", ex.ToString());
                // TODO webhook report
                reqChannel.SendMessageAsync(InternalError).Wait();
                return;
            }
            if (known)
            {
                await reqChannel.SendMessageAsync(":white_check_mark: Your information has been updated.")
                    .ConfigureAwait(false);
            }
            else
            {
                await reqChannel.SendMessageAsync(":white_check_mark: Your birthday has been recorded.")
                    .ConfigureAwait(false);
            }
        }

        private async Task CmdZone(ShardInstance instance, GuildConfiguration gconf,
                                   string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser)
        {
            if (param.Length != 2)
            {
                await reqChannel.SendMessageAsync(ParameterError, embed: DocZone.UsageEmbed).ConfigureAwait(false);
                return;
            }

            var user = await GuildUserConfiguration.LoadAsync(gconf.GuildId, reqUser.Id).ConfigureAwait(false);
            if (!user.IsKnown)
            {
                await reqChannel.SendMessageAsync(":x: You may only update your time zone when you have a birthday registered."
                    + $" Refer to the `{CommandPrefix}set` command.", embed: DocZone.UsageEmbed)
                    .ConfigureAwait(false);
                return;
            }

            string btz;
            try
            {
                btz = ParseTimeZone(param[1]);
            }
            catch (Exception ex)
            {
                reqChannel.SendMessageAsync(ex.Message, embed: DocZone.UsageEmbed).Wait();
                return;
            }
            await user.UpdateAsync(user.BirthMonth, user.BirthDay, btz).ConfigureAwait(false);

            await reqChannel.SendMessageAsync($":white_check_mark: Your time zone has been updated to **{btz}**.")
                .ConfigureAwait(false);
        }

        private async Task CmdRemove(ShardInstance instance, GuildConfiguration gconf,
                                     string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser)
        {
            // Parameter count check
            if (param.Length != 1)
            {
                await reqChannel.SendMessageAsync(NoParameterError, embed: DocRemove.UsageEmbed).ConfigureAwait(false);
                return;
            }

            // Extra detail: Send a notification if the user isn't actually known by the bot.
            bool known;
            var u = await GuildUserConfiguration.LoadAsync(gconf.GuildId, reqUser.Id).ConfigureAwait(false);
            known = u.IsKnown;
            await u.DeleteAsync().ConfigureAwait(false);
            if (!known)
            {
                await reqChannel.SendMessageAsync(":white_check_mark: This bot already does not contain your information.")
                    .ConfigureAwait(false);
            }
            else
            {
                await reqChannel.SendMessageAsync(":white_check_mark: Your information has been removed.")
                    .ConfigureAwait(false);
            }
        }
    }
}
