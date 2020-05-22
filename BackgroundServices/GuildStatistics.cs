using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace BirthdayBot.BackgroundServices
{
    class GuildStatistics : BackgroundService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public GuildStatistics(BirthdayBot instance) : base(instance) { }

        public async override Task OnTick()
        {
            var counts = GetGuildCounts();

            // Build this report
            int goodCount = 0;
            int badCount = 0;
            int goodShards = 0;
            int badShards = 0;
            foreach (var status in counts)
            {
                if (status.Item2.ShardConnected)
                {
                    goodShards++;
                    goodCount += status.Item2.GuildCount;
                }
                else
                {
                    badShards++;
                    badCount += status.Item2.GuildCount;
                }
            }
            Log($"{goodShards} shard(s) connected, serving {goodCount} guild(s).");
            if (badShards != 0) Log($"{badShards} shard(s) unavailable, >={badCount} guild(s) will not count in the next report.");

            // Report only connected shards (to not have fluctuating member numbers on initial startup)
            await SendExternalStatistics(counts.Where(t => t.Item2.ShardConnected), counts.Count());
        }

        private struct ShardStatus
        {
            public bool ShardConnected;
            public int GuildCount;
        }

        private IEnumerable<(int, ShardStatus)> GetGuildCounts()
        {
            var results = new List<(int, ShardStatus)>();
            var shards = BotInstance.DiscordClient.Shards;
            foreach (var shard in shards)
            {
                results.Add((shard.ShardId, new ShardStatus()
                {
                    ShardConnected = shard.ConnectionState == Discord.ConnectionState.Connected,
                    GuildCount = shard.Guilds.Count
                }));
            }
            return results;
        }

        /// <summary>
        /// Send statistical information to external services.
        /// </summary>
        async Task SendExternalStatistics(IEnumerable<(int, ShardStatus)> shardStats, int totalShards)
        {
            var dbotsToken = BotInstance.Config.DBotsToken;
            if (dbotsToken != null)
            {
                const string dBotsApiUrl = "https://discord.bots.gg/api/v1/bots/{0}/stats";
                const string Body = "{{ \"shardCount\": {0}, \"shardId\": {1}, \"guildCount\": {2} }}";
                var uri = new Uri(string.Format(dBotsApiUrl, BotInstance.DiscordClient.CurrentUser.Id));
                foreach (var shard in shardStats)
                {
                    var post = new HttpRequestMessage(HttpMethod.Post, uri);
                    post.Headers.Add("Authorization", dbotsToken);
                    var data = string.Format(Body, totalShards, shard.Item1, shard.Item2.GuildCount);
                    post.Content = new StringContent(data, Encoding.UTF8, "application/json");
                    await Task.Delay(80); // Discord Bots rate limit for this endpoint is 20 per second
                    await _httpClient.SendAsync(post);
                    Log("Discord Bots: Reports sent successfully.");
                }
            }
        }
    }
}
