using System.Linq.Expressions;
using BirthdayBot.Data;
using NodaTime;
using NoiPublicBot;
using NoiPublicBot.Cache;

namespace BirthdayBot;

public class LocalCache(ShardInstance shard) : UserCache<BotDatabaseContext>(shard) {
    const int DefaultCacheDaysBuffer = 5;

    private List<ulong> GetLocal(ulong guildId) {
        var g = GetGuild(guildId, true);
        if (g is null) return [];
        return [.. g.Select(e => e.Value.UserId)];
    }

    /// <summary>
    /// Provides a filter that returns a list of missing users, taking into account all users registered with the bot.
    /// Great for full listings, data exports, database row expiration checks, and so on. Use sparingly.
    /// </summary>
    internal CacheFetchFilter FilterGetAllMissing()
        => (context, guildId) => {
            var local = GetLocal(guildId);
            var remote = context.UserEntries
                .Where(e => e.GuildId == guildId)
                .Select(e => e.UserId)
                .ToList();
            return [.. remote.Except(local)];
        };

    /// <summary>
    /// Provides a filter that returns a list of missing users with birthdays within <paramref name="days"/> days of the current date.
    /// </summary>
    internal CacheFetchFilter FilterMissingWithinDays(int days)
        => (context, guildId) => {
            var local = GetLocal(guildId);
            var remote = context.UserEntries
                .Where(e => e.GuildId == guildId)
                .Where(IsWithinDays(days))
                .Select(e => e.UserId)
                .ToList();
            return [.. remote.Except(local)];
        };

    internal CacheFetchFilter FilterBackground()
        => (context, guildId) => {
            var local = GetLocal(guildId);
            var remote = context.UserEntries
                .Where(e => e.GuildId == guildId)
                .Where(IsWithinDays(1)) // 3 days
                .Select(e => e.UserId)
                .ToList();
            return [.. remote.Except(local)];
        };

    private static readonly LocalDate _yearStart = new(2000, 1, 1);
    private static readonly LocalDate _yearEnd = new(2000, 12, 31);
    private static Expression<Func<UserEntry, bool>> IsWithinDays(int days) {
        // A query using this instantly becomes wildly inefficient when attempting to do time zone conversions per row.
        // It also doesn't adjust to non-leap years.
        // Usage of this predicate assumes the query's casting a wide net, narrowing down the results with further processing done later on.
        static (LocalDate min, LocalDate max) GetDbSearchRange(int days, DateTimeZone zone) {
            var now = SystemClock.Instance.GetCurrentInstant().InZone(zone).Date;

            var normal = new LocalDate(2000, now.Month, now.Day);
            var min = normal.PlusDays(-days);
            var max = normal.PlusDays(days);

            static LocalDate toDate(LocalDate d) => new(d.Year, d.Month, d.Day);
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
