using BirthdayBot.Data;
using Newtonsoft.Json.Linq;
using Npgsql;
using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BirthdayBot
{
    /// <summary>
    /// Loads and holds configuration values.
    /// </summary>
    class Configuration
    {
        public string BotToken { get; }
        public string LogWebhook { get; }
        public string DBotsToken { get; }

        public const string ShardLenConfKey = "ShardRange";
        public int ShardStart { get; }
        public int ShardAmount { get; }

        public int ShardTotal { get; }

        public bool QuitOnFails { get; }

        public Configuration()
        {
            // Looks for settings.json in the executable directory.
            var confPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            confPath += Path.DirectorySeparatorChar + "settings.json";

            if (!File.Exists(confPath))
            {
                throw new Exception("Settings file not found."
                    + " Create a file in the executable directory named 'settings.json'.");
            }

            var jc = JObject.Parse(File.ReadAllText(confPath));

            BotToken = jc[nameof(BotToken)]?.Value<string>();
            if (string.IsNullOrWhiteSpace(BotToken))
                throw new Exception($"'{nameof(BotToken)}' must be specified.");

            LogWebhook = jc[nameof(LogWebhook)]?.Value<string>();
            if (string.IsNullOrWhiteSpace(LogWebhook))
                throw new Exception($"'{nameof(LogWebhook)}' must be specified.");

            var dbj = jc[nameof(DBotsToken)];
            if (dbj != null)
            {
                DBotsToken = dbj.Value<string>();
            }
            else
            {
                DBotsToken = null;
            }

            var sqlhost = jc["SqlHost"]?.Value<string>() ?? "localhost"; // Default to localhost
            var sqluser = jc["SqlUsername"]?.Value<string>();
            var sqlpass = jc["SqlPassword"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(sqluser) || string.IsNullOrWhiteSpace(sqlpass))
                throw new Exception("'SqlUsername', 'SqlPassword' must be specified.");
            var csb = new NpgsqlConnectionStringBuilder()
            {
                Host = sqlhost,
                Username = sqluser,
                Password = sqlpass
            };
            var sqldb = jc["SqlDatabase"]?.Value<string>();
            if (sqldb != null) csb.Database = sqldb; // Optional database setting
            Database.DBConnectionString = csb.ToString();

            int? sc = jc[nameof(ShardTotal)]?.Value<int>();
            if (!sc.HasValue) ShardTotal = 1;
            else
            {
                ShardTotal = sc.Value;
                if (ShardTotal <= 0)
                {
                    throw new Exception($"'{nameof(ShardTotal)}' must be a positive integer.");
                }
            }

            string srVal = jc[ShardLenConfKey]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(srVal))
            {
                Regex srPicker = new(@"(?<low>\d{1,2})[-,]{1}(?<high>\d{1,2})");
                var m = srPicker.Match(srVal);
                if (m.Success)
                {
                    ShardStart = int.Parse(m.Groups["low"].Value);
                    int high = int.Parse(m.Groups["high"].Value);
                    ShardAmount = high - (ShardStart - 1);
                }
                else
                {
                    throw new Exception($"Shard range not properly formatted in '{ShardLenConfKey}'.");
                }
            }
            else
            {
                // Default: this instance handles all shards from ShardTotal
                ShardStart = 0;
                ShardAmount = ShardTotal;
            }

            QuitOnFails = jc[nameof(QuitOnFails)]?.Value<bool>() ?? false;
        }
    }
}
