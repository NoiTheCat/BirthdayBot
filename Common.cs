using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Text;

namespace BirthdayBot
{
    static class Common
    {
        /// <summary>
        /// Formats a user's name to a consistent, readable format which makes use of their nickname.
        /// </summary>
        public static string FormatName(SocketGuildUser member, bool ping)
        {
            if (ping) return member.Mention;

            string escapeFormattingCharacters(string input)
            {
                var result = new StringBuilder();
                foreach (var c in input)
                {
                    if (c == '\\' || c == '_' || c == '~' || c == '*')
                    {
                        result.Append('\\');
                    }
                    result.Append(c);
                }
                return result.ToString();
            }

            var username = escapeFormattingCharacters(member.Username);
            if (member.Nickname != null)
            {
                return $"**{escapeFormattingCharacters(member.Nickname)}** ({username}#{member.Discriminator})";
            }
            return $"**{username}**#{member.Discriminator}";
        }

        public static readonly Dictionary<int, string> MonthNames = new Dictionary<int, string>()
        {
            {1, "Jan"}, {2, "Feb"}, {3, "Mar"}, {4, "Apr"}, {5, "May"}, {6, "Jun"},
            {7, "Jul"}, {8, "Aug"}, {9, "Sep"}, {10, "Oct"}, {11, "Nov"}, {12, "Dec"}
        };

        public static string BotUptime => (DateTimeOffset.UtcNow - Program.BotStartTime).ToString("d' days, 'hh':'mm':'ss");
    }
}
