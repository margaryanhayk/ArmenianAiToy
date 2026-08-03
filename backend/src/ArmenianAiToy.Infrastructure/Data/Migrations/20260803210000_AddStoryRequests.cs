using System;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Slice F: StoryRequests — the owner's custom-story work queue.
    /// Purely additive; deliberately FK-free (requests outlive the
    /// requesting account, same idiom as AuditEvents).
    ///
    /// Hand-written rather than scaffolded — dotnet-ef cannot run against
    /// this solution (see AddDeviceSdCardOk). BOTH attributes below are
    /// required for EF discovery; the model snapshot is updated alongside.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260803210000_AddStoryRequests")]
    public partial class AddStoryRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoryRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    PhotoPath = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoryRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoryRequests_ParentId",
                table: "StoryRequests",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_StoryRequests_Status_CreatedAtUtc",
                table: "StoryRequests",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoryRequests");
        }
    }
}
