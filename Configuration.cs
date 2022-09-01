using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BirthdayBot;
/// <summary>
/// Loads and holds configuration values.
/// </summary>
class Configuration {
    const string KeyShardRange = "ShardRange";

    public string BotToken { get; }
    public string? DBotsToken { get; }
    public bool QuitOnFails { get; }

    public int ShardStart { get; }
    public int ShardAmount { get; }
    public int ShardTotal { get; }
    
    public string? SqlHost { get; }
    public string? SqlDatabase { get; }
    public string SqlUsername { get; }
    public string SqlPassword { get; }
    internal string SqlApplicationName { get; }

    public Configuration() {
        var args = CommandLineParameters.Parse(Environment.GetCommandLineArgs());
        var path = args?.ConfigFile ?? Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)
            + Path.DirectorySeparatorChar + "." + Path.DirectorySeparatorChar + "settings.json";

        // Looks for configuration file
        JObject jc;
        try {
            var conftxt = File.ReadAllText(path);
            jc = JObject.Parse(conftxt);
        } catch (Exception ex) {
            string pfx;
            if (ex is JsonException) pfx = "Unable to parse configuration: ";
            else pfx = "Unable to access configuration: ";

            throw new Exception(pfx + ex.Message, ex);
        }

        BotToken = ReadConfKey<string>(jc, nameof(BotToken), true);
        DBotsToken = ReadConfKey<string>(jc, nameof(DBotsToken), false);
        QuitOnFails = ReadConfKey<bool?>(jc, nameof(QuitOnFails), false) ?? false;

        ShardTotal = args.ShardTotal ?? ReadConfKey<int?>(jc, nameof(ShardTotal), false) ?? 1;
        if (ShardTotal < 1) throw new Exception($"'{nameof(ShardTotal)}' must be a positive integer.");

        var shardRangeInput = args.ShardRange ?? ReadConfKey<string>(jc, KeyShardRange, false);
        if (!string.IsNullOrWhiteSpace(shardRangeInput)) {
            Regex srPicker = new(@"(?<low>\d{1,2})[-,]{1}(?<high>\d{1,2})");
            var m = srPicker.Match(shardRangeInput);
            if (m.Success) {
                ShardStart = int.Parse(m.Groups["low"].Value);
                var high = int.Parse(m.Groups["high"].Value);
                ShardAmount = high - (ShardStart - 1);
            } else {
                throw new Exception($"Shard range not properly formatted in '{KeyShardRange}'.");
            }
        } else {
            // Default: this instance handles all shards
            ShardStart = 0;
            ShardAmount = ShardTotal;
        }

        SqlHost = ReadConfKey<string>(jc, nameof(SqlHost), false);
        SqlDatabase = ReadConfKey<string?>(jc, nameof(SqlDatabase), false);
        SqlUsername = ReadConfKey<string>(jc, nameof(SqlUsername), true);
        SqlPassword = ReadConfKey<string>(jc, nameof(SqlPassword), true);
        SqlApplicationName = $"ClientShard{ShardStart}+{ShardAmount}";
    }

    private static T? ReadConfKey<T>(JObject jc, string key, [DoesNotReturnIf(true)] bool failOnEmpty) {
        if (jc.ContainsKey(key)) return jc[key]!.Value<T>();
        if (failOnEmpty) throw new Exception($"'{key}' must be specified.");
        return default;
    }

    class CommandLineParameters {
        [Option('c', "config")]
        public string? ConfigFile { get; set; }

        [Option("shardtotal")]
        public int? ShardTotal { get; set; }

        [Option("shardrange")]
        public string? ShardRange { get; set; }

        public static CommandLineParameters? Parse(string[] args) {
            CommandLineParameters? result = null;

            new Parser(settings => {
                settings.IgnoreUnknownArguments = true;
                settings.AutoHelp = false;
                settings.AutoVersion = false;
            }).ParseArguments<CommandLineParameters>(args)
                .WithParsed(p => result = p)
                .WithNotParsed(e => { /* ignore */ });
            return result;
        }
    }
}
