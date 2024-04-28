namespace BirthdayBot.BackgroundServices;
/// <summary>
/// Handles the execution of periodic background tasks specific to each shard.
/// </summary>
class ShardBackgroundWorker : IDisposable {
    /// <summary>
    /// The interval, in seconds, in which background tasks are attempted to be run within a shard.
    /// </summary>
    private int Interval { get; }

    private readonly Task _workerTask;
    private readonly CancellationTokenSource _workerCanceller;
    private readonly List<BackgroundService> _workers;
    private int _tickCount = -1;

    private ShardInstance Instance { get; }

    public DateTimeOffset LastBackgroundRun { get; private set; }
    public string? CurrentExecutingService { get; private set; }

    public ShardBackgroundWorker(ShardInstance instance) {
        Instance = instance;
        Interval = instance.Config.BackgroundInterval;
        _workerCanceller = new CancellationTokenSource();

        _workers = new List<BackgroundService>()
        {
                {new AutoUserDownload(instance)},
                {new BirthdayRoleUpdate(instance)},
                {new DataRetention(instance)},
                {new ExternalStatisticsReporting(instance)}
            };

        _workerTask = Task.Factory.StartNew(WorkerLoop, _workerCanceller.Token);
    }

    public void Dispose() {
        _workerCanceller.Cancel();
        _workerTask.Wait(5000);
        if (!_workerTask.IsCompleted)
            Instance.Log("Dispose", "Warning: Background worker has not yet stopped. Forcing its disposal.");
        _workerTask.Dispose();
        _workerCanceller.Dispose();
    }

    /// <summary>
    /// *The* background task for the shard.
    /// Executes service tasks and handles errors.
    /// </summary>
    private async Task WorkerLoop() {
        LastBackgroundRun = DateTimeOffset.UtcNow;
        try {
            while (!_workerCanceller.IsCancellationRequested) {
                await Task.Delay(Interval * 1000, _workerCanceller.Token).ConfigureAwait(false);

                // Skip this round of task execution if the client is not connected
                if (Instance.DiscordClient.ConnectionState != ConnectionState.Connected) continue;

                // Execute tasks sequentially
                _tickCount++;
                foreach (var service in _workers) {
                    CurrentExecutingService = service.GetType().Name;
                    try {
                        if (_workerCanceller.IsCancellationRequested) break;
                        await service.OnTick(_tickCount, _workerCanceller.Token);
                    } catch (Exception ex) when (ex is not
                                                    (TaskCanceledException or OperationCanceledException or ObjectDisposedException)) {
                        Instance.Log(CurrentExecutingService, ex.ToString());
                    }
                }
                CurrentExecutingService = null;
                LastBackgroundRun = DateTimeOffset.UtcNow;
            }
        } catch (TaskCanceledException) { }
    }
}
