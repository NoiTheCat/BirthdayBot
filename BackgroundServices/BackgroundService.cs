namespace BirthdayBot.BackgroundServices;

abstract class BackgroundService {
    protected ShardInstance ShardInstance { get; }

    public BackgroundService(ShardInstance instance) => ShardInstance = instance;

    protected void Log(string message) => ShardInstance.Log(GetType().Name, message);

    public abstract Task OnTick(int tickCount, CancellationToken token);
}
