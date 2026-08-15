using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using NoiPublicBot;
using Npgsql;

namespace BirthdayBot.Data;

internal static class Extensions
{
    extension(SocketGuild guild)
    {
        /// <summary>
        /// Gets the corresponding <see cref="GuildConfig"/> for this guild, or a new one if one does not exist.
        /// If it doesn't exist in the database, <see cref="GuildConfig.IsNew"/> returns true.
        /// </summary>
        public async Task<GuildConfig> GetConfigOrNewAsync(BotDatabaseContext db)
        {
            var c = await db.GuildConfigurations
                .Where(g => g.GuildId == guild.Id)
                .FirstOrDefaultAsync();
            return c ?? new GuildConfig() { IsNew = true, GuildId = guild.Id };
        }
    }

    extension(SocketGuildUser user)
    {
        /// <summary>
        /// Gets the corresponding <see cref="UserEntry"/> for this user in this guild, or a new one if one does not exist.
        /// If it doesn't exist in the database, <see cref="UserEntry.IsNew"/> returns true.
        /// </summary>
        public async Task<UserEntry> GetUserEntryOrNewAsync(BotDatabaseContext db)
        {
            var u = await db.UserEntries
                .Where(u => u.GuildId == user.Guild.Id)
                .Where(u => u.UserId == user.Id)
                .SingleOrDefaultAsync();
            return u ?? new UserEntry() { IsNew = true, GuildId = user.Guild.Id, UserId = user.Id };
        }
    }

    private static DbContextOptions<BotDatabaseContext> ContextOptions
    {
        get
        {
            field ??= new DbContextOptionsBuilder<BotDatabaseContext>()
                .UseNpgsql(Instance.SqlConnectionString, opts => opts.UseNodaTime()).Options;
            return field;
        }
    }
    extension(BotDatabaseContext ctx)
    {
        /// <summary>
        /// Quick little thing to get an instance outside of DI.
        /// Assumes <see cref="NoiPublicBot.Instance"/> is initialized.
        /// </summary>
        public static BotDatabaseContext New() => new(ContextOptions);
    }
}
