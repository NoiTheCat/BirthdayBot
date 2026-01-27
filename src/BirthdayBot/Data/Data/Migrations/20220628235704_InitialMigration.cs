using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorldTime.Data.Migrations
{
    public partial class InitialMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "userdata",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    zone = table.Column<string>(type: "text", nullable: false),
                    last_active = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("userdata_pkey", x => new { x.guild_id, x.user_id });
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "userdata");
        }
    }
}
