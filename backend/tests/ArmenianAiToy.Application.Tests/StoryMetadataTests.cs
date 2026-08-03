using ArmenianAiToy.Application.Stories;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// B1 story-metadata tests: the optional author/goal/lesson fields parse
/// through to CuratedStory, absent fields stay null (back-compat with every
/// pre-B1 story file), and present-but-blank values fail loudly.
/// </summary>
public class StoryMetadataTests
{
    private static string MinimalJson(string extraFields = "") => $$"""
        {
          "schemaVersion": 1,
          "id": "test-story",
          "title": "Թեստ",
          {{extraFields}}
          "minAge": 4,
          "maxAge": 7,
          "tone": "warm",
          "tags": ["kindness"],
          "category": "general",
          "bedtimeSafe": true,
          "segments": ["Մի անգամ..."],
          "reflectionText": "Վերջ։",
          "reflectionQuestions": ["Ի՞նչ սովորեցինք։"],
          "review": { "status": "approved", "listenTestAt": "2026-08-03" }
        }
        """;

    [Fact]
    public void Parse_WithAuthorGoalLesson_CarriesThemThrough()
    {
        var story = StoryFileParser.Parse(MinimalJson("""
            "author": "Հովհաննես Թումանյան",
            "goal": "Նպատակ։",
            "lesson": "Դաս։",
            """), "test");

        Assert.Equal("Հովհաննես Թումանյան", story.Author);
        Assert.Equal("Նպատակ։", story.Goal);
        Assert.Equal("Դաս։", story.Lesson);
    }

    // KEYSTONE back-compat: every pre-B1 story file (no metadata fields)
    // must keep parsing, with nulls.
    [Fact]
    public void Parse_WithoutMetadataFields_NullsAndStillParses()
    {
        var story = StoryFileParser.Parse(MinimalJson(), "test");

        Assert.Null(story.Author);
        Assert.Null(story.Goal);
        Assert.Null(story.Lesson);
    }

    [Theory]
    [InlineData("\"author\": \"  \",")]
    [InlineData("\"goal\": \"\",")]
    [InlineData("\"lesson\": \" \",")]
    public void Parse_PresentButBlankMetadata_FailsLoudly(string field)
    {
        Assert.Throws<InvalidDataException>(
            () => StoryFileParser.Parse(MinimalJson(field), "test"));
    }

    // The embedded library must load with the new fields present in the
    // shipped files — and the five Tumanyan classics carry attribution.
    [Fact]
    public void EmbeddedLibrary_LoadsWithMetadata()
    {
        var stories = new EmbeddedCuratedStoryLibrary(
                typeof(EmbeddedCuratedStoryLibrary).Assembly,
                "ArmenianAiToy.Application.Stories.Content")
            .ListAvailable();

        var anban = stories.Single(s => s.Id == "anban-huri");
        Assert.Equal("Հովհաննես Թումանյան", anban.Author);
        Assert.False(string.IsNullOrWhiteSpace(anban.Goal));
        Assert.False(string.IsNullOrWhiteSpace(anban.Lesson));

        // Unverified / original / folk titles must NOT carry a guessed author.
        Assert.Null(stories.Single(s => s.Id == "ulik").Author);
        Assert.Null(stories.Single(s => s.Id == "three-piglets").Author);

        // Every shipped story has a lesson (drives the library card + the
        // future summary clip).
        Assert.All(stories, s => Assert.False(string.IsNullOrWhiteSpace(s.Lesson)));
    }
}
