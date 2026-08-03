using System;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <summary>
    /// B4 reflection-answer memory: append-only per-listen child answers to
    /// the post-story reflection question. Purely additive.
    ///
    /// Hand-written rather than scaffolded — dotnet-ef cannot run against
    /// this solution (see AddDeviceSdCardOk). BOTH attributes below are
    /// required for EF discovery; the model snapshot is updated alongside.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260803122000_AddStoryReflectionAnswers")]
    public partial class AddStoryReflectionAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoryReflectionAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoryId = table.Column<string>(type: "TEXT", nullable: false),
                    QuestionIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    AnswerText = table.Column<string>(type: "TEXT", nullable: false),
                    SafetyFlag = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryReflectionAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoryReflectionAnswers_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoryReflectionAnswers_DeviceId_StoryId",
                table: "StoryReflectionAnswers",
                columns: new[] { "DeviceId", "StoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_StoryReflectionAnswers_DeviceId_CreatedAtUtc",
                table: "StoryReflectionAnswers",
                columns: new[] { "DeviceId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoryReflectionAnswers");
        }
    }
}
