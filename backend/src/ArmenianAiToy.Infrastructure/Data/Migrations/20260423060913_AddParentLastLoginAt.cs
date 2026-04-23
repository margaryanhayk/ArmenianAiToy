using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddParentLastLoginAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoginAt",
                table: "Parents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastLoginAt",
                table: "Parents");
        }
    }
}
