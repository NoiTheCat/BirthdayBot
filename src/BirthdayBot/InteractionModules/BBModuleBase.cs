using System.Text.RegularExpressions;
using BirthdayBot.Data;
using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NoiPublicBot;

namespace BirthdayBot.InteractionModules;

public partial class BBModuleBase : InteractionModuleBase<SocketInteractionContext> {
    protected const string MemberCacheEmptyError = ":warning: Please try the command again.";
    public const string AccessDeniedError = ":warning: You are not allowed to run this command.";

    protected const string HelpOptDate = "A date, including the month and day. For example, \"15 January\".";
    protected const string HelpOptZone = "A 'tzdata'-compliant time zone name. See help for more details.";

    // Injected by DI:
    public ShardInstance Shard { get; set; } = null!;
    public BotDatabaseContext DbContext { get; set; } = null!;
    public LocalCache Cache { get; set; } = null!;

    // Opportunistically caches user data coming in via interactions.
    public override Task BeforeExecuteAsync(ICommandInfo command) {
        if (Context.User is IGuildUser incoming) Cache.Update(incoming);
        return base.BeforeExecuteAsync(command);
    }

    /// <summary>
    /// Checks given time zone input, throwing a FormatException if the input is not recognized as one.
    /// </summary>
    protected static DateTimeZone ParseTimeZone(string tzinput) {
        var tzdb = DateTimeZoneProviders.Tzdb;
        var result = tzdb.GetZoneOrNull(tzinput);
        if (result != null) return result;

        var search = tzdb.Ids.FirstOrDefault(t => string.Equals(t, tzinput, StringComparison.OrdinalIgnoreCase));
        if (search != null) return tzdb.GetZoneOrNull(search)!;

        throw new FormatException(":x: Unknown time zone name.\n" +
                "To find your time zone, please refer to: https://zones.arilyn.cc/");
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
    /// <exception cref="FormatException">
    /// Thrown for any parsing issue. Reason is expected to be sent to Discord as-is.
    /// </exception>
    // TODO replace with native date input?
    protected static LocalDate ParseDate(string dateInput) {
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

        // TODO look into framework's localization stuff, may be able to convert better
        month = GetMonth(monthVal);

        try {
            return new(2000, month, day);
        } catch (ArgumentOutOfRangeException) {
            throw new FormatException(":x: The date you specified is not a valid calendar date.");
        }
    }

    /// <summary>
    /// Returns information for a given month input.
    /// </summary>
    /// <param name="input"></param>
    /// <returns>Tuple: Month value, upper limit of days in the month</returns>
    /// <exception cref="FormatException">
    /// Thrown on error. Send out to Discord as-is.
    /// </exception>
    private static int GetMonth(string input) {
        return input.ToLower() switch {
            "jan" or "january" => 1,
            "feb" or "february" => 2,
            "mar" or "march" => 3,
            "apr" or "april" => 4,
            "may" => 5,
            "jun" or "june" => 6,
            "jul" or "july" => 7,
            "aug" or "august" => 8,
            "sep" or "september" => 9,
            "oct" or "october" => 10,
            "nov" or "november" => 11,
            "dec" or "december" => 12,
            _ => throw new FormatException($":x: Can't determine month name `{input}`. Check your spelling and try again."),
        };
    }

    /// <summary>
    /// Returns a string representing a birthday in a consistent format.
    /// </summary>
    protected static string FormatDate(LocalDate date) => $"{date.Day:00}-{Common.MonthNames[date.Month]}";
    #endregion

    #region Listing helper methods
    /// <summary>
    /// Fetches all guild birthdays and places them into an easily usable structure.
    /// Users currently not in the cache are excluded from the result.
    /// </summary>
    // TODO still needed?
    protected List<ListItem> GetSortedUserList(ulong guildId) {
        var query = from row in DbContext.UserEntries.AsNoTracking()
                    where row.GuildId == guildId
                    orderby row.BirthDate ascending
                    select new {
                        row.UserId,
                        Date = row.BirthDate,
                        Zone = row.TimeZone
                    };

        var result = new List<ListItem>();
        var users = Cache.GetGuild(guildId);
        if (users is null) return [];
        foreach (var row in query) {
            if (!users.TryGetValue(row.UserId, out var cval)) continue; // Skip user not cached
            result.Add(new ListItem() {
                BirthDate = row.Date,
                UserId = row.UserId,
                DisplayName = cval.FormatName(),
                TimeZone = row.Zone?.Id
            });
        }
        return result;
    }

    protected record ListItem {
        public LocalDate BirthDate;
        public ulong UserId;
        public required string DisplayName;
        public string? TimeZone;
    }
    #endregion

    // For use when responding directly to user input
    protected async Task<bool> RefreshCacheAsync(LocalCache.CacheFetchFilter filter) {
        const string BusyDownloading = Constants.LoadingEmote + " Please wait a moment. Gathering data...";

        var wasDeferred = false;
        // casting a wide net here...
        var refresh = Cache.RequestGuildRefreshAsync(DbContext, Context.Guild.Id, filter);
        if (!refresh.IsCompleted) {
            // This may take a while
            wasDeferred = true;
            await RespondAsync(BusyDownloading).ConfigureAwait(false);
            await refresh.ConfigureAwait(false);
        }
        // Run a second time in case we got an ongoing task with a narrower filter than requested
        refresh = Cache.RequestGuildRefreshAsync(DbContext, Context.Guild.Id, filter);
        if (!refresh.IsCompleted) {
            if (!wasDeferred) {
                wasDeferred = true;
                await RespondAsync(BusyDownloading).ConfigureAwait(false);
                await refresh.ConfigureAwait(false);
            }
        }
        return wasDeferred;
    }
}
