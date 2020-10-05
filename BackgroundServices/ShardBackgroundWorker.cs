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
        public const int Interval = 20;

        private readonly Task _workerTask;
        private readonly CancellationTokenSource _workerCanceller;
        private readonly List<BackgroundService> _workers;

        private ShardInstance Instance { get; }

        public ConnectionStatus ConnStatus { get; }
        public BirthdayRoleUpdate BirthdayUpdater { get; }
        public DateTimeOffset LastBackgroundRun { get; private set; }
        public int ConnectionScore => ConnStatus.Score;

        public ShardBackgroundWorker(ShardInstance instance)
        {
            Instance = instance;
            _workerCanceller = new CancellationTokenSource();

            ConnStatus = new ConnectionStatus(instance);
            BirthdayUpdater = new BirthdayRoleUpdate(instance);
            _workers = new List<BackgroundService>()
            {
                {BirthdayUpdater},
                {new StaleDataCleaner(instance)}
            };

            _workerTask = Task.Factory.StartNew(WorkerLoop, _workerCanceller.Token,
                                                TaskCreationOptions.LongRunning, TaskScheduler.Default);
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
        /// *The* background task. Executes service tasks and handles errors.
        /// </summary>
        private async Task WorkerLoop()
        {
            LastBackgroundRun = DateTimeOffset.UtcNow;
            try
            {
                while (!_workerCanceller.IsCancellationRequested)
                {
                    await Task.Delay(Interval * 1000, _workerCanceller.Token);

                    // Wait a while for a stable connection, the threshold for which is defined within ConnectionStatus.
                    await ConnStatus.OnTick(_workerCanceller.Token);
                    if (!ConnStatus.Stable) continue;

                    // Execute tasks sequentially
                    foreach (var service in _workers)
                    {
                        try
                        {
                            await service.OnTick(_workerCanceller.Token);
                        }
                        catch (Exception ex)
                        {
                            var svcname = service.GetType().Name;
                            if (ex is TaskCanceledException)
                            {
                                Instance.Log(nameof(WorkerLoop), $"{svcname} was interrupted by a cancellation request.");
                                throw;
                            }
                            else
                            {
                                // TODO webhook log
                                Instance.Log(nameof(WorkerLoop), $"{svcname} encountered an exception:\n" + ex.ToString());
                            }
                        }
                    }
                    LastBackgroundRun = DateTimeOffset.UtcNow;
                }
            }
            catch (TaskCanceledException) { }

            Instance.Log(nameof(WorkerLoop), "Background worker has concluded normally.");
        }
    }
}
