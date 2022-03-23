#pragma warning disable CS0618
using BirthdayBot.Data;
using System.Text.RegularExpressions;

namespace BirthdayBot.TextCommands;

internal class UserCommands : CommandsCommon {
    public UserCommands(Configuration db) : base(db) { }

    public override IEnumerable<(string, CommandHandler)> Commands
        => new List<(string, CommandHandler)>()
        {
                ("set", CmdSet),
                ("zone", CmdZone),
                ("remove", CmdRemove)
        };

    #region Date parsing
    const string FormatError = ":x: Unrecognized date format. The following formats are accepted, as examples: "
            + "`15-jan`, `jan-15`, `15 jan`, `jan 15`, `15 January`, `January 15`.";

    private static readonly Regex DateParse1 = new(@"^(?<day>\d{1,2})[ -](?<month>[A-Za-z]+)$", RegexOptions.Compiled);
    private static readonly Regex DateParse2 = new(@"^(?<month>[A-Za-z]+)[ -](?<day>\d{1,2})$", RegexOptions.Compiled);

    /// <summary>
    /// Parses a date input.
    /// </summary>
    /// <returns>Tuple: month, day</returns>
    /// <exception cref="FormatException">
    /// Thrown for any parsing issue. Reason is expected to be sent to Discord as-is.
    /// </exception>
    private static (int, int) ParseDate(string dateInput) {
        var m = DateParse1.Match(dateInput);
        if (!m.Success) {
            // Flip the fields around, try again
            m = DateParse2.Match(dateInput);
            if (!m.Success) throw new FormatException(FormatError);
        }

        int day, month;
        string monthVal;
        try {
            day = int.Parse(m.Groups["day"].Value);
        } catch (FormatException) {
            throw new Exception(FormatError);
        }
        monthVal = m.Groups["month"].Value;

        int dayUpper; // upper day of month check
        (month, dayUpper) = GetMonth(monthVal);

        if (day == 0 || day > dayUpper) throw new FormatException(":x: The date you specified is not a valid calendar date.");

        return (month, day);
    }

    /// <summary>
    /// Returns information for a given month input.
    /// </summary>
    /// <param name="input"></param>
    /// <returns>Tuple: Month value, upper limit of days in the month</returns>
    /// <exception cref="FormatException">
    /// Thrown on error. Send out to Discord as-is.
    /// </exception>
    private static (int, int) GetMonth(string input) {
        return input.ToLower() switch {
            "jan" or "january" => (1, 31),
            "feb" or "february" => (2, 29),
            "mar" or "march" => (3, 31),
            "apr" or "april" => (4, 30),
            "may" => (5, 31),
            "jun" or "june" => (6, 30),
            "jul" or "july" => (7, 31),
            "aug" or "august" => (8, 31),
            "sep" or "september" => (9, 30),
            "oct" or "october" => (10, 31),
            "nov" or "november" => (11, 30),
            "dec" or "december" => (12, 31),
            _ => throw new FormatException($":x: Can't determine month name `{input}`. Check your spelling and try again."),
        };
    }
    #endregion

    #region Documentation
    public static readonly CommandDocumentation DocSet =
        new(new string[] { "set (date)" }, "Registers your birth month and day.",
            $"`{CommandPrefix}set jan-31`, `{CommandPrefix}set 15 may`.");
    public static readonly CommandDocumentation DocZone =
        new(new string[] { "zone (zone)" }, "Sets your local time zone. "
            + $"See also `{CommandPrefix}help-tzdata`.", null);
    public static readonly CommandDocumentation DocRemove =
        new(new string[] { "remove" }, "Removes your birthday information from this bot.", null);
    #endregion

    private async Task CmdSet(ShardInstance instance, GuildConfiguration gconf,
                              string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser) {
        if (param.Length < 2) {
            await reqChannel.SendMessageAsync(ParameterError, embed: DocSet.UsageEmbed).ConfigureAwait(false);
            return;
        }

        // Date format accepts spaces. Must coalesce parameters to a single string.
        var fullinput = "";
        foreach (var p in param[1..]) fullinput += " " + p;
        fullinput = fullinput[1..]; // trim leading space

        int bmonth, bday;
        try {
            (bmonth, bday) = ParseDate(fullinput);
        } catch (FormatException ex) {
            // Our parse method's FormatException has its message to send out to Discord.
            reqChannel.SendMessageAsync(ex.Message, embed: DocSet.UsageEmbed).Wait();
            return;
        }

        // Parsing successful. Update user information.
        bool known; // Extra detail: Bot's response changes if the user was previously unknown.
        try {
            var user = await GuildUserConfiguration.LoadAsync(gconf.GuildId, reqUser.Id).ConfigureAwait(false);
            known = user.IsKnown;
            await user.UpdateAsync(bmonth, bday, user.TimeZone).ConfigureAwait(false);
        } catch (Exception ex) {
            Program.Log("Error", ex.ToString());
            reqChannel.SendMessageAsync(ShardInstance.InternalError).Wait();
            return;
        }
        await reqChannel.SendMessageAsync($":white_check_mark: Your birthday has been { (known ? "updated" : "recorded") }.")
                .ConfigureAwait(false);
    }

    private async Task CmdZone(ShardInstance instance, GuildConfiguration gconf,
                               string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser) {
        if (param.Length != 2) {
            await reqChannel.SendMessageAsync(ParameterError, embed: DocZone.UsageEmbed).ConfigureAwait(false);
            return;
        }

        var user = await GuildUserConfiguration.LoadAsync(gconf.GuildId, reqUser.Id).ConfigureAwait(false);
        if (!user.IsKnown) {
            await reqChannel.SendMessageAsync(":x: You may only update your time zone when you have a birthday registered."
                + $" Refer to the `{CommandPrefix}set` command.", embed: DocZone.UsageEmbed)
                .ConfigureAwait(false);
            return;
        }

        string btz;
        try {
            btz = ParseTimeZone(param[1]);
        } catch (Exception ex) {
            reqChannel.SendMessageAsync(ex.Message, embed: DocZone.UsageEmbed).Wait();
            return;
        }
        await user.UpdateAsync(user.BirthMonth, user.BirthDay, btz).ConfigureAwait(false);

        await reqChannel.SendMessageAsync($":white_check_mark: Your time zone has been updated to **{btz}**.")
            .ConfigureAwait(false);
    }

    private async Task CmdRemove(ShardInstance instance, GuildConfiguration gconf,
                                 string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser) {
        // Parameter count check
        if (param.Length != 1) {
            await reqChannel.SendMessageAsync(NoParameterError, embed: DocRemove.UsageEmbed).ConfigureAwait(false);
            return;
        }

        // Extra detail: Send a notification if the user isn't actually known by the bot.
        bool known;
        var u = await GuildUserConfiguration.LoadAsync(gconf.GuildId, reqUser.Id).ConfigureAwait(false);
        known = u.IsKnown;
        await u.DeleteAsync().ConfigureAwait(false);
        if (!known) {
            await reqChannel.SendMessageAsync(":white_check_mark: This bot already does not contain your information.")
                .ConfigureAwait(false);
        } else {
            await reqChannel.SendMessageAsync(":white_check_mark: Your information has been removed.")
                .ConfigureAwait(false);
        }
    }
}
