using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BirthdayBot.Data;

[Table("banned_users")]
public class BlocklistEntry {
    [Key]
    public ulong GuildId { get; set; }
    [Key]
    public ulong UserId { get; set; }

    [ForeignKey(nameof(GuildConfig.GuildId))]
    [InverseProperty(nameof(GuildConfig.BlockedUsers))]
    public GuildConfig Guild { get; set; } = null!;
}
