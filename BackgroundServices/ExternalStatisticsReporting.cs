using System.Text;

namespace BirthdayBot.BackgroundServices;
/// <summary>
/// Reports user count statistics to external services on a shard by shard basis.
/// </summary>
class ExternalStatisticsReporting : BackgroundService {
    readonly int ProcessInterval;
    readonly int ProcessOffset;

    private static readonly HttpClient _httpClient = new();

    public ExternalStatisticsReporting(ShardInstance instance) : base(instance) {
        ProcessInterval = 1200 / Shard.Config.BackgroundInterval; // Process every ~20 minutes
        ProcessOffset = 300 / Shard.Config.BackgroundInterval; // No processing until ~5 minutes after shard start
    }

    public override async Task OnTick(int tickCount, CancellationToken token) {
        if (tickCount < ProcessOffset) return;
        if (tickCount % ProcessInterval != 0) return;

        var botId = Shard.DiscordClient.CurrentUser.Id;
        if (botId == 0) return;
        var count = Shard.DiscordClient.Guilds.Count;

        var dbotsToken = Shard.Config.DBotsToken;
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
                userCount, Shard.Config.ShardTotal, Shard.ShardId),
                Encoding.UTF8, "application/json");

            await _httpClient.SendAsync(post, token);
            Log("Discord Bots: Update successful.");
        } catch (Exception ex) {
            Log("Discord Bots: Exception encountered during update: " + ex.Message);
        }
    }
}
