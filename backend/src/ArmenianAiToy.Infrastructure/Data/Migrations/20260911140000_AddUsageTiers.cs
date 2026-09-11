using System;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Usage-tier METERING FOUNDATION (2026-09-11), shipped behind
    /// <c>Usage:Tiers:Enabled</c> = false. Two purely additive changes:
    /// <list type="bullet">
    ///   <item><description><c>Devices.UsageTier</c> — NOT NULL string,
    ///   backfilled <c>"free"</c> so every existing device starts on the
    ///   free tier (matching <c>Device.UsageTier</c>'s C# default) rather
    ///   than an empty string a config lookup would then have to special-
    ///   case.</description></item>
    ///   <item><description><c>DeviceUsageDays</c> — the new per-device
    ///   per-UTC-day question/cost counter, exact shape of
    ///   <c>StoryPlays</c>/<c>GamePlays</c> (FK cascade to Device, one
    ///   unique index).</description></item>
    /// </list>
    /// Neither column is read on any request path while the flag is off —
    /// see <c>UsageTiersOptions</c>.
    ///
    /// Hand-written rather than scaffolded — dotnet-ef cannot run against
    /// this solution (see AddDeviceSdCardOk). The attributes below are what
    /// the scaffolder would have emitted; the model snapshot is updated
    /// alongside so the next scaffolded migration still diffs correctly.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260911140000_AddUsageTiers")]
    public partial class AddUsageTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UsageTier",
                table: "Devices",
                type: "TEXT",
                nullable: false,
                defaultValue: "free");

            migrationBuilder.CreateTable(
                name: "DeviceUsageDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DayUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Questions = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedUsd = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceUsageDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceUsageDays_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceUsageDays_DeviceId_DayUtc",
                table: "DeviceUsageDays",
                columns: new[] { "DeviceId", "DayUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceUsageDays");

            migrationBuilder.DropColumn(
                name: "UsageTier",
                table: "Devices");
        }
    }
}
