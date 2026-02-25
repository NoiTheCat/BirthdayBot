using BirthdayBot.BackgroundServices;
using BirthdayBot.Data;
using BirthdayBot.InteractionModules;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoiPublicBot;
using Npgsql;

namespace BirthdayBot;

public class ModuleConfig : ModuleConfigBase {
    public override string AppName => "BirthdayBot";

    public override ILocalizationManager? LocalizationManager
        => new ResxLocalizationManager("BirthdayBot.Localization.Commands", typeof(ModuleConfig).Assembly, [new("en"), new("es")]);

    public override IEnumerable<Type> BackgroundServices => [
        //typeof(DataJanitor),
        typeof(CachePreloader),
        typeof(BirthdayUpdater)
    ];

    public override void PreShardSetup(ref IServiceCollection services) {
        services.AddSingleton(s => new LocalCache(s.GetRequiredService<ShardInstance>()));
        services.AddDbContext<BotDatabaseContext>(opts =>
            opts.UseNpgsql(Instance.SqlConnectionString, pgopts => { pgopts.UseNodaTime(); })
            .UseSnakeCaseNamingConvention());
    }

    public override void PostShardSetup(ShardInstance shard) {
        shard.OnStatusCheck += () => {
            var c = shard.LocalServices.GetRequiredService<LocalCache>();
            return $"Cache: {c.GuildsCount:000} guilds -> {c.UsersCount:0000} users.";
        };
        shard.DiscordClient.ModalSubmitted += modal => {
            return ModalResponder.DiscordClient_ModalSubmitted(shard, modal);
        };
    }
}
