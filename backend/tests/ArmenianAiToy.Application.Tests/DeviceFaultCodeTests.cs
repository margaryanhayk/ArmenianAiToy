using ArmenianAiToy.Application.Helpers;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Pins the two new parent-facing fault codes allocated 2026-09-11 for the
/// content-sync/firmware-health verdicts (<see cref="DeviceContentHealth.SyncFailed"/>,
/// <see cref="DeviceContentHealth.CrashLooping"/>) and the combining rule in
/// <see cref="DeviceFaultCode.FromHealth"/>. <see cref="DeviceFaultCode.FromStoryHealth"/>
/// itself stays pinned by <see cref="DeviceStoryHealthTests"/> — this file
/// only covers what changed.
/// </summary>
public class DeviceFaultCodeTests
{
    // ────────────────────────────────────────────────────────────
    // FromContentHealth — only the two genuine faults get a code.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void FromContentHealth_SyncFailed_GetsACode()
    {
        Assert.Equal(DeviceFaultCode.LibrarySyncFailed,
            DeviceFaultCode.FromContentHealth(DeviceContentHealth.SyncFailed));
    }

    [Fact]
    public void FromContentHealth_CrashLooping_GetsACode()
    {
        Assert.Equal(DeviceFaultCode.CrashLooping,
            DeviceFaultCode.FromContentHealth(DeviceContentHealth.CrashLooping));
    }

    [Theory]
    [InlineData(DeviceContentHealth.UpToDate)]
    [InlineData(DeviceContentHealth.Syncing)]
    [InlineData(DeviceContentHealth.Stale)]
    [InlineData(DeviceContentHealth.Offline)]
    [InlineData(DeviceContentHealth.Unknown)]
    public void FromContentHealth_EverythingElse_IsNone(string contentHealth)
    {
        // "syncing" is expected mid-download, "stale"/"offline" are already
        // conveyed elsewhere, "unknown" is old firmware that never reports —
        // none of these are a fault to hand a parent a code for.
        Assert.Equal(DeviceFaultCode.None, DeviceFaultCode.FromContentHealth(contentHealth));
    }

    // ────────────────────────────────────────────────────────────
    // FromHealth — the combine/ranking rule a parent's single FaultCode
    // actually goes through.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void FromHealth_NoFaultEitherSide_IsNone()
    {
        Assert.Equal(DeviceFaultCode.None,
            DeviceFaultCode.FromHealth(DeviceStoryHealth.Ok, DeviceContentHealth.UpToDate));
    }

    [Fact]
    public void FromHealth_StorageFaultOnly_UsesStorageCode()
    {
        Assert.Equal(DeviceFaultCode.StorageUnavailable,
            DeviceFaultCode.FromHealth(DeviceStoryHealth.NoStorage, DeviceContentHealth.UpToDate));
    }

    [Fact]
    public void FromHealth_SyncFailedOnly_UsesLibrarySyncFailedCode()
    {
        Assert.Equal(DeviceFaultCode.LibrarySyncFailed,
            DeviceFaultCode.FromHealth(DeviceStoryHealth.Ok, DeviceContentHealth.SyncFailed));
    }

    [Fact]
    public void FromHealth_CrashLoopingOnly_UsesCrashLoopingCode()
    {
        Assert.Equal(DeviceFaultCode.CrashLooping,
            DeviceFaultCode.FromHealth(DeviceStoryHealth.Ok, DeviceContentHealth.CrashLooping));
    }

    [Fact]
    public void FromHealth_CrashLooping_OutranksAStorageFaultToo()
    {
        // KEYSTONE: a toy that is crash-looping AND separately reports a dead
        // card must show ONE code — the deeper fault, crash-looping — not the
        // storage code, since a panicking toy is not "running with a bad card."
        Assert.Equal(DeviceFaultCode.CrashLooping,
            DeviceFaultCode.FromHealth(DeviceStoryHealth.NoStorage, DeviceContentHealth.CrashLooping));
    }

    [Fact]
    public void FromHealth_StorageFault_OutranksSyncFailed()
    {
        // A dead card is the more actionable, physical fault; a failed sync
        // can also just be a symptom of the card being unreadable.
        Assert.Equal(DeviceFaultCode.StorageUnavailable,
            DeviceFaultCode.FromHealth(DeviceStoryHealth.NoStorage, DeviceContentHealth.SyncFailed));
    }

    // ────────────────────────────────────────────────────────────
    // Stability — codes go out to parents and get read back to support
    // months later; an accidental renumbering must fail loudly.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void NewFaultCodes_ValuesAreStable()
    {
        Assert.Equal("E-401", DeviceFaultCode.LibrarySyncFailed);
        Assert.Equal("E-501", DeviceFaultCode.CrashLooping);
    }
}
