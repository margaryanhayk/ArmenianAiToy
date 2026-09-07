using ArmenianAiToy.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <summary>
    /// After-story question parent toggle: NOT NULL bool on Device,
    /// backfilled TRUE, so every toy already in the field keeps asking its
    /// one reflection question after a story and a parent opts OUT rather
    /// than discovering the feature by opting in. Same posture as B3's
    /// StoryIntroEnabled and the AddStoryFeatureToggles pair.
    ///
    /// Hand-written rather than scaffolded — dotnet-ef cannot run against
    /// this solution (see AddDeviceSdCardOk). Note the explicit
    /// <c>defaultValue: true</c>: a scaffolded bool column backfills FALSE
    /// (the scaffolder reads the CLR type, not the <c>= true</c>
    /// initializer), which would have silently switched the question off on
    /// every existing toy while the entity, DTO, manifest and dashboard all
    /// said ON. BOTH attributes below are required for EF to discover the
    /// migration; the model snapshot is updated alongside (no
    /// HasDefaultValue there — the default lives in C#, like the pauses /
    /// variant-endings columns).
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260907090000_AddDeviceStoryQuestionsEnabled")]
    public partial class AddDeviceStoryQuestionsEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StoryQuestionsEnabled",
                table: "Devices",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoryQuestionsEnabled",
                table: "Devices");
        }
    }
}
