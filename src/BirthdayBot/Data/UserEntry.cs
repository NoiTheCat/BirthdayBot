using NodaTime;

namespace BirthdayBot.Data;

public class UserEntry {
    // Composite PK: GuildId, UserId
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    
    public LocalDate BirthDate { get; set; }
    public DateTimeZone? TimeZone { get; set; }
    
    /// <summary>
    /// To measure the entry's TTL.
    /// </summary>
    public Instant LastSeen { get; set; }
    /// <summary>
    /// The last time that <see cref="BackgroundServices.BirthdayUpdater"/> acted on this entry in a meaningful way.
    /// </summary>
    public Instant LastProcessed { get; set; }

    // Associated guild for this user
    public GuildConfig Guild { get; set; } = null!;

    /// <summary>
    /// Gets if this instance is new and does not (yet) exist in the database.
    /// This value is not in the database.
    /// </summary>
    public bool IsNew { get; set; }
}
