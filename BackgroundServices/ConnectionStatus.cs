using System.Threading;
using System.Threading.Tasks;

namespace BirthdayBot.BackgroundServices
{
    /// <summary>
    /// Keeps track of the connection status, assigning a score based on either the connection's
    /// longevity or the amount of time it has remained persistently disconnected.
    /// </summary>
    class ConnectionStatus : BackgroundService
    {
        // About 3 minutes
        public const int StableScore = 180 / ShardBackgroundWorker.Interval;

        public bool Stable { get { return Score >= StableScore; } }
        public int Score { get; private set; }

        public ConnectionStatus(ShardInstance instance) : base(instance) { }

        public override Task OnTick(CancellationToken token)
        {
            switch (ShardInstance.DiscordClient.ConnectionState)
            {
                case Discord.ConnectionState.Connected:
                    if (Score < 0) Score = 0;
                    Score++;
                    break;
                default:
                    if (Score > 0) Score = 0;
                    Score--;
                    break;
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// In response to a disconnection event, will immediately reset a positive score to zero.
        /// </summary>
        public void Disconnected()
        {
            if (Score > 0) Score = 0;
        }
    }
}
