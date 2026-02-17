using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoiPublicBot;
using NoiPublicBot.Config;
using Npgsql;
using BirthdayBot;
using BirthdayBot.Data;

// Usage:
// dotnet run --project src/BirthdayBot.Registration/BirthdayBot.Registration.csproj -c CONFIGTYPE -- -c path/to/settings.json
// Where CONFIGTYPE is either
//   Debug - Updates commands locally
//   Release - Updates commands globally

Console.WriteLine("Loading config");
var conf = Loader.LoadAppConfiguration(args);

Console.WriteLine("Connecting to service");
var rest = new DiscordRestClient(new DiscordRestConfig {
    DefaultRetryMode = RetryMode.Retry502 | RetryMode.RetryTimeouts
});
rest.Log += arg => {
    Console.WriteLine($"[DiscordRestClient] {arg.Severity}: {arg.Message}");
    return Task.CompletedTask;
};
await rest.LoginAsync(TokenType.Bot, conf.BotToken);
var appId = (await rest.GetApplicationInfoAsync()).Id;
Console.WriteLine($"Connected as application with ID {appId}");

Console.WriteLine("Interactions setup and module registration");
// Dummy entities - required for module loading
var services = new ServiceCollection()
    .AddSingleton(rest)
    .AddSingleton(s => new ShardInstance(s))
    .AddSingleton(s => new LocalCache(s.GetRequiredService<ShardInstance>()))
    .AddSingleton(new DiscordSocketClient())
    .AddDbContext<BotDatabaseContext>(options => options
        .UseNpgsql(new NpgsqlConnectionStringBuilder() {
            Host = conf.Database.Host,
            Database = conf.Database.Database,
            Username = conf.Database.Username,
            Password = conf.Database.Password
        }.ConnectionString, pgopts => { pgopts.UseNodaTime(); })
        .UseSnakeCaseNamingConvention())
    .BuildServiceProvider();
var ia = new InteractionService(rest);
await ia.AddModulesAsync(Assembly.GetAssembly(typeof(ModuleConfig)), services);
Console.WriteLine();
Console.WriteLine("Found modules: " + string.Join(' ', ia.Modules.Select(m => m.Name)));
Console.WriteLine("Found slash commands: " + string.Join(' ', ia.SlashCommands.Select(m => m.Name)));
Console.WriteLine("Found modals: " + string.Join(' ', ia.Modals.Select(m => m.Title)));
Console.WriteLine("Found modal commands: " + string.Join(' ', ia.ModalCommands.Select(m => m.Name)));
Console.WriteLine("Found context commands: " + string.Join(' ', ia.ContextCommands.Select(m => m.Name)));
Console.WriteLine("Found component commands: " + string.Join(' ', ia.ComponentCommands.Select(m => m.Name)));

Console.WriteLine();

#if DEBUG
Console.WriteLine("DEBUG variable was found. Commands will be updated locally to all joined guilds.");
Console.WriteLine("Press enter to continue, otherwise quit now.");
Console.ReadLine();

Console.WriteLine("Sending registrations...");
foreach (var g in await rest.GetGuildsAsync()) {
    await ia.RegisterCommandsToGuildAsync(g.Id, true);
    Console.WriteLine($"    ✔ To {g.Id}: {g.Name}");
}

// (Un)comment to remove all global registrations
Console.WriteLine("Removing global registration");
await rest.DeleteAllGlobalCommandsAsync();

#else
Console.WriteLine("This is the Release configuration. Commands will be updated globally.");
Console.WriteLine("Press enter to continue, otherwise quit now.");
Console.ReadLine();

Console.WriteLine("Sending registration...");
await ia.RegisterCommandsGloballyAsync(true);
Console.WriteLine("All done! Give it about an hour to update globally.");
#endif

// Uncomment this to wipe all exsiting guild commands everywhere they may still exist
// Console.WriteLine("Wiping all guild-specific registrations...");
// var guilds = await rest.GetGuildsAsync();
// Console.WriteLine($"Found ${guilds.Count} guilds. Blindly sending requests for each...");
// foreach (var g in guilds) {
//     await rest.BulkOverwriteGuildCommands([], g.Id);
// }



Console.WriteLine("Disconnecting from service");
await rest.LogoutAsync();
