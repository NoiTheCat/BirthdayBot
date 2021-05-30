using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BirthdayBot.BackgroundServices
{
    /// <summary>
    /// Reports user count statistics to external services on a shard by shard basis.
    /// </summary>
    class ExternalStatisticsReporting : BackgroundService
    {
        const int ProcessInterval = 600 / ShardBackgroundWorker.Interval; // Process every ~5 minutes
        private int _tickCount = 0;

        private static readonly HttpClient _httpClient = new HttpClient();

        public ExternalStatisticsReporting(ShardInstance instance) : base(instance) { }

        public override async Task OnTick(CancellationToken token)
        {
            if (++_tickCount % ProcessInterval != 0) return;

            var botId = ShardInstance.DiscordClient.CurrentUser.Id;
            if (botId == 0) return;

            await SendDiscordBots(ShardInstance.DiscordClient.Guilds.Count, botId, token);
        }

        private async Task SendDiscordBots(int userCount, ulong botId, CancellationToken token)
        {
            var dbotsToken = ShardInstance.Config.DBotsToken;
            if (dbotsToken != null)
            {
                try
                {
                    const string dBotsApiUrl = "https://discord.bots.gg/api/v1/bots/{0}/stats";
                    const string Body = "{{ \"guildCount\": {0}, \"shardCount\": {1}, \"shardId\": {2} }}";
                    var uri = new Uri(string.Format(dBotsApiUrl, botId));

                    var post = new HttpRequestMessage(HttpMethod.Post, uri);
                    post.Headers.Add("Authorization", dbotsToken);
                    post.Content = new StringContent(string.Format(Body,
                        userCount, ShardInstance.Config.ShardTotal, ShardInstance.ShardId),
                        Encoding.UTF8, "application/json");

                    await _httpClient.SendAsync(post, token);
                    Log("Discord Bots: Update successful.");
                }
                catch (Exception ex)
                {
                    Log("Discord Bots: Exception encountered during update: " + ex.Message);
                }
            }
        }
    }
}
