using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Toy self-diagnostics: nullable SdCardOk on Device, stamped from the
    /// heartbeat. Purely additive — nullable, no default, no backfill, so
    /// existing rows read "never reported" and legacy firmware that omits the
    /// field keeps working unchanged.
    ///
    /// Hand-written rather than scaffolded: dotnet-ef cannot run against this
    /// solution (the Api startup project does not reference
    /// Microsoft.EntityFrameworkCore.Design, and adding a NuGet dependency is
    /// an owner decision). The Migration/DbContext attributes below are what
    /// the scaffolder would have emitted into a .Designer.cs; the model
    /// snapshot is updated alongside so the next scaffolded migration still
    /// diffs correctly.
    /// </summary>
    // BOTH attributes are required for EF to discover this migration: without
    // [DbContext] it is not associated with AppDbContext and Migrate() silently
    // skips it (verified — the first hand-written attempt carried only
    // [Migration] and never ran against a real database).
    [DbContext(typeof(AppDbContext))]
    [Migration("20260803010000_AddDeviceSdCardOk")]
    public partial class AddDeviceSdCardOk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SdCardOk",
                table: "Devices",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SdCardOk",
                table: "Devices");
        }
    }
}
