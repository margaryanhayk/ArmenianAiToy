using ArmenianAiToy.Api.Security;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// 2026-09-11 — fail-closed dev/prod discipline for
/// <c>Audio:BlobStoreRoot</c>, the durable storage root for child +
/// assistant voice recordings. Same posture family as
/// <see cref="DatabaseConnectionStringTests"/> and the
/// <c>ContentSync:UploadRoot</c> contract, but non-throwing: the caller
/// (an endpoint, a health check, a startup log) decides what "not
/// configured" means for it.
/// </summary>
public class AudioBlobStoreRootResolverTests
{
    [Fact]
    public void Development_Unset_ResolvesToDevDefault()
    {
        var resolution = AudioBlobStoreRootResolver.Resolve(isDevelopment: true, configured: null);

        Assert.True(resolution.IsConfigured);
        Assert.Equal(AudioBlobStoreRootResolver.DevDefaultRoot, resolution.Root);
        Assert.Null(resolution.Reason);
    }

    [Fact]
    public void Development_Blank_ResolvesToDevDefault()
    {
        var resolution = AudioBlobStoreRootResolver.Resolve(isDevelopment: true, configured: "   ");

        Assert.True(resolution.IsConfigured);
        Assert.Equal(AudioBlobStoreRootResolver.DevDefaultRoot, resolution.Root);
    }

    [Fact]
    public void Development_RelativeConfigured_IsTrimmedAndReturned()
    {
        var resolution = AudioBlobStoreRootResolver.Resolve(isDevelopment: true, configured: "  my-blobs  ");

        Assert.True(resolution.IsConfigured);
        Assert.Equal("my-blobs", resolution.Root);
    }

    [Fact]
    public void Development_AbsoluteConfigured_IsReturned()
    {
        var resolution = AudioBlobStoreRootResolver.Resolve(isDevelopment: true, configured: "/data/audio-blobs");

        Assert.True(resolution.IsConfigured);
        Assert.Equal("/data/audio-blobs", resolution.Root);
    }

    [Fact]
    public void NonDevelopment_Unset_IsNotConfigured()
    {
        var resolution = AudioBlobStoreRootResolver.Resolve(isDevelopment: false, configured: null);

        Assert.False(resolution.IsConfigured);
        Assert.Null(resolution.Root);
        Assert.NotNull(resolution.Reason);
    }

    [Fact]
    public void NonDevelopment_Blank_IsNotConfigured()
    {
        var resolution = AudioBlobStoreRootResolver.Resolve(isDevelopment: false, configured: "   ");

        Assert.False(resolution.IsConfigured);
    }

    [Fact]
    public void NonDevelopment_Relative_IsNotConfigured()
    {
        // The load-bearing case: a relative path resolves under the app's
        // own directory, exactly as ephemeral as unset on a redeploy.
        var resolution = AudioBlobStoreRootResolver.Resolve(isDevelopment: false, configured: "audio-blobs");

        Assert.False(resolution.IsConfigured);
        Assert.Null(resolution.Root);
        Assert.Contains("relative", resolution.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonDevelopment_Absolute_IsConfigured()
    {
        var resolution = AudioBlobStoreRootResolver.Resolve(isDevelopment: false, configured: "/data/audio-blobs");

        Assert.True(resolution.IsConfigured);
        Assert.Equal("/data/audio-blobs", resolution.Root);
        Assert.Null(resolution.Reason);
    }

    [Fact]
    public void NonDevelopment_AbsoluteIsTrimmed()
    {
        var resolution = AudioBlobStoreRootResolver.Resolve(
            isDevelopment: false, configured: "  /data/audio-blobs  ");

        Assert.True(resolution.IsConfigured);
        Assert.Equal("/data/audio-blobs", resolution.Root);
    }
}
