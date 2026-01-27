using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace BirthdayBot.Data;

public sealed class BotDatabaseContext(DbContextOptions<BotDatabaseContext> options) : DbContext(options) {
    public DbSet<GuildConfig> GuildConfigurations { get; set; } = null!;
    public DbSet<UserEntry> UserEntries { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        modelBuilder.Entity<GuildConfig>(entity => {
            entity.HasKey(e => e.GuildId);
            entity.Property(e => e.GuildId).ValueGeneratedNever();
            entity.Property(e => e.LastSeen).HasDefaultValueSql("NOW()");
            entity.Property(e => e.GuildTimeZone)
                .HasConversion(
                    enval => enval == null ? null : enval.Id,
                    dbstr => dbstr == null ? null : DateTimeZoneProviders.Tzdb[dbstr]
                );
            entity.Ignore(e => e.IsNew);
        });

        modelBuilder.Entity<UserEntry>(entity => {
            entity.HasKey(e => new { e.GuildId, e.UserId });
            entity.HasOne(d => d.Guild)
                .WithMany(p => p.UserEntries)
                .HasForeignKey(d => d.GuildId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.LastSeen).HasDefaultValueSql("NOW()");
            entity.Property(e => e.TimeZone)
                .HasConversion(
                    enval => enval == null ? null : enval.Id,
                    dbstr => dbstr == null ? null : DateTimeZoneProviders.Tzdb[dbstr]
                );
            entity.Ignore(e => e.IsNew);
        });

        
    }

    /// <summary>
    /// Quick little thing to get an instance outside of DI.
    /// Assumes <see cref="NoiPublicBot.Instance"/> is initialized.
    /// </summary>
    internal static BotDatabaseContext New() {
        return new BotDatabaseContext(new DbContextOptionsBuilder<BotDatabaseContext>()
            .UseNpgsql(NoiPublicBot.Instance.SqlConnectionString, pgopts => { pgopts.UseNodaTime(); })
            .UseSnakeCaseNamingConvention()
            .Options);
    }
}
