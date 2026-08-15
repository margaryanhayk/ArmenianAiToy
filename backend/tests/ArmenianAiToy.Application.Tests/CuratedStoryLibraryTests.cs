using ArmenianAiToy.Application.Stories;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins the first curated-library slice: the single reviewed Armenian
/// story is returned VERBATIM (byte-identical to the reviewed text),
/// is well-formed for ages 4–7 TTS delivery (Armenian script only,
/// zero Latin codepoints, zero digits), and the library lookup
/// surface behaves deterministically. Nothing here touches the live
/// chat/audio flow — the library is not wired anywhere yet.
/// </summary>
public class CuratedStoryLibraryTests
{
    private readonly InMemoryCuratedStoryLibrary _library = new();

    // The reviewed text, pinned byte-for-byte. If these constants ever
    // need to change, the new text must go through a fresh
    // armenian-story-master linguistic review first.
    private const string ExpectedTitle = "Փոքրիկ ամպիկը";
    private static readonly string[] ExpectedSegments =
    [
        "Երկնքում ապրում էր մի փոքրիկ ամպիկ։ Նա շատ էր սիրում թռչել քամու հետ։ Մի օր ամպիկը տեսավ մի փոքրիկ ծաղիկ։ Ծաղիկը ծարավ էր։",
        "Ամպիկը մոտեցավ ծաղկին։ Նա մի քիչ անձրև մաղեց։ Ծաղիկն ուրախացավ ու բացեց իր թերթիկները։",
        "Ծաղիկն ասաց՝ շնորհակալ եմ, ամպի՛կ ջան։ Ամպիկը ժպտաց ու թռավ երկինք։ Նրանք դարձան լավ ընկերներ։",
    ];
    private const string ExpectedReflection =
        "Երբ օգնում ենք ուրիշներին, մեր սիրտն էլ է ուրախանում։";
    private const string ExpectedQuestion =
        "Իսկ դու ո՞ւմ ես սիրում օգնել։";

    [Fact]
    public void GetById_KnownId_ReturnsStory()
    {
        var story = _library.GetById(InMemoryCuratedStoryLibrary.LittleCloudId);

        Assert.NotNull(story);
        Assert.Equal(InMemoryCuratedStoryLibrary.LittleCloudId, story!.Id);
    }

    [Fact]
    public void GetById_UnknownId_ReturnsNull()
    {
        Assert.Null(_library.GetById("no-such-story"));
    }

    [Fact]
    public void SelectDefault_ReturnsTheSingleStory()
    {
        var story = _library.SelectDefault();

        Assert.Equal(InMemoryCuratedStoryLibrary.LittleCloudId, story.Id);
    }

    [Fact]
    public void ListAvailable_ReturnsTheOriginalLaunchStories()
    {
        var stories = _library.ListAvailable();

        // Count is deliberately not pinned — the library grows as stories are
        // promoted (12 added 2026-08-03). EmbeddedCuratedStoryLibraryTests
        // owns the full inventory; this only guards that the two originals
        // never silently vanish.
        Assert.Contains(stories, s => s.Id == InMemoryCuratedStoryLibrary.LittleCloudId);
        Assert.Contains(stories, s => s.Id == InMemoryCuratedStoryLibrary.HedgehogAppleId);
    }

    [Fact]
    public void Story_HasNonEmptyArmenianTitle()
    {
        var story = _library.SelectDefault();

        Assert.False(string.IsNullOrWhiteSpace(story.Title));
        Assert.Contains(story.Title, c => IsArmenian(c));
    }

    [Fact]
    public void Story_SegmentsAreNonEmptyAndSequentiallyIndexedFromZero()
    {
        var story = _library.SelectDefault();

        Assert.InRange(story.Segments.Count, 2, 3);
        for (var i = 0; i < story.Segments.Count; i++)
        {
            Assert.Equal(i, story.Segments[i].Index);
            Assert.False(string.IsNullOrWhiteSpace(story.Segments[i].Text));
        }
    }

    [Fact]
    public void Story_SegmentText_IsReturnedVerbatim()
    {
        var story = _library.GetById(InMemoryCuratedStoryLibrary.LittleCloudId)!;

        Assert.Equal(ExpectedSegments.Length, story.Segments.Count);
        for (var i = 0; i < ExpectedSegments.Length; i++)
        {
            Assert.Equal(ExpectedSegments[i], story.Segments[i].Text);
        }
    }

    [Fact]
    public void Story_SegmentText_IsStableAcrossRepeatedLookups()
    {
        var first = _library.GetById(InMemoryCuratedStoryLibrary.LittleCloudId)!;
        var second = _library.GetById(InMemoryCuratedStoryLibrary.LittleCloudId)!;

        for (var i = 0; i < first.Segments.Count; i++)
        {
            Assert.Equal(first.Segments[i].Text, second.Segments[i].Text);
        }
    }

    [Fact]
    public void Story_ReflectionTextAndQuestion_ArePinnedVerbatim()
    {
        var story = _library.SelectDefault();

        Assert.Equal(ExpectedReflection, story.ReflectionText);
        // Reflection-dialogue slice (2026-08-03): stories carry up to 3
        // questions, and every question must pair with a conclusion.
        //
        // REORDERED 2026-08-15 (owner decision). The originally-reviewed
        // question is still pinned verbatim, but it is now asked LAST rather
        // than first: it asks the child about himself, and every other story
        // in the library asks about the STORY first and the child last.
        // It began to matter the day the toy started asking ONE question per
        // listen beginning at index 0 — a child's very first question after
        // this story would have been about himself, before he had been asked
        // anything at all about what he just heard.
        Assert.Equal(ExpectedQuestion, story.ReflectionQuestions[^1]);
        Assert.Equal("Ինչո՞ւ էր ծաղիկը տխուր։", story.ReflectionQuestions[0]);
        Assert.InRange(story.ReflectionQuestions.Count, 1, 3);
        Assert.NotNull(story.ReflectionConclusions);
        Assert.Equal(story.ReflectionQuestions.Count, story.ReflectionConclusions!.Count);
    }

    [Fact]
    public void Story_Title_IsPinnedVerbatim()
    {
        Assert.Equal(ExpectedTitle, _library.SelectDefault().Title);
    }

    // ── Second story «Ոզնիկն ու խնձորը» — reviewed text, pinned
    //    byte-for-byte. Same contract as the first story: changing
    //    these constants requires a fresh armenian-story-master
    //    linguistic review first.

    private const string ExpectedHedgehogTitle = "Ոզնիկն ու խնձորը";
    private static readonly string[] ExpectedHedgehogSegments =
    [
        "Անտառում ապրում էր մի փոքրիկ ոզնիկ։ Մի առավոտ նա գտավ մի մեծ կարմիր խնձոր։ Խնձորը շատ համով էր երևում։ Բայց խնձորը ծանր էր, շատ ծանր։",
        "Ոզնիկը կանչեց իր ընկեր նապաստակին։ Նրանք միասին գլորեցին խնձորը։ Խնձորը դանդաղ գլորվեց մինչև ոզնիկի տնակը։",
        "Ոզնիկն ու նապաստակը միասին կերան խնձորը։ Խնձորն իսկապես շատ քաղցր էր։ Հետո նրանք նստեցին ծառի տակ։ Անտառում խաղաղ էր ու հանգիստ։",
    ];
    private const string ExpectedHedgehogReflection =
        "Ընկերոջ հետ նույնիսկ ծանր խնձորը հեշտ է գլորվում։";
    private const string ExpectedHedgehogQuestion =
        "Իսկ դու ի՞նչ ես սիրում անել ընկերոջդ հետ։";

    [Fact]
    public void HedgehogStory_TextIsPinnedVerbatim()
    {
        var story = _library.GetById(InMemoryCuratedStoryLibrary.HedgehogAppleId);

        Assert.NotNull(story);
        Assert.Equal(ExpectedHedgehogTitle, story!.Title);
        Assert.Equal(ExpectedHedgehogSegments.Length, story.Segments.Count);
        for (var i = 0; i < ExpectedHedgehogSegments.Length; i++)
        {
            Assert.Equal(ExpectedHedgehogSegments[i], story.Segments[i].Text);
        }
        Assert.Equal(ExpectedHedgehogReflection, story.ReflectionText);
        // Reordered on the same owner decision as little-cloud, 2026-08-15,
        // and for the same reason — see the note in
        // Story_ReflectionTextAndQuestion_ArePinnedVerbatim. The reviewed
        // question is still pinned verbatim; it is simply asked last now.
        Assert.Equal(ExpectedHedgehogQuestion, story.ReflectionQuestions[^1]);
        Assert.Equal("Ինչպե՞ս ոզնիկը տարավ ծանր խնձորը։", story.ReflectionQuestions[0]);
        Assert.InRange(story.ReflectionQuestions.Count, 1, 3);
    }

    [Fact]
    public void HedgehogStory_IsBedtimeSafeAndAgeRangedForTargetAudience()
    {
        var story = _library.GetById(InMemoryCuratedStoryLibrary.HedgehogAppleId)!;

        Assert.True(story.BedtimeSafe);
        Assert.Equal(4, story.MinAge);
        Assert.Equal(7, story.MaxAge);
    }

    [Fact]
    public void SelectDefault_IsUnchangedByAddingSecondStory()
    {
        // The deterministic default remains the first launch story —
        // adding library content must never silently change what a
        // future serving site would pick as default.
        Assert.Equal(InMemoryCuratedStoryLibrary.LittleCloudId, _library.SelectDefault().Id);
        Assert.Equal(InMemoryCuratedStoryLibrary.LittleCloudId, new InMemoryCuratedStoryLibrary().SelectDefault().Id);
    }

    [Fact]
    public void Story_ChildFacingText_HasNoLatinLettersAndNoDigits()
    {
        var story = _library.SelectDefault();
        var childFacing = new List<string> { story.Title, story.ReflectionText };
        childFacing.AddRange(story.Segments.Select(s => s.Text));
        childFacing.AddRange(story.ReflectionQuestions);

        foreach (var text in childFacing)
        {
            Assert.DoesNotContain(text, c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
            Assert.DoesNotContain(text, char.IsDigit);
        }
    }

    [Fact]
    public void Story_IsBedtimeSafeAndAgeRangedForTargetAudience()
    {
        var story = _library.SelectDefault();

        Assert.True(story.BedtimeSafe);
        Assert.Equal(4, story.MinAge);
        Assert.Equal(7, story.MaxAge);
    }

    private static bool IsArmenian(char c) => c is >= 'Ա' and <= '֏';
}
