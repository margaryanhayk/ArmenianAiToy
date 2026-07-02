using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Services;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the firmware-manifest decision + signing. Pins: no-update when
/// disabled / current / newer / wrong-board; update when older; signature
/// present only with a key.
/// </summary>
public class FirmwareManifestServiceTests
{
    private static readonly DateTime Now = new(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);

    private static FirmwareManifestService WithRelease(
        bool enabled = true, string latest = "1.0.1", string board = "", string signingKey = "")
        => new(new FirmwareUpdateOptions
        {
            Enabled = enabled,
            LatestVersion = latest,
            MinVersion = "1.0.0",
            BoardModel = board,
            Url = "https://backend.example/fw/1.0.1.bin",
            SizeBytes = 1_524_995,
            Sha256 = "abc123",
            SigningKey = signingKey,
            TtlSeconds = 300,
        });

    [Fact]
    public void Disabled_ReturnsNoUpdate()
    {
        var svc = WithRelease(enabled: false);
        Assert.False(svc.Build("1.0.0", null, Now).UpdateAvailable);
    }

    [Fact]
    public void DeviceCurrent_ReturnsNoUpdate()
    {
        var svc = WithRelease(latest: "1.0.1");
        Assert.False(svc.Build("1.0.1", null, Now).UpdateAvailable);
    }

    [Fact]
    public void DeviceNewer_ReturnsNoUpdate()
    {
        var svc = WithRelease(latest: "1.0.1");
        Assert.False(svc.Build("1.2.0", null, Now).UpdateAvailable);
    }

    [Fact]
    public void DeviceOlder_ReturnsUpdate_WithFields()
    {
        var svc = WithRelease(latest: "1.0.1");

        var m = svc.Build("1.0.0", null, Now);

        Assert.True(m.UpdateAvailable);
        Assert.Equal("1.0.1", m.Version);
        Assert.Equal("https://backend.example/fw/1.0.1.bin", m.Url);
        Assert.Equal("abc123", m.Sha256);
        Assert.Equal(1_524_995, m.SizeBytes);
        Assert.Equal(Now.AddSeconds(300), m.ExpiresAt);
    }

    [Fact]
    public void DeviceVersionUnknown_TreatedAsOldest_ReturnsUpdate()
    {
        var svc = WithRelease(latest: "1.0.1");
        Assert.True(svc.Build(null, null, Now).UpdateAvailable);
    }

    [Fact]
    public void BoardMismatch_ReturnsNoUpdate()
    {
        var svc = WithRelease(latest: "1.0.1", board: "areg-s3-n8");
        Assert.False(svc.Build("1.0.0", "some-other-board", Now).UpdateAvailable);
    }

    [Fact]
    public void BoardMatch_ReturnsUpdate()
    {
        var svc = WithRelease(latest: "1.0.1", board: "areg-s3-n8");
        Assert.True(svc.Build("1.0.0", "areg-s3-n8", Now).UpdateAvailable);
    }

    [Fact]
    public void Signature_EmptyPlaceholder_WhenNoKey()
    {
        var svc = WithRelease(signingKey: "");
        Assert.Equal("", svc.Build("1.0.0", null, Now).Signature);
    }

    [Fact]
    public void Signature_Hmac_WhenKeyConfigured_AndDeterministic()
    {
        var svc = WithRelease(signingKey: "super-secret-key");

        var a = svc.Build("1.0.0", null, Now).Signature;
        var b = svc.Build("1.0.0", null, Now).Signature;

        Assert.False(string.IsNullOrEmpty(a));
        Assert.Equal(64, a!.Length);   // hex of SHA-256
        Assert.Equal(a, b);            // same inputs → same signature
    }
}
