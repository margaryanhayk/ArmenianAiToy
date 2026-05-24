# Moderation fallback diagnosis — 2026-05-24

## Summary

The persistent `SafetyFlag=2` + «Մի րոպե սպասիր, ու նորից փորձենք։»
sentinel observed in the `20260524-170208` StoryInteractiveLoop
evidence is **caused by OpenAI billing quota exhaustion**, not a
code bug, not a rate-limit window, and not Armenian-vs-English
content classification.

The backend's moderation fail-closed path is doing exactly what it
was designed to do: when moderation cannot run, the request is
refused with a child-safe sentinel rather than being passed through
unmoderated.

**No code change is recommended in this slice.** The action belongs
to the OpenAI account owner (billing).

## Run context

- **Diagnosis timestamp**: 2026-05-24 ~18:00 UTC
- **Branch**: `main`
- **Commit SHA**: `6af2a3d`
- **Working tree**: dirty
  (pre-existing M files: `.claude/settings.local.json`,
  `esp32/AregVoiceMvp/config.h`; pre-existing untracked files
  unrelated to this slice — none touched)
- **Backend health**: `GET /api/health` → `200 OK`
  `{"status":"ok","service":"ArmenianAiToy API","database":"ok"}`
- **Backend listener**: `:5000` (PID 5732, owner unknown — not this
  session). Process left alone; diagnostics ran read-only against
  it.
- **Metrics endpoint**: `/metrics` → 404 (concealment-fail-closed —
  no `Metrics:ScrapeToken` configured). Standard production posture;
  not a diagnostic issue.

## Moderation flow map (Phase 1)

`SafetyFlag` enum (`backend/src/ArmenianAiToy.Domain/Enums/SafetyFlag.cs`):

| Value | Name      |
|-----:-|-----------|
| 0     | `Clean`   |
| 1     | `Flagged` |
| 2     | `Blocked` |

`SafetyFlag=2` = **`Blocked`**.

The sentinel constant lives at `ChatService.cs:1284`:

```
ModerationUnavailableFallbackResponse = "Մի րոպե սպասիր, ու նորից փորձենք։"
```

Reached **only** via the branch at `ChatService.cs:1352-1376`:

```
inputModeration = await _moderation.CheckContentAsync(userMessage);
if (!inputModeration.IsSafe)
{
    bool moderationUnavailable =
        inputModeration.FlaggedCategories.Contains("moderation_unavailable");
    ...
    var fallback = moderationUnavailable
        ? ModerationUnavailableFallbackResponse
        : _config["SafetyFallbackResponse"] ?? "Արի, մի ուրիշ հետաքրքիր բան խոսենք։";
    ...
    return new ChatResponse(fallback, ..., SafetyFlag.Blocked);
}
```

The `"moderation_unavailable"` flag is produced by
`OpenAIModerationAdapter.FailClosed` (`OpenAIModerationAdapter.cs:188`),
which is reached from five distinct catch arms in `CheckContentAsync`:

| Catch                              | `reason`                       | Triggered by |
|------------------------------------|--------------------------------|--------------|
| `ClientResultException` 429 + retry fail | `rate_limited_retry_failed`    | OpenAI returns 429 twice |
| `ClientResultException` 401/403          | `auth_error`                   | Invalid / revoked API key |
| `ClientResultException` other            | `server_error`                 | Any other OpenAI HTTP error |
| `OperationCanceledException`             | `timeout`                      | >10 s adapter timeout |
| Other `Exception`                        | `network_error`                | DNS / TLS / socket / parse |

The D1 retry path (lines 93-117): single retry after 400 ms constant
backoff, on 429 only. Other failure classes are never retried.

The exact reason is **only** emitted via the structured-log line in
`FailClosed`:

```
"Moderation unavailable. reason={Reason} status={Status} latency_ms={LatencyMs} retry_count={RetryCount} preview={Preview}"
```

No reason field is exposed on the HTTP response shape. This is by
design (the child-facing sentinel must not leak internal failure
mode), but it means a diagnosis needs either the backend stdout or
an out-of-band OpenAI probe.

## Reproduction (Phase 2)

A fresh device was registered, then two prompts were sent — one
English, one Armenian — to the SAME endpoint StoryInteractiveLoop
uses.

| Probe       | Prompt                                       | HTTP | Body shape |
|-------------|----------------------------------------------|-----:|------------|
| EN          | `tell me a story about a small hedgehog`     | 200  | `safetyFlag=2`, response=«Մի րոպե սպասիր...» |
| HY          | `Պատմիր հեքիաթ փոքրիկ ոզնիի մասին`            | 200  | `safetyFlag=2`, response=«Մի րոպե սպասիր...» |
| Timing      | (probe) `timing probe`                       | —    | 2.6 s end-to-end |

**Identical sentinel for both English and Armenian** confirms the
fail-closed path is not content-driven. The narrow violence-override
path (`ShouldOverrideViolenceBlock`) is not involved — that path only
runs when the OpenAI response itself comes back. Here the response
never comes back.

**Latency of 2.6 s** rules out `timeout` (10 s adapter ceiling).
Confirms a fast, deterministic failure response from OpenAI.

## Out-of-band OpenAI probe

A direct probe against `api.openai.com` from the same machine (the
key was read from `dotnet user-secrets` into a one-pipeline shell
variable, never printed to any tool transcript, and cleared
immediately):

```
POST https://api.openai.com/v1/moderations
  body: {"model":"omni-moderation-latest","input":"hello"}
  → HTTP 429
  → {"error":{"message":"Too Many Requests","type":"invalid_request_error","param":null,"code":null}}

POST https://api.openai.com/v1/moderations  (second call)
  body: {"model":"omni-moderation-latest","input":"second probe"}
  → HTTP 429 (persistent — not a one-off)

POST https://api.openai.com/v1/chat/completions
  body: {"model":"gpt-4o-mini","max_tokens":10,"messages":[{"role":"user","content":"hi"}]}
  → HTTP 429
  → {"error":{"message":"You exceeded your current quota, please
                check your plan and billing details. For more
                information on this error, read the docs:
                https://platform.openai.com/docs/guides/error-codes/api-errors.",
              "type":"insufficient_quota",
              "code":"insufficient_quota"}}
```

The chat endpoint's response is the smoking gun: **`insufficient_quota`**.

The moderation endpoint surfaces this as a generic 429 "Too Many
Requests" with no detail (OpenAI's moderation endpoint is less
verbose about billing). Same root cause; manifests as the same
adapter `FailClosed` branch (`rate_limited_retry_failed`) because
the retry obviously can't recover a quota-exhausted account.

## Conclusions

| Question                                 | Answer |
|------------------------------------------|--------|
| Is the moderation endpoint reachable?    | **Yes** — TCP / TLS / HTTP all succeed. |
| Is the API key valid (auth OK)?          | **Yes** — server responds with billing error, not auth error. |
| Is this a rate-limit window?             | **No** — `insufficient_quota` is a billing limit, not a per-minute throttle. Retrying later will keep returning 429 until billing is settled. |
| Is this an auth/key issue?               | No. |
| Is this a timeout?                       | No. End-to-end latency 2.6 s. |
| Is this a parse / network error?         | No. |
| Is this a content-flag block?            | No. Identical behavior on harmless English prompts. |
| Is this an Armenian-classification issue?| No. Same sentinel for `hello`. |
| Is the backend code wrong?               | **No.** The D1 retry + fail-closed contract is performing exactly as designed. |
| Is the StoryInteractiveLoop runner wrong?| **No.** It correctly detects `SafetyFlag != Clean` and stops sessions. |

**Strongest hypothesis = confirmed root cause**:
**OpenAI account quota / billing exhausted. `insufficient_quota`
returned by chat-completions; surfaced as generic 429 on
moderations.** Resolution lives with the OpenAI billing dashboard,
not in this codebase.

## What was NOT changed

- No source files were edited.
- No moderation behavior was altered.
- No fail-closed sentinel was relaxed.
- No tests were touched (`tools/StoryInteractiveLoop.Tests`: 65/65
  passing on the baseline commit `6af2a3d`).
- No new log channels were added — the existing `FailClosed.LogError`
  already emits the exact reason (`rate_limited_retry_failed`) needed
  for ops diagnosis. The gap is that the running backend's stdout
  was not accessible to this session; a future operator with stdout
  access will see the line immediately. No code change is needed
  to "fix the log"; the log already carries the right information.

## What WAS NOT retried

The StoryInteractiveLoop 5×4 was **NOT re-run** after diagnosis.
Every retry against a quota-exhausted account is a guaranteed
failed billed call — burning calls won't surface new signal.

## Recommended next step

1. **Operator action (out of scope for code)**: open the OpenAI
   billing dashboard and either:
   - Top up the prepaid balance, OR
   - Upgrade the plan / increase the usage cap, OR
   - Wait for the billing cycle to refresh and retry.
2. **After billing recovers**, re-run the validation that was
   blocked by this slice:
   ```
   dotnet run --project tools/StoryInteractiveLoop -- \
       --max-sessions 5 --max-turns 4 \
       --seed-id S01,S02,S03,S04,S05 --allow-larger-run
   ```
   Compare against the `20260524-151621` baseline to confirm the
   stemmer fix from commit `6af2a3d` actually drops the noun-warning
   false-positive count.
3. **Optional future code slice** (not now): expose the
   `FailClosed.reason` value as a structured metric tag on
   `aat_moderation_classify_duration_seconds` or as a separate
   counter `aat_moderation_failclosed_total{reason=...}`. This
   would make billing/quota exhaustion observable on Prometheus
   without requiring stdout access. The change is small, has the
   same no-high-cardinality discipline as existing AppMeter
   counters (`reason` is bounded to the 5 enum values above), and
   strictly augments observability — does not change child-safety
   behavior. Would have closed this slice's diagnosis in seconds
   instead of needing a direct OpenAI probe.
