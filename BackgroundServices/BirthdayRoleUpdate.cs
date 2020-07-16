using BirthdayBot.Data;
using Discord.WebSocket;
using NodaTime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BirthdayBot.BackgroundServices
{
    /// <summary>
    /// Core automatic functionality of the bot. Manages role memberships based on birthday information,
    /// and optionally sends the announcement message to appropriate guilds.
    /// </summary>
    class BirthdayRoleUpdate : BackgroundService
    {
        public BirthdayRoleUpdate(BirthdayBot instance) : base(instance) { }

        /// <summary>
        /// Does processing on all available guilds at once.
        /// </summary>
        public override async Task OnTick()
        {
            var tasks = new List<Task>();
            foreach (var guild in BotInstance.DiscordClient.Guilds)
            {
                tasks.Add(ProcessGuildAsync(guild));
            }
            var alltasks = Task.WhenAll(tasks);

            try
            {
                await alltasks;
            }
            catch (Exception ex)
            {
                var exs = alltasks.Exception;
                if (exs != null)
                {
                    Log($"{exs.InnerExceptions.Count} exception(s) during bulk processing!");
                    // TODO needs major improvements. output to file?
                }
                else
                {
                    Log(ex.ToString());
                }
            }

            // TODO metrics for role sets, unsets, announcements - and how to do that for singles too?

            // Running GC now. Many long-lasting items have likely been discarded by now.
            GC.Collect();
        }

        /// <summary>
        /// Access to <see cref="ProcessGuildAsync(SocketGuild)"/> for the testing command.
        /// </summary>
        /// <returns>Diagnostic data in string form.</returns>
        public async Task<string> SingleProcessGuildAsync(SocketGuild guild) => (await ProcessGuildAsync(guild)).Export();

        /// <summary>
        /// Main method where actual guild processing occurs.
        /// </summary>
        private async Task<PGDiagnostic> ProcessGuildAsync(SocketGuild guild)
        {
            var diag = new PGDiagnostic();

            var gc = await GuildConfiguration.LoadAsync(guild.Id);

            // Check if role settings are correct before continuing with further processing
            SocketRole role = null;
            if (gc.RoleId.HasValue) role = guild.GetRole(gc.RoleId.Value);
            diag.RoleCheck = CheckCorrectRoleSettings(guild, role);
            if (diag.RoleCheck != null) return diag;

            // Determine who's currently having a birthday
            var users = await GuildUserConfiguration.LoadAllAsync(guild.Id);
            var tz = gc.TimeZone;
            var birthdays = GetGuildCurrentBirthdays(users, tz);
            // Note: Don't quit here if zero people are having birthdays. Roles may still need to be removed by BirthdayApply.
            diag.CurrentBirthdays = birthdays.Count.ToString();

            IEnumerable<SocketGuildUser> announcementList;
            // Update roles as appropriate
            try
            {
                var updateResult = await UpdateGuildBirthdayRoles(guild, role, birthdays);
                announcementList = updateResult.Item1;
                diag.RoleApplyResult = updateResult.Item2; // statistics
            }
            catch (Discord.Net.HttpException ex)
            {
                diag.RoleApply = ex.Message;
                return diag;
            }
            diag.RoleApply = null;

            // Birthday announcement
            var announce = gc.AnnounceMessages;
            var announceping = gc.AnnouncePing;
            SocketTextChannel channel = null;
            if (gc.AnnounceChannelId.HasValue) channel = guild.GetTextChannel(gc.AnnounceChannelId.Value);
            if (announcementList.Count() != 0)
            {
                var announceResult = await AnnounceBirthdaysAsync(announce, announceping, channel, announcementList);
                diag.Announcement = announceResult;
            }
            else
            {
                diag.Announcement = "No new role additions. Announcement not needed.";
            }

            return diag;
        }

        /// <summary>
        /// Checks if the bot may be allowed to alter roles.
        /// </summary>
        private string CheckCorrectRoleSettings(SocketGuild guild, SocketRole role)
        {
            if (role == null) return "Designated role is not set, or target role cannot be found.";

            if (!guild.CurrentUser.GuildPermissions.ManageRoles)
            {
                return "Bot does not have the 'Manage Roles' permission.";
            }

            // Check potential role order conflict
            if (role.Position >= guild.CurrentUser.Hierarchy)
            {
                return "Bot is unable to access the designated role due to permission hierarchy.";
            }

            return null;
        }

        /// <summary>
        /// Gets all known users from the given guild and returns a list including only those who are
        /// currently experiencing a birthday in the respective time zone.
        /// </summary>
        private HashSet<ulong> GetGuildCurrentBirthdays(IEnumerable<GuildUserConfiguration> guildUsers, string defaultTzStr)
        {
            var birthdayUsers = new HashSet<ulong>();

            DateTimeZone defaultTz = null;
            if (defaultTzStr != null) defaultTz = DateTimeZoneProviders.Tzdb.GetZoneOrNull(defaultTzStr);
            defaultTz ??= DateTimeZoneProviders.Tzdb.GetZoneOrNull("UTC");

            foreach (var item in guildUsers)
            {
                // Determine final time zone to use for calculation
                DateTimeZone tz = null;
                if (item.TimeZone != null)
                {
                    // Try user-provided time zone
                    tz = DateTimeZoneProviders.Tzdb.GetZoneOrNull(item.TimeZone);
                }
                tz ??= defaultTz;

                var targetMonth = item.BirthMonth;
                var targetDay = item.BirthDay;

                var checkNow = SystemClock.Instance.GetCurrentInstant().InZone(tz);
                // Special case: If birthday is February 29 and it's not a leap year, recognize it on March 1st
                if (targetMonth == 2 && targetDay == 29 && !DateTime.IsLeapYear(checkNow.Year))
                {
                    targetMonth = 3;
                    targetDay = 1;
                }
                if (targetMonth == checkNow.Month && targetDay == checkNow.Day)
                {
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
        private async Task<(IEnumerable<SocketGuildUser>, (int, int))> UpdateGuildBirthdayRoles(
            SocketGuild g, SocketRole r, HashSet<ulong> names)
        {
            // Check members currently with the role. Figure out which users to remove it from.
            var roleRemoves = new List<SocketGuildUser>();
            var roleKeeps = new HashSet<ulong>();
            foreach (var member in r.Members)
            {
                if (!names.Contains(member.Id)) roleRemoves.Add(member);
                else roleKeeps.Add(member.Id);
            }

            // TODO Can we remove during the iteration instead of after? investigate later...
            foreach (var user in roleRemoves)
            {
                await user.RemoveRoleAsync(r);
            }

            // Apply role to members not already having it. Prepare announcement list.
            var newBirthdays = new List<SocketGuildUser>();
            foreach (var target in names)
            {
                var member = g.GetUser(target);
                if (member == null) continue;
                if (roleKeeps.Contains(member.Id)) continue; // already has role - do nothing
                await member.AddRoleAsync(r);
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
        private async Task<string> AnnounceBirthdaysAsync(
            (string, string) announce, bool announcePing, SocketTextChannel c, IEnumerable<SocketGuildUser> names)
        {
            if (c == null) return "Announcement channel is not set, or previous announcement channel has been deleted.";

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
            foreach (var item in namestrings)
            {
                namedisplay.Append(", ");
                namedisplay.Append(item);
            }
            namedisplay.Remove(0, 2); // Remove initial comma and space

            try
            {
                await c.SendMessageAsync(announceMsg.Replace("%n", namedisplay.ToString()));
                return null;
            }
            catch (Discord.Net.HttpException ex)
            {
                // Directly use the resulting exception message in the operation status log
                return ex.Message;
            }
        }

        private class PGDiagnostic
        {
            const string DefaultValue = "--";

            public string RoleCheck = DefaultValue;
            public string CurrentBirthdays = DefaultValue;
            public string RoleApply = DefaultValue;
            public (int, int)? RoleApplyResult;
            public string Announcement = DefaultValue;

            public string Export()
            {
                var result = new StringBuilder();
                result.AppendLine("Test result:");
                result.AppendLine("Check role permissions: " + (RoleCheck ?? ":white_check_mark:"));
                result.AppendLine("Number of known users currently with a birthday: " + CurrentBirthdays);
                result.AppendLine("Role application process: " + (RoleApply ?? ":white_check_mark:"));
                result.Append("Role application metrics: ");
                if (RoleApplyResult.HasValue) result.AppendLine($"{RoleApplyResult.Value.Item1} additions, {RoleApplyResult.Value.Item2} removals.");
                else result.AppendLine(DefaultValue);
                result.AppendLine("Announcement: " + (Announcement ?? ":white_check_mark:"));

                return result.ToString();
            }
        }
    }
}
