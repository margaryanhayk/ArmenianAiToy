# AI landscape, September 2026 — what it means for Areg

Written 2026-09-23. Research pass over chat LLMs, speech (STT/TTS/realtime),
safety classifiers, vendor terms, regulation and the AI-toy market, read
against this repo's own evidence (`docs/latency-plan.md`,
`tools/quality-evidence/elevenlabs-realtime-armenian-20260903.md`,
`docs/business-readiness-2026-09-12.md`, `appsettings.json`).

**How much to trust this.** The container's egress proxy blocked almost every
vendor page (openai.com, ai.google.dev, elevenlabs.io, huggingface.co,
arxiv.org). Most facts below come from search-result snippets, each marked:

- **[V]** — read on the source page, or already measured in this repo
- **[S]** — search snippet only; confirm on the vendor page before acting

Nothing here was tested against a live API this session. No code changed.

---

## 1. Deadlines and risks (act on these first)

| # | Finding | Why it matters to Areg | Status |
|---|---|---|---|
| R1 | **OpenAI removes `gpt-4o-mini-transcribe`, `gpt-4o-transcribe` and `whisper-1` on 2027-02-26** (announced 2026-08-26). Replacements are `gpt-transcribe` (batch) and `gpt-live-transcribe` (streaming). | Every child question goes through `StoryQa:TranscriptionModel` / `OpenAI` STT. After that date it stops working. [deprecations](https://developers.openai.com/api/docs/deprecations), [llmlatency.dev](https://llmlatency.dev/migrate/openai-gpt-4o-mini-transcribe) | [S], corroborated by two sources |
| R2 | **Gemini API terms: users must be 18+, and a service "directed towards or likely to be accessed by individuals under 18" is not allowed.** | Production chat runs on Gemini (`AI__ChatProvider=gemini`). Areg is a toy for 4–7 year olds. The consumer Gemini API terms appear to forbid this. Vertex AI (Google Cloud terms) is the usual route for child-directed products; confirm with Google. [terms](https://ai.google.dev/gemini-api/terms) | [S] + matches the known clause; page blocked here |
| R3 | **ElevenLabs Prohibited Use Policy** forbids making its services available to under-13s or offering "bundled solutions that target anyone under 13", and forbids uploading voice data of anyone under 18. | Live TTS on `elevenlabs` and the planned `eleven_v3_conversational` switch are child-facing. Pre-rendered story audio is a grey area. Needs written permission or an enterprise agreement. Child voices must never go to Scribe. [use policy](https://elevenlabs.io/use-policy) | [S] |
| R4 | **California SB 867 (signed 2026-09-10): bans selling toys with a "companion chatbot" to minors, 2027-01-01 → 2031-01-01.** Filters and parental controls do not help; the only way out is to fall outside the definition ("human-like responses… meeting a user's social needs… anthropomorphic features… sustain a relationship across multiple interactions"). Stand-alone voice assistants that do not sustain a relationship are excluded. New York has a similar moratorium awaiting signature. | Areg's "play leader, not a friend" rule and the Absence Test are exactly the right design. They now need a written legal opinion, and every future feature (memory, name, "I missed you") must be checked against this definition. [Ballard Spahr](https://www.ballardspahr.com/insights/alerts-and-articles/2026/09/california-ai-toy-bill-brings-software-behavior-into-product-safety), [bill text](https://leginfo.legislature.ca.gov/faces/billTextClient.xhtml?bill_id=202520260SB867), [Toy Association](https://www.toyassociation.org/ta/PressRoom2/News/2026-News/ai-companion-chatbot-legislation-update.aspx) | [S], several sources agree |
| R5 | **California SB 1119 ("Adam's Law")**: child accounts default to limited memory, 1-hour sessions, 2 hours/day, crisis procedures; core rules from 2027-07-01. **SB 243** (in force 2026-01-01): AI disclosure and self-harm protocol for known minors. | Areg's daily cap and bounded modes are close already; a session-length limit and a documented crisis procedure are new. [gov.ca.gov](https://www.gov.ca.gov/2026/09/10/governor-newsom-signs-the-strongest-child-safety-chatbot-and-social-media-laws-in-the-nation/) | [S] |
| R6 | **COPPA amendments, compliance deadline 2026-04-22 (already passed):** voiceprints are personal information; sharing with third parties needs separate parental consent. OpenAI's under-18 API guidance reportedly requires **zero data retention (ZDR)** before processing an under-13's personal data. | A child's recorded question goes to OpenAI STT today. `docs/legal/parental-consent-draft.md` should gain this; apply to OpenAI for ZDR. [Federal Register](https://www.federalregister.gov/documents/2025/04/22/2025-05904/childrens-online-privacy-protection-rule), [OpenAI under-18 guidance](https://developers.openai.com/api/docs/guides/safety-checks/under-18-api-guidance) | [S] |
| R7 | **EU AI Act Art. 50** (tell users they are talking to AI) applies from 2026-08-02; high-risk obligations for AI in toys moved to 2028-08-02 by the Digital Omnibus. | Only matters when selling in the EU. The system prompt says "Do not say you are an AI" — that conflicts with Art. 50 for EU sales (and with SB 243 in California). Fix it through the parent/box disclosure or a spoken line. | [S] |
| R8 | **Gemini Flash price reportedly doubles on 2027-01-01** ($0.75/$3.75 → $1.50/$7.50 per 1M tokens for 3.6/3.7/3.8 Flash). | Update `AI:ChatCostPerMTokensIn/Out` so the daily cap stays honest. | [S], sources disagree |
| R9 | **`gpt-4o-2024-05-13` reportedly shuts down 2026-10-23.** Unclear whether the plain `gpt-4o` alias (the `OpenAI:ChatModel` fallback) is affected. | The OpenAI chat fallback may break in a month. | [S], unverified |

---

## 2. Opportunities, ranked by value ÷ risk

### O1. Cut the in-story question wait further (latency)

What exists: first-byte streaming on `story-qa` (latency-plan Part 5), TLS reuse,
POST-before-earcon. What the research adds:

1. **On-device end-of-speech detection with ESP-SR VADNet** (`vadnet1_medium`,
   part of Espressif's AFE, `vad_min_noise_ms` / `vad_min_speech_ms`). It
   replaces the fixed listening window, so the upload starts the moment the
   child stops talking. [ESP-SR docs](https://docs.espressif.com/projects/esp-sr/en/latest/esp32s3/vadnet/README.html) [S]
2. **Opus instead of raw PCM for the upload.** Today 16 kHz/16-bit PCM is
   32,000 B/s (latency-plan §1.3). Opus at ~16–24 kbps is **~10–15× smaller**,
   so a 3 s question is ~8 KB instead of 96 KB. This is how `xiaozhi-esp32`
   (MIT, ~30k stars, the same ESP32-S3) does it: one WebSocket, Opus both ways,
   60 ms frames, server-side VAD → ASR → LLM → streaming TTS.
   [xiaozhi-esp32](https://github.com/78/xiaozhi-esp32) [V]. Needs an STT
   listen/WER check on Opus-coded child Armenian.
3. **Stream the upload while the child speaks** (latency-plan Part 4 step 4).
   `gpt-live-transcribe` ($0.017/min) is OpenAI's streaming successor. Our own
   test showed ElevenLabs Scribe v2 *Realtime* is **not** usable in Armenian
   (invents words) [V, repo evidence] — the upload-while-recording, transcribe-
   once-at-release shape with a batch model stays the safe design.
4. **`eleven_v3_conversational`** — measured here: 0.7 s first byte vs 1.3 s,
   same clone [V]. Blocked on the owner's listen test **and on R3**.

### O2. Better Armenian speech recognition

Child Armenian STT is where the whole turn fails first. New evidence:

- **ArmBench-ASR** (Metric, 2026-08-20; 20.7 h, 5 domains): Gemini 2.5 Pro best
  at 14.3 % WER; closed systems hold the top 8 places.
  [runtimewire](https://runtimewire.com/article/metric-armbench-asr-armenian-speech-benchmark) [S]
- **Meta Omnilingual ASR** (open, Nov 2025): reported Armenian FLEURS CER
  ~3.9 % (7B) / ~4.9 % (1B). [repo](https://github.com/facebookresearch/omnilingual-asr) [S]
- **Fine-tuned Whisper large-v3-turbo Armenian** (HF community): 15.3 % WER on
  Common Voice 20. [S]
- **Azure Speech hy-AM** STT is supported, including fast transcription. [V]
- **VS.AM** (Armenian company): Eastern + Western STT/TTS, claims 96 %+ accuracy. [S]

**The strategic point:** a **self-hosted** STT (Omnilingual ASR or a fine-tuned
Whisper) means the child's voice never leaves Areg's own servers. That removes
the third-party voiceprint problem (R6), the ElevenLabs child-voice ban (R3),
and the R1 deprecation risk in one move. The Firebird/NVIDIA AI data centre in
Hrazdan (opened 2026-08-08) reportedly reserves capacity for Armenian startups.
[OC Media](https://oc-media.org/firebird-opens-regions-largest-ai-data-centre-in-armenia/) [S]

**Before choosing:** build a small child-Armenian WER set (20–50 real bench
recordings, parent consent, stored like the red-team corpus) and score
`gpt-transcribe`, Azure hy-AM, Omnilingual ASR and Scribe v2 batch on it.
`tools/elevenlabs-realtime/el_realtime_test.py` is the template.

### O3. The chat model

- **Armenian quality:** ArmBench-LLM 1.0 (Metric, April 2026) put **Gemini 3
  (Flash beat Pro) first at 0.635**, `gpt-5.2-pro` second at ~50× the cost;
  best open model Qwen3.5-27B (0.577).
  [HF blog](https://huggingface.co/blog/Metric-AI/armbench-llm) [S]. This
  confirms the 2026-08-06 bake-off choice — but see R2 for *how* Gemini is
  called.
- **Newer candidates to benchmark** (through `tools/StoryBenchmark` + the owner
  listen test, per the existing rule):
  - `gemini-3.8-flash` (2026-09-02) vs the current `gemini-3.6-flash` — via Vertex AI
  - OpenAI GPT-5.6 tiers (July 2026): Terra $2/$12, Luna $0.20/$1.20 — Luna is
    ~10× cheaper than `gpt-4o` ($2.50/$10) and a natural fallback replacement (R9)
  - Anthropic Sonnet 5 ($2/$10), Haiku 4.5 ($1/$5). No published Armenian score;
    test before trusting
  - **arm-gemma-e4b** (Gemma-4-E4B continued-pretrained on Armenian; weights,
    data and code released Sept 2026) — small enough to self-host; a candidate
    for the bounded jobs (`voice-intent`, reflection reactions)
- **Cost levers not yet used:** put the long static system prompt first and the
  per-turn content last so provider prompt caching applies (cached input ≈ 10 %
  of the price at OpenAI and Gemini); use batch APIs (50 % off) for offline
  content generation (story drafts, variant endings, reflection text).

### O4. Safety in Armenian — measure what we rely on

Moderation is fail-closed on OpenAI `omni-moderation-latest`, which OpenAI
tested on 40 languages — **Armenian is not confirmed to be one of them** [S].
Research also shows LLMs are much easier to jailbreak in low-resource languages
including Armenian. None of the newer open classifiers (Llama Guard 4,
ShieldGemma, gpt-oss-safeguard) list Armenian; Qwen3Guard (119 languages) might.

1. **Run the existing Armenian red-team corpus**
   (`tools/quality-evidence/armenian-red-team-safety-corpus-20260519.md`)
   **directly against the moderation endpoint** and record recall per category.
   That turns "unverified" into a number.
2. If recall is weak: add a policy-prompted LLM classifier (the chat model
   itself, or Qwen3Guard) as a **third** check — never as a replacement, and
   only with owner approval (moderation is a hard stop).
3. Do **not** adopt speech-to-speech realtime models (`gpt-realtime-2`, Gemini
   Live) for the child path: audio goes straight to the model, which bypasses
   the input-moderation-before-model ordering, and their Armenian is unproven.

### O5. Narration voice

- **Gemini-TTS lists hy-AM (Preview)** on Google Cloud — one more candidate for
  the narrator listen test (`docs/voice-decision-brief.md` §1 already flagged
  it). [S]
- ElevenLabs has **no new model after v3**; Armenian remains v3-only. A PVC on
  v3 is reported to work (1–3 h clean audio); whether it works in Armenian is
  unverified — check before the owner records. [S]
- Azure hy-AM voices are still standard neural only (no HD). [V]

### O6. Product and market

- **What wins:** structured, screen-free story content (Yoto, Toniebox) keeps
  selling; general "AI friend" chat toys failed — returns of 30–40 % and
  engagement collapse after day 14 in China; FoloToy/Kumma, Bondu (50,000
  transcripts exposed), Miko data exposure. Common Sense Media rated AI
  companion toys "Unacceptable" (Jan 2026), mainly for emotional-attachment
  design. [S]
- **Areg's positioning is the right answer to all of this.** Make it the
  headline: "a storyteller, not a friend; no companion features; stories work
  offline from the SD card; parents see everything."
- **Evidence for existing features:** a 117-child randomized trial found an
  AI agent asking questions during a story matched a human reading partner on
  comprehension (Xu et al., *Child Development* 2022) — this backs in-story
  Q&A and after-story reflection.
- **Diaspora heritage-language market:** parents want to keep the language but
  lack exposure and time. AGBU (Armenian Virtual College, "Gus on the Go")
  is a natural distribution partner; Western Armenian stays a v2 item
  (`docs/v2-backlog.md`).

---

## 3. What not to change

- The dual-moderation, fail-closed chain and its ordering.
- The SD-card-first story library (costs nothing per play; the most defensible
  part of the product under every new law).
- The rule that no provider/model switch reaches children without a benchmark
  run and the owner listen test.
