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

    [Fact]
    public void Signature_VerifiesAgainstJsonWireForm()
    {
        // KEYSTONE for device-side verification: the device can only rebuild
        // the canonical string from the raw JSON text it received, so the
        // signature MUST verify against the JSON WIRE FORM of every field —
        // especially expiresAt, where "O"-format vs System.Text.Json rendering
        // differ (trailing fractional-second zeros). This test does exactly
        // what the firmware does: serialize the response the way ASP.NET
        // would, re-extract raw field strings, recompute the HMAC.
        const string Key = "bench-manifest-hmac-key-1";
        var svc = WithRelease(signingKey: Key);
        var manifest = svc.Build("1.0.0", null, Now);
        Assert.True(manifest.UpdateAvailable);

        // Serialize the way the API ACTUALLY does — including the
        // UtcDateTimeConverter registered in Program.cs. Without it this test
        // built its own idea of the wire, reproduced Sign()'s own trailing-zero
        // bug, and passed green while production refused ~1 update in 10.
        var wireOptions = new System.Text.Json.JsonSerializerOptions
        { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        wireOptions.Converters.Add(new WireUtcDateTimeConverter());
        var json = System.Text.Json.JsonSerializer.Serialize(manifest, wireOptions);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        var canonical =
            root.GetProperty("version").GetString() + "\n" +
            root.GetProperty("url").GetString() + "\n" +
            root.GetProperty("sha256").GetString() + "\n" +
            root.GetProperty("sizeBytes").GetInt64() + "\n" +
            root.GetProperty("expiresAt").GetRawText().Trim('"');

        using var h = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(Key));
        var recomputed = Convert.ToHexString(
            h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

        Assert.Equal(recomputed, root.GetProperty("signature").GetString());
    }
}

/// <summary>
/// Mirrors <c>ArmenianAiToy.Api.Serialization.UtcDateTimeConverter</c> for the
/// signature test. The test project does not reference Api, so this asserts on
/// the SHARED <see cref="ArmenianAiToy.Application.Helpers.JsonWireFormats"/>
/// constant that both the real converter and the real signer read — which is
/// what makes drift between them impossible to reintroduce silently.
/// </summary>
internal sealed class WireUtcDateTimeConverter
    : System.Text.Json.Serialization.JsonConverter<DateTime>
{
    public override DateTime Read(ref System.Text.Json.Utf8JsonReader reader, Type t,
        System.Text.Json.JsonSerializerOptions o) => reader.GetDateTime();

    public override void Write(System.Text.Json.Utf8JsonWriter writer, DateTime value,
        System.Text.Json.JsonSerializerOptions o)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        writer.WriteStringValue(utc.ToString(
            ArmenianAiToy.Application.Helpers.JsonWireFormats.UtcDateTime,
            System.Globalization.CultureInfo.InvariantCulture));
    }
}
