using BirthdayBot.Data;
using Discord.WebSocket;
using NodaTime;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BirthdayBot.UserInterface
{
    /// <summary>
    /// Common base class for common constants and variables.
    /// </summary>
    internal abstract class CommandsCommon
    {
#if DEBUG
        public const string CommandPrefix = "bt.";
#else
        public const string CommandPrefix = "bb.";
#endif
        public const string BadUserError = ":x: Unable to find user. Specify their `@` mention or their ID.";
        public const string ParameterError = ":x: Invalid usage. Refer to how to use the command and try again.";
        public const string NoParameterError = ":x: This command does not accept any parameters.";
        public const string InternalError = ":x: An internal bot error occurred. The bot maintainer has been notified of the issue.";

        public delegate Task CommandHandler(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel, SocketGuildUser reqUser);

        protected static Dictionary<string, string> TzNameMap {
            get {
                // Because IDateTimeZoneProvider.GetZoneOrNull is not case sensitive:
                // Getting every existing zone name and mapping it onto a dictionary. Now a case-insensitive
                // search can be made with the accepted value retrieved as a result.
                if (_tzNameMap == null)
                {
                    _tzNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var name in DateTimeZoneProviders.Tzdb.Ids) _tzNameMap.Add(name, name);
                }
                return _tzNameMap;
            }
        }
        protected static Regex ChannelMention { get; } = new Regex(@"<#(\d+)>");
        protected static Regex UserMention { get; } = new Regex(@"\!?(\d+)>");
        private static Dictionary<string, string> _tzNameMap; // Value set by getter property on first read

        protected BirthdayBot Instance { get; }
        protected Configuration BotConfig { get; }
        protected DiscordShardedClient Discord { get; }

        protected CommandsCommon(BirthdayBot inst, Configuration db)
        {
            Instance = inst;
            BotConfig = db;
            Discord = inst.DiscordClient;
        }

        /// <summary>
        /// On command dispatcher initialization, it will retrieve all available commands through here.
        /// </summary>
        public abstract IEnumerable<(string, CommandHandler)> Commands { get; }

        /// <summary>
        /// Checks given time zone input. Returns a valid string for use with NodaTime.
        /// </summary>
        protected string ParseTimeZone(string tzinput)
        {
            string tz = null;
            if (tzinput != null)
            {
                // Just check if the input exists in the map. Get the "true" value, or reject it altogether.
                if (!TzNameMap.TryGetValue(tzinput, out tz))
                {
                    throw new FormatException(":x: Unexpected time zone name."
                        + $" Refer to `{CommandPrefix}help-tzdata` to help determine the correct value.");
                }
            }
            return tz;
        }

        /// <summary>
        /// Given user input where a user-like parameter is expected, attempts to resolve to an ID value.
        /// Input must be a mention or explicit ID. No name resolution is done here.
        /// </summary>
        protected bool TryGetUserId(string input, out ulong result)
        {
            string doParse;
            var m = UserMention.Match(input);
            if (m.Success) doParse = m.Groups[1].Value; 
            else doParse = input;

            ulong resultVal;
            if (ulong.TryParse(doParse, out resultVal))
            {
                result = resultVal;
                return true;
            }

            result = default;
            return false;
        }
    }
}
