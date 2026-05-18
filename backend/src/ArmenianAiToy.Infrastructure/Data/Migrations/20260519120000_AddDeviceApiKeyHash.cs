using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceApiKeyHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the old plain-unique index. SQLite's behavior of treating
            // NULL values as distinct in a UNIQUE index would technically
            // keep this working after the column is nullable, but the
            // filtered variant below makes the intent explicit and matches
            // the GoogleSubject pattern already used elsewhere.
            migrationBuilder.DropIndex(
                name: "IX_Devices_ApiKey",
                table: "Devices");

            // Make the legacy ApiKey column nullable. Existing rows keep
            // their plaintext value until the first successful auth lazy-
            // upgrades them; new registrations write null here.
            migrationBuilder.AlterColumn<string>(
                name: "ApiKey",
                table: "Devices",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            // Add the primary hashed-credential column.
            migrationBuilder.AddColumn<string>(
                name: "ApiKeyHash",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            // Filtered unique index on the legacy column. Multiple null
            // ApiKey values (the steady state once every row has been
            // upgraded) coexist freely; the historical uniqueness contract
            // is preserved for any row still carrying plaintext.
            migrationBuilder.CreateIndex(
                name: "IX_Devices_ApiKey",
                table: "Devices",
                column: "ApiKey",
                unique: true,
                filter: "\"ApiKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverting drops the hash column. Any row whose ApiKey was
            // already lazy-upgraded to hash-only will have null in ApiKey
            // and will be unusable after the Down migration completes —
            // the AlterColumn back to NOT NULL would fail in that case.
            // Operators rolling back must therefore first re-generate
            // plaintext keys (re-register devices) or restore from backup.
            migrationBuilder.DropIndex(
                name: "IX_Devices_ApiKey",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ApiKeyHash",
                table: "Devices");

            migrationBuilder.AlterColumn<string>(
                name: "ApiKey",
                table: "Devices",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_ApiKey",
                table: "Devices",
                column: "ApiKey",
                unique: true);
        }
    }
}
