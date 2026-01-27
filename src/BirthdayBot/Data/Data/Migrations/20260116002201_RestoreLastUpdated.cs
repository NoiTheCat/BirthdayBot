using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorldTime.Data.Migrations
{
    /// <inheritdoc />
    public partial class RestoreLastUpdated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "userdata_pkey",
                table: "userdata");

            migrationBuilder.RenameTable(
                name: "userdata",
                newName: "user_entries");

            migrationBuilder.RenameColumn(
                name: "zone",
                table: "user_entries",
                newName: "time_zone");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_seen",
                table: "user_entries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_entries",
                table: "user_entries",
                columns: new[] { "guild_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_entries_guild_id",
                table: "user_entries",
                column: "guild_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_user_entries",
                table: "user_entries");

            migrationBuilder.DropIndex(
                name: "ix_user_entries_guild_id",
                table: "user_entries");

            migrationBuilder.DropColumn(
                name: "last_seen",
                table: "user_entries");

            migrationBuilder.RenameTable(
                name: "user_entries",
                newName: "userdata");

            migrationBuilder.RenameColumn(
                name: "time_zone",
                table: "userdata",
                newName: "zone");

            migrationBuilder.AddPrimaryKey(
                name: "userdata_pkey",
                table: "userdata",
                columns: new[] { "guild_id", "user_id" });
        }
    }
}
