using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Slice E: bedtime-music parent toggle. NOT NULL bool with a default of
    /// FALSE — the suggestion is opt-in, so no existing device changes
    /// behavior at migration time.
    ///
    /// Hand-written rather than scaffolded — dotnet-ef cannot run against
    /// this solution (see AddDeviceSdCardOk). BOTH attributes below are
    /// required for EF discovery; the model snapshot is updated alongside.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260803220000_AddDeviceBedtimeMusicEnabled")]
    public partial class AddDeviceBedtimeMusicEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BedtimeMusicEnabled",
                table: "Devices",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BedtimeMusicEnabled",
                table: "Devices");
        }
    }
}
