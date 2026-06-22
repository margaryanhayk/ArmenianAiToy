using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceChildDobWithBirthYear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #066: store only the birth YEAR. Add the new column FIRST, copy the
            // year out of any existing exact DOB (stored as TEXT "yyyy-MM-dd"),
            // THEN drop the precise date — so no age data is lost on migrate.
            migrationBuilder.AddColumn<int>(
                name: "BirthYear",
                table: "Children",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE \"Children\" SET \"BirthYear\" = CAST(substr(\"DateOfBirth\", 1, 4) AS INTEGER) " +
                "WHERE \"DateOfBirth\" IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "Children");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse, preserving what we can: reconstruct an approximate DOB
            // (Jan 1 of the stored year) before dropping BirthYear.
            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfBirth",
                table: "Children",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE \"Children\" SET \"DateOfBirth\" = \"BirthYear\" || '-01-01' " +
                "WHERE \"BirthYear\" IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "BirthYear",
                table: "Children");
        }
    }
}
