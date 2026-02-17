using BirthdayBot.Data;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NoiPublicBot.BackgroundServices;
using NoiPublicBot.Cache;
using System.Text;

namespace BirthdayBot.BackgroundServices;
// Core automatic functionality of the bot. Manages role memberships based on birthday information,
// and optionally sends the announcement message to appropriate guilds.
// Ensure this runs *after* CachePreloader.
public class BirthdayUpdater : BackgroundService {
    public override async Task OnTick(int tickCount, CancellationToken token) {
        // Assumes cache has already been prepared, so do the reverse: start from cache, act only on known users
        var cacheG = Shard.LocalServices.GetRequiredService<LocalCache>().GetAll();

        using var db = BotDatabaseContext.New();
        var shardGuilds = db.GuildConfigurations.AsNoTracking()
            .Where(gc => cacheG.Keys.Contains(gc.GuildId))
            .ToDictionary(k => k.GuildId, v => v);

        foreach (var (gid, users) in cacheG) {
            // Allow interruptions only in between processing guilds.
            token.ThrowIfCancellationRequested();

            // Quit immediately if disconnected.
            if (Shard.DiscordClient.ConnectionState != ConnectionState.Connected) break;

            // Some more checks before proceeding
            var guild = Shard.DiscordClient.GetGuild(gid);
            if (guild is null) continue;
            if (!shardGuilds.TryGetValue(gid, out var config)) continue; // In cache but no config - this is probably not actually possible?
            // TODO Birthday role no longer strictly required for this operation. Must reconsider the following checks
            if (!guild.CurrentUser.GuildPermissions.ManageRoles) continue;
            var role = guild.GetRole(config.BirthdayRole ?? 0);
            if (role is null) continue;
            if (role.Position >= guild.CurrentUser.Hierarchy) continue;
            if (IsRoleIdInvalid(role)) continue;

            // All clear - set up all remaining data before doing work
            db.Entry(config).Collection(t => t.UserEntries).Load();
            var items = PrepareUserInfo(users, shardGuilds.GetValueOrDefault(gid)?.UserEntries);
            if (items is null || items.Count == 0) continue; // No eligible users in this guild
            // Transaction ensures if errors occur during processing, timestamp updates aren't affected
            using (var tx = db.Database.BeginTransaction()) {
                // Given that the cache contains entries for a day prior and ahead, the same data set can be used
                // to determine new and expiring birthdays.
                var (starting, ending) = GetUpdateCrossedThresholds(db, items);
                var rest = Shard.DiscordClient.Rest;
                // Note about role management: At some point, Discord.Net stopped throwing exceptions on
                // trying to add roles when role already added, or removing when role isn't set.
                // Extra checks are no longer necessary.
                var announceList = new List<string>();
                foreach (var u in starting) {
                    // TODO check if specific exception handling is necessary
                    await rest.AddRoleAsync(config.GuildId, u.User.UserId, config.BirthdayRole!.Value).ConfigureAwait(false);
                    if (config.AnnouncePing) announceList.Add($"<@{u.User.UserId}>");
                    else announceList.Add(u.User.FormatName());
                }
                foreach (var u in ending) {
                    // TODO same here - exception handling?
                    await rest.RemoveRoleAsync(config.GuildId, u.User.UserId, config.BirthdayRole!.Value).ConfigureAwait(false);
                }

                await AnnounceBirthdaysAsync(config, guild, announceList).ConfigureAwait(false);

                db.SaveChanges();
                tx.Commit();
            }
            await Task.Yield();
        }
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
            Log("Encountered a bad role configuration that has now been cleared.");
            return true;
        }
        return false;
    }

    #region Threshold checks
    enum TimePosition { Before, During, After }

    // Combined cache + database data to easily pass around
    private readonly struct Item(UserInfo user, UserEntry row, DateTimeZone zone) {
        private static readonly LocalDate LeapDay = new(2000, 2, 29);

        public readonly UserInfo User = user;
        public readonly UserEntry DbRow = row;
        public readonly DateTimeZone Zone = zone;

        // Determines the relative position of the current date and this birthday, without regard to year
        // TODO Must figure out start/end of year (where comparison years may become invalid - 1999, 2001)
        public readonly TimePosition GetRelativeDayPosition(Instant currentTime, bool isLeapYear) {
            var now = currentTime.InZone(Zone)
                .LocalDateTime.With(ldt => new LocalDate(2000, ldt.Month, ldt.Day))
                .InZoneLeniently(Zone)
                .ToInstant();
            
            // Local date of user's birthday to check against
            LocalDate baseCheckDate;
            // Leap year: If birthday is 29-Feb and it's not a leap year, pretend birthday is 1-Mar.
            if ((!isLeapYear) && DbRow.BirthDate == LeapDay) baseCheckDate = new LocalDate(2000, 3, 1);
            else baseCheckDate = new LocalDate(2000, DbRow.BirthDate.Month, DbRow.BirthDate.Day);

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

    private List<Item> PrepareUserInfo(Dictionary<ulong, UserInfo> users, ICollection<UserEntry>? userEntries) {
        if (userEntries is null) return [];
        var result = new List<Item>();

        foreach (var ci in userEntries) {
            if (!users.TryGetValue(ci.UserId, out var ui)) continue;
            var z = ci.TimeZone ?? ci.Guild.GuildTimeZone ?? DateTimeZone.Utc;
            result.Add(new Item(ui, ci, z));
        }
        return result;
    }

    private (IEnumerable<Item> starting, IEnumerable<Item> ending) GetUpdateCrossedThresholds(
                                                                        BotDatabaseContext db, IEnumerable<Item> users) {
        var starting = new List<Item>();
        var ending = new List<Item>();
        var currentTime = SystemClock.Instance.GetCurrentInstant();

        void UpdateDb(UserEntry row) {
            db.Attach(row);
            db.Entry(row).State = EntityState.Modified;
            row.LastProcessed = currentTime;
        }

        var isLeapYear = DateTime.IsLeapYear(DateTimeOffset.UtcNow.Year);
        foreach (var u in users) {
            // Avoid out of range errors in conversions... LastProcessed is currently only read here.
            var uLastProc = u.DbRow.LastProcessed;
            if (uLastProc == Instant.MinValue) uLastProc = Instant.FromUnixTimeSeconds(0);

            // Check relative to current month/day (ignoring year) to see when the birthday is/was
            var bdayDatePos = u.GetRelativeDayPosition(currentTime, isLeapYear);
            // And check where we're located in time compared to the last_processed value (year matters)
            var lactDatePos = GetRelativeDayPosition(currentTime, uLastProc, u.Zone);
            if (bdayDatePos == TimePosition.After) {
                // The birthday has passed.
                if (lactDatePos == TimePosition.Before) {
                    // Before -> After: Missed it. Update and do nothing else.
                    UpdateDb(u.DbRow);
                } else if (lactDatePos == TimePosition.During) {
                    // During -> After: Birthday is ending.
                    UpdateDb(u.DbRow);
                    ending.Add(u);
                } else {
                    // After -> After: Do nothing.
                }
            } else if (bdayDatePos == TimePosition.During) {
                // It is currently the birthday.
                if (lactDatePos == TimePosition.Before) {
                    // Before -> During: Birthday is starting.
                    UpdateDb(u.DbRow);
                    starting.Add(u);
                }
                // During -> During: Do nothing.
                // After -> During: Impossible.
            } else {
                // The birthday has not yet occurred.
                // Before -> any: Do nothing.
            }
        }
        return (starting, ending);
    }
    #endregion

    public const string DefaultAnnounce = "Please wish a happy birthday to %n!";
    public const string DefaultAnnouncePl = "Please wish a happy birthday to our esteemed members: %n";

    // Made public for the announcement message test feature
    public static async Task AnnounceBirthdaysAsync(GuildConfig settings, SocketGuild g, IEnumerable<string> names) {
        if (!names.Any()) return;

        var c = g.GetTextChannel(settings.AnnouncementChannel ?? 0);
        if (c == null) return;
        if (!c.Guild.CurrentUser.GetPermissions(c).SendMessages) return;

        string announceMsg;
        if (names.Count() == 1) announceMsg = settings.AnnounceMessage ?? settings.AnnounceMessagePl ?? DefaultAnnounce;
        else announceMsg = settings.AnnounceMessagePl ?? settings.AnnounceMessage ?? DefaultAnnouncePl;
        announceMsg = announceMsg.TrimEnd();
        if (!announceMsg.Contains("%n")) announceMsg += " %n";

        var namedisplay = new StringBuilder();
        foreach (var item in names) {
            namedisplay.Append(", ");
            namedisplay.Append(item);
        }
        namedisplay.Remove(0, 2); // Remove initial comma and space

        await c.SendMessageAsync(announceMsg.Replace("%n", namedisplay.ToString())).ConfigureAwait(false);
    }
}
