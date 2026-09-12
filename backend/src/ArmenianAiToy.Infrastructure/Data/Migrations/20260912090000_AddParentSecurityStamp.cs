using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <summary>
    /// N10 — per-parent security stamp so a parent JWT stops validating
    /// after a password change / reset / anonymization (see
    /// <c>Parent.SecurityStamp</c>). NOT NULL TEXT, added with an empty
    /// default so SQLite's ADD COLUMN accepts it, then every existing row
    /// is backfilled with its OWN random 32-hex-char value in raw SQL —
    /// SQLite evaluates <c>randomblob(16)</c> per row, so no fixed
    /// sentinel is needed and no two parents ever share a stamp. Chosen
    /// over a sentinel because a sentinel would have made every pre-N10
    /// parent's stamp guessable (a token forged with the sentinel claim
    /// would pass the stamp check until that parent first changed their
    /// password); a per-row random value gives old rows the same strength
    /// as new ones from the first request after deploy. The code mints
    /// the same shape (<c>ParentService.GenerateSecurityStamp</c>).
    ///
    /// One-time operator-visible effect: no token issued before this
    /// migration carries the <c>sst</c> claim, so with the shipped
    /// <c>Jwt:RequireSecurityStamp=true</c> every logged-in parent has to
    /// log in once after deploy (docs/ops-runbook.md).
    ///
    /// Hand-written rather than scaffolded — dotnet-ef cannot run against
    /// this solution (see AddDeviceSdCardOk). BOTH attributes below are
    /// required for EF to discover the migration; the model snapshot is
    /// updated alongside.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260912090000_AddParentSecurityStamp")]
    public partial class AddParentSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "Parents",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Per-row backfill: lower-case hex of 16 CSPRNG bytes, the same
            // 32-char shape ParentService.GenerateSecurityStamp mints.
            migrationBuilder.Sql(
                "UPDATE \"Parents\" SET \"SecurityStamp\" = lower(hex(randomblob(16))) " +
                "WHERE \"SecurityStamp\" = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Parents");
        }
    }
}
