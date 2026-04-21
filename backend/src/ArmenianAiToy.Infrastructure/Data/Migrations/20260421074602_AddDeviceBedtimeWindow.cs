using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceBedtimeWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "BedtimeEnd",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "BedtimeStart",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                table: "Devices",
                type: "TEXT",
                nullable: false,
                defaultValue: "Asia/Yerevan");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BedtimeEnd",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "BedtimeStart",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                table: "Devices");
        }
    }
}
