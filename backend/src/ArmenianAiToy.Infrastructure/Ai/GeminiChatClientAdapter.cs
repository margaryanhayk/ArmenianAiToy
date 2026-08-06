using ArmenianAiToy.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArmenianAiToy.Infrastructure.Ai;

/// <summary>
/// Google Gemini chat adapter (owner decision 2026-08-06: «GEMINI is
/// winner unanimous» after the 7-case toy bake-off vs gpt-5.3-chat /
/// gpt-5.4-mini — richer Armenian, «Կար-չկար» openings, honest
/// corrections — and gpt-4o, the shipped model, is being retired).
///
/// <para>
/// Selected via <c>AI:ChatProvider = "gemini"</c>. Raw BCL HttpClient
/// against <c>POST /v1beta/models/{model}:generateContent</c> — no new
/// NuGet, same posture as the Resend/ElevenLabs adapters. Config:
/// <c>Gemini:ApiKey</c> (falls back to the <c>GEMINI_API_KEY</c> env
/// name), <c>Gemini:Model</c> (default <c>gemini-3-flash-preview</c> —
/// the exact model the bake-off ran and the ArmBench-LLM 1.0 winner;
/// re-run the bake-off before pointing at a newer id).
/// </para>
///
/// <para>
/// Thinking is DISABLED per request (<c>thinkingBudget: 0</c>) — the
/// measured thinking-mode TTFT (~7 s) is unusable on a toy; fast mode
/// is how the bake-off ran. The 30-second ceiling matches the OpenAI
/// adapter. Not routed through OpenAIReliabilityGate (that gate's
/// classification is OpenAI-SDK-shaped); a Gemini-aware retry/breaker
/// is a follow-up — failures surface to ChatController's existing
/// Path-5 sanitized 502. Bake-off note recorded: Gemini needed the
/// companion-boundary told off harder than GPT («իմ փոքրիկ ընկեր») —
/// the full production prompt carries those rules; the compact bake-off
/// prompt did not. Output moderation and the quality gates run on every
/// reply regardless of provider.
/// </para>
/// </summary>
/// <summary>Typed holder so DI can register Gemini's gate instance
/// without colliding with the OpenAI singleton of the same class.</summary>
public sealed record GeminiGateHolder(OpenAI.OpenAIReliabilityGate Gate);

public class GeminiChatClientAdapter : IAiChatClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Default per-request Gemini safety threshold (owner
    /// approval 2026-08-06). Strictest blocking tier — children's
    /// product. Configurable DOWN via <c>Gemini:SafetyThreshold</c> only
    /// within <see cref="AllowedSafetyThresholds"/>; BLOCK_NONE is
    /// deliberately not representable.</summary>
    public const string DefaultSafetyThreshold = "BLOCK_LOW_AND_ABOVE";

    /// <summary>Bounded value space for <c>Gemini:SafetyThreshold</c>.
    /// A typo must refuse boot (same posture as AiProviderConfig), and
    /// BLOCK_NONE is excluded so no config value can switch Gemini's own
    /// safety filter off.</summary>
    public static readonly string[] AllowedSafetyThresholds =
        { "BLOCK_LOW_AND_ABOVE", "BLOCK_MEDIUM_AND_ABOVE", "BLOCK_ONLY_HIGH" };

    /// <summary>Mirrors ChatService's <c>SafetyFallbackResponse</c>
    /// default — the calm Armenian line a child hears instead of an
    /// error when a reply is withheld.</summary>
    public const string DefaultSafetyFallbackText = "Արի, մի հեքիաթ սկսենք։";

    /// <summary>The four Gemini harm categories the request pins. Kept
    /// as a field so tests can assert the wire body covers all of them.</summary>
    public static readonly string[] SafetyCategories =
    {
        "HARM_CATEGORY_HARASSMENT",
        "HARM_CATEGORY_HATE_SPEECH",
        "HARM_CATEGORY_SEXUALLY_EXPLICIT",
        "HARM_CATEGORY_DANGEROUS_CONTENT",
    };

    /// <summary>Resolve + validate <c>Gemini:SafetyThreshold</c>.
    /// Null/empty → the strict default; a value outside
    /// <see cref="AllowedSafetyThresholds"/> throws — boot must refuse a
    /// typo rather than silently weaken child safety (BLOCK_NONE is not
    /// in the allowed set on purpose).</summary>
    public static string ResolveSafetyThreshold(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return DefaultSafetyThreshold;
        if (Array.IndexOf(AllowedSafetyThresholds, configured) < 0)
        {
            throw new InvalidOperationException(
                $"Gemini:SafetyThreshold '{configured}' is not supported. " +
                $"Allowed: {string.Join(", ", AllowedSafetyThresholds)}.");
        }
        return configured;
    }

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int? _thinkingBudget;
    private readonly string _safetyThreshold;
    private readonly string _safetyFallbackText;
    private readonly OpenAI.OpenAIReliabilityGate? _gate;
    private readonly ILogger<GeminiChatClientAdapter> _logger;

    public GeminiChatClientAdapter(
        HttpClient http,
        string apiKey,
        string model,
        ILogger<GeminiChatClientAdapter> logger,
        OpenAI.OpenAIReliabilityGate? gate = null,
        int? thinkingBudget = null,
        string? safetyThreshold = null,
        string? safetyFallbackText = null)
    {
        _http = http;
        _apiKey = apiKey;
        // Default moved to the STABLE flash (2026-08-06): the -preview
        // endpoint 503'd on ~17% of raw requests during baseline capture
        // (Google-side overload), while 3.6-flash answered clean at
        // ~1.4 s. NOTE the API drift: 3.6 REJECTS thinkingConfig with
        // HTTP 400 — thinking is omitted unless Gemini:ThinkingBudget is
        // explicitly configured (needed only for the older preview
        // model, where omitting it means ~7 s thinking-mode TTFT).
        _model = string.IsNullOrWhiteSpace(model) ? "gemini-3.6-flash" : model;
        _thinkingBudget = thinkingBudget;
        _safetyThreshold = string.IsNullOrWhiteSpace(safetyThreshold)
            ? DefaultSafetyThreshold : safetyThreshold;
        _safetyFallbackText = string.IsNullOrWhiteSpace(safetyFallbackText)
            ? DefaultSafetyFallbackText : safetyFallbackText;
        _logger = logger;
        _gate = gate;
    }

    public Task<string> GetCompletionAsync(
        string systemPrompt, List<(string Role, string Content)> messages)
    {
        // Same reliability semantics as the OpenAI adapter: retry-once on
        // 429/5xx/timeout with jittered backoff, circuit breaker on
        // repeated failure — via a provider-tagged gate instance. The
        // classifier reads HttpRequestException.StatusCode, which
        // SendAsync sets. Null gate (older tests) = direct call.
        return _gate is null
            ? CoreAsync(systemPrompt, messages, CancellationToken.None)
            : _gate.RunAsync(
                ct => CoreAsync(systemPrompt, messages, ct),
                CancellationToken.None);
    }

    private async Task<string> CoreAsync(
        string systemPrompt, List<(string Role, string Content)> messages,
        CancellationToken outerCt)
    {
        var contents = new List<object>();
        foreach (var (role, content) in messages)
        {
            // Gemini's role vocabulary is user/model; anything unknown is
            // treated as user, mirroring the OpenAI adapter's fallback.
            var geminiRole = role == "assistant" ? "model" : "user";
            contents.Add(new { role = geminiRole, parts = new[] { new { text = content } } });
        }

        // Gemini's OWN safety filter, pinned on every request (owner
        // approval 2026-08-06). Belt-and-braces UNDER the product's dual
        // OpenAI moderation — this is the vendor-side layer that stops
        // an unsafe candidate from ever being generated, in whatever
        // language, before our output moderation even sees it.
        var safetySettings = Array.ConvertAll(
            SafetyCategories, c => new { category = c, threshold = _safetyThreshold });

        object body = _thinkingBudget is { } tb
            ? new
            {
                system_instruction = new { parts = new[] { new { text = systemPrompt } } },
                contents,
                safetySettings,
                generationConfig = new { thinkingConfig = new { thinkingBudget = tb } },
            }
            : new
            {
                system_instruction = new { parts = new[] { new { text = systemPrompt } } },
                contents,
                safetySettings,
            };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        cts.CancelAfter(RequestTimeout);
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent");
        req.Headers.Add("x-goog-api-key", _apiKey);
        req.Content = JsonContent.Create(body);

        var resp = await _http.SendAsync(req, cts.Token);
        var json = await resp.Content.ReadAsStringAsync(cts.Token);
        if (!resp.IsSuccessStatusCode)
        {
            // Status only — bodies can echo prompt text; the key never
            // appears anywhere.
            _logger.LogWarning(
                "Gemini chat non-success: HTTP {Status} (model {Model})",
                (int)resp.StatusCode, _model);
            // StatusCode set so the reliability gate's classifier can map
            // 429/5xx to retryable kinds — message stays status-only.
            throw new HttpRequestException(
                $"Gemini chat returned HTTP {(int)resp.StatusCode}.",
                inner: null,
                statusCode: resp.StatusCode);
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Safety block, prompt level: no candidates at all, reason in
        // promptFeedback.blockReason. Before 2026-08-06 this path threw
        // (missing-property) and the child heard the sanitized 502
        // ("service unavailable") — wrong voice for a safety refusal.
        // A block is a VALID outcome: return the same calm Armenian
        // fallback ChatService uses, which then flows through output
        // moderation and persists as a normal assistant reply.
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            var blockReason =
                root.TryGetProperty("promptFeedback", out var pf)
                && pf.TryGetProperty("blockReason", out var br)
                    ? br.GetString() : "no_candidates";
            _logger.LogWarning(
                "Gemini blocked the prompt (reason {Reason}, model {Model}); returning the safety fallback",
                blockReason, _model);
            return _safetyFallbackText;
        }

        // Safety block, candidate level: generation was cut off by the
        // safety filter (or a related content policy) — same calm
        // fallback. Non-safety finish reasons (STOP, MAX_TOKENS, …)
        // fall through to normal parsing.
        var candidate = candidates[0];
        var finishReason = candidate.TryGetProperty("finishReason", out var fr)
            ? fr.GetString() : null;
        if (finishReason is "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "SPII" or "IMAGE_SAFETY")
        {
            _logger.LogWarning(
                "Gemini withheld the reply (finishReason {Reason}, model {Model}); returning the safety fallback",
                finishReason, _model);
            return _safetyFallbackText;
        }

        var parts = candidate
            .GetProperty("content")
            .GetProperty("parts");
        var sb = new System.Text.StringBuilder();
        foreach (var p in parts.EnumerateArray())
        {
            if (p.TryGetProperty("text", out var t)) sb.Append(t.GetString());
        }
        return sb.ToString();
    }
}
