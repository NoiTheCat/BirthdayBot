using BirthdayBot.Data;
using Discord.Interactions;

namespace BirthdayBot.ApplicationCommands;

/// <summary>
/// Only users not on the blocklist or affected by moderator mode may use the command.<br/>
/// This is used in the <see cref="BotModuleBase"/> base class. Manually using it anywhere else is unnecessary.
/// </summary>
class EnforceBlockingAttribute : PreconditionAttribute {
    public const string FailModerated = "Guild has moderator mode enabled.";
    public const string FailBlocked = "User is in the guild's block list.";
    public const string ReplyModerated = ":x: This bot is in moderated mode, preventing you from using any bot commands in this server.";
    public const string ReplyBlocked = ":x: You have been blocked from using bot commands in this server.";

    public override Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services) {
        // Not in guild context, unaffected by blocking
        if (context.Guild is not SocketGuild guild) return Task.FromResult(PreconditionResult.FromSuccess());

        // Manage Guild permission overrides any blocks
        var user = (SocketGuildUser)context.User;
        if (user.GuildPermissions.ManageGuild) return Task.FromResult(PreconditionResult.FromSuccess());

        using var db = new BotDatabaseContext();
        var settings = (from row in db.GuildConfigurations
                       where row.GuildId == guild.Id
                       select new { ModRole = row.ModeratorRole, ModMode = row.Moderated }).FirstOrDefault();
        if (settings != null) {
            // Bot moderators override all blocking measures in place
            if (user.Roles.Any(r => r.Id == settings.ModRole)) return Task.FromResult(PreconditionResult.FromSuccess());

            // Check for moderated mode
            if (settings.ModMode) return Task.FromResult(PreconditionResult.FromError(FailModerated));

            // Check if user exists in blocklist
            if (db.BlocklistEntries.Where(row => row.GuildId == guild.Id && row.UserId == user.Id).Any())
                return Task.FromResult(PreconditionResult.FromError(FailBlocked));
        }

        return Task.FromResult(PreconditionResult.FromSuccess());
    }
}