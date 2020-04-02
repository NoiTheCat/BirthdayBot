using BirthdayBot.BackgroundServices;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BirthdayBot
{
    /// <summary>
    /// Handles the execution of periodic background tasks.
    /// </summary>
    class BackgroundServiceRunner
    {
        const int Interval = 8 * 60; // Tick interval in seconds. Adjust as needed.

        private List<BackgroundService> _workers;
        private readonly CancellationTokenSource _workerCancel;
        private Task _workerTask;

        internal BirthdayRoleUpdate BirthdayUpdater { get; }

        public BackgroundServiceRunner(BirthdayBot instance)
        {
            _workerCancel = new CancellationTokenSource();
            BirthdayUpdater = new BirthdayRoleUpdate(instance);
            _workers = new List<BackgroundService>()
            {
                {new GuildStatistics(instance)},
                {new Heartbeat(instance)},
                {BirthdayUpdater}
            };
        }

        public void Start()
        {
            _workerTask = Task.Factory.StartNew(WorkerLoop, _workerCancel.Token,
                                                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public async Task Cancel()
        {
            _workerCancel.Cancel();
            await _workerTask;
        }

        /// <summary>
        /// *The* background task. Executes service tasks and handles errors.
        /// </summary>
        private async Task WorkerLoop()
        {
            while (!_workerCancel.IsCancellationRequested)
            {
                try
                {
                    // Delay a bit before we start (or continue) work.
                    await Task.Delay(Interval * 1000, _workerCancel.Token);

                    // Execute background tasks.
                    var tasks = new List<Task>();
                    foreach (var service in _workers)
                    {
                        tasks.Add(service.OnTick());
                    }
                    await Task.WhenAll(tasks);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Program.Log("Background task", "Unhandled exception during background task execution:");
                    Program.Log("Background task", ex.ToString());
                }
            }
        }
    }
}
