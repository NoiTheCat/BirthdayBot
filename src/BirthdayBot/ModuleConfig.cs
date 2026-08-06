using BirthdayBot.BackgroundServices;
using BirthdayBot.Data;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoiPublicBot;
using NoiPublicBot.Common.UserCache;
using Npgsql;
using Serilog.Events;

namespace BirthdayBot;

public class ModuleConfig : ModuleConfigBase {
    public override IEnumerable<Type> BackgroundServices => [
        //typeof(DataJanitor),
        typeof(CachePreloader),
        typeof(BirthdayUpdater)
    ];

    public override void PreShardSetup(ref IServiceCollection services) {
        services.AddSingleton(
            s => new UserCache<BotDatabaseContext>(s.GetRequiredService<ShardInstance>(),
                                                   new EFWarmCacheProvider(BotDatabaseContext.New)));
        services.AddDbContext<BotDatabaseContext>(opts => opts
            .UseNpgsql(Instance.SqlConnectionString,
            npgopts => npgopts.UseNodaTime())
            .UseSnakeCaseNamingConvention());
    }

    public override IEnumerable<(LogEventLevel log, string message, object?[]? propertyValues)> StatusMessages(ShardInstance shard) {
        var c = shard.LocalServices.GetRequiredService<UserCache<BotDatabaseContext>>();
        return [(LogEventLevel.Information, "Cache[g:{CachedGuildsCount:000} u:{CachedUsersCount:0000}]", [c.GuildsCount, c.UsersCount])];
    }

    public override ILocalizationManager? LocalizationManager
        => new JsonLocalizationManager("Localization", "Commands");

    public override Func<string, string> GenericErrorProvider
        => loc => Localization.StringProviders.Responses.Get(loc, "errGeneric");

    public override DbContext? StartupMigrationsDbContext => BotDatabaseContext.New();
}
