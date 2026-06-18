using ArmenianAiToy.Api.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <see cref="RateLimitRejectionPolicy.ResolvePolicyTag"/>.
/// Pure-unit style against <see cref="DefaultHttpContext"/>, same
/// pattern as <see cref="AuthRateLimiterTests"/> and
/// <see cref="MetricsScrapeAuthTests"/>.
///
/// <para>
/// The mapping itself is two-valued by construction. These tests pin
/// the bounded value space and the defensive fallbacks so a future
/// refactor that accidentally introduced a third tag value (or
/// regressed to path-based inference) would fail loudly.
/// </para>
/// </summary>
public class RateLimitRejectionPolicyTests
{
    private static HttpContext CtxWithRateLimitingAttribute(string? policyName)
    {
        var ctx = new DefaultHttpContext();
        if (policyName is null)
        {
            // No endpoint at all — defensive branch. Leaves
            // IEndpointFeature unset.
            return ctx;
        }

        // Build an Endpoint that carries exactly one
        // EnableRateLimitingAttribute in its metadata, matching how
        // ASP.NET Core records the attribute at endpoint-routing time.
        var endpoint = new Endpoint(
            requestDelegate: null,
            metadata: new EndpointMetadataCollection(
                new EnableRateLimitingAttribute(policyName)),
            displayName: "test-endpoint");
        ctx.SetEndpoint(endpoint);
        return ctx;
    }

    [Fact]
    public void ResolvePolicyTag_ChatEndpoint_ReturnsChat()
    {
        var ctx = CtxWithRateLimitingAttribute(ChatRateLimiter.PolicyName);
        Assert.Equal(RateLimitRejectionPolicy.ChatTag,
            RateLimitRejectionPolicy.ResolvePolicyTag(ctx));
    }

    [Fact]
    public void ResolvePolicyTag_AuthEndpoint_ReturnsAuth()
    {
        var ctx = CtxWithRateLimitingAttribute(AuthRateLimiter.PolicyName);
        Assert.Equal(RateLimitRejectionPolicy.AuthTag,
            RateLimitRejectionPolicy.ResolvePolicyTag(ctx));
    }

    [Fact]
    public void ResolvePolicyTag_StoryAudioEndpoint_ReturnsStoryAudio()
    {
        var ctx = CtxWithRateLimitingAttribute(StoryAudioRateLimiter.PolicyName);
        Assert.Equal(RateLimitRejectionPolicy.StoryAudioTag,
            RateLimitRejectionPolicy.ResolvePolicyTag(ctx));
    }

    [Fact]
    public void ResolvePolicyTag_NoEndpoint_FallsBackToChat()
    {
        // Defensive fallback documented on the helper — keeps the
        // value space bounded even if the middleware order changes.
        var ctx = CtxWithRateLimitingAttribute(policyName: null);
        Assert.Equal(RateLimitRejectionPolicy.ChatTag,
            RateLimitRejectionPolicy.ResolvePolicyTag(ctx));
    }

    [Fact]
    public void ResolvePolicyTag_UnknownPolicyName_FallsBackToChat()
    {
        // A future policy shipped without extending this helper must
        // NOT leak a new tag value to Prometheus. It instead maps to
        // the safe default "chat" until explicitly added.
        var ctx = CtxWithRateLimitingAttribute("some-future-policy");
        Assert.Equal(RateLimitRejectionPolicy.ChatTag,
            RateLimitRejectionPolicy.ResolvePolicyTag(ctx));
    }

    [Fact]
    public void ResolvePolicyTag_ValueSpace_IsExactlyChatAuthAndStoryAudio()
    {
        // Documents the bounded-value-space invariant at a place where
        // a failing test would surface ANY accidental widening. The map
        // is exactly three fixed values; a fourth must be added here
        // explicitly (never an unbounded dimension).
        Assert.Equal("chat", RateLimitRejectionPolicy.ChatTag);
        Assert.Equal("auth", RateLimitRejectionPolicy.AuthTag);
        Assert.Equal("story_audio", RateLimitRejectionPolicy.StoryAudioTag);

        // And confirm only these three possibilities come out of the
        // helper under the inputs we care about.
        var chatCtx = CtxWithRateLimitingAttribute(ChatRateLimiter.PolicyName);
        var authCtx = CtxWithRateLimitingAttribute(AuthRateLimiter.PolicyName);
        var storyCtx = CtxWithRateLimitingAttribute(StoryAudioRateLimiter.PolicyName);
        var chatTag = RateLimitRejectionPolicy.ResolvePolicyTag(chatCtx);
        var authTag = RateLimitRejectionPolicy.ResolvePolicyTag(authCtx);
        var storyTag = RateLimitRejectionPolicy.ResolvePolicyTag(storyCtx);
        Assert.Equal(new[] { "auth", "chat", "story_audio" },
            new[] { authTag, chatTag, storyTag });
    }
}
