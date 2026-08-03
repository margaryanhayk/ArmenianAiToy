using System;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <summary>
    /// StoryPlays table — device-reported story playback events (the toy's
    /// store-and-forward upload for offline SD-cache plays). Purely additive.
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
    // skips it (see AddDeviceSdCardOk's note — verified the hard way).
    [DbContext(typeof(AppDbContext))]
    [Migration("20260803120000_AddStoryPlays")]
    public partial class AddStoryPlays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoryPlays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoryId = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    Finished = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClientEventKey = table.Column<string>(type: "TEXT", nullable: false),
                    PlayedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TimeIsApproximate = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryPlays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoryPlays_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoryPlays_DeviceId_ClientEventKey",
                table: "StoryPlays",
                columns: new[] { "DeviceId", "ClientEventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoryPlays_DeviceId_PlayedAtUtc",
                table: "StoryPlays",
                columns: new[] { "DeviceId", "PlayedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StoryPlays_DeviceId_StoryId",
                table: "StoryPlays",
                columns: new[] { "DeviceId", "StoryId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoryPlays");
        }
    }
}
