using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BirthdayBot.Data;
[Table("user_birthdays")]
public class UserEntry {
    [Key]
    public ulong GuildId { get; set; }
    [Key]
    public ulong UserId { get; set; }
    
    public int BirthMonth { get; set; }
    
    public int BirthDay { get; set; }
    
    public string? TimeZone { get; set; }
    
    public DateTimeOffset LastSeen { get; set; }

    [ForeignKey(nameof(GuildConfig.GuildId))]
    [InverseProperty(nameof(GuildConfig.UserEntries))]
    public GuildConfig Guild { get; set; } = null!;

    /// <summary>
    /// Gets if this instance is new and does not (yet) exist in the database.
    /// </summary>
    [NotMapped]
    public bool IsNew { get; set; }
}
