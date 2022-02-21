using BirthdayBot.Data;
using System.Text;

namespace BirthdayBot.ApplicationCommands;

internal class ModCommands : BotApplicationCommand {
    private readonly ShardManager _instance;

    private delegate Task SubCommandHandler(GuildConfiguration gconf, SocketSlashCommand arg, Dictionary<string, object> subparam);
    private static Embed HelpSubAnnounceEmbed { get; } = new EmbedBuilder()
        .AddField("Subcommands for `/announce`",
            $"`channel` - {HelpSAnChannel} {HelpPofxBlankUnset}\n" +
            $"`ping` - {HelpSAnPing}\n" +
            $"`message-single` - {HelpSAnSingle}\n" +
            $"`message-multi` - {HelpSAnMulti}")
        .AddField("Custom announcement messages",
            "The `message-single` and `message-multi` subcommands allow moderators to edit the message sent into the announcement " +
            "channel.\nThe first command `message-single` sets the message that is displayed when *one* user has a birthday. The second " +
            "command `message-multi` sets the message used when *two or more* users have birthdays. If only one of the two messages " +
            "have been set, this bot will use the same message in both cases.\n\n" +
            "For further customization, you may use the token `%n` in your message to specify where the name(s) should appear.\n")
        .Build();
    private static Embed HelpSubBlockingEmbed { get; } = new EmbedBuilder()
        .AddField("Commands", "testtesttest").Build();

    public const string HelpConfig = "Configure for essential bot settings.";
    public const string HelpConfAnnounce = "Configuration regarding announcement messages.";
    public const string HelpConfBlocking = "Configuration regarding limiting user access.";
    const string HelpPofxBlankUnset = " Leave blank to unset.";
    const string HelpOptChannelDefault = "The corresponding channel to use.";
    const string HelpOptRoleDefault = "The corresponding role to use.";
    private const string HelpSAnChannel = "Set the channel which to send birthday announcements.";
    private const string HelpSAnPing = "Set whether to ping users mentioned in the announcement.";
    private const string HelpSAnSingle = "Set the message announced when one user has a birthday.";
    private const string HelpSAnMulti = "Set the message announced when two or more users have a birthday.";

    public ModCommands(ShardManager instance) => _instance = instance;

    public override IEnumerable<ApplicationCommandProperties> GetCommands() => new ApplicationCommandProperties[] {
            new SlashCommandBuilder()
                .WithName("config")
                .WithDescription(HelpPfxModOnly + HelpConfig)
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("birthday-role")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .WithDescription(HelpPfxModOnly + "Set or modify the role given to those having a birthday.")
                    .AddOption("role", ApplicationCommandOptionType.Role, HelpOptRoleDefault, isRequired: true)
                )
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("mod-role")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .WithDescription(HelpPfxModOnly + "Allow a role to be able to use moderator commands." + HelpPofxBlankUnset)
                    .AddOption("role", ApplicationCommandOptionType.Role, HelpOptRoleDefault, isRequired: false)
                )
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("server-timezone")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .WithDescription(HelpPfxModOnly + "Set the default time zone to be used in this server." + HelpPofxBlankUnset)
                    .AddOption("zone", ApplicationCommandOptionType.String, HelpOptZone, isRequired: false)
                )
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("check")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .WithDescription(HelpPfxModOnly + "Give a configuration status report.")
                )
                .Build(),
            new SlashCommandBuilder()
                .WithName("announce")
                .WithDescription(HelpPfxModOnly + HelpConfAnnounce)
                .AddOption("help", ApplicationCommandOptionType.SubCommand,
                    HelpPfxModOnly + "Display information regarding announcement messages.")
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("channel")
                    .WithDescription(HelpPfxModOnly + HelpSAnChannel + HelpPofxBlankUnset)
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("channel", ApplicationCommandOptionType.Channel, HelpOptChannelDefault, isRequired: false)
                )
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("ping")
                    .WithDescription(HelpPfxModOnly + HelpSAnPing)
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("option", ApplicationCommandOptionType.Boolean,
                        "True to ping users or False to display names normally.", isRequired: true)
                )
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("message-single")
                    .WithDescription(HelpPfxModOnly + HelpSAnSingle)
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("message", ApplicationCommandOptionType.String, "The new message to use.")
                )
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("message-multi")
                    .WithDescription(HelpPfxModOnly + HelpSAnMulti)
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("message", ApplicationCommandOptionType.String, "The new message to use.")
                )
                .Build(),
            new SlashCommandBuilder()
                .WithName("blocking")
                .WithDescription(HelpPfxModOnly + HelpConfBlocking)
                .AddOption("help", ApplicationCommandOptionType.SubCommand,
                    HelpPfxModOnly + "Display information regarding user blocking.")
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("moderated")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .WithDescription(HelpPfxModOnly + "Set moderated mode on the server.")
                    .AddOption("enable", ApplicationCommandOptionType.Boolean,
                        "True to enable moderated mode, False to disable.", isRequired: true)
                )
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("block-user")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .WithDescription(HelpPfxModOnly + "Add a user to the blocklist.")
                    .AddOption("user", ApplicationCommandOptionType.User, "The user to add to the blocklist.", isRequired: true)
                )
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("unblock-user")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .WithDescription(HelpPfxModOnly + "Remove a user from the blocklist.")
                    .AddOption("user", ApplicationCommandOptionType.User, "The user to remove from the blocklist.", isRequired: true)
                )
            .Build()
        };
    public override CommandResponder? GetHandlerFor(string commandName) => commandName switch {
        "config" => CmdConfigDispatch,
        "announce" => CmdConfigDispatch,
        "blocking" => CmdConfigDispatch,
        _ => null,
    };

    private Task CmdConfigDispatch(ShardInstance instance, GuildConfiguration gconf, SocketSlashCommand arg) {
        if (!gconf.IsBotModerator((SocketGuildUser)arg.User)) return arg.RespondAsync(ErrNotAllowed);

        var name = arg.Data.Options.First().Name;
        if (name == "help") return HelpCommandHandler(arg, arg.CommandName);

        SubCommandHandler? subh = arg.Data.Options.First().Name switch {
            "birthday-role" => CmdConfigSubBRole,
            "mod-role" => CmdConfigSubMRole,
            "server-timezone" => CmdConfigSubTz,
            "check" => CmdConfigSubCheck,
            "channel" => CmdAnnounceSubChannel,
            "ping" => CmdAnnounceSubPing,
            "message-single" => CmdAnnounceSubMsg,
            "message-multi" => CmdAnnounceSubMsg,
            "moderated" => CmdBlockSubModerated,
            "block-user" => CmdBlockSubAddDel,
            "unblock-user" => CmdBlockSubAddDel,
            _ => null
        };

        if (subh == null) return arg.RespondAsync(ShardInstance.UnknownCommandError, ephemeral: true);

        var subparam = ((SocketSlashCommandDataOption)arg.Data.Options.First()).Options.ToDictionary(o => o.Name, o => o.Value);
        return subh(gconf, arg, subparam);
    }

    private static async Task HelpCommandHandler(SocketSlashCommand arg, string baseCommand) {
        var answer = baseCommand switch {
            "announce" => HelpSubAnnounceEmbed,
            "blocking" => HelpSubBlockingEmbed,
            _ => null
        };
        if (answer == null) {
            await arg.RespondAsync(ShardInstance.UnknownCommandError, ephemeral: true);
            return;
        }
        await arg.RespondAsync(embed: answer);
    }

    private async Task CmdConfigSubBRole(GuildConfiguration gconf, SocketSlashCommand arg, Dictionary<string, object> subparam) {
        var role = (SocketRole)subparam["role"];
        gconf.RoleId = role.Id;
        await gconf.UpdateAsync().ConfigureAwait(false);
        await arg.RespondAsync($":white_check_mark: The birthday role has been set to **{role.Name}**.").ConfigureAwait(false);
    }

    private async Task CmdConfigSubMRole(GuildConfiguration gconf, SocketSlashCommand arg, Dictionary<string, object> subparam) {
        var role = subparam.GetValueOrDefault("role") as SocketRole;
        gconf.ModeratorRole = role?.Id;
        await gconf.UpdateAsync().ConfigureAwait(false);
        await arg.RespondAsync(":white_check_mark: The moderator role has been " +
            (role == null ? "unset." : $"set to **{role.Name}**."));
    }

    private async Task CmdConfigSubTz(GuildConfiguration gconf, SocketSlashCommand arg, Dictionary<string, object> subparam) {
        const string Response = ":white_check_mark: The server's time zone has been ";
        var inputtz = subparam.GetValueOrDefault("zone") as string;

        if (inputtz == null) {
            gconf.TimeZone = null;
            await gconf.UpdateAsync().ConfigureAwait(false);
            await arg.RespondAsync(Response + "unset.").ConfigureAwait(false);
        } else {
            string zone;
            try {
                zone = ParseTimeZone(inputtz);
            } catch (FormatException e) {
                arg.RespondAsync(e.Message).Wait();
                return;
            }

            gconf.TimeZone = zone;
            await gconf.UpdateAsync().ConfigureAwait(false);
            await arg.RespondAsync(Response + $"set to **{zone}**.").ConfigureAwait(false);
        }
    }

    private async Task CmdConfigSubCheck(GuildConfiguration gconf, SocketSlashCommand arg, Dictionary<string, object> subparam) {
        static string DoTestFor(string label, Func<bool> test) => $"{label}: { (test() ? ":white_check_mark: Yes" : ":x: No") }";
        var result = new StringBuilder();
        SocketTextChannel channel = (SocketTextChannel)arg.Channel;
        var guild = channel.Guild;
        var conf = await GuildConfiguration.LoadAsync(guild.Id, true).ConfigureAwait(false);
        var userbdays = await GuildUserConfiguration.LoadAllAsync(guild.Id).ConfigureAwait(false);

        result.AppendLine($"Server ID: `{guild.Id}` | Bot shard ID: `{_instance.GetShardIdFor(guild.Id):00}`");
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
        
        await arg.RespondAsync(embed: new EmbedBuilder() {
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

    private async Task CmdAnnounceSubChannel(GuildConfiguration gconf, SocketSlashCommand arg, Dictionary<string, object> subparam) {
        var channel = subparam.GetValueOrDefault("channel") as SocketTextChannel;
        gconf.AnnounceChannelId = channel?.Id;
        await gconf.UpdateAsync();
        await arg.RespondAsync(":white_check_mark: The announcement channel has been " +
            (channel == null ? "unset." : $"set to **{channel.Name}**."));
    }

    private async Task CmdAnnounceSubPing(GuildConfiguration gconf, SocketSlashCommand arg, Dictionary<string, object> subparam) {
        var setting = (bool)subparam["option"];
        gconf.AnnouncePing = setting;
        await gconf.UpdateAsync().ConfigureAwait(false);
        await arg.RespondAsync(":white_check_mark: Announcement pings are now " + (setting ? "**on**." : "**off**.")).ConfigureAwait(false);
    }

    private async Task CmdAnnounceSubMsg(GuildConfiguration gconf, SocketSlashCommand arg, Dictionary<string, object> subparam) {
        // Handles "message-single" and "message-multi" subcommands
        await arg.RespondAsync("unimplemented");
    }

    private async Task CmdBlockSubModerated(GuildConfiguration gconf, SocketSlashCommand arg, Dictionary<string, object> subparam) {
        var setting = (bool)subparam["option"];
        gconf.IsModerated = setting;
        await gconf.UpdateAsync().ConfigureAwait(false);
        await arg.RespondAsync(":white_check_mark: Moderated mode is now " + (setting ? "**on**." : "**off**.")).ConfigureAwait(false);
    }

    private async Task CmdBlockSubAddDel(GuildConfiguration gconf, SocketSlashCommand arg, Dictionary<string, object> subparam) {
        // Handles "block-user" and "unblock-user" subcommands
        await arg.RespondAsync("unimplemented");
    }
}
