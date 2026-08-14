using System;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <summary>
    /// ContentItems table — the first content in this product that does not
    /// arrive through git. Until now, adding one story meant authoring an
    /// embedded resource, hand-adding a placeholder config row, running a
    /// PowerShell shipper that needs ffmpeg, committing tens of megabytes of
    /// audio and redeploying. This table is the runtime path that replaces all
    /// of it.
    ///
    /// The audio does NOT live here — only its identity, its server-computed
    /// sha256 and size, and where to find it relative to
    /// ContentSync:UploadRoot on the persistent volume. The shipped catalogue
    /// stays exactly where it is, in the image under ContentSync:AudioRoot.
    ///
    /// No foreign key, deliberately: the catalogue is fleet-level. Which TOY
    /// gets which item is DeviceContentOverrides, keyed by the same ItemKey, so
    /// entitlement and delivery cannot disagree about what an item is.
    ///
    /// DefaultEnabled ships false on every uploaded row, which is why this
    /// migration changes no toy's manifest on the day it lands: an empty table
    /// resolves to exactly the config catalogue, by reference.
    ///
    /// The unique (Kind, ItemKey) index is load-bearing rather than tidiness —
    /// ItemKey is the content-file lookup key, so two rows sharing one would
    /// make the endpoint ambiguous. It is what lets a re-upload be an upsert
    /// that bumps Version.
    ///
    /// Hand-written rather than scaffolded, same reason as
    /// AddDeviceContentOverrides and AddDeviceSyncDiagnostics: dotnet-ef cannot
    /// run against this solution. The attributes below are what the scaffolder
    /// would have emitted into a .Designer.cs, and the model snapshot is
    /// updated alongside so the next scaffolded migration still diffs
    /// correctly.
    /// </summary>
    // BOTH attributes are required for EF to discover this migration: without
    // [DbContext] it is not associated with AppDbContext and Migrate() silently
    // skips it.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260814160000_AddContentItems")]
    public partial class AddContentItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    ItemKey = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    DefaultEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RetiredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_Kind_ItemKey",
                table: "ContentItems",
                columns: new[] { "Kind", "ItemKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drops the catalogue rows only. The uploaded MP3s on the volume
            // are deliberately left alone — this migration never wrote them and
            // a rollback must not destroy the one copy of content an owner
            // recorded.
            migrationBuilder.DropTable(
                name: "ContentItems");
        }
    }
}
