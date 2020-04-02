using System;
using System.Net;
using System.Threading.Tasks;

namespace BirthdayBot.BackgroundServices
{
    class GuildStatistics : BackgroundService
    {
        private string DBotsToken { get; }

        public GuildStatistics(BirthdayBot instance) : base(instance) => DBotsToken = instance.Config.DBotsToken;

        public async override Task OnTick()
        {
            var count = BotInstance.DiscordClient.Guilds.Count;
            Log($"Currently in {count} guild(s).");

            await SendExternalStatistics(count);
        }

        /// <summary>
        /// Send statistical information to external services.
        /// </summary>
        /// <remarks>
        /// Only Discord Bots is currently supported. No plans to support others any time soon.
        /// </remarks>
        async Task SendExternalStatistics(int guildCount)
        {
            var rptToken = BotInstance.Config.DBotsToken;
            if (rptToken == null) return;

            const string apiUrl = "https://discord.bots.gg/api/v1/bots/{0}/stats";
            using (var client = new WebClient())
            {
                var uri = new Uri(string.Format(apiUrl, BotInstance.DiscordClient.CurrentUser.Id));
                var data = $"{{ \"guildCount\": {guildCount} }}";
                client.Headers[HttpRequestHeader.Authorization] = rptToken;
                client.Headers[HttpRequestHeader.ContentType] = "application/json";
                try
                {
                    await client.UploadStringTaskAsync(uri, data);
                    Log("Discord Bots: Report sent successfully.");
                } catch (WebException ex)
                {
                    Log("Discord Bots: Encountered an error. " + ex.Message);
                }
            }
        }
    }
}
