using System.Text;
using Discord.WebSocket;

namespace BirthdayBot;

static class Common {
    /// <summary>
    /// Formats a user's name to a consistent, readable format which makes use of their nickname.
    /// </summary>
    public static string FormatName(SocketGuildUser member, bool ping) {
        if (ping) return member.Mention;

        static string escapeFormattingCharacters(string input) {
            var result = new StringBuilder();
            foreach (var c in input) {
                if (c is '\\' or '_' or '~' or '*' or '@' or '`') {
                    result.Append('\\');
                }
                result.Append(c);
            }
            return result.ToString();
        }

        if (member.DiscriminatorValue == 0) {
            var username = escapeFormattingCharacters(member.GlobalName ?? member.Username);
            if (member.Nickname != null) {
                return $"{escapeFormattingCharacters(member.Nickname)} ({username})";
            }
            return username;
        } else {
            var username = escapeFormattingCharacters(member.Username);
            if (member.Nickname != null) {
                return $"{escapeFormattingCharacters(member.Nickname)} ({username}#{member.Discriminator})";
            }
            return $"{username}#{member.Discriminator}";
        }
    }

    public static Dictionary<int, string> MonthNames { get; } = new() {
        { 1, "Jan" }, { 2, "Feb" }, { 3, "Mar" }, { 4, "Apr" }, { 5, "May" }, { 6, "Jun" },
        { 7, "Jul" }, { 8, "Aug" }, { 9, "Sep" }, { 10, "Oct" }, { 11, "Nov" }, { 12, "Dec" }
    };

}
