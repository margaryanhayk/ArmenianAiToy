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
    public void ListAvailable_ReturnsExactlyOneStory()
    {
        var stories = _library.ListAvailable();

        Assert.Single(stories);
        Assert.Equal(InMemoryCuratedStoryLibrary.LittleCloudId, stories[0].Id);
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
        var question = Assert.Single(story.ReflectionQuestions);
        Assert.Equal(ExpectedQuestion, question);
    }

    [Fact]
    public void Story_Title_IsPinnedVerbatim()
    {
        Assert.Equal(ExpectedTitle, _library.SelectDefault().Title);
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
