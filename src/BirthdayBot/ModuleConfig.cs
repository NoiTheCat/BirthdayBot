using BirthdayBot.BackgroundServices;
using BirthdayBot.Data;
using BirthdayBot.InteractionModules;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoiPublicBot;
using NoiPublicBot.Common;
using Npgsql;

namespace BirthdayBot;

public class ModuleConfig : ModuleConfigBase {
    public override IEnumerable<Type> BackgroundServices => [
        //typeof(DataJanitor),
        typeof(CachePreloader),
        typeof(BirthdayUpdater)
    ];

    public override void PreShardSetup(ref IServiceCollection services) {
        services.AddSingleton(s => new UserCache<BotDatabaseContext>(s.GetRequiredService<ShardInstance>()));
        services.AddDbContext<BotDatabaseContext>(opts => opts
            .UseNpgsql(Instance.SqlConnectionString.ConnectionString,
            npgopts => npgopts.UseNodaTime())
            .UseSnakeCaseNamingConvention());
    }

    public override void PostShardSetup(ShardInstance shard) {
        shard.OnStatusCheck += () => {
            var c = shard.LocalServices.GetRequiredService<UserCache<BotDatabaseContext>>();
            return $"Cache: {c.GuildsCount:000} guilds -> {c.UsersCount:0000} users.";
        };
        shard.DiscordClient.ModalSubmitted += modal => {
            return ModalResponder.DiscordClient_ModalSubmitted(shard, modal);
        };
    }

    public override ILocalizationManager? LocalizationManager
        => new JsonLocalizationManager("Localization", "Commands");

    public override Func<string, string> GenericErrorProvider
        => loc => Localization.StringProviders.Responses.Get(loc, "errGeneric");
}
