using BirthdayBot.Data;
using NodaTime;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace BirthdayBot.UserInterface;

/// <summary>
/// Common base class for common constants and variables.
/// </summary>
internal abstract class CommandsCommon {
#if DEBUG
    public const string CommandPrefix = "bt.";
#else
        public const string CommandPrefix = "bb.";
#endif
    public const string BadUserError = ":x: Unable to find user. Specify their `@` mention or their ID.";
    public const string ParameterError = ":x: Invalid usage. Refer to how to use the command and try again.";
    public const string NoParameterError = ":x: This command does not accept any parameters.";
    public const string InternalError = ":x: An unknown error occurred. If it persists, please notify the bot owner.";
    public const string MemberCacheEmptyError = ":warning: Please try the command again.";

    public delegate Task CommandHandler(ShardInstance instance, GuildConfiguration gconf,
                                        string[] param, SocketTextChannel reqChannel, SocketGuildUser reqUser);

    protected static ReadOnlyDictionary<string, string> TzNameMap { get; }
    protected static Regex ChannelMention { get; } = new Regex(@"<#(\d+)>");
    protected static Regex UserMention { get; } = new Regex(@"\!?(\d+)>");

    protected Configuration BotConfig { get; }

    protected CommandsCommon(Configuration db) {
        BotConfig = db;
    }

    static CommandsCommon() {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in DateTimeZoneProviders.Tzdb.Ids) dict.Add(name, name);
        TzNameMap = new(dict);
    }

    /// <summary>
    /// On command dispatcher initialization, it will retrieve all available commands through here.
    /// </summary>
    public abstract IEnumerable<(string, CommandHandler)> Commands { get; }

    /// <summary>
    /// Checks given time zone input. Returns a valid string for use with NodaTime.
    /// </summary>
    protected static string ParseTimeZone(string tzinput) {
        if (tzinput.Equals("Asia/Calcutta", StringComparison.OrdinalIgnoreCase)) tzinput = "Asia/Kolkata";
        if (!TzNameMap.TryGetValue(tzinput, out string? tz)) throw new FormatException(":x: Unexpected time zone name."
                     + $" Refer to `{CommandPrefix}help-tzdata` to help determine the correct value.");
        return tz;
    }

    /// <summary>
    /// Given user input where a user-like parameter is expected, attempts to resolve to an ID value.
    /// Input must be a mention or explicit ID. No name resolution is done here.
    /// </summary>
    protected static bool TryGetUserId(string input, out ulong result) {
        string doParse;
        var m = UserMention.Match(input);
        if (m.Success) doParse = m.Groups[1].Value;
        else doParse = input;

        if (ulong.TryParse(doParse, out ulong resultVal)) {
            result = resultVal;
            return true;
        }

        result = default;
        return false;
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
