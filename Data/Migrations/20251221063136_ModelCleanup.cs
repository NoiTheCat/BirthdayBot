using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirthdayBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class ModelCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "user_birthdays_guild_id_fkey",
                table: "user_birthdays");

            migrationBuilder.DropPrimaryKey(
                name: "user_birthdays_pkey",
                table: "user_birthdays");

            migrationBuilder.DropPrimaryKey(
                name: "settings_pkey",
                table: "settings");

            migrationBuilder.RenameTable(
                name: "user_birthdays",
                newName: "user_entries");

            migrationBuilder.RenameTable(
                name: "settings",
                newName: "guild_configurations");

            migrationBuilder.RenameColumn(
                name: "time_zone",
                table: "guild_configurations",
                newName: "guild_time_zone");

            migrationBuilder.RenameColumn(
                name: "role_id",
                table: "guild_configurations",
                newName: "birthday_role");

            migrationBuilder.RenameColumn(
                name: "channel_announce_id",
                table: "guild_configurations",
                newName: "announcement_channel");

            migrationBuilder.AlterColumn<bool>(
                name: "announce_ping",
                table: "guild_configurations",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_entries",
                table: "user_entries",
                columns: new[] { "guild_id", "user_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_guild_configurations",
                table: "guild_configurations",
                column: "guild_id");

            migrationBuilder.AddForeignKey(
                name: "fk_user_entries_guild_configurations_guild_id",
                table: "user_entries",
                column: "guild_id",
                principalTable: "guild_configurations",
                principalColumn: "guild_id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_entries_guild_configurations_guild_id",
                table: "user_entries");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_entries",
                table: "user_entries");

            migrationBuilder.DropPrimaryKey(
                name: "pk_guild_configurations",
                table: "guild_configurations");

            migrationBuilder.RenameTable(
                name: "user_entries",
                newName: "user_birthdays");

            migrationBuilder.RenameTable(
                name: "guild_configurations",
                newName: "settings");

            migrationBuilder.RenameColumn(
                name: "guild_time_zone",
                table: "settings",
                newName: "time_zone");

            migrationBuilder.RenameColumn(
                name: "birthday_role",
                table: "settings",
                newName: "role_id");

            migrationBuilder.RenameColumn(
                name: "announcement_channel",
                table: "settings",
                newName: "channel_announce_id");

            migrationBuilder.AlterColumn<bool>(
                name: "announce_ping",
                table: "settings",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AddPrimaryKey(
                name: "user_birthdays_pkey",
                table: "user_birthdays",
                columns: new[] { "guild_id", "user_id" });

            migrationBuilder.AddPrimaryKey(
                name: "settings_pkey",
                table: "settings",
                column: "guild_id");

            migrationBuilder.AddForeignKey(
                name: "user_birthdays_guild_id_fkey",
                table: "user_birthdays",
                column: "guild_id",
                principalTable: "settings",
                principalColumn: "guild_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
