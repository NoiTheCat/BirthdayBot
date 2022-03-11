using BirthdayBot.Data;
using Discord.Interactions;
using System.Text;

namespace BirthdayBot.ApplicationCommands;

[RequireContext(ContextType.Guild)]
[RequireBotModerator]
[Group("config", HelpCmdConfig)]
public class ConfigModule : BotModuleBase {
    public const string HelpCmdConfig = "Configure basic settings for the bot.";
    public const string HelpCmdAnnounce = "Settings regarding birthday announcements.";
    public const string HelpCmdBlocking = "Settings for limiting user access.";
    public const string HelpCmdRole = "Settings for roles used by this bot.";
    public const string HelpCmdCheck = "Test the bot's current configuration and show the results.";

    const string HelpPofxBlankUnset = " Leave blank to unset.";
    const string HelpOptChannel = "The corresponding channel to use.";
    const string HelpOptRole = "The corresponding role to use.";

    [Group("announce", HelpPfxModOnly + HelpCmdAnnounce)]
    public class SubCmdsConfigAnnounce : BotModuleBase {
        private const string HelpSubCmdChannel = "Set which channel will receive announcement messages.";
        private const string HelpSubCmdMessage = "Modify the announcement message.";
        private const string HelpSubCmdPing = "Set whether to ping users mentioned in the announcement.";

        [SlashCommand("help", "Show information regarding announcement messages.")]
        public async Task CmdAnnounceHelp() {
            const string subcommands =
                $"`/config announce` - {HelpCmdAnnounce}\n" +
                $" ⤷`set-channel` - {HelpSubCmdChannel}\n" +
                $" ⤷`set-message` - {HelpSubCmdMessage}\n" +
                $" ⤷`set-ping` - {HelpSubCmdPing}";
            const string whatIs =
                "As the name implies, an announcement message is the messages displayed when somebody's birthday be" +
                "arrives. If enabled, an announcment message is shown at midnight respective to the appropriate time zone, " +
                "first using the user's local time (if it is known), or else using the server's default time zone, or else " +
                "referring back to midnight in Universal Time (UTC).\n\n" +
                "To enable announcement messages, use the `set-channel` subcommand.";
            const string editMsg =
                "The `set-message` subcommand allow moderators to edit the message sent into the announcement channel.\n" +
                "Two messages may be provided: `single` sets the message that is displayed when one user has a birthday, and " +
                "`multi` sets the message used when two or more users have birthdays. If only one of the two messages " +
                "have been set, this bot will use the same message in both cases.\n\n" +
                "You may use the token `%n` in your message to specify where the name(s) should appear, otherwise the names " +
                "will appear at the very end of your custom message.";
            await RespondAsync(embed: new EmbedBuilder()
                .WithAuthor("Announcement configuration")
                .WithDescription(subcommands)
                .AddField("What is an announcement message?", whatIs)
                .AddField("Customization", editMsg)
                .Build()).ConfigureAwait(false);
        }

        [SlashCommand("set-channel", HelpPfxModOnly + HelpSubCmdChannel + HelpPofxBlankUnset)]
        public async Task CmdSetChannel([Summary(description: HelpOptRole)] SocketTextChannel? channel = null) {
            var gconf = await Context.Guild.GetConfigAsync().ConfigureAwait(false);
            gconf.AnnounceChannelId = channel?.Id;
            await gconf.UpdateAsync().ConfigureAwait(false);
            await RespondAsync(":white_check_mark: The announcement channel has been " +
            (channel == null ? "unset." : $"set to **{channel.Name}**."));
        }

        [SlashCommand("set-message", HelpPfxModOnly + HelpSubCmdMessage)]
        public async Task CmdSetMessage() {
            // TODO implement this
            var pfx = TextCommands.CommandsCommon.CommandPrefix;
            await RespondAsync(":x: Sorry, changing the announcement message via slash commands is not yet available. " +
                "Please use the corresponding text command: " +
                $"`{pfx}config message` for single, `{pfx}config message-pl` for multi.", ephemeral: true);
        }

        [SlashCommand("set-ping", HelpPfxModOnly + HelpSubCmdPing)]
        public async Task CmdSetPing([Summary(description: "Set True to ping users, False to display them normally.")]bool option) {
            var gconf = await Context.Guild.GetConfigAsync().ConfigureAwait(false);
            gconf.AnnouncePing = option;
            await gconf.UpdateAsync().ConfigureAwait(false);
            await RespondAsync($":white_check_mark: Announcement pings are now **{(option ? "on" : "off")}**.").ConfigureAwait(false);
        }
    }

    [Group("role", HelpPfxModOnly + HelpCmdRole)]
    public class SubCmdsConfigRole : BotModuleBase {
        [SlashCommand("set-birthday-role", HelpPfxModOnly + "Set the role given to users having a birthday.")]
        public async Task CmdSetBRole([Summary(description: HelpOptRole)] SocketRole role) {
            var gconf = await Context.Guild.GetConfigAsync().ConfigureAwait(false);
            gconf.RoleId = role.Id;
            await gconf.UpdateAsync().ConfigureAwait(false);
            await RespondAsync($":white_check_mark: The birthday role has been set to **{role.Name}**.").ConfigureAwait(false);
        }

        [SlashCommand("set-moderator-role", HelpPfxModOnly + "Designate a role whose members can configure the bot." + HelpPofxBlankUnset)]
        public async Task CmdSetModRole([Summary(description: HelpOptRole)]SocketRole? role = null) {
            var gconf = await Context.Guild.GetConfigAsync().ConfigureAwait(false);
            gconf.ModeratorRole = role?.Id;
            await gconf.UpdateAsync().ConfigureAwait(false);
            await RespondAsync(":white_check_mark: The moderator role has been " +
                (role == null ? "unset." : $"set to **{role.Name}**."));
        }
    }

    [Group("block", HelpCmdBlocking)]
    public class SubCmdsConfigBlocking : BotModuleBase {
        [SlashCommand("add-block", HelpPfxModOnly + "Add a user to the block list.")]
        public Task CmdAddBlock([Summary(description: "The user to block.")] SocketGuildUser user) => DoBlocklist(user.Id, true);

        [SlashCommand("remove-block", HelpPfxModOnly + "Remove a user from the block list.")]
        public Task CmdDelBlock([Summary(description: "The user to unblock.")] SocketGuildUser user) => DoBlocklist(user.Id, false);

        private async Task DoBlocklist(ulong userId, bool setting) {
            var gconf = await Context.Guild.GetConfigAsync().ConfigureAwait(false);
            bool already = setting == await gconf.IsUserBlockedAsync(userId).ConfigureAwait(false);
            if (already) {
                // TODO bug: this may sometimes be misleading when in moderated mode
                await ReplyAsync($":white_check_mark: User is already {(setting ? "" : "not ")}blocked.").ConfigureAwait(false);
            } else {
                if (setting) await gconf.BlockUserAsync(userId).ConfigureAwait(false);
                else await gconf.UnblockUserAsync(userId).ConfigureAwait(false);
                await ReplyAsync($":white_check_mark: User has been {(setting ? "" : "un")}blocked.");
            }
        }

        [SlashCommand("set-moderated", HelpPfxModOnly + "Set moderated mode on the server.")]
        public async Task CmdAddBlock([Summary(name: "enable", description: "The moderated mode setting.")] bool setting) {
            var gconf = await Context.Guild.GetConfigAsync().ConfigureAwait(false);
            bool already = setting == gconf.IsModerated;
            if (already) {
                await RespondAsync($":white_check_mark: Moderated mode is already **{(setting ? "en" : "dis")}abled**.").ConfigureAwait(false);
            } else {
                gconf.IsModerated = setting;
                await gconf.UpdateAsync().ConfigureAwait(false);
                await RespondAsync($":white_check_mark: Moderated mode is now **{(setting ? "en" : "dis")}abled**.").ConfigureAwait(false);
            }
        }
    }

    [SlashCommand("check", HelpPfxModOnly + HelpCmdCheck)]
    public async Task CmdCheck() {
        static string DoTestFor(string label, Func<bool> test) => $"{label}: { (test() ? ":white_check_mark: Yes" : ":x: No") }";
        var result = new StringBuilder();
        SocketTextChannel channel = (SocketTextChannel)Context.Channel;
        var guild = Context.Guild;
        var conf = await guild.GetConfigAsync().ConfigureAwait(false);
        var usercfgs = await guild.GetUserConfigurationsAsync().ConfigureAwait(false);

        result.AppendLine($"Server ID: `{guild.Id}` | Bot shard ID: `{Shard.ShardId:00}`");
        result.AppendLine($"Number of registered birthdays: `{ usercfgs.Count() }`");
        result.AppendLine($"Server time zone: `{ (conf?.TimeZone ?? "Not set - using UTC") }`");
        result.AppendLine();

        bool hasMembers = Common.HasMostMembersDownloaded(guild);
        result.Append(DoTestFor("Bot has obtained the user list", () => hasMembers));
        result.AppendLine($" - Has `{guild.DownloadedMemberCount}` of `{guild.MemberCount}` members.");
        int bdayCount = -1;
        result.Append(DoTestFor("Birthday processing", delegate {
            if (!hasMembers) return false;
            bdayCount = BackgroundServices.BirthdayRoleUpdate.GetGuildCurrentBirthdays(usercfgs, conf?.TimeZone).Count;
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

        SocketTextChannel? announcech = null;
        result.AppendLine(DoTestFor("(Optional) Announcement channel set with `bb.config channel`", delegate {
            if (conf == null) return false;
            announcech = guild.GetTextChannel(conf.AnnounceChannelId ?? 0);
            return announcech != null;
        }));
        string disp = announcech == null ? "announcement channel" : $"<#{announcech.Id}>";
        result.AppendLine(DoTestFor($"(Optional) Bot can send messages into { disp }", delegate {
            if (announcech == null) return false;
            return guild.CurrentUser.GetPermissions(announcech).SendMessages;
        }));

        await RespondAsync(embed: new EmbedBuilder() {
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
                em = em.AddField("Multi", prepareAnnouncePreview(conf.AnnounceMessages.Item2));
            }
            await channel.SendMessageAsync(embed: em.Build()).ConfigureAwait(false);
        }
    }

    [SlashCommand("set-timezone", HelpPfxModOnly + "Configure the time zone to use by default in the server." + HelpPofxBlankUnset)]
    public async Task CmdSetTimezone([Summary(description: HelpOptZone)] string? zone = null) {
        const string Response = ":white_check_mark: The server's time zone has been ";
        var gconf = await Context.Guild.GetConfigAsync().ConfigureAwait(false);

        if (zone == null) {
            gconf.TimeZone = null;
            await gconf.UpdateAsync().ConfigureAwait(false);
            await RespondAsync(Response + "unset.").ConfigureAwait(false);
        } else {
            string parsedZone;
            try {
                parsedZone = ParseTimeZone(zone);
            } catch (FormatException e) {
                await RespondAsync(e.Message).ConfigureAwait(false);
                return;
            }

            gconf.TimeZone = parsedZone;
            await gconf.UpdateAsync().ConfigureAwait(false);
            await RespondAsync(Response + $"set to **{zone}**.").ConfigureAwait(false);
        }
    }
}
