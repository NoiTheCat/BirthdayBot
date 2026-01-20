using System.Runtime.InteropServices;

namespace BirthdayBot;
class Program {
    private static ShardManager _bot = null!;
    private static readonly DateTimeOffset _botStartTime = DateTimeOffset.UtcNow;

    /// <summary>
    /// Returns the amount of time the program has been running in a human-readable format.
    /// </summary>
    public static string BotUptime => (DateTimeOffset.UtcNow - _botStartTime).ToString("d' days, 'hh':'mm':'ss");

    static async Task Main() {
        Configuration? cfg = null;
        try {
            cfg = new Configuration();
        } catch (Exception ex) {
            Console.WriteLine(ex);
            Environment.Exit(2);
        }

        _bot = new ShardManager(cfg);

        Console.CancelKeyPress += static (s, e) => {
            e.Cancel = true;
            Log("Shutdown", "Caught Ctrl-C or SIGINT.");
            DoShutdown();
        };
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) {
            _sigtermHandler = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => {
                ctx.Cancel = true;
                Log("Shutdown", "Caught SIGTERM.");
                DoShutdown();
            });
        }

        await _shutdownBlock.Task;
        Log(nameof(BirthdayBot), $"Shutdown complete. Uptime: {BotUptime}");
    }

    /// <summary>
    /// Sends a formatted message to console.
    /// </summary>
    public static void Log(string source, string message) {
        var ts = DateTime.Now;
        var ls = new string[] { "\r\n", "\n" };
        foreach (var item in message.Split(ls, StringSplitOptions.None))
            Console.WriteLine($"{ts:s} [{source}] {item}");
    }

    #region Shutdown logic
    private static int _isShuttingDown = 0;
    private static PosixSignalRegistration? _sigtermHandler; // DO NOT REMOVE else signal handler is GCed away
    private static readonly TaskCompletionSource<bool> _shutdownBlock = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void DoShutdown() {
        if (Interlocked.Exchange(ref _isShuttingDown, 1) == 1) return;

        Log("Shutdown", "Shutting down...");
        var dispose = Task.Run(_bot.Dispose);
        if (!dispose.Wait(10000)) {
            Log("Shutdown", "Normal shutdown is taking too long. We're force-quitting.");
            Environment.Exit(1);
        }

        Environment.ExitCode = 0;
        _shutdownBlock.SetResult(true);
    }
    #endregion
}
