// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.DependencyInjection;
// using NoiPublicBot;
// using NoiPublicBot.BackgroundServices;

// namespace BirthdayBot.BackgroundServices;

// // Keeps track of known existing users. Removes old unused data
// class DataJanitor : BackgroundService {
//     private readonly int ProcessInterval;
//     private static readonly SemaphoreSlim _dbGate = new(3);

//     // Amount of days without updates before data is considered stale and up for deletion.
//     const int StaleUserThreashold = 90;

//     public DataJanitor()
//         => ProcessInterval = 10_800 / Instance.UserConfig.BackgroundInterval; // Process about once every two hours

//     public override async Task OnTick(int tickCount, CancellationToken token) {
//         if (tickCount % ProcessInterval != 0) return;

//         await _dbGate.WaitAsync(token);
//         try {
// #if DEBUG
//             // splitting this out as a separate method this way prevents from accidentally removing a
//             // 'using' statement up above for the millionth time...
//             await DebugBumpAsync(token);
// #pragma warning disable IDE0051
// #else
//             await RemoveStaleEntriesAsync(token);
// #endif
//         } finally {
//             try {
//                 _dbGate.Release();
//             } catch (ObjectDisposedException) { }
//         }
//     }

//     private async Task RemoveStaleEntriesAsync(CancellationToken token) {
//         using var db = BotDatabaseContext.New();

//         // Update guild users
//         var now = DateTimeOffset.UtcNow;
//         var cache = Shard.LocalServices.GetRequiredService<LocalCache>();
//         var updatedUsers = 0;
//         foreach (var guild in Shard.DiscordClient.Guilds) {
//             var local = cache.GetEntriesForGuild(guild.Id, false)
//                 .Select(e => e.UserId).ToList();

//             foreach (var queue in local.Chunk(1000)) {
//                 updatedUsers += await db.UserEntries
//                     .Where(gu => gu.GuildId == guild.Id)
//                     .Where(gu => local.Contains(gu.UserId))
//                     .ExecuteUpdateAsync(upd => upd.SetProperty(p => p.LastSeen, now), token);
//             }
//         }
//         Log($"Refreshed {updatedUsers} users.");

//         // And let go of old data
//         var staleUserCount = await db.UserEntries
//             .Where(gu => now - TimeSpan.FromDays(StaleUserThreashold) > gu.LastSeen)
//             .ExecuteDeleteAsync(token);
//         if (staleUserCount != 0) Log($"Discarded {staleUserCount} users across the whole database.");
//     }

//     private async Task DebugBumpAsync(CancellationToken token) {
//         using var db = BotDatabaseContext.New();
//         var now = DateTimeOffset.UtcNow;
//         await db.UserEntries.ExecuteUpdateAsync(upd => upd.SetProperty(p => p.LastSeen, now), token);
//         Log("DEBUG: Extended TTL of existing entries.");
//     }
// }

// /// <summary>
// /// Automatically removes database information for guilds/users that have not been accessed in a long time.
// /// </summary>
// /// #error needs review
// class DataRetention : BackgroundService {
//     private readonly int _interval;

//     // Amount of days without updates before data is considered stale and up for deletion.
//     const int StaleGuildThreshold = 180;
//     const int StaleUserThreashold = 360;

//     public DataRetention()
//         => _interval = 21600 / Instance.UserConfig.BackgroundInterval; // Process about once per six hours

//     public override async Task OnTick(int tickCount, CancellationToken token) {
//         // Run only a subset of shards each time, each running every ProcessInterval ticks.
//         if ((tickCount + Shard.ShardId) % _interval != 0) return;

//         try {
//             await DbAccessGate.WaitAsync(token);
//             await RemoveStaleEntriesAsync();
//         } finally {
//             try {
//                 DbAccessGate.Release();
//             } catch (ObjectDisposedException) { }
//         }
//     }

//     private async Task RemoveStaleEntriesAsync() {
//         using var db = new BotDatabaseContext();
//         var now = DateTimeOffset.UtcNow;

//         // Update guilds
//         var localGuilds = Shard.DiscordClient.Guilds.Select(g => g.Id).ToList();
//         var updatedGuilds = await db.GuildConfigurations
//             .Where(g => localGuilds.Contains(g.GuildId))
//             .ExecuteUpdateAsync(upd => upd.SetProperty(p => p.LastSeen, now));

//         // Update guild users
//         var updatedUsers = 0;
//         foreach (var guild in Shard.DiscordClient.Guilds) {
//             var localUsers = guild.Users.Select(u => u.Id).ToList();
//             updatedUsers += await db.UserEntries
//                 .Where(gu => gu.GuildId == guild.Id)
//                 .Where(gu => localUsers.Contains(gu.UserId))
//                 .ExecuteUpdateAsync(upd => upd.SetProperty(p => p.LastSeen, now));
//         }

//         // And let go of old data
//         var staleGuildCount = await db.GuildConfigurations
//             .Where(g => localGuilds.Contains(g.GuildId))
//             .Where(g => now - TimeSpan.FromDays(StaleGuildThreshold) > g.LastSeen)
//             .ExecuteDeleteAsync();
//         var staleUserCount = await db.UserEntries
//             .Where(gu => localGuilds.Contains(gu.GuildId))
//             .Where(gu => now - TimeSpan.FromDays(StaleUserThreashold) > gu.LastSeen)
//             .ExecuteDeleteAsync();

//         // Build report
//         var resultText = new StringBuilder();
//         resultText.Append($"Updated {updatedGuilds} guilds, {updatedUsers} users.");
//         if (staleGuildCount != 0 || staleUserCount != 0) {
//             resultText.Append(" Discarded ");
//             if (staleGuildCount != 0) {
//                 resultText.Append($"{staleGuildCount} guilds");
//                 if (staleUserCount != 0) resultText.Append(", ");
//             }
//             if (staleUserCount != 0) {
//                 resultText.Append($"{staleUserCount} users");
//             }
//             resultText.Append('.');
//         }
//         Log(resultText.ToString());
//     }
// }
