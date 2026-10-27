using System.Linq.Expressions;
using BirthdayBot.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NoiPublicBot.Common.UserCache;

namespace BirthdayBot;

static class CacheFilters
{
    /// <summary>
    /// Provides a filter that returns a list of missing users, taking into account all users registered with the bot.
    /// Meant for full listings, data exports, database row expiration checks, and so on.
    /// </summary>
    internal static UserCache<BotDatabaseContext>.AsyncCacheFetchFilter AllMissing()
        => async (cache, context, guildId) =>
        {
            var local = LocalCache(cache, guildId);
            var remote = await context.UserEntries
                .Where(e => e.GuildId == guildId)
                .Select(e => e.UserId)
                .ToListAsync().ConfigureAwait(false);
            return [.. remote.Except(local)];
        };

    /// <summary>
    /// Provides a filter that returns a list of missing users with birthdays within <paramref name="days"/> days of the current date.
    /// </summary>
    internal static UserCache<BotDatabaseContext>.AsyncCacheFetchFilter MissingWithinDays(int days)
        => async (cache, context, guildId) =>
        {
            var local = LocalCache(cache, guildId);
            var remote = await context.UserEntries
                .Where(e => e.GuildId == guildId)
                .Where(IsWithinDays(days))
                .Select(e => e.UserId)
                .ToListAsync().ConfigureAwait(false);
            return [.. remote.Except(local)];
        };

    /// <summary>
    /// Provides a filter that returns all users missing in cache which have a strong likelihood to be acted upon
    /// by the BirthdayUpdater background service.
    /// </summary>
    internal static UserCache<BotDatabaseContext>.AsyncCacheFetchFilter PrepareBackground()
        => async (cache, context, guildId) =>
        {
            var local = LocalCache(cache, guildId);
            var remote = await context.UserEntries
                .Where(e => e.GuildId == guildId)
                .Where(IsWithinDays(1)) // 3 days
                .Select(e => e.UserId)
                .ToListAsync().ConfigureAwait(false);
            return [.. remote.Except(local)];
        };

    #region Helpers
    private static IEnumerable<ulong> LocalCache(UserCache<BotDatabaseContext> cache, ulong guildId)
    {
        var g = cache.GetGuild(guildId, true);
        if (g is null) return [];
        return g.Select(e => e.Value.UserId);
    }

    private static readonly LocalDate YearStart = new(2000, 1, 1);
    private static readonly LocalDate YearEnd = new(2000, 12, 31);
    private static Expression<Func<UserEntry, bool>> IsWithinDays(int days)
    {
        var today = SystemClock.Instance.GetCurrentInstant().InZone(DateTimeZone.Utc).Date;

        var normal = new LocalDate(2000, today.Month, today.Day);
        var min = normal.PlusDays(-days);
        var max = normal.PlusDays(days);

        /*
           Special case occurs when min or max end up outside the year 2000.
           (That is, when there are birthdays very near the start/end of the year.)

            Two date ranges are considered instead within the year 2000:
            01-01 <=======x------------------------------------x====> 12-31
                         max                                  min
        */
        if (min.Year < 2000)
        {
            var min2000 = new LocalDate(2000, min.Month, min.Day);
            return e => (YearStart <= e.BirthDate && e.BirthDate <= max) ||
                        (min2000 <= e.BirthDate && e.BirthDate <= YearEnd);
        }
        else if (max.Year > 2000)
        {
            var max2000 = new LocalDate(2000, max.Month, max.Day);
            return e => (min <= e.BirthDate && e.BirthDate <= YearEnd) ||
                        (YearStart <= e.BirthDate && e.BirthDate <= max2000);
        }
        else
        {
            // No special case. Select dates between calculated minimum and maximum dates inclusive.
            return e => min <= e.BirthDate && e.BirthDate <= max;
        }
    }
    #endregion
}
