using BirthdayBot.Data;

namespace BirthdayBot.ApplicationCommands;

internal class RegistrationOverrideCommands : BotApplicationCommand {
    private delegate Task SubCommandHandler(GuildConfiguration gconf, SocketSlashCommand arg, Dictionary<string, object> subparam);

    #region Help strings
    public const string HelpOverride = "Run certain commands on behalf of other users.";

    const string HelpOptTarget = "The user whose data to modify.";
    #endregion

    public override IEnumerable<ApplicationCommandProperties> GetCommands() => new ApplicationCommandProperties[] {
            new SlashCommandBuilder()
                .WithName("override")
                .WithDescription(HelpPfxModOnly + HelpOverride)
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("set-birthday")
                    .WithDescription(HelpPfxModOnly + "Sets or updates a user's birthday on their behalf.")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption(new SlashCommandOptionBuilder()
                        .WithName("target")
                        .WithType(ApplicationCommandOptionType.User)
                        .WithDescription(HelpOptTarget)
                        .WithRequired(true)
                    ).AddOption(new SlashCommandOptionBuilder()
                        .WithName("date")
                        .WithType(ApplicationCommandOptionType.String)
                        .WithDescription(RegistrationCommands.HelpOptDate)
                        .WithRequired(true)
                    )
                ).AddOption(new SlashCommandOptionBuilder()
                    .WithName("set-timezone")
                    .WithDescription(HelpPfxModOnly + "Sets or updates a user's time zone on their behalf.")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption(new SlashCommandOptionBuilder()
                        .WithName("target")
                        .WithType(ApplicationCommandOptionType.User)
                        .WithDescription(HelpOptTarget)
                        .WithRequired(true)
                    ).AddOption(new SlashCommandOptionBuilder()
                        .WithName("zone")
                        .WithType(ApplicationCommandOptionType.String)
                        .WithDescription(RegistrationCommands.HelpOptZone)
                        .WithRequired(true)
                    )
                ).AddOption(new SlashCommandOptionBuilder()
                    .WithName("remove-timezone")
                    .WithDescription(HelpPfxModOnly + "Removes a user's time zone on their behalf.")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption(new SlashCommandOptionBuilder()
                            .WithName("target")
                            .WithType(ApplicationCommandOptionType.User)
                            .WithDescription(HelpOptTarget)
                            .WithRequired(true)
                    )
                ).AddOption(new SlashCommandOptionBuilder()
                    .WithName("remove-birthday")
                    .WithDescription(HelpPfxModOnly + "Removes a user's data from the bot on their behalf.")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption(new SlashCommandOptionBuilder()
                        .WithName("target")
                        .WithType(ApplicationCommandOptionType.User)
                        .WithDescription(HelpOptTarget)
                        .WithRequired(true)
                    )
                ).Build()
        };
    public override CommandResponder? GetHandlerFor(string commandName) => commandName switch {
        "override" => CmdOverride,
        _ => null
    };

    /*
     * Personally, this confuses me. So here are some notes:
     * arg.Data.Options contains the SubCommand and only the SubCommand. Its name is the subcommand's name.
     * arg.Data.Options.Options then contains the options. "target" and others, all within the same collection. 
     */
    private Task CmdOverride(ShardInstance instance, GuildConfiguration gconf, SocketSlashCommand arg) {
        SubCommandHandler? subh = arg.Data.Options.First().Name switch {
            "set-birthday" => SubCmdSetBd,
            "set-timezone" => SubCmdSetTz,
            "remove-timezone" => SubCmdDelTz,
            "remove-birthday" => SubCmdDelBd,
            _ => null
        };

        if (subh == null) {
            instance.Log($"{nameof(RegistrationOverrideCommands)}", $"Encountered unknown subcommand {arg.Data.Name}");
            return arg.RespondAsync(ShardInstance.UnknownCommandError, ephemeral: true);
        }

        var subparam = ((SocketSlashCommandDataOption)arg.Data.Options.First()).Options.ToDictionary(o => o.Name, o => o.Value);
        return subh(gconf, arg, subparam);
    }

    // Note that the following subcommands have largely been copied from RegistrationCommands.
    // Any changes made there should be reflected here, if appropriate.
    // TODO A common base class might be more appropriate...

    private async Task SubCmdSetBd(GuildConfiguration gconf, SocketSlashCommand arg, Dictionary<string, object> subparam) {
        var target = (SocketGuildUser)subparam["target"];
        int inmonth, inday;
        try {
            (inmonth, inday) = ParseDate((string)subparam["date"]);
        } catch (FormatException e) {
            // Our parse method's FormatException has its message to send out to Discord.
            arg.RespondAsync(e.Message).Wait();
            return;
        }

        bool known;
        try {
            var user = await GuildUserConfiguration.LoadAsync(gconf.GuildId, target.Id).ConfigureAwait(false);
            known = user.IsKnown;
            await user.UpdateAsync(inmonth, inday, user.TimeZone).ConfigureAwait(false);
        } catch (Exception ex) {
            Program.Log("Error", ex.ToString());
            arg.RespondAsync(ShardInstance.InternalError).Wait();
            return;
        }

        await arg.RespondAsync($":white_check_mark: {target}'s birthday has been " +
            $"{ (known ? "updated to" : "recorded as") } **{inday:00}-{Common.MonthNames[inmonth]}**.").ConfigureAwait(false);
    }

    private async Task SubCmdSetTz(GuildConfiguration gconf, SocketSlashCommand arg, Dictionary<string, object> subparam) {
        var target = (SocketGuildUser)subparam["target"];
        var user = await GuildUserConfiguration.LoadAsync(gconf.GuildId, target.Id).ConfigureAwait(false);
        if (!user.IsKnown) {
            await arg.RespondAsync(":x: The user must have a birthday set before you can use this command.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }
        bool hasZone = user.TimeZone != null;

        string inZone;
        try {
            inZone = ParseTimeZone((string)subparam["zone"]);
        } catch (Exception e) {
            arg.RespondAsync(e.Message).Wait();
            return;
        }
        await user.UpdateAsync(user.BirthMonth, user.BirthDay, inZone).ConfigureAwait(false);

        await arg.RespondAsync($":white_check_mark: {target}'s time zone has been { (hasZone ? "updated" : "set") } to **{inZone}**.")
            .ConfigureAwait(false);
    }

    private async Task SubCmdDelTz(GuildConfiguration gconf, SocketSlashCommand arg, Dictionary<string, object> subparam) {
        var target = (SocketGuildUser)subparam["target"];
        var u = await GuildUserConfiguration.LoadAsync(gconf.GuildId, target.Id).ConfigureAwait(false);
        if (!u.IsKnown) {
            await arg.RespondAsync(":white_check_mark: User is not registered. Nothing to remove.");
        } else if (u.TimeZone is null) {
            await arg.RespondAsync(":white_check_mark: User does not have zone registered. Nothing to remove.");
        } else {
            await u.UpdateAsync(u.BirthMonth, u.BirthDay, null);
            await arg.RespondAsync($":white_check_mark: {target}'s time zone information has been removed.");
        }
    }

    private async Task SubCmdDelBd(GuildConfiguration gconf, SocketSlashCommand arg, Dictionary<string, object> subparam) {
        var target = (SocketGuildUser)subparam["target"];
        var u = await GuildUserConfiguration.LoadAsync(gconf.GuildId, target.Id).ConfigureAwait(false);
        if (u.IsKnown) {
            await u.DeleteAsync().ConfigureAwait(false);
            await arg.RespondAsync($":white_check_mark: {target}'s birthday information has been removed.");
        } else {
            await arg.RespondAsync(":white_check_mark: User is not registered. Nothing to remove.");
        }
    }
}
