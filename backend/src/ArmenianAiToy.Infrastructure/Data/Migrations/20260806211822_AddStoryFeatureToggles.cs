using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArmenianAiToy.Infrastructure.Data.Migrations
{
    /// <summary>
    /// The two story-shaping parent toggles: in-story pauses and variant
    /// endings. Both NOT NULL bools on Device, both backfilled TRUE, so every
    /// device already in the field keeps the full authored story experience
    /// and a parent has to opt OUT rather than discover a feature by opting
    /// in. Same posture as B3's StoryIntroEnabled.
    ///
    /// SCAFFOLDED (dotnet ef), then hand-corrected in ONE place: the generated
    /// migration wrote <c>defaultValue: false</c> for both columns. That is
    /// the standard EF trap — the scaffolder reads the CLR type, not the
    /// property initializer (<c>= true</c>), so a bool property always
    /// backfills as false. Left alone it would have shipped both features OFF
    /// on every existing toy while the entity, the DTO, the manifest fallback
    /// and the dashboard all said ON: a silent split-brain that only shows up
    /// as "the toy stopped pausing" on hardware already in a child's hands.
    /// If this migration is ever regenerated, re-apply the two <c>true</c>s.
    ///
    /// The model snapshot deliberately carries no HasDefaultValue for these
    /// columns (the default lives in C#, not in the schema), so the edit below
    /// does not desync it — <c>dotnet ef migrations has-pending-model-changes</c>
    /// stays clean.
    /// </summary>
    public partial class AddStoryFeatureToggles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StoryPausesEnabled",
                table: "Devices",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "VariantEndingsEnabled",
                table: "Devices",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StoryPausesEnabled",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "VariantEndingsEnabled",
                table: "Devices");
        }
    }
}
