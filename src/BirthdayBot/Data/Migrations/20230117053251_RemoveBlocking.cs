using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirthdayBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBlocking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "banned_users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "banned_users",
                columns: table => new
                {
                    guildid = table.Column<decimal>(name: "guild_id", type: "numeric(20,0)", nullable: false),
                    userid = table.Column<decimal>(name: "user_id", type: "numeric(20,0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("banned_users_pkey", x => new { x.guildid, x.userid });
                    table.ForeignKey(
                        name: "banned_users_guild_id_fkey",
                        column: x => x.guildid,
                        principalTable: "settings",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });
        }
    }
}
