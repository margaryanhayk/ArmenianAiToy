using ArmenianAiToy.Application.Helpers;
using ArmenianAiToy.Application.Services;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for the device content-manifest (Cloud→SD sync, minimal slice).
/// Pins: fail-closed empty manifest (disabled / unconfigured / bad hash),
/// deterministic single item with sha256+size when configured.
/// </summary>
public class ContentManifestServiceTests
{
    private const string ValidSha = "4ba0969646dfcb34ede49b3c82ac234a55299ab6789354f0ccbc6beb64f7e631";

    private static ContentManifestService Svc(Action<ContentSyncOptions>? mutate = null)
    {
        var opts = new ContentSyncOptions
        {
            Enabled = true,
            StoryId = "anban-huri",
            Version = 2,
            Title = "Անբան Հուռին",
            AudioUrl = "/api/devices/content-file",
            SizeBytes = 1_264_752,
            Sha256 = ValidSha.ToUpperInvariant(), // service must normalize to lowercase
        };
        mutate?.Invoke(opts);
        return new ContentManifestService(opts);
    }

    [Fact]
    public void Disabled_ReturnsEmptyList()
    {
        var m = Svc(o => o.Enabled = false).Build();
        Assert.Empty(m.Stories);
    }

    [Fact]
    public void DefaultOptions_ReturnsEmptyList()
    {
        // Shipped appsettings defaults (all empty) must yield an empty
        // manifest, never a half-configured item.
        var m = new ContentManifestService(new ContentSyncOptions()).Build();
        Assert.Empty(m.Stories);
    }

    [Theory]
    [InlineData("")]          // no story id
    public void MissingStoryId_ReturnsEmpty(string storyId)
    {
        Assert.Empty(Svc(o => o.StoryId = storyId).Build().Stories);
    }

    [Fact]
    public void MissingOrShortSha256_ReturnsEmpty()
    {
        Assert.Empty(Svc(o => o.Sha256 = "").Build().Stories);
        Assert.Empty(Svc(o => o.Sha256 = "abc123").Build().Stories); // truncated
    }

    [Fact]
    public void ZeroSize_ReturnsEmpty()
    {
        Assert.Empty(Svc(o => o.SizeBytes = 0).Build().Stories);
    }

    [Fact]
    public void Configured_ReturnsDeterministicItem_WithShaAndSize()
    {
        var a = Svc().Build();
        var b = Svc().Build();

        var item = Assert.Single(a.Stories);
        Assert.Equal("anban-huri", item.StoryId);
        Assert.Equal(2, item.Version);
        Assert.Equal("Անբան Հուռին", item.Title);
        Assert.Equal("/api/devices/content-file", item.AudioUrl);
        Assert.Equal(ValidSha, item.Sha256);           // lowercased on the wire
        Assert.Equal(1_264_752, item.SizeBytes);
        Assert.True(item.Enabled);
        Assert.Equal(a.Stories[0], b.Stories[0]);      // deterministic
    }

    [Fact]
    public void NonPositiveVersion_ClampsToOne()
    {
        var item = Assert.Single(Svc(o => o.Version = 0).Build().Stories);
        Assert.Equal(1, item.Version);
    }
}
