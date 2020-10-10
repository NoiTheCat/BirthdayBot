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

        /// <summary>
        /// Parses date parameter. Strictly takes dd-MMM or MMM-dd only. Eliminates ambiguity over dd/mm vs mm/dd.
        /// </summary>
        /// <returns>Tuple: month, day</returns>
        /// <exception cref="FormatException">Thrown for any parsing issue. Reason is expected to be sent to Discord as-is.</exception>
        private (int, int) ParseDate(string dateInput)
        {
            const string FormatError = ":x: Incorrect date format. Use a three-letter abbreviation and a number separated by "
                + "hyphen to specify a date. Examples: `jan-15` `23-aug` `may-12` `5-jun`";
            // Not doing DateTime.Parse. Setting it up is rather complicated, and it's probably case sensitive.
            // Admittedly, doing it the way it's being done here probably isn't any better.
            var m = Regex.Match(dateInput, @"^(?<day>\d{1,2})-(?<month>[A-Za-z]{3})$");
            if (!m.Success)
            {
                // Flip the fields around, try again
                m = Regex.Match(dateInput, @"^(?<month>[A-Za-z]{3})-(?<day>\d{1,2})$");
                if (!m.Success) throw new FormatException(FormatError);
            }
            int day;
            try
            {
                day = int.Parse(m.Groups["day"].Value);
            }
            catch (FormatException)
            {
                throw new Exception(FormatError);
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
            // Requires one parameter. Optionally two.
            if (param.Length < 2 || param.Length > 3)
            {
                await reqChannel.SendMessageAsync(ParameterError, embed: DocSet.UsageEmbed).ConfigureAwait(false);
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
                reqChannel.SendMessageAsync(ex.Message, embed: DocSet.UsageEmbed).Wait();
                return;
            }

            // Parsing successful. Update user information.
            bool known; // Extra detail: Bot's response changes if the user was previously unknown.
            try
            {
                var user = await GuildUserConfiguration.LoadAsync(gconf.GuildId, reqUser.Id).ConfigureAwait(false);
                known = user.IsKnown;
                await user.UpdateAsync(bmonth, bday, btz).ConfigureAwait(false);
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
