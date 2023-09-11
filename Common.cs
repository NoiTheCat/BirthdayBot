using System.Text;

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
            if (member.Nickname !=  null) {
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

    /// <summary>
    /// An alternative to <see cref="SocketGuild.HasAllMembers"/>.
    /// Returns true if *most* members have been downloaded.
    /// Used as a workaround check due to Discord.Net occasionally unable to actually download all members.
    /// </summary>
    public static bool HasMostMembersDownloaded(SocketGuild guild) {
        if (guild.HasAllMembers) return true;
        if (guild.MemberCount > 30) {
            // For guilds of size over 30, require 85% or more of the members to be known
            // (26/30, 42/50, 255/300, etc)
            var threshold = (int)(guild.MemberCount * 0.85);
            return guild.DownloadedMemberCount >= threshold;
        } else {
            // For smaller guilds, fail if two or more members are missing
            return guild.MemberCount - guild.DownloadedMemberCount <= 2;
        }
    }
}
