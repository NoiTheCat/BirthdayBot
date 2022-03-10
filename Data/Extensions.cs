namespace BirthdayBot.Data;

internal static class Extensions {
    /// <summary>
    /// Retrieves the database-backed bot configuration for this guild.
    /// </summary>
    internal static async Task<GuildConfiguration> GetConfigAsync(this SocketGuild guild)
        => await GuildConfiguration.LoadAsync(guild.Id, false);

    /// <summary>
    /// Retrieves a collection of all existing user configurations for this guild.
    /// </summary>
    internal static async Task<IEnumerable<GuildUserConfiguration>> GetUserConfigurationsAsync(this SocketGuild guild)
        => await GuildUserConfiguration.LoadAllAsync(guild.Id);

    /// <summary>
    /// Retrieves the database-backed bot configuration (birthday info) for this guild user.
    /// </summary>
    internal static async Task<GuildUserConfiguration> GetConfigAsync(this SocketGuildUser user)
        => await GuildUserConfiguration.LoadAsync(user.Guild.Id, user.Id);
}
