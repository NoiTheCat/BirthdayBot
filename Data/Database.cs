using Npgsql;
using System.Threading.Tasks;

namespace BirthdayBot.Data;

[Obsolete(ObsoleteReason, error: false)]
internal static class Database {
    public const string ObsoleteReason = "Will be removed in favor of EF6 stuff when text commands are removed";

    public static string DBConnectionString { get; set; }

    /// <summary>
    /// Sets up and opens a database connection.
    /// </summary>
    public static async Task<NpgsqlConnection> OpenConnectionAsync() {
        var db = new NpgsqlConnection(DBConnectionString);
        await db.OpenAsync().ConfigureAwait(false);
        return db;
    }

    public static async Task DoInitialDatabaseSetupAsync() {
        using var db = await OpenConnectionAsync().ConfigureAwait(false);

        // Refer to the methods being called for information on how the database is set up.
        // Note: The order these are called is important. (Foreign reference constraints.)
        await GuildConfiguration.DatabaseSetupAsync(db).ConfigureAwait(false);
        await GuildUserConfiguration.DatabaseSetupAsync(db).ConfigureAwait(false);
    }
}
