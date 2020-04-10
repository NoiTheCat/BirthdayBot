using BirthdayBot.Data;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BirthdayBot.BackgroundServices
{
    /// <summary>
    /// Automatically removes database information for guilds that have not been accessed in a long time.
    /// </summary>
    class StaleDataCleaner : BackgroundService
    {
        public StaleDataCleaner(BirthdayBot instance) : base(instance) { }

        public override async Task OnTick()
        {
            using var db = await BotInstance.Config.DatabaseSettings.OpenConnectionAsync();

            // Update only for all guilds the bot has cached
            using (var c = db.CreateCommand())
            {
                c.CommandText = $"update {GuildStateInformation.BackingTable} set last_seen = now() "
                    + "where guild_id = @Gid";
                var updateGuild = c.Parameters.Add("@Gid", NpgsqlTypes.NpgsqlDbType.Bigint);
                c.Prepare();

                var list = new List<ulong>(BotInstance.GuildCache.Keys);
                foreach (var id in list)
                {
                    updateGuild.Value = (long)id;
                    c.ExecuteNonQuery();
                }
            }

            // Delete all old values - expecte referencing tables to have 'on delete cascade'
            using (var t = db.BeginTransaction())
            {
                using (var c = db.CreateCommand())
                {
                    // Delete data for guilds not seen in 2 weeks
                    c.CommandText = $"delete from {GuildUserSettings.BackingTable} where (now() - interval '14 days') > last_seen";
                    var r = c.ExecuteNonQuery();
                    if (r != 0) Log($"Removed {r} stale guild(s).");
                }
            }
        }
    }
}
