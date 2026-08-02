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

    /// <summary>
    /// Owner-designated CARRIED CLASSICS. Their text is a published work
    /// reproduced faithfully, so two of the authoring rules below do not
    /// apply to them: CLAUDE.md states classics "keep the source's natural
    /// segment count" and are "never force-resegmented to 3", which also
    /// means their scene beats are longer than an in-project original's and
    /// a beat may trail off on «․․․» instead of closing on «։».
    ///
    /// <para>
    /// This is an EXPLICIT list, not a heuristic, precisely so it cannot
    /// grow silently: a newly authored story is held to the full rules
    /// unless someone deliberately adds its id here, and that edit is
    /// visible in review. Every other rule — no Latin, no digits, no
    /// emoji, no markdown, the TTS-safe whitelist, no structural tokens,
    /// no malformed words, bedtime-safety — still applies to classics too.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> CarriedClassics = new()
    {
        "anban-huri", "khosogh-dzuk", "pochat-aghves", "sutasan", "sutlik-orskan", "ulik",
    };

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
        // Anything else (ellipsis, ASCII punctuation, symbols) is a TTS
        // risk and fails authoring.
        //
        // WIDENED when the classic library was promoted (2026-08-03). The
        // original list was written for in-project originals; real Armenian
        // literary prose legitimately uses four more characters, and every
        // one of them was audited before being admitted:
        //   U+2024 ․  the MIJAKET — standard Armenian punctuation, 174 uses
        //             across seven stories. Not optional in classic text.
        //   U+002D -  hyphen inside compound words: «պարապ-սարապ»,
        //             «կամաց-կամաց», «Նիֆ-Նիֆ». Not a dash, not a separator.
        //   U+2014 —  the em dash that opens a line of speech. The ONE
        //             permitted dialogue dash: en dash (U+2013) and
        //             horizontal bar (U+2015) were strays and were
        //             normalised to this at promotion, so they stay
        //             rejected and cannot creep back.
        //   U+000A \n verse line breaks (the songs inside anban-huri).
        //   U+2032 ′  ONE occurrence, «մանեցե′ք» in anban-huri. This looks
        //             like a typo for «՛» and I "corrected" it — then
        //             LibraryStoryQuestionTests.StoryText_RemainsUnchanged
        //             failed, because that character is a deliberately
        //             PRESERVED SOURCE QUIRK ("the import must never be
        //             'fixed'"). The edit was reverted. Admitted here as a
        //             documented exception rather than silently normalising
        //             a text somebody decided to keep verbatim. TTS impact
        //             is checked at listen test, not guessed at here.
        // The en dash (U+2013) and horizontal bar (U+2015) were NOT admitted:
        // they were inconsistent strays among em dashes and were normalised
        // in the source, so they stay rejected and cannot creep back.
        AssertNoneMatch(
            c => !(c is >= '԰' and <= '֏'
                     or ' ' or ',' or '«' or '»'
                     or '․' or '-' or '—' or '\n' or '′'),
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
        // In-project originals only — a carried classic may legitimately let
        // a beat trail off on «․․․» because that is what the published text
        // does. See CarriedClassics.
        foreach (var story in AllStories.Where(s => !CarriedClassics.Contains(s.Id)))
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
        // In-project originals only: classics keep the source's own scene
        // beats, which run longer. See CarriedClassics.
        foreach (var story in AllStories.Where(s => !CarriedClassics.Contains(s.Id)))
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
    public void ListAvailable_IncludesTheOriginalLaunchStories()
    {
        // The two originals must never silently disappear from the library.
        // The COUNT is deliberately not pinned here: the library grows as
        // stories are promoted (12 more on 2026-08-03), and a hard count
        // would turn every future promotion into a failing test that says
        // nothing useful. EmbeddedCuratedStoryLibraryTests owns the
        // "exactly what is embedded" inventory.
        var ids = AllStories.Select(s => s.Id).ToList();

        Assert.Contains(InMemoryCuratedStoryLibrary.LittleCloudId, ids);
        Assert.Contains(InMemoryCuratedStoryLibrary.HedgehogAppleId, ids);
    }
}
