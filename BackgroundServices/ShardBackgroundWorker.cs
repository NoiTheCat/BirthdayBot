using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BirthdayBot.BackgroundServices
{
    /// <summary>
    /// Handles the execution of periodic background tasks specific to each shard.
    /// </summary>
    class ShardBackgroundWorker : IDisposable
    {
        /// <summary>
        /// The interval, in seconds, in which background tasks are attempted to be run within a shard.
        /// </summary>
        public const int Interval = 40;

        private readonly Task _workerTask;
        private readonly CancellationTokenSource _workerCanceller;
        private readonly List<BackgroundService> _workers;
        private int _tickCount = -1;

        private ShardInstance Instance { get; }

        public BirthdayRoleUpdate BirthdayUpdater { get; }
        public SelectiveAutoUserDownload UserDownloader { get; }
        public DateTimeOffset LastBackgroundRun { get; private set; }
        public string? CurrentExecutingService { get; private set; }

        public ShardBackgroundWorker(ShardInstance instance)
        {
            Instance = instance;
            _workerCanceller = new CancellationTokenSource();

            BirthdayUpdater = new BirthdayRoleUpdate(instance);
            UserDownloader = new SelectiveAutoUserDownload(instance);
            _workers = new List<BackgroundService>()
            {
                {UserDownloader},
                {BirthdayUpdater},
                {new DataRetention(instance)},
                {new ExternalStatisticsReporting(instance)}
            };

            _workerTask = Task.Factory.StartNew(WorkerLoop, _workerCanceller.Token);
        }

        public void Dispose()
        {
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
        private async Task WorkerLoop()
        {
            LastBackgroundRun = DateTimeOffset.UtcNow;
            try
            {
                while (!_workerCanceller.IsCancellationRequested)
                {
                    await Task.Delay(Interval * 1000, _workerCanceller.Token).ConfigureAwait(false);

                    // Skip this round of task execution if the client is not connected
                    if (Instance.DiscordClient.ConnectionState != Discord.ConnectionState.Connected) continue;

                    // Execute tasks sequentially
                    foreach (var service in _workers)
                    {
                        CurrentExecutingService = service.GetType().Name;
                        try
                        {
                            if (_workerCanceller.IsCancellationRequested) break;
                            _tickCount++;
                            await service.OnTick(_tickCount, _workerCanceller.Token).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not TaskCanceledException)
                        {
                            // TODO webhook log
                            Instance.Log(nameof(WorkerLoop), $"{CurrentExecutingService} encountered an exception:\n" + ex.ToString());
                        }
                    }
                    CurrentExecutingService = null;
                    LastBackgroundRun = DateTimeOffset.UtcNow;
                }
            }
            catch (TaskCanceledException) { }
        }
    }
}
