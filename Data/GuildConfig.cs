namespace BirthdayBot.Data;
public class GuildConfig {
    public ulong GuildId { get; set; }

    public ulong? BirthdayRole { get; set; }

    public ulong? AnnouncementChannel { get; set; }

    public string? GuildTimeZone { get; set; }

    public string? AnnounceMessage { get; set; }

    public string? AnnounceMessagePl { get; set; }

    public bool AnnouncePing { get; set; }

    public DateTimeOffset LastSeen { get; set; }

    public bool EphemeralConfirm { get; set; }

    // Users associated with guild
    public ICollection<UserEntry> UserEntries { get; set; } = null!;

    /// <summary>
    /// Gets if this instance is new and does not (yet) exist in the database.
    /// This value is not in the database.
    /// </summary>
    public bool IsNew { get; set; }
}
