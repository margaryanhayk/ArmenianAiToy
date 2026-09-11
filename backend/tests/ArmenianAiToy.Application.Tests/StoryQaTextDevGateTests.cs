using System.Text;
using ArmenianAiToy.Api.Observability;
using Microsoft.AspNetCore.Http;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// Tests for <see cref="StoryQaTextDevGate"/>, the pre-routing
/// middleware gate for <c>POST /api/story-qa-text</c>. Pure-unit style
/// against <see cref="DefaultHttpContext"/>, same pattern as
/// <see cref="MetricsScrapeAuthTests"/> — no WebApplicationFactory /
/// TestHost is introduced, as the slice forbids new NuGet packages, so
/// the actual <c>Program.cs</c> wiring (this gate runs before
/// <c>app.MapControllers()</c>) is proven by pattern-match against the
/// already-working <c>/api/internal/*</c> gate rather than by an
/// end-to-end HTTP test.
/// <para>
/// The controller-level behaviour for well-formed / malformed requests
/// IN Development, and for a well-formed request OUTSIDE Development,
/// stays pinned by <see cref="StoryQaTextControllerModerationTests"/>.
/// What this suite adds is the fact the recon exposed: outside
/// Development, a malformed or bodyless HTTP request used to reach
/// ASP.NET's own 400/415 (leaking route existence) because the
/// controller's check ran AFTER model binding. This gate runs before
/// routing and never inspects the body at all, so it cannot draw that
/// distinction — pinned below by asserting the path match (the only
/// thing the gate looks at) is identical regardless of what the request
/// body contains.
/// </para>
/// </summary>
public class StoryQaTextDevGateTests
{
    // ────────────────────────────────────────────────────────────
    // Path constant — pins the endpoint this guard covers.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void Path_IsExactlySlashApiStoryQaText()
    {
        Assert.Equal("/api/story-qa-text", StoryQaTextDevGate.Path);
    }

    // ────────────────────────────────────────────────────────────
    // Path matching.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void IsStoryQaTextPath_ExactPath_Matches()
    {
        Assert.True(StoryQaTextDevGate.IsStoryQaTextPath("/api/story-qa-text"));
    }

    [Theory]
    [InlineData("/API/STORY-QA-TEXT")]
    [InlineData("/Api/Story-Qa-Text")]
    public void IsStoryQaTextPath_IsCaseInsensitive(string path)
    {
        Assert.True(StoryQaTextDevGate.IsStoryQaTextPath(path));
    }

    [Theory]
    [InlineData("/api/story-qa-text/")]
    [InlineData("/api/story-qa-text/extra")]
    [InlineData("/api/story-qa")]
    [InlineData("/api/internal/devices")]
    [InlineData("/")]
    [InlineData("")]
    public void IsStoryQaTextPath_AnyOtherPath_DoesNotMatch(string path)
    {
        Assert.False(StoryQaTextDevGate.IsStoryQaTextPath(path));
    }

    // ────────────────────────────────────────────────────────────
    // The bug this gate closes: the decision must not depend on the
    // request body — a well-formed request, malformed JSON, and a
    // bodyless request all have to be indistinguishable to a caller
    // outside Development.
    // ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{"storyId":"ulik","segment":0,"question":"Ի՞նչ եղավ հետո"}""")] // well-formed
    [InlineData("{not-valid-json")] // malformed
    [InlineData(null)] // bodyless
    public void IsStoryQaTextPath_SameVerdict_RegardlessOfBodyShape(string? body)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = StoryQaTextDevGate.Path;
        if (body is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Request.Body = new MemoryStream(bytes);
            ctx.Request.ContentLength = bytes.Length;
            ctx.Request.ContentType = "application/json";
        }

        // The gate is decided from ctx.Request.Path alone — it never
        // reads ctx.Request.Body, so every shape above matches equally.
        Assert.True(StoryQaTextDevGate.IsStoryQaTextPath(ctx.Request.Path));
    }
}
