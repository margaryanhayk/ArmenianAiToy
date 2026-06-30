using ArmenianAiToy.Application.Stories;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins the additive "3–4 conclusions per story" behavior: the
/// <see cref="CuratedStory"/> selector and the
/// <see cref="StoryFileParser"/> schema handling. Conclusions are an
/// OPTIONAL pool of closing "meaning" lines; when absent the story
/// keeps its single <see cref="CuratedStory.ReflectionText"/> ending,
/// so every pre-existing story and every existing caller is
/// unaffected.
/// </summary>
public class CuratedStoryConclusionTests
{
    private static CuratedStory StoryWith(IReadOnlyList<string>? conclusions) =>
        new(
            Id: "test-conc",
            Title: "Փորձ",
            MinAge: 4,
            MaxAge: 7,
            Tone: "warm",
            Segments: [new CuratedStorySegment(0, "Մի փոքրիկ պատմություն։")],
            ReflectionText: "Միակ վերջաբանը։",
            ReflectionQuestions: ["Ի՞նչ սովորեցինք։"],
            BedtimeSafe: true,
            Conclusions: conclusions);

    [Fact]
    public void EffectiveConclusions_NoPool_FallsBackToReflectionText()
    {
        var story = StoryWith(conclusions: null);

        var effective = story.EffectiveConclusions;

        Assert.Single(effective);
        Assert.Equal("Միակ վերջաբանը։", effective[0]);
    }

    [Fact]
    public void EffectiveConclusions_EmptyPool_FallsBackToReflectionText()
    {
        var story = StoryWith(conclusions: []);

        Assert.Single(story.EffectiveConclusions);
        Assert.Equal("Միակ վերջաբանը։", story.EffectiveConclusions[0]);
    }

    [Fact]
    public void EffectiveConclusions_WithPool_UsesPoolVerbatim()
    {
        var pool = new[] { "Առաջին։", "Երկրորդ։", "Երրորդ։" };
        var story = StoryWith(pool);

        Assert.Equal(pool, story.EffectiveConclusions);
    }

    [Fact]
    public void SelectConclusion_NoPool_AlwaysReturnsReflectionText()
    {
        var story = StoryWith(conclusions: null);

        Assert.Equal("Միակ վերջաբանը։", story.SelectConclusion(0));
        Assert.Equal("Միակ վերջաբանը։", story.SelectConclusion(7));
        Assert.Equal("Միակ վերջաբանը։", story.SelectConclusion(-3));
    }

    [Fact]
    public void SelectConclusion_IsDeterministic_SameSeedSamePick()
    {
        var story = StoryWith(["Ա։", "Բ։", "Գ։"]);

        Assert.Equal(story.SelectConclusion(42), story.SelectConclusion(42));
    }

    [Theory]
    [InlineData(0, "Ա։")]
    [InlineData(1, "Բ։")]
    [InlineData(2, "Գ։")]
    [InlineData(3, "Ա։")]   // wraps
    [InlineData(-1, "Գ։")]  // negative seed → safe positive modulo
    public void SelectConclusion_WrapsAndStaysInRange(int seed, string expected)
    {
        var story = StoryWith(["Ա։", "Բ։", "Գ։"]);

        Assert.Equal(expected, story.SelectConclusion(seed));
    }

    [Fact]
    public void SelectConclusion_DifferentSeeds_CanPickDifferentEndings()
    {
        var story = StoryWith(["Ա։", "Բ։", "Գ։"]);

        var picks = new HashSet<string>
        {
            story.SelectConclusion(0),
            story.SelectConclusion(1),
            story.SelectConclusion(2),
        };

        Assert.Equal(3, picks.Count); // variety: all three reachable
    }

    // ---- schema (StoryFileParser) ----

    private const string BaseFields = """
        "schemaVersion": 1,
        "id": "test-conc",
        "title": "Փորձ",
        "minAge": 4,
        "maxAge": 7,
        "tone": "warm",
        "tags": ["kindness"],
        "category": "general",
        "bedtimeSafe": true,
        "segments": ["Մի փոքրիկ պատմություն։"],
        "reflectionText": "Միակ վերջաբանը։",
        "reflectionQuestions": ["Ի՞նչ սովորեցինք։"],
        "review": { "status": "draft" }
        """;

    [Fact]
    public void Parse_WithConclusions_PopulatesPool()
    {
        var json = "{" + """
            "conclusions": ["Առաջին։", "Երկրորդ։", "Երրորդ։"],
            """ + BaseFields + "}";

        var story = StoryFileParser.Parse(json, "test-conc.story.json", requireApproved: false);

        Assert.Equal(new[] { "Առաջին։", "Երկրորդ։", "Երրորդ։" }, story.EffectiveConclusions);
    }

    [Fact]
    public void Parse_WithoutConclusions_FallsBackToReflectionText()
    {
        var json = "{" + BaseFields + "}";

        var story = StoryFileParser.Parse(json, "test-conc.story.json", requireApproved: false);

        Assert.Single(story.EffectiveConclusions);
        Assert.Equal("Միակ վերջաբանը։", story.EffectiveConclusions[0]);
    }

    [Fact]
    public void Parse_TooManyConclusions_Throws()
    {
        var json = "{" + """
            "conclusions": ["Ա։", "Բ։", "Գ։", "Դ։", "Ե։"],
            """ + BaseFields + "}";

        var ex = Assert.Throws<InvalidDataException>(
            () => StoryFileParser.Parse(json, "test-conc.story.json", requireApproved: false));
        Assert.Contains("conclusions", ex.Message);
    }

    [Fact]
    public void Parse_BlankConclusion_Throws()
    {
        var json = "{" + """
            "conclusions": ["Ա։", "  "],
            """ + BaseFields + "}";

        Assert.Throws<InvalidDataException>(
            () => StoryFileParser.Parse(json, "test-conc.story.json", requireApproved: false));
    }
}
