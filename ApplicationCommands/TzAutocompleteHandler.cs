
using BirthdayBot.Data;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;

namespace BirthdayBot.ApplicationCommands;

public class TzAutocompleteHandler : AutocompleteHandler {
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext cx,
                                                                        IAutocompleteInteraction ia, IParameterInfo pm,
                                                                        IServiceProvider sv) {
        var input = ((SocketAutocompleteInteraction)ia).Data.Current.Value.ToString()!;
        var inparts = input.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var db = new BotDatabaseContext();
        var query = db.UserEntries.AsNoTracking();
        if (inparts.Length == 2) {
            query = query.Where(u => EF.Functions.ILike(u.TimeZone!, $"%{inparts[0]}%/%{inparts[1]}%"));
        } else {
            // No '/' in query - search for string within each side of zone name (tested to not give conflicting results)
            query = query.Where(u =>
                EF.Functions.ILike(u.TimeZone!, $"%{input}%/%") || EF.Functions.ILike(u.TimeZone!, $"%/%{input}%"));
        }
        // TODO Should also find a way to include zones with counts of 0 for full completion (with a join, maybe)
        var result = query.GroupBy(u => u.TimeZone)
            .Select(g => new { ZoneName = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(25)
            .Select(x => new AutocompleteResult(x.ZoneName, x.ZoneName))
            .ToList();
        return Task.FromResult(AutocompletionResult.FromSuccess(result));
    }
}