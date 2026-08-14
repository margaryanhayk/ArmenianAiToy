using System;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <summary>
    /// GamePlays table — device-reported OFFLINE game sessions. The exact
    /// shape of StoryPlays, for the exact same reason: the three offline games
    /// and the offline quiz run entirely on the toy from SD clips and make no
    /// network call, so without this table a child can play all afternoon and
    /// the parent dashboard shows nothing.
    ///
    /// Purely additive. A deployment whose toys never report a game play has
    /// an empty table and behaves exactly as it did before this migration.
    ///
    /// Note what is NOT here: no text column of any kind. These games have no
    /// microphone, so the toy can report only what the buttons measured, and
    /// the absence of a place to put a child's words is the enforcement of
    /// that rule rather than a comment asking for it.
    ///
    /// Hand-written rather than scaffolded, same reason as AddStoryPlays and
    /// AddContentItems: dotnet-ef cannot run against this solution (the Api
    /// startup project does not reference Microsoft.EntityFrameworkCore.Design,
    /// and adding a NuGet dependency is an owner decision). The attributes
    /// below are what the scaffolder would have emitted into a .Designer.cs;
    /// the model snapshot is updated alongside so the next scaffolded
    /// migration still diffs correctly.
    /// </summary>
    // BOTH attributes are required for EF to discover this migration: without
    // [DbContext] it is not associated with AppDbContext and Migrate() silently
    // skips it (see AddDeviceSdCardOk's note — verified the hard way).
    [DbContext(typeof(AppDbContext))]
    [Migration("20260814170000_AddGamePlays")]
    public partial class AddGamePlays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GamePlays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GameKey = table.Column<string>(type: "TEXT", nullable: false),
                    ClientEventKey = table.Column<string>(type: "TEXT", nullable: false),
                    PlayedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TimeIsApproximate = table.Column<bool>(type: "INTEGER", nullable: false),
                    Rounds = table.Column<int>(type: "INTEGER", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", nullable: true),
                    Score = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GamePlays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GamePlays_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GamePlays_DeviceId_ClientEventKey",
                table: "GamePlays",
                columns: new[] { "DeviceId", "ClientEventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GamePlays_DeviceId_PlayedAtUtc",
                table: "GamePlays",
                columns: new[] { "DeviceId", "PlayedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GamePlays_DeviceId_GameKey",
                table: "GamePlays",
                columns: new[] { "DeviceId", "GameKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GamePlays");
        }
    }
}
