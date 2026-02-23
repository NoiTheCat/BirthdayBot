using Discord;
using Discord.Interactions;
using NodaTime;
using System.Globalization;
using System.Text;

namespace BirthdayBot.InteractionModules;

public class ExportModule : BBModuleBase {
    public const string HelpCmdExport = "Generates a text file with all known and available birthdays.";

    public enum ExportFormat {
        [ChoiceDisplay("Simple, readable text file")]
        Default,
        [ChoiceDisplay("CSV with header")]
        Csv,
        [ChoiceDisplay("iCal (.ics) with recurring events")]
        ICal
    }

    delegate MemoryStream FileBuilder(IEnumerable<KnownGuildUser> list);

    [SlashCommand("export-birthdays", HelpCmdExport)]
    [DefaultMemberPermissions(GuildPermission.ManageGuild)]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task CmdExport(
        [Summary(description: "Specify the format of the exported data.")] ExportFormat format = ExportFormat.Default) {
        var deferred = await RefreshCacheAsync(Cache.FilterGetAllMissing());

        var bdlist = GetAllKnownUsers(Context.Guild.Id);

        var filename = "birthdaybot-" + Context.Guild.Id;
        FileBuilder contentSource;
        switch (format) {
            case ExportFormat.Csv:
                contentSource = ListExportCsv;
                filename += ".csv";
                break;
            case ExportFormat.ICal:
                contentSource = ListExportICal;
                filename += ".ics";
                break;
            default:
                contentSource = ListExportNormal;
                filename += ".txt";
                break;
        }
        var output = contentSource(bdlist);
        var outtext = $"Exported {bdlist.Count()} birthdays to file.";
        if (!deferred) {
            await RespondWithFileAsync(output, filename, text: outtext).ConfigureAwait(false);
        } else {
            await FollowupWithFileAsync(output, filename, text: outtext).ConfigureAwait(false);
            await DeleteOriginalResponseAsync().ConfigureAwait(false);
        }
    }

    private MemoryStream ListExportNormal(IEnumerable<KnownGuildUser> list) {
        // Output: "● Mon-dd: (user ID) Username [ - Nickname: (nickname)]"
        var result = new MemoryStream();
        var writer = new StreamWriter(result, Encoding.UTF8) { NewLine = "\r\n" };

        writer.WriteLine($"Birthdays in {Context.Guild.Name}");
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
        // crlf line ending is defined for CSV per RFC 4180 - we'd do it regardless

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
        writer.WriteLine("UserId,Username,DisplayName,Nickname,MonthDayDisp,Month,Day,TimeZone");
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

    private MemoryStream ListExportICal(IEnumerable<KnownGuildUser> list) {
        var result = new MemoryStream();
        var writer = new StreamWriter(result, Encoding.UTF8) { NewLine = "\r\n" };
        writer.WriteLine("BEGIN:VCALENDAR");
        writer.WriteLine("VERSION:2.0");
        writer.WriteLine("PRODID:-//NoiTheCat//BirthdayBot//EN");

        var dtstamp = SystemClock.Instance.GetCurrentInstant()
            .InUtc().ToString("yyyyMMdd'T'HHmmss'Z'", DateTimeFormatInfo.InvariantInfo);
        foreach (var item in list) {
            // Some lines may be too long.
            // iCal ics standard disallows lines over 75 bytes - some attempt is made to avoid that limit
            // Line continuations are defined as leading space in new line

            // FormatName is specific to Discord output, but this isn't for Discord. Need to return back to unescaped form.
            // A simple replace is not sufficient - any intended \ characters get stripped away.
            static string Unescape(string input) {
                var result = new StringBuilder();
                var priorIsSlash = false;
                foreach (var c in input) {
                    if (!priorIsSlash) {
                        if (c == '\\') priorIsSlash = true; // encountered slash with no prior slash. do not use in result
                        else result.Append(c);
                    } else {
                        // Previously encountered a backslash - preserve next character unconditionally
                        result.Append(c);
                        priorIsSlash = false;
                    }
                }
                return result.ToString();
            }

            writer.WriteLine("BEGIN:VEVENT");
            writer.WriteLine($"DTSTAMP:{dtstamp}");
            // Deterministic UID based on guildId xor userId
            writer.WriteLine($"UID:{Context.Guild.Id ^ item.UserId:00000000000000000000}@birthdaybot");
            writer.WriteLine($"DTSTART:2000{item.BirthDate.Month:00}{item.BirthDate.Day:00}"); // start at year 2000; time omitted
            writer.WriteLine("RRULE:FREQ=YEARLY");
            writer.WriteLine($"SUMMARY:Birthday: "); // Similar output to molochxte's contributed script
            writer.WriteLine($" {Unescape(item.DisplayName)}");
            writer.WriteLine("DESCRIPTION: Birthday for "); 
            writer.WriteLine($" {item.CacheUser.Username}\\n");
            writer.WriteLine($" From {Context.Guild.Name}\\n");
            writer.WriteLine(" Exported from Birthday Bot");
            writer.WriteLine("END:VEVENT");
        }

        writer.WriteLine("END:VCALENDAR");
        writer.Flush();
        result.Position = 0;
        return result;
    }
}
