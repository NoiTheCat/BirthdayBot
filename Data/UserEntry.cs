using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BirthdayBot.Data;

[Table("user_birthdays")]
public class UserEntry {
    [Key]
    [Column("guild_id")]
    public long GuildId { get; set; }
    [Key]
    [Column("user_id")]
    public long UserId { get; set; }
    [Column("birth_month")]
    public int BirthMonth { get; set; }
    [Column("birth_day")]
    public int BirthDay { get; set; }
    [Column("time_zone")]
    public string? TimeZone { get; set; }
    [Column("last_seen")]
    public DateTimeOffset LastSeen { get; set; }

    [ForeignKey(nameof(GuildConfig.GuildId))]
    [InverseProperty(nameof(GuildConfig.UserEntries))]
    public GuildConfig Guild { get; set; } = null!;
}
