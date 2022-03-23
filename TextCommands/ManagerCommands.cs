#pragma warning disable CS0618
using BirthdayBot.Data;
using System.Text;
using System.Text.RegularExpressions;

namespace BirthdayBot.TextCommands;

internal class ManagerCommands : CommandsCommon {
    private static readonly string ConfErrorPostfix =
        $" Refer to the `{CommandPrefix}help-config` command for information on this command's usage.";
    private delegate Task ConfigSubcommand(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel);

    private readonly Dictionary<string, ConfigSubcommand> _subcommands;
    private readonly Dictionary<string, CommandHandler> _usercommands;

    public ManagerCommands(Configuration db, IEnumerable<(string, CommandHandler)> userCommands) : base(db) {
        _subcommands = new Dictionary<string, ConfigSubcommand>(StringComparer.OrdinalIgnoreCase)
        {
                { "role", ScmdRole },
                { "channel", ScmdChannel },
                { "modrole", ScmdModRole },
                { "message", ScmdAnnounceMsg },
                { "messagepl", ScmdAnnounceMsg },
                { "ping", ScmdPing },
                { "zone", ScmdZone },
                { "block", ScmdBlock },
                { "unblock", ScmdBlock },
                { "moderated", ScmdModerated }
            };

        // Set up local copy of all user commands accessible by the override command
        _usercommands = new Dictionary<string, CommandHandler>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in userCommands) _usercommands.Add(item.Item1, item.Item2);
    }

    public override IEnumerable<(string, CommandHandler)> Commands
        => new List<(string, CommandHandler)>()
        {
                ("config", CmdConfigDispatch),
                ("override", CmdOverride),
                ("check", CmdCheck),
                ("test", CmdCheck)
        };

    #region Documentation
    public static readonly CommandDocumentation DocOverride =
        new(new string[] { "override (user ping or ID) (command w/ parameters)" },
            "Perform certain commands on behalf of another user.", null);
    #endregion

    private async Task CmdConfigDispatch(ShardInstance instance, GuildConfiguration gconf,
                                        string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser) {
        // Ignore those without the proper permissions.
        if (!gconf.IsBotModerator(reqUser)) {
            await reqChannel.SendMessageAsync(":x: This command may only be used by bot moderators.").ConfigureAwait(false);
            return;
        }

        if (param.Length < 2) {
            await reqChannel.SendMessageAsync($":x: See `{CommandPrefix}help-config` for information on how to use this command.")
                .ConfigureAwait(false);
            return;
        }

        // Special case: Restrict 'modrole' to only guild managers, not mods
        if (string.Equals(param[1], "modrole", StringComparison.OrdinalIgnoreCase) && !reqUser.GuildPermissions.ManageGuild) {
            await reqChannel.SendMessageAsync(":x: This command may only be used by those with the `Manage Server` permission.")
                .ConfigureAwait(false);
            return;
        }

        // Subcommands get a subset of the parameters, to make things a little easier.
        var confparam = new string[param.Length - 1];
        Array.Copy(param, 1, confparam, 0, param.Length - 1);

        if (_subcommands.TryGetValue(confparam[0], out var h)) {
            await h(confparam, gconf, reqChannel).ConfigureAwait(false);
        }
    }

    #region Configuration sub-commands
    // Birthday role set
    private async Task ScmdRole(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel) {
        if (param.Length != 2) {
            await reqChannel.SendMessageAsync(":x: A role name, role mention, or ID value must be specified.")
                .ConfigureAwait(false);
            return;
        }
        var guild = reqChannel.Guild;
        var role = FindUserInputRole(param[1], guild);

        if (role == null) {
            await reqChannel.SendMessageAsync(RoleInputError).ConfigureAwait(false);
        } else if (role.Id == reqChannel.Guild.EveryoneRole.Id) {
            await reqChannel.SendMessageAsync(":x: You cannot set that as the birthday role.").ConfigureAwait(false);
        } else {
            gconf.RoleId = role.Id;
            await gconf.UpdateAsync().ConfigureAwait(false);
            await reqChannel.SendMessageAsync($":white_check_mark: The birthday role has been set as **{role.Name}**.")
                .ConfigureAwait(false);
        }
    }

    // Ping setting
    private async Task ScmdPing(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel) {
        const string InputErr = ":x: You must specify either `off` or `on` in this setting.";
        if (param.Length != 2) {
            await reqChannel.SendMessageAsync(InputErr).ConfigureAwait(false);
            return;
        }

        var input = param[1].ToLower();
        bool setting;
        string result;
        if (input == "off") {
            setting = false;
            result = ":white_check_mark: Announcement pings are now **off**.";
        } else if (input == "on") {
            setting = true;
            result = ":white_check_mark: Announcement pings are now **on**.";
        } else {
            await reqChannel.SendMessageAsync(InputErr).ConfigureAwait(false);
            return;
        }

        gconf.AnnouncePing = setting;
        await gconf.UpdateAsync().ConfigureAwait(false);
        await reqChannel.SendMessageAsync(result).ConfigureAwait(false);
    }

    // Announcement channel set
    private async Task ScmdChannel(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel) {
        if (param.Length == 1) // No extra parameter. Unset announcement channel.
        {
            // Extra detail: Show a unique message if a channel hadn't been set prior.
            if (!gconf.AnnounceChannelId.HasValue) {
                await reqChannel.SendMessageAsync(":x: There is no announcement channel set. Nothing to unset.")
                    .ConfigureAwait(false);
                return;
            }

            gconf.AnnounceChannelId = null;
            await gconf.UpdateAsync();
            await reqChannel.SendMessageAsync(":white_check_mark: The announcement channel has been unset.")
                .ConfigureAwait(false);
        } else {
            // Determine channel from input
            ulong chId = 0;

            // Try channel mention
            var m = ChannelMention.Match(param[1]);
            if (m.Success) {
                chId = ulong.Parse(m.Groups[1].Value);
            } else if (ulong.TryParse(param[1], out chId)) {
                // Continue...
            } else {
                // Try text-based search
                var res = reqChannel.Guild.TextChannels
                    .FirstOrDefault(ch => string.Equals(ch.Name, param[1], StringComparison.OrdinalIgnoreCase));
                if (res != null) {
                    chId = res.Id; // Yep, we're throwing the full result away only to go look for it again later...
                }
            }

            // Attempt to find channel in guild
            SocketTextChannel? chTt = null;
            if (chId != 0) chTt = reqChannel.Guild.GetTextChannel(chId);
            if (chTt == null) {
                await reqChannel.SendMessageAsync(":x: Unable to find the specified channel.").ConfigureAwait(false);
                return;
            }

            // Update the value
            gconf.AnnounceChannelId = chId;
            await gconf.UpdateAsync().ConfigureAwait(false);

            // Report the success
            await reqChannel.SendMessageAsync($":white_check_mark: The announcement channel is now set to <#{chId}>.")
                .ConfigureAwait(false);
        }
    }

    // Moderator role set
    private async Task ScmdModRole(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel) {
        if (param.Length != 2) {
            await reqChannel.SendMessageAsync(":x: A role name, role mention, or ID value must be specified.")
                .ConfigureAwait(false);
            return;
        }
        var guild = reqChannel.Guild;
        var role = FindUserInputRole(param[1], guild);

        if (role == null) {
            await reqChannel.SendMessageAsync(RoleInputError).ConfigureAwait(false);
        } else {
            gconf.ModeratorRole = role.Id;
            await gconf.UpdateAsync().ConfigureAwait(false);
            await reqChannel.SendMessageAsync($":white_check_mark: The moderator role is now **{role.Name}**.")
                .ConfigureAwait(false);
        }
    }

    // Guild default time zone set/unset
    private async Task ScmdZone(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel) {
        if (param.Length == 1) // No extra parameter. Unset guild default time zone.
        {
            // Extra detail: Show a unique message if there is no set zone.
            if (!gconf.AnnounceChannelId.HasValue) {
                await reqChannel.SendMessageAsync(":x: A default zone is not set. Nothing to unset.").ConfigureAwait(false);
                return;
            }

            gconf.TimeZone = null;
            await gconf.UpdateAsync().ConfigureAwait(false);
            await reqChannel.SendMessageAsync(":white_check_mark: The default time zone preference has been removed.")
                .ConfigureAwait(false);
        } else {
            // Parameter check.
            string zone;
            try {
                zone = ParseTimeZone(param[1]);
            } catch (FormatException ex) {
                reqChannel.SendMessageAsync(ex.Message).Wait();
                return;
            }

            // Update value
            gconf.TimeZone = zone;
            await gconf.UpdateAsync().ConfigureAwait(false);

            // Report the success
            await reqChannel.SendMessageAsync($":white_check_mark: The server's time zone has been set to **{zone}**.")
                .ConfigureAwait(false);
        }
    }

    // Block/unblock individual non-manager users from using commands.
    private async Task ScmdBlock(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel) {
        if (param.Length != 2) {
            await reqChannel.SendMessageAsync(ParameterError + ConfErrorPostfix).ConfigureAwait(false);
            return;
        }

        bool doBan = param[0].ToLower() == "block"; // true = block, false = unblock

        if (!TryGetUserId(param[1], out ulong inputId)) {
            await reqChannel.SendMessageAsync(BadUserError).ConfigureAwait(false);
            return;
        }

        var isBanned = await gconf.IsUserBlockedAsync(inputId).ConfigureAwait(false);
        if (doBan) {
            if (!isBanned) {
                await gconf.BlockUserAsync(inputId).ConfigureAwait(false);
                await reqChannel.SendMessageAsync(":white_check_mark: User has been blocked.").ConfigureAwait(false);
            } else {
                // TODO bug: this is incorrectly always displayed when in moderated mode
                await reqChannel.SendMessageAsync(":white_check_mark: User is already blocked.").ConfigureAwait(false);
            }
        } else {
            if (await gconf.UnblockUserAsync(inputId).ConfigureAwait(false)) {
                await reqChannel.SendMessageAsync(":white_check_mark: User is now unblocked.").ConfigureAwait(false);
            } else {
                await reqChannel.SendMessageAsync(":white_check_mark: The specified user is not blocked.").ConfigureAwait(false);
            }
        }
    }

    // "moderated on/off" - Sets/unsets moderated mode.
    private async Task ScmdModerated(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel) {
        if (param.Length != 2) {
            await reqChannel.SendMessageAsync(ParameterError + ConfErrorPostfix).ConfigureAwait(false);
            return;
        }

        var parameter = param[1].ToLower();
        bool modSet;
        if (parameter == "on") modSet = true;
        else if (parameter == "off") modSet = false;
        else {
            await reqChannel.SendMessageAsync(":x: Expecting `on` or `off` as a parameter." + ConfErrorPostfix)
                .ConfigureAwait(false);
            return;
        }

        if (gconf.IsModerated == modSet) {
            await reqChannel.SendMessageAsync($":white_check_mark: Moderated mode is already {parameter}.")
                .ConfigureAwait(false);
        } else {
            gconf.IsModerated = modSet;
            await gconf.UpdateAsync().ConfigureAwait(false);
            await reqChannel.SendMessageAsync($":white_check_mark: Moderated mode has been turned {parameter}.")
                .ConfigureAwait(false);
        }
    }

    // Sets/unsets custom announcement message.
    private async Task ScmdAnnounceMsg(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel) {
        var plural = param[0].ToLower().EndsWith("pl");

        string? newmsg;
        bool clear;
        if (param.Length == 2) {
            newmsg = param[1];
            clear = false;
        } else {
            newmsg = null;
            clear = true;
        }

        (string?, string?) update;
        if (!plural) update = (newmsg, gconf.AnnounceMessages.Item2);
        else update = (gconf.AnnounceMessages.Item1, newmsg);
        gconf.AnnounceMessages = update;
        await gconf.UpdateAsync().ConfigureAwait(false);
        await reqChannel.SendMessageAsync(string.Format(":white_check_mark: The {0} birthday announcement message has been {1}.",
            plural ? "plural" : "singular", clear ? "reset" : "updated")).ConfigureAwait(false);
    }
    #endregion

    // Execute command as another user
    private async Task CmdOverride(ShardInstance instance, GuildConfiguration gconf,
                                   string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser) {
        // Moderators only. As with config, silently drop if this check fails.
        if (!gconf.IsBotModerator(reqUser)) return;

        if (!await HasMemberCacheAsync(reqChannel.Guild)) {
            await reqChannel.SendMessageAsync(MemberCacheEmptyError);
            return;
        }

        if (param.Length != 3) {
            await reqChannel.SendMessageAsync(ParameterError, embed: DocOverride.UsageEmbed).ConfigureAwait(false);
            return;
        }

        // Second parameter: determine the user to act as
        if (!TryGetUserId(param[1], out ulong user)) {
            await reqChannel.SendMessageAsync(BadUserError, embed: DocOverride.UsageEmbed).ConfigureAwait(false);
            return;
        }
        var overuser = reqChannel.Guild.GetUser(user);
        if (overuser == null) {
            await reqChannel.SendMessageAsync(BadUserError, embed: DocOverride.UsageEmbed).ConfigureAwait(false);
            return;
        }

        // Third parameter: determine command to invoke.
        // Reminder that we're only receiving a param array of size 3 at maximum. String must be split again.
        var overparam = param[2].Split(" ", 3, StringSplitOptions.RemoveEmptyEntries);
        var cmdsearch = overparam[0];
        if (cmdsearch.StartsWith(CommandPrefix)) {
            // Strip command prefix to search for the given command.
            cmdsearch = cmdsearch[CommandPrefix.Length..];
        } else {
            // Add command prefix to input, just in case.
            overparam[0] = CommandPrefix + overparam[0].ToLower();
        }
        if (!_usercommands.TryGetValue(cmdsearch, out var action)) {
            await reqChannel.SendMessageAsync(
                $":x: `{cmdsearch}` is not an overridable command.", embed: DocOverride.UsageEmbed)
                .ConfigureAwait(false);
            return;
        }

        // Preparations complete. Run the command.
        await reqChannel.SendMessageAsync(
            $"Executing `{cmdsearch.ToLower()}` on behalf of {overuser.Nickname ?? overuser.Username}:")
            .ConfigureAwait(false);
        await action.Invoke(instance, gconf, overparam, reqChannel, overuser).ConfigureAwait(false);
    }

    // Troubleshooting tool: Check for common problems regarding typical background operation.
    private async Task CmdCheck(ShardInstance instance, GuildConfiguration gconf,
                               string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser) {
        // Moderators only. As with config, silently drop if this check fails.
        if (!gconf.IsBotModerator(reqUser)) return;

        if (param.Length != 1) {
            // Too many parameters
            // Note: Non-standard error display
            await reqChannel.SendMessageAsync(NoParameterError).ConfigureAwait(false);
            return;
        }

        static string DoTestFor(string label, Func<bool> test) => $"{label}: { (test() ? ":white_check_mark: Yes" : ":x: No") }";
        var result = new StringBuilder();
        var guild = reqChannel.Guild;
        var conf = await GuildConfiguration.LoadAsync(guild.Id, true).ConfigureAwait(false);
        var userbdays = await GuildUserConfiguration.LoadAllAsync(guild.Id).ConfigureAwait(false);

        result.AppendLine($"Server ID: `{guild.Id}` | Bot shard ID: `{instance.ShardId:00}`");
        result.AppendLine($"Number of registered birthdays: `{ userbdays.Count() }`");
        result.AppendLine($"Server time zone: `{ (conf?.TimeZone ?? "Not set - using UTC") }`");
        result.AppendLine();

        bool hasMembers = Common.HasMostMembersDownloaded(guild);
        result.Append(DoTestFor("Bot has obtained the user list", () => hasMembers));
        result.AppendLine($" - Has `{guild.DownloadedMemberCount}` of `{guild.MemberCount}` members.");
        int bdayCount = -1;
        result.Append(DoTestFor("Birthday processing", delegate {
            if (!hasMembers) return false;
            bdayCount = BackgroundServices.BirthdayRoleUpdate.GetGuildCurrentBirthdays(userbdays, conf?.TimeZone).Count;
            return true;
        }));
        if (hasMembers) result.AppendLine($" - `{bdayCount}` user(s) currently having a birthday.");
        else result.AppendLine(" - Previous step failed.");
        result.AppendLine();

        result.AppendLine(DoTestFor("Birthday role set with `bb.config role`", delegate {
            if (conf == null) return false;
            SocketRole? role = guild.GetRole(conf.RoleId ?? 0);
            return role != null;
        }));
        result.AppendLine(DoTestFor("Birthday role can be managed by bot", delegate {
            if (conf == null) return false;
            SocketRole? role = guild.GetRole(conf.RoleId ?? 0);
            if (role == null) return false;
            return guild.CurrentUser.GuildPermissions.ManageRoles && role.Position < guild.CurrentUser.Hierarchy;
        }));
        result.AppendLine();

        SocketTextChannel? channel = null;
        result.AppendLine(DoTestFor("(Optional) Announcement channel set with `bb.config channel`", delegate {
            if (conf == null) return false;
            channel = guild.GetTextChannel(conf.AnnounceChannelId ?? 0);
            return channel != null;
        }));
        string disp = channel == null ? "announcement channel" : $"<#{channel.Id}>";
        result.AppendLine(DoTestFor($"(Optional) Bot can send messages into { disp }", delegate {
            if (channel == null) return false;
            return guild.CurrentUser.GetPermissions(channel).SendMessages;
        }));

        await reqChannel.SendMessageAsync(embed: new EmbedBuilder() {
            Author = new EmbedAuthorBuilder() { Name = "Status and config check" },
            Description = result.ToString()
        }.Build()).ConfigureAwait(false);

        const int announceMsgPreviewLimit = 350;
        static string prepareAnnouncePreview(string announce) {
            string trunc = announce.Length > announceMsgPreviewLimit ? announce[..announceMsgPreviewLimit] + "`(...)`" : announce;
            var result = new StringBuilder();
            foreach (var line in trunc.Split('\n'))
                result.AppendLine($"> {line}");
            return result.ToString();
        }
        if (conf != null && (conf.AnnounceMessages.Item1 != null || conf.AnnounceMessages.Item2 != null)) {
            var em = new EmbedBuilder().WithAuthor(new EmbedAuthorBuilder() { Name = "Custom announce messages:" });
            var dispAnnounces = new StringBuilder("Custom announcement message(s):\n");
            if (conf.AnnounceMessages.Item1 != null) {
                em = em.AddField("Single", prepareAnnouncePreview(conf.AnnounceMessages.Item1));
            }
            if (conf.AnnounceMessages.Item2 != null) {
                em = em.AddField("Plural", prepareAnnouncePreview(conf.AnnounceMessages.Item2));
            }
            await reqChannel.SendMessageAsync(embed: em.Build()).ConfigureAwait(false);
        }
    }

    #region Common/helper methods
    private const string RoleInputError = ":x: Unable to determine the given role.";
    private static readonly Regex RoleMention = new(@"<@?&(?<snowflake>\d+)>", RegexOptions.Compiled);

    private static SocketRole? FindUserInputRole(string inputStr, SocketGuild guild) {
        // Resembles a role mention? Strip it to the pure number
        var input = inputStr;
        var rmatch = RoleMention.Match(input);
        if (rmatch.Success) input = rmatch.Groups["snowflake"].Value;

        // Attempt to get role by ID, or null
        if (ulong.TryParse(input, out ulong rid)) {
            return guild.GetRole(rid);
        } else {
            // Reset the search value on the off chance there's a role name that actually resembles a role ping.
            input = inputStr;
        }

        // If not already found, attempt to search role by string name
        foreach (var search in guild.Roles) {
            if (string.Equals(search.Name, input, StringComparison.OrdinalIgnoreCase)) return search;
        }

        return null;
    }
    #endregion
}
