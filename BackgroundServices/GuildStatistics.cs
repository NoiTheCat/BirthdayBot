using System;
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
            var count = BotInstance.DiscordClient.Guilds.Count;
            Log($"Currently in {count} guilds.");
            await SendExternalStatistics(count);
        }

        /// <summary>
        /// Send statistical information to external services.
        /// </summary>
        async Task SendExternalStatistics(int count)
        {
            var dbotsToken = BotInstance.Config.DBotsToken;
            if (dbotsToken != null)
            {
                const string dBotsApiUrl = "https://discord.bots.gg/api/v1/bots/{0}/stats";
                const string Body = "{{ \"guildCount\": {0} }}";
                var uri = new Uri(string.Format(dBotsApiUrl, BotInstance.DiscordClient.CurrentUser.Id));

                var post = new HttpRequestMessage(HttpMethod.Post, uri);
                post.Headers.Add("Authorization", dbotsToken);
                post.Content = new StringContent(string.Format(Body, count), Encoding.UTF8, "application/json");

                await Task.Delay(80); // Discord Bots rate limit for this endpoint is 20 per second
                await _httpClient.SendAsync(post);
                Log("Discord Bots: Count sent successfully.");
            }
        }
    }
}
