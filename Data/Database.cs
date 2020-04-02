using Npgsql;
using System.Threading.Tasks;

namespace BirthdayBot.Data
{
    /// <summary>
    /// Some database abstractions.
    /// </summary>
    class Database
    {
        /*
         * Database storage in this project, explained:
         * Each guild gets a row in the settings table. This table is referred to when doing most things.
         * Within each guild, each known user gets a row in the users table with specific information specified.
         * Users can override certain settings in global, such as time zone.
         */

        private string DBConnectionString { get; }

        public Database(string connString)
        {
            DBConnectionString = connString;

            // Database initialization happens here as well.
            SetupTables();
        }

        public async Task<NpgsqlConnection> OpenConnectionAsync()
        {
            var db = new NpgsqlConnection(DBConnectionString);
            await db.OpenAsync();
            return db;
        }

        private void SetupTables()
        {
            using var db = OpenConnectionAsync().GetAwaiter().GetResult();
            GuildStateInformation.SetUpDatabaseTable(db); // Note: Call this first. (Foreign reference constraints.)
            GuildUserSettings.SetUpDatabaseTable(db);
        }
    }
}
