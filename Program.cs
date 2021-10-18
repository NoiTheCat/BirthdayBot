using BirthdayBot.Data;
using System;
using System.Threading.Tasks;

namespace BirthdayBot;

class Program {
    private static ShardManager _bot;
    private static readonly DateTimeOffset _botStartTime = DateTimeOffset.UtcNow;

    /// <summary>
    /// Returns the amount of time the program has been running in a human-readable format.
    /// </summary>
    public static string BotUptime => (DateTimeOffset.UtcNow - _botStartTime).ToString("d' days, 'hh':'mm':'ss");

    static async Task Main() {
        var cfg = new Configuration();
        try {
            await Database.DoInitialDatabaseSetupAsync();
        } catch (Npgsql.NpgsqlException e) {
            Console.WriteLine("Error when attempting to connect to database: " + e.Message);
            Environment.Exit(1);
        }
        
        Console.CancelKeyPress += OnCancelKeyPressed;
        _bot = new ShardManager(cfg);

        await Task.Delay(-1);
    }

    /// <summary>
    /// Sends a formatted message to console.
    /// </summary>
    public static void Log(string source, string message) {
        var ts = DateTime.UtcNow;
        var ls = new string[] { "\r\n", "\n" };
        foreach (var item in message.Split(ls, StringSplitOptions.None))
            Console.WriteLine($"{ts:u} [{source}] {item}");
    }

    private static void OnCancelKeyPressed(object sender, ConsoleCancelEventArgs e) {
        e.Cancel = true;
        Log("Shutdown", "Captured cancel key; sending shutdown.");
        ProgramStop();
    }

    private static bool _stopping = false;
    public static void ProgramStop() {
        if (_stopping) return;
        _stopping = true;
        Log("Shutdown", "Commencing shutdown...");

        var dispose = Task.Run(_bot.Dispose);
        if (!dispose.Wait(90000)) {
            Log("Shutdown", "Normal shutdown has not concluded after 90 seconds. Will force quit.");
            Environment.ExitCode += 0x200;
        }
        Environment.Exit(Environment.ExitCode);
    }
}
