using BirthdayBot.Data;
using Newtonsoft.Json.Linq;
using Npgsql;
using System;
using System.IO;
using System.Reflection;

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
        public int ShardCount { get; }

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

            BotToken = jc["BotToken"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(BotToken))
                throw new Exception("'BotToken' must be specified.");

            LogWebhook = jc["LogWebhook"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(LogWebhook))
                throw new Exception("'LogWebhook' must be specified.");

            var dbj = jc["DBotsToken"];
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

            int? sc = jc["ShardCount"]?.Value<int>();
            if (!sc.HasValue) ShardCount = 1;
            else
            {
                ShardCount = sc.Value;
                if (ShardCount <= 0)
                {
                    throw new Exception("'ShardCount' must be a positive integer.");
                }
            }
        }
    }
}
