using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChildModeOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CuriosityEnabled",
                table: "Children",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "GameEnabled",
                table: "Children",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RiddleEnabled",
                table: "Children",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StoryEnabled",
                table: "Children",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CuriosityEnabled",
                table: "Children");

            migrationBuilder.DropColumn(
                name: "GameEnabled",
                table: "Children");

            migrationBuilder.DropColumn(
                name: "RiddleEnabled",
                table: "Children");

            migrationBuilder.DropColumn(
                name: "StoryEnabled",
                table: "Children");
        }
    }
}
