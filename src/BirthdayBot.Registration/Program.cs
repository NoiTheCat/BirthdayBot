using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorldTime;
using WorldTime.Caching;
using WorldTime.Data;

Console.WriteLine("Loading config");
var conf = new ConfigurationLoader(Environment.GetCommandLineArgs());

Console.WriteLine("Connecting to service");
var rest = new DiscordRestClient(new DiscordRestConfig {
    DefaultRetryMode = RetryMode.Retry502 | RetryMode.RetryTimeouts
});
rest.Log += arg => {
    Console.WriteLine($"[DiscordRestClient] {arg.Severity}: {arg.Message}");
    return Task.CompletedTask;
};
await rest.LoginAsync(TokenType.Bot, conf.Config.BotToken);
var appId = (await rest.GetApplicationInfoAsync()).Id;
Console.WriteLine($"Connected as application with ID {appId}");

Console.WriteLine("Interactions setup and module registration");
// Dummy entities - required for module loading
var services = new ServiceCollection()
    .AddSingleton(rest)
    .AddSingleton(s => new ShardInstance(s))
    .AddSingleton(new UserCache())
    .AddSingleton(new DiscordSocketClient())
    .AddDbContext<BotDatabaseContext>(options => options.UseNpgsql(conf.GetConnectionString()).UseSnakeCaseNamingConvention())
    .BuildServiceProvider();
var ia = new InteractionService(rest);
await ia.AddModulesAsync(Assembly.GetAssembly(typeof(ShardInstance)), services);

Console.WriteLine("Sending registration...");
await ia.RegisterCommandsGloballyAsync(true);
Console.WriteLine("All done! Give it about an hour to update globally.");

// Uncomment this to wipe all exsiting guild commands everywhere they may still exist
// Console.WriteLine("Wiping all guild-specific registrations...");
// var guilds = await rest.GetGuildsAsync();
// Console.WriteLine($"Found ${guilds.Count} guilds. Blindly sending requests for each...");
// foreach (var g in guilds) {
//     await rest.BulkOverwriteGuildCommands([], g.Id);
// }

Console.WriteLine("Disconnecting from service");
await rest.LogoutAsync();
