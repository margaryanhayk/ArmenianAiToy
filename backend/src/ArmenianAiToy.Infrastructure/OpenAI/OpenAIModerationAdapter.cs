using System.ClientModel;
using System.Diagnostics;
using ArmenianAiToy.Application.Interfaces;
using Microsoft.Extensions.Logging;
using OpenAI.Moderations;
using AppModerationResult = ArmenianAiToy.Application.DTOs.ModerationResult;

namespace ArmenianAiToy.Infrastructure.OpenAI;

public class OpenAIModerationAdapter : IModerationService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    // D1: single retry on transient 429 with a small constant backoff.
    // Never retry on other exception classes.
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(400);

    // Narrow false-positive override for the violence category.
    //
    // Problem: omni-moderation-latest flags "tell me a story" (score 0.35)
    // and "պdelays delays" (0.36) as violence:true. But it also correctly
    // flags "how to make a bomb" as violence:true at score 0.01. This means
    // the Flagged boolean carries classification logic beyond the raw score,
    // so a simple score threshold is unsafe.
    //
    // Solution: keep the Flagged boolean for ALL categories. Apply a narrow
    // override ONLY when all of these conditions are met simultaneously:
    //   1. Violence is the SOLE flagged category (no sexual/self-harm/etc.)
    //   2. Violence score is below 0.40 (high-confidence violence scores 0.5+)
    //   3. The input contains NONE of the violence keywords below
    //
    // If any condition fails, the block stands. This catches "how to make a
    // bomb" (contains "bomb") while letting through "tell me a story"
    // (no violence keywords, score 0.35).
    internal const float ViolenceFalsePositiveCeiling = 0.40f;

    // Words that are never appropriate in a 4-7 year-old's input, even in
    // fairy-tale context. Kept short and unambiguous on purpose — words like
    // "fight", "hit", "sword" are omitted because they appear in normal
    // children's stories. Checked case-insensitively.
    internal static readonly string[] ViolenceKeywords =
    [
        "kill", "murder", "bomb", "weapon", "gun", "shoot", "stab",
        "poison", "explode", "suicide", "terrorist", "terror",
        "drug", "alcohol",
    ];

    private readonly ModerationClient _client;
    private readonly ILogger<OpenAIModerationAdapter> _logger;

    public OpenAIModerationAdapter(ModerationClient client, ILogger<OpenAIModerationAdapter> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<AppModerationResult> CheckContentAsync(string content)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            return await ClassifyOnceAsync(content);
        }
        catch (ClientResultException cre) when (cre.Status == 429)
        {
            _logger.LogWarning(
                "Moderation transient 429 — retrying once after {DelayMs} ms. preview={Preview}",
                (int)RetryDelay.TotalMilliseconds, Preview(content));
            try
            {
                await Task.Delay(RetryDelay);
                var result = await ClassifyOnceAsync(content);
                _logger.LogInformation(
                    "Moderation transient 429 — recovered on retry. latency_ms={LatencyMs} preview={Preview}",
                    sw.ElapsedMilliseconds, Preview(content));
                return result;
            }
            catch (Exception retryEx)
            {
                return FailClosed(
                    reason: "rate_limited_retry_failed",
                    status: StatusOf(retryEx),
                    latencyMs: sw.ElapsedMilliseconds,
                    retryCount: 1,
                    ex: retryEx,
                    content: content);
            }
        }
        catch (ClientResultException cre)
        {
            var reason = cre.Status is 401 or 403 ? "auth_error" : "server_error";
            return FailClosed(reason, cre.Status, sw.ElapsedMilliseconds, 0, cre, content);
        }
        catch (OperationCanceledException oce)
        {
            return FailClosed("timeout", 0, sw.ElapsedMilliseconds, 0, oce, content);
        }
        catch (Exception ex)
        {
            return FailClosed("network_error", 0, sw.ElapsedMilliseconds, 0, ex, content);
        }
    }

    /// <summary>
    /// Performs a single classification call and maps the OpenAI response to
    /// <see cref="AppModerationResult"/>. The call path is exposed as a
    /// narrow test seam — tests subclass the adapter and override this method
    /// to simulate specific exceptions or results. Production code always
    /// calls the real <c>ModerationClient</c>.
    /// </summary>
    protected virtual async Task<AppModerationResult> ClassifyOnceAsync(string content)
    {
        using var cts = new CancellationTokenSource(RequestTimeout);
        var raw = await _client.ClassifyTextAsync(content, cts.Token);
        var categories = raw.Value;

        var flagged = new List<string>();

        if (categories.Sexual.Flagged) flagged.Add("sexual");
        if (categories.Violence.Flagged) flagged.Add("violence");
        if (categories.SelfHarm.Flagged) flagged.Add("self-harm");
        if (categories.Hate.Flagged) flagged.Add("hate");
        if (categories.Harassment.Flagged) flagged.Add("harassment");

        // Narrow false-positive override. Conditions are intentionally
        // strict — see class-level comment for the full rationale.
        if (flagged.Count == 1
            && flagged[0] == "violence"
            && categories.Violence.Score < ViolenceFalsePositiveCeiling
            && !ContainsViolenceKeyword(content))
        {
            _logger.LogInformation(
                "Moderation violence false-positive override: score={VioScore:F4} (< {Ceiling:F2}), no violence keywords. ContentPreview: {Preview}",
                categories.Violence.Score, ViolenceFalsePositiveCeiling,
                Preview(content));
            flagged.Clear();
        }

        if (flagged.Count > 0)
        {
            _logger.LogWarning(
                "Moderation blocked. Categories: {Categories}, Scores: [sexual={SexScore:F4}, violence={VioScore:F4}, self-harm={ShScore:F4}, hate={HateScore:F4}, harassment={HarScore:F4}], ContentPreview: {Preview}",
                string.Join(", ", flagged),
                categories.Sexual.Score, categories.Violence.Score,
                categories.SelfHarm.Score, categories.Hate.Score, categories.Harassment.Score,
                Preview(content));
        }

        return new AppModerationResult(flagged.Count == 0, flagged);
    }

    private AppModerationResult FailClosed(
        string reason, int status, long latencyMs, int retryCount, Exception ex, string content)
    {
        _logger.LogError(ex,
            "Moderation unavailable. reason={Reason} status={Status} latency_ms={LatencyMs} retry_count={RetryCount} preview={Preview}",
            reason, status, latencyMs, retryCount, Preview(content));
        return new AppModerationResult(false, new List<string> { "moderation_unavailable" });
    }

    private static int StatusOf(Exception ex) =>
        ex is ClientResultException c ? c.Status : 0;

    private static string Preview(string content) =>
        content.Length > 80 ? content[..80] + "..." : content;

    private static bool ContainsViolenceKeyword(string content)
    {
        var lower = content.ToLowerInvariant();
        for (int i = 0; i < ViolenceKeywords.Length; i++)
        {
            if (lower.Contains(ViolenceKeywords[i]))
                return true;
        }
        return false;
    }
}
