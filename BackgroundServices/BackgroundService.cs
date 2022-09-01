namespace BirthdayBot.BackgroundServices;
abstract class BackgroundService {
    protected static SemaphoreSlim DbConcurrentOperationsLock { get; } = new(ShardManager.MaxConcurrentOperations);
    protected ShardInstance ShardInstance { get; }

    public BackgroundService(ShardInstance instance) => ShardInstance = instance;

    protected void Log(string message) => ShardInstance.Log(GetType().Name, message);

    public abstract Task OnTick(int tickCount, CancellationToken token);
}
