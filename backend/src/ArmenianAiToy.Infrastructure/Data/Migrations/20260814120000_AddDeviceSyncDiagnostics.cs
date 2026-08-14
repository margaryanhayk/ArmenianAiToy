using System;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Sync diagnostics on Devices — what the toy's last content sync TRIED,
    /// beside the existing report of what it HAS.
    ///
    /// Motivating incident (2026-08-14): firmware 1.2.1 crash-looped all
    /// night, panicking on the manifest parse and rebooting every ~184 s. It
    /// heartbeat normally for the first 180 s of every cycle, so LastSeenAt
    /// stayed fresh and every surface showed the toy online and merely
    /// "stale". Nothing in the product could say it had panicked 400 times.
    /// ResetReason/BootCount exist because a device that dies MID-sync can
    /// never report a status at all — the next boot's reset reason is the
    /// only surviving evidence.
    ///
    /// Purely additive: six nullable columns, no defaults, no index, no table.
    /// Every existing row keeps working and a device that sends none of these
    /// behaves exactly as before.
    ///
    /// Hand-written rather than scaffolded, same reason as AddStoryPlays and
    /// AddDeviceInvites: dotnet-ef cannot run against this solution. The
    /// attributes below are what the scaffolder would have emitted into a
    /// .Designer.cs, and the model snapshot is updated alongside so the next
    /// scaffolded migration still diffs correctly.
    /// </summary>
    // BOTH attributes are required for EF to discover this migration: without
    // [DbContext] it is not associated with AppDbContext and Migrate() silently
    // skips it.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260814120000_AddDeviceSyncDiagnostics")]
    public partial class AddDeviceSyncDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BootCount",
                table: "Devices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentSyncError",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContentSyncReportedAt",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentSyncStatus",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContentSyncedAt",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResetReason",
                table: "Devices",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BootCount",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ContentSyncError",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ContentSyncReportedAt",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ContentSyncStatus",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ContentSyncedAt",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ResetReason",
                table: "Devices");
        }
    }
}
