using BirthdayBot.Data;
using Discord.Interactions;
using NodaTime;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace BirthdayBot.ApplicationCommands;

/// <summary>
/// Base class for our interaction module classes. Contains common data for use in implementing classes.
/// </summary>
public abstract partial class BotModuleBase : InteractionModuleBase<SocketInteractionContext> {
    protected const string MemberCacheEmptyError = ":warning: Please try the command again.";
    public const string AccessDeniedError = ":warning: You are not allowed to run this command.";

    protected const string HelpOptDate = "A date, including the month and day. For example, \"15 January\".";
    protected const string HelpOptZone = "A 'tzdata'-compliant time zone name. See help for more details.";

    /// <summary>
    /// The corresponding <see cref="ShardInstance"/> handling the client where the command originated from.
    /// </summary>
    [NotNull]
    public ShardInstance? Shard { get; set; }

    protected static IReadOnlyDictionary<string, string> TzNameMap { get; }

    static BotModuleBase() {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in DateTimeZoneProviders.Tzdb.Ids) dict.Add(name, name);
        TzNameMap = new ReadOnlyDictionary<string, string>(dict);
    }

    /// <summary>
    /// Checks given time zone input. Returns a valid string for use with NodaTime,
    /// throwing a FormatException if the input is not recognized.
    /// </summary>
    protected static string ParseTimeZone(string tzinput) {
        if (!TzNameMap.TryGetValue(tzinput, out var tz))
            throw new FormatException(":x: Unknown time zone name.\n" +
                "To find your time zone, please refer to: https://zones.arilyn.cc/");
        return tz!;
    }

    /// <summary>
    /// An alternative to <see cref="SocketGuild.HasAllMembers"/> to be called by command handlers needing a full member cache.
    /// Creates a download request if necessary.
    /// </summary>
    /// <returns>
    /// True if the member cache is already filled, false otherwise.
    /// </returns>
    /// <remarks>
    /// Any updates to the member cache aren't accessible until the event handler finishes execution, meaning proactive downloading
    /// is necessary, and is handled by <seealso cref="BackgroundServices.AutoUserDownload"/>. In situations where
    /// this approach fails, this is to be called, and the user must be asked to attempt the command again if this returns false.
    /// </remarks>
    protected static async Task<bool> HasMemberCacheAsync(SocketGuild guild) {
        if (Common.HasMostMembersDownloaded(guild)) return true;
        // Event handling thread hangs if awaited normally or used with Task.Run
        await Task.Factory.StartNew(guild.DownloadUsersAsync).ConfigureAwait(false);
        return false;
    }

    #region Date parsing
    const string FormatError = ":x: Unrecognized date format. The following formats are accepted, as examples: "
            + "`15-jan`, `jan-15`, `15 jan`, `jan 15`, `15 January`, `January 15`.";

    [GeneratedRegex(@"^(?<day>\d{1,2})[ -](?<month>[A-Za-z]+)$")]
    private static partial Regex DateParser1();
    [GeneratedRegex(@"^(?<month>[A-Za-z]+)[ -](?<day>\d{1,2})$")]
    private static partial Regex DateParser2();

    /// <summary>
    /// Parses a date input.
    /// </summary>
    /// <returns>Tuple: month, day</returns>
    /// <exception cref="FormatException">
    /// Thrown for any parsing issue. Reason is expected to be sent to Discord as-is.
    /// </exception>
    protected static (int, int) ParseDate(string dateInput) {
        var m = DateParser1().Match(dateInput);
        if (!m.Success) {
            // Flip the fields around, try again
            m = DateParser2().Match(dateInput);
            if (!m.Success) throw new FormatException(FormatError);
        }

        int day, month;
        string monthVal;
        try {
            day = int.Parse(m.Groups["day"].Value);
        } catch (FormatException) {
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
    private static (int, int) GetMonth(string input) {
        return input.ToLower() switch {
            "jan" or "january" => (1, 31),
            "feb" or "february" => (2, 29),
            "mar" or "march" => (3, 31),
            "apr" or "april" => (4, 30),
            "may" => (5, 31),
            "jun" or "june" => (6, 30),
            "jul" or "july" => (7, 31),
            "aug" or "august" => (8, 31),
            "sep" or "september" => (9, 30),
            "oct" or "october" => (10, 31),
            "nov" or "november" => (11, 30),
            "dec" or "december" => (12, 31),
            _ => throw new FormatException($":x: Can't determine month name `{input}`. Check your spelling and try again."),
        };
    }

    /// <summary>
    /// Returns a string representing a birthday in a consistent format.
    /// </summary>
    protected static string FormatDate(int month, int day) => $"{day:00}-{Common.MonthNames[month]}";
    #endregion

    #region Listing helper methods
    /// <summary>
    /// Fetches all guild birthdays and places them into an easily usable structure.
    /// Users currently not in the guild are not included in the result.
    /// </summary>
    protected static List<ListItem> GetSortedUserList(SocketGuild guild) {
        using var db = new BotDatabaseContext();
        var query = from row in db.UserEntries
                    where row.GuildId == guild.Id
                    orderby row.BirthMonth, row.BirthDay
                    select new {
                        row.UserId,
                        Month = row.BirthMonth,
                        Day = row.BirthDay,
                        Zone = row.TimeZone
                    };

        var result = new List<ListItem>();
        foreach (var row in query) {
            var guildUser = guild.GetUser(row.UserId);
            if (guildUser == null) continue; // Skip user not in guild

            result.Add(new ListItem() {
                BirthMonth = row.Month,
                BirthDay = row.Day,
                DateIndex = DateIndex(row.Month, row.Day),
                UserId = guildUser.Id,
                DisplayName = Common.FormatName(guildUser, false),
                TimeZone = row.Zone
            });
        }
        return result;
    }

    protected static int DateIndex(int month, int day) {
        var dateindex = 0;
        // Add month offsets
        if (month > 1) dateindex += 31; // Offset January
        if (month > 2) dateindex += 29; // Offset February (incl. leap day)
        if (month > 3) dateindex += 31; // etc
        if (month > 4) dateindex += 30;
        if (month > 5) dateindex += 31;
        if (month > 6) dateindex += 30;
        if (month > 7) dateindex += 31;
        if (month > 8) dateindex += 31;
        if (month > 9) dateindex += 30;
        if (month > 10) dateindex += 31;
        if (month > 11) dateindex += 30;
        dateindex += day;
        return dateindex;
    }

    protected struct ListItem {
        public int DateIndex;
        public int BirthMonth;
        public int BirthDay;
        public ulong UserId;
        public string DisplayName;
        public string? TimeZone;
    }
    #endregion
}