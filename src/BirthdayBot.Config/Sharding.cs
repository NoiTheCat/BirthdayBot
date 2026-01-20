using System.ComponentModel;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace WorldTime.Config;

public partial class Sharding {
    [JsonProperty(Required = Required.DisallowNull)]
    [Description("The shard ID that this instance will initialize first.")]
    [DefaultValue(0)]
    public int StartId { get; set; } = 0;

    [JsonProperty(Required = Required.DisallowNull)]
    [Description("Total amount of shards this instance will host, beginning with StartId.")]
    [DefaultValue(1)]
    // TODO adapt to Total if not defined
    public int Amount { get; set; } = 1;

    [JsonProperty(Required = Required.DisallowNull)]
    [Description("Total amount of shards run by the bot. MUST be the same across instances.")]
    [DefaultValue(1)]
    public int Total { get; set; } = 1;

    [JsonProperty(Required = Required.DisallowNull)]
    [Description("The amount of shards to initialize at once. Discord usually limits this to 2 before enforcing rate limits.")]
    [DefaultValue(2)]
    public int Interval { get; set; } = 2;

    public void ProcessShardRange(string input) {
        var m = Regex.Match(input, @"(?<low>\d{1,3})[-,](?<high>\d{1,3})");
        if (m.Success) {
            StartId = int.Parse(m.Groups["low"].Value);
            var high = int.Parse(m.Groups["high"].Value);
            Amount = high - (StartId - 1);
        }
        else {
            throw new Exception("Shard range parameter is not properly formatted.");
        }
    }

    internal void Validate() {
        if (StartId < 0) throw new Exception($"{nameof(Sharding)}/{nameof(StartId)} cannot be negative.");
        if (Amount < 1) throw new Exception($"{nameof(Sharding)}/{nameof(Amount)} must be 1 or more.");
        if (Total < 1) throw new Exception($"{nameof(Sharding)}/{nameof(Total)} must be 1 or more.");
        if (Interval < 1) throw new Exception($"{nameof(Sharding)}/{nameof(Interval)} must be 1 or more.");
    }
}
