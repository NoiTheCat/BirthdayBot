using BirthdayBot.Data;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace BirthdayBot.ApplicationCommands;
public class TzAutocompleteHandler : AutocompleteHandler {
    private static readonly IReadOnlyCollection<string> AllTimeZones;

    static TzAutocompleteHandler() {
        // This bot discourages use of certain zone names and prefer the typical Region/City format over individual countries.
        // They have been excluded from this autocomplete list.
        var canonicalZones = DateTimeZoneProviders.Tzdb.Ids
            .Where(z => z.StartsWith("Africa/")
                || z.StartsWith("America/")
                || z.StartsWith("Antarctica/") // yep
                || z.StartsWith("Asia/")
                || z.StartsWith("Atlantic/")
                || z.StartsWith("Australia/")
                || z.StartsWith("Europe/")
                || z.StartsWith("Indian/")
                || z.StartsWith("Pacific/")
                || z.StartsWith("Etc/")
                || z == "UTC"
                || z == "GMT")
            .Distinct();
        AllTimeZones = new HashSet<string>(canonicalZones!);
    }
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext cx,
                                                                        IAutocompleteInteraction ia, IParameterInfo pm,
                                                                        IServiceProvider sv) {
        // Get our table sorted out

        // List of zones by current popularity
        var db = new BotDatabaseContext();
        var tzPopCount = db.UserEntries.AsNoTracking()
            .GroupBy(u => u.TimeZone)
            .Select(g => new { ZoneName = g.Key, Count = g.Count() })
            .ToList();

        // Left join: left = all NodaTime canonical zones, right = zones plus popularity data
        var withAllZones = AllTimeZones.GroupJoin(tzPopCount, 
            left => left,
            right => right.ZoneName,
            (tz, group) => new {
                ZoneName = tz,
                Count = group.FirstOrDefault()?.Count ?? 0
            })
            .ToList();

        // Remove all non-canonical zones, do sorting
        var resultList = withAllZones
            .Where(z => AllTimeZones.Contains(z.ZoneName))
            .OrderByDescending(z => z.Count)
            .ToList();
        // TODO cache and refresh this periodically instead of recomputing every time

        // Filter from existing input, give results
        var input = ((SocketAutocompleteInteraction)ia).Data.Current.Value.ToString()!;
        var inputsplit = input.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = resultList
            .Where(r => {
                var tzsplit = r.ZoneName.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (inputsplit.Length == 2) {
                    if (tzsplit.Length == 1) return false;
                    return tzsplit[0].Contains(inputsplit[0], StringComparison.OrdinalIgnoreCase)
                        && tzsplit[1].Contains(inputsplit[1], StringComparison.OrdinalIgnoreCase);
                } else {
                    // No '/' in query - search for string within each side of zone name (tested to not give conflicting results)
                    if (tzsplit.Length == 1) return tzsplit[0].Contains(input, StringComparison.OrdinalIgnoreCase);
                    else return tzsplit[0].Contains(input, StringComparison.OrdinalIgnoreCase)
                        || tzsplit[1].Contains(input, StringComparison.OrdinalIgnoreCase);
                }
            })
            .Take(25)
            .Select(x => new AutocompleteResult(x.ZoneName, x.ZoneName))
            .ToList();
        return Task.FromResult(AutocompletionResult.FromSuccess(result));
    }
}