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
        var input = userInput.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (input.Length < 2) {
            // Suggest region if not given
        } else {
            // Suggest within region

        }

        IEnumerable<AutocompleteResult> results = new[]
        {
            new AutocompleteResult("foo", "foo_value"),
            new AutocompleteResult("bar", "bar_value"),
            new AutocompleteResult("baz", "baz_value"),
        }.Where(x => x.Name.StartsWith(userInput, StringComparison.InvariantCultureIgnoreCase)); // only send suggestions that starts with user's input; use case insensitive matching


        // max - 25 suggestions at a time
        return Task.FromResult(AutocompletionResult.FromSuccess(results.Take(25)));
    }

    private void RefreshTopZones() {

    }

    private static readonly ReadOnlyCollection<string> SuggestedRegions;
    private static readonly ReadOnlyDictionary<string, ReadOnlyCollection<string>> SuggestedZones;

    static TzAutocompleteHandler() {
        // TODO After ensuring this list is fine, have this created automatically at bot startup. Query used:
        // select time_zone as tz, count(*)
        // from user_birthdays
        // where starts_with(time_zone, 'America/') -- use with: Africa America Antarctica Asia Atlantic Australia Europe Pacific
        // group by tz
        // order by count desc
        // limit 100;
        // Should also find a way to include zones with counts of 0 for full completion

        var Africa = new List<string>() {
            "Cairo", "Johannesburg", "Casablanca", "Algiers", "Tunis", "Lagos", "Nairobi", "Dakar", "Abidjan",
            "Khartoum", "Accra", "Maputo", "Tripoli", "Harare", "Windhoek", "Gaborone", "Lusaka", "Luanda", "Kigali",
            "Ceuta", "Lubumbashi", "Addis_Ababa", "Malabo", "Blantyre", "Maseru", "Monrovia", "Banjul", "Niamey",
            "Porto-Novo", "Bangui", "Asmera", "Dar_es_Salaam", "Juba"
        }.AsReadOnly();
        var America = new List<string>() {
            "New_York", "Chicago", "Los_Angeles", "Toronto", "Denver", "Mexico_City", "Sao_Paulo", "Vancouver", "Phoenix",
            "Santiago", "Buenos_Aires", "Detroit", "Edmonton", "Bogota", "Lima", "Argentina/Buenos_Aires", "Winnipeg",
            "Montreal", "Halifax", "Regina", "Caracas", "Indianapolis", "Montevideo", "Guayaquil", "Anchorage",
            "Costa_Rica", "Panama", "Puerto_Rico", "Boise", "Monterrey", "Guatemala", "Santo_Domingo", "St_Johns",
            "Port_of_Spain", "Chihuahua", "Tijuana", "El_Salvador", "Moncton", "La_Paz", "Indiana/Indianapolis",
            "Fortaleza", "Tegucigalpa", "Hermosillo", "Bahia", "Louisville", "Jamaica", "Asuncion", "Belem",
            "Santa_Isabel", "Argentina/Cordoba", "Cancun", "Kentucky/Louisville", "Mazatlan", "Indiana/Knox", "Manaus",
            "Recife", "Merida", "Barbados", "Managua", "Bahia_Banderas", "Guyana", "Matamoros", "Cuiaba", "Belize",
            "Paramaribo", "Cordoba", "Havana", "North_Dakota/Center", "Nome", "Adak", "Shiprock", "Fort_Wayne",
            "Godthab", "Swift_Current", "Anguilla", "Argentina/Salta", "Whitehorse", "Campo_Grande", "Yellowknife",
            "Araguaina", "Guadeloupe", "Menominee", "Nassau", "Nuuk", "Ojinaga", "Virgin", "Goose_Bay",
            "Argentina/Tucuman", "Rainy_River", "North_Dakota/New_Salem", "Scoresbysund", "Martinique", "Atikokan",
            "Creston", "Glace_Bay", "Tortola", "Rankin_Inlet", "Knox_IN", "Iqaluit", "North_Dakota/Beulah"
        }.AsReadOnly();
        var Antarctica = new List<string>() {
            "McMurdo", "Troll", "DumontDUrville", "Macquarie", "South_Pole"
        }.AsReadOnly();
        var Asia = new List<string>() {
            "Manila", "Kolkata", "Calcutta", "Jakarta", "Kuala_Lumpur", "Singapore", "Bangkok", "Riyadh", "Dubai",
            "Saigon", "Tokyo", "Hong_Kong", "Seoul", "Dhaka", "Ho_Chi_Minh", "Karachi", "Shanghai", "Taipei", "Makassar",
            "Jerusalem", "Tehran", "Yekaterinburg", "Qatar", "Baghdad", "Colombo", "Beirut", "Bahrain", "Kuching",
            "Rangoon", "Amman", "Kuwait", "Muscat", "Almaty", "Brunei", "Tbilisi", "Phnom_Penh", "Kathmandu",
            "Vladivostok", "Krasnoyarsk", "Nicosia", "Katmandu", "Yangon", "Baku", "Ulaanbaatar", "Pontianak", "Yakutsk",
            "Novosibirsk", "Yerevan", "Irkutsk", "Macau", "Tashkent", "Damascus", "Omsk", "Bishkek", "Macao", "Dili",
            "Jayapura", "Pyongyang", "Chongqing", "Tomsk", "Kamchatka", "Vientiane", "Kabul", "Novokuznetsk", "Istanbul",
            "Sakhalin", "Choibalsan", "Hovd", "Khandyga", "Dacca", "Ashgabat", "Tel_Aviv", "Chita", "Oral", "Magadan",
            "Hebron", "Gaza", "Srednekolymsk", "Anadyr", "Urumqi", "Barnaul", "Aqtobe"
        }.AsReadOnly();
        var Atlantic = new List<string>() {
            "Canary", "Reykjavik", "Azores", "South_Georgia", "Faroe", "Stanley"
        }.AsReadOnly();
        var Australia = new List<string>() {
            "Sydney", "Melbourne", "Brisbane", "Perth", "Adelaide", "Victoria", "Queensland", "Hobart", "NSW", "Darwin",
            "Canberra", "Tasmania", "West", "South", "Currie", "ACT", "North", "Lindeman", "Broken_Hill"
        }.AsReadOnly();
        var Europe = new List<string>() {
            "London", "Berlin", "Paris", "Amsterdam", "Madrid", "Stockholm", "Warsaw", "Rome", "Moscow", "Brussels",
            "Prague", "Helsinki", "Oslo", "Bucharest", "Lisbon", "Copenhagen", "Dublin", "Istanbul", "Athens", "Vienna",
            "Budapest", "Zurich", "Vilnius", "Kiev", "Sofia", "Belgrade", "Tallinn", "Zagreb", "Bratislava", "Riga",
            "Ljubljana", "Sarajevo", "Kyiv", "Luxembourg", "Minsk", "Skopje", "Chisinau", "Tirane", "Samara", "Belfast",
            "Malta", "Kaliningrad", "Podgorica", "Monaco", "Gibraltar", "Volgograd", "Busingen", "Jersey", "Zaporozhye",
            "Saratov", "Andorra", "Astrakhan", "Isle_of_Man", "Guernsey", "Vaduz", "Vatican", "Nicosia", "Tiraspol"
        }.AsReadOnly();
        var Pacific = new List<string>() {
            "Auckland", "Honolulu", "Guam", "Fiji", "Port_Moresby", "Rarotonga", "Noumea", "Kiritimati", "Apia",
            "Tahiti", "Majuro", "Midway", "Pago_Pago", "Palau", "Saipan"
        }.AsReadOnly();

        SuggestedZones = new Dictionary<string, ReadOnlyCollection<string>> {
            { "Africa", Africa },
            { "America", America },
            { "Antarctica", Antarctica },
            { "Asia", Asia },
            { "Australia", Australia },
            { "Europe", Europe },
            { "Pacific", Pacific }
        }.AsReadOnly();
        SuggestedRegions = SuggestedZones.Select(i => i.Key).ToList().AsReadOnly();
    }
}