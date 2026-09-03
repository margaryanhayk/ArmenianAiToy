using System.Reflection;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Stories;
using ArmenianAiToy.Infrastructure.Audio;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Content-library Slice 1: pins the *.story.json schema, the strict
/// parser, and the embedded-resource loader — all against a TEST-ONLY
/// fixture embedded in this test assembly. The Application assembly
/// embeds no story content yet; InMemoryCuratedStoryLibrary remains the
/// canonical runtime-dead library until the migration slice.
/// </summary>
public class EmbeddedCuratedStoryLibraryTests
{
    private static readonly Assembly TestAssembly = typeof(EmbeddedCuratedStoryLibraryTests).Assembly;
    private const string FixturePrefix = "ArmenianAiToy.Application.Tests.Fixtures.Stories";

    private static EmbeddedCuratedStoryLibrary LoadFixtureLibrary() =>
        new(TestAssembly, FixturePrefix);

    // A valid baseline matching the parser's strict schema; individual
    // tests mutate it to prove each rejection path.
    private const string ValidJson = """
        {
          "schemaVersion": 1,
          "id": "valid-story",
          "title": "Փորձնական վերնագիր",
          "minAge": 4,
          "maxAge": 7,
          "tone": "warm",
          "tags": ["nature"],
          "category": "general",
          "bedtimeSafe": true,
          "segments": ["Առաջին հատվածն է։", "Երկրորդ հատվածն է։"],
          "reflectionText": "Հանգիստ միտք է։",
          "reflectionQuestions": ["Իսկ դու ի՞նչ ես մտածում։"],
          "review": {
            "status": "approved",
            "linguisticReviewAt": "2026-06-10",
            "listenTestAt": "2026-06-10",
            "notes": null
          }
        }
        """;

    // ── Loader: happy path against the embedded fixture ─────────────

    [Fact]
    public void Loader_LoadsValidEmbeddedFixture()
    {
        var library = LoadFixtureLibrary();
        var story = library.GetById("test-star");

        Assert.NotNull(story);
        Assert.Equal("Փոքրիկ աստղիկը", story!.Title);
        Assert.Equal(4, story.MinAge);
        Assert.Equal(7, story.MaxAge);
        Assert.Equal("warm", story.Tone);
        Assert.True(story.BedtimeSafe);
        Assert.Equal(2, story.Segments.Count);
    }

    [Fact]
    public void Loader_MapsSegmentIndexesPositionally()
    {
        var story = LoadFixtureLibrary().GetById("test-star")!;

        for (var i = 0; i < story.Segments.Count; i++)
        {
            Assert.Equal(i, story.Segments[i].Index);
        }
    }

    [Fact]
    public void Loader_PreservesStringsVerbatim()
    {
        var story = LoadFixtureLibrary().GetById("test-star")!;

        Assert.Equal(
            "Երկնքում փայլում էր մի փոքրիկ աստղիկ։ Նա սիրում էր նայել քնած քաղաքին։",
            story.Segments[0].Text);
        Assert.Equal(
            "Աստղիկը կամաց ժպտաց։ Հետո նա էլ քնեց իր ամպե բարձիկի վրա։",
            story.Segments[1].Text);
        Assert.Equal("Գիշերը երկինքն էլ է հանգստանում։", story.ReflectionText);
        Assert.Equal("Իսկ դու սիրու՞մ ես աստղեր նայել։", Assert.Single(story.ReflectionQuestions));
    }

    [Fact]
    public void Loader_GetById_UnknownId_ReturnsNull()
    {
        Assert.Null(LoadFixtureLibrary().GetById("no-such-story"));
    }

    [Fact]
    public void Loader_SelectDefault_IsDeterministicAcrossInstances()
    {
        Assert.Equal("test-star", LoadFixtureLibrary().SelectDefault().Id);
        Assert.Equal("test-star", LoadFixtureLibrary().SelectDefault().Id);
    }

    [Fact]
    public void Loader_EmptyPrefix_ZeroResources_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => new EmbeddedCuratedStoryLibrary(TestAssembly, "No.Such.Prefix"));
    }

    // ── Parser: strict schema rejections ─────────────────────────────

    [Fact]
    public void Parser_AcceptsValidBaseline()
    {
        var story = StoryFileParser.Parse(ValidJson, "test");

        Assert.Equal("valid-story", story.Id);
        Assert.Equal(2, story.Segments.Count);
    }

    // ── origin: provenance for a story nobody wrote ──────────────────
    // Added 2026-09-03 after the owner's listen test heard the two folk
    // tales' intros stop after the title, with nothing where the other
    // eight stories name an author.

    [Fact]
    public void Parser_AcceptsOrigin_WhenThereIsNoAuthor()
    {
        var json = ValidJson.Replace(
            "\"title\": \"Փորձնական վերնագիր\",",
            "\"title\": \"Փորձնական վերնագիր\", \"origin\": \"Ժողովրդական հեքիաթ\",");

        var story = StoryFileParser.Parse(json, "test");

        Assert.Equal("Ժողովրդական հեքիաթ", story.Origin);
        Assert.Null(story.Author);
    }

    [Fact]
    public void Parser_RejectsBlankOrigin()
    {
        var json = ValidJson.Replace(
            "\"title\": \"Փորձնական վերնագիր\",",
            "\"title\": \"Փորձնական վերնագիր\", \"origin\": \"   \",");

        var ex = Assert.Throws<InvalidDataException>(() => StoryFileParser.Parse(json, "test"));
        Assert.Contains("origin is present but blank", ex.Message);
    }

    // KEYSTONE. A story is authored or it is not. Both fields set would mean
    // a named author had also been called folk -- one of the two is then a
    // false statement, and the intro composer would be picking which to
    // speak to a child.
    [Fact]
    public void Parser_RejectsAuthorAndOriginTogether()
    {
        var json = ValidJson.Replace(
            "\"title\": \"Փորձնական վերնագիր\",",
            "\"title\": \"Փորձնական վերնագիր\", \"author\": \"Հովհաննես Թումանյան\", "
                + "\"origin\": \"Ժողովրդական հեքիաթ\",");

        var ex = Assert.Throws<InvalidDataException>(() => StoryFileParser.Parse(json, "test"));
        Assert.Contains("mutually exclusive", ex.Message);
    }

    [Fact]
    public void Parser_RejectsUnknownJsonField()
    {
        var json = ValidJson.Replace("\"tone\": \"warm\",", "\"tone\": \"warm\", \"surprise\": true,");

        var ex = Assert.Throws<InvalidDataException>(() => StoryFileParser.Parse(json, "test"));
        Assert.Contains("test", ex.Message);
    }

    [Fact]
    public void Parser_RejectsMissingRequiredField()
    {
        var json = ValidJson.Replace("\"title\": \"Փորձնական վերնագիր\",", "");

        Assert.Throws<InvalidDataException>(() => StoryFileParser.Parse(json, "test"));
    }

    [Fact]
    public void Parser_RejectsUnsupportedSchemaVersion()
    {
        var json = ValidJson.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2");

        var ex = Assert.Throws<InvalidDataException>(() => StoryFileParser.Parse(json, "test"));
        Assert.Contains("schemaVersion", ex.Message);
    }

    [Fact]
    public void Parser_RejectsNonApprovedStory_WhenRequireApproved()
    {
        var json = ValidJson.Replace("\"status\": \"approved\"", "\"status\": \"draft\"");

        var ex = Assert.Throws<InvalidDataException>(() => StoryFileParser.Parse(json, "test"));
        Assert.Contains("approved", ex.Message);
    }

    [Fact]
    public void Parser_AllowsDraft_WhenRequireApprovedIsFalse()
    {
        var json = ValidJson.Replace("\"status\": \"approved\"", "\"status\": \"draft\"");

        var story = StoryFileParser.Parse(json, "test", requireApproved: false);

        Assert.Equal("valid-story", story.Id);
    }

    [Fact]
    public void Parser_AllowsSource_WhenRequireApprovedIsFalse()
    {
        // "source" is the exact-reference-import status (see
        // backend/content/source-stories/): lintable like a draft…
        var json = ValidJson.Replace("\"status\": \"approved\"", "\"status\": \"source\"");

        var story = StoryFileParser.Parse(json, "test", requireApproved: false);

        Assert.Equal("valid-story", story.Id);
    }

    [Fact]
    public void Parser_RejectsSourceStory_WhenRequireApproved()
    {
        // …but structurally unservable: the runtime posture accepts
        // ONLY "approved". A source import can never load at runtime.
        var json = ValidJson.Replace("\"status\": \"approved\"", "\"status\": \"source\"");

        var ex = Assert.Throws<InvalidDataException>(() => StoryFileParser.Parse(json, "test"));
        Assert.Contains("approved", ex.Message);
    }

    [Fact]
    public void Parser_RejectsStatusOutsideVocabulary_EvenWithRequireApprovedFalse()
    {
        // The vocabulary is exactly draft | approved | source — any
        // other status fails even at linting posture.
        foreach (var badStatus in new[] { "Source", "imported", "reference", "pending", "" })
        {
            var json = ValidJson.Replace("\"status\": \"approved\"", $"\"status\": \"{badStatus}\"");

            var ex = Assert.Throws<InvalidDataException>(
                () => StoryFileParser.Parse(json, "test", requireApproved: false));
            Assert.Contains("status", ex.Message);
        }
    }

    [Fact]
    public void Parser_RejectsApprovedStoryMissingListenTest()
    {
        var json = ValidJson.Replace("\"listenTestAt\": \"2026-06-10\"", "\"listenTestAt\": null");

        var ex = Assert.Throws<InvalidDataException>(() => StoryFileParser.Parse(json, "test"));
        Assert.Contains("listenTestAt", ex.Message);
    }

    [Fact]
    public void Parser_RejectsInvalidCategory()
    {
        var json = ValidJson.Replace("\"category\": \"general\"", "\"category\": \"folklore\"");

        var ex = Assert.Throws<InvalidDataException>(() => StoryFileParser.Parse(json, "test"));
        Assert.Contains("category", ex.Message);
    }

    [Fact]
    public void Parser_RejectsTagOutsideAllowedVocabulary()
    {
        var json = ValidJson.Replace("\"tags\": [\"nature\"]", "\"tags\": [\"random-new-tag\"]");

        var ex = Assert.Throws<InvalidDataException>(() => StoryFileParser.Parse(json, "test"));
        Assert.Contains("tag", ex.Message);
    }

    [Fact]
    public void Parser_RejectsNonKebabCaseId()
    {
        foreach (var badId in new[] { "Valid-Story", "valid_story", "valid-story-", "-valid", "story7" })
        {
            var json = ValidJson.Replace("\"id\": \"valid-story\"", $"\"id\": \"{badId}\"");
            Assert.Throws<InvalidDataException>(() => StoryFileParser.Parse(json, "test"));
        }
    }

    [Fact]
    public void Parser_RejectsEmptySegments()
    {
        var json = ValidJson.Replace(
            "\"segments\": [\"Առաջին հատվածն է։\", \"Երկրորդ հատվածն է։\"]",
            "\"segments\": []");

        Assert.Throws<InvalidDataException>(() => StoryFileParser.Parse(json, "test"));
    }

    [Fact]
    public void Parser_RejectsInvalidAgeRange()
    {
        var json = ValidJson.Replace("\"maxAge\": 7", "\"maxAge\": 3");

        var ex = Assert.Throws<InvalidDataException>(() => StoryFileParser.Parse(json, "test"));
        Assert.Contains("age", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Slice 2: real content migration ──────────────────────────────

    [Fact]
    public void RealContent_ApplicationAssembly_EmbedsExactlyTheApprovedLibrary()
    {
        // THE inventory test: an exact list, so a story can never reach a
        // child by accident. Adding one here is the deliberate act that says
        // "this is approved to be spoken to children" — which is why the
        // assertion stays exact rather than a count or a subset.
        // 2026-08-03: owner promoted 12 stories (the heqiat.am selection plus
        // the in-project originals) alongside the two launch stories.
        var real = new EmbeddedCuratedStoryLibrary(
            typeof(CuratedStory).Assembly,
            "ArmenianAiToy.Application.Stories.Content");

        var ids = real.ListAvailable().Select(s => s.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
        Assert.Equal(
            [
                "anban-huri", "hedgehog-apple", "khosogh-dzuk", "little-cloud",
                "pochat-aghves", "princess-and-pea", "sutasan", "sutlik-orskan",
                "three-piglets", "ulik",
            ],
            ids);
    }

    [Fact]
    public void RealContent_WrapperAndDirectLoader_ServeIdenticalStories()
    {
        // The compatibility wrapper must be a pure pass-through over the
        // embedded content — same object data, no transformation. The
        // byte-pins in CuratedStoryLibraryTests prove the loaded text
        // equals the pre-migration hardcoded strings; this test proves
        // the wrapper adds nothing on top of the loader.
        var direct = new EmbeddedCuratedStoryLibrary(
            typeof(CuratedStory).Assembly,
            "ArmenianAiToy.Application.Stories.Content");
        var wrapper = new InMemoryCuratedStoryLibrary();

        foreach (var id in new[] { "little-cloud", "hedgehog-apple" })
        {
            var fromDirect = direct.GetById(id)!;
            var fromWrapper = wrapper.GetById(id)!;
            Assert.Equal(fromDirect.Title, fromWrapper.Title);
            Assert.Equal(
                fromDirect.Segments.Select(s => s.Text),
                fromWrapper.Segments.Select(s => s.Text));
            Assert.Equal(fromDirect.ReflectionText, fromWrapper.ReflectionText);
            Assert.Equal(fromDirect.ReflectionQuestions, fromWrapper.ReflectionQuestions);
        }
    }

    [Fact]
    public void RealContent_WrapperSelectDefault_RemainsLittleCloud()
    {
        // Ordinal-first in the embedded set is "hedgehog-apple"; the
        // wrapper deliberately pins the pre-migration default so the
        // migration cannot silently change serving behavior.
        Assert.Equal("little-cloud", new InMemoryCuratedStoryLibrary().SelectDefault().Id);
    }

    [Fact]
    public void Loader_DuplicateIds_AcrossResources_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() => new EmbeddedCuratedStoryLibrary(
            TestAssembly,
            "ArmenianAiToy.Application.Tests.Fixtures.DuplicateIdStories"));
        Assert.Contains("dup-story", ex.Message);
    }

    // ── Runtime-dead pin ─────────────────────────────────────────────

    [Fact]
    public void NoRuntimeAssembly_ImplementsCuratedStoryLibrary()
    {
        // Wiring the library into the live flow would require an
        // implementation or adapter in Api/Infrastructure. Pin that the
        // only implementations live in Application (InMemory + Embedded
        // + the bench/local Configurable decorator) and that
        // Api/Infrastructure carry none.
        var apiTypes = typeof(ChatController).Assembly.GetTypes();
        var infraTypes = typeof(LocalDiskAudioBlobStore).Assembly.GetTypes();

        Assert.DoesNotContain(apiTypes, t => typeof(ICuratedStoryLibrary).IsAssignableFrom(t) && !t.IsInterface);
        Assert.DoesNotContain(infraTypes, t => typeof(ICuratedStoryLibrary).IsAssignableFrom(t) && !t.IsInterface);

        var applicationImpls = typeof(ICuratedStoryLibrary).Assembly.GetTypes()
            .Where(t => typeof(ICuratedStoryLibrary).IsAssignableFrom(t) && !t.IsInterface)
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(
            ["ConfigurableCuratedStoryLibrary", "EmbeddedCuratedStoryLibrary", "InMemoryCuratedStoryLibrary"],
            applicationImpls);
    }
}
