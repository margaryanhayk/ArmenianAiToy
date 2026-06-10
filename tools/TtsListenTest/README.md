# TtsListenTest

Manual listen-test tool for the curated Library Story texts
(MODES.md §1A makes a production-voice listen test a mandatory
acceptance step for every library story).

## What it does

- Reads every curated story **directly from
  `InMemoryCuratedStoryLibrary`** — byte-identity with what the toy
  would speak is automatic, no hand-copied strings.
- Enumerates every child-facing string: each segment, the reflection
  sentence, each reflection question (titles only with
  `--include-titles` — no current contract speaks titles).
- **Default mode is a safe dry-run**: prints story ids/titles, string
  labels, character counts, the per-string listen-for watchpoints, and
  a manifest preview. No network call, no files written.
- Render mode reuses the **production `OpenAITtsSynthesisService`**
  (model from `OpenAI:TtsModel`, default `tts-1`; voice **Nova**; MP3) —
  voice/model/format parity by construction, then writes one MP3 per
  string plus a `manifest.md` listing file / story / label / chars /
  watchpoints.

## Usage

```bash
# Safe dry-run (no API call, no files):
dotnet run --project tools/TtsListenTest

# Render MP3s (PAID OpenAI API — both flags required):
dotnet run --project tools/TtsListenTest -- --render --confirm-paid-api

# Custom output dir (default: %TEMP%/areg-tts-listen-test):
dotnet run --project tools/TtsListenTest -- --render --confirm-paid-api --output D:\listen-test
```

`--render` without `--confirm-paid-api` fails fast before any client
construction. A full render of the current 10 strings is roughly
~800 characters ≈ $0.01 at tts-1 pricing.

## API key

Resolution order — never printed, never written to disk:
1. `OPENAI_API_KEY` / `OpenAI__ApiKey` environment variable
2. The backend Api project's user-secrets store (`OpenAI:ApiKey`) —
   the same place `dotnet user-secrets set` already provisioned for
   running the API. No second copy of the key is created.

## Safety rules

- NOT in CI, NOT in the solution file, NOT registered in runtime DI,
  no endpoint — manual invocation only.
- Output goes outside the repo by default (`%TEMP%`); never commit
  MP3s.
- The listening itself is the acceptance gate: a human listens to
  every file against the watchpoints in the manifest (mid-word «՛»,
  «՞» digraph placement, «մաղեց»/«գլորվեց» stress, «և» mid-word,
  ն-article liaison, «՝» pause, comma beat, «։» finality) and records
  verdicts as a markdown note under `tools/quality-evidence/`.
- A word the voice mangles gets **rewritten** through a fresh
  armenian-story-master review — never phonetically hacked, and the
  byte-pinned story text never changes without that review.
