using BirthdayBot.Data;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NoiPublicBot.BackgroundServices;
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
            .Where(gc => cacheG.ContainsKey(gc.GuildId))
            .ToDictionary(k => k.GuildId, v => v);

        foreach (var (gid, users) in cacheG) {
            // Allow interruptions only in between processing guilds.
            token.ThrowIfCancellationRequested();

            // Quit immediately if disconnected.
            if (Shard.DiscordClient.ConnectionState != ConnectionState.Connected) break;

            // Some more checks before proceeding
            var guild = Shard.DiscordClient.GetGuild(gid);
            if (guild is null) continue;
            if (!shardGuilds.TryGetValue(gid, out var config)) continue; // In cache but no config - probably not actually possible?

            // TODO Birthday role no longer strictly required. Must reconsider these restrictions
            if (!guild.CurrentUser.GuildPermissions.ManageRoles) continue;
            var role = guild.GetRole(config.BirthdayRole ?? 0);
            if (role is null) continue;
            if (role.Position >= guild.CurrentUser.Hierarchy) continue;
            if (IsRoleIdInvalid(role)) continue;

            // All clear - do the thing
            db.Entry(config).Collection(t => t.UserEntries).Load();
            var birthdays = GetGuildCurrentBirthdays(config.UserEntries, config.GuildTimeZone);
            // Transaction ensures if errors occur during processing, timestamp updates aren't affected
            using (var tx = db.Database.BeginTransaction()) {
                var birthdayUp = await GetNewBirthdaysAsync(db, birthdays);
                // As the cache contains entries for users from a day prior,
                // processing for birthdays that are ending can be done with this same set of data.
                var birthdayDown = await GetExpiringBirthdaysAsync(db, config.UserEntries.Except(birthdays));

                await ProcessNewBirthdays(config, role, birthdayUp);
                await ProcessExpiringBirthdays(config, role, birthdayDown);
                tx.Commit();
            }
            await Task.Yield();
        }
    }

    private bool IsRoleIdInvalid(SocketRole role) {
        // This remains here for exceptional circumstances, back when the configured role was unchecked during input.
        // May be removed in the future.
        using var db = BotDatabaseContext.New(); // a new, extremely short-lived db context
        if (role.IsEveryone || role.IsManaged) {
            var conf = db.GuildConfigurations.Where(g => g.GuildId == role.Guild.Id).SingleOrDefault();
            if (conf == null) return false; // ????
            conf.BirthdayRole = null;
            db.SaveChanges();
            Log("Encountered a bad role configuration that has now been cleared.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Gets all known users from the given guild and returns a list including only those who are
    /// currently experiencing a birthday in the appropriate time zone.
    /// </summary>
    public static List<UserEntry> GetGuildCurrentBirthdays(IEnumerable<UserEntry> guildUsers, DateTimeZone? serverDefaultTz) {
        var result = new List<UserEntry>();
        foreach (var record in guildUsers) {
            // Determine final time zone to use for calculation
            DateTimeZone tz = record.TimeZone ?? serverDefaultTz ?? DateTimeZone.Utc;

            var checkNow = SystemClock.Instance.GetCurrentInstant().InZone(tz).Date;
            var checkdate = new DateOnly(2000, checkNow.Month, checkNow.Day);
            // Special case: If user's birthday is 29-Feb and it's currently not a leap year, check against 1-Mar
            if (!DateTime.IsLeapYear(checkNow.Year) && record.BirthDate == new DateOnly(2000, 2, 29)) {
                if (checkNow.Month == 3 && checkNow.Day == 1) birthdayUsers.Add(record.UserId);
            } else if (record.BirthDate == checkdate) {
                result.Add(record);
            }
        }
        return result;
    }

    private async Task<List<UserEntry>> GetNewBirthdaysAsync(BotDatabaseContext db, IEnumerable<UserEntry> users) {
        // actually maybe this could process expiring before new to not complicate the comparison logic
        throw new NotImplementedException();
    }

    private async Task ProcessNewBirthdays(GuildConfig config, SocketRole role, IEnumerable<UserEntry> users) {
        throw new NotImplementedException();
    }

    private async Task<List<UserEntry>> GetExpiringBirthdaysAsync(BotDatabaseContext db, IEnumerable<UserEntry> users) {
        throw new NotImplementedException();
    }

    private async Task ProcessExpringBirthdays(GuildConfig config, SocketRole role, IEnumerable<UserEntry> users) {
        throw new NotImplementedException();
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
            var removals = new List<SocketGuildUser>();
            var no_ops = new HashSet<ulong>();

            // Scan role for members no longer needing it
            foreach (var user in r.Members) {
                if (!toApply.Contains(user.Id)) removals.Add(user);
                else no_ops.Add(user.Id);
            }
            foreach (var user in removals) {
                await user.RemoveRoleAsync(r).ConfigureAwait(false);
            }

            foreach (var target in toApply) {
                if (no_ops.Contains(target)) continue;
                var user = g.GetUser(target);
                if (user == null) continue; // User existing in database but not in guild
                await user.AddRoleAsync(r).ConfigureAwait(false);
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
    public static async Task AnnounceBirthdaysAsync(GuildConfig settings, SocketGuild g, IEnumerable<SocketGuildUser> names) {
        var c = g.GetTextChannel(settings.AnnouncementChannel ?? 0);
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
