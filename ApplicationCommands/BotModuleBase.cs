using Discord.Interactions;
using NodaTime;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace BirthdayBot.ApplicationCommands;

/// <summary>
/// Base class for our interaction module classes. Contains common data for use in implementing classes.
/// </summary>
public abstract class BotModuleBase : InteractionModuleBase<SocketInteractionContext> {
    protected const string HelpPfxModOnly = "Bot moderators only: ";
    protected const string ErrGuildOnly = ":x: This command can only be run within a server.";
    protected const string ErrNotAllowed = ":x: Only server moderators may use this command.";
    protected const string MemberCacheEmptyError = ":warning: Please try the command again.";
    public const string AccessDeniedError = ":warning: You are not allowed to run this command.";

    protected const string HelpOptDate = "A date, including the month and day. For example, \"15 January\".";
    protected const string HelpOptZone = "A 'tzdata'-compliant time zone name. See help for more details.";

#pragma warning disable CS8618
    public DiscordSocketClient BotClient { get; set; }
    public ShardInstance Instance { get; set; }
#pragma warning restore CS8618

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
        if (!TzNameMap.TryGetValue(tzinput, out string? tz))
            throw new FormatException(":x: Unknown time zone name.\n" +
                "To find your time zone, please refer to: https://kevinnovak.github.io/Time-Zone-Picker/");
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

    private static readonly Regex DateParse1 = new(@"^(?<day>\d{1,2})[ -](?<month>[A-Za-z]+)$", RegexOptions.Compiled);
    private static readonly Regex DateParse2 = new(@"^(?<month>[A-Za-z]+)[ -](?<day>\d{1,2})$", RegexOptions.Compiled);

    /// <summary>
    /// Parses a date input.
    /// </summary>
    /// <returns>Tuple: month, day</returns>
    /// <exception cref="FormatException">
    /// Thrown for any parsing issue. Reason is expected to be sent to Discord as-is.
    /// </exception>
    protected static (int, int) ParseDate(string dateInput) {
        var m = DateParse1.Match(dateInput);
        if (!m.Success) {
            // Flip the fields around, try again
            m = DateParse2.Match(dateInput);
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
    #endregion
}
