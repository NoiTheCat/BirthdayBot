using Discord;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;

namespace BirthdayBot
{
    class Program
    {
        private static BirthdayBot _bot;

        public static DateTimeOffset BotStartTime { get; private set; }

        static void Main()
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            Log("Birthday Bot", $"Version {ver.ToString(3)} is starting.");

            BotStartTime = DateTimeOffset.UtcNow;
            var cfg = new Configuration();

            var dc = new DiscordSocketConfig()
            {
                AlwaysDownloadUsers = true,
                DefaultRetryMode = Discord.RetryMode.RetryRatelimit,
                MessageCacheSize = 0,
                TotalShards = cfg.ShardCount,
                ExclusiveBulkDelete = true
            };

            var client = new DiscordShardedClient(dc);
            client.Log += DNetLog;

            _bot = new BirthdayBot(cfg, client);

            Console.CancelKeyPress += OnCancelKeyPressed;

            _bot.Start().Wait();
        }

        /// <summary>
        /// Sends a formatted message to console.
        /// </summary>
        public static void Log(string source, string message)
        {
            var ts = DateTime.UtcNow;
            var ls = new string[]{ "\r\n", "\n" };
            foreach (var item in message.Split(ls, StringSplitOptions.None))
                Console.WriteLine($"{ts:u} [{source}] {item}");
        }

        private static Task DNetLog(LogMessage arg)
        {
            // Suppress 'Unknown Dispatch' messages
            if (arg.Message.StartsWith("Unknown Dispatch ")) return Task.CompletedTask;

            if (arg.Severity <= LogSeverity.Info)
            {
                Log("Discord.Net", $"{arg.Severity}: {arg.Message}");
            }

            if (arg.Exception != null)
            {
                Log("Discord.Net", arg.Exception.ToString());
            }

            return Task.CompletedTask;
        }

        private static void OnCancelKeyPressed(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            Log("Shutdown", "Caught cancel key. Will shut down...");
            var hang = !_bot.Shutdown().Wait(10000);
            if (hang)
            {
                Log("Shutdown", "Normal shutdown has not concluded after 10 seconds. Will force quit.");
            }
            Environment.Exit(0);
        }
    }
}
