using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorldTime.Data.Migrations
{
    /// <inheritdoc />
    public partial class Add12HrSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "guild_settings",
                columns: table => new
                {
                    guildid = table.Column<decimal>(name: "guild_id", type: "numeric(20,0)", nullable: false),
                    use12hourtime = table.Column<bool>(name: "use12hour_time", type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guild_settings", x => x.guildid);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guild_settings");
        }
    }
}
