using System.Reflection;
using ArmenianAiToy.Api.Controllers;
using ArmenianAiToy.Application.Interfaces;
using ArmenianAiToy.Application.Services;
using ArmenianAiToy.Application.Stories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ArmenianAiToy.Application.Tests;

/// <summary>
/// SHIP.md B1: "Every response path passes moderation. Proven by a test that
/// fails if a path bypasses it." Each path is pinned behaviourally elsewhere;
/// this is the STRUCTURAL net. Anything that can reach a model must also hold
/// <see cref="IModerationService"/>, and every route that can speak to a
/// child must be classified. Structural only: it cannot prove the injected
/// moderation is CALLED (the per-path tests do), nor see a type that resolves
/// <c>IAiChatClient</c> through <c>IServiceProvider</c>.
/// </summary>
public class ModerationCoverageStructuralTests
{
    // These two take IAiChatClient and moderate nothing themselves: they are
    // prompt builders, and every caller (ChatService, StoryQaController,
    // StoryQaTextController, InternalController) moderates around them. They
    // are exempt as consumers but count as model-reaching for THEIR consumers.
    private static readonly Type[] UnmoderatedWrappers =
        [typeof(LibraryStoryQuestionService), typeof(ReflectionDialogueService)];

    private static readonly Type[] ModelReaching = [typeof(IAiChatClient), .. UnmoderatedWrappers];

    private static readonly Assembly[] Scanned =
    [
        typeof(ChatService).Assembly,      // Application
        typeof(ChatController).Assembly,   // Api
        typeof(ArmenianAiToy.Infrastructure.Ai.GeminiChatClientAdapter).Assembly, // Infrastructure
    ];

    // Everything a type can be handed: constructor parameters, plus [FromServices]
    // action parameters on controllers (DeviceController.VoiceIntent does this).
    private static IEnumerable<Type> Injected(Type t)
    {
        var methods = typeof(ControllerBase).IsAssignableFrom(t)
            ? t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(m => (MethodBase)m)
            : [];
        return t.GetConstructors().Cast<MethodBase>().Concat(methods)
            .SelectMany(m => m.GetParameters()).Select(p => p.ParameterType);
    }

    private static IEnumerable<Type> Concrete(IEnumerable<Assembly> asm) =>
        asm.SelectMany(a => a.GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false });

    private static IEnumerable<Type> Unmoderated(IEnumerable<Type> types) =>
        types.Where(t => !UnmoderatedWrappers.Contains(t)
                         && Injected(t).Intersect(ModelReaching).Any()
                         && !Injected(t).Contains(typeof(IModerationService)));

    [Fact]
    public void EveryModelConsumingType_AlsoInjectsModeration()
    {
        var consumers = Concrete(Scanned).Where(t => Injected(t).Intersect(ModelReaching).Any()).ToList();

        // Guard against a vacuous pass if a rename moves the types out of the scan.
        foreach (var known in new[] { typeof(ChatService), typeof(StoryQaController),
                     typeof(StoryQaTextController), typeof(InternalController) })
            Assert.Contains(known, consumers);

        var offenders = Unmoderated(Concrete(Scanned)).Select(t => t.FullName).ToList();
        Assert.True(offenders.Count == 0,
            "These types reach a model but do not inject IModerationService (SHIP.md B1): " +
            string.Join(", ", offenders) + ". Moderate input and output, or, if a caller moderates for " +
            "it, add it to UnmoderatedWrappers with the test that pins that caller.");
    }

    // The check itself can fail: an unmoderated consumer is flagged, a moderated one is not.
    private sealed class BypassingConsumer(IAiChatClient ai) { public IAiChatClient Ai => ai; }
    private sealed class ModeratedConsumer(IAiChatClient ai, IModerationService m) { public object[] P => [ai, m]; }

    [Fact]
    public void Check_FlagsAModelConsumerWithoutModeration()
    {
        var flagged = Unmoderated([typeof(BypassingConsumer), typeof(ModeratedConsumer)]).ToList();

        Assert.Equal([typeof(BypassingConsumer)], flagged);
    }

    // Routes that can answer a child. A route here is covered by (a) injecting
    // IModerationService itself (ctor or [FromServices]) or (b) going through
    // IChatService, whose ChatService injects it (above) and whose moderation
    // is pinned path-by-path in ChatService*Tests. Anything else must be listed
    // as model-free, with the reason.
    private static readonly Dictionary<Type, string> ModelFree = new()
    {
        [typeof(StoryAudioTokenController)] = "mints a signed URL for stored story audio; no model, no free text",
    };

    private static bool IsChildFacingRoute(string r) =>
        r.StartsWith("api/chat") || r.StartsWith("api/story-qa-text") || r == "api/devices/voice-intent"
        || (r.StartsWith("api/internal/") && r.Contains("story-qa"));

    private static IEnumerable<(Type Controller, string Route)> Routes() =>
        Concrete([typeof(ChatController).Assembly]).Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(c =>
            {
                var prefix = c.GetCustomAttributes<RouteAttribute>().FirstOrDefault()?.Template ?? "";
                return c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(m => m.GetCustomAttributes().OfType<HttpMethodAttribute>())
                    .Select(a => a.Template is { } t && (t.StartsWith('/') || t.StartsWith("~/"))
                        ? (c, t.TrimStart('~').Trim('/'))              // absolute: ignores the controller prefix
                        : (c, $"{prefix}/{a.Template}".Trim('/')));
            });

    [Fact]
    public void EveryChildFacingRoute_IsModerated_OrExplicitlyModelFree()
    {
        var scope = Routes().Where(x => IsChildFacingRoute(x.Route)).ToList();

        foreach (var known in new[] { typeof(ChatController), typeof(AudioChatController), typeof(StoryQaController),
                     typeof(StoryQaTextController), typeof(InternalController), typeof(DeviceController) })
            Assert.Contains(scope, x => x.Controller == known);

        var uncovered = scope.Select(x => x.Controller).Distinct()
            .Where(c => !Injected(c).Contains(typeof(IModerationService))
                        && !Injected(c).Contains(typeof(IChatService))
                        && !ModelFree.ContainsKey(c))
            .Select(c => c.Name).ToList();
        Assert.True(uncovered.Count == 0,
            "Child-facing routes without moderation (SHIP.md B1): " + string.Join(", ", uncovered));

        // A "model-free" label must be true.
        foreach (var c in ModelFree.Keys)
            Assert.Empty(Injected(c).Intersect(ModelReaching));
    }
}
