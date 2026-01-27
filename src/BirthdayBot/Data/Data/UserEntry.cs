namespace WorldTime.Data;

public class UserEntry {
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }

    public string TimeZone { get; set; } = null!;

    public DateTimeOffset LastSeen { get; set; }
}