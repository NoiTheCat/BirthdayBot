namespace BirthdayBot.Data;
internal static class Extensions {
    /// <summary>
    /// Gets the corresponding <see cref="GuildConfig"/> for this guild, or a new one if one does not exist.
    /// If it doesn't exist in the database, <see cref="GuildConfig.IsNew"/> returns true.
    /// </summary>
    public static GuildConfig GetConfigOrNew(this SocketGuild guild, BotDatabaseContext db)
        => db.GuildConfigurations.Where(g => g.GuildId == guild.Id).FirstOrDefault() 
            ?? new GuildConfig() { IsNew = true, GuildId = guild.Id };

    /// <summary>
    /// Gets the corresponding <see cref="UserEntry"/> for this user in this guild, or a new one if one does not exist.
    /// If it doesn't exist in the database, <see cref="UserEntry.IsNew"/> returns true.
    /// </summary>
    public static UserEntry GetUserEntryOrNew(this SocketGuildUser user, BotDatabaseContext db)
        => db.UserEntries.Where(u => u.GuildId == user.Guild.Id && u.UserId == user.Id).FirstOrDefault()
            ?? new UserEntry() { IsNew = true, GuildId = user.Guild.Id, UserId = user.Id };
}