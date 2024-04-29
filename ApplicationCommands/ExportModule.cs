using Discord.Interactions;
using System.Text;

namespace BirthdayBot.ApplicationCommands;
public class ExportModule : BotModuleBase {
    public const string HelpCmdExport = "Generates a text file with all known and available birthdays.";

    [SlashCommand("export-birthdays", HelpCmdExport)]
    [DefaultMemberPermissions(GuildPermission.ManageGuild)]
    [EnabledInDm(false)]
    public async Task CmdExport([Summary(description: "Specify whether to export the list in CSV format.")] bool asCsv = false) {
        if (!await HasMemberCacheAsync(Context.Guild)) {
            await RespondAsync(MemberCacheEmptyError, ephemeral: true);
            return;
        }

        var bdlist = GetSortedUserList(Context.Guild);

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

    private static MemoryStream ListExportNormal(SocketGuild guild, IEnumerable<ListItem> list) {
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
            writer.Write(" " + user.Username);
            if (user.DiscriminatorValue != 0) writer.Write($"#{user.Discriminator}");
            if (user.GlobalName != null) writer.Write($" ({user.GlobalName})");
            if (user.Nickname != null) writer.Write(" - Nickname: " + user.Nickname);
            if (item.TimeZone != null) writer.Write(" | Time zone: " + item.TimeZone);
            writer.WriteLine();
        }
        writer.Flush();
        result.Position = 0;
        return result;
    }

    private static MemoryStream ListExportCsv(SocketGuild guild, IEnumerable<ListItem> list) {
        // Output: User ID, Username, Nickname, Month-Day, Month, Day
        var result = new MemoryStream();
        var writer = new StreamWriter(result, Encoding.UTF8);

        static string csvEscape(string input) {
            var result = new StringBuilder();
            result.Append('"');
            foreach (var ch in input) {
                if (ch == '"') result.Append('"');
                result.Append(ch);
            }
            result.Append('"');
            return result.ToString();
        }

        // Conforming to RFC 4180; with header
        writer.Write("UserId,Username,DisplayName,Nickname,MonthDayDisp,Month,Day,TimeZone");
        writer.Write("\r\n"); // crlf line break is specified by the standard
        foreach (var item in list) {
            var user = guild.GetUser(item.UserId);
            if (user == null) continue; // User disappeared in the instant between getting list and processing
            writer.Write(item.UserId);
            writer.Write(',');
            writer.Write(csvEscape(user.Username));
            if (user.DiscriminatorValue != 0) writer.Write($"#{user.Discriminator}");
            writer.Write(',');
            if (user.GlobalName != null) writer.Write(csvEscape(user.GlobalName));
            writer.Write(',');
            if (user.Nickname != null) writer.Write(csvEscape(user.Nickname));
            writer.Write(',');
            writer.Write($"{Common.MonthNames[item.BirthMonth]}-{item.BirthDay:00}");
            writer.Write(',');
            writer.Write(item.BirthMonth);
            writer.Write(',');
            writer.Write(item.BirthDay);
            writer.Write(',');
            writer.Write(item.TimeZone);
            writer.Write("\r\n");
        }
        writer.Flush();
        result.Position = 0;
        return result;
    }
}