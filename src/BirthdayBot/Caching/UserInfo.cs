using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace WorldTime.Caching;

public sealed record UserInfo {
    private static readonly TimeSpan MinimumTTL = TimeSpan.FromMinutes(360);
    private const int TTLAdjustAddMaxMinutes = 1440;
    private const double TTLAdjustWhenNull = 0.3;

    public ulong GuildId { get; private init; }
    public ulong UserId { get; private init; }

    [MemberNotNullWhen(false, nameof(IsNull))]
    public string? Username { get; private init; }
    public string? GlobalName { get; private init; }
    public string? GuildNickname { get; private init; }

    public DateTimeOffset EntryTTL { get; private init; }
    /// <summary>
    /// Gets if the entry is marked as null. This marks a cache miss and serves as a
    /// means to track users in cache that could not be retrieved on Discord.
    /// </summary>
    public bool IsNull { get; private init; }

    public static UserInfo CreateFrom(IGuildUser user) => new() {
        GuildId = user.GuildId,
        UserId = user.Id,
        Username = user.Username,
        GlobalName = user.GlobalName,
        GuildNickname = user.Nickname,

        EntryTTL = DateTimeOffset.UtcNow + CalculateJitter(),
        IsNull = false
    };

    public static UserInfo NullFrom(ulong guildId, ulong userId) => new() {
        GuildId = guildId,
        UserId = userId,

        // Null results get a slightly higher lifetime
        EntryTTL = DateTimeOffset.UtcNow + (CalculateJitter() * TTLAdjustWhenNull),
        IsNull = true
    };

    /// <summary>
    /// Formats this user's name to a consistent, readable format that prioritizes the nickname or global name.
    /// </summary>
    public string FormatName() {
        if (IsNull) throw new InvalidOperationException("This entry is incomplete and must be considered effectively null.");
        static string escapeFormattingCharacters(string input) {
            var result = new StringBuilder();
            foreach (var c in input) {
                if (c is '\\' or '_' or '~' or '*' or '@' or '`') {
                    result.Append('\\');
                }
                result.Append(c);
            }
            return result.ToString();
        }
        var username = escapeFormattingCharacters(GlobalName ?? Username!);
        if (GuildNickname != null) {
            return $"{escapeFormattingCharacters(GuildNickname)} ({username})";
        }
        return username;
    }

    private static TimeSpan CalculateJitter() {
        var jitter = Program.JitterSource.Value!.Next(TTLAdjustAddMaxMinutes);
        return MinimumTTL + TimeSpan.FromMinutes(jitter);
    }
}
