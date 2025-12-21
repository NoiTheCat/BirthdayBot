namespace BirthdayBot.Data;
public class UserEntry {
    // Composite PK: GuildId, UserId
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    
    public int BirthMonth { get; set; }
    
    public int BirthDay { get; set; }
    
    public string? TimeZone { get; set; }
    
    public DateTimeOffset LastSeen { get; set; }

    // Associated guild for this user
    public GuildConfig Guild { get; set; } = null!;

    /// <summary>
    /// Gets if this instance is new and does not (yet) exist in the database.
    /// This value is not in the database.
    /// </summary>
    public bool IsNew { get; set; }
}
