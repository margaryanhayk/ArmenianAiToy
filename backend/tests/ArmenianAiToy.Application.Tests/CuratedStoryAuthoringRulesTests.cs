using System.Text;
using ArmenianAiToy.Application.Stories;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Authoring linter over EVERY curated story in the library — the
/// executable form of the MODES.md §1A acceptance checklist. Unlike the
/// byte-pin tests in CuratedStoryLibraryTests (which freeze specific
/// reviewed text), these rules apply automatically to every story added
/// in the future: a new story that violates the checklist fails the
/// build before it can ever reach a child. Runtime-dead like the
/// library itself — nothing here touches a live flow.
/// </summary>
public class CuratedStoryAuthoringRulesTests
{
    private static readonly IReadOnlyList<CuratedStory> AllStories =
        new InMemoryCuratedStoryLibrary().ListAvailable();

    /// <summary>Every child-facing string with a label for failure
    /// messages: title, each segment, reflection text, each reflection
    /// question.</summary>
    private static IEnumerable<(string Where, string Text)> AllChildFacingStrings()
    {
        foreach (var story in AllStories)
        {
            yield return ($"{story.Id}/title", story.Title);
            foreach (var segment in story.Segments)
            {
                yield return ($"{story.Id}/segment[{segment.Index}]", segment.Text);
            }
            yield return ($"{story.Id}/reflection", story.ReflectionText);
            for (var i = 0; i < story.ReflectionQuestions.Count; i++)
            {
                yield return ($"{story.Id}/question[{i}]", story.ReflectionQuestions[i]);
            }
        }
    }

    private static void AssertNoneMatch(Func<char, bool> bad, string ruleName)
    {
        var violations = new StringBuilder();
        foreach (var (where, text) in AllChildFacingStrings())
        {
            foreach (var c in text.Where(bad).Distinct())
            {
                violations.AppendLine($"{where}: '{c}' (U+{(int)c:X4}) violates {ruleName}");
            }
        }
        Assert.True(violations.Length == 0, violations.ToString());
    }

    [Fact]
    public void ChildFacingText_HasNoLatinLetters() =>
        AssertNoneMatch(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z', "no-Latin");

    [Fact]
    public void ChildFacingText_HasNoCyrillicLetters() =>
        AssertNoneMatch(c => c is >= 'Ѐ' and <= 'ӿ' or >= 'Ԁ' and <= 'ԯ', "no-Cyrillic");

    [Fact]
    public void ChildFacingText_HasNoDigits() =>
        AssertNoneMatch(char.IsDigit, "no-digits");

    [Fact]
    public void ChildFacingText_HasNoEmojiOrNonBmpCodepoints() =>
        // All emoji and pictographs live outside the BMP or in surrogate
        // pairs; Armenian text is pure BMP, so any surrogate is a violation.
        AssertNoneMatch(char.IsSurrogate, "no-emoji");

    [Fact]
    public void ChildFacingText_HasNoMarkdownCharacters() =>
        AssertNoneMatch(c => c is '*' or '_' or '#' or '`' or '[' or ']' or '~' or '>' or '|', "no-markdown");

    [Fact]
    public void ChildFacingText_UsesOnlyTtsSafeCharacters()
    {
        // Whitelist form of the §1A punctuation rule: Armenian block
        // (letters + «։», «՛», «՜», «՝», «՞»), space, comma, guillemets.
        // Anything else (hyphens, ellipsis, ASCII punctuation, symbols)
        // is a TTS risk and fails authoring.
        AssertNoneMatch(
            c => !(c is >= '԰' and <= '֏' or ' ' or ',' or '«' or '»'),
            "tts-safe-whitelist");
    }

    [Fact]
    public void ChildFacingText_HasNoStructuralTokens()
    {
        string[] forbidden = ["CHOICE_A", "CHOICE_B", "STORY_MEMORY", "---"];
        foreach (var (where, text) in AllChildFacingStrings())
        {
            foreach (var token in forbidden)
            {
                Assert.False(text.Contains(token, StringComparison.OrdinalIgnoreCase),
                    $"{where}: contains structural token '{token}'");
            }
        }
    }

    [Fact]
    public void ChildFacingText_HasNoKnownMalformedWords()
    {
        // Malformations previously caught in live GPT output. Curated
        // text containing one means an unreviewed edit slipped in.
        string[] badWords = ["խաղաքում", "խաղացում", "շրթունջ", "մտացեց"];
        foreach (var (where, text) in AllChildFacingStrings())
        {
            foreach (var bad in badWords)
            {
                Assert.False(text.Contains(bad, StringComparison.Ordinal),
                    $"{where}: contains known malformed word '{bad}'");
            }
        }
    }

    [Fact]
    public void EveryStory_SegmentsAreNonEmptyAndSequentiallyIndexedFromZero()
    {
        foreach (var story in AllStories)
        {
            Assert.True(story.Segments.Count >= 2, $"{story.Id}: fewer than 2 segments");
            for (var i = 0; i < story.Segments.Count; i++)
            {
                Assert.Equal(i, story.Segments[i].Index);
                Assert.False(string.IsNullOrWhiteSpace(story.Segments[i].Text),
                    $"{story.Id}/segment[{i}]: empty text");
            }
        }
    }

    [Fact]
    public void EveryStory_HasTitleToneReflectionAndAtLeastOneQuestion()
    {
        foreach (var story in AllStories)
        {
            Assert.False(string.IsNullOrWhiteSpace(story.Title), $"{story.Id}: empty title");
            Assert.False(string.IsNullOrWhiteSpace(story.Tone), $"{story.Id}: empty tone");
            Assert.False(string.IsNullOrWhiteSpace(story.ReflectionText), $"{story.Id}: empty reflection");
            Assert.True(story.ReflectionQuestions.Count >= 1, $"{story.Id}: no reflection question");
            Assert.All(story.ReflectionQuestions,
                q => Assert.False(string.IsNullOrWhiteSpace(q), $"{story.Id}: blank reflection question"));
        }
    }

    [Fact]
    public void EverySegment_EndsWithArmenianFullStop()
    {
        foreach (var story in AllStories)
        {
            foreach (var segment in story.Segments)
            {
                Assert.True(segment.Text.TrimEnd().EndsWith('։'),
                    $"{story.Id}/segment[{segment.Index}]: does not end with «։»");
            }
        }
    }

    [Fact]
    public void EverySegment_IsShortEnoughForOneTtsRender()
    {
        // MODES.md §1A: segment = one scene beat, 2-4 sentences, ~≤300 chars.
        foreach (var story in AllStories)
        {
            foreach (var segment in story.Segments)
            {
                Assert.True(segment.Text.Length <= 300,
                    $"{story.Id}/segment[{segment.Index}]: {segment.Text.Length} chars exceeds 300");
            }
        }
    }

    [Fact]
    public void BedtimeSafeStories_HaveNoQuestionsOrExclamationsInSegmentsOrReflection()
    {
        // §1A: BedtimeSafe requires zero questions/exclamations in
        // segments AND the reflection sentence (the reflection QUESTION
        // is exempt — it is structurally suppressed at bedtime by the
        // future serving site). The vocative stress mark «՛» (U+055B,
        // e.g. «ամպի՛կ ջան») is neither a question nor an exclamation
        // and stays allowed — story 1 passed BedtimeSafe review with it.
        char[] forbidden = ['՞', '՜', '?', '!'];
        foreach (var story in AllStories.Where(s => s.BedtimeSafe))
        {
            var bedtimeServed = story.Segments.Select(s => s.Text).Append(story.ReflectionText);
            foreach (var text in bedtimeServed)
            {
                foreach (var c in forbidden)
                {
                    Assert.False(text.Contains(c),
                        $"{story.Id}: BedtimeSafe text contains '{c}' (U+{(int)c:X4})");
                }
            }
        }
    }

    [Fact]
    public void StoryIds_AreUniqueAndAsciiKebabCase()
    {
        Assert.Equal(AllStories.Count, AllStories.Select(s => s.Id).Distinct().Count());
        foreach (var story in AllStories)
        {
            Assert.Matches("^[a-z]+(-[a-z]+)*$", story.Id);
        }
    }

    [Fact]
    public void StoryTitles_AreUnique()
    {
        Assert.Equal(AllStories.Count, AllStories.Select(s => s.Title).Distinct().Count());
    }

    [Fact]
    public void SelectDefault_IsDeterministicAcrossInstancesAndCalls()
    {
        var library = new InMemoryCuratedStoryLibrary();
        var first = library.SelectDefault().Id;

        Assert.Equal(first, library.SelectDefault().Id);
        Assert.Equal(first, new InMemoryCuratedStoryLibrary().SelectDefault().Id);
    }

    [Fact]
    public void ListAvailable_IncludesBothLaunchStories()
    {
        var ids = AllStories.Select(s => s.Id).ToList();

        Assert.Equal(2, ids.Count);
        Assert.Contains(InMemoryCuratedStoryLibrary.LittleCloudId, ids);
        Assert.Contains(InMemoryCuratedStoryLibrary.HedgehogAppleId, ids);
    }
}
