using BirthdayBot.Data;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NoiPublicBot.BackgroundServices;
using NoiPublicBot.Common.UserCache;
using Serilog;
using static BirthdayBot.Localization.StringProviders;

namespace BirthdayBot.BackgroundServices;

// Core automatic functionality of the bot. Manages role memberships based on birthday information,
// and optionally sends the announcement message to appropriate guilds.
public class BirthdayUpdater : BackgroundService
{
    #region Main processing
    public override async Task OnTick(int tickCount, CancellationToken token)
    {
        // CachePreloader precedes this and prepares the cache for us. All cached users will be checked.
        var cache = Shard.LocalServices.GetRequiredService<UserCache<BotDatabaseContext>>().GetAll();

        Dictionary<ulong, GuildConfig> guildsConfigured;
        using (var db = BotDatabaseContext.New())
        {
            guildsConfigured = await db.GuildConfigurations.AsNoTracking()
                .Where(conf => cache.Keys.Contains(conf.GuildId))
                .ToDictionaryAsync(k => k.GuildId, v => v);
            Log.Verbose("{GuildCheckCount} guilds to be considered", guildsConfigured.Count);
        }

        foreach (var (gid, gconf) in guildsConfigured)
        {
            // Allow interruptions only in between processing guilds.
            if (token.IsCancellationRequested) return;

            // Some more checks before proceeding
            if (Shard.DiscordClient.ConnectionState != ConnectionState.Connected) break; // Quit immediately if disconnected
            var guild = Shard.DiscordClient.GetGuild(gid);
            if (guild is null) continue; // No longer in guild
            var doRoleManipulation = IsRoleUsable(guild, gconf);

            using var db = BotDatabaseContext.New();
            var userRows = await db.UserEntries.AsNoTracking() // manually tracked later as appropriate
                .Where(u => u.GuildId == gid)
                .Where(u => cache[gid].Keys.ToHashSet().Contains(u.UserId))
                .ToListAsync().ConfigureAwait(false);
            Log.Verbose("{GuildId}: Loaded {UserConfCount} user row(s)", gid, userRows.Count);
            if (userRows.Count == 0) continue;
            var guildTz = await db.GuildConfigurations
                .Where(g => g.GuildId == gid)
                .Select(s => s.GuildTimeZone)
                .SingleAsync().ConfigureAwait(false);

            // Join cache and database info, sort into buckets
            var items = UserInformation.Consolidate(cache[gid], userRows, guildTz);
            var (starting, ending, skipped) = GetCrossedThresholds(items);

            await HandleStartingBirthdaysAsync(guild, gconf, starting, doRoleManipulation).ConfigureAwait(false);
            await HandleEndingBirthdaysAsync(gconf, ending, doRoleManipulation).ConfigureAwait(false);
            await HandleSkippedBirthdaysAsync(gid, skipped).ConfigureAwait(false);
        }
    }

    private bool IsRoleUsable(SocketGuild guild, GuildConfig config)
    {
        if (!guild.CurrentUser.GuildPermissions.ManageRoles) return false;
        var role = guild.GetRole(config.BirthdayRole ?? 0);
        if (role is null) return false;
        if (role.Position >= guild.CurrentUser.Hierarchy) return false;
        return true;
    }

    private async Task HandleStartingBirthdaysAsync(SocketGuild guild, GuildConfig config,
                                                    IEnumerable<UserInformation> users, bool doRoleManipulation)
    {
        var rest = Shard.DiscordClient.Rest;
        using var db = BotDatabaseContext.New();
        db.AttachRange(users.Select(u => u.DbEntry));
        // The database transaction ensures announcement and role application are either fully complete before recording to database
        // or else records none, ensuring a full retry of all eligible users
        using var tx = await db.Database.BeginTransactionAsync().ConfigureAwait(false);

        var announceList = new List<string>();
        foreach (var u in users)
        {
            if (doRoleManipulation)
            {
                try
                {
                    Log.Verbose("{GuildId} starting: Add role {RoleId} to user {UserId}",
                        config.GuildId, config.BirthdayRole!.Value, u.CacheEntry.UserId);
                    await rest.AddRoleAsync(config.GuildId, u.CacheEntry.UserId, config.BirthdayRole!.Value).ConfigureAwait(false);
                }
                catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownMember)
                {
                    // We may learn here that the user is no longer in the guild
                    var cache = Shard.LocalServices.GetRequiredService<UserCache<BotDatabaseContext>>();
                    cache.Invalidate(config.GuildId, u.CacheEntry.UserId);
                    continue;
                }
            }
            if (config.AnnouncePing) announceList.Add($"<@{u.CacheEntry.UserId}>");
            else announceList.Add(u.CacheEntry.FormatName());

            u.DbEntry.LastProcessed = SystemClock.Instance.GetCurrentInstant();
            u.DbEntry.LastSeen = SystemClock.Instance.GetCurrentInstant();
        }
        await AnnounceBirthdaysAsync(config, guild, announceList, Log).ConfigureAwait(false);
        var updateCount = await db.SaveChangesAsync().ConfigureAwait(false);
        await tx.CommitAsync().ConfigureAwait(false);
        Log.Verbose("{GuildId} starting: {UpdateCount} user row(s)", config.GuildId, updateCount);
    }

    private async Task HandleEndingBirthdaysAsync(GuildConfig config, IEnumerable<UserInformation> users, bool doRoleManipulation)
    {
        var rest = Shard.DiscordClient.Rest;
        using var db = BotDatabaseContext.New();
        db.AttachRange(users.Select(u => u.DbEntry));

        foreach (var u in users)
        {
            if (doRoleManipulation)
            {
                try
                {
                    Log.Verbose("{GuildId} ending: Remove role {RoleId} on user {UserId}",
                        config.GuildId, config.BirthdayRole!.Value, u.CacheEntry.UserId);
                    await rest.RemoveRoleAsync(config.GuildId, u.CacheEntry.UserId, config.BirthdayRole!.Value).ConfigureAwait(false);
                }
                catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownMember)
                {
                    // We may learn here that the user is no longer in the guild
                    var cache = Shard.LocalServices.GetRequiredService<UserCache<BotDatabaseContext>>();
                    cache.Invalidate(config.GuildId, u.CacheEntry.UserId);
                    continue;
                }
                catch (HttpException ex)
                {
                    // TODO Check if issue has been resolved, then remove if appropriate
                    Log.Warning(ex, "Encountered HTTP code {HttpCode} on attempted role removal", Enum.GetName(ex.HttpCode));
                    break;
                }
            }
            u.DbEntry.LastProcessed = SystemClock.Instance.GetCurrentInstant();
            u.DbEntry.LastSeen = SystemClock.Instance.GetCurrentInstant();
        }
        var updateCount = await db.SaveChangesAsync().ConfigureAwait(false);
        Log.Verbose("{GuildId} ending: {UpdateCount} user row(s)", config.GuildId, updateCount);
    }

    private async Task HandleSkippedBirthdaysAsync(ulong guildId, IEnumerable<UserInformation> users)
    {
        using var db = BotDatabaseContext.New();
        db.AttachRange(users.Select(u => u.DbEntry));
        foreach (var u in users)
        {
            u.DbEntry.LastProcessed = SystemClock.Instance.GetCurrentInstant();
            u.DbEntry.LastSeen = SystemClock.Instance.GetCurrentInstant();
        }
        var updateCount = await db.SaveChangesAsync().ConfigureAwait(false);
        Log.Verbose("{GuildId} skipped: {UpdateCount} user row(s)", guildId, updateCount);
    }
    #endregion

    #region Threshold checks
    enum TimePosition { Before, During, After }

    // Combined per-user cache + database information
    private record UserInformation
    {
        private static readonly LocalDate LeapDay = new(2000, 2, 29);

        public required UserCacheItem CacheEntry { get; init; }
        public required UserEntry DbEntry { get; init; }
        public required DateTimeZone Zone { get; init; }

        public static List<UserInformation>
        Consolidate(Dictionary<ulong, UserCacheItem> users, IEnumerable<UserEntry> userEntries, DateTimeZone? guildTz)
        {
            var result = new List<UserInformation>();

            foreach (var uconf in userEntries)
            {
                if (!users.TryGetValue(uconf.UserId, out var ui)) continue;
                var z = uconf.TimeZone ?? guildTz ?? DateTimeZone.Utc;
                result.Add(new UserInformation { CacheEntry = ui, DbEntry = uconf, Zone = z });
            }
            return result;
        }

        // Determines the relative position of the current date and this birthday, without regard to year
        // TODO Must figure out start/end of year (where comparison years may become invalid - 1999, 2001)
        public TimePosition GetRelativeDayPosition(Instant currentTime, bool isLeapYear)
        {
            var now = currentTime.InZone(Zone)
                .LocalDateTime.With(ldt => new LocalDate(2000, ldt.Month, ldt.Day))
                .InZoneLeniently(Zone)
                .ToInstant();

            // Local date of user's birthday to check against
            LocalDate baseCheckDate;
            // Leap year: If birthday is 29-Feb and it's not a leap year, pretend birthday is 1-Mar.
            if ((!isLeapYear) && DbEntry.BirthDate == LeapDay) baseCheckDate = new LocalDate(2000, 3, 1);
            else baseCheckDate = new LocalDate(2000, DbEntry.BirthDate.Month, DbEntry.BirthDate.Day);

            return BirthdayUpdater.GetRelativeDayPosition(baseCheckDate, now, Zone);
        }
    }

    // Given 'base', returns whether 'check' occurs before, during, or after base's calendar date with respect to time zone
    static TimePosition GetRelativeDayPosition(Instant @base, Instant check, DateTimeZone zone)
    {
        var zonedBaseDate = @base.InZone(zone).Date;
        return GetRelativeDayPosition(zonedBaseDate, check, zone);
    }
    static TimePosition GetRelativeDayPosition(LocalDate @base, Instant check, DateTimeZone zone)
    {
        // Instant is time zone invariant, but we care about converting to a day's start time
        var baseDayStart = zone.AtStartOfDay(@base).ToInstant();
        var baseDayEnd = zone.AtStartOfDay(@base.PlusDays(1)).ToInstant();

        if (check >= baseDayEnd) return TimePosition.After;
        else if (check < baseDayStart) return TimePosition.Before;
        else return TimePosition.During;
    }

    private static (IEnumerable<UserInformation>, IEnumerable<UserInformation>, IEnumerable<UserInformation>)
    GetCrossedThresholds(IEnumerable<UserInformation> items)
    {
        var starting = new List<UserInformation>();
        var ending = new List<UserInformation>();
        var skipped = new List<UserInformation>();

        var currentTime = SystemClock.Instance.GetCurrentInstant();

        var isLeapYear = DateTime.IsLeapYear(DateTimeOffset.UtcNow.Year);
        foreach (var u in items)
        {
            // Avoiding out-of-range operations during relative position calculation...
            var uLastProc = u.DbEntry.LastProcessed;
            if (uLastProc == Instant.MinValue) uLastProc = Instant.FromUnixTimeSeconds(0);

            // Checking relative to current month/day to see when the birthday is/was (year is disregarded)
            var bdayDatePos = u.GetRelativeDayPosition(currentTime, isLeapYear);
            // And check where we're located in time compared to the last_processed value (year is used here)
            var lactDatePos = GetRelativeDayPosition(currentTime, uLastProc, u.Zone);
            if (bdayDatePos == TimePosition.After)
            { // Current day is after the birthday
                if (lactDatePos == TimePosition.Before)
                {
                    // Before -> After: Missed it. Silently update it, move on.
                    skipped.Add(u);
                }
                else if (lactDatePos == TimePosition.During)
                {
                    // During -> After: Birthday is ending.
                    ending.Add(u);
                }
                else
                {
                    // After -> After: Nothing to do.
                }
            }
            else if (bdayDatePos == TimePosition.During)
            { // Current day is the birthday
                if (lactDatePos == TimePosition.Before)
                {
                    // Before -> During: Birthday is starting.
                    starting.Add(u);
                }
                // During -> During: Do nothing.
                // After -> During: Time travel?
            }
            // Else: Current day is before the birthday.
            // Before -> any: Do nothing.
        }
        return (starting, ending, skipped);
    }
    #endregion

    // Made public for the announcement message test feature
    public static async Task AnnounceBirthdaysAsync(GuildConfig settings, SocketGuild g, IEnumerable<string> names, ILogger localLog)
    {
        if (!names.Any()) return;

        localLog.Verbose($"{nameof(AnnounceBirthdaysAsync)} for guild {{GuildId}}: Checking, will quit if unable to continue.", g.Id);
        var c = g.GetTextChannel(settings.AnnouncementChannel ?? 0);
        if (c == null) return;
        if (!c.Guild.CurrentUser.GetPermissions(c).SendMessages) return;

        string announceMsg;
        if (names.Count() == 1)
            announceMsg = settings.AnnounceMessage ?? settings.AnnounceMessagePl ?? Responses.Get(g.PreferredLocale, "defaultSingle");
        else
            announceMsg = settings.AnnounceMessagePl ?? settings.AnnounceMessage ?? Responses.Get(g.PreferredLocale, "defaultMulti");
        announceMsg = announceMsg.TrimEnd();
        if (!announceMsg.Contains("%n")) announceMsg += " %n";

        announceMsg = announceMsg
            .Replace("%n", string.Join(", ", names))
            .Replace("%e", $"@everyone");

        localLog.Verbose($"{nameof(AnnounceBirthdaysAsync)} for guild {{GuildId}}: will attempt in channel {{ChannelId}},"
            + " with {{NameCount}} entries", g.Id, c.Id, names.Count());
        await c.SendMessageAsync(announceMsg).ConfigureAwait(false);
    }
}
