using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BirthdayBot.Data;
public class BotDatabaseContext : DbContext {
    private static readonly string _connectionString;

    static BotDatabaseContext() {
        // Get our own config loaded just for the SQL stuff
        var conf = new Configuration();
        _connectionString = new NpgsqlConnectionStringBuilder() {
            Host = conf.SqlHost ?? "localhost", // default to localhost
            Database = conf.SqlDatabase,
            Username = conf.SqlUsername,
            Password = conf.SqlPassword,
            ApplicationName = conf.SqlApplicationName
        }.ToString();
    }

    public DbSet<GuildConfig> GuildConfigurations { get; set; } = null!;
    public DbSet<UserEntry> UserEntries { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
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
                .HasConstraintName("user_birthdays_guild_id_fkey")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
