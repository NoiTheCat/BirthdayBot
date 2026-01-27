using System.Globalization;
using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using NodaTime;
using NoiPublicBot.Cache;

namespace WorldTime.InteractionModules;

public class UserCommands : WTModuleBase {
    [SlashCommand("list", HelpCommand.HelpList)]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task CmdList([Summary(description: "A specific user whose time to look up.")]SocketGuildUser? user = null) {
        if (user is not null) {
            Cache.Update(UserInfo.CreateFrom(user));
            // User obtained passively. Go ahead with single listing with this data.
            await CmdListWithUserParamAsync(user).ConfigureAwait(false);
            return;
        }

        var isDeferred = false;
        var refresh = Cache.RequestGuildRefreshAsync(DbContext, Context.Guild.Id);
        if (!refresh.IsCompleted) {
            // This may take a while
            isDeferred = true;
            await DeferAsync().ConfigureAwait(false);
            await refresh.ConfigureAwait(false);
        }
        await CmdListWithoutParamAsync(isDeferred).ConfigureAwait(false);
    }

    // Guild-wide list output, called from the list command
    private async Task CmdListWithoutParamAsync(bool isDeferred) {
        const string NoResultText = ":x: Nothing to show. Register your time zones with the bot using the `/set` command.";

        // Full query replaces previous manual steps; returns timezone/user dictionary
        var sortedUsers = DbContext.UserEntries
                .Where(e => e.GuildId == Context.Guild.Id)
                .GroupBy(e => e.TimeZone)
                .Select(e => new { e.Key, Users = e.Select(x => x.UserId).ToList() })
                .ToList() // force evaluation - this becomes client-side
                .Select(o => (Area: TzPrint(o.Key), o.Users))
                .GroupBy(g => g.Area)
                .Select(e => (Area: e.Key, Subscribers: e.SelectMany(u => u.Users).Shuffle()))
                .OrderBy(x => x.Area)
                .ToList();
        var cacheusers = Cache.GetGuildCopy(Context.Guild.Id);
        if (cacheusers == null || sortedUsers.Count == 0) {
            if (isDeferred) await ModifyOriginalResponseAsync(response => response.Content = NoResultText);
            else await RespondAsync(NoResultText, ephemeral: true).ConfigureAwait(false);
            return;
        }

        const int MaxSingleLineLength = 750;
        const int MaxSingleOutputLength = 3000;

        // Build zone listings with users
        var outputlines = new List<string>();
        foreach (var (Area, Users) in sortedUsers) {
            var buffer = new StringBuilder();
            buffer.Append(Area[6..] + ": ");
            var empty = true;
            foreach (var userid in Users) {
                if (!cacheusers.TryGetValue(userid, out var userInfo)) continue;
                if (empty) empty = !empty;
                else buffer.Append(", ");
                var useradd = userInfo.FormatName();
                if (buffer.Length + useradd.Length > MaxSingleLineLength) {
                    buffer.Append("others...");
                    break;
                } else buffer.Append(useradd);
            }
            if (!empty) outputlines.Add(buffer.ToString());
        }

        // Prepare for output - send buffers out if they become too large
        outputlines.Sort();
        var useFollowup = false;
        // First output is shown as an interaction response, followed then as regular channel messages
        Task OutputAsync(Embed msg) {
            if (!useFollowup) {
                useFollowup = true;
                if (isDeferred) return ModifyOriginalResponseAsync(response => response.Embed = msg);
                else return RespondAsync(embed: msg);
            } else {
                return FollowupAsync(embed: msg);
            }
        }

        var resultout = new StringBuilder();
        foreach (var line in outputlines) {
            if (resultout.Length + line.Length > MaxSingleOutputLength) {
                await OutputAsync(new EmbedBuilder().WithDescription(resultout.ToString()).Build()).ConfigureAwait(false);
                resultout.Clear();
            }
            if (resultout.Length > 0) resultout.AppendLine(); // avoids trailing newline by adding to the previous line
            resultout.Append(line);
        }
        if (resultout.Length > 0) {
            await OutputAsync(new EmbedBuilder().WithDescription(resultout.ToString()).Build()).ConfigureAwait(false);
        }
    }

    // Single user's listing output, called from the list command
    private async Task CmdListWithUserParamAsync(SocketGuildUser target) {
        var zone = DbContext.UserEntries
            .Where(e => e.GuildId == Context.Guild.Id)
            .Where(e => e.UserId == target.Id)
            .Select(e => e.TimeZone)
            .SingleOrDefault();
        if (zone == null) {
            var isself = Context.User.Id == target.Id;
            if (isself) await RespondAsync(":x: You do not have a time zone. Set it with `tz.set`.", ephemeral: true);
            else await RespondAsync(":x: The given user does not have a time zone set.", ephemeral: true);
            return;
        }

        var resulttext = TzPrint(zone)[6..] + ": " + Cache.GetGuildCopy(Context.Guild.Id)![target.Id].FormatName();
        await RespondAsync(embed: new EmbedBuilder().WithDescription(resulttext).Build());
    }

    [SlashCommand("set", HelpCommand.HelpSet)]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task CmdSet([Summary(description: "The new time zone to set."), Autocomplete<TzAutocompleteHandler>]string zone) {
        var parsedzone = ParseTimeZone(zone);
        if (parsedzone == null) {
            await RespondAsync(ErrInvalidZone, ephemeral: true);
            return;
        }
        using var db = DbContext;
        await UpdateDbUserAsync((SocketGuildUser)Context.User, parsedzone);
        await RespondAsync($":white_check_mark: Your time zone has been set to **{parsedzone}**.",
            ephemeral: db.GuildSettings.Where(r => r.GuildId == Context.Guild.Id).SingleOrDefault()?.EphemeralConfirm ?? false)
            .ConfigureAwait(false);
    }

    [SlashCommand("remove", HelpCommand.HelpRemove)]
    [CommandContextType(InteractionContextType.Guild)]
    public async Task CmdRemove() {
        using var db = DbContext;
        var success = await DeleteDbUserAsync((SocketGuildUser)Context.User);
        if (success) await RespondAsync(":white_check_mark: Your zone has been removed.",
                ephemeral: db.GuildSettings.Where(r => r.GuildId == Context.Guild.Id).SingleOrDefault()?.EphemeralConfirm ?? false)
                .ConfigureAwait(false);
        else await RespondAsync(":x: You don't have a time zone set.",
                ephemeral: db.GuildSettings.Where(r => r.GuildId == Context.Guild.Id).SingleOrDefault()?.EphemeralConfirm ?? false)
                .ConfigureAwait(false);
    }

    private bool? ampm;
    private bool Is12Hour { get {
            if (ampm.HasValue) return (bool)ampm;
            ampm = DbContext.GuildSettings
                .Where(s => s.GuildId == Context.Guild.Id)
                .SingleOrDefault()?
                .Use12HourTime ?? false;
            return (bool)ampm;
        }
    }

    /// <summary>
    /// Returns a string displaying the current time in the given time zone.
    /// The result begins with six numbers for sorting purposes. Must be trimmed before output.
    /// </summary>
    private string TzPrint(string zone) {
        var tzdb = DateTimeZoneProviders.Tzdb;
        DateTimeZone tz = tzdb.GetZoneOrNull(zone) ?? throw new Exception("Encountered unknown time zone: " + zone);
        var now = SystemClock.Instance.GetCurrentInstant().InZone(tz);
        var sortpfx = now.ToString("MMddHH", DateTimeFormatInfo.InvariantInfo);
        string fullstr;
        if (Is12Hour) {
            var ap = now.ToString("tt", DateTimeFormatInfo.InvariantInfo).ToLowerInvariant();
            fullstr = now.ToString($"MMM' 'dd', 'hh':'mm'{ap} 'x' (UTC'o<g>')'", DateTimeFormatInfo.InvariantInfo);
        } else fullstr = now.ToString("dd'-'MMM', 'HH':'mm' 'x' (UTC'o<g>')'", DateTimeFormatInfo.InvariantInfo);
        return $"{sortpfx}● `{fullstr}`";
    }
}
