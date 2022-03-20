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
        var settings = guild.GetConfigOrNew(db);

        // Bot moderators override any blocks
        if (settings.ModeratorRole.HasValue && user.Roles.Any(r => r.Id == (ulong)settings.ModeratorRole.Value))
            return Task.FromResult(PreconditionResult.FromSuccess());

        // Moderated mode check
        if (settings.Moderated) return Task.FromResult(PreconditionResult.FromError(FailModerated));

        // Block list check
        var blockquery = db.BlocklistEntries.Where(entry => entry.GuildId == (long)guild.Id && entry.UserId == (long)user.Id);
        if (blockquery.Any()) return Task.FromResult(PreconditionResult.FromError(FailBlocked));

        return Task.FromResult(PreconditionResult.FromSuccess());
    }
}