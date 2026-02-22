using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NoiPublicBot.Config;
using Npgsql;

namespace BirthdayBot.Data;

public class DesignTimeFactory : IDesignTimeDbContextFactory<BotDatabaseContext> {
    // Used by EF Core tools for migrations, etc.
    public BotDatabaseContext CreateDbContext(string[] args) {
        var conf = Loader.LoadAppConfiguration(args).Database;
        var connstr = new NpgsqlConnectionStringBuilder() {
            Host = conf.Host,
            Database = conf.Database,
            Username = conf.Username,
            Password = conf.Password
        }.ConnectionString;
        return new BotDatabaseContext(new DbContextOptionsBuilder<BotDatabaseContext>()
            .UseNpgsql(connstr, pgopts => { pgopts.UseNodaTime(); })
            .UseSnakeCaseNamingConvention()
            .Options);
    }
}
