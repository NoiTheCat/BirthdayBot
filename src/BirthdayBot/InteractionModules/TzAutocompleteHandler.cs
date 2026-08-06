using BirthdayBot.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NoiPublicBot.Common;

namespace BirthdayBot.InteractionModules;

public class TzAutocompleteHandler : TimezoneAutocompleteBase
{
    protected override async Task<IEnumerable<(DateTimeZone zone, int count)>> GetPopularityCountsAsync()
    {
        using var db = BotDatabaseContext.New();
        var query = await db.UserEntries.AsNoTracking()
            .GroupBy(u => u.TimeZone)
            .Select(g => new { Zone = g.Key!, Count = g.Count() })
            .ToListAsync().ConfigureAwait(false);
        // Cannot use ValueTuple in EF Core select (for now?)
        return query.Select(s => ValueTuple.Create(s.Zone, s.Count));
    }
}
