using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BirthdayBot.Data;

internal static class Extensions {
    /// <summary>
    /// Retrieves the database-backed guild configuration for the executing guild.
    /// </summary>
    internal static async Task<GuildConfiguration> GetConfigAsync(this SocketGuild guild)
        => await GuildConfiguration.LoadAsync(guild.Id, false);

    /// <summary>
    /// Retrieves the database-backed guild user configuration for this user.
    /// </summary>
    internal static async Task<GuildUserConfiguration> GetConfigAsync(this SocketGuildUser user)
        => await GuildUserConfiguration.LoadAsync(user.Guild.Id, user.Id);
}
