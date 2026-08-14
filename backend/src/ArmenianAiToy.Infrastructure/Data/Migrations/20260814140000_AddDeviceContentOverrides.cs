using System;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <summary>
    /// DeviceContentOverrides table — per-toy content entitlement, the first
    /// thing in this repo that lets one toy hold a different library from
    /// another. It is what makes "ship a new story to one toy first, then the
    /// fleet" possible, and the table the console upload slice writes its
    /// "send this to this toy" rows into.
    ///
    /// Default-plus-exceptions: a device with NO rows here resolves to exactly
    /// the fleet catalogue, so this migration changes no toy's manifest by a
    /// single byte on the day it lands.
    ///
    /// The unique (DeviceId, ItemKind, ItemKey) index is load-bearing, not
    /// tidiness: it is what makes a contradictory allow AND deny for one item
    /// unrepresentable, which is what keeps the resolver's precedence rule to
    /// one sentence.
    ///
    /// Purely additive: one table, no change to Devices or to any existing row.
    ///
    /// Hand-written rather than scaffolded, same reason as AddDeviceInvites and
    /// AddDeviceSyncDiagnostics: dotnet-ef cannot run against this solution. The
    /// attributes below are what the scaffolder would have emitted into a
    /// .Designer.cs, and the model snapshot is updated alongside so the next
    /// scaffolded migration still diffs correctly.
    /// </summary>
    // BOTH attributes are required for EF to discover this migration: without
    // [DbContext] it is not associated with AppDbContext and Migrate() silently
    // skips it.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260814140000_AddDeviceContentOverrides")]
    public partial class AddDeviceContentOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceContentOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemKind = table.Column<string>(type: "TEXT", nullable: false),
                    ItemKey = table.Column<string>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceContentOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceContentOverrides_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Unique, not merely an index: one row per (device, item) is what
            // makes an allow/deny contradiction unrepresentable. It also backs
            // the per-device lookup on the content-manifest path, which every
            // toy hits on every sync attempt.
            migrationBuilder.CreateIndex(
                name: "IX_DeviceContentOverrides_DeviceId_ItemKind_ItemKey",
                table: "DeviceContentOverrides",
                columns: new[] { "DeviceId", "ItemKind", "ItemKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceContentOverrides");
        }
    }
}
