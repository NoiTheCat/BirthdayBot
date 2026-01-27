using NodaTime;

namespace BirthdayBot.Data;

public class GuildConfig {
    public ulong GuildId { get; set; }

    public ulong? BirthdayRole { get; set; }

    public ulong? AnnouncementChannel { get; set; }

    public DateTimeZone? GuildTimeZone { get; set; }

    public string? AnnounceMessage { get; set; }

    public string? AnnounceMessagePl { get; set; }

    public bool AnnouncePing { get; set; }

    public Instant LastSeen { get; set; }

    public ICollection<UserEntry> UserEntries { get; set; } = null!;

    /// <summary>
    /// Gets if this instance is new and does not (yet) exist in the database.
    /// </summary>
    public bool IsNew { get; set; }
}
