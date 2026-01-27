using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BirthdayBot.Data;
[Table("settings")]
public class GuildConfig {
    [Key]
    public ulong GuildId { get; set; }

    [Column("role_id")]
    public ulong? BirthdayRole { get; set; }

    [Column("channel_announce_id")]
    public ulong? AnnouncementChannel { get; set; }

    [Column("time_zone")]
    public string? GuildTimeZone { get; set; }

    public string? AnnounceMessage { get; set; }

    public string? AnnounceMessagePl { get; set; }

    public bool AnnouncePing { get; set; }

    public DateTimeOffset LastSeen { get; set; }

    [InverseProperty(nameof(UserEntry.Guild))]
    public ICollection<UserEntry> UserEntries { get; set; } = null!;

    /// <summary>
    /// Gets if this instance is new and does not (yet) exist in the database.
    /// </summary>
    [NotMapped]
    public bool IsNew { get; set; }
}
