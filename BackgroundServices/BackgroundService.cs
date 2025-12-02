namespace BirthdayBot.BackgroundServices;
abstract class BackgroundService {
    /// <summary>
    /// Use to avoid excessive concurrent work on the database.
    /// </summary>
    protected static SemaphoreSlim DbAccessGate { get; private set; } = null!;

    protected ShardInstance Shard { get; }

    public BackgroundService(ShardInstance instance) {
        Shard = instance;
        DbAccessGate ??= new SemaphoreSlim(instance.Config.MaxConcurrentOperations);
    }

    protected void Log(string message) => Shard.Log(GetType().Name, message);

    public abstract Task OnTick(int tickCount, CancellationToken token);
}
