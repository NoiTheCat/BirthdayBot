using System.Collections.ObjectModel;
using CommandLine;
using Discord.Interactions;
using System.Linq;

namespace BirthdayBot.ApplicationCommands;

public class TzAutocompleteHandler : AutocompleteHandler {
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext cx,
                                                                        IAutocompleteInteraction ia, IParameterInfo pm,
                                                                        IServiceProvider sv) {
        var userInput = ((SocketAutocompleteInteraction)ia).Data.Current.Value.ToString()!;

        var results = Top25.Select(i => new AutocompleteResult(i, i))
        .Where(x => x.Name.StartsWith(userInput, StringComparison.InvariantCultureIgnoreCase)); // only send suggestions that starts with user's input; use case insensitive matching


        // max - 25 suggestions at a time
        return Task.FromResult(AutocompletionResult.FromSuccess(results.Take(25)));
    }

    private static readonly ReadOnlyCollection<string> Top25 = new List<string>() {
        "America/New_York", "America/Chicago", "America/Los_Angeles", "Europe/London", "Asia/Manila", "Europe/Berlin",
        "Europe/Paris", "Europe/Amsterdam", "Asia/Kolkata", "Australia/Sydney", "Asia/Calcutta", "Asia/Jakarta",
        "America/Toronto", "Asia/Kuala_Lumpur", "America/Denver", "Europe/Madrid", "Australia/Melbourne",
        "Asia/Singapore", "America/Mexico_City", "Australia/Brisbane", "America/Sao_Paulo", "Pacific/Auckland",
        "Europe/Stockholm", "America/Vancouver", "Europe/Warsaw"
    }.AsReadOnly();
    // select time_zone as tz, count(*)
    // from user_birthdays
    // where time_zone like '%/%'
    // group by tz
    // order by count desc
    // limit ???;
    // TODO Should also find a way to include zones with counts of 0 for full completion
}