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
        private static string _connString;
        public static string DBConnectionString
        {
            get => _connString;
            set => _connString = "Minimum Pool Size=5;Maximum Pool Size=50;Connection Idle Lifetime=30;" + value;
        }

        public static async Task<NpgsqlConnection> OpenConnectionAsync()
        {
            if (DBConnectionString == null) throw new Exception("Database connection string not set");
            var db = new NpgsqlConnection(DBConnectionString);
            await db.OpenAsync();
            return db;
        }

        public static async Task DoInitialDatabaseSetupAsync()
        {
            using var db = await OpenConnectionAsync();

            // Refer to the methods being called for information on how the database is set up.
            await GuildConfiguration.DatabaseSetupAsync(db); // Note: Call this first. (Foreign reference constraints.)
            await GuildUserConfiguration.DatabaseSetupAsync(db);
        }
    }
}
