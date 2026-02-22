using System.Collections.ObjectModel;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Discord;
using Discord.WebSocket;
using BirthdayBot.Data;
using NoiPublicBot;

namespace BirthdayBot.InteractionModules;

public class TzAutocompleteHandler : AutocompleteHandler {
    private static readonly TimeSpan _maxListAge = TimeSpan.FromHours(24);
    private static readonly ReaderWriterLockSlim _lock = new();
    private static ReadOnlyCollection<string> _baseZonesList;
    private static DateTimeOffset _lastListUpdate;

    static TzAutocompleteHandler() {
        _baseZonesList = RebuildSuggestionBaseList();
        _lastListUpdate = DateTimeOffset.UtcNow;
    }

    private static ReadOnlyCollection<string> RebuildSuggestionBaseList() {
        // In case we're running in an uninitialized environment (command registration helper), quit early
        if (Instance.SqlConnectionString is null) return [];

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
            .Distinct()
            .ToHashSet();

        // List of zones by current popularity
        using var db = BotDatabaseContext.New();

        var tzPopCount = db.UserEntries.AsNoTracking()
            .GroupBy(u => u.TimeZone)
            .Select(g => new { ZoneName = g.Key!.Id, Count = g.Count() })
            .ToList();

        // Left join: left = all NodaTime canonical zones, right = zones plus popularity data
        var withAllZones = canonicalZones.GroupJoin(tzPopCount,
            left => left,
            right => right.ZoneName,
            (tz, group) => new {
                ZoneName = tz,
                Count = group.FirstOrDefault()?.Count ?? 0
            })
            .ToList();

        // Remove all non-canonical zones, sort by popularity
        return withAllZones
                .Where(z => canonicalZones.Contains(z.ZoneName))
                .OrderByDescending(z => z.Count)
                .Select(z => z.ZoneName)
                .ToList()
                .AsReadOnly();
    }

    private static ReadOnlyCollection<string> GetBaseList() {
        _lock.EnterUpgradeableReadLock();
        try {
            // Should regenerate base list?
            var now = DateTimeOffset.UtcNow;
            if (now - _lastListUpdate > _maxListAge) {
                _lock.EnterWriteLock();
                try {
                    // Double-check in the write thread - in case another took the write lock just before us
                    if (now - _lastListUpdate > _maxListAge) {
                        _baseZonesList = RebuildSuggestionBaseList();
                        _lastListUpdate = now;
                    }
                } finally {
                    _lock.ExitWriteLock();
                }
            }
            return _baseZonesList;
        } finally {
            _lock.ExitUpgradeableReadLock();
        }
    }

    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext cx,
                                                                        IAutocompleteInteraction ia, IParameterInfo pm,
                                                                        IServiceProvider sv) {
        var resultList = GetBaseList();

        // Filter from existing input, give results
        var input = ((SocketAutocompleteInteraction)ia).Data.Current.Value.ToString()!;
        var inputsplit = input.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = resultList
            .Where(r => {
                var tzsplit = r.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (inputsplit.Length == 2) {
                    if (tzsplit.Length == 1) return false;
                    return tzsplit[0].Contains(inputsplit[0], StringComparison.OrdinalIgnoreCase)
                        && tzsplit[1].Contains(inputsplit[1], StringComparison.OrdinalIgnoreCase);
                } else {
                    // No '/' in query - search for string within each side of zone name
                    // Testing confirms this does not give conflicting results
                    if (tzsplit.Length == 1) return tzsplit[0].Contains(input, StringComparison.OrdinalIgnoreCase);
                    else return tzsplit[0].Contains(input, StringComparison.OrdinalIgnoreCase)
                        || tzsplit[1].Contains(input, StringComparison.OrdinalIgnoreCase);
                }
            })
            .Take(25)
            .Select(z => new AutocompleteResult(z, z))
            .ToList();
        return Task.FromResult(AutocompletionResult.FromSuccess(result));
    }
}
