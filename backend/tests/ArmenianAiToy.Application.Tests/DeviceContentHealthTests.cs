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

    // --- Sync diagnostics -------------------------------------------------
    // Added 2026-08-14 after firmware 1.2.1 crash-looped all night, panicking
    // on the manifest parse and rebooting every ~184 s. It heartbeat normally
    // for the first 180 s of every cycle, so LastSeenAt stayed fresh and every
    // surface showed it online and merely "stale". These tests exist so that
    // failure can never again be invisible.

    [Fact]
    public void ReportsAFailedSync_IsSyncFailed_NotSyncing()
    {
        // KEYSTONE. A failed sync leaves the card exactly as it was, and the
        // firmware's carry-forward then re-advertises the OLD entry as
        // verified — so counting alone reports "still working on it" for a
        // toy that has given up. The toy's own verdict is the only thing that
        // distinguishes the two, and it must win.
        var verdict = DeviceContentHealth.Resolve(
            "ulik:11",
            Advertised(("ulik", 12), ("anban-huri", 9)),
            Now.AddSeconds(-10), Now,
            DeviceContentHealth.DefaultOnlineThresholdSeconds,
            contentSyncStatus: DeviceContentHealth.SyncStatusFailed);

        Assert.Equal(DeviceContentHealth.SyncFailed, verdict);
    }

    [Fact]
    public void ReportsAFailedSync_WithNoStoryList_IsSyncFailed_NotUnknown()
    {
        // KEYSTONE, and the defect this slice was written to close. A toy
        // whose SD card is dead sends no story list at all, so it used to land
        // in "unknown" beside a perfectly healthy toy running old firmware —
        // and that toy will never tell a story. It can still report the
        // failure, and that has to outrank silence.
        var verdict = DeviceContentHealth.Resolve(
            null,
            Advertised(("ulik", 12)),
            Now.AddSeconds(-10), Now,
            DeviceContentHealth.DefaultOnlineThresholdSeconds,
            contentSyncStatus: DeviceContentHealth.SyncStatusFailed);

        Assert.Equal(DeviceContentHealth.SyncFailed, verdict);
    }

    [Fact]
    public void APartialSync_IsAlsoAFault()
    {
        // "partial" is the toy saying it tried and did not get everything.
        // Reported even when every advertised story happens to be present:
        // the card looking complete is precisely the lie the verdict exists
        // to break, and a toy whose sync half-fails will not receive the next
        // story either.
        var verdict = DeviceContentHealth.Resolve(
            "ulik:12",
            Advertised(("ulik", 12)),
            Now.AddSeconds(-10), Now,
            DeviceContentHealth.DefaultOnlineThresholdSeconds,
            contentSyncStatus: DeviceContentHealth.SyncStatusPartial);

        Assert.Equal(DeviceContentHealth.SyncFailed, verdict);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("never")]
    [InlineData("OK")]
    public void AHealthySyncStatus_DoesNotCreateAFault(string status)
    {
        var verdict = DeviceContentHealth.Resolve(
            "ulik:12",
            Advertised(("ulik", 12)),
            Now.AddSeconds(-10), Now,
            DeviceContentHealth.DefaultOnlineThresholdSeconds,
            contentSyncStatus: status);

        Assert.Equal(DeviceContentHealth.UpToDate, verdict);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("FAILED")]
    [InlineData(" partial ")]
    public void SyncStatusIsMatchedCaseInsensitivelyAndTrimmed(string status)
    {
        var verdict = DeviceContentHealth.Resolve(
            "ulik:12", Advertised(("ulik", 12)), Now.AddSeconds(-10), Now,
            DeviceContentHealth.DefaultOnlineThresholdSeconds,
            contentSyncStatus: status);

        Assert.Equal(DeviceContentHealth.SyncFailed, verdict);
    }

    [Theory]
    [InlineData("ok", true)]
    [InlineData("partial", true)]
    [InlineData("failed", true)]
    [InlineData("never", true)]
    [InlineData("OK", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("succeeded", false)]
    [InlineData("failure", false)]
    [InlineData("ok!", false)]
    public void TheSyncStatusVocabularyIsClosed(string? status, bool known)
    {
        // The value space is exactly ok/partial/failed/never. Anything else
        // is a firmware typo or a status from a build this backend predates.
        Assert.Equal(known, DeviceContentHealth.IsKnownSyncStatus(status));
    }

    [Fact]
    public void AnUnrecognisedSyncStatus_IsNoOpinion_NotAFault()
    {
        // KEYSTONE. This string crosses a wire from a device we do not
        // control. Treating an unknown word as a failure would paint a
        // healthy toy broken on a parent's phone the day a firmware typo
        // ships; ignoring it degrades to the verdict we can actually justify.
        var verdict = DeviceContentHealth.Resolve(
            "ulik:12", Advertised(("ulik", 12)), Now.AddSeconds(-10), Now,
            DeviceContentHealth.DefaultOnlineThresholdSeconds,
            contentSyncStatus: "borked");

        Assert.Equal(DeviceContentHealth.UpToDate, verdict);
    }

    [Fact]
    public void RepeatedFaultReboots_AreACrashLoop()
    {
        // KEYSTONE. The 2026-08-14 failure. The toy heartbeats normally
        // between crashes, so presence says nothing and the story counts say
        // "a bit behind" — the boot count is the only signal that says loop.
        var verdict = DeviceContentHealth.Resolve(
            "ulik:11",
            Advertised(("ulik", 12)),
            Now.AddSeconds(-10), Now,
            DeviceContentHealth.DefaultOnlineThresholdSeconds,
            contentSyncStatus: DeviceContentHealth.SyncStatusFailed,
            resetReason: "PANIC",
            bootCount: 400);

        // Outranks sync_failed: a toy that panics before finishing reports the
        // failed sync as a SYMPTOM, and naming the crash sends the operator to
        // the right place.
        Assert.Equal(DeviceContentHealth.CrashLooping, verdict);
    }

    [Theory]
    [InlineData("PANIC")]
    [InlineData("panic")]
    [InlineData("TASK_WDT")]
    [InlineData("INT_WDT")]
    [InlineData("RTC_WDT")]
    public void FaultResetReasons_CountTowardsACrashLoop(string reason)
    {
        Assert.True(DeviceContentHealth.IsCrashLooping(
            reason, DeviceContentHealth.CrashLoopBootThreshold));
    }

    [Theory]
    [InlineData("SW")]
    [InlineData("POWERON")]
    [InlineData("DEEPSLEEP")]
    [InlineData("")]
    [InlineData(null)]
    public void OrdinaryResetReasons_AreNeverACrashLoop(string? reason)
    {
        // KEYSTONE. A high boot count on its own is not a fault: an OTA
        // reboots by software, and a family that switches the toy off every
        // night runs the count up with nothing wrong. Calling that a crash
        // loop would tell a parent to contact support about a healthy toy.
        Assert.False(DeviceContentHealth.IsCrashLooping(reason, 400));
    }

    [Fact]
    public void ASingleFaultReboot_IsNotYetALoop()
    {
        // One panic is an incident. The threshold is what separates the two,
        // and the real failure crossed it inside ten minutes.
        Assert.False(DeviceContentHealth.IsCrashLooping(
            "PANIC", DeviceContentHealth.CrashLoopBootThreshold - 1));
        Assert.True(DeviceContentHealth.IsCrashLooping(
            "PANIC", DeviceContentHealth.CrashLoopBootThreshold));
    }

    [Fact]
    public void ANeverReportedBootCount_IsNotALoop()
    {
        Assert.False(DeviceContentHealth.IsCrashLooping("PANIC", null));
    }

    [Fact]
    public void Offline_StillWinsOverEveryFaultVerdict()
    {
        // KEYSTONE. The decision order is unchanged at the top: a toy that is
        // switched off cannot be crash-looping NOW, and reporting a days-old
        // crash as a live one sends a parent chasing a toy that is simply in
        // a cupboard.
        var verdict = DeviceContentHealth.Resolve(
            "ulik:11", Advertised(("ulik", 12)), Now.AddDays(-3), Now,
            DeviceContentHealth.DefaultOnlineThresholdSeconds,
            contentSyncStatus: DeviceContentHealth.SyncStatusFailed,
            resetReason: "PANIC", bootCount: 400);

        Assert.Equal(DeviceContentHealth.Offline, verdict);
    }

    [Fact]
    public void ADeviceThatReportsNoDiagnostics_BehavesExactlyAsBefore()
    {
        // KEYSTONE. Every toy in the field is in this state until the
        // firmware release lands. The new parameters are optional and their
        // absence must not move a single verdict.
        Assert.Equal(DeviceContentHealth.UpToDate, DeviceContentHealth.Resolve(
            "ulik:12", Advertised(("ulik", 12)), Now.AddSeconds(-10), Now));
        Assert.Equal(DeviceContentHealth.Unknown, DeviceContentHealth.Resolve(
            null, Advertised(("ulik", 12)), Now.AddSeconds(-10), Now));
        Assert.Equal(DeviceContentHealth.Stale, DeviceContentHealth.Resolve(
            "", Advertised(("ulik", 12)), Now.AddSeconds(-10), Now));
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
