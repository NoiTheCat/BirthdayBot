using BirthdayBot.Data;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BirthdayBot.UserInterface
{
    internal class ManagerCommands : CommandsCommon
    {
        private static readonly string ConfErrorPostfix =
            $" Refer to the `{CommandPrefix}help-config` command for information on this command's usage.";
        private delegate Task ConfigSubcommand(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel);

        private readonly Dictionary<string, ConfigSubcommand> _subcommands;
        private readonly Dictionary<string, CommandHandler> _usercommands;
        private readonly Func<SocketGuild, Task<string>> _bRoleUpdAccess;

        public ManagerCommands(BirthdayBot inst, Configuration db,
            IEnumerable<(string, CommandHandler)> userCommands, Func<SocketGuild, Task<string>> brsingleupdate)
            : base(inst, db)
        {
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

            // and access to the otherwise automated guild update function
            _bRoleUpdAccess = brsingleupdate;
        }

        public override IEnumerable<(string, CommandHandler)> Commands
            => new List<(string, CommandHandler)>()
            {
                ("config", CmdConfigDispatch),
                ("override", CmdOverride),
                ("test", CmdTest)
            };

        #region Documentation
        public static readonly CommandDocumentation DocOverride =
            new CommandDocumentation(new string[] { "override (user ping or ID) (command w/ parameters)" },
                "Perform certain commands on behalf of another user.", null);
        #endregion

        private async Task CmdConfigDispatch(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel, SocketGuildUser reqUser)
        {
            // Ignore those without the proper permissions.
            if (!gconf.IsBotModerator(reqUser))
            {
                await reqChannel.SendMessageAsync(":x: This command may only be used by bot moderators.");
                return;
            }

            if (param.Length < 2)
            {
                await reqChannel.SendMessageAsync($":x: See `{CommandPrefix}help-config` for information on how to use this command.");
                return;
            }

            // Special case: Restrict 'modrole' to only guild managers, not mods
            if (string.Equals(param[1], "modrole", StringComparison.OrdinalIgnoreCase) && !reqUser.GuildPermissions.ManageGuild)
            {
                await reqChannel.SendMessageAsync(":x: This command may only be used by those with the `Manage Server` permission.");
                return;
            }

            // Subcommands get a subset of the parameters, to make things a little easier.
            var confparam = new string[param.Length - 1];
            Array.Copy(param, 1, confparam, 0, param.Length - 1);

            if (_subcommands.TryGetValue(confparam[0], out ConfigSubcommand h))
            {
                await h(confparam, gconf, reqChannel);
            }
        }

        #region Configuration sub-commands
        // Birthday role set
        private async Task ScmdRole(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel)
        {
            if (param.Length != 2)
            {
                await reqChannel.SendMessageAsync(":x: A role name, role mention, or ID value must be specified.");
                return;
            }
            var guild = reqChannel.Guild;
            var role = FindUserInputRole(param[1], guild);

            if (role == null)
            {
                await reqChannel.SendMessageAsync(RoleInputError);
            }
            else if (role.Id == reqChannel.Guild.EveryoneRole.Id)
            {
                await reqChannel.SendMessageAsync(":x: You cannot set that as the birthday role.");
            }
            else
            {
                gconf.RoleId = role.Id;
                await gconf.UpdateAsync();
                await reqChannel.SendMessageAsync($":white_check_mark: The birthday role has been set as **{role.Name}**.");
            }
        }

        // Ping setting
        private async Task ScmdPing(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel)
        {
            const string InputErr = ":x: You must specify either `off` or `on` in this setting.";
            if (param.Length != 2)
            {
                await reqChannel.SendMessageAsync(InputErr);
                return;
            }

            var input = param[1].ToLower();
            bool setting;
            string result;
            if (input == "off")
            {
                setting = false;
                result = ":white_check_mark: Announcement pings are now **off**.";
            }
            else if (input == "on")
            {
                setting = true;
                result = ":white_check_mark: Announcement pings are now **on**.";
            }
            else
            {
                await reqChannel.SendMessageAsync(InputErr);
                return;
            }

            gconf.AnnouncePing = setting;
            await gconf.UpdateAsync();
            await reqChannel.SendMessageAsync(result);
        }

        // Announcement channel set
        private async Task ScmdChannel(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel)
        {
            if (param.Length == 1) // No extra parameter. Unset announcement channel.
            {
                // Extra detail: Show a unique message if a channel hadn't been set prior.
                if (!gconf.AnnounceChannelId.HasValue)
                {
                    await reqChannel.SendMessageAsync(":x: There is no announcement channel set. Nothing to unset.");
                    return;
                }

                gconf.AnnounceChannelId = null;
                await gconf.UpdateAsync();
                await reqChannel.SendMessageAsync(":white_check_mark: The announcement channel has been unset.");
            }
            else
            {
                // Determine channel from input
                ulong chId = 0;

                // Try channel mention
                var m = ChannelMention.Match(param[1]);
                if (m.Success)
                {
                    chId = ulong.Parse(m.Groups[1].Value);
                }
                else if (ulong.TryParse(param[1], out chId))
                {
                    // Continue...
                }
                else
                {
                    // Try text-based search
                    var res = reqChannel.Guild.TextChannels
                        .FirstOrDefault(ch => string.Equals(ch.Name, param[1], StringComparison.OrdinalIgnoreCase));
                    if (res != null)
                    {
                        chId = res.Id; // Yep, we're throwing the full result away only to go look for it again later...
                    }
                }

                // Attempt to find channel in guild
                SocketTextChannel chTt = null;
                if (chId != 0) chTt = reqChannel.Guild.GetTextChannel(chId);
                if (chTt == null)
                {
                    await reqChannel.SendMessageAsync(":x: Unable to find the specified channel.");
                    return;
                }

                // Update the value
                gconf.AnnounceChannelId = chId;
                await gconf.UpdateAsync();

                // Report the success
                await reqChannel.SendMessageAsync($":white_check_mark: The announcement channel is now set to <#{chId}>.");
            }
        }

        // Moderator role set
        private async Task ScmdModRole(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel)
        {
            if (param.Length != 2)
            {
                await reqChannel.SendMessageAsync(":x: A role name, role mention, or ID value must be specified.");
                return;
            }
            var guild = reqChannel.Guild;
            var role = FindUserInputRole(param[1], guild);

            if (role == null)
            {
                await reqChannel.SendMessageAsync(RoleInputError);
            }
            else
            {
                gconf.ModeratorRole = role.Id;
                await gconf.UpdateAsync();
                await reqChannel.SendMessageAsync($":white_check_mark: The moderator role is now **{role.Name}**.");
            }
        }

        // Guild default time zone set/unset
        private async Task ScmdZone(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel)
        {
            if (param.Length == 1) // No extra parameter. Unset guild default time zone.
            {
                // Extra detail: Show a unique message if there is no set zone.
                if (!gconf.AnnounceChannelId.HasValue)
                {
                    await reqChannel.SendMessageAsync(":x: A default zone is not set. Nothing to unset.");
                    return;
                }

                gconf.TimeZone = null;
                await gconf.UpdateAsync();
                await reqChannel.SendMessageAsync(":white_check_mark: The default time zone preference has been removed.");
            }
            else
            {
                // Parameter check.
                string zone;
                try
                {
                    zone = ParseTimeZone(param[1]);
                }
                catch (FormatException ex)
                {
                    reqChannel.SendMessageAsync(ex.Message).Wait();
                    return;
                }

                // Update value
                gconf.TimeZone = zone;
                await gconf.UpdateAsync();

                // Report the success
                await reqChannel.SendMessageAsync($":white_check_mark: The server's time zone has been set to **{zone}**.");
            }
        }

        // Block/unblock individual non-manager users from using commands.
        private async Task ScmdBlock(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel)
        {
            if (param.Length != 2)
            {
                await reqChannel.SendMessageAsync(ParameterError + ConfErrorPostfix);
                return;
            }

            bool doBan = param[0].ToLower() == "block"; // true = block, false = unblock

            if (!TryGetUserId(param[1], out ulong inputId))
            {
                await reqChannel.SendMessageAsync(BadUserError);
                return;
            }

            var isBanned = await gconf.IsUserBlockedAsync(inputId);
            if (doBan)
            {
                if (!isBanned)
                {
                    await gconf.BlockUserAsync(inputId);
                    await reqChannel.SendMessageAsync(":white_check_mark: User has been blocked.");
                }
                else
                {
                    // TODO bug: this is incorrectly always displayed when in moderated mode
                    await reqChannel.SendMessageAsync(":white_check_mark: User is already blocked.");
                }
            }
            else
            {
                if (await gconf.UnblockUserAsync(inputId))
                {
                    await reqChannel.SendMessageAsync(":white_check_mark: User is now unblocked.");
                }
                else
                {
                    await reqChannel.SendMessageAsync(":white_check_mark: The specified user is not blocked.");
                }
            }
        }

        // "moderated on/off" - Sets/unsets moderated mode.
        private async Task ScmdModerated(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel)
        {
            if (param.Length != 2)
            {
                await reqChannel.SendMessageAsync(ParameterError + ConfErrorPostfix);
                return;
            }

            var parameter = param[1].ToLower();
            bool modSet;
            if (parameter == "on") modSet = true;
            else if (parameter == "off") modSet = false;
            else
            {
                await reqChannel.SendMessageAsync(":x: Expecting `on` or `off` as a parameter." + ConfErrorPostfix);
                return;
            }

            if (gconf.IsModerated == modSet)
            {
                await reqChannel.SendMessageAsync($":white_check_mark: Moderated mode is already {parameter}.");
            }
            else
            {
                gconf.IsModerated = modSet;
                await gconf.UpdateAsync();
                await reqChannel.SendMessageAsync($":white_check_mark: Moderated mode has been turned {parameter}.");
            }
        }

        // Sets/unsets custom announcement message.
        private async Task ScmdAnnounceMsg(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel)
        {
            var plural = param[0].ToLower().EndsWith("pl");

            string newmsg;
            bool clear;
            if (param.Length == 2)
            {
                newmsg = param[1];
                clear = false;
            }
            else
            {
                newmsg = null;
                clear = true;
            }

            (string, string) update;
            if (!plural) update = (newmsg, gconf.AnnounceMessages.Item2);
            else update = (gconf.AnnounceMessages.Item1, newmsg);
            gconf.AnnounceMessages = update;
            await gconf.UpdateAsync();
            await reqChannel.SendMessageAsync(string.Format(":white_check_mark: The {0} birthday announcement message has been {1}.",
                plural ? "plural" : "singular", clear ? "reset" : "updated"));
        }
        #endregion

        // Execute command as another user
        private async Task CmdOverride(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel, SocketGuildUser reqUser)
        {
            // Moderators only. As with config, silently drop if this check fails.
            if (!gconf.IsBotModerator(reqUser)) return;

            if (param.Length != 3)
            {
                await reqChannel.SendMessageAsync(ParameterError, embed: DocOverride.UsageEmbed);
                return;
            }

            // Second parameter: determine the user to act as
            if (!TryGetUserId(param[1], out ulong user))
            {
                await reqChannel.SendMessageAsync(BadUserError, embed: DocOverride.UsageEmbed);
                return;
            }
            var overuser = reqChannel.Guild.GetUser(user);
            if (overuser == null)
            {
                await reqChannel.SendMessageAsync(BadUserError, embed: DocOverride.UsageEmbed);
                return;
            }

            // Third parameter: determine command to invoke.
            // Reminder that we're only receiving a param array of size 3 at maximum. String must be split again.
            var overparam = param[2].Split(" ", 3, StringSplitOptions.RemoveEmptyEntries);
            var cmdsearch = overparam[0];
            if (cmdsearch.StartsWith(CommandPrefix))
            {
                // Strip command prefix to search for the given command.
                cmdsearch = cmdsearch.Substring(CommandPrefix.Length);
            }
            else
            {
                // Add command prefix to input, just in case.
                overparam[0] = CommandPrefix + overparam[0].ToLower();
            }
            if (!_usercommands.TryGetValue(cmdsearch, out CommandHandler action))
            {
                await reqChannel.SendMessageAsync($":x: `{cmdsearch}` is not an overridable command.", embed: DocOverride.UsageEmbed);
                return;
            }

            // Preparations complete. Run the command.
            await reqChannel.SendMessageAsync($"Executing `{cmdsearch.ToLower()}` on behalf of {overuser.Nickname ?? overuser.Username}:");
            await action.Invoke(overparam, gconf, reqChannel, overuser);
        }

        // Publicly available command that immediately processes the current guild, 
        private async Task CmdTest(string[] param, GuildConfiguration gconf, SocketTextChannel reqChannel, SocketGuildUser reqUser)
        {
            // Moderators only. As with config, silently drop if this check fails.
            if (!gconf.IsBotModerator(reqUser)) return;

            if (param.Length != 1) 
            {
                // Too many parameters
                // Note: Non-standard error display
                await reqChannel.SendMessageAsync(NoParameterError);
                return;
            }

            // had an option to clear roles here, but application testing revealed that running the
            // test at this point would make the updater assume that roles had not yet been cleared
            // may revisit this later...

            try
            {
                var result = await _bRoleUpdAccess(reqChannel.Guild);
                await reqChannel.SendMessageAsync(result);
            }
            catch (Exception ex)
            {
                Program.Log("Test command", ex.ToString());
                reqChannel.SendMessageAsync(InternalError).Wait();
                // TODO webhook report
            }
        }

        #region Common/helper methods
        private const string RoleInputError = ":x: Unable to determine the given role.";
        private static readonly Regex RoleMention = new Regex(@"<@?&(?<snowflake>\d+)>", RegexOptions.Compiled);

        private SocketRole FindUserInputRole(string inputStr, SocketGuild guild)
        {
            // Resembles a role mention? Strip it to the pure number
            var input = inputStr;
            var rmatch = RoleMention.Match(input);
            if (rmatch.Success) input = rmatch.Groups["snowflake"].Value;

            // Attempt to get role by ID, or null
            if (ulong.TryParse(input, out ulong rid))
            {
                return guild.GetRole(rid);
            }
            else
            {
                // Reset the search value on the off chance there's a role name that actually resembles a role ping.
                input = inputStr;
            }

            // If not already found, attempt to search role by string name
            foreach (var search in guild.Roles)
            {
                if (string.Equals(search.Name, input, StringComparison.OrdinalIgnoreCase)) return search;
            }

            return null;
        }
        #endregion
    }
}
