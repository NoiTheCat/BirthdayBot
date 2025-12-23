using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirthdayBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "add_only",
                table: "guild_configurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "add_only",
                table: "guild_configurations");
        }
    }
}
