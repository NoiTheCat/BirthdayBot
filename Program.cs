using BirthdayBot.Data;
using System;
using System.Threading.Tasks;

namespace BirthdayBot
{
    class Program
    {
        private static ShardManager _bot;
        public static DateTimeOffset BotStartTime { get; private set; }

        static async Task Main()
        {
            BotStartTime = DateTimeOffset.UtcNow;
            var cfg = new Configuration();

            await Database.DoInitialDatabaseSetupAsync();

            Console.CancelKeyPress += OnCancelKeyPressed;
            _bot = new ShardManager(cfg);

            await Task.Delay(-1);
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

        private static void OnCancelKeyPressed(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            ProgramStop();
        }

        private static bool _stopping = false;
        public static void ProgramStop()
        {
            if (_stopping) return;
            _stopping = true;

            var dispose = Task.Run(_bot.Dispose);
            if (!dispose.Wait(90000))
            {
                Log("Shutdown", "Normal shutdown has not concluded after 90 seconds. Will force quit.");
            }
            Environment.Exit(0);
        }
    }
}
