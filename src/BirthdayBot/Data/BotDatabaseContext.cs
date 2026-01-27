using Microsoft.EntityFrameworkCore;

namespace BirthdayBot.Data;

public sealed class BotDatabaseContext(DbContextOptions<BotDatabaseContext> options) : DbContext(options) {
    public DbSet<GuildConfig> GuildConfigurations { get; set; } = null!;
    public DbSet<UserEntry> UserEntries { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        modelBuilder.Entity<GuildConfig>(entity => {
            entity.HasKey(e => e.GuildId)
                .HasName("settings_pkey");

            entity.Property(e => e.GuildId).ValueGeneratedNever();

            entity.Property(e => e.LastSeen).HasDefaultValueSql("NOW()");
        });

        modelBuilder.Entity<UserEntry>(entity => {
            entity.HasKey(e => new { e.GuildId, e.UserId })
                .HasName("user_birthdays_pkey");

            entity.Property(e => e.LastSeen).HasDefaultValueSql("NOW()");

            entity.HasOne(d => d.Guild)
                .WithMany(p => p.UserEntries)
                .HasForeignKey(d => d.GuildId)
                .HasConstraintName("user_birthdays_guild_id_fkey")
                .OnDelete(DeleteBehavior.Cascade);
        });
        // TODO remove custom names, double-check model properties, remove attributes in entity classes
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
