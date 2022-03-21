using Microsoft.EntityFrameworkCore;

namespace BirthdayBot.Data;

public class BotDatabaseContext : DbContext {
    private static string? _npgsqlConnectionString;
    internal static string NpgsqlConnectionString {
#if DEBUG
        get {
            if (_npgsqlConnectionString != null) return _npgsqlConnectionString;
            Program.Log(nameof(BotDatabaseContext), "Using hardcoded connection string!");
            return _npgsqlConnectionString ?? "Host=localhost;Username=birthdaybot;Password=bb";
        }
#else
        get => _npgsqlConnectionString!;
#endif
        set => _npgsqlConnectionString ??= value;
    }

    public DbSet<BlocklistEntry> BlocklistEntries { get; set; } = null!;
    public DbSet<GuildConfig> GuildConfigurations { get; set; } = null!;
    public DbSet<UserEntry> UserEntries { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
         => optionsBuilder
            .UseNpgsql(NpgsqlConnectionString)
#if DEBUG
            .LogTo((string line) => Program.Log("EF", line), Microsoft.Extensions.Logging.LogLevel.Information)
#endif
            .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        modelBuilder.Entity<BlocklistEntry>(entity => {
            entity.HasKey(e => new { e.GuildId, e.UserId })
                .HasName("banned_users_pkey");

            entity.HasOne(d => d.Guild)
                .WithMany(p => p.BlockedUsers)
                .HasForeignKey(d => d.GuildId)
                .HasConstraintName("banned_users_guild_id_fkey");
        });

        modelBuilder.Entity<GuildConfig>(entity => {
            entity.HasKey(e => e.GuildId)
                .HasName("settings_pkey");

            entity.Property(e => e.GuildId).ValueGeneratedNever();

            entity.Property(e => e.LastSeen).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<UserEntry>(entity => {
            entity.HasKey(e => new { e.GuildId, e.UserId })
                .HasName("user_birthdays_pkey");

            entity.Property(e => e.LastSeen).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Guild)
                .WithMany(p => p.UserEntries)
                .HasForeignKey(d => d.GuildId)
                .HasConstraintName("user_birthdays_guild_id_fkey");
        });
    }
}
