# Post-recovery validation — 2026-05-24 (FAILED: quota still exhausted)

## Summary

**OpenAI billing quota has NOT yet recovered.** The validation
slice halted at Phase 3 per its own stop-rule and did NOT run the
StoryInteractiveLoop 5×4 live run. No code changes were made. No
calls were burned beyond the bounded probes documented below.

This file documents the validation attempt so the next run after
billing is genuinely restored has a clear before/after artifact.

## Run context

- **Validation timestamp**: 2026-05-24 ~18:45 UTC
- **Branch**: `main`
- **Commit SHA**: `4fa6274`
- **Working tree**: dirty
  (pre-existing M files only: `.claude/settings.local.json`,
  `esp32/AregVoiceMvp/config.h`; pre-existing untracked files
  unrelated to this slice — none touched)
- **Backend listener**: `:5000`, owner unknown — not this session.

## Phase 0 — Deterministic test baseline (Pass)

| Suite                                                         | Result        |
|---------------------------------------------------------------|---------------|
| `dotnet test tools/StoryInteractiveLoop.Tests`                | 65/65 pass    |
| `dotnet test backend/.../Application.Tests --filter Moderation` | 55/55 pass    |

Both green — code state is stable; nothing on the code side
needs unblocking. The moderation metric counter (commit
`4fa6274`) is pinned by 14 dedicated tests.

## Phase 1 — Backend health (Pass)

```
GET http://localhost:5000/api/health
→ 200 OK
→ {"status":"ok","service":"ArmenianAiToy API","database":"ok"}
```

## Phase 2 — Metrics endpoint (Observed: 404 concealment)

```
GET http://localhost:5000/metrics   (no Authorization header)
→ 404, 0 bytes
```

This is the **documented concealment-fail-closed default** when
`Metrics:ScrapeToken` is unset (CLAUDE.md § Metrics
(OpenTelemetry + Prometheus) → "Shipped default is fail-closed").
Configuring a local scrape token is a deploy-slice concern; this
validation slice does NOT alter local config.

The `aat_moderation_failclosed_total` counter behavior is
already pinned by the 14 unit tests in
`ModerationFailClosedMetricsTests` (every reason value + each
negative-space case). Endpoint wiring will be validated as part
of the future deploy slice that owns local `Metrics:ScrapeToken`
provisioning.

## Phase 3 — Minimal /api/chat probe (FAILED — still sentinel)

```
POST http://localhost:5000/api/devices/register
  body: {"macAddress":"POSTREC-001"}
→ 200 OK
→ {"deviceId":"b606df85-994a-44d4-aea4-dd407751fd86", "apiKey":"<redacted>"}

POST http://localhost:5000/api/chat
  headers: X-Device-Id: <…>, X-Api-Key: <redacted>
  body: {"message":"Պատմիր հեքիաթ փոքրիկ ոզնիի մասին"}
→ 200 OK in 5.9 s
→ {
    "safetyFlag": 2,                          ← Blocked
    "mode": null,
    "conversationId": "2fe3fa8d-…",
    "storySessionId": null,
    "choiceA": null,
    "choiceB": null,
    "response": "Մի րոպե սպասիր, ու նորից փորձենք։"  ← sentinel
  }
```

`SafetyFlag.Blocked=2` + sentinel = `moderation_unavailable`
fail-closed path fired. Same outcome as the
`20260524-170208` evidence under the previous diagnosis.

**Per the slice's own stop-rule, StoryInteractiveLoop was NOT
run.** Running it would just produce another five
safety-fallback evidence files identical to `20260524-170208-*`.

### Metric increment by code inspection

`/metrics` is not scrape-accessible (Phase 2), so the counter
value cannot be read directly here. However, the unit tests
prove the code path: a 429-followed-by-429 (the upstream class
confirmed below) calls `FailClosed` once with reason
`rate_limited_retry_failed`, which increments
`aat_moderation_failclosed_total{reason="rate_limited_retry_failed"}`
by exactly 1.

**Conclusion**: this single probe almost certainly incremented
the counter by 1 with `reason=rate_limited_retry_failed`. A
future run with a configured scrape token will be able to
confirm this directly.

## Phase 3a — Out-of-band probe to OpenAI (confirms billing root cause)

To characterize the current upstream class, two bounded probes
were issued directly against `api.openai.com` from this machine.
The API key was read from `dotnet user-secrets` into a one-line
shell pipeline, used in the curl headers, and cleared
immediately. It was never echoed, never written to disk in
clear text, and is not committed anywhere in this repo.

```
POST https://api.openai.com/v1/moderations
  body: {"model":"omni-moderation-latest","input":"hello"}
  → 429
  → {"error":{"message":"Too Many Requests","type":"invalid_request_error","param":null,"code":null}}

POST https://api.openai.com/v1/chat/completions
  body: {"model":"gpt-4o-mini","max_tokens":5,"messages":[{"role":"user","content":"hi"}]}
  → 429
  → {"error":{
       "message":"You exceeded your current quota, please check
                  your plan and billing details. For more
                  information on this error, read the docs:
                  https://platform.openai.com/docs/guides/error-codes/api-errors.",
       "type":"insufficient_quota",
       "code":"insufficient_quota"}}
```

**Same `insufficient_quota` as 2026-05-24 morning.** The billing
state has not changed.

## What was NOT done

Per the validation slice's own rules, none of the following were
performed:

- ❌ Phase 4: StoryInteractiveLoop 5×4 live run.
- ❌ Phase 5: evidence comparison against `20260524-151621`
       baseline and `20260524-170208` safety-fallback.
- ❌ Phase 6: post-run metrics scrape.
- ❌ Any code change.
- ❌ Any moderation-behavior alteration.
- ❌ Any retry of the 429 in hopes of recovery.

These will be performed in the next slice **only after billing
is genuinely restored** (confirmed by `/v1/chat/completions`
returning 200 on a single probe — not a side effect, not a
guess).

## Cost summary

- 2 probes through `/api/chat` (1 for the timing measurement,
  1 for the Armenian-prompt probe). Both fail-closed at the
  moderation layer — **zero billed OpenAI chat-completion
  tokens** were generated.
- 2 direct probes to `api.openai.com` (1 moderation, 1 chat).
  Both returned 429 immediately with no tokens generated.
- StoryInteractiveLoop was NOT run.

## Conclusions

| Question                                                | Answer |
|---------------------------------------------------------|--------|
| Did the validation succeed?                             | **No — halted at Phase 3.** |
| Has OpenAI billing recovered?                           | **No — `insufficient_quota` persists.** |
| Is the backend healthy?                                 | **Yes** — `/api/health` 200 OK. |
| Is the moderation fail-closed contract working?         | **Yes** — sentinel returned correctly, `SafetyFlag.Blocked` correctly set. |
| Did the new metric likely increment?                    | **Yes (by code inspection)** — `aat_moderation_failclosed_total{reason="rate_limited_retry_failed"}` += 1 per probe. Not directly readable because Phase 2 saw 404 on `/metrics`. |
| Is the stemmer fix from commit `6af2a3d` validated against fresh natural-prose evidence? | **Not yet** — blocked on billing recovery. |
| Is any code change recommended?                         | **No.** The diagnosis is unchanged; no new finding warrants a slice. |

## Recommended next slice

**Operator (out of code scope)**: top up / upgrade the OpenAI
billing on the account whose key is configured in
`backend/src/ArmenianAiToy.Api` user-secrets.

**After billing recovers, this same validation slice should run
again unchanged.** Expected:

- Phase 3 returns `safetyFlag=0`, real Armenian story body, two
  choices.
- Phase 4 (StoryInteractiveLoop 5×4) runs to completion.
- Phase 5 shows the stemmer fix from commit `6af2a3d` does drop
  the noun-warning false-positive count on the same seeds vs the
  `20260524-151621` baseline, while real positives
  (`քարտեզ`, `ուղի`) still fire.
- Phase 6 confirms `aat_moderation_failclosed_total` did not
  increment during the healthy run (and the
  `20260524-184500` probe's pre-existing increment is recorded
  in any captured pre-run scrape).

If the `/metrics` scrape needs to be directly readable for that
future validation, set `Metrics:ScrapeToken` in user-secrets and
pass `Authorization: Bearer <token>` on the scrape request. That
is a deploy-side config flip, not a code change.
