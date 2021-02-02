using BirthdayBot.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BirthdayBot.BackgroundServices
{
    /// <summary>
    /// A type of workaround to the issue of user information not being cached for guilds that
    /// have user information existing in the bot's database. This service runs frequently and
    /// determines guilds in which user data must be downloaded, and proceeds to request it.
    /// </summary>
    class SelectiveAutoUserDownload : BackgroundService
    {
        private static readonly SemaphoreSlim _updateLock = new SemaphoreSlim(2);

        private readonly HashSet<ulong> _fetchRequests = new HashSet<ulong>();

        public SelectiveAutoUserDownload(ShardInstance instance) : base(instance) { }

        public override async Task OnTick(CancellationToken token)
        {
            IEnumerable<ulong> requests;
            lock (_fetchRequests)
            {
                requests = _fetchRequests.ToArray();
                _fetchRequests.Clear();
            }

            foreach (var guild in ShardInstance.DiscordClient.Guilds)
            {
                if (ShardInstance.DiscordClient.ConnectionState != Discord.ConnectionState.Connected)
                {
                    Log("Client no longer connected. Stopping early.");
                    return;
                }

                // Determine if there is action to be taken...
                if (guild.HasAllMembers) continue;
                if (requests.Contains(guild.Id) || await GuildUserAnyAsync(guild.Id, token).ConfigureAwait(false))
                {
                    await guild.DownloadUsersAsync().ConfigureAwait(false);
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Determines if the user database contains any entries corresponding to this guild.
        /// </summary>
        /// <returns>True if any entries exist.</returns>
        private async Task<bool> GuildUserAnyAsync(ulong guildId, CancellationToken token)
        {
            try
            {
                await _updateLock.WaitAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
            {
                // Calling thread does not expect the exception that SemaphoreSlim throws...
                throw new TaskCanceledException();
            }
            try
            {
                using var db = await Database.OpenConnectionAsync().ConfigureAwait(false);
                using var c = db.CreateCommand();
                c.CommandText = $"select count(*) from {GuildUserConfiguration.BackingTable} where guild_id = @Gid";
                c.Parameters.Add("@Gid", NpgsqlTypes.NpgsqlDbType.Bigint).Value = (long)guildId;
                await c.PrepareAsync().ConfigureAwait(false);
                var r = (long)await c.ExecuteScalarAsync(token).ConfigureAwait(false);
                return r != 0;
            }
            finally
            {
                _updateLock.Release();
            }
        }

        public void RequestDownload(ulong guildId)
        {
            lock (_fetchRequests) _fetchRequests.Add(guildId);
        }
    }
}
