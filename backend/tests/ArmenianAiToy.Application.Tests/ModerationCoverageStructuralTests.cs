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
///
/// <para>
/// <b>Granularity is the ACTION, not the controller.</b> A controller's
/// constructor is shared by all its actions, but a <c>[FromServices]</c>
/// parameter belongs to ONE action. Unioning them per controller would let a
/// moderated action (<c>DeviceController.VoiceIntent</c>) vouch for an
/// unmoderated one added beside it. So each action is judged on its own
/// scope: constructor parameters plus ITS OWN parameters.
/// </para>
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

    // One unit of dependency scope. A plain type is one unit (its constructor).
    // A controller is one unit PER public action: constructor parameters plus
    // that action's own parameters (DeviceController.VoiceIntent takes
    // [FromServices] services; ASP.NET also infers interface-typed parameters,
    // so every parameter counts, attribute or not — the stricter reading).
    private sealed record Scope(Type Type, string Action, IReadOnlyList<Type> Injected)
    {
        public string Name => Action.Length == 0 ? Type.FullName! : $"{Type.FullName}.{Action}";
        public bool ReachesModel => Injected.Intersect(ModelReaching).Any();
        public bool HasModeration => Injected.Contains(typeof(IModerationService));
    }

    private static IEnumerable<Type> CtorParameters(Type t)
        => t.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType);

    private static IEnumerable<MethodInfo> PublicMethods(Type t)
        => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

    private static IEnumerable<Scope> Scopes(Type t)
    {
        var ctor = CtorParameters(t).ToList();
        var methods = typeof(ControllerBase).IsAssignableFrom(t) ? PublicMethods(t).ToList() : [];
        if (methods.Count == 0)
        {
            yield return new Scope(t, "", ctor);       // a controller with no actions still has its constructor
            yield break;
        }
        foreach (var m in methods)
            yield return new Scope(t, m.Name, ctor.Concat(m.GetParameters().Select(p => p.ParameterType)).ToList());
    }

    private static IEnumerable<Type> Concrete(IEnumerable<Assembly> asm) =>
        asm.SelectMany(a => a.GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false });

    private static IEnumerable<Scope> Unmoderated(IEnumerable<Type> types) =>
        types.Where(t => !UnmoderatedWrappers.Contains(t))
            .SelectMany(Scopes)
            .Where(sc => sc.ReachesModel && !sc.HasModeration);

    [Fact]
    public void EveryModelConsumingType_AlsoInjectsModeration()
    {
        var consumers = Concrete(Scanned).SelectMany(Scopes).Where(sc => sc.ReachesModel)
            .Select(sc => sc.Type).Distinct().ToList();

        // Guard against a vacuous pass if a rename moves the types out of the scan.
        foreach (var known in new[] { typeof(ChatService), typeof(StoryQaController),
                     typeof(StoryQaTextController), typeof(InternalController) })
            Assert.Contains(known, consumers);

        // ...and that the scan really is per action (a controller-wide scope would be one unit).
        Assert.True(Scopes(typeof(DeviceController)).Count() > 1);
        Assert.Contains(Scopes(typeof(DeviceController)), sc => sc.Action == nameof(DeviceController.VoiceIntent) && sc.HasModeration);

        var offenders = Unmoderated(Concrete(Scanned)).Select(sc => sc.Name).ToList();
        Assert.True(offenders.Count == 0,
            "These types/actions reach a model but do not hold IModerationService in their own scope " +
            "(constructor + that action's parameters; SHIP.md B1): " +
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

        Assert.Equal([typeof(BypassingConsumer)], flagged.Select(sc => sc.Type));
    }

    // The 2026-10-27 hole: one controller, one moderated action, one not. The
    // old per-controller union saw IModerationService (from the first action's
    // [FromServices]) and passed the second. [Route("api/chat/...")] keeps it
    // inside the child-facing prefix for the route check below.
    [Route("api/chat/fixture")]
    private sealed class MixedActionsController : ControllerBase
    {
        [HttpPost("moderated")]
        public IActionResult Moderated([FromServices] IAiChatClient ai, [FromServices] IModerationService mod)
            => Ok(new object[] { ai, mod });

        [HttpPost("free-chat")]
        public IActionResult Unmoderated([FromServices] IAiChatClient ai) => Ok(ai);
    }

    // Moderation in the constructor covers every action of that controller.
    [Route("api/chat/fixture-ctor")]
    private sealed class CtorModeratedController(IModerationService mod) : ControllerBase
    {
        [HttpPost("a")] public IActionResult A([FromServices] IAiChatClient ai) => Ok(new object[] { ai, mod });
        [HttpPost("b")] public IActionResult B([FromServices] LibraryStoryQuestionService q) => Ok(q);
    }

    [Fact]
    public void Check_JudgesEachActionOnItsOwn_NotTheControllerUnion()
    {
        // The old view: the controller-wide union contains moderation...
        var union = Scopes(typeof(MixedActionsController)).SelectMany(sc => sc.Injected).ToList();
        Assert.Contains(typeof(IModerationService), union);

        // ...but the per-action check still flags the one action without it.
        var flagged = Unmoderated([typeof(MixedActionsController)]).ToList();
        Assert.Equal([nameof(MixedActionsController.Unmoderated)], flagged.Select(sc => sc.Action));

        Assert.Empty(Unmoderated([typeof(CtorModeratedController)]));
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

    private sealed record RouteAction(Type Controller, MethodInfo Method, string Route);

    private static IEnumerable<RouteAction> RoutesOf(IEnumerable<Type> controllers) =>
        controllers.Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(c =>
            {
                var prefix = c.GetCustomAttributes<RouteAttribute>().FirstOrDefault()?.Template ?? "";
                return PublicMethods(c)
                    .SelectMany(m => m.GetCustomAttributes().OfType<HttpMethodAttribute>().Select(a => (m, a)))
                    .Select(x => x.a.Template is { } t && (t.StartsWith('/') || t.StartsWith("~/"))
                        ? new RouteAction(c, x.m, t.TrimStart('~').Trim('/'))   // absolute: ignores the controller prefix
                        : new RouteAction(c, x.m, $"{prefix}/{x.a.Template}".Trim('/')));
            });

    private static IEnumerable<RouteAction> Routes() => RoutesOf(Concrete([typeof(ChatController).Assembly]));

    // A child-facing ACTION is covered only by what is in ITS scope (constructor
    // + its own parameters): IModerationService itself, or IChatService.
    private static IEnumerable<RouteAction> Uncovered(IEnumerable<RouteAction> childFacing) =>
        childFacing.Where(x =>
        {
            if (ModelFree.ContainsKey(x.Controller)) return false;
            var injected = CtorParameters(x.Controller)
                .Concat(x.Method.GetParameters().Select(p => p.ParameterType)).ToList();
            return !injected.Contains(typeof(IModerationService)) && !injected.Contains(typeof(IChatService));
        });

    [Fact]
    public void EveryChildFacingRoute_IsModerated_OrExplicitlyModelFree()
    {
        var scope = Routes().Where(x => IsChildFacingRoute(x.Route)).ToList();

        foreach (var known in new[] { typeof(ChatController), typeof(AudioChatController), typeof(StoryQaController),
                     typeof(StoryQaTextController), typeof(InternalController), typeof(DeviceController) })
            Assert.Contains(scope, x => x.Controller == known);

        var uncovered = Uncovered(scope).Select(x => $"{x.Controller.Name}.{x.Method.Name} ({x.Route})").ToList();
        Assert.True(uncovered.Count == 0,
            "Child-facing routes without moderation in their own scope (SHIP.md B1): " + string.Join(", ", uncovered));

        // A "model-free" label must be true.
        foreach (var c in ModelFree.Keys)
            Assert.Empty(Scopes(c).SelectMany(sc => sc.Injected).Intersect(ModelReaching));
    }

    [Fact]
    public void ChildFacingRouteCheck_FailsOnTheUnmoderatedActionOnly()
    {
        var scope = RoutesOf([typeof(MixedActionsController), typeof(CtorModeratedController)])
            .Where(x => IsChildFacingRoute(x.Route)).ToList();
        Assert.Equal(4, scope.Count);       // the fixtures really are inside the guarded prefix

        var uncovered = Uncovered(scope).ToList();

        var only = Assert.Single(uncovered);
        Assert.Equal(nameof(MixedActionsController.Unmoderated), only.Method.Name);
        Assert.Equal("api/chat/fixture/free-chat", only.Route);
    }
}
