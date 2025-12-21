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

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
        base.OnConfiguring(optionsBuilder);

        optionsBuilder
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention(); // <- requires package EFCore.NamingConventions
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<GuildConfig>(entity => {
            entity.HasKey(e => e.GuildId);

            entity.Property(e => e.GuildId).ValueGeneratedNever();
            entity.Property(e => e.LastSeen).HasDefaultValueSql("now()");

            entity.Ignore(e => e.IsNew);
        });

        modelBuilder.Entity<UserEntry>(entity => {
            entity.HasKey(e => new { e.GuildId, e.UserId });

            entity.Property(e => e.LastSeen).HasDefaultValueSql("now()");

            // Define relation and how to point to references.
            // Also enables cascade deletion
            entity.HasOne(d => d.Guild)
                .WithMany(p => p.UserEntries)
                .HasForeignKey(d => d.GuildId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Ignore(e => e.IsNew);
        });

        // Set *all* non-nullable bool types to default to false
        foreach (var e in modelBuilder.Model.GetEntityTypes()) {
            foreach (var p in e.GetProperties()) {
                if (p.ClrType == typeof(bool) && !p.IsNullable) {
                    p.SetDefaultValue(false);
                }
            }
        }
    }
}
