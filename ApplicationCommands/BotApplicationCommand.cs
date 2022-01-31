using BirthdayBot.Data;
using NodaTime;
using System.Collections.ObjectModel;

namespace BirthdayBot.ApplicationCommands;

/// <summary>
/// Base class for classes handling slash command execution.
/// </summary>
internal abstract class BotApplicationCommand {
    public delegate Task CommandResponder(ShardInstance instance, GuildConfiguration gconf, SocketSlashCommand arg);

    protected const string ErrGuildOnly = ":x: This command can only be run within a server.";
    protected string ErrNotAllowed = ":x: Only server moderators may use this command.";
    protected const string MemberCacheEmptyError = ":warning: Please try the command again.";
    public const string AccessDeniedError = ":warning: You are not allowed to run this command.";

    protected static ReadOnlyDictionary<string, string> TzNameMap { get; }

    /// <summary>
    /// Returns a list of application command definitions handled by the implementing class,
    /// for use when registering/updating this bot's available slash commands.
    /// </summary>
    public abstract IEnumerable<ApplicationCommandProperties> GetCommands();

    /// <summary>
    /// Given the command name, returns the designated handler to execute to fulfill the command.
    /// Returns null if this class does not contain a handler for the given command.
    /// </summary>
    public abstract CommandResponder? GetHandlerFor(string commandName);

    static BotApplicationCommand() {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in DateTimeZoneProviders.Tzdb.Ids) dict.Add(name, name);
        TzNameMap = new(dict);
    }

    /// <summary>
    /// Checks given time zone input. Returns a valid string for use with NodaTime,
    /// throwing a FormatException if the input is not recognized.
    /// </summary>
    protected static string ParseTimeZone(string tzinput) {
        if (!TzNameMap.TryGetValue(tzinput, out string? tz)) throw new FormatException(":x: Unexpected time zone name."
                     + $" Refer to `INSERT COMMAND NAME HERE` to help determine the correct value."); // TODO fix!!!!!!!!!!!!!!!!!!!
        return tz!;
    }

    /// <summary>
    /// An alternative to <see cref="SocketGuild.HasAllMembers"/> to be called by command handlers needing a full member cache.
    /// Creates a download request if necessary.
    /// </summary>
    /// <returns>
    /// True if the member cache is already filled, false otherwise.
    /// </returns>
    /// <remarks>
    /// Any updates to the member cache aren't accessible until the event handler finishes execution, meaning proactive downloading
    /// is necessary, and is handled by <seealso cref="BackgroundServices.AutoUserDownload"/>. In situations where
    /// this approach fails, this is to be called, and the user must be asked to attempt the command again if this returns false.
    /// </remarks>
    protected static async Task<bool> HasMemberCacheAsync(SocketGuild guild) {
        if (Common.HasMostMembersDownloaded(guild)) return true;
        // Event handling thread hangs if awaited normally or used with Task.Run
        await Task.Factory.StartNew(guild.DownloadUsersAsync).ConfigureAwait(false);
        return false;
    }
}
