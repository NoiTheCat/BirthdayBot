using System.Text;

namespace BirthdayBot.BackgroundServices;
/// <summary>
/// Reports user count statistics to external services on a shard by shard basis.
/// </summary>
class ExternalStatisticsReporting : BackgroundService {
    const int ProcessInterval = 1200 / ShardBackgroundWorker.Interval; // Process every ~20 minutes
    const int ProcessOffset = 300 / ShardBackgroundWorker.Interval; // Begin processing ~5 minutes after shard start

    private static readonly HttpClient _httpClient = new();

    public ExternalStatisticsReporting(ShardInstance instance) : base(instance) { }

    public override async Task OnTick(int tickCount, CancellationToken token) {
        if (tickCount < ProcessOffset) return;
        if (tickCount % ProcessInterval != 0) return;

        var botId = ShardInstance.DiscordClient.CurrentUser.Id;
        if (botId == 0) return;
        var count = ShardInstance.DiscordClient.Guilds.Count;

        var dbotsToken = ShardInstance.Config.DBotsToken;
        if (dbotsToken != null) await SendDiscordBots(dbotsToken, count, botId, token);
    }

    private async Task SendDiscordBots(string apiToken, int userCount, ulong botId, CancellationToken token) {
        try {
            const string dBotsApiUrl = "https://discord.bots.gg/api/v1/bots/{0}/stats";
            const string Body = "{{ \"guildCount\": {0}, \"shardCount\": {1}, \"shardId\": {2} }}";
            var uri = new Uri(string.Format(dBotsApiUrl, botId));

            var post = new HttpRequestMessage(HttpMethod.Post, uri);
            post.Headers.Add("Authorization", apiToken);
            post.Content = new StringContent(string.Format(Body,
                userCount, ShardInstance.Config.ShardTotal, ShardInstance.ShardId),
                Encoding.UTF8, "application/json");

            await _httpClient.SendAsync(post, token);
            Log("Discord Bots: Update successful.");
        } catch (Exception ex) {
            Log("Discord Bots: Exception encountered during update: " + ex.Message);
        }
    }
}
