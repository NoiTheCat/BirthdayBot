using Npgsql;
using System;
using System.Threading.Tasks;

namespace BirthdayBot.Data
{
    /// <summary>
    /// Database access and some abstractions.
    /// </summary>
    internal static class Database
    {
        public static string DBConnectionString { get; set; }

        public static async Task<NpgsqlConnection> OpenConnectionAsync()
        {
            if (DBConnectionString == null) throw new Exception("Database connection string not set");
            var db = new NpgsqlConnection(DBConnectionString);
            await db.OpenAsync().ConfigureAwait(false);
            return db;
        }

        public static async Task DoInitialDatabaseSetupAsync()
        {
            using var db = await OpenConnectionAsync().ConfigureAwait(false);

            // Refer to the methods being called for information on how the database is set up.
            // Note: The order these are called is important. (Foreign reference constraints.)
            await GuildConfiguration.DatabaseSetupAsync(db).ConfigureAwait(false);
            await GuildUserConfiguration.DatabaseSetupAsync(db).ConfigureAwait(false);
        }
    }
}
