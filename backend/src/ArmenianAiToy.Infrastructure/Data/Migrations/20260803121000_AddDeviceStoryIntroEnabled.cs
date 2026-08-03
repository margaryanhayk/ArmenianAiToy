using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <summary>
    /// B3 spoken-story-intro parent toggle: NOT NULL bool on Device with a
    /// default of true, so every existing device gains the intro at migration
    /// time (educational-by-default, owner decision 2026-08-03) and the
    /// non-nullable property never sees a null in-memory.
    ///
    /// Hand-written rather than scaffolded — dotnet-ef cannot run against
    /// this solution (see AddDeviceSdCardOk). BOTH attributes below are
    /// required for EF to discover the migration; the model snapshot is
    /// updated alongside.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260803121000_AddDeviceStoryIntroEnabled")]
    public partial class AddDeviceStoryIntroEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StoryIntroEnabled",
                table: "Devices",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoryIntroEnabled",
                table: "Devices");
        }
    }
}
