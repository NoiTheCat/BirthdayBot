using BirthdayBot.Data;
using Discord;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BirthdayBot.BackgroundServices;

/// <summary>
/// Rather than use up unnecessary resources by auto-downloading the user list in -every-
/// server we're in, this service checks if fetching the user list is warranted for each
/// guild before proceeding to request it.
/// </summary>
class SelectiveAutoUserDownload : BackgroundService {
    private readonly HashSet<ulong> _fetchRequests = new();

    public SelectiveAutoUserDownload(ShardInstance instance) : base(instance) { }

    public override async Task OnTick(CancellationToken token) {
        IEnumerable<ulong> requests;
        lock (_fetchRequests) {
            requests = _fetchRequests.ToArray();
            _fetchRequests.Clear();
        }

        foreach (var guild in ShardInstance.DiscordClient.Guilds) {
            if (ShardInstance.DiscordClient.ConnectionState != ConnectionState.Connected) {
                Log("Client no longer connected. Stopping early.");
                return;
            }

            // Determine if there is action to be taken...
            if (guild.HasAllMembers) continue;
            if (requests.Contains(guild.Id) || await GuildUserAnyAsync(guild.Id).ConfigureAwait(false)) {
                await guild.DownloadUsersAsync().ConfigureAwait(false);
                // Must delay after a download request. Seems to hang indefinitely otherwise.
                await Task.Delay(300, CancellationToken.None).ConfigureAwait(false);
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

    public void RequestDownload(ulong guildId) {
        lock (_fetchRequests) _fetchRequests.Add(guildId);
    }
}
