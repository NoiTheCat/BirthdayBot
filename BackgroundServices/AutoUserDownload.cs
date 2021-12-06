using BirthdayBot.Data;

namespace BirthdayBot.BackgroundServices;

/// <summary>
/// Proactively fills the user cache for guilds in which any birthday data already exists.
/// </summary>
class AutoUserDownload : BackgroundService {
    public AutoUserDownload(ShardInstance instance) : base(instance) { }

    public override async Task OnTick(int tickCount, CancellationToken token) {
        foreach (var guild in ShardInstance.DiscordClient.Guilds) {
            // Has the potential to disconnect while in the middle of processing.
            if (ShardInstance.DiscordClient.ConnectionState != ConnectionState.Connected) return;

            // Determine if there is action to be taken...
            if (!guild.HasAllMembers && await GuildUserAnyAsync(guild.Id).ConfigureAwait(false)) {
                await guild.DownloadUsersAsync().ConfigureAwait(false); // This is already on a separate thread; no need to Task.Run
                await Task.Delay(200, CancellationToken.None).ConfigureAwait(false); // Must delay, or else it seems to hang...
            }
        }
    }

    /// <summary>
    /// Determines if the user database contains any entries corresponding to this guild.
    /// </summary>
    /// <returns>True if any entries exist.</returns>
    private static async Task<bool> GuildUserAnyAsync(ulong guildId) {
        using var db = await Database.OpenConnectionAsync().ConfigureAwait(false);
        using var c = db.CreateCommand();
        c.CommandText = $"select true from {GuildUserConfiguration.BackingTable} where guild_id = @Gid limit 1";
        c.Parameters.Add("@Gid", NpgsqlTypes.NpgsqlDbType.Bigint).Value = (long)guildId;
        await c.PrepareAsync(CancellationToken.None).ConfigureAwait(false);
        using var r = await c.ExecuteReaderAsync(CancellationToken.None).ConfigureAwait(false);
        return r.Read();
    }
}
