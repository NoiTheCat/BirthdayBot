using BirthdayBot.Data;

namespace BirthdayBot.BackgroundServices;
/// <summary>
/// Proactively fills the user cache for guilds in which any birthday data already exists.
/// </summary>
class AutoUserDownload : BackgroundService {
    public AutoUserDownload(ShardInstance instance) : base(instance) { }

    public override async Task OnTick(int tickCount, CancellationToken token) {
        using var db = new BotDatabaseContext();

        // Take action if a guild's cache is incomplete...
        var incompleteCaches = ShardInstance.DiscordClient.Guilds.Where(g => !g.HasAllMembers).Select(g => (long)g.Id).ToHashSet();
        // ...and if the guild contains any user data
        var mustFetch = db.UserEntries.Where(e => incompleteCaches.Contains(e.GuildId)).Select(e => e.GuildId).Distinct();

        var processed = 0;
        foreach (var item in mustFetch) {
            // May cause a disconnect in certain situations. Cancel all further attempts until the next pass if it happens.
            if (ShardInstance.DiscordClient.ConnectionState != ConnectionState.Connected) break;

            var guild = ShardInstance.DiscordClient.GetGuild((ulong)item);
            if (guild == null) continue; // A guild disappeared...?
            await guild.DownloadUsersAsync().ConfigureAwait(false); // We're already on a seperate thread, no need to use Task.Run
            await Task.Delay(200, CancellationToken.None).ConfigureAwait(false); // Must delay, or else it seems to hang...
            processed++;
        }

        if (processed > 100) Log($"Explicit user list request processed for {processed} guild(s).");
    }
}
