using Microsoft.AspNetCore.Http;

namespace ArmenianAiToy.Api.Observability;

/// <summary>
/// Pre-routing concealment guard for the Development-only text harness
/// (<c>POST /api/story-qa-text</c>), wired as inline middleware in
/// <c>Program.cs</c> ahead of <c>app.MapControllers()</c> — the same
/// path-check-before-routing pattern <see cref="InternalAdminAuth"/> and
/// <see cref="MetricsScrapeAuth"/> already use.
///
/// <para>
/// <c>StoryQaTextController.Ask</c> already refuses outside Development,
/// but that check runs INSIDE the action — after <c>[ApiController]</c>'s
/// automatic model binding/validation. So outside Development, a
/// malformed body (unparseable JSON) or a bodyless POST used to get
/// ASP.NET's own 400/415 before the action's own check ever ran,
/// leaking that the route exists and what shape it expects — a
/// well-formed request got the intended 404, a malformed one did not.
/// This guard runs before routing, so a non-Development request never
/// reaches model binding at all: every shape of request (well-formed,
/// malformed, bodyless) now gets the identical 404, regardless of body
/// content — this guard does not even look at the body.
/// </para>
///
/// <para>
/// The controller's own check is left in place as defense in depth (a
/// route that must never reach GPT/moderation outside Development gets
/// two independent gates, not one), and is what the direct-controller
/// unit tests exercise without an HTTP pipeline.
/// </para>
/// </summary>
public static class StoryQaTextDevGate
{
    /// <summary>The single route this guard covers.</summary>
    public const string Path = "/api/story-qa-text";

    /// <summary>Whether <paramref name="path"/> is the guarded harness route
    /// or anything beneath it. Case-insensitive and segment-bounded
    /// (<see cref="PathString.StartsWithSegments(PathString, StringComparison)"/>,
    /// the same idiom as the <c>/api/internal</c> gate): routing treats a
    /// trailing slash as the same endpoint, so an exact match let
    /// <c>/api/story-qa-text/</c> reach model binding (AI logic review
    /// 2026-10-27, B12), while a sibling such as <c>/api/story-qa-textx</c>
    /// or <c>/api/story-qa</c> still does not match.</summary>
    public static bool IsStoryQaTextPath(PathString path) =>
        path.StartsWithSegments(Path, StringComparison.OrdinalIgnoreCase);
}
