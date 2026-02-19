using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Text;

namespace BirthdayBot.InteractionModules;

public class ExportModule : BBModuleBase {
    public const string HelpCmdExport = "Generates a text file with all known and available birthdays.";

    [SlashCommand("export-birthdays", HelpCmdExport)]
    [DefaultMemberPermissions(GuildPermission.ManageGuild)]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task CmdExport([Summary(description: "Specify whether to export the list in CSV format.")] bool asCsv = false) {
        var deferred = await RefreshCacheAsync(Cache.FilterGetAllMissing());

        var bdlist = GetAllKnownUsers(Context.Guild.Id);

        var filename = "birthdaybot-" + Context.Guild.Id;
        Stream fileoutput;
        if (asCsv) {
            fileoutput = ListExportCsv(bdlist);
            filename += ".csv";
        } else {
            fileoutput = ListExportNormal(Context.Guild.Name, bdlist);
            filename += ".txt";
        }
        var outtext = $"Exported {bdlist.Count()} birthdays to file.";
        if (!deferred) {
            await RespondWithFileAsync(fileoutput, filename, text: outtext).ConfigureAwait(false);
        } else {
            await FollowupWithFileAsync(fileoutput, filename, text: outtext).ConfigureAwait(false);
            await DeleteOriginalResponseAsync().ConfigureAwait(false);
        }
    }

    private static MemoryStream ListExportNormal(string guildName, IEnumerable<KnownGuildUser> list) {
        // Output: "● Mon-dd: (user ID) Username [ - Nickname: (nickname)]"
        var result = new MemoryStream();
        var writer = new StreamWriter(result, Encoding.UTF8) { NewLine = "\r\n" };
        // crlf explicitly set for maximum compatibility

        writer.WriteLine($"Birthdays in {guildName}");
        writer.WriteLine();
        foreach (var item in list) {
            writer.Write($"● {FormatDate(item.BirthDate)}: ");
            writer.Write(item.UserId);
            writer.Write(" " + item.CacheUser.Username);
            if (item.CacheUser.GlobalName != null) writer.Write($" ({item.CacheUser.GlobalName})");
            if (item.CacheUser.GuildNickname != null) writer.Write(" - Nickname: " + item.CacheUser.GuildNickname);
            if (item.TimeZone != null) writer.Write(" | Time zone: " + item.TimeZone.Id);
            writer.WriteLine();
        }
        writer.Flush();
        result.Position = 0;
        return result;
    }

    private static MemoryStream ListExportCsv(IEnumerable<KnownGuildUser> list) {
        // Output: User ID, Username, Nickname, Month-Day, Month, Day
        var result = new MemoryStream();
        var writer = new StreamWriter(result, Encoding.UTF8) { NewLine = "\r\n" };
        // crlf line ending is defined for CSV per RFC 4180

        static string csvEscape(string? input) {
            if (input is null) return string.Empty;
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
        writer.WriteLine();
        foreach (var item in list) {
            writer.Write(item.UserId);
            writer.Write(',');
            writer.Write(csvEscape(item.CacheUser.Username!));
            writer.Write(',');
            writer.Write(csvEscape(item.CacheUser.GlobalName)); // may be empty
            writer.Write(',');
            writer.Write(csvEscape(item.CacheUser.GuildNickname)); // may be empty
            writer.Write(',');
            writer.Write($"{FormatDate(item.BirthDate)}");
            writer.Write(',');
            writer.Write(item.BirthDate.Month);
            writer.Write(',');
            writer.Write(item.BirthDate.Day);
            writer.Write(',');
            writer.Write(csvEscape(item.TimeZone?.Id)); // may be empty
            writer.WriteLine();
        }
        writer.Flush();
        result.Position = 0;
        return result;
    }
}
