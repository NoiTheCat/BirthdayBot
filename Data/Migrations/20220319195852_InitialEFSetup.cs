using System;
using Microsoft.EntityFrameworkCore.Migrations;

// command used:
// dotnet ef migrations add InitialEFSetup --output-dir Data/Migrations
// (don't forget to replace with a proper migration name)

#nullable disable

namespace BirthdayBot.Data.Migrations
{
    public partial class InitialEFSetup : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: true),
                    channel_announce_id = table.Column<long>(type: "bigint", nullable: true),
                    time_zone = table.Column<string>(type: "text", nullable: true),
                    moderated = table.Column<bool>(type: "boolean", nullable: false),
                    moderator_role = table.Column<long>(type: "bigint", nullable: true),
                    announce_message = table.Column<string>(type: "text", nullable: true),
                    announce_message_pl = table.Column<string>(type: "text", nullable: true),
                    announce_ping = table.Column<bool>(type: "boolean", nullable: false),
                    last_seen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("settings_pkey", x => x.guild_id);
                });

            migrationBuilder.CreateTable(
                name: "banned_users",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("banned_users_pkey", x => new { x.guild_id, x.user_id });
                    table.ForeignKey(
                        name: "banned_users_guild_id_fkey",
                        column: x => x.guild_id,
                        principalTable: "settings",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_birthdays",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    birth_month = table.Column<int>(type: "integer", nullable: false),
                    birth_day = table.Column<int>(type: "integer", nullable: false),
                    time_zone = table.Column<string>(type: "text", nullable: true),
                    last_seen = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("user_birthdays_pkey", x => new { x.guild_id, x.user_id });
                    table.ForeignKey(
                        name: "user_birthdays_guild_id_fkey",
                        column: x => x.guild_id,
                        principalTable: "settings",
                        principalColumn: "guild_id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "banned_users");

            migrationBuilder.DropTable(
                name: "user_birthdays");

            migrationBuilder.DropTable(
                name: "settings");
        }
    }
}
