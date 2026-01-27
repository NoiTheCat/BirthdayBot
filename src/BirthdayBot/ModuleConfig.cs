using BirthdayBot.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoiPublicBot;
using WorldTime;
using WorldTime.BackgroundServices;

namespace BirthdayBot;

public class ModuleConfig : ModuleConfigBase {
    public override string AppName => "BirthdayBot";

    public override IEnumerable<Type> BackgroundServices => [
        typeof(DataJanitor),
        typeof(CacheRefresher)
    ];

    public override void PreShardSetup(ref IServiceCollection services) {
        services.AddSingleton(s => new LocalCache(s.GetRequiredService<ShardInstance>()));
        services.AddDbContext<BotDatabaseContext>(opts =>
            opts.UseNpgsql(Instance.SqlConnectionString)
            .UseSnakeCaseNamingConvention());
    }

    public override void PostShardSetup(ShardInstance shard) {
        shard.OnStatusCheck += () => {
            var c = shard.LocalServices.GetRequiredService<LocalCache>();
            return $"Cache: {c.GuildsCount:000} guilds -> {c.UsersCount:0000} users.";
        };
    }
}
