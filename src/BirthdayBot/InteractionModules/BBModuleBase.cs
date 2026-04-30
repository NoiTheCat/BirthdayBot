using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BirthdayBot.Data;
using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
using NoiPublicBot;
using NoiPublicBot.Common.UserCache;
using static BirthdayBot.Localization.StringProviders;

namespace BirthdayBot.InteractionModules;

public partial class BBModuleBase : InteractionModuleBase<SocketInteractionContext> {
    // Injected by DI:
    public ShardInstance Shard { get; set; } = null!;
    public BotDatabaseContext DbContext { get; set; } = null!;
    public UserCache<BotDatabaseContext> Cache { get; set; } = null!;

    // Other helpers:
    protected string GuildLocale => Context.Interaction.GuildLocale;
    protected string UserLocale => Context.Interaction.UserLocale;

    // Opportunistically caches user data coming in via interactions.
    public override Task BeforeExecuteAsync(ICommandInfo command) {
        if (Context.User is IGuildUser incoming) Cache.Update(incoming);
        return base.BeforeExecuteAsync(command);
    }
    
    protected static bool TryParseZone(string tzinput, [NotNullWhen(true)] out DateTimeZone? parsedZone) {
        var tzdb = DateTimeZoneProviders.Tzdb;
        parsedZone = tzdb.GetZoneOrNull(tzinput);
        if (parsedZone != null) return true;

        var search = tzdb.Ids.FirstOrDefault(t => string.Equals(t, tzinput, StringComparison.OrdinalIgnoreCase));
        if (search != null) {
            parsedZone = tzdb.GetZoneOrNull(search)!;
            return true;
        }

        parsedZone = null;
        return false;
    }

    /// <summary>
    /// Checks if the server allows ephemeral command confirmations.
    /// </summary>
    protected bool IsEphemeralSet()
        => DbContext.GuildConfigurations.Where(r => r.GuildId == Context.Guild.Id).SingleOrDefault()?.EphemeralConfirm ?? false;

    #region Date handling
    public enum MonthName {
        January, February, March, April, May, June, July, August, September, October, November, December
    }

    /// <exception cref="FormatException">Thrown for any parsing issue.</exception>
    protected static bool TryParseDate(string dayInput, MonthName monthInput, [NotNullWhen(true)] out LocalDate? result) {
        result = null;
        if (!int.TryParse(dayInput, out var day)) return false;
        var month = (int)monthInput + 1;

        try {
            result = new(2000, month, day);
            return true;
        } catch (ArgumentOutOfRangeException) {
            return false;
        }
    }

    protected static string DateFormat(LocalDate date, string locale, bool abbreviated = false) {
        // Avoid inconsistency - if we otherwise have zero support for the given locale, fall back to default.
        if (!Localization.SupportedLocales.List.Contains(locale)) locale = "en_US";

        var culture = new CultureInfo(locale);
        var outPattern = culture.DateTimeFormat.MonthDayPattern;
        if (abbreviated) outPattern = outPattern.Replace("MMMM", "MMM").Replace("d", "dd");

        return LocalDatePattern.Create(outPattern, culture).Format(date);
    }
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
        public required UserCacheItem CacheUser;
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
    protected async Task<bool> RefreshCacheAsync(UserCache<BotDatabaseContext>.CacheFetchFilter filter) {
        var wasDeferred = false;
        // casting a wide net here...
        var refresh = Cache.RequestGuildRefreshAsync(DbContext, Context.Guild.Id, filter);
        if (!refresh.IsCompleted) {
            // This may take a while
            wasDeferred = true;
            await RespondAsync(LRg("loadingUsers")).ConfigureAwait(false);
            await refresh.ConfigureAwait(false);
        }
        // Run a second time in case we got an ongoing task with a narrower filter than requested
        refresh = Cache.RequestGuildRefreshAsync(DbContext, Context.Guild.Id, filter);
        if (!refresh.IsCompleted) {
            if (!wasDeferred) {
                wasDeferred = true;
                await RespondAsync(LRg("loadingUsers")).ConfigureAwait(false);
                await refresh.ConfigureAwait(false);
            }
        }
        return wasDeferred;
    }

    /// <summary>Get string from Commands using guild locale.</summary>
    protected string LCg(string key, params object?[] format) => Commands.Get(GuildLocale, key, format);
    /// <summary>Get string from Commands using user locale.</summary>
    protected string LCu(string key, params object?[] format) => Commands.Get(UserLocale, key, format);
    /// <summary>Get string from Responses using guild locale.</summary>
    protected string LRg(string key, params object?[] format) => Responses.Get(GuildLocale, key, format);
    /// <summary>Get string from Responses using user locale.</summary>
    protected string LRu(string key, params object?[] format) => Responses.Get(UserLocale, key, format);
}
