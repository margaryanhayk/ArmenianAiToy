using ArmenianAiToy.Application.Stories;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the deterministic StoryOfTheDaySelector. Pins:
///  - part-of-day boundaries (morning / daytime / evening incl. wrap),
///  - determinism (same date+part → same story),
///  - daily rotation,
///  - the evening = calm-pool rule (reuses BedtimeSafe, with fallback),
///  - empty library → null.
/// </summary>
public class StoryOfTheDaySelectorTests
{
    private static CuratedStory Story(string id, bool bedtimeSafe) =>
        new(
            Id: id,
            Title: id,
            MinAge: 4,
            MaxAge: 7,
            Tone: "warm",
            Segments: [new CuratedStorySegment(0, "Տեքստ։")],
            ReflectionText: "Միտք։",
            ReflectionQuestions: ["Հարց՞"],
            BedtimeSafe: bedtimeSafe);

    // ---- ResolvePartOfDay ----

    [Theory]
    [InlineData(5, PartOfDay.Morning)]
    [InlineData(10, PartOfDay.Morning)]
    [InlineData(11, PartOfDay.Daytime)]
    [InlineData(17, PartOfDay.Daytime)]
    [InlineData(18, PartOfDay.Evening)]
    [InlineData(23, PartOfDay.Evening)]
    [InlineData(0, PartOfDay.Evening)]
    [InlineData(4, PartOfDay.Evening)]
    public void ResolvePartOfDay_MapsHourToBucket(int hour, PartOfDay expected)
    {
        var part = StoryOfTheDaySelector.ResolvePartOfDay(new TimeOnly(hour, 0));
        Assert.Equal(expected, part);
    }

    // ---- Select ----

    [Fact]
    public void Select_EmptyLibrary_ReturnsNull()
    {
        var result = StoryOfTheDaySelector.Select(
            new List<CuratedStory>(), new DateOnly(2026, 7, 1), PartOfDay.Morning);
        Assert.Null(result);
    }

    [Fact]
    public void Select_IsDeterministic_SameDateAndPart_SameStory()
    {
        var lib = new List<CuratedStory>
        {
            Story("a", true), Story("b", true), Story("c", false),
        };
        var date = new DateOnly(2026, 7, 1);

        var first = StoryOfTheDaySelector.Select(lib, date, PartOfDay.Daytime);
        var again = StoryOfTheDaySelector.Select(lib, date, PartOfDay.Daytime);

        Assert.NotNull(first);
        Assert.Equal(first!.Id, again!.Id);
    }

    [Fact]
    public void Select_RotatesAcrossConsecutiveDays()
    {
        var lib = new List<CuratedStory>
        {
            Story("a", true), Story("b", true), Story("c", true),
        };

        var d1 = StoryOfTheDaySelector.Select(lib, new DateOnly(2026, 7, 1), PartOfDay.Morning);
        var d2 = StoryOfTheDaySelector.Select(lib, new DateOnly(2026, 7, 2), PartOfDay.Morning);

        Assert.NotEqual(d1!.Id, d2!.Id);
    }

    [Fact]
    public void Select_Evening_PicksOnlyBedtimeSafeStories()
    {
        var lib = new List<CuratedStory>
        {
            Story("lively-1", false),
            Story("lively-2", false),
            Story("calm-1", true),
        };

        // Every date's evening pick must be the only bedtime-safe story.
        for (var day = 1; day <= 14; day++)
        {
            var pick = StoryOfTheDaySelector.Select(
                lib, new DateOnly(2026, 7, day), PartOfDay.Evening);
            Assert.Equal("calm-1", pick!.Id);
            Assert.True(pick.BedtimeSafe);
        }
    }

    [Fact]
    public void Select_Evening_NoBedtimeSafe_FallsBackToWholeLibrary()
    {
        var lib = new List<CuratedStory>
        {
            Story("lively-1", false),
            Story("lively-2", false),
        };

        var pick = StoryOfTheDaySelector.Select(
            lib, new DateOnly(2026, 7, 1), PartOfDay.Evening);

        Assert.NotNull(pick); // never null just because nothing is calm
    }

    [Fact]
    public void Select_MorningAndDaytime_CanDifferOnSameDate()
    {
        var lib = new List<CuratedStory>
        {
            Story("a", true), Story("b", true), Story("c", true), Story("d", true),
        };
        var date = new DateOnly(2026, 7, 1);

        var morning = StoryOfTheDaySelector.Select(lib, date, PartOfDay.Morning);
        var daytime = StoryOfTheDaySelector.Select(lib, date, PartOfDay.Daytime);

        // Per-bucket offset means the two windows are not forced to the
        // same story (they differ whenever the library has >1 story).
        Assert.NotEqual(morning!.Id, daytime!.Id);
    }
}
