using BirthdayBot.Data;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NoiPublicBot.BackgroundServices;
using NoiPublicBot.Common.UserCache;
using System.Net;
using System.Text;
using static BirthdayBot.Localization.StringProviders;

namespace BirthdayBot.BackgroundServices;
// Core automatic functionality of the bot. Manages role memberships based on birthday information,
// and optionally sends the announcement message to appropriate guilds.
// Ensure this runs *after* CachePreloader.
public class BirthdayUpdater : BackgroundService {
    public override async Task OnTick(int tickCount, CancellationToken token) {
        // Assumes cache has already been prepared, so do the reverse: start from cache, act only on known users
        var cache = Shard.LocalServices.GetRequiredService<UserCache<BotDatabaseContext>>();
        var cacheG = cache.GetAll();

        using var db = BotDatabaseContext.New();
        var shardGuilds = db.GuildConfigurations.AsNoTracking()
            .Where(gc => cacheG.Keys.Contains(gc.GuildId))
            .ToDictionary(k => k.GuildId, v => v);

        foreach (var (gid, users) in cacheG) {
            // Allow interruptions only in between processing guilds.
            if (token.IsCancellationRequested) return;

            // Quit immediately if disconnected.
            if (Shard.DiscordClient.ConnectionState != ConnectionState.Connected) break;

            // Some more checks before proceeding
            var guild = Shard.DiscordClient.GetGuild(gid);
            if (guild is null) continue;
            if (!shardGuilds.TryGetValue(gid, out var config)) continue; // In cache but no config - this is probably not actually possible?
            var doRoleManipulation = IsRoleUsable(guild, config);

            // All clear - set up all remaining data before doing work
            db.Entry(config).Collection(t => t.UserEntries).Load();
            var items = PrepareUserInfo(users, shardGuilds.GetValueOrDefault(gid)?.UserEntries);
            if (items is null || items.Count == 0) continue; // No eligible users in this guild

            // Given that the cache contains entries for a day prior and ahead, the same data set can be used
            // to determine new and expiring birthdays.
            var (starting, ending) = GetCrossedThresholds(db, items);
            var rest = Shard.DiscordClient.Rest;
            using (var tx = db.Database.BeginTransaction()) {
                // Transaction ensures announcement + role application is either fully complete before recording to database
                // or else records none, to ensure a full retry of all eligible users

                var announceList = new List<string>();
                foreach (var u in starting) {
                    if (doRoleManipulation) {
                        try {
                            await rest.AddRoleAsync(config.GuildId, u.User.UserId, config.BirthdayRole!.Value).ConfigureAwait(false);
                        } catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownMember) {
                            // If role manipulation is allowed, we got to see that our cache for this particular
                            // user is no longer invalid. Do something about it, carry on.
                            cache.Invalidate(config.GuildId, u.User.UserId);
                            continue;
                        }
                    }
                    if (config.AnnouncePing) announceList.Add($"<@{u.User.UserId}>");
                    else announceList.Add(u.User.FormatName());
                    UpdateThreshold(db, u);
                }
                await AnnounceBirthdaysAsync(config, guild, announceList).ConfigureAwait(false);
                db.SaveChanges();
                tx.Commit();
            }

            foreach (var u in ending) {
                if (doRoleManipulation) {
                    try {
                        await rest.RemoveRoleAsync(config.GuildId, u.User.UserId, config.BirthdayRole!.Value).ConfigureAwait(false);
                    } catch (HttpException ex) {
                        if (ex.DiscordCode == DiscordErrorCode.UnknownMember) {
                            // See equivalent exception handler above
                            cache.Invalidate(config.GuildId, u.User.UserId);
                            continue;
                        } else {
                            // Rough workaround for https://github.com/discord/discord-api-docs/issues/6549
                            // TODO Consider if a more robust workaround is needed
                            Log($"Warning: Encountered HTTP status code {Enum.GetName(typeof(HttpStatusCode), ex.HttpCode)} "
                                + "on attempted role removal");
                            break;
                        }
                    }
                }
                UpdateThreshold(db, u);
            }
            
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
            Log("Encountered a bad role configuration that has now been cleared.");
            return true;
        }
        return false;
    }

    #region Threshold checks
    enum TimePosition { Before, During, After }

    // Combined cache + database data to easily pass around
    private readonly struct Item(UserCacheItem user, UserEntry row, DateTimeZone zone) {
        private static readonly LocalDate LeapDay = new(2000, 2, 29);

        public readonly UserCacheItem User = user;
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

    private List<Item> PrepareUserInfo(Dictionary<ulong, UserCacheItem> users, ICollection<UserEntry>? userEntries) {
        if (userEntries is null) return [];
        var result = new List<Item>();

        foreach (var ci in userEntries) {
            if (!users.TryGetValue(ci.UserId, out var ui)) continue;
            var z = ci.TimeZone ?? ci.Guild.GuildTimeZone ?? DateTimeZone.Utc;
            result.Add(new Item(ui, ci, z));
        }
        return result;
    }

    private (IEnumerable<Item> starting, IEnumerable<Item> ending)
        GetCrossedThresholds(BotDatabaseContext db, IEnumerable<Item> users) {
        var starting = new List<Item>();
        var ending = new List<Item>();
        var currentTime = SystemClock.Instance.GetCurrentInstant();

        var isLeapYear = DateTime.IsLeapYear(DateTimeOffset.UtcNow.Year);
        foreach (var u in users) {
            // Avoiding out-of-range operations during relative position calculation...
            var uLastProc = u.DbRow.LastProcessed;
            if (uLastProc == Instant.MinValue) uLastProc = Instant.FromUnixTimeSeconds(0);

            // Checking relative to current month/day (ignoring year) to see when the birthday is/was
            var bdayDatePos = u.GetRelativeDayPosition(currentTime, isLeapYear);
            // And check where we're located in time compared to the last_processed value (year matters)
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
    
    private void UpdateThreshold(BotDatabaseContext db, Item entity) {
        db.Attach(entity.DbRow);
        db.Entry(entity.DbRow).State = EntityState.Modified;
        entity.DbRow.LastProcessed = SystemClock.Instance.GetCurrentInstant();
    }
    #endregion

    // Made public for the announcement message test feature
    public static async Task AnnounceBirthdaysAsync(GuildConfig settings, SocketGuild g, IEnumerable<string> names) {
        if (!names.Any()) return;

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

        var namedisplay = new StringBuilder();
        foreach (var item in names) {
            namedisplay.Append(", ");
            namedisplay.Append(item);
        }
        namedisplay.Remove(0, 2); // Remove initial comma and space

        announceMsg = announceMsg
            .Replace("%n", namedisplay.ToString())
            .Replace("%e", $"<@&{g.EveryoneRole.Id}>");

        await c.SendMessageAsync(announceMsg).ConfigureAwait(false);
    }
}
