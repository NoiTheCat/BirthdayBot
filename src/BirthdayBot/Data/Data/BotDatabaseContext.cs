using Microsoft.EntityFrameworkCore;

namespace WorldTime.Data;

public sealed class BotDatabaseContext(DbContextOptions<BotDatabaseContext> options) : DbContext(options) {
    public DbSet<UserEntry> UserEntries { get; set; } = null!;
    public DbSet<GuildConfiguration> GuildSettings { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        // No foreign key references between the two. This is on purpose.
        modelBuilder.Entity<GuildConfiguration>(e => {
            e.HasKey(k => k.GuildId);
            e.Property(p => p.Use12HourTime).HasDefaultValue(false);
        });

        modelBuilder.Entity<UserEntry>(e => {
            e.HasKey(e => new { e.GuildId, e.UserId });
            e.HasIndex(c => c.GuildId);
            e.Property(p => p.LastSeen).HasDefaultValueSql("NOW()");
        });
    }

    /// <summary>
    /// Quick little thing to get an instance outside of DI.
    /// Assumes <see cref="NoiPublicBot.Instance"/> is initialized.
    /// </summary>
    internal static BotDatabaseContext New() {
        return new BotDatabaseContext(new DbContextOptionsBuilder<BotDatabaseContext>()
            .UseNpgsql(NoiPublicBot.Instance.SqlConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
    }
}
