using System.Linq.Expressions;
using BirthdayBot.Data;
using NodaTime;
using NoiPublicBot;

namespace BirthdayBot;

public class LocalCache(ShardInstance shard) : NoiPublicBot.Cache.UserCache<BotDatabaseContext>(shard) {
    const int DefaultCacheDaysBuffer = 5;

    private List<ulong> GetLocal(ulong guildId) => [.. GetEntriesForGuild(guildId, true).Select(e => e.UserId)];

    /// <summary>
    /// Builds a list of missing users with birthdays within <see cref="DefaultCacheDaysBuffer"/> days of the current date.
    /// This is the one called by default, and is meant to be used exclusively by <seealso cref="BackgroundServices.CacheRefresher"/>.
    /// </summary>
    protected override List<ulong> GetCacheMissingUsers(BotDatabaseContext context, ulong guildId) {
        var filter = FilterMissingWithinDays(DefaultCacheDaysBuffer);
        return filter(context, guildId);
    }

    /// <summary>
    /// Gets a filter that returns a list of missing users, taking into account all users registered with the bot.
    /// Great for full listings, data exports, database row expiration checks, and so on. Use sparingly.
    /// </summary>
    internal CacheMissingUsersFilter FilterGetAllMissing()
        => (context, guildId) => {
            var local = GetLocal(guildId);
            var remote = context.UserEntries
                .Where(e => e.GuildId == guildId)
                .Select(e => e.UserId)
                .ToList();
            return [.. remote.Except(local)];
    };

    /// <summary>
    /// Gets a filter that returns a list of missing users with birthdays within <paramref name="days"/> days of the current date.
    /// </summary>
    internal CacheMissingUsersFilter FilterMissingWithinDays(int days)
        => (context, guildId) => {
            var local = GetLocal(guildId);
            var remote = context.UserEntries
                .Where(e => e.GuildId == guildId)
                .Where(IsWithinDays(days))
                .Select(e => e.UserId)
                .ToList();
            return [.. remote.Except(local)];
        };

    private static readonly DateOnly _yearStart = new(2000, 1, 1);
    private static readonly DateOnly _yearEnd = new(2000, 12, 31);
    private static Expression<Func<UserEntry, bool>> IsWithinDays(int days) {
        // A query using this instantly becomes wildly inefficient when attempting to do time zone conversions per row.
        // It also doesn't adjust to non-leap years.
        // Usage of this predicate assumes the query's casting a wide net, narrowing down the results with further processing done later on.
        static (DateOnly min, DateOnly max) GetDbSearchRange(int days, DateTimeZone zone) {
            var now = SystemClock.Instance.GetCurrentInstant().InZone(zone).Date;

            var normal = new LocalDate(2000, now.Month, now.Day);
            var min = normal.PlusDays(-days);
            var max = normal.PlusDays(days);

            static DateOnly toDate(LocalDate d) => new(d.Year, d.Month, d.Day);
            return (toDate(min), toDate(max));
        }

        var zone = DateTimeZone.Utc;
        var (min, max) = GetDbSearchRange(days, zone);

        // Special case: searching across year boundaries
        if (min.Year < 2000 || max.Year > 2000) {
            // Use two search ranges, replacing min/max with year start/end:
            return e => (e.BirthDate >= _yearStart && e.BirthDate <= max)   // if birthday between 01-01 and max, or
                || (e.BirthDate >= min && e.BirthDate <= _yearEnd);         // if birthday between min and 12-31
        }

        // Simple range
        return e => e.BirthDate >= min && e.BirthDate <= max;
    }
}
