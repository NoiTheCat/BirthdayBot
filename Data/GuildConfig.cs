using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BirthdayBot.Data;

[Table("settings")]
public class GuildConfig {
    public GuildConfig() {
        BlockedUsers = new HashSet<BlocklistEntry>();
        UserEntries = new HashSet<UserEntry>();
    }

    [Key]
    [Column("guild_id")]
    public long GuildId { get; set; }
    [Column("role_id")]
    public long? RoleId { get; set; }
    [Column("channel_announce_id")]
    public long? ChannelAnnounceId { get; set; }
    [Column("time_zone")]
    public string? TimeZone { get; set; }
    [Column("moderated")]
    public bool Moderated { get; set; }
    [Column("moderator_role")]
    public long? ModeratorRole { get; set; }
    [Column("announce_message")]
    public string? AnnounceMessage { get; set; }
    [Column("announce_message_pl")]
    public string? AnnounceMessagePl { get; set; }
    [Column("announce_ping")]
    public bool AnnouncePing { get; set; }
    [Column("last_seen")]
    public DateTimeOffset LastSeen { get; set; }

    [InverseProperty(nameof(BlocklistEntry.Guild))]
    public ICollection<BlocklistEntry> BlockedUsers { get; set; }
    [InverseProperty(nameof(UserEntry.Guild))]
    public ICollection<UserEntry> UserEntries { get; set; }
}
