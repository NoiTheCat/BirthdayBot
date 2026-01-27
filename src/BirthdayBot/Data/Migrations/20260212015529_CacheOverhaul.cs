using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirthdayBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class CacheOverhaul : Migration
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

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "last_seen",
                table: "user_entries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            // Manual edits: Converting two integer values into a single date value
            migrationBuilder.AddColumn<DateOnly>(
                name: "birth_date",
                table: "user_entries",
                type: "date",
                nullable: true);
            migrationBuilder.Sql(
                """UPDATE "user_entries" SET "birth_date" = MAKE_DATE(2000, "birth_month", "birth_day");""");
            migrationBuilder.AlterColumn<DateOnly>(
                name: "birth_date",
                table: "user_entries",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true
            );
            // Now we can drop these
            migrationBuilder.DropColumn(
                name: "birth_day",
                table: "user_entries");
            migrationBuilder.DropColumn(
                name: "birth_month",
                table: "user_entries");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_processed",
                table: "user_entries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "last_seen",
                table: "guild_configurations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

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
            // Writing this is more trouble than it's worth, especially if it might never actually be reverted.
            throw new NotSupportedException("Cannot be reverted.");
        }
    }
}
