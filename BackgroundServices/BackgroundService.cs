namespace BirthdayBot.BackgroundServices;
abstract class BackgroundService {
    protected static SemaphoreSlim ConcurrentSemaphore { get; private set; } = null!;

    protected ShardInstance Shard { get; }

    public BackgroundService(ShardInstance instance) {
        Shard = instance;
        ConcurrentSemaphore ??= new SemaphoreSlim(instance.Config.MaxConcurrentOperations);
    }

    protected void Log(string message) => Shard.Log(GetType().Name, message);

    public abstract Task OnTick(int tickCount, CancellationToken token);
}
