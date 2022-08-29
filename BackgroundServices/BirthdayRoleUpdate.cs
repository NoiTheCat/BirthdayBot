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
        try {
            await DbConcurrentOperationsLock.WaitAsync(token);
            await ProcessBirthdaysAsync(token);
        } finally {
            try {
                DbConcurrentOperationsLock.Release();
            } catch (ObjectDisposedException) { }
        }
    }

    private async Task ProcessBirthdaysAsync(CancellationToken token) {
        // For database efficiency, fetch all database information at once before proceeding
        using var db = new BotDatabaseContext();
        var shardGuilds = ShardInstance.DiscordClient.Guilds.Select(g => (long)g.Id).ToHashSet();
        var presentGuildSettings = db.GuildConfigurations.Where(s => shardGuilds.Contains(s.GuildId));
        var guildChecks = presentGuildSettings.ToList().Select(s => Tuple.Create((ulong)s.GuildId, s));

        var exceptions = new List<Exception>();
        foreach (var (guildId, settings) in guildChecks) {
            var guild = ShardInstance.DiscordClient.GetGuild(guildId);
            if (guild == null) continue; // A guild disappeared...?

            // Check task cancellation here. Processing during a single guild is never interrupted.
            if (token.IsCancellationRequested) throw new TaskCanceledException();

            if (ShardInstance.DiscordClient.ConnectionState != ConnectionState.Connected) {
                Log("Client is not connected. Stopping early.");
                return;
            }

            try {
                // Verify that role settings and permissions are usable
                SocketRole? role = guild.GetRole((ulong)(settings.RoleId ?? 0));
                if (role == null
                    || !guild.CurrentUser.GuildPermissions.ManageRoles
                    || role.Position >= guild.CurrentUser.Hierarchy) continue;
                if (role.IsEveryone || role.IsManaged) {
                    // Invalid role was configured. Clear the setting and quit.
                    settings.RoleId = null;
                    db.Update(settings);
                    await db.SaveChangesAsync(CancellationToken.None);
                    continue;
                }

                // Load up user configs and begin processing birthdays
                await db.Entry(settings).Collection(t => t.UserEntries).LoadAsync(CancellationToken.None);
                var birthdays = GetGuildCurrentBirthdays(settings.UserEntries, settings.TimeZone);

                // Add or remove roles as appropriate
                var announcementList = await UpdateGuildBirthdayRoles(guild, role, birthdays);

                // Process birthday announcement
                if (announcementList.Any()) {
                    await AnnounceBirthdaysAsync(settings, guild, announcementList);
                }
            } catch (Exception ex) {
                // Catch all exceptions per-guild but continue processing, throw at end.
                exceptions.Add(ex);
            }
        }
        if (exceptions.Count > 1) throw new AggregateException("Unhandled exceptions occurred when processing birthdays.", exceptions);
        else if (exceptions.Count == 1) throw new Exception("An unhandled exception occurred when processing a birthday.", exceptions[0]);
    }

    /// <summary>
    /// Gets all known users from the given guild and returns a list including only those who are
    /// currently experiencing a birthday in the respective time zone.
    /// </summary>
    public static HashSet<ulong> GetGuildCurrentBirthdays(IEnumerable<UserEntry> guildUsers, string? serverDefaultTzId) {
        var birthdayUsers = new HashSet<ulong>();

        foreach (var record in guildUsers) {
            // Determine final time zone to use for calculation
            DateTimeZone tz = DateTimeZoneProviders.Tzdb
                .GetZoneOrNull(record.TimeZone ?? serverDefaultTzId ?? "UTC")!;

            var checkNow = SystemClock.Instance.GetCurrentInstant().InZone(tz);
            // Special case: If user's birthday is 29-Feb and it's currently not a leap year, check against 1-Mar
            if (!DateTime.IsLeapYear(checkNow.Year) && record.BirthMonth == 2 && record.BirthDay == 29) {
                if (checkNow.Month == 3 && checkNow.Day == 1) birthdayUsers.Add((ulong)record.UserId);
            } else if (record.BirthMonth == checkNow.Month && record.BirthDay== checkNow.Day) {
                birthdayUsers.Add((ulong)record.UserId);
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
    private static async Task<IEnumerable<SocketGuildUser>> UpdateGuildBirthdayRoles(SocketGuild g, SocketRole r, HashSet<ulong> toApply) {
        var additions = new List<SocketGuildUser>();
        try {
            var removals = new List<SocketGuildUser>(); // TODO check if roles can be removed in-place instead of building a list first
            var no_ops = new HashSet<ulong>();

            // Scan role for members no longer needing it
            foreach (var user in r.Members) {
                if (!toApply.Contains(user.Id)) removals.Add(user);
                else no_ops.Add(user.Id);
            }
            foreach (var user in removals) {
                await user.RemoveRoleAsync(r);
            }

            foreach (var target in toApply) {
                if (no_ops.Contains(target)) continue;
                var user = g.GetUser(target);
                if (user == null) continue; // User existing in database but not in guild
                await user.AddRoleAsync(r);
                additions.Add(user);
            }
        } catch (Discord.Net.HttpException ex)
            when (ex.DiscordCode is DiscordErrorCode.MissingPermissions or DiscordErrorCode.InsufficientPermissions) {
            // Encountered access and/or permission issues despite earlier checks. Quit the loop here, don't report error.
        }
        return additions;
    }

    public const string DefaultAnnounce = "Please wish a happy birthday to %n!";
    public const string DefaultAnnouncePl = "Please wish a happy birthday to our esteemed members: %n";

    /// <summary>
    /// Attempts to send an announcement message.
    /// </summary>
    internal static async Task AnnounceBirthdaysAsync(GuildConfig settings, SocketGuild g, IEnumerable<SocketGuildUser> names) {
        var c = g.GetTextChannel((ulong)(settings.ChannelAnnounceId ?? 0));
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
