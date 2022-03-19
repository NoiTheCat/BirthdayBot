using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BirthdayBot.Data;

[Table("banned_users")]
public class BlocklistEntry {
    [Key]
    [Column("guild_id")]
    public long GuildId { get; set; }
    [Key]
    [Column("user_id")]
    public long UserId { get; set; }

    [ForeignKey(nameof(GuildConfig.GuildId))]
    [InverseProperty(nameof(GuildConfig.BlockedUsers))]
    public GuildConfig Guild { get; set; } = null!;
}
