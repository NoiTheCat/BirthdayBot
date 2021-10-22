using BirthdayBot.Data;
using Discord.WebSocket;
using NodaTime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
        var exs = new List<Exception>();
        foreach (var guild in ShardInstance.DiscordClient.Guilds) {
            if (ShardInstance.DiscordClient.ConnectionState != Discord.ConnectionState.Connected) {
                Log("Client is not connected. Stopping early.");
                return;
            }

            // Check task cancellation here. Processing during a single guild is never interrupted.
            if (token.IsCancellationRequested) throw new TaskCanceledException();

            try {
                await ProcessGuildAsync(guild).ConfigureAwait(false);
            } catch (Exception ex) {
                // Catch all exceptions per-guild but continue processing, throw at end.
                exs.Add(ex);
            }
        }
        if (exs.Count != 0) throw new AggregateException(exs);
    }

    /// <summary>
    /// Main method where actual guild processing occurs.
    /// </summary>
    private static async Task ProcessGuildAsync(SocketGuild guild) {
        // Load guild information - stop if local cache is unavailable.
        if (!Common.HasMostMembersDownloaded(guild)) return;
        var gc = await GuildConfiguration.LoadAsync(guild.Id, true).ConfigureAwait(false);
        if (gc == null) return;

        // Check if role settings are correct before continuing with further processing
        SocketRole? role = guild.GetRole(gc.RoleId ?? 0);
        if (role == null || !guild.CurrentUser.GuildPermissions.ManageRoles || role.Position >= guild.CurrentUser.Hierarchy) return;

        // Determine who's currently having a birthday
        var users = await GuildUserConfiguration.LoadAllAsync(guild.Id).ConfigureAwait(false);
        var tz = gc.TimeZone;
        var birthdays = GetGuildCurrentBirthdays(users, tz);
        // Note: Don't quit here if zero people are having birthdays. Roles may still need to be removed by BirthdayApply.

        IEnumerable<SocketGuildUser> announcementList;
        // Update roles as appropriate
        try {
            var updateResult = await UpdateGuildBirthdayRoles(guild, role, birthdays).ConfigureAwait(false);
            announcementList = updateResult.Item1;
        } catch (Discord.Net.HttpException) {
            return;
        }

        // Birthday announcement
        var announce = gc.AnnounceMessages;
        var announceping = gc.AnnouncePing;
        SocketTextChannel channel = null;
        if (gc.AnnounceChannelId.HasValue) channel = guild.GetTextChannel(gc.AnnounceChannelId.Value);
        if (announcementList.Any()) {
            await AnnounceBirthdaysAsync(announce, announceping, channel, announcementList).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gets all known users from the given guild and returns a list including only those who are
    /// currently experiencing a birthday in the respective time zone.
    /// </summary>
    private static HashSet<ulong> GetGuildCurrentBirthdays(IEnumerable<GuildUserConfiguration> guildUsers, string defaultTzStr) {
        var birthdayUsers = new HashSet<ulong>();

        DateTimeZone defaultTz = null;
        if (defaultTzStr != null) defaultTz = DateTimeZoneProviders.Tzdb.GetZoneOrNull(defaultTzStr);
        defaultTz ??= DateTimeZoneProviders.Tzdb.GetZoneOrNull("UTC");

        foreach (var item in guildUsers) {
            // Determine final time zone to use for calculation
            DateTimeZone tz = null;
            if (item.TimeZone != null) {
                // Try user-provided time zone
                tz = DateTimeZoneProviders.Tzdb.GetZoneOrNull(item.TimeZone);
            }
            tz ??= defaultTz;

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
    /// Sets the birthday role to all applicable users. Unsets it from all others who may have it.
    /// </summary>
    /// <returns>
    /// First item: List of users who had the birthday role applied, used to announce.
    /// Second item: Counts of users who have had roles added/removed, used for operation reporting.
    /// </returns>
    private static async Task<(IEnumerable<SocketGuildUser>, (int, int))> UpdateGuildBirthdayRoles(
        SocketGuild g, SocketRole r, HashSet<ulong> names) {
        // Check members currently with the role. Figure out which users to remove it from.
        var roleRemoves = new List<SocketGuildUser>();
        var roleKeeps = new HashSet<ulong>();
        foreach (var member in r.Members) {
            if (!names.Contains(member.Id)) roleRemoves.Add(member);
            else roleKeeps.Add(member.Id);
        }

        // TODO Can we remove during the iteration instead of after? investigate later...
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

        return (newBirthdays, (newBirthdays.Count, roleRemoves.Count));
    }

    public const string DefaultAnnounce = "Please wish a happy birthday to %n!";
    public const string DefaultAnnouncePl = "Please wish a happy birthday to our esteemed members: %n";

    /// <summary>
    /// Makes (or attempts to make) an announcement in the specified channel that includes all users
    /// who have just had their birthday role added.
    /// </summary>
    /// <returns>The message to place into operation status log.</returns>
    private static async Task<string> AnnounceBirthdaysAsync(
        (string, string) announce, bool announcePing, SocketTextChannel c, IEnumerable<SocketGuildUser> names) {
        if (c == null) return "Announcement channel is not configured, or has gone missing.";

        string announceMsg;
        if (names.Count() == 1) announceMsg = announce.Item1 ?? announce.Item2 ?? DefaultAnnounce;
        else announceMsg = announce.Item2 ?? announce.Item1 ?? DefaultAnnouncePl;
        announceMsg = announceMsg.TrimEnd();
        if (!announceMsg.Contains("%n")) announceMsg += " %n";

        // Build sorted name list
        var namestrings = new List<string>();
        foreach (var item in names)
            namestrings.Add(Common.FormatName(item, announcePing));
        namestrings.Sort(StringComparer.OrdinalIgnoreCase);

        var namedisplay = new StringBuilder();
        foreach (var item in namestrings) {
            namedisplay.Append(", ");
            namedisplay.Append(item);
        }
        namedisplay.Remove(0, 2); // Remove initial comma and space

        try {
            await c.SendMessageAsync(announceMsg.Replace("%n", namedisplay.ToString())).ConfigureAwait(false);
            return null;
        } catch (Discord.Net.HttpException ex) {
            // Directly use the resulting exception message in the operation status log
            return ex.Message;
        }
    }
}
