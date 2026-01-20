using System.ComponentModel;
using Newtonsoft.Json;

namespace WorldTime.Config;

/// <summary>
/// Root config class. Not to be confused with <seealso cref="ConfigurationLoader" />.
/// </summary>
public class Configuration {
    [JsonProperty(Required = Required.Always)]
    [Description("Discord application token.")]
    public string BotToken { get; set; } = null!;

    [Description("Token for submitting statistics to Discord Bots.")]
    public string? DBotsToken { get; set; } = null!;

    [JsonProperty(Required = Required.Always)]
    [Description("PostgreSQL database settings.")]
    public DatabaseSettings Database { get; set; } = new();

    [JsonProperty(Required = Required.DisallowNull)]
    [Description("Defines how this instance will handle sharding.")]
    public Sharding Sharding { get; set; } = new();

    [Description("Interval between status updates, in seconds.")]
    [DefaultValue(90)]
    public int StatusInterval { get; set; } = 90;

    [Description("The maximum amount of background operations that can run concurrently.")]
    [DefaultValue(4)]
    public int MaxConcurrentOperations { get; set; } = 4;

    [Description("How often background tasks are run per shard, in seconds.")]
    [DefaultValue(300)]
    public int BackgroundInterval { get; set; } = 300;

    [Description("Whether to show common connect and disconnect events, and other messages regarding connection to Discord.\n"
        + "This is disabled in the public instance, but is worth keeping enabled in self-hosted bots.")]
    [DefaultValue(true)]
    public bool LogConnectionStatus { get; set; } = true;

    public void Validate() {
        if (StatusInterval < 10) throw new Exception($"{nameof(StatusInterval)} must be 10 or more.");
        if (MaxConcurrentOperations < 1) throw new Exception($"{nameof(MaxConcurrentOperations)} must be 1 or more.");
        if (BackgroundInterval < 1) throw new Exception($"{nameof(BackgroundInterval)} must be 1 or more.");

        Database.Validate();
        Sharding.Validate();
    }
}
