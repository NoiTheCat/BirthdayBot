using BirthdayBot.Data;
using Newtonsoft.Json.Linq;
using Npgsql;
using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BirthdayBot;

/// <summary>
/// Loads and holds configuration values.
/// </summary>
class Configuration {
    const string KeySqlHost = "SqlHost";
    const string KeySqlUsername = "SqlUsername";
    const string KeySqlPassword = "SqlPassword";
    const string KeySqlDatabase = "SqlDatabase";
    const string KeyShardRange = "ShardRange";

    public string BotToken { get; }
    public string LogWebhook { get; }
    public string? DBotsToken { get; }
    public bool QuitOnFails { get; }

    public int ShardStart { get; }
    public int ShardAmount { get; }
    public int ShardTotal { get; }

    public Configuration() {
        // Looks for settings.json in the executable directory.
        var confPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        confPath += Path.DirectorySeparatorChar + "settings.json";

        if (!File.Exists(confPath)) {
            throw new Exception("Settings file not found."
                + " Create a file in the executable directory named 'settings.json'.");
        }

        var jc = JObject.Parse(File.ReadAllText(confPath));

        BotToken = ReadConfKey<string>(jc, nameof(BotToken), true);
        LogWebhook = ReadConfKey<string>(jc, nameof(LogWebhook), true);
        DBotsToken = ReadConfKey<string>(jc, nameof(DBotsToken), false);
        QuitOnFails = ReadConfKey<bool?>(jc, nameof(QuitOnFails), false) ?? false;

        ShardTotal = ReadConfKey<int?>(jc, nameof(ShardTotal), false) ?? 1;
        if (ShardTotal < 1) throw new Exception($"'{nameof(ShardTotal)}' must be a positive integer.");

        string shardRangeInput = ReadConfKey<string>(jc, KeyShardRange, false);
        if (!string.IsNullOrWhiteSpace(shardRangeInput)) {
            Regex srPicker = new(@"(?<low>\d{1,2})[-,]{1}(?<high>\d{1,2})");
            var m = srPicker.Match(shardRangeInput);
            if (m.Success) {
                ShardStart = int.Parse(m.Groups["low"].Value);
                int high = int.Parse(m.Groups["high"].Value);
                ShardAmount = high - (ShardStart - 1);
            } else {
                throw new Exception($"Shard range not properly formatted in '{KeyShardRange}'.");
            }
        } else {
            // Default: this instance handles all shards
            ShardStart = 0;
            ShardAmount = ShardTotal;
        }

        var sqlhost = ReadConfKey<string>(jc, KeySqlHost, false) ?? "localhost"; // Default to localhost
        var sqluser = ReadConfKey<string>(jc, KeySqlUsername, false);
        var sqlpass = ReadConfKey<string>(jc, KeySqlPassword, false);
        if (string.IsNullOrWhiteSpace(sqluser) || string.IsNullOrWhiteSpace(sqlpass))
            throw new Exception("'SqlUsername', 'SqlPassword' must be specified.");
        var csb = new NpgsqlConnectionStringBuilder() {
            Host = sqlhost,
            Username = sqluser,
            Password = sqlpass,
            ApplicationName = $"ClientShard{ShardStart}+{ShardAmount}"
        };
        var sqldb = ReadConfKey<string>(jc, KeySqlDatabase, false);
        if (sqldb != null) csb.Database = sqldb; // Optional database setting
        Database.DBConnectionString = csb.ToString();
    }

    private static T ReadConfKey<T>(JObject jc, string key, bool failOnEmpty) {
        if (jc.ContainsKey(key)) return jc[key].Value<T>();
        if (failOnEmpty) throw new Exception($"'{key}' must be specified.");
        return default;
    }
}
