using System;
using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <summary>
    /// DeviceInvites table — short-lived, single-use invitations letting a
    /// SECOND parent link an existing toy by typing a short code instead of the
    /// toy's 36-character id plus the claim code printed on its box. Purely
    /// additive; nothing on Devices changes, and in particular ClaimCodeHash is
    /// untouched (re-minting it would kill the QR printed on the toy).
    ///
    /// Hand-written rather than scaffolded, same reason as AddStoryPlays:
    /// dotnet-ef cannot run against this solution. The attributes below are what
    /// the scaffolder would have emitted into a .Designer.cs, and the model
    /// snapshot is updated alongside so the next scaffolded migration still
    /// diffs correctly.
    /// </summary>
    // BOTH attributes are required for EF to discover this migration: without
    // [DbContext] it is not associated with AppDbContext and Migrate() silently
    // skips it.
    [DbContext(typeof(AppDbContext))]
    [Migration("20260811130000_AddDeviceInvites")]
    public partial class AddDeviceInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceInvites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByParentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Selector = table.Column<string>(type: "TEXT", nullable: false),
                    SecretHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConsumedByParentId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceInvites_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Unique: redemption looks the row up by selector ALONE. The joiner
            // is never asked for a device id — that is the point of the feature —
            // so a selector collision would make a code ambiguous.
            migrationBuilder.CreateIndex(
                name: "IX_DeviceInvites_Selector",
                table: "DeviceInvites",
                column: "Selector",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceInvites_DeviceId",
                table: "DeviceInvites",
                column: "DeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceInvites");
        }
    }
}
