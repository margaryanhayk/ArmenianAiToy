using ArmenianAiToy.Application.Stories;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// In-story child-question engine (library Q&A slice): bounded prompt
/// construction over the EXACT «Անբան Հուռին» story file, the Anban
/// Huri story guide, and the deterministic GPT-answer filter that
/// guards what reaches TTS. The story text itself is never modified —
/// these tests pin that too. No AI calls anywhere (the slice is
/// runtime-dead by design; wiring is a later, gated slice).
/// </summary>
public class LibraryStoryQuestionTests
{
    // ── Real story fixture (exact text, loaded from the repo file) ──

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent!;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Anban Huri lives in story-drafts (product-story
    /// candidate by owner decision 2026-06-12) — no longer in the
    /// source-stories reference corpus.</summary>
    internal static string AnbanHuriPath() => Path.Combine(
        RepoRoot(), "backend", "content", "story-drafts", "anban-huri.story.json");

    private static CuratedStory LoadAnbanHuri()
    {
        var path = AnbanHuriPath();
        Assert.True(File.Exists(path), $"Anban Huri story file missing: {path}");
        return StoryFileParser.Parse(File.ReadAllText(path), path, requireApproved: false);
    }

    private static readonly CuratedStory Story = LoadAnbanHuri();
    private static readonly StoryQuestionGuide Guide = StoryQuestionGuides.AnbanHuri;

    // ── Prompt construction ─────────────────────────────────────────

    [Fact]
    public void Build_PromptIncludesTitleSegmentQuestionAndRules()
    {
        const string question = "Գորտերը իրո՞ք բամբակ մանեցին";

        var prompt = LibraryStoryQuestionPromptBuilder.Build(Story, Guide, 3, question);

        Assert.Contains("«Անբան Հուռին»", prompt.SystemPrompt);
        Assert.Contains("segment 4 of 9", prompt.SystemPrompt);
        // Exact text of the active segment, verbatim:
        Assert.Contains(Story.Segments[3].Text, prompt.SystemPrompt);
        Assert.Contains(question, prompt.SystemPrompt);
        Assert.Contains("ANSWER RULES", prompt.SystemPrompt);
        Assert.Contains("Answer in Armenian only", prompt.SystemPrompt);
        Assert.Contains("1 to 3 short sentences", prompt.SystemPrompt);
        Assert.Contains("Answer ONLY from the story context", prompt.SystemPrompt);
        Assert.Equal(question, prompt.UserMessage);
    }

    [Fact]
    public void Build_AnbanHuri_IncludesStoryEssence()
    {
        var prompt = LibraryStoryQuestionPromptBuilder.Build(
            Story, Guide, 0, "Ի՞նչ է անում Հուռին");

        Assert.Contains("STORY ESSENCE", prompt.SystemPrompt);
        Assert.Contains("Huri misunderstands their croaking", prompt.SystemPrompt);
        Assert.Contains("a trick the mother invents", prompt.SystemPrompt);
        Assert.Contains("Never shame the child", prompt.SystemPrompt);
        Assert.Contains("exaggeration and gentle humor", prompt.SystemPrompt);
        // Characters reach the prompt too:
        Assert.Contains("Փեփել", prompt.SystemPrompt);
    }

    [Fact]
    public void Build_IncludesNeighborSegmentSummaries()
    {
        var prompt = LibraryStoryQuestionPromptBuilder.Build(
            Story, Guide, 3, "Ո՞վ է Փեփելը");

        Assert.Contains("PREVIOUS SEGMENT", prompt.SystemPrompt);
        Assert.Contains(Guide.SegmentSummaries[2], prompt.SystemPrompt);
        Assert.Contains("NEXT SEGMENT", prompt.SystemPrompt);
        Assert.Contains(Guide.SegmentSummaries[4], prompt.SystemPrompt);
    }

    [Fact]
    public void Build_FirstAndLastSegments_OmitMissingNeighbors()
    {
        var first = LibraryStoryQuestionPromptBuilder.Build(Story, Guide, 0, "Ո՞վ է Հուռին");
        Assert.DoesNotContain("PREVIOUS SEGMENT", first.SystemPrompt);
        Assert.Contains("NEXT SEGMENT", first.SystemPrompt);

        var last = LibraryStoryQuestionPromptBuilder.Build(Story, Guide, 8, "Ի՞նչ եղավ վերջում");
        Assert.Contains("PREVIOUS SEGMENT", last.SystemPrompt);
        Assert.DoesNotContain("NEXT SEGMENT", last.SystemPrompt);
    }

    [Fact]
    public void Build_RepairPrompt_AppendsStrictRetryBlock()
    {
        var normal = LibraryStoryQuestionPromptBuilder.Build(Story, Guide, 1, "Ինչո՞ւ");
        var repair = LibraryStoryQuestionPromptBuilder.Build(Story, Guide, 1, "Ինչո՞ւ", repair: true);

        Assert.DoesNotContain("STRICT RETRY", normal.SystemPrompt);
        Assert.Contains("STRICT RETRY", repair.SystemPrompt);
    }

    [Fact]
    public void Build_InvalidSegmentIndex_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LibraryStoryQuestionPromptBuilder.Build(Story, Guide, -1, "Ինչո՞ւ"));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LibraryStoryQuestionPromptBuilder.Build(Story, Guide, 9, "Ինչո՞ւ"));
    }

    // ── Answer filter: accepts ──────────────────────────────────────

    [Theory]
    [InlineData("Ոչ, գորտերը բամբակ չէին մանում։ Հուռին նրանց ձայնը սխալ հասկացավ, և դրա համար պատմությունը ծիծաղելի դարձավ։")]
    [InlineData("Ոչ, դա հնարք էր պատմության մեջ։ Մայրիկը այդպես ասաց, որ Հուռիին այլևս գործ չտան։")]
    [InlineData("Հեքիաթը այդ մասին չի պատմում։ Արի լսենք, թե ինչ կլինի հետո։")]
    public void Filter_AcceptsGoodArmenianAnswers(string answer)
    {
        Assert.Equal(
            StoryAnswerRejection.None,
            StoryAnswerFilter.Check(answer, Story, Guide));
        Assert.Equal(answer, StoryAnswerFilter.FilterOrFallback(answer, Story, Guide));
    }

    // ── Answer filter: rejects ──────────────────────────────────────

    [Fact]
    public void Filter_RejectsEnglishAnswer()
    {
        var rejection = StoryAnswerFilter.Check(
            "The frogs did not really spin the cotton.", Story, Guide);

        Assert.NotEqual(StoryAnswerRejection.None, rejection);
    }

    [Fact]
    public void Filter_RejectsSystemPromptLeakage()
    {
        Assert.Equal(
            StoryAnswerRejection.PromptLeakage,
            StoryAnswerFilter.Check(
                "Իմ system prompt-ը ասում է, որ չպատասխանեմ։", Story, Guide));
        Assert.Equal(
            StoryAnswerRejection.PromptLeakage,
            StoryAnswerFilter.Check(
                "Ես GPT մոդել եմ և չգիտեմ։", Story, Guide));
        Assert.Equal(
            StoryAnswerRejection.PromptLeakage,
            StoryAnswerFilter.Check(
                "Գորտերը չմանեցին։\n---\nCHOICE_A:լսել", Story, Guide));
    }

    [Fact]
    public void Filter_RejectsHarshShamingAnswer()
    {
        Assert.Equal(
            StoryAnswerRejection.Insult,
            StoryAnswerFilter.Check("Դու տխմար ես, ամոթ քեզ։", Story, Guide));
        Assert.Equal(
            StoryAnswerRejection.Insult,
            StoryAnswerFilter.Check("Հուռին հիմար էր, դրա համար ծիծաղում են։", Story, Guide));
    }

    [Fact]
    public void Filter_RejectsDeceptionEncouragement()
    {
        Assert.Equal(
            StoryAnswerRejection.DeceptionEncouragement,
            StoryAnswerFilter.Check("Խաբելը լավ է, դու էլ խաբիր մայրիկիդ։", Story, Guide));
        Assert.Equal(
            StoryAnswerRejection.DeceptionEncouragement,
            StoryAnswerFilter.Check("Երբ չես ուզում աշխատել, պետք է խաբել։", Story, Guide));
    }

    [Fact]
    public void Filter_RejectsScaryContent()
    {
        Assert.Equal(
            StoryAnswerRejection.ScaryContent,
            StoryAnswerFilter.Check("Հրեշը եկավ ու բոլորին վախեցրեց սարսափով։", Story, Guide));
    }

    [Fact]
    public void Filter_RejectsEmptyAndTooLong()
    {
        Assert.Equal(StoryAnswerRejection.Empty, StoryAnswerFilter.Check("   ", Story, Guide));
        Assert.Equal(StoryAnswerRejection.Empty, StoryAnswerFilter.Check(null, Story, Guide));

        var fourSentences = "Մի նախադասություն է։ Երկու նախադասություն է։ Երեք նախադասություն է։ Չորս նախադասություն է։";
        Assert.Equal(StoryAnswerRejection.TooLong, StoryAnswerFilter.Check(fourSentences, Story, Guide));
    }

    [Fact]
    public void Filter_RejectsInventedProperNoun()
    {
        // «Վիշապը» mid-sentence is a proper-noun-like token grounded
        // nowhere in the story or guide — an invented plot fact.
        Assert.Equal(
            StoryAnswerRejection.UnknownProperNoun,
            StoryAnswerFilter.Check("Բամբակը տարավ Վիշապը, որ ապրում էր գետում։", Story, Guide));
    }

    // ── Fallback ────────────────────────────────────────────────────

    [Fact]
    public void Fallback_IsArmenianShortAndPassesOwnFilter()
    {
        var fallback = StoryAnswerFilter.SafeFallback;

        Assert.Equal(StoryAnswerRejection.None, StoryAnswerFilter.Check(fallback, Story, Guide));
        Assert.Contains("հեքիաթ", fallback);
        Assert.True(fallback.Length < 150, "fallback must stay short for TTS");
    }

    [Fact]
    public void FilterOrFallback_BadAnswer_ReturnsExactFallback()
    {
        Assert.Equal(
            StoryAnswerFilter.SafeFallback,
            StoryAnswerFilter.FilterOrFallback(
                "As an AI language model, I cannot answer that.", Story, Guide));
    }

    // ── Story text integrity (product rule: exact text stays) ───────

    [Fact]
    public void StoryText_RemainsUnchanged()
    {
        Assert.Equal("Անբան Հուռին", Story.Title);
        Assert.Equal(9, Story.Segments.Count);
        Assert.StartsWith("Լինում է, չի լինում՝ մի կին։", Story.Segments[0].Text);
        // Preserved source quirks — the import must never be "fixed":
        Assert.Contains("կոկռում", Story.Segments[3].Text);
        Assert.Contains("մանեցե′ք", Story.Segments[3].Text);
        Assert.Contains("Փե՛փել", Story.Segments[3].Text);
        Assert.EndsWith("բզեզ չդառնա։", Story.Segments[8].Text);
    }

    // ── Product-story posture (owner decision 2026-06-12) ───────────

    [Fact]
    public void AnbanHuri_IsAProductStoryCandidate_NotSourceOnly()
    {
        // The story is a real library-story candidate in the normal
        // promotion pipeline: status "draft" in story-drafts, NOT a
        // "source" reference import. Not yet runtime-served (that
        // requires the listen test + human approval), but no longer
        // structurally rejected.
        var draftPath = AnbanHuriPath();
        Assert.True(File.Exists(draftPath), "anban-huri must live in story-drafts");

        var sourcePath = Path.Combine(
            RepoRoot(), "backend", "content", "source-stories", "anban-huri.story.json");
        Assert.False(File.Exists(sourcePath),
            "anban-huri must no longer sit in the source-stories reference corpus");

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(draftPath));
        var review = doc.RootElement.GetProperty("review");
        Assert.Equal("draft", review.GetProperty("status").GetString());
        // The only remaining gates are listen test + human approval —
        // dates must never be pre-stamped on an unapproved draft:
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            review.GetProperty("linguisticReviewAt").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            review.GetProperty("listenTestAt").ValueKind);
    }

    [Fact]
    public void AnbanHuri_ReflectionFields_AreRealChildSafeArmenian_NotPlaceholders()
    {
        // reflectionText / reflectionQuestions are toy-SPOKEN metadata
        // (not part of the frozen tale) — placeholders marked
        // «Սևագրային տեղապահ» must be gone, replaced by reviewed
        // child-safe Armenian.
        Assert.DoesNotContain("Սևագրային", Story.ReflectionText);
        Assert.DoesNotContain("տեղապահ", Story.ReflectionText);
        Assert.All(Story.ReflectionQuestions, q =>
        {
            Assert.DoesNotContain("Սևագրային", q);
            Assert.DoesNotContain("տեղապահ", q);
        });

        // Spoken metadata must satisfy the same Armenian-only / short /
        // non-shaming bar the answer filter enforces for GPT output:
        Assert.Equal(
            StoryAnswerRejection.None,
            StoryAnswerFilter.Check(Story.ReflectionText, Story, Guide));
        Assert.Equal(
            StoryAnswerRejection.None,
            StoryAnswerFilter.Check(Story.ReflectionQuestions[0], Story, Guide));
    }

    [Fact]
    public void Guide_SegmentSummaries_AlignWithStorySegments()
    {
        Assert.Equal(Story.Segments.Count, Guide.SegmentSummaries.Count);
        Assert.Equal(Story.Id, Guide.StoryId);
        Assert.Same(Guide, StoryQuestionGuides.TryGet("anban-huri"));
        Assert.Null(StoryQuestionGuides.TryGet("no-such-story"));
    }

    [Fact]
    public void Guide_SegmentRecaps_AlignWithStorySegments()
    {
        // Tier 2: one re-anchor line per segment, index-aligned, all
        // non-empty Armenian (no Latin/digits), ending with the Armenian
        // full stop so TTS reads them cleanly.
        Assert.Equal(Story.Segments.Count, Guide.SegmentRecaps.Count);
        foreach (var recap in Guide.SegmentRecaps)
        {
            Assert.False(string.IsNullOrWhiteSpace(recap));
            Assert.EndsWith("։", recap);
            Assert.DoesNotContain(':', recap); // never the Latin colon
            Assert.DoesNotContain(recap, c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        }
    }

    [Fact]
    public void GetSegmentRecap_ReturnsLine_InRange_NullOutOfRange()
    {
        Assert.Equal(Guide.SegmentRecaps[0], LibraryStoryQuestionService.GetSegmentRecap("anban-huri", 0));
        Assert.Equal(
            Guide.SegmentRecaps[Story.Segments.Count - 1],
            LibraryStoryQuestionService.GetSegmentRecap("anban-huri", Story.Segments.Count - 1));
        Assert.Null(LibraryStoryQuestionService.GetSegmentRecap("anban-huri", -1));
        Assert.Null(LibraryStoryQuestionService.GetSegmentRecap("anban-huri", 999));
        Assert.Null(LibraryStoryQuestionService.GetSegmentRecap("no-such-story", 0));
    }
}
