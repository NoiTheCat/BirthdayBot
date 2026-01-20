using Newtonsoft.Json;
using WorldTime.Config;

namespace WorldTime;

public class ConfigurationLoader {
    public Configuration Config { get; }

    // All exception handling is responsibility of the caller
    public ConfigurationLoader(string[] args) {
        // Search and load config file
        var argOpts = CommandLineParser.Parse(args);
        var path = argOpts?.ConfigFile ?? Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory)
            + Path.DirectorySeparatorChar + "." + Path.DirectorySeparatorChar + "settings.json";

        var file = File.ReadAllText(path);
        // Do NOT use DefaultValueHandling.Populate! It leads to confusing behavior with
        // default reference types (like Sharding).
        Config = JsonConvert.DeserializeObject<Configuration>(file)!;

        // Command line overrides file config
        // Only a few options are supported, particularly those that help with multi-instances.
        if (argOpts?.ShardRange is not null) {
            Config.Sharding.ProcessShardRange(argOpts.ShardRange);
        }

        Config.Validate();
    }

    public string GetConnectionString() {
        return new Npgsql.NpgsqlConnectionStringBuilder() {
            Host = Config.Database.Host,
            Database = Config.Database.Database,
            Username = Config.Database.Username,
            Password = Config.Database.Password,
            ApplicationName = $"Shard{Config.Sharding.StartId:00}-{Config.Sharding.StartId + Config.Sharding.Amount - 1:00}"
        }.ConnectionString;
    }
}