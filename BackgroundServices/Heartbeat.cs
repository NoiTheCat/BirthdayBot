using System;
using System.Threading.Tasks;

namespace BirthdayBot.BackgroundServices
{
    /// <summary>
    /// Basic heartbeat function - hints that the background task is still alive.
    /// </summary>
    class Heartbeat : BackgroundService
    {
        public Heartbeat(BirthdayBot instance) : base(instance) { }

        public override Task OnTick()
        {
            var uptime = DateTimeOffset.UtcNow - Program.BotStartTime;
            Log($"Bot uptime: {Common.BotUptime}");

            // Disconnection warn
            foreach (var shard in BotInstance.DiscordClient.Shards)
            {
                if (shard.ConnectionState == Discord.ConnectionState.Disconnected)
                {
                    Log($"Shard {shard.ShardId} is disconnected! Restart the app if this persists.");
                    // The library alone cannot be restarted as it is in an unknown state. It was not designed to be restarted.
                    // TODO This is the part where we'd signal something to restart us if we were fancy.
                }
            }

            return Task.CompletedTask;
        }
    }
}
