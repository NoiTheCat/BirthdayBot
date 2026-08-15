using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace BirthdayBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizationsBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_entries_guild_configurations_guild_id",
                table: "user_entries");

            migrationBuilder.DropTable(
                name: "warm_cache");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_entries",
                table: "user_entries");

            migrationBuilder.DropPrimaryKey(
                name: "pk_guild_configurations",
                table: "guild_configurations");

            migrationBuilder.RenameTable(
                name: "user_entries",
                newName: "UserEntries");

            migrationBuilder.RenameTable(
                name: "guild_configurations",
                newName: "GuildConfigurations");

            migrationBuilder.RenameColumn(
                name: "time_zone",
                table: "UserEntries",
                newName: "TimeZone");

            migrationBuilder.RenameColumn(
                name: "last_seen",
                table: "UserEntries",
                newName: "LastSeen");

            migrationBuilder.RenameColumn(
                name: "last_processed",
                table: "UserEntries",
                newName: "LastProcessed");

            migrationBuilder.RenameColumn(
                name: "birth_date",
                table: "UserEntries",
                newName: "BirthDate");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "UserEntries",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "guild_id",
                table: "UserEntries",
                newName: "GuildId");

            migrationBuilder.RenameColumn(
                name: "last_seen",
                table: "GuildConfigurations",
                newName: "LastSeen");

            migrationBuilder.RenameColumn(
                name: "guild_time_zone",
                table: "GuildConfigurations",
                newName: "GuildTimeZone");

            migrationBuilder.RenameColumn(
                name: "ephemeral_confirm",
                table: "GuildConfigurations",
                newName: "EphemeralConfirm");

            migrationBuilder.RenameColumn(
                name: "birthday_role",
                table: "GuildConfigurations",
                newName: "BirthdayRole");

            migrationBuilder.RenameColumn(
                name: "announcement_channel",
                table: "GuildConfigurations",
                newName: "AnnouncementChannel");

            migrationBuilder.RenameColumn(
                name: "announce_ping",
                table: "GuildConfigurations",
                newName: "AnnouncePing");

            migrationBuilder.RenameColumn(
                name: "announce_message_pl",
                table: "GuildConfigurations",
                newName: "AnnounceMessagePl");

            migrationBuilder.RenameColumn(
                name: "announce_message",
                table: "GuildConfigurations",
                newName: "AnnounceMessage");

            migrationBuilder.RenameColumn(
                name: "add_only",
                table: "GuildConfigurations",
                newName: "AddOnly");

            migrationBuilder.RenameColumn(
                name: "guild_id",
                table: "GuildConfigurations",
                newName: "GuildId");

            migrationBuilder.AlterColumn<LocalDate>(
                name: "LastSeen",
                table: "UserEntries",
                type: "date",
                nullable: false,
                defaultValueSql: "NOW()",
                oldClrType: typeof(Instant),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "NOW()");

            migrationBuilder.AlterColumn<LocalDate>(
                name: "LastSeen",
                table: "GuildConfigurations",
                type: "date",
                nullable: false,
                defaultValueSql: "NOW()",
                oldClrType: typeof(Instant),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "NOW()");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserEntries",
                table: "UserEntries",
                columns: new[] { "GuildId", "UserId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_GuildConfigurations",
                table: "GuildConfigurations",
                column: "GuildId");

            migrationBuilder.CreateTable(
                name: "WarmCache",
                columns: table => new
                {
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    UserId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    ExpiresAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarmCache", x => new { x.GuildId, x.UserId });
                });

            migrationBuilder.AddForeignKey(
                name: "FK_UserEntries_GuildConfigurations_GuildId",
                table: "UserEntries",
                column: "GuildId",
                principalTable: "GuildConfigurations",
                principalColumn: "GuildId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserEntries_GuildConfigurations_GuildId",
                table: "UserEntries");

            migrationBuilder.DropTable(
                name: "WarmCache");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserEntries",
                table: "UserEntries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_GuildConfigurations",
                table: "GuildConfigurations");

            migrationBuilder.RenameTable(
                name: "UserEntries",
                newName: "user_entries");

            migrationBuilder.RenameTable(
                name: "GuildConfigurations",
                newName: "guild_configurations");

            migrationBuilder.RenameColumn(
                name: "TimeZone",
                table: "user_entries",
                newName: "time_zone");

            migrationBuilder.RenameColumn(
                name: "LastSeen",
                table: "user_entries",
                newName: "last_seen");

            migrationBuilder.RenameColumn(
                name: "LastProcessed",
                table: "user_entries",
                newName: "last_processed");

            migrationBuilder.RenameColumn(
                name: "BirthDate",
                table: "user_entries",
                newName: "birth_date");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "user_entries",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "GuildId",
                table: "user_entries",
                newName: "guild_id");

            migrationBuilder.RenameColumn(
                name: "LastSeen",
                table: "guild_configurations",
                newName: "last_seen");

            migrationBuilder.RenameColumn(
                name: "GuildTimeZone",
                table: "guild_configurations",
                newName: "guild_time_zone");

            migrationBuilder.RenameColumn(
                name: "EphemeralConfirm",
                table: "guild_configurations",
                newName: "ephemeral_confirm");

            migrationBuilder.RenameColumn(
                name: "BirthdayRole",
                table: "guild_configurations",
                newName: "birthday_role");

            migrationBuilder.RenameColumn(
                name: "AnnouncementChannel",
                table: "guild_configurations",
                newName: "announcement_channel");

            migrationBuilder.RenameColumn(
                name: "AnnouncePing",
                table: "guild_configurations",
                newName: "announce_ping");

            migrationBuilder.RenameColumn(
                name: "AnnounceMessagePl",
                table: "guild_configurations",
                newName: "announce_message_pl");

            migrationBuilder.RenameColumn(
                name: "AnnounceMessage",
                table: "guild_configurations",
                newName: "announce_message");

            migrationBuilder.RenameColumn(
                name: "AddOnly",
                table: "guild_configurations",
                newName: "add_only");

            migrationBuilder.RenameColumn(
                name: "GuildId",
                table: "guild_configurations",
                newName: "guild_id");

            migrationBuilder.AlterColumn<Instant>(
                name: "last_seen",
                table: "user_entries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()",
                oldClrType: typeof(LocalDate),
                oldType: "date",
                oldDefaultValueSql: "NOW()");

            migrationBuilder.AlterColumn<Instant>(
                name: "last_seen",
                table: "guild_configurations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()",
                oldClrType: typeof(LocalDate),
                oldType: "date",
                oldDefaultValueSql: "NOW()");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_entries",
                table: "user_entries",
                columns: new[] { "guild_id", "user_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_guild_configurations",
                table: "guild_configurations",
                column: "guild_id");

            migrationBuilder.CreateTable(
                name: "warm_cache",
                columns: table => new
                {
                    guild_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    user_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warm_cache", x => new { x.guild_id, x.user_id });
                });

            migrationBuilder.AddForeignKey(
                name: "fk_user_entries_guild_configurations_guild_id",
                table: "user_entries",
                column: "guild_id",
                principalTable: "guild_configurations",
                principalColumn: "guild_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
