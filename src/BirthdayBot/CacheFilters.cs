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
        => async (cache, context, guildId)
            => await context.UserEntries.AsNoTracking()
                .Where(e => e.GuildId == guildId)
                .Select(e => e.UserId)
                .ToAsyncEnumerable()
                .Except(LocalCache(cache, guildId))
                .ToListAsync().ConfigureAwait(false);

    /// <summary>
    /// Provides a filter that returns a list of missing users with birthdays within <paramref name="days"/> days of the current date.
    /// </summary>
    internal static UserCache<BotDatabaseContext>.AsyncCacheFetchFilter MissingWithinDays(int days)
        => async (cache, context, guildId)
            => await context.UserEntries.AsNoTracking()
                .Where(e => e.GuildId == guildId)
                .Where(IsWithinDays(days))
                .Select(e => e.UserId)
                .ToAsyncEnumerable()
                .Except(LocalCache(cache, guildId))
                .ToListAsync().ConfigureAwait(false);

    /// <summary>
    /// Provides a filter that returns all users missing in cache which have a strong likelihood to be acted upon
    /// by the BirthdayUpdater background service.
    /// </summary>
    internal static UserCache<BotDatabaseContext>.AsyncCacheFetchFilter PrepareBackground()
        => async (cache, context, guildId)
            => await context.UserEntries.AsNoTracking()
                .Where(e => e.GuildId == guildId)
                .Where(IsWithinDays(1)) // 3 days
                .Select(e => e.UserId)
                .ToAsyncEnumerable()
                .Except(LocalCache(cache, guildId))
                .ToListAsync().ConfigureAwait(false);

    #region Helpers
    private static IAsyncEnumerable<ulong> LocalCache(UserCache<BotDatabaseContext> cache, ulong guildId)
    {
        var g = cache.GetGuild(guildId, true);
        if (g is null) return Array.Empty<ulong>().ToAsyncEnumerable();
        return g.Select(e => e.Value.UserId).ToAsyncEnumerable();
    }

    private static readonly LocalDate YearStart = new(2000, 1, 1);
    private static readonly LocalDate YearEnd = new(2000, 12, 31);
    private static Expression<Func<UserEntry, bool>> IsWithinDays(int days)
    {
        static (LocalDate min, LocalDate max) GetDbSearchRange(int days, DateTimeZone zone)
        {
            var now = SystemClock.Instance.GetCurrentInstant().InZone(zone).Date;

            var normal = new LocalDate(2000, now.Month, now.Day);
            var min = normal.PlusDays(-days);
            var max = normal.PlusDays(days);

            static LocalDate toDate(LocalDate d) => new(d.Year, d.Month, d.Day);
            return (toDate(min), toDate(max));
        }

        var zone = DateTimeZone.Utc;
        var (min, max) = GetDbSearchRange(days, zone);

        if (min.Year < 2000 || max.Year > 2000)
        {
            // Special case: searching across year boundaries
            // Use two search ranges, replacing min/max with year start/end:
            return e => (e.BirthDate >= YearStart && e.BirthDate <= max)   // if birthday between 01-01 and max, or
                || (e.BirthDate >= min && e.BirthDate <= YearEnd);         // if birthday between min and 12-31
        }
        else
        {
            // Simple range
            return e => e.BirthDate >= min && e.BirthDate <= max;
        }
    }
    #endregion
}
