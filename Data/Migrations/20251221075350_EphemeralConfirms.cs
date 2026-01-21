using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirthdayBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class EphemeralConfirms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ephemeral_confirm",
                table: "guild_configurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ephemeral_confirm",
                table: "guild_configurations");
        }
    }
}
