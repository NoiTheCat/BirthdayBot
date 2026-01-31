using System.Linq.Expressions;
using NodaTime;

namespace BirthdayBot.Data;

public static class Predicates {
    public static Expression<Func<UserEntry, bool>> IsWithinDays(int days, DateTimeZone? defaultZone) {
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

        var zone = defaultZone ?? DateTimeZone.Utc;
        var (min, max) = GetDbSearchRange(days, zone);

        // Special case: searching across year boundaries
        if (min.Year < 2000 || max.Year > 2000) {
            var yearStart = new DateOnly(2000, 1, 1);
            var yearEnd = new DateOnly(2000, 12, 31);

            // Use two search ranges, replacing min/max with year start/end
            return e => (e.BirthDate >= min && e.BirthDate <= yearEnd) // if birthday between min and 12-31, or
                || (e.BirthDate >= yearStart && e.BirthDate <= max);   // if birthday between 01-01 and max
        }

        // Simple range
        return e => e.BirthDate >= min && e.BirthDate <= max;
    }
}
