using BirthdayBot.Data;

namespace BirthdayBot.ApplicationCommands;

internal class RegistrationCommands : BotApplicationCommand {
    #region Help strings
    public const string HelpSet = "Sets or updates your birthday.";
    public const string HelpZone = "Sets or updates your time zone. For use only if you have already set a birthday.";
    public const string HelpZoneDel = "Removes your time zone information from the bot.";
    public const string HelpDel = "Removes your birthday information from the bot.";

    const string MsgNoData = "This bot does not have your birthday information for this server.";
    #endregion

    public override IEnumerable<ApplicationCommandProperties> GetCommands() => new ApplicationCommandProperties[] {
            new SlashCommandBuilder()
                .WithName("set-birthday")
                .WithDescription(HelpSet)
                .AddOption("date", ApplicationCommandOptionType.String, HelpOptDate, isRequired: true)
                .Build(),
            new SlashCommandBuilder()
                .WithName("set-timezone")
                .WithDescription(HelpZone)
                .AddOption("zone", ApplicationCommandOptionType.String, HelpOptZone, isRequired: true)
                .Build(),
            new SlashCommandBuilder()
                .WithName("remove-timezone")
                .WithDescription(HelpZoneDel)
                .AddOption("zone", ApplicationCommandOptionType.String, HelpOptZone, isRequired: true)
                .Build(),
            new SlashCommandBuilder()
                .WithName("remove-birthday")
                .WithDescription(HelpDel)
                .Build()
        };
    public override CommandResponder? GetHandlerFor(string commandName) => commandName switch {
        "set-birthday" => CmdSetBirthday,
        "set-timezone" => CmdSetTimezone,
        "remove-timezone" => CmdDelTz,
        "remove-birthday" => CmdDelBd,
        _ => null
    };

    // Note that the following subcommands have largely been copied to RegistrationOverrideCommands.
    // Any changes made here should be reflected there, if appropriate.

    private static async Task CmdSetBirthday(ShardInstance instance, GuildConfiguration gconf, SocketSlashCommand arg) {
        int inmonth, inday;
        try {
            (inmonth, inday) = ParseDate((string)arg.Data.Options.First().Value);
        } catch (FormatException e) {
            // Our parse method's FormatException has its message to send out to Discord.
            arg.RespondAsync(e.Message).Wait();
            return;
        }

        bool known;
        try {
            var user = await GuildUserConfiguration.LoadAsync(gconf.GuildId, arg.User.Id).ConfigureAwait(false);
            known = user.IsKnown;
            await user.UpdateAsync(inmonth, inday, user.TimeZone).ConfigureAwait(false);
        } catch (Exception ex) {
            Program.Log("Error", ex.ToString());
            arg.RespondAsync(ShardInstance.InternalError).Wait();
            return;
        }

        await arg.RespondAsync(":white_check_mark: Your birthday has been " +
            $"{ (known ? "updated to" : "recorded as") } **{inday:00}-{Common.MonthNames[inmonth]}**.").ConfigureAwait(false);
    }

    private static async Task CmdSetTimezone(ShardInstance instance, GuildConfiguration gconf, SocketSlashCommand arg) {
        var user = await GuildUserConfiguration.LoadAsync(gconf.GuildId, arg.User.Id).ConfigureAwait(false);
        if (!user.IsKnown) {
            await arg.RespondAsync(":x: You must have a birthday set before you can use this command.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }
        bool hasZone = user.TimeZone != null;

        string inZone;
        try {
            inZone = ParseTimeZone((string)arg.Data.Options.First().Value);
        } catch (Exception e) {
            arg.RespondAsync(e.Message).Wait();
            return;
        }
        await user.UpdateAsync(user.BirthMonth, user.BirthDay, inZone).ConfigureAwait(false);

        await arg.RespondAsync($":white_check_mark: Your time zone has been { (hasZone ? "updated" : "set") } to **{inZone}**.")
            .ConfigureAwait(false);
    }

    private static async Task CmdDelTz(ShardInstance instance, GuildConfiguration gconf, SocketSlashCommand arg) {
        var u = await GuildUserConfiguration.LoadAsync(gconf.GuildId, arg.User.Id).ConfigureAwait(false);
        if (!u.IsKnown) {
            await arg.RespondAsync(":white_check_mark: " + MsgNoData);
        } else if (u.TimeZone is null) {
            await arg.RespondAsync(":white_check_mark: You do not have any time zone information.");
        } else {
            await u.UpdateAsync(u.BirthMonth, u.BirthDay, null);
            await arg.RespondAsync(":white_check_mark: Your time zone information has been removed.");
        }
    }

    private static async Task CmdDelBd(ShardInstance instance, GuildConfiguration gconf, SocketSlashCommand arg) {
        var u = await GuildUserConfiguration.LoadAsync(gconf.GuildId, arg.User.Id).ConfigureAwait(false);
        if (u.IsKnown) {
            await u.DeleteAsync().ConfigureAwait(false);
            await arg.RespondAsync(":white_check_mark: Your birthday information has been removed.");
        } else {
            await arg.RespondAsync(":white_check_mark: " + MsgNoData);
        }
    }
}
