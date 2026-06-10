# TtsVoiceBakeoff

Manual voice/model bakeoff for Armenian child-story narration.
Production `tts-1` + Nova renders Armenian acceptably but sounds too
robotic for a children's toy — this tool compares warmer candidates
before any production change.

**Exploration tool, NOT production parity.** Production-parity
acceptance renders belong exclusively to `tools/TtsListenTest`. This
tool uses raw HTTP to `/v1/audio/speech` so the gpt-4o-mini-tts
`instructions` parameter can be sent (the repo's pinned OpenAI SDK
does not expose it). No NuGet changes, no production code touched.

## Candidate matrix (8 combos × 3 strings = 24 renders)

| Model | Voice | Instructions |
|---|---|---|
| gpt-4o-mini-tts | coral | yes |
| gpt-4o-mini-tts | shimmer | yes |
| gpt-4o-mini-tts | sage | yes |
| gpt-4o-mini-tts | marin | yes (may be rejected — realtime voice id; a rejection is a recorded result) |
| gpt-4o-mini-tts | cedar | yes (same caveat) |
| tts-1-hd | shimmer | no |
| tts-1-hd | fable | no |
| tts-1-hd | nova | no (control — isolates tts-1-vs-Nova as the robotic culprit) |

Instructions (gpt-4o-mini-tts only, one constant for the whole round):
*"Speak in a warm, gentle Armenian children's storyteller tone.
Natural, kind, soft, not robotic. Moderate speed. Clear pronunciation.
Calm bedtime-friendly energy."*

## Sample strings

Read directly from `InMemoryCuratedStoryLibrary` (byte-identical,
never hand-copied): `little-cloud--segment-2` («ամպի՛կ», «ասաց՝»),
`hedgehog-apple--segment-1` («գլորեցին»/«գլորվեց»),
`little-cloud--question-0` («ո՞ւմ» intonation).

## Usage

```bash
# Safe dry-run (no API key read, no network, no files):
dotnet run --project tools/TtsVoiceBakeoff

# Render the matrix (PAID OpenAI API — both flags required, ~$0.05-0.10):
dotnet run --project tools/TtsVoiceBakeoff -- --render --confirm-paid-api

# Custom output dir (default: %TEMP%/areg-tts-voice-bakeoff):
dotnet run --project tools/TtsVoiceBakeoff -- --render --confirm-paid-api --output D:\bakeoff
```

Output files: `{model}--{voice}--{story}--{label}.mp3` plus a
`manifest.md` with empty rating columns (Warmth, Naturalness, Armenian
pronunciation, Child-friendly, Roboticness lower=better, Final
verdict) and a failed-combos section. Per-combo API failures are
recorded and never abort the run.

## API key

Same resolution as TtsListenTest — `OPENAI_API_KEY` / `OpenAI__ApiKey`
env var, else the backend Api user-secrets store. Never printed, never
written to disk.

## Safety rules

- NOT in CI, NOT in the solution, no runtime wiring, no DI changes,
  no production TTS changes.
- Dry-run by default; render gated behind `--render --confirm-paid-api`.
- Output outside the repo; never commit MP3s.
- After a winner is chosen, production adoption is a SEPARATE reviewed
  slice (likely: OpenAI NuGet upgrade for instructions support +
  OpenAITtsSynthesisService update + full TtsListenTest re-validation
  of every library string on the winning voice).
