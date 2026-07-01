using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.DTOs;
using ArmenianAiToy.Application.Stories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Controller-level tests for StoryController.GetStoryOfTheDay — the
/// device-facing "story of the day" endpoint. The rotation logic itself
/// is pinned by StoryOfTheDaySelectorTests; here we pin the wire shape,
/// the empty-library 404, and the time-zone fail-soft.
/// </summary>
public class StoryControllerTests
{
    private static CuratedStory Story(string id, bool bedtimeSafe) =>
        new(id, id, 4, 7, "warm",
            [new CuratedStorySegment(0, "Տեքստ։")],
            "Միտք։", ["Հարց՞"], bedtimeSafe);

    private static StoryController CreateController(params CuratedStory[] stories)
    {
        var library = Substitute.For<ICuratedStoryLibrary>();
        library.ListAvailable().Returns(stories.ToList());
        var logger = Substitute.For<ILogger<StoryController>>();
        return new StoryController(library, logger);
    }

    [Fact]
    public void GetStoryOfTheDay_ReturnsOkWithStoryMetadata()
    {
        var controller = CreateController(Story("little-cloud", true), Story("hedgehog-apple", true));
        var asOf = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero); // morning-ish

        var result = controller.GetStoryOfTheDay(tz: "UTC", asOf: asOf);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<StoryOfTheDayDto>(ok.Value);
        Assert.Contains(dto.StoryId, new[] { "little-cloud", "hedgehog-apple" });
        Assert.False(string.IsNullOrWhiteSpace(dto.Title));
        Assert.True(dto.SegmentCount >= 1);
        Assert.Equal("UTC", dto.TimeZoneId);
        Assert.True(dto.TimeZoneResolved);
    }

    [Fact]
    public void GetStoryOfTheDay_EmptyLibrary_Returns404()
    {
        var controller = CreateController(); // no stories

        var result = controller.GetStoryOfTheDay(tz: "UTC", asOf: DateTimeOffset.UnixEpoch);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void GetStoryOfTheDay_UnresolvableTimeZone_FailsSoftToUtc()
    {
        var controller = CreateController(Story("little-cloud", true));

        var result = controller.GetStoryOfTheDay(tz: "Mars/Phobos", asOf: DateTimeOffset.UnixEpoch);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<StoryOfTheDayDto>(ok.Value);
        Assert.False(dto.TimeZoneResolved);
        Assert.Equal("Mars/Phobos", dto.TimeZoneId); // echoes the attempted id
    }

    [Fact]
    public void GetStoryOfTheDay_Evening_PrefersBedtimeSafeStory()
    {
        var controller = CreateController(
            Story("lively", false), Story("calm", true));
        // 20:00 UTC → evening bucket in UTC.
        var evening = new DateTimeOffset(2026, 7, 1, 20, 0, 0, TimeSpan.Zero);

        var result = controller.GetStoryOfTheDay(tz: "UTC", asOf: evening);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<StoryOfTheDayDto>(ok.Value);
        Assert.Equal("calm", dto.StoryId);
        Assert.Equal("Evening", dto.PartOfDay);
        Assert.True(dto.BedtimeSafe);
    }
}
