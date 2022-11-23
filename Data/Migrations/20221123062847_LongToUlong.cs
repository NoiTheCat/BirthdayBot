using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirthdayBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class LongToUlong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTE: manually edited - must drop and re-add foreign key due to altered types
            migrationBuilder.DropForeignKey(
                name: "user_birthdays_guild_id_fkey",
                table: "user_birthdays");

            migrationBuilder.AlterColumn<decimal>(
                name: "user_id",
                table: "user_birthdays",
                type: "numeric(20,0)",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "guild_id",
                table: "user_birthdays",
                type: "numeric(20,0)",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "role_id",
                table: "settings",
                type: "numeric(20,0)",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "moderator_role",
                table: "settings",
                type: "numeric(20,0)",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "channel_announce_id",
                table: "settings",
                type: "numeric(20,0)",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "guild_id",
                table: "settings",
                type: "numeric(20,0)",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "user_id",
                table: "banned_users",
                type: "numeric(20,0)",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<decimal>(
                name: "guild_id",
                table: "banned_users",
                type: "numeric(20,0)",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddForeignKey(
                name: "user_birthdays_guild_id_fkey",
                table: "user_birthdays",
                column: "guild_id",
                principalTable: "settings",
                principalColumn: "guild_id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "user_birthdays_guild_id_fkey",
                table: "user_birthdays");

            migrationBuilder.AlterColumn<long>(
                name: "user_id",
                table: "user_birthdays",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,0)");

            migrationBuilder.AlterColumn<long>(
                name: "guild_id",
                table: "user_birthdays",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,0)");

            migrationBuilder.AlterColumn<long>(
                name: "role_id",
                table: "settings",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,0)",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "moderator_role",
                table: "settings",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,0)",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "channel_announce_id",
                table: "settings",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,0)",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "guild_id",
                table: "settings",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,0)");

            migrationBuilder.AlterColumn<long>(
                name: "user_id",
                table: "banned_users",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,0)");

            migrationBuilder.AlterColumn<long>(
                name: "guild_id",
                table: "banned_users",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(20,0)");

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
