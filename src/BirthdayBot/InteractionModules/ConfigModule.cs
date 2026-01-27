using BirthdayBot.BackgroundServices;
using BirthdayBot.Data;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Globalization;
using System.Text;

namespace BirthdayBot.InteractionModules;

[Group("config", HelpCmdConfig)]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[CommandContextType(InteractionContextType.Guild)]
public class ConfigModule : BBModuleBase {
    public const string HelpCmdConfig = "Configure basic settings for the bot.";
    public const string HelpCmdAnnounce = "Settings regarding birthday announcements.";
    public const string HelpCmdBirthdayRole = "Set the role given to users having a birthday.";
    public const string HelpCmdCheck = "Report the bot's current configuration.";

    const string HelpPofxBlankUnset = " Leave blank to unset.";
    const string HelpOptChannel = "The corresponding channel to use.";
    const string HelpOptRole = "The corresponding role to use.";

    [Group("announce", HelpCmdAnnounce)]
    public class SubCmdsConfigAnnounce : BBModuleBase {
        private const string HelpSubCmdChannel = "Set which channel will receive announcement messages.";
        private const string HelpSubCmdMessage = "Modify the announcement message.";
        private const string HelpSubCmdPing = "Set whether to ping users mentioned in the announcement.";
        private const string HelpSubCmdTest = "Immediately attempt to send an announcement message as configured.";
        private const string HelpSubCmdTimersReset = "Resets the bot's internal timers to possibly repeat today's announcements.";

        internal const string ModalCidAnnounce = "edit-announce";
        private const string ModalComCidSingle = "msg-single";
        private const string ModalComCidMulti = "msg-multi";

        [SlashCommand("help", "Show information regarding announcement messages.")]
        public async Task CmdAnnounceHelp() {
            const string subcommands =
                $"`/config announce` - {HelpCmdAnnounce}\n" +
                $" ⤷`set-channel` - {HelpSubCmdChannel}\n" +
                $" ⤷`set-message` - {HelpSubCmdMessage}\n" +
                $" ⤷`set-ping` - {HelpSubCmdPing}\n" +
                $" ⤷`test` - {HelpSubCmdTest}\n" +
                $" ⤷`timers-reset` - {HelpSubCmdTimersReset}";
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

        [SlashCommand("set-channel", HelpSubCmdChannel + HelpPofxBlankUnset)]
        public async Task CmdSetChannel([Summary(description: HelpOptChannel)] SocketTextChannel? channel = null) {
            await DoDatabaseUpdate(Context, DbContext, s => s.AnnouncementChannel = channel?.Id);
            await RespondAsync(":white_check_mark: The announcement channel has been " +
            (channel == null ? "unset." : $"set to **{channel.Name}**."));
        }

        [SlashCommand("set-message", HelpSubCmdMessage)]
        public async Task CmdSetMessage() {
            var settings = Context.Guild.GetConfigOrNew(DbContext);

            var txtSingle = new TextInputBuilder() {
                Label = "Single - Message for one birthday",
                CustomId = ModalComCidSingle,
                Style = TextInputStyle.Paragraph,
                MaxLength = 1500,
                Required = false,
                Placeholder = BirthdayUpdater.DefaultAnnounce,
                Value = settings.AnnounceMessage ?? ""
            };
            var txtMulti = new TextInputBuilder() {
                Label = "Multi - Message for multiple birthdays",
                CustomId = ModalComCidMulti,
                Style = TextInputStyle.Paragraph,
                MaxLength = 1500,
                Required = false,
                Placeholder = BirthdayUpdater.DefaultAnnouncePl,
                Value = settings.AnnounceMessagePl ?? ""
            };

            var form = new ModalBuilder()
                .WithTitle("Edit announcement message")
                .WithCustomId(ModalCidAnnounce)
                .AddTextInput(txtSingle)
                .AddTextInput(txtMulti)
                .Build();

            await RespondWithModalAsync(form).ConfigureAwait(false);
        }

        // Must be static - responds to modal interaction
        internal static async Task CmdSetMessageResponse(SocketModal modal, SocketGuildChannel channel,
                                                  Dictionary<string, SocketMessageComponentData> data) {
            var newSingle = data[ModalComCidSingle].Value;
            var newMulti = data[ModalComCidMulti].Value;
            if (string.IsNullOrWhiteSpace(newSingle)) newSingle = null;
            if (string.IsNullOrWhiteSpace(newMulti)) newMulti = null;

            var db = BotDatabaseContext.New();
            var settings = channel.Guild.GetConfigOrNew(db);
            if (settings.IsNew) db.GuildConfigurations.Add(settings);
            settings.AnnounceMessage = newSingle;
            settings.AnnounceMessagePl = newMulti;
            await db.SaveChangesAsync();
            await modal.RespondAsync(":white_check_mark: Announcement messages have been updated.");
        }

        [SlashCommand("set-ping", HelpSubCmdPing)]
        public async Task CmdSetPing([Summary(description: "Set True to ping users, False to display them normally.")] bool option) {
            await DoDatabaseUpdate(Context, DbContext, s => s.AnnouncePing = option);
            await RespondAsync($":white_check_mark: Announcement pings are now **{(option ? "on" : "off")}**.").ConfigureAwait(false);
        }

        const string HelpOptTestPlaceholder = "A user to add into the testing announcement as a placeholder.";
        [SlashCommand("test", HelpSubCmdTest)]
        public async Task CmdTest([Summary(description: HelpOptTestPlaceholder)] SocketGuildUser placeholder,
                                  [Summary(description: HelpOptTestPlaceholder)] SocketGuildUser? placeholder2 = null,
                                  [Summary(description: HelpOptTestPlaceholder)] SocketGuildUser? placeholder3 = null,
                                  [Summary(description: HelpOptTestPlaceholder)] SocketGuildUser? placeholder4 = null,
                                  [Summary(description: HelpOptTestPlaceholder)] SocketGuildUser? placeholder5 = null) {
            // Prepare config
            var settings = Context.Guild.GetConfigOrNew(DbContext);
            if (settings.IsNew || settings.AnnouncementChannel == null) {
                await RespondAsync(":x: Unable to send a birthday message. The announcement channel is not configured.")
                    .ConfigureAwait(false);
                return;
            }
            // Check permissions
            var announcech = Context.Guild.GetTextChannel(settings.AnnouncementChannel.Value);
            if (!Context.Guild.CurrentUser.GetPermissions(announcech).SendMessages) {
                await RespondAsync(":x: Unable to send a birthday message. Insufficient permissions to send to the announcement channel.")
                    .ConfigureAwait(false);
                return;
            }

            // Send and confirm with user
            await RespondAsync($":white_check_mark: An announcement test will be sent to {announcech.Mention}.").ConfigureAwait(false);

            // this is so bad...
            // TODO: make core's FormatName a bit more generic? avoiding so much duplicate code
            SocketGuildUser?[] testUsers = [placeholder, placeholder2, placeholder3, placeholder4, placeholder5];
            List<string> names = [];
            static string FormatName(SocketGuildUser name) {
                static string escapeFormattingCharacters(string input) {
                    var result = new StringBuilder();
                    foreach (var c in input) {
                        if (c is '\\' or '_' or '~' or '*' or '@' or '`') {
                            result.Append('\\');
                        }
                        result.Append(c);
                    }
                    return result.ToString();
                }
                var username = escapeFormattingCharacters(name.GlobalName ?? name.Username!);
                if (name.Nickname != null) {
                    return $"{escapeFormattingCharacters(name.Nickname)} ({name.Username})";
                }
                return username;
            }
            foreach (var u in testUsers) {

                if (u != null) {
                    if (settings.AnnouncePing) names.Add(u.Mention);
                    else names.Add(FormatName(u));
                    Cache.Update(u);
                }
            }

            await BirthdayUpdater.AnnounceBirthdaysAsync(settings, Context.Guild, names).ConfigureAwait(false);
        }

        [SlashCommand("timers-reset", HelpSubCmdTimersReset)]
        public async Task CmdTimersReset() {
            await DbContext.UserEntries
                .Where(u => u.GuildId == Context.Guild.Id)
                .ExecuteUpdateAsync(upd => upd.SetProperty(p => p.LastProcessed, Instant.MinValue));
            await RespondAsync(":white_check_mark: Internal timers for all users have been reset." +
                " If there are birthdays, it may take several minutes before a new announcement is made.");
        }
    }

    [SlashCommand("birthday-role", HelpCmdBirthdayRole)]
    public async Task CmdSetBRole([Summary(description: HelpOptRole)] SocketRole role) {
        if (role.IsEveryone || role.IsManaged) {
            await RespondAsync(":x: This role cannot be used for this setting.", ephemeral: true);
            return;
        }
        await DoDatabaseUpdate(Context, DbContext, s => s.BirthdayRole = role.Id);
        await RespondAsync($":white_check_mark: The birthday role has been set to **{role.Name}**.").ConfigureAwait(false);
    }

    [SlashCommand("check", HelpCmdCheck)]
    public async Task CmdCheck() {
        static string YesOrNo(bool result) => result ? ":white_check_mark: Yes" : ":x: No";

        var guild = Context.Guild;
        var guildconf = guild.GetConfigOrNew(DbContext);
        if (!guildconf.IsNew) await DbContext.Entry(guildconf).Collection(t => t.UserEntries).LoadAsync();

        var resultTemplate = """
            ### Diagnostics
            Server ID: `{0}` | Bot shard: `{1}`
            Members: `{5}`
            Birthdays registered: `{2}`
            Users in cache: `{3}`
            Background cache eligibility: `{4}`
            ### Validation
            - Default server time zone
              Set to: `{6}`
              Current time: {7}
            - Birthday role
              Set to: {8}
              Exists: {9}
              Bot has permission to use: {10}
            - Announcement channel
              Set to: {11}
              Exists: {12}
              Bot has permission to use: {13}
            """;

        var results = new string[14];
        results[0] = Context.Guild.Id.ToString();
        results[1] = Shard.ShardId.ToString("00");
        results[5] = guild.MemberCount.ToString();
        results[2] = guildconf.UserEntries.Count.ToString();
        results[3] = (Cache.GetGuild(guild.Id)?.Count ?? 0).ToString();
        results[4] = Cache.FilterBackground()(DbContext, guild.Id).Count.ToString();
        results[6] = guildconf.GuildTimeZone?.Id ?? "Not set - using UTC";
        results[7] = SystemClock.Instance.GetCurrentInstant()
            .InZone(guildconf.GuildTimeZone ?? DateTimeZone.Utc)
            .ToString("yyyy-MM-dd HH:mm:ss x o<m>", DateTimeFormatInfo.InvariantInfo);

        if (guildconf.BirthdayRole.HasValue) {
            results[8] = $"<@&{guildconf.BirthdayRole.Value}>";
            var role = guild.GetRole(guildconf.BirthdayRole.Value);
            results[9] = YesOrNo(role is not null);
            results[10] = role is not null
                ? YesOrNo(guild.CurrentUser.GuildPermissions.ManageRoles && role!.Position < guild.CurrentUser.Hierarchy) : "n/a";
        } else {
            results[8] = "Not set!";
            results[9] = "n/a";
            results[10] = "n/a";
        }

        if (guildconf.AnnouncementChannel.HasValue) {
            results[11] = $"<#{guildconf.AnnouncementChannel.Value}>";
            var announcech = guild.GetChannel(guildconf.AnnouncementChannel.Value);
            results[12] = YesOrNo(announcech is not null);
            results[13] = announcech is not null
                ? YesOrNo(guild.CurrentUser.GetPermissions(announcech).SendMessages)
                : "n/a";
        }

        await RespondAsync(embed: new EmbedBuilder() {
            Author = new EmbedAuthorBuilder() { Name = "Status and config check" },
            Description = string.Format(resultTemplate, results)
        }.Build()).ConfigureAwait(false);
    }

    [SlashCommand("set-timezone", "Configure the time zone to use by default in the server." + HelpPofxBlankUnset)]
    public async Task CmdSetTimezone([Summary(description: HelpOptZone), Autocomplete<TzAutocompleteHandler>] string? zone = null) {
        const string Response = ":white_check_mark: The server's time zone has been ";

        if (zone == null) {
            await DoDatabaseUpdate(Context, DbContext, s => s.GuildTimeZone = null);
            await RespondAsync(Response + "unset.").ConfigureAwait(false);
        } else {
            DateTimeZone? parsedZone;
            try {
                parsedZone = ParseTimeZone(zone);
            } catch (FormatException e) {
                await RespondAsync(e.Message).ConfigureAwait(false);
                return;
            }

            await DoDatabaseUpdate(Context, DbContext, s => s.GuildTimeZone = parsedZone);
            await RespondAsync(Response + $"set to **{parsedZone}**.").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Helper method for updating arbitrary <see cref="GuildConfig"/> values without all the boilerplate.
    /// </summary>
    /// <param name="valueUpdater">A delegate which modifies <see cref="GuildConfig"/> properties as needed.</param>
    private static async Task DoDatabaseUpdate(SocketInteractionContext icx, BotDatabaseContext dcx, Action<GuildConfig> valueUpdater) {
        var settings = icx.Guild.GetConfigOrNew(dcx);
        if (settings.IsNew) dcx.GuildConfigurations.Add(settings);

        valueUpdater(settings);
        await dcx.SaveChangesAsync();
    }
}
