
using System.Net;
using BirthdayBot.Data;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace BirthdayBot.ApplicationCommands;
public class TzAutocompleteHandler : AutocompleteHandler {
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext cx,
                                                                        IAutocompleteInteraction ia, IParameterInfo pm,
                                                                        IServiceProvider sv) {
        var input = ((SocketAutocompleteInteraction)ia).Data.Current.Value.ToString()!;
        var inparts = input.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var db = new BotDatabaseContext();
        Func<UserEntry, bool> whereInputProcessing;
        if (inparts.Length == 2) {
            whereInputProcessing = (UserEntry u) => { return EF.Functions.ILike(u.TimeZone!, $"%{inparts[0]}%/%{inparts[1]}%"); };
        } else {
            // No '/' in query - search for string within each side of zone name (tested to not give conflicting results)
            whereInputProcessing =
                (UserEntry u) => { return EF.Functions.ILike(u.TimeZone!, $"%{input}%/%") || EF.Functions.ILike(u.TimeZone!, $"%/%{input}%"); };
        }
        var tzPopCount = db.UserEntries.AsNoTracking()
            .Where(whereInputProcessing)
            .GroupBy(u => u.TimeZone)
            .Select(g => new { ZoneName = g.Key, Count = g.Count() });

        var withAllZones = DateTimeZoneProviders.Tzdb.Ids
            .GroupJoin(tzPopCount, // left join. left = all NodaTime zones, right = user tz popularity count
                left => left,
                right => right.ZoneName,
                (tz, group) => new {
                    ZoneName = tz,
                    Count = group.FirstOrDefault()?.Count ?? 0
                });

        // TODO Filter out undesirable zone names, aliases, etc from autocompletion
        var result = withAllZones
            .OrderByDescending(x => x.Count)
            .Take(25)
            .Select(x => new AutocompleteResult(x.ZoneName, x.ZoneName))
            .ToList();
        return Task.FromResult(AutocompletionResult.FromSuccess(result));
    }
}