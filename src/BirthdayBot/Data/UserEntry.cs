using NodaTime;

namespace BirthdayBot.Data;

public class UserEntry {
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    
    public DateOnly BirthDate { get; set; }
    
    public DateTimeZone? TimeZone { get; set; }
    
    public DateTimeOffset LastSeen { get; set; }

    public GuildConfig Guild { get; set; } = null!;

    /// <summary>
    /// Gets if this instance is new and does not (yet) exist in the database.
    /// </summary>
    public bool IsNew { get; set; }
}
