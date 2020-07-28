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
            return Task.CompletedTask;
        }
    }
}
