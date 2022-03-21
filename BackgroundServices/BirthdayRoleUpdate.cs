using BirthdayBot.Data;
using NodaTime;
using System.Text;

namespace BirthdayBot.BackgroundServices;

/// <summary>
/// Core automatic functionality of the bot. Manages role memberships based on birthday information,
/// and optionally sends the announcement message to appropriate guilds.
/// </summary>
class BirthdayRoleUpdate : BackgroundService {
    public BirthdayRoleUpdate(ShardInstance instance) : base(instance) { }

    /// <summary>
    /// Processes birthday updates for all available guilds synchronously.
    /// </summary>
    public override async Task OnTick(int tickCount, CancellationToken token) {
        // For database efficiency, fetch all database information at once before proceeding
        // and combine it into the guild IDs that will be processed
        using var db = new BotDatabaseContext();
        var shardGuilds = ShardInstance.DiscordClient.Guilds.Select(g => (long)g.Id).ToHashSet();
        var settings = db.GuildConfigurations.Where(s => shardGuilds.Contains(s.GuildId));
        var guildChecks = shardGuilds.Join(settings, o => o, i => i.GuildId, (id, conf) => new { Key = (ulong)id, Value = conf });

        var exceptions = new List<Exception>();
        foreach (var pair in guildChecks) {
            var guild = ShardInstance.DiscordClient.GetGuild(pair.Key);
            if (guild == null) continue; // A guild disappeared...?
            var guildConf = pair.Value;

            // Check task cancellation here. Processing during a single guild is never interrupted.
            if (token.IsCancellationRequested) throw new TaskCanceledException();

            if (ShardInstance.DiscordClient.ConnectionState != Discord.ConnectionState.Connected) {
                Log("Client is not connected. Stopping early.");
                return;
            }

            try {
                // Verify that role settings and permissions are usable
                SocketRole? role = guild.GetRole((ulong)(guildConf.RoleId ?? 0));
                if (role == null || !guild.CurrentUser.GuildPermissions.ManageRoles || role.Position >= guild.CurrentUser.Hierarchy) return;

                // Load up user configs and begin processing birthdays
                await db.Entry(guildConf).Collection(t => t.UserEntries).LoadAsync(CancellationToken.None);
                var birthdays = GetGuildCurrentBirthdays(guildConf.UserEntries, guildConf.TimeZone);
                // Note: Don't quit here if zero people are having birthdays. Roles may still need to be removed by BirthdayApply.

                // Update roles as appropriate
                var announcementList = await UpdateGuildBirthdayRoles(guild, role, birthdays);

                // Birthday announcement
                var channel = guild.GetTextChannel((ulong)(guildConf.ChannelAnnounceId ?? 0));
                if (announcementList.Any()) {
                    await AnnounceBirthdaysAsync(guildConf, channel, announcementList);
                }
            } catch (Exception ex) {
                // Catch all exceptions per-guild but continue processing, throw at end.
                exceptions.Add(ex);
            }
        }
        if (exceptions.Count != 0) throw new AggregateException(exceptions);
    }

    /// <summary>
    /// Gets all known users from the given guild and returns a list including only those who are
    /// currently experiencing a birthday in the respective time zone.
    /// </summary>
#pragma warning disable 618
    [Obsolete(Database.ObsoleteReason)]
#pragma warning restore 618
    public static HashSet<ulong> GetGuildCurrentBirthdays(IEnumerable<GuildUserConfiguration> guildUsers, string? defaultTzStr) {
        var tzdb = DateTimeZoneProviders.Tzdb;
        DateTimeZone defaultTz = (defaultTzStr != null ? DateTimeZoneProviders.Tzdb.GetZoneOrNull(defaultTzStr) : null)
            ?? tzdb.GetZoneOrNull("UTC")!;

        var birthdayUsers = new HashSet<ulong>();
        foreach (var item in guildUsers) {
            // Determine final time zone to use for calculation
            DateTimeZone tz = (item.TimeZone != null ? tzdb.GetZoneOrNull(item.TimeZone) : null) ?? defaultTz;

            var targetMonth = item.BirthMonth;
            var targetDay = item.BirthDay;

            var checkNow = SystemClock.Instance.GetCurrentInstant().InZone(tz);
            // Special case: If birthday is February 29 and it's not a leap year, recognize it on March 1st
            if (targetMonth == 2 && targetDay == 29 && !DateTime.IsLeapYear(checkNow.Year)) {
                targetMonth = 3;
                targetDay = 1;
            }
            if (targetMonth == checkNow.Month && targetDay == checkNow.Day) {
                birthdayUsers.Add(item.UserId);
            }
        }
        return birthdayUsers;
    }

    /// <summary>
    /// Gets all known users from the given guild and returns a list including only those who are
    /// currently experiencing a birthday in the respective time zone.
    /// </summary>
    public static HashSet<ulong> GetGuildCurrentBirthdays(IEnumerable<UserEntry> guildUsers, string? defaultTzStr) {
        var tzdb = DateTimeZoneProviders.Tzdb;
        DateTimeZone defaultTz = (defaultTzStr != null ? DateTimeZoneProviders.Tzdb.GetZoneOrNull(defaultTzStr) : null) ?? tzdb.GetZoneOrNull("UTC")!;

        var birthdayUsers = new HashSet<ulong>();
        foreach (var item in guildUsers) {
            // Determine final time zone to use for calculation
            DateTimeZone tz = (item.TimeZone != null ? tzdb.GetZoneOrNull(item.TimeZone) : null) ?? defaultTz;

            var targetMonth = item.BirthMonth;
            var targetDay = item.BirthDay;

            var checkNow = SystemClock.Instance.GetCurrentInstant().InZone(tz);
            // Special case: If birthday is February 29 and it's not a leap year, recognize it on March 1st
            if (targetMonth == 2 && targetDay == 29 && !DateTime.IsLeapYear(checkNow.Year)) {
                targetMonth = 3;
                targetDay = 1;
            }
            if (targetMonth == checkNow.Month && targetDay == checkNow.Day) {
                birthdayUsers.Add((ulong)item.UserId);
            }
        }
        return birthdayUsers;
    }

    /// <summary>
    /// Sets the birthday role to all applicable users. Unsets it from all others who may have it.
    /// </summary>
    /// <returns>
    /// List of users who had the birthday role applied, used to announce.
    /// </returns>
    private static async Task<IEnumerable<SocketGuildUser>> UpdateGuildBirthdayRoles(SocketGuild g, SocketRole r, HashSet<ulong> names) {
        // Check members currently with the role. Figure out which users to remove it from.
        var roleRemoves = new List<SocketGuildUser>();
        var roleKeeps = new HashSet<ulong>();
        foreach (var member in r.Members) {
            if (!names.Contains(member.Id)) roleRemoves.Add(member);
            else roleKeeps.Add(member.Id);
        }

        foreach (var user in roleRemoves) {
            await user.RemoveRoleAsync(r).ConfigureAwait(false);
        }

        // Apply role to members not already having it. Prepare announcement list.
        var newBirthdays = new List<SocketGuildUser>();
        foreach (var target in names) {
            var member = g.GetUser(target);
            if (member == null) continue;
            if (roleKeeps.Contains(member.Id)) continue; // already has role - do nothing
            await member.AddRoleAsync(r).ConfigureAwait(false);
            newBirthdays.Add(member);
        }

        return newBirthdays;
    }

    public const string DefaultAnnounce = "Please wish a happy birthday to %n!";
    public const string DefaultAnnouncePl = "Please wish a happy birthday to our esteemed members: %n";

    /// <summary>
    /// Attempts to send an announcement message.
    /// </summary>
    private static async Task AnnounceBirthdaysAsync(GuildConfig settings, SocketTextChannel? c, IEnumerable<SocketGuildUser> names) {
        if (c == null) return;
        if (!c.Guild.CurrentUser.GetPermissions(c).SendMessages) return;

        string announceMsg;
        if (names.Count() == 1) announceMsg = settings.AnnounceMessage ?? settings.AnnounceMessagePl ?? DefaultAnnounce;
        else announceMsg = settings.AnnounceMessagePl ?? settings.AnnounceMessage ?? DefaultAnnouncePl;
        announceMsg = announceMsg.TrimEnd();
        if (!announceMsg.Contains("%n")) announceMsg += " %n";

        // Build sorted name list
        var namestrings = new List<string>();
        foreach (var item in names)
            namestrings.Add(Common.FormatName(item, settings.AnnouncePing));
        namestrings.Sort(StringComparer.OrdinalIgnoreCase);

        var namedisplay = new StringBuilder();
        foreach (var item in namestrings) {
            namedisplay.Append(", ");
            namedisplay.Append(item);
        }
        namedisplay.Remove(0, 2); // Remove initial comma and space

        await c.SendMessageAsync(announceMsg.Replace("%n", namedisplay.ToString())).ConfigureAwait(false);
    }
}
