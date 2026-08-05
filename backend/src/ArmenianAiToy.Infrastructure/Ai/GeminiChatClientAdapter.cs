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
public class GeminiChatClientAdapter : IAiChatClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<GeminiChatClientAdapter> _logger;

    public GeminiChatClientAdapter(
        HttpClient http,
        string apiKey,
        string model,
        ILogger<GeminiChatClientAdapter> logger)
    {
        _http = http;
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "gemini-3-flash-preview" : model;
        _logger = logger;
    }

    public async Task<string> GetCompletionAsync(
        string systemPrompt, List<(string Role, string Content)> messages)
    {
        var contents = new List<object>();
        foreach (var (role, content) in messages)
        {
            // Gemini's role vocabulary is user/model; anything unknown is
            // treated as user, mirroring the OpenAI adapter's fallback.
            var geminiRole = role == "assistant" ? "model" : "user";
            contents.Add(new { role = geminiRole, parts = new[] { new { text = content } } });
        }

        var body = new
        {
            system_instruction = new { parts = new[] { new { text = systemPrompt } } },
            contents,
            generationConfig = new { thinkingConfig = new { thinkingBudget = 0 } },
        };

        using var cts = new CancellationTokenSource(RequestTimeout);
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
            throw new HttpRequestException($"Gemini chat returned HTTP {(int)resp.StatusCode}.");
        }

        using var doc = JsonDocument.Parse(json);
        var parts = doc.RootElement
            .GetProperty("candidates")[0]
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
