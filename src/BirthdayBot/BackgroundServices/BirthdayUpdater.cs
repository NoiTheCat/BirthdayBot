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
public class BirthdayUpdater : BackgroundService {
    public override async Task OnTick(int tickCount, CancellationToken token) {
        // CachePreloader has run before this service, should already contain the users we're interested in
        var cache = Shard.LocalServices.GetRequiredService<UserCache<BotDatabaseContext>>();
        var guildsCached = cache.GetAll();

        using var db = BotDatabaseContext.New();
        var guildConfigs = db.GuildConfigurations.AsNoTracking()
            .Where(gc => guildsCached.Keys.Contains(gc.GuildId))
            .ToDictionary(k => k.GuildId, v => v);

        Log.Verbose("{GuildCheckCount} guilds to be considered", guildConfigs.Count);
        foreach (var (gid, users) in guildsCached) {
            // Allow interruptions only in between processing guilds.
            if (token.IsCancellationRequested) return;

            // Some more checks before proceeding
            if (Shard.DiscordClient.ConnectionState != ConnectionState.Connected) break; // Quit immediately if disconnected
            var guild = Shard.DiscordClient.GetGuild(gid);
            if (guild is null) continue; // No longer in guild
            if (!guildConfigs.TryGetValue(gid, out var config)) continue; // Guild has no configuration
            var doRoleManipulation = IsRoleUsable(guild, config);

            // All good - consolidate information now
            db.Entry(config).Collection(t => t.UserEntries).Load();
            var items = UserInformation.Consolidate(users, guildConfigs.GetValueOrDefault(gid)?.UserEntries);
            if (items is null || items.Count == 0) continue; // No eligible users in this guild

            // Cache is assumed to contain entries for a 72 hour period centered on the current time, 
            // so all cached users will be checked to determine new and expiring birthdays.
            var (starting, ending) = GetCrossedThresholds(db, items);
            var rest = Shard.DiscordClient.Rest;
            // Transaction ensures announcement and role application are either fully complete before recording to database
            // or else records none, ensuring a full retry of all eligible users
            using (var tx = db.Database.BeginTransaction()) {
                var announceList = new List<string>();
                foreach (var u in starting) {
                    if (doRoleManipulation) {
                        try {
                            Log.Verbose("Adding role. Guild {GuildId}, User {UserId}, Role {RoleId}",
                                config.GuildId, u.CacheEntry.UserId, config.BirthdayRole!.Value);
                            await rest.AddRoleAsync(config.GuildId, u.CacheEntry.UserId, config.BirthdayRole!.Value).ConfigureAwait(false);
                        } catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownMember) {
                            // If role manipulation is allowed, we may see this user's cached data as no longer valid
                            cache.Invalidate(config.GuildId, u.CacheEntry.UserId);
                            continue;
                        }
                    }
                    if (config.AnnouncePing) announceList.Add($"<@{u.CacheEntry.UserId}>");
                    else announceList.Add(u.CacheEntry.FormatName());
                    UpdateThreshold(db, u);
                }
                await AnnounceBirthdaysAsync(config, guild, announceList, Log).ConfigureAwait(false);
                var upd1 = db.SaveChanges();
                tx.Commit();
                Log.Verbose("Transaction 1 updated {GuildId}: {UpdateCount} user row(s)", config.GuildId, upd1);
            }

            foreach (var u in ending) {
                if (doRoleManipulation) {
                    try {
                        Log.Verbose("Removing role. Guild {GuildId}, User {UserId}, Role {RoleId}",
                                config.GuildId, u.CacheEntry.UserId, config.BirthdayRole!.Value);
                        await rest.RemoveRoleAsync(config.GuildId, u.CacheEntry.UserId, config.BirthdayRole!.Value).ConfigureAwait(false);
                    } catch (HttpException ex) {
                        if (ex.DiscordCode == DiscordErrorCode.UnknownMember) {
                            // See equivalent exception handler above
                            cache.Invalidate(config.GuildId, u.CacheEntry.UserId);
                            continue;
                        } else {
                            // TODO Check if issue has been resolved, then remove if appropriate
                            Log.Warning(ex, "Encountered HTTP code {HttpCode} on attempted role removal", Enum.GetName(ex.HttpCode));
                            break;
                        }
                    }
                }
                UpdateThreshold(db, u);
            }
            var upd2 = await db.SaveChangesAsync().ConfigureAwait(false);
            Log.Verbose("Transaction 2 updated {GuildId}: {UpdateCount} user row(s)", config.GuildId, upd2);
            await Task.Yield();
        }
    }

    private bool IsRoleUsable(SocketGuild guild, GuildConfig config) {
        if (!guild.CurrentUser.GuildPermissions.ManageRoles) return false;
        var role = guild.GetRole(config.BirthdayRole ?? 0);
        if (role is null) return false;
        if (role.Position >= guild.CurrentUser.Hierarchy) return false;
        if (IsRoleIdInvalid(role)) return false;
        return true;
    }

    private bool IsRoleIdInvalid(SocketRole role) {
        // This remains here for exceptional circumstances, back when the configured role was unchecked during input.
        // May be removed in the future.
        if (role.IsEveryone || role.IsManaged) {
            using var db = BotDatabaseContext.New(); // a new, extremely short-lived db context
            var conf = db.GuildConfigurations.Where(g => g.GuildId == role.Guild.Id).SingleOrDefault();
            if (conf == null) return true; // ????
            conf.BirthdayRole = null;
            db.SaveChanges();
            Log.Warning($"{nameof(IsRoleIdInvalid)} triggered in guild {{GuildId}}.", conf.GuildId);
            return true;
        }
        return false;
    }

    #region Threshold checks
    enum TimePosition { Before, During, After }

    // Combined per-user cache + database information
    private record UserInformation {
        private static readonly LocalDate LeapDay = new(2000, 2, 29);

        public required UserCacheItem CacheEntry { get; init; }
        public required UserEntry DbEntry { get; init; }
        public required DateTimeZone Zone { get; init; }

        public static List<UserInformation> Consolidate(Dictionary<ulong, UserCacheItem> users, ICollection<UserEntry>? userEntries) {
            if (userEntries is null) return [];
            var result = new List<UserInformation>();

            foreach (var uconf in userEntries) {
                if (!users.TryGetValue(uconf.UserId, out var ui)) continue;
                var z = uconf.TimeZone ?? uconf.Guild.GuildTimeZone ?? DateTimeZone.Utc;
                result.Add(new UserInformation { CacheEntry = ui, DbEntry = uconf, Zone = z });
            }
            return result;
        }

        // Determines the relative position of the current date and this birthday, without regard to year
        // TODO Must figure out start/end of year (where comparison years may become invalid - 1999, 2001)
        public TimePosition GetRelativeDayPosition(Instant currentTime, bool isLeapYear) {
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
    static TimePosition GetRelativeDayPosition(Instant @base, Instant check, DateTimeZone zone) {
        var zonedBaseDate = @base.InZone(zone).Date;
        return GetRelativeDayPosition(zonedBaseDate, check, zone);
    }
    static TimePosition GetRelativeDayPosition(LocalDate @base, Instant check, DateTimeZone zone) {
        // Instant is time zone invariant, but we care about converting to a day's start time
        var baseDayStart = zone.AtStartOfDay(@base).ToInstant();
        var baseDayEnd = zone.AtStartOfDay(@base.PlusDays(1)).ToInstant();

        if (check >= baseDayEnd) return TimePosition.After;
        else if (check < baseDayStart) return TimePosition.Before;
        else return TimePosition.During;
    }

    private static (IEnumerable<UserInformation> starting, IEnumerable<UserInformation> ending)
        GetCrossedThresholds(BotDatabaseContext db, IEnumerable<UserInformation> users)
    {
        var starting = new List<UserInformation>();
        var ending = new List<UserInformation>();
        var currentTime = SystemClock.Instance.GetCurrentInstant();

        var isLeapYear = DateTime.IsLeapYear(DateTimeOffset.UtcNow.Year);
        foreach (var u in users) {
            // Avoiding out-of-range operations during relative position calculation...
            var uLastProc = u.DbEntry.LastProcessed;
            if (uLastProc == Instant.MinValue) uLastProc = Instant.FromUnixTimeSeconds(0);

            // Checking relative to current month/day to see when the birthday is/was (year is disregarded)
            var bdayDatePos = u.GetRelativeDayPosition(currentTime, isLeapYear);
            // And check where we're located in time compared to the last_processed value (year is used here)
            var lactDatePos = GetRelativeDayPosition(currentTime, uLastProc, u.Zone);
            if (bdayDatePos == TimePosition.After) { // Current day is after the birthday
                if (lactDatePos == TimePosition.Before) {
                    // Before -> After: Missed it. Silently update it, move on.
                    UpdateThreshold(db, u);
                } else if (lactDatePos == TimePosition.During) {
                    // During -> After: Birthday is ending.
                    ending.Add(u);
                } else {
                    // After -> After: Nothing to do.
                }
            } else if (bdayDatePos == TimePosition.During) { // Current day is the birthday
                if (lactDatePos == TimePosition.Before) {
                    // Before -> During: Birthday is starting.
                    starting.Add(u);
                }
                // During -> During: Do nothing.
                // After -> During: Not possible.
            }
            // Else: Current day is before the birthday.
            // Before -> any: Do nothing.
        }
        return (starting, ending);
    }
    
    private static void UpdateThreshold(BotDatabaseContext db, UserInformation entity) {
        db.Attach(entity.DbEntry);
        db.Entry(entity.DbEntry).State = EntityState.Modified;
        entity.DbEntry.LastProcessed = SystemClock.Instance.GetCurrentInstant();
    }
    #endregion

    // Made public for the announcement message test feature
    public static async Task AnnounceBirthdaysAsync(GuildConfig settings, SocketGuild g, IEnumerable<string> names, ILogger localLog) {
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
