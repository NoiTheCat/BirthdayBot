using System.Text.RegularExpressions;
using BirthdayBot.Data;
using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NoiPublicBot;
using NoiPublicBot.Cache;

namespace BirthdayBot.InteractionModules;

public partial class BBModuleBase : InteractionModuleBase<SocketInteractionContext> {
    protected const string MemberCacheEmptyError = ":warning: Please try the command again.";
    public const string AccessDeniedError = ":warning: You are not allowed to run this command.";

    protected const string HelpOptDate = "A date, including the month and day. For example, \"15 January\".";
    protected const string HelpOptZone = "A 'tzdata'-compliant time zone name. See help for more details.";
    protected const string HelpBool = "True to enable, False to disable.";

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

    /// <summary>
    /// Checks if the server allows ephemeral command confirmations.
    /// </summary>
    protected bool IsEphemeralSet()
        => DbContext.GuildConfigurations.Where(r => r.GuildId == Context.Guild.Id).SingleOrDefault()?.EphemeralConfirm ?? false;

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

    #region Whole guild queries
    /// <summary>
    /// Fetches all guild birthdays and places them into an easily usable structure.
    /// Users currently not in the cache are excluded from the result.
    /// </summary>
    protected List<KnownGuildUser> GetAllKnownUsers(ulong guildId) {
        var query = DbContext.UserEntries.AsNoTracking()
            .Where(r => r.GuildId == guildId)
            .OrderBy(r => r.BirthDate);
        var users = Cache.GetGuild(guildId);
        if (users is null) return [];

        var result = new List<KnownGuildUser>();
        foreach (var row in query) {
            if (!users.TryGetValue(row.UserId, out var cval)) continue; // Skip user not cached
            result.Add(new KnownGuildUser() { DbUser = row, CacheUser = cval});
        }
        return result;
    }

    /// <summary>
    /// Consolidated database + usercache information
    /// </summary>
    protected sealed record KnownGuildUser {
        public required UserEntry DbUser;
        public required UserInfo CacheUser;
        public LocalDate BirthDate => DbUser.BirthDate;
        public ulong UserId => CacheUser.UserId;
        public string DisplayName => CacheUser.FormatName();
        public DateTimeZone? TimeZone => DbUser.TimeZone;
    }
    #endregion

    /// <summary>
    /// Helper method for updating arbitrary <see cref="GuildConfig"/> values without all the boilerplate.
    /// </summary>
    /// <param name="valueUpdater">A delegate with access to the appropriate <see cref="GuildConfig"/> in this context.</param>
    protected async Task DbUpdateGuildAsync(Action<GuildConfig> valueUpdater) {
        var settings = Context.Guild.GetConfigOrNew(DbContext);

        valueUpdater(settings);

        if (settings.IsNew) DbContext.GuildConfigurations.Add(settings);
        await DbContext.SaveChangesAsync().ConfigureAwait(false);
    }

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
