using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceContentReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContentGameClips",
                table: "Devices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContentIndexSchema",
                table: "Devices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContentMusicTracks",
                table: "Devices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContentReportedAt",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentStories",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContentVoiceClips",
                table: "Devices",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentGameClips",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ContentIndexSchema",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ContentMusicTracks",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ContentReportedAt",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ContentStories",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ContentVoiceClips",
                table: "Devices");
        }
    }
}
