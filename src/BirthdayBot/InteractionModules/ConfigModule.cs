using System.Globalization;
using System.Text;
using BirthdayBot.BackgroundServices;
using BirthdayBot.Data;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using static BirthdayBot.Localization.CommandsEnUS.Config;

namespace BirthdayBot.InteractionModules;

[Group(Name, Description)]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[CommandContextType(InteractionContextType.Guild)]
public class ConfigModule : BBModuleBase
{
    [Group(Announce.Name, Announce.Description)]
    public class SubCmdsConfigAnnounce : BBModuleBase
    {
        [SlashCommand(Announce.Help.Name, Announce.Help.Description)]
        public async Task CmdAnnounceHelp()
        {
            // Note: This may not work for more international groups...
            // TODO Find a way to grab the slash command name directly from Discord, place them here. Will need a different type of formatting
            var subcommands = $"""
                `/{LCg("config.name")} {LCg("config.announce.name")}` - {LCg("config.announce.description")}
                ` ⤷{LCg("config.announce.set-channel.name")}` - {LCg("config.announce.set-channel.description")}
                ` ⤷{LCg("config.announce.set-message.name")}` - {LCg("config.announce.set-message.description")}
                ` ⤷{LCg("config.announce.set-ping.name")}` - {LCg("config.announce.set-ping.description")}
                ` ⤷{LCg("config.announce.test.name")}` - {LCg("config.announce.test.description")}
                ` ⤷{LCg("config.announce.timers-reset.name")}` - {LCg("config.announce.timers-reset.description")}
                """;

            await RespondAsync(embed: new EmbedBuilder()
                .WithAuthor(LRg("config.announce.help.header"))
                .WithDescription(subcommands)
                .AddField(LRg("config.announce.help.f1Header"), LRg("config.announce.help.f1Body"))
                .AddField(LRg("config.announce.help.f2Header"), LRg("config.announce.help.f2Body"))
                .Build()).ConfigureAwait(false);
        }

        [SlashCommand(Announce.SetChannel.Name, Announce.SetChannel.Description)]
        public async Task CmdSetChannel(
            [Summary(description: Announce.SetChannel.Channel.Description)]
            [ChannelTypes(ChannelType.Text, ChannelType.News)]
            IGuildChannel? channel = null)
        {
            await DbUpdateGuildAsync(s => s.AnnouncementChannel = channel?.Id).ConfigureAwait(false);
            await RespondAsync(channel != null
                ? LRg("config.announce.set-channel.successAdd", channel.Name)
                : LRg("config.announce.set-channel.successDel")).ConfigureAwait(false);
        }

        #region Announce message modal


        [SlashCommand(Announce.SetMessage.Name, Announce.SetMessage.Description)]
        public async Task CmdSetMessage()
        {
            var settings = await Context.Guild.GetConfigOrNewAsync(DbContext).ConfigureAwait(false);
            var form = AnnouncementMsgModal.Create(settings, UserLocale, GuildLocale);
            await RespondWithModalAsync(form).ConfigureAwait(false);
        }

        [ModalInteraction(AnnouncementMsgModal.CustomId, ignoreGroupNames: true)]
        public async Task SetAnnounceModalResponseAsync(AnnouncementMsgModal response)
        {
            var guildConf = await Context.Guild.GetConfigOrNewAsync(DbContext).ConfigureAwait(false);
            if (guildConf.IsNew) DbContext.GuildConfigurations.Add(guildConf);

            guildConf.AnnounceMessage = string.IsNullOrWhiteSpace(response.TextSingle) ? null : response.TextSingle;
            guildConf.AnnounceMessagePl = string.IsNullOrWhiteSpace(response.TextMulti) ? null : response.TextMulti;
            await DbContext.SaveChangesAsync().ConfigureAwait(false);

            await RespondAsync(Localization.StringProviders
                .Responses.Get(Context.Interaction.GuildLocale, "config.announce.set-message.msgSuccess"))
                .ConfigureAwait(false);
        }
        #endregion

        [SlashCommand(Announce.SetPing.Name, Announce.SetPing.Description)]
        public async Task CmdSetPing([Summary(description: Announce.SetPing.Option.Description)] bool option)
        {
            await DbUpdateGuildAsync(s => s.AnnouncePing = option).ConfigureAwait(false);
            await RespondAsync(LRg("config.announce.set-ping.success" + (option ? "On" : "Off"))).ConfigureAwait(false);
        }

        [SlashCommand(Announce.Test.Name, Announce.Test.Description)]
        public async Task CmdTest([Summary(description: Announce.Test.Placeholder.Description)] SocketGuildUser placeholder,
                                  [Summary(description: Announce.Test.Placeholder2.Description)] SocketGuildUser? placeholder2 = null,
                                  [Summary(description: Announce.Test.Placeholder3.Description)] SocketGuildUser? placeholder3 = null,
                                  [Summary(description: Announce.Test.Placeholder4.Description)] SocketGuildUser? placeholder4 = null,
                                  [Summary(description: Announce.Test.Placeholder5.Description)] SocketGuildUser? placeholder5 = null)
        {
            // Prepare config
            var settings = await Context.Guild.GetConfigOrNewAsync(DbContext).ConfigureAwait(false);
            if (settings.IsNew || settings.AnnouncementChannel == null)
            {
                await RespondAsync(LRg("config.announce.test.errNoChannel")).ConfigureAwait(false);
                return;
            }
            // Check permissions
            var announcech = Context.Guild.GetTextChannel(settings.AnnouncementChannel.Value);
            if (!Context.Guild.CurrentUser.GetPermissions(announcech).SendMessages)
            {
                await RespondAsync(LRg("config.announce.test.errPermissions")).ConfigureAwait(false);
                return;
            }

            // Send and confirm with user
            await RespondAsync(LRg("config.announce.test.invoke", announcech.Mention)).ConfigureAwait(false);

            // this is so bad...
            // TODO: make core's FormatName a bit more generic? avoiding so much duplicate code
            SocketGuildUser?[] testUsers = [placeholder, placeholder2, placeholder3, placeholder4, placeholder5];
            List<string> names = [];
            static string FormatName(SocketGuildUser name)
            {
                static string escapeFormattingCharacters(string input)
                {
                    var result = new StringBuilder();
                    foreach (var c in input)
                    {
                        if (c is '\\' or '_' or '~' or '*' or '@' or '`')
                        {
                            result.Append('\\');
                        }
                        result.Append(c);
                    }
                    return result.ToString();
                }
                var username = escapeFormattingCharacters(name.GlobalName ?? name.Username!);
                if (name.Nickname != null)
                {
                    return $"{escapeFormattingCharacters(name.Nickname)} ({name.Username})";
                }
                return username;
            }
            foreach (var u in testUsers)
            {

                if (u != null)
                {
                    if (settings.AnnouncePing) names.Add(u.Mention);
                    else names.Add(FormatName(u));
                    Cache.Update(u);
                }
            }

            await BirthdayUpdater.AnnounceBirthdaysAsync(settings, Context.Guild, names, Log).ConfigureAwait(false);
        }

        [SlashCommand(Announce.TimersReset.Name, Announce.TimersReset.Description)]
        public async Task CmdTimersReset()
        {
            await DbContext.UserEntries
                .Where(u => u.GuildId == Context.Guild.Id)
                .ExecuteUpdateAsync(upd => upd.SetProperty(p => p.LastProcessed, Instant.MinValue))
                .ConfigureAwait(false);
            Cache.Invalidate(Context.Guild.Id);
            await RespondAsync(LRg("config.announce.reset-timers")).ConfigureAwait(false);
        }
    }

    [SlashCommand(BirthdayRole.Name, BirthdayRole.Description)]
    public async Task CmdSetBRole([Summary(description: BirthdayRole.Role.Description)] SocketRole? role = null)
    {
        if (role is not null)
        {
            if (role.IsEveryone || role.IsManaged)
            {
                await RespondAsync(LRu("config.role.errBadRole"), ephemeral: true).ConfigureAwait(false);
                return;
            }
            await DbUpdateGuildAsync(s => s.BirthdayRole = role.Id).ConfigureAwait(false);
            await RespondAsync(LRg("config.role.successAdd", role.Name)).ConfigureAwait(false);
        }
        else
        {
            await DbUpdateGuildAsync(s => s.BirthdayRole = null).ConfigureAwait(false);
            await RespondAsync(LRg("config.role.successDel")).ConfigureAwait(false);
        }
    }

    [SlashCommand(Check.Name, Check.Description)]
    public async Task CmdCheck()
    {
        var strPass = LRg("config.check.pass");
        var strFail = LRg("config.check.fail");
        string TestResult(bool result) => result ? strPass : strFail;

        var guild = Context.Guild;
        var guildconf = await guild.GetConfigOrNewAsync(DbContext).ConfigureAwait(false);
        if (!guildconf.IsNew) await DbContext.Entry(guildconf).Collection(t => t.UserEntries).LoadAsync().ConfigureAwait(false);

        Log.Information("config-check invoked in {GuildId}.", Context.Guild.Id);

        var results = new string[14];
        results[0] = Context.Guild.Id.ToString();
        results[1] = Shard.ShardId.ToString("00");
        results[5] = guild.MemberCount.ToString();
        var confUserCount = await DbContext.UserEntries
            .Where(u => u.GuildId == guild.Id)
            .CountAsync().ConfigureAwait(false);
        results[2] = confUserCount.ToString();
        results[3] = (Cache.GetGuild(guild.Id)?.Count ?? 0).ToString();
        var cacheCount = await CacheFilters.Background()(Cache, DbContext, guild.Id).ConfigureAwait(false);
        results[4] = cacheCount.Count.ToString();
        results[6] = guildconf.GuildTimeZone?.Id ?? LRg("config.check.usingUtcFallback");
        results[7] = SystemClock.Instance.GetCurrentInstant()
            .InZone(guildconf.GuildTimeZone ?? DateTimeZone.Utc)
            .ToString("yyyy-MM-dd HH:mm:ss x o<m>", DateTimeFormatInfo.InvariantInfo);

        if (guildconf.BirthdayRole.HasValue)
        {
            results[8] = $"<@&{guildconf.BirthdayRole.Value}>";
            var role = guild.GetRole(guildconf.BirthdayRole.Value);
            results[9] = TestResult(role is not null);
            results[10] = role is not null
                ? TestResult(guild.CurrentUser.GuildPermissions.ManageRoles && role!.Position < guild.CurrentUser.Hierarchy)
                : LRg("config.check.notAvailable");
        }
        else
        {
            results[8] = LRg("config.check.notSetAttn");
            results[9] = LRg("config.check.notAvailable");
            results[10] = LRg("config.check.notAvailable");
        }

        if (guildconf.AnnouncementChannel.HasValue)
        {
            results[11] = $"<#{guildconf.AnnouncementChannel.Value}>";
            var announcech = guild.GetChannel(guildconf.AnnouncementChannel.Value);
            results[12] = TestResult(announcech is not null);
            results[13] = announcech is not null
                ? TestResult(guild.CurrentUser.GetPermissions(announcech).SendMessages)
                : LRg("config.check.notAvailable");
        }
        else
        {
            results[11] = LRg("config.check.notSet");
            results[12] = LRg("config.check.notAvailable");
            results[13] = LRg("config.check.notAvailable");
        }

        await RespondAsync(embed: new EmbedBuilder()
        {
            Author = new EmbedAuthorBuilder { Name = LRg("config.check.header") },
            Description = LRg("config.check.template", results)
        }.Build()).ConfigureAwait(false);
    }

    [SlashCommand(SetTimezone.Name, SetTimezone.Description)]
    public async Task CmdSetTimezone(
        [Summary(description: SetTimezone.Zone.Description)]
        [Autocomplete<TzAutocompleteHandler>]
        string? zone = null
    )
    {
        if (zone == null)
        {
            await DbUpdateGuildAsync(s => s.GuildTimeZone = null).ConfigureAwait(false);
            await RespondAsync(LRg("config.set-timezone.successDel")).ConfigureAwait(false);
        }
        else
        {
            if (!TryParseZone(zone, out var parsedZone))
            {
                await RespondAsync(LRg("errParseZone")).ConfigureAwait(false);
                return;
            }

            await DbUpdateGuildAsync(s => s.GuildTimeZone = parsedZone).ConfigureAwait(false);
            await RespondAsync(LRg("config.set-timezone.successAdd", parsedZone)).ConfigureAwait(false);
        }
    }

    [SlashCommand(PrivateConfirms.Name, PrivateConfirms.Description)]
    public async Task PrivateConfirmations([Summary(description: PrivateConfirms.Setting.Description)] bool setting)
    {
        await DbUpdateGuildAsync(s => s.EphemeralConfirm = setting).ConfigureAwait(false);
        await RespondAsync(LRg("config.private-confirms.success" + (setting ? "On" : "Off")),
            ephemeral: false).ConfigureAwait(false); // Always show this confirmation despite setting
    }

    [SlashCommand(AddOnly.Name, AddOnly.Description)]
    public async Task CmdAddOnly([Summary(description: AddOnly.Setting.Description)] bool setting)
    {
        await DbUpdateGuildAsync(s => s.AddOnly = setting).ConfigureAwait(false);
        await RespondAsync(LRg("config.add-only.success" + (setting ? "On" : "Off")),
            ephemeral: false).ConfigureAwait(false);
    }
}
