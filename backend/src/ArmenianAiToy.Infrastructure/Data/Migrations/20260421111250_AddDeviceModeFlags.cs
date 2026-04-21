using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceModeFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CuriosityEnabled",
                table: "Devices",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "GameEnabled",
                table: "Devices",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RiddleEnabled",
                table: "Devices",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "StoryEnabled",
                table: "Devices",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CuriosityEnabled",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "GameEnabled",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "RiddleEnabled",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "StoryEnabled",
                table: "Devices");
        }
    }
}
