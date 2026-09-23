# AI improvements roadmap — zero to hero (2026-09-23)

Every improvement from `docs/ai-landscape-2026-09.md`, put in the order it has
to happen. Each row says **who** can do it: **Owner** (a decision, an account,
a contract, a listen test, hardware), **Claude** (code/docs, can run
unattended), or **Claude after approval** (a `CLAUDE.md` hard stop: ChatService,
system prompt, moderation, auth, provider/NuGet, or a child-facing model switch
— needs a plan and the owner's "go").

Deploy path: a PR into `main` → owner says "merge" → Railway redeploys `main`.
Firmware cannot be flashed from the cloud; the owner cable-flashes or stages OTA.

---

## Phase 0 — done in this PR

| Item | What changed | Verified |
|---|---|---|
| Research report | `docs/ai-landscape-2026-09.md` | sources tagged [V]/[S] |
| Legal/vendor map | `docs/legal/vendor-terms-and-ai-toy-laws-2026-09.md` — vendor terms vs. data sent, SB 867 element-by-element, dated rule table, three ready-to-send vendor messages | not read by a lawyer |
| STT retirement warning | `ModelRetirementCatalog` + boot warning in `Program.cs`: each of the 3 STT config keys that resolves to a model OpenAI removes on 2027-02-26 logs a warning naming the replacement | unit tests; booted in Production — all 3 keys warn (`OpenAI:TranscriptionModel` and `Devices:VoiceIntentTranscriptionModel` resolve to **whisper-1**, `StoryQa:TranscriptionModel` to `gpt-4o-mini-transcribe`) |
| Gemini cost-cap rate | default chat rate for Gemini $0.50/$3.00 → **$0.75/$3.75** per 1M tokens (was below list, so the daily cap fired late) | build + tests; price itself is [S] |
| Moderation recall tool | `tools/safety/moderation_recall.py` — runs the 55-case Armenian red-team corpus against OpenAI moderation, recall per language/category | offline tests + dry-run; **not run live (no `OPENAI_API_KEY` here)** |
| STT benchmark tool | `tools/stt-bench/stt_wer_bench.py` — WER/CER/latency for `openai:<model>`, Azure hy-AM, ElevenLabs (adult voices only, enforced), or any self-hosted command | offline tests + dry-run; not run live |

---

## Phase 1 — Owner, this week (no code)

1. **Google:** send message 4a (`vendor-terms…md`) — Gemini's API terms exclude
   under-18 services. Decide: Vertex AI, or back to OpenAI chat.
2. **ElevenLabs:** send message 4b. Until answered, keep live TTS on OpenAI.
3. **OpenAI:** send message 4c (zero data retention).
4. **Lawyer:** one opinion on SB 867 (§ 2 of the legal file) and the AI-disclosure
   route (SB 243 / EU Art. 50).
5. **Railway:** check whether `OpenAI__TranscriptionModel` is set. If not,
   `/api/chat/audio` and voice-intent run on **whisper-1** (slower, removed
   2027-02-26). Setting it to `gpt-4o-mini-transcribe` is the already-approved
   fast model (`a5dfcf5`).
6. **Give the next session an `OPENAI_API_KEY`** (environment secret) so Phase 2
   can run.
7. **Record 20–50 short Armenian questions** (adult voices first; children only
   with parental consent), each with a `.txt` transcript — kept out of git.

## Phase 2 — Measure (Claude, once Phase 1.6/1.7 exist)

| # | Run | Decides |
|---|---|---|
| 2.1 | `tools/safety/moderation_recall.py` | Is omni-moderation's Armenian recall good enough, or is Phase 3.5 needed |
| 2.2 | `tools/stt-bench` on `gpt-4o-mini-transcribe` vs `gpt-transcribe` vs Azure hy-AM (+ a self-hosted model later) | The STT replacement (Phase 3.1) |
| 2.3 | `tools/StoryBenchmark` + `ModeBenchmark` on candidate chat models: `gemini-3.8-flash`, GPT-5.6 Luna/Terra, Sonnet 5 / Haiku 4.5 | The chat model after Phase 3.2 |

## Phase 3 — Switch (Claude after approval; before the dates)

| # | Change | Deadline | Hard stop |
|---|---|---|---|
| 3.1 | Move all 3 STT keys to the Phase 2.2 winner (config only if OpenAI) + Armenian listen test | **2027-02-26** | model switch |
| 3.2 | Chat: Vertex AI adapter (if Google says yes) or OpenAI model from 2.3 | before first sale | provider / auth |
| 3.3 | AI-disclosure route per the lawyer (box/onboarding copy, or one spoken line) | SB 243 now; EU 2026-08-02 | system prompt if spoken |
| 3.4 | Prompt-caching order: static system prompt first, per-turn context last | — | ChatService |
| 3.5 | If 2.1 recall is weak: add a policy-prompted third safety check (never replacing the existing two) | — | moderation |
| 3.6 | Session-length / daily-time limit per child account (SB 1119) | 2027-07-01 | new behaviour + dashboard |
| 3.7 | Raise the Gemini cost default again if the 2027-01-01 doubling is confirmed | 2027-01-01 | none (config) |

## Phase 4 — Faster answers (firmware; Claude writes + compile-verifies, Owner flashes and listens)

| # | Change | Expected gain |
|---|---|---|
| 4.1 | ESP-SR VADNet end-of-speech detection instead of the fixed listening window (Arduino core 3.x ships `ESP_SR` for S3) | upload starts the moment the child stops |
| 4.2 | Opus-encode the question upload (~10–15× smaller than 16 kHz PCM). Backend needs an Opus decoder — a NuGet package, so approval | ~1–3 s on weak Wi-Fi |
| 4.3 | Stream the upload while the child speaks; transcribe once at release (latency-plan Part 4 step 4) | 2–4 s |
| 4.4 | `eleven_v3_conversational` live (0.7 s vs 1.3 s first byte, measured) — only after Phase 1.2 answer + listen test | ~0.6 s |

## Phase 5 — Own the voice pipeline (strategic)

| # | Change | Why |
|---|---|---|
| 5.1 | Self-hosted Armenian STT (Meta Omnilingual ASR or a fine-tuned Whisper large-v3-turbo) behind the existing `AI:TranscriptionProvider` seam — scored first with `tools/stt-bench --provider cmd:…` | Child voice never leaves Areg's servers: solves COPPA voiceprint sharing, the ElevenLabs child-voice ban and future deprecations at once |
| 5.2 | GPU host: ask the Firebird/NVIDIA data centre (Hrazdan) about capacity reserved for Armenian startups | cheap local inference |
| 5.3 | `arm-gemma-e4b` (open Armenian Gemma) for bounded jobs — voice-intent, reflection reactions | cost + no third party |

## Phase 6 — Product and market (Owner)

- Lead marketing with the non-companion design: "a storyteller, not a friend;
  stories work offline; parents see everything" — it is the answer to every
  2025–26 AI-toy scandal and to SB 867.
- Cite the evidence for in-story questions (Xu et al., *Child Development* 2022).
- Diaspora channel: AGBU (Armenian Virtual College); Western Armenian stays v2.
- Narrator: add Gemini-TTS hy-AM (Preview) to the next narrator listen test;
  confirm PVC works in Armenian on v3 before recording.

## Not doing

- Speech-to-speech realtime models (`gpt-realtime-2`, Gemini Live) on the child
  path: they skip input moderation before the model, and their Armenian is unproven.
- ElevenLabs Scribe v2 Realtime: tested 2026-09-03, invents words in Armenian.
