using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins the "does this toy have the current stories?" verdict.
///
/// Motivating gap (2026-08-13): the owner asked what content version his toy
/// was on and there was no answer. The backend knew what it ADVERTISED and
/// nothing about what any toy had downloaded, so the only confirmation that a
/// library update had landed was listening to a story. These tests exist so
/// the answer stays truthful — including the two ways it could become a lie:
/// calling a toy stale when it simply has not reported, and calling a toy
/// up to date when it holds last week's version of a story.
/// </summary>
public class DeviceContentHealthTests
{
    private static readonly DateTime Now = new(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyCollection<(string, int)> Advertised(params (string, int)[] items)
        => items;

    [Fact]
    public void HoldsEveryAdvertisedStory_IsUpToDate()
    {
        var verdict = DeviceContentHealth.Resolve(
            "ulik:12,anban-huri:9",
            Advertised(("ulik", 12), ("anban-huri", 9)),
            Now.AddSeconds(-10), Now);

        Assert.Equal(DeviceContentHealth.UpToDate, verdict);
    }

    [Fact]
    public void HoldsAnOlderVersion_IsNotUpToDate()
    {
        // KEYSTONE. Bumping a Version is exactly how a re-render reaches a
        // toy; a toy still playing v11 of «Ուլիկը» while the library serves
        // v12 is playing last week's narration. Counting that as "has the
        // story" would make the whole feature a comfortable lie.
        var verdict = DeviceContentHealth.Resolve(
            "ulik:11,anban-huri:9",
            Advertised(("ulik", 12), ("anban-huri", 9)),
            Now.AddSeconds(-10), Now);

        Assert.Equal(DeviceContentHealth.Syncing, verdict);

        var counts = DeviceContentHealth.Count(
            "ulik:11,anban-huri:9", Advertised(("ulik", 12), ("anban-huri", 9)));
        Assert.Equal(1, counts.Present);
        Assert.Equal(2, counts.Advertised);
        Assert.Equal(new[] { "ulik" },
            DeviceContentHealth.MissingStoryIds(
                "ulik:11,anban-huri:9", Advertised(("ulik", 12), ("anban-huri", 9))));
    }

    [Fact]
    public void PartWayThroughASync_IsSyncing()
    {
        var verdict = DeviceContentHealth.Resolve(
            "ulik:12",
            Advertised(("ulik", 12), ("anban-huri", 9), ("sutasan", 6)),
            Now.AddSeconds(-10), Now);

        Assert.Equal(DeviceContentHealth.Syncing, verdict);
    }

    [Fact]
    public void ReportedAndHasNothing_IsStale()
    {
        var verdict = DeviceContentHealth.Resolve(
            "", Advertised(("ulik", 12)), Now.AddSeconds(-10), Now);

        Assert.Equal(DeviceContentHealth.Stale, verdict);
    }

    [Fact]
    public void NeverReported_IsUnknown_NotAFault()
    {
        // KEYSTONE. A toy on firmware older than the content-report slice
        // sends nothing. Reading silence as "has no stories" would accuse a
        // perfectly healthy toy — and every toy in the field is in exactly
        // this state until the firmware release lands.
        var verdict = DeviceContentHealth.Resolve(
            null, Advertised(("ulik", 12)), Now.AddSeconds(-10), Now);

        Assert.Equal(DeviceContentHealth.Unknown, verdict);
        Assert.Equal(0, DeviceContentHealth.Count(null, Advertised(("ulik", 12))).Present);
        Assert.Empty(DeviceContentHealth.MissingStoryIds(null, Advertised(("ulik", 12))));
    }

    [Fact]
    public void Offline_WinsOverAStaleReport()
    {
        // Same rule as DeviceStoryHealth: a toy unplugged three days ago is
        // not an out-of-date toy, it is an off toy, and telling a parent
        // otherwise sends them chasing the wrong thing.
        var verdict = DeviceContentHealth.Resolve(
            "", Advertised(("ulik", 12)), Now.AddDays(-3), Now);

        Assert.Equal(DeviceContentHealth.Offline, verdict);
    }

    [Fact]
    public void NothingAdvertised_IsUpToDate()
    {
        // Content sync disabled, or an empty manifest: a toy cannot be behind
        // something that was never offered.
        var verdict = DeviceContentHealth.Resolve(
            "", Advertised(), Now.AddSeconds(-10), Now);

        Assert.Equal(DeviceContentHealth.UpToDate, verdict);
    }

    [Fact]
    public void ExtraStoriesOnTheCard_DoNotMakeItStale()
    {
        // A story retired from the manifest stays cached on the card by
        // design (content sync never deletes). It must not read as a fault.
        var verdict = DeviceContentHealth.Resolve(
            "ulik:12,retired-tale:3",
            Advertised(("ulik", 12)),
            Now.AddSeconds(-10), Now);

        Assert.Equal(DeviceContentHealth.UpToDate, verdict);
    }

    [Theory]
    [InlineData("ulik:12,,anban-huri:9")]          // empty entry
    [InlineData("ulik:12, anban-huri:9 ")]         // stray whitespace
    [InlineData("ulik:12,garbage,anban-huri:9")]   // no colon
    [InlineData("ulik:12,broken:,anban-huri:9")]   // no version
    [InlineData("ulik:12,:9,anban-huri:9")]        // no id
    [InlineData("ulik:12,x:notanumber,anban-huri:9")]
    public void AMalformedEntryCostsOnlyThatEntry(string reported)
    {
        // This string crosses a wire from a device we do not control. One bad
        // entry must never discard the rest of the report — the failure that
        // would produce is "your toy has no stories", shown to a parent whose
        // toy is fine.
        var counts = DeviceContentHealth.Count(
            reported, Advertised(("ulik", 12), ("anban-huri", 9)));

        Assert.Equal(2, counts.Present);
    }

    [Fact]
    public void StoryIdsAreMatchedCaseInsensitively()
    {
        // Mirrors the content-sync contract, where one backend story can
        // never become two files on the card.
        var counts = DeviceContentHealth.Count("ULIK:12", Advertised(("ulik", 12)));

        Assert.Equal(1, counts.Present);
    }

    [Fact]
    public void ADuplicateIdKeepsTheHighestVersion()
    {
        var counts = DeviceContentHealth.Count(
            "ulik:3,ulik:12", Advertised(("ulik", 12)));

        Assert.Equal(1, counts.Present);
    }

    [Fact]
    public void MissingStoryIds_PreserveAdvertisedOrder()
    {
        // The console lists them for an operator to read; manifest order is
        // the order they appear everywhere else.
        var missing = DeviceContentHealth.MissingStoryIds(
            "sutasan:6",
            Advertised(("ulik", 12), ("sutasan", 6), ("anban-huri", 9)));

        Assert.Equal(new[] { "ulik", "anban-huri" }, missing);
    }
}
