# Spoken-answer latency: where the ~16 s goes, and what to do about it

Written 2026-08-10, after the owner measured a ~16 s wait between a child
finishing their question and hearing Areg answer, with `POST /api/chat`
(text in, text out, from a PC) measured at 3.4–4.6 s with 0.1 s connect.

Every number below is labelled **measured** or **inferred**. The measured
ones come from this repo's own instrumentation and bench runs, not from
estimates.

---

## Part 0 — which code path is actually being timed

This matters, because two different voice endpoints exist and they are not
equally live.

A press while a story is playing is a **barge-in question** and goes to
`POST /api/chat/story-qa` (`AregVoiceMvp.ino:1531` → `voice_client.cpp:854-884`).
A press at idle does **not** ask a question at all — it starts or resumes a
story (`AregVoiceMvp.ino:2355`).

`POST /api/chat/audio` is reached only from the boot welcome flow, when the
spoken menu resolves to game / riddle / curiosity (`AregVoiceMvp.ino:1065-1070`).
The classic "record → POST `/api/chat/audio` → play" turn,
`handle_record_upload_playback()` (`AregVoiceMvp.ino:255-420`), is **defined
and never called** in the current sketch — including its own
`[latency] release->play_begin_ms` print.

**So "the child asks a question and waits" is the `story-qa` path.** The rest
of this document is anchored there; the `/api/chat/audio` figures are noted
where they differ.

---

## Part 1 — the breakdown

### 1.1 Backend stages — MEASURED

`StoryQaController.Ask` has had per-stage `Stopwatch` instrumentation since
commit `a73b443`: one structured log line per turn
(`"Story-QA timing ms: stt=… inMod=… gpt=… outMod=… tts=… total=…"`) plus
`X-Qa-*-Ms` response headers in Development. Two bench runs of 8 real
Armenian turns each are recorded in the commit messages:

| Stage | Before (`a73b443`) | After (`a5dfcf5`) | What it is |
|---|---:|---:|---|
| STT (Whisper) | **1900 ms** | 886 ms | `whisper-1` → `gpt-4o-mini-transcribe` |
| Input moderation | **580 ms** | ~580 ms | unchanged, and must stay serial |
| GPT answer | **1400 ms** | 903 ms | bounded `LibraryStoryQuestionService` |
| Output moderation | **490 ms** | ~300 ms, hidden under TTS | now overlapped with speculative TTS |
| TTS | **4250 ms** | 1322 ms | `tts-1` → `gpt-4o-mini-tts`, and shorter answers |
| **Backend total** | **8650 ms** | **3676 ms** | |
| PC client total | 10700 ms | 3945 ms | measured from a PC, not the toy |

Two things follow directly:

- **TTS is the single largest backend stage**, and it is driven by how many
  characters are handed to it.
- The 8650 → 3676 ms win was **entirely configuration** (two model names)
  plus one safe overlap. No new mechanism was invented.

### 1.2 The configuration finding — MEASURED, and the largest single item

The models that produced the 3676 ms number are set in exactly one place in
this repo:

```
backend/run-local.ps1:32   $env:StoryQa__TranscriptionModel = "gpt-4o-mini-transcribe"
backend/run-local.ps1:33   $env:OpenAI__TtsModel            = "gpt-4o-mini-tts"
```

That is the **local bench launcher**. Neither key appears in
`appsettings.json`, in `appsettings.Development.json`, in `railway.json`, or
in the `Dockerfile`. The shipped defaults are `whisper-1`
(`DependencyInjection.cs:116`) and `tts-1` (`:117`).

**Unless those two variables are set by hand in the Railway dashboard, the
deployed backend is running the 8650 ms configuration, not the 3676 ms one.**
This cannot be verified from the repo — the Railway dashboard is the source
of truth — but it is the first thing to check, and on its own it accounts for
roughly **5 s** of the 16.

### 1.3 Device-side stages — INFERRED (with measured constants)

There is no device instrumentation splitting record / upload / server /
download / decode. The only live timing print is
`[latency] qa_release->play_begin_ms` (`AregVoiceMvp.ino:1599-1601`), and its
anchor is taken at `:1529` — **after** the 600 ms earcon — so it already
understates the child-perceived gap.

| Stage | Estimate | Basis |
|---|---:|---|
| Blocking "thinking" earcon before the POST | **600 ms** | measured constant `AREG_EARCON_DURATION_MS` (`config.h:145`), blocking synth at `.ino:1491`, POST only fires at `.ino:1531` |
| TLS handshake | **~300–800 ms** | inferred. `~HTTPClient()` calls `_client->stop()` unconditionally (`HTTPClient.cpp:88-91`), so the shared `WiFiClientSecure` (`net_transport.cpp:51-55`) is torn down after every call and each request re-handshakes |
| WAV upload | **~1–3 s** | 16 kHz/16-bit/mono PCM = **32 000 B/s** (`config.h:101-102`); a 3 s question is 96 044 B, the 15 s cap is 480 044 B. Store-and-forward: the whole buffer is `memcpy`'d into PSRAM and POSTed with a `Content-Length` (`.ino:1465-1472`, `voice_client.cpp:884`). No compression anywhere |
| Backend | **3.7 s or 8.7 s** | measured, §1.1 — depending on §1.2 |
| Response download | **~0.5–1.5 s** | inferred. Body = answer + 1.2 s silence + bridge (+ recap), typically ~30–60 KB |
| Wait for the earcon pulse boundary | **0–600 ms** | the completion poll only happens between pulses (`.ino:1547-1563`) |
| Decoder start | **~100 ms** + `DIAG_MARK` overhead | `audio_play_mp3_buffer` from a resident buffer (`audio_io.cpp:292-317`); `DIAG_MARK` is not compile-gated and each mark does `Serial.flush(); delay(5);` (`diag.cpp:69-77`) — ~75 ms/turn |

Adding the slow-config case: 0.6 + 0.5 + 2 + 8.7 + 1 + 0.6 + 0.2 ≈ **13.6 s**,
and a longer question or a weaker Wi-Fi link closes the gap to the observed
16 s. The breakdown is consistent with what the owner measured.

### 1.4 The structural fact that constrains every fix

**The toy cannot start playing until the whole response has arrived, and it
rejects any response without a `Content-Length`.**

```
voice_client.cpp:543   const int body_len = http.getSize();
voice_client.cpp:544   if (body_len <= 0) {
voice_client.cpp:545       Serial.printf("[voice] http: unexpected body length %d\n", body_len);
voice_client.cpp:546       return false;
```

`getSize()` is `-1` for a chunked response, so a chunked answer **fails the
turn outright** and the child hears the canned failure clip. This is not a
theory: backend chunked streaming was shipped (`fdc4b66`) and then reverted
after hardware bring-up (`96d6084`) for exactly this reason.

The streaming player that would fix it, `audio_play_qa_stream()`
(`audio_io.cpp:754-805`), **is written and never called** — the sketch plays
the buffered POST body instead (`.ino:1646-1648`), with the intended fix
recorded as a comment at `.ino:1640-1645`.

So: *any* backend change that shortens time-to-first-audio by streaming is
blocked until a firmware slice lands. Backend-only work can only reduce
**total** wall clock.

### 1.5 What must stay serial

Input moderation → GPT is a safety ordering, not a latency choice: an unsafe
transcript is never sent to the answer model. It stays serial.

Output moderation is **already overlapped** correctly (`a5dfcf5`, and
`StoryQaController.cs:404-436`): the answer TTS is started speculatively while
the classifier runs, and if moderation blocks, the speculative audio is
**discarded** and the canned fallback is spoken. Unmoderated audio is never
returned. Nothing further should be taken out of that path.

---

## Part 2 — levers, ranked by seconds saved ÷ risk

| # | Lever | Est. saving | Risk | Owner? |
|---|---|---:|---|---|
| 1 | **Verify/set the two fast-model env vars on Railway** (`OpenAI__TtsModel=gpt-4o-mini-tts`, `StoryQa__TranscriptionModel=gpt-4o-mini-transcribe`) | **~5.0 s** (measured 8650→3676 ms) | Low mechanically; child-facing, so it needs the Armenian listen test. Both were already reviewed and approved once (`a5dfcf5`) | **Owner/operator** — config only, no code |
| 2 | **Stream TTS to the device, play on first bytes** | ~2–4 s | High — needs firmware. Backend chunking alone **breaks the turn** (§1.4). Requires calling the already-written `audio_play_qa_stream()` and removing the `getSize()` gate | Firmware slice |
| 3 | **Kill the ~1.2 s of blocking earcon padding** (fire the POST before/while the earcon plays; poll completion inside the pulse) | ~0.6–1.2 s | Low-medium, firmware-only | Firmware slice |
| 4 | **Reuse the TLS connection** (`~HTTPClient()` closes the shared client) | ~0.3–0.8 s/turn | Medium, firmware-only | Firmware slice |
| 5 | **Parallel sentence-chunked TTS** — render a long reply's sentences concurrently and concatenate | ~1–3 s on long replies; **no effect below 260 chars** | Low. Backend-only, no firmware dependency, text unchanged | **Implemented here, flag off** |
| 6 | **Compress or downsample the upload** (16 kHz PCM → 8 kHz, or any codec) | ~0.5–1.5 s | Medium — firmware + STT accuracy on child Armenian; the bias prompt already props up short answers | Firmware slice |
| 7 | **Trim what the device must download before it plays** (`StoryQa:AnswerBridgePauseMs`, recap gating) | ~0.2–0.5 s | Low, but degrades a reviewed UX; recap is already effectively off at 130 chars | Config |
| 8 | **Host near OpenAI** | ~0.1–0.2 s × 4–5 sequential calls | — | **Owner decision**, not code |
| 9 | Compile-gate `DIAG_MARK` (~75 ms of `delay()` per turn) | ~0.075 s | Very low, firmware-only | Firmware slice |

**Explicitly rejected: taking output moderation off the critical path.** It is
already overlapped as far as it safely can be, and the fail-closed contract
(unsafe → canned fallback, speculative audio discarded) is child-safety
critical. It must remain, and it must remain gating what is *returned*.

Note that levers 2, 3, 4, 6 and 9 are all firmware, and together they are
worth more than everything available on the backend. **The next real latency
work is a firmware slice**, and the single most valuable item in it is calling
the streaming player that is already written.

---

## Part 3 — what was implemented

**Lever 5: parallel sentence-chunked TTS, behind `Audio:ParallelTts:Enabled`,
defaulting to `false` (today's exact behaviour).**

It was chosen because it is the only item on the list that attacks the largest
backend stage (§1.1) without touching firmware, moderation, models, prompts,
or any child-facing text.

### How it works

`SpeechChunker` (`Application/Audio/SpeechChunker.cs`) splits a reply into
whole-sentence chunks; `ParallelChunkedSynthesisService`
(`Application/Audio/ParallelChunkedSynthesisService.cs`) renders them
concurrently through the real provider and concatenates the audio in order.
It is registered in `AddInfrastructure` as a **decorator** over whichever TTS
provider is configured, so every caller benefits with no controller change.

Design decisions worth keeping:

- **Armenian punctuation.** «՞» and «՜» are placed inside the stressed vowel
  of a word, *not* at the end of a sentence, so they are **not** boundaries.
  The Armenian full stop «։» is, as are ASCII `.` `!` `?` and `…`.
- **Never cuts inside a sentence.** A single long sentence stays whole even if
  it exceeds the target size; a reply with no sentence boundary is a single
  call.
- **Short replies take the current path exactly.** Below `MinTextChars` (260)
  it is one call with the original string — so most in-story Q&A answers,
  which are 1–2 sentences by prompt rule, are unaffected.
- **Failure posture is no worse than today.** Splitting multiplies the chance
  that one request blips, so any chunk failure falls back to a single
  full-text render; the caller's sanitized-failure handling only sees an
  exception if that fallback fails too.
- **A streaming-capable provider is never wrapped.** `AudioChatController`
  feature-detects `IStreamingAudioSynthesisService` (ElevenLabs) to start the
  device at first byte; wrapping would hide that capability and make the toy
  slower.
- **One ID3 tag, not one per chunk.** Trailing chunks have their ID3v2 header
  stripped. The concatenated file still carries the first chunk's duration
  header — the same property the shipped answer+bridge+recap composition
  already has, harmless on the toy's decoder, worth knowing for the parent
  dashboard's browser replay.

### Honest limits

- Saves **nothing** on a short reply, which is most in-story Q&A turns today.
  Its value is on the long story-mode replies (`/api/chat/audio`,
  3–5 sentences plus the spoken choice bridge) and any future long spoken
  answer.
- Chunk seams are a **listening** question, exactly as they were for the
  `eleven_v3` narration renders. The flag must not be turned on for children
  before someone listens to a chunked reply end to end.
- It does not change time-to-first-*byte*; the device still buffers the whole
  body (§1.4).

### Tests

13 new tests in
`backend/tests/ArmenianAiToy.Application.Tests/ParallelChunkedSynthesisTests.cs`:
the flag is off with no config; overrides parse and `MaxChunks` clamps;
garbage values fall back to defaults; short text and a single long sentence
and an «Ի՞նչ» question each make exactly one call with the original string;
long text splits, **renders concurrently** (asserted via observed max
concurrency and wall clock), and concatenates back to the original sentences
in order; the chunk cap holds; a failing chunk falls back to one full-text
render; cancellation propagates without a fallback render; ID3 stripping and
non-MP3 pass-through.

Full suite: **2522 passed, 0 failed** (2509 before).


---

## Part 4 — 2026-09-01 research update: what the industry does, and our path

Since Part 2 was written, levers 1/3/4/9 LANDED (fast models in prod `327720b`,
POST-before-earcon + TLS reuse + DIAG gating in fw 1.2.0 `64a6957`, streaming
playback ON `f7b529b`). But the AI provider flips changed the board: chat is
Gemini, TTS is the ElevenLabs clone — and `/api/chat/story-qa` still synthesises
the WHOLE answer buffered (3–5 s) before the first byte leaves; the e4f0d6f
streaming pass-through was wired only on `/api/chat/audio`.

**Industry reality (sourced, 2026):** production voice agents run ~680–950 ms
voice-to-voice. The budget: streaming STT with server endpointing 150–300 ms,
LLM first token 150–500 ms, TTS first byte 100–300 ms, transport 50–150 ms —
everything streams, nothing store-and-forwards. Perceived-latency research: an
instant earcon + a spoken filler buys 2–4 s of tolerated wait.

**The Armenian constraint, re-checked:** Gemini Live and the fast TTS vendors
(Cartesia, Inworld, Deepgram) still have no hy. But two 2026 releases at OUR
existing vendor potentially close both gaps:
- **ElevenLabs Scribe v2 Realtime** — 150 ms streaming STT, "90+ languages";
  hy CONFIRMED in batch Scribe v2 (5–10 % WER), realtime list unenumerated.
- **Eleven v3 Conversational** — ~280 ms realtime TTS in the v3 family (the
  only family that speaks Armenian; the clone's family). Listed with hy.
Both need an afternoon of empirical testing with an API key before believing.
Azure hy-AM neural voices remain the proven-realtime TTS fallback (owner rated
them mediocre in the 2026-08-05 listen test — a quality compromise).

**Ranked plan from here:**

| # | Step | Cuts | Size |
|---|---|---:|---|
| 0 | Measure the CURRENT number on the toy (`[latency]` print + X-Qa headers) | — | tonight |
| 1 | **Stream story-qa TTS pass-through** — mirror e4f0d6f on `StoryQaController`, negotiated per request (firmware `useHTTP10(true)` on the QA POST avoids the HTTPClient chunked-framing trap; the AREG_QA_STREAM_PLAYBACK reader already tolerates a missing Content-Length) | **2–4 s** | backend, days |
| 2 | Gemini PAID tier (free tier = 5 req/min; throttling is latency too) | variable | config |
| 3 | **Empirical hy test: Scribe v2 Realtime + Eleven v3 Conversational** — one afternoon, decides whether both realtime gaps close at ElevenLabs | gate for #4 | test |
| 4 | **Stream the mic upload while the child speaks** (Willow shape: chunked upload/websocket during recording, server-side endpointing; STT result ~ready at button release) | **2–4 s** (upload + STT leave the critical path) | firmware+backend slice |
| 5 | Filler clip in the clone's voice between upload-end and first answer byte (the 2026-06 failure was decode-concurrent-with-UPLOAD starvation; with #4 the upload is over before the filler starts — but re-verify on the bench) | perceived | firmware |
| 6 | Host near providers | 0.4–1 s | ops decision |

**End state:** release → first audio ≈ 1.5–2.5 s real (the realistic floor for
an Armenian pipeline that keeps dual moderation serial), perceived ≈ instant
with the earcon + filler. The 800 ms industry median is English-only-stack
territory; we do not chase it at the cost of the safety ordering.

Safety invariant unchanged: input moderation stays serial before the model;
output moderation keeps gating what is returned (speculative TTS discarded on
block). Streaming changes WHERE bytes wait, never what is allowed to speak.

---

## Part 5 — negotiated first-byte streaming on `story-qa` (2026-09-01, implemented)

Step 1 of the Part 4 ranked plan (lever 2 of Part 2), done on both ends without a fleet-wide flag day. The
firmware notes (`esp32/AregVoiceMvp/latency-firmware-notes.md` § "What the
backend must do for item 5") had already named the contract; this is it.

**Backend.** `POST /api/chat/story-qa` streams the answer audio on its first
synthesized bytes — then pause + bridge (+ recap) in the same body, chunked —
**only** when the request carries `X-Areg-Accept-Stream: 1`, the TTS provider
implements `IStreamingAudioSynthesisService` (ElevenLabs), the answer is
model-authored, and output moderation passed. Every other request keeps the
buffered Content-Length body byte for byte (§1.4 is why). During output
moderation the speculative act is opening the provider stream; not one byte is
written before the classifier passes, and a blocked answer disposes the unread
stream and speaks the canned fallback exactly as before (§1.5 still holds).
Kill switch: `StoryQa:StreamAnswerAudio` (default true).

**Firmware.** The flag-on (`AREG_QA_STREAM_PLAYBACK`) async upload sends the
header; the sync fallback path and every flag-off build do not, because they
read through `read_response_into()` (§1.4). The request stays HTTP/1.1 — the
streaming reader already de-frames chunked bodies and treats a missing
Content-Length *as* chunked, so the `useHTTP10(true)` that Part 4's row 1 suggested would have broken it (Kestrel answers HTTP/1.0 with an unframed body closed by the server) and was rejected.
Compiled at the canonical FQBN, flag off (byte-identical) and flag on; **not
flashed, not bench-run** — the toy was busy with hardware work.

**Measured END TO END, every stage real (2026-09-02, after the environment's
network policy was opened to `api.openai.com`).** The real API (`dotnet run`,
Development, `AI:TtsProvider=elevenlabs`), a registered + claimed bench toy,
a real Armenian question clip («Ո՞վ է փոքրիկ ամպիկը։», 29 KB MP3 rendered by
ElevenLabs) posted as the child's recording, `gpt-4o-mini-transcribe` STT,
GPT answer, both moderation calls, `eleven_v3` TTS. Six warm-ups, then four
alternated pairs, `curl --no-buffer`, LittleCloud segment 0, every turn
`answered` (QLen 18, ALen ~40):

| Pair | buffered first byte | streamed first byte | streamed last byte | TTS stage (buffered → stream-open) |
|---|---:|---:|---:|---|
| 1 | 6.57 s | **2.81 s** | 4.09 s | 3377 → 633 ms |
| 2 | 4.55 s | **2.35 s** | 3.34 s | 2804 → 786 ms |
| 3 | 5.61 s | **2.47 s** | 4.77 s | 4081 → 681 ms |
| 4 | 6.20 s | **3.21 s** | 4.95 s | 4058 → 728 ms |
| mean | **5.73 s** | **2.71 s** | 4.29 s | |

**~3.0 s off the child's wait for the first spoken byte, end to end.** The
2.7 s that remain before the first byte are STT (0.5–1.1 s) + input
moderation (0.1–0.3 s) + GPT (0.6–2.3 s) + output moderation (0.1–0.2 s) +
the stream opening (~0.7 s) — none of which this slice touches. Buffered
`X-Qa-Tts-Ms` (2.8–4.1 s) against streamed (0.6–0.8 s) is the mechanism
itself, isolated. All eight bodies are clean MP3 with no chunk framing
inside. `Content-Length` without the header, `Transfer-Encoding: chunked`
with it.

**Earlier the same day — LIVE ElevenLabs, OpenAI stages stubbed** (kept: it
isolates the TTS half from STT/GPT variance). Real Kestrel, the
production `ElevenLabsTtsSynthesisService` adapter, `eleven_v3`,
`areg-storyteller`, a two-sentence Armenian answer (~85 chars), all five
bridges pre-warmed, buffered and streamed requests alternated. STT / GPT /
moderation stubbed (the org proxy blocks `api.openai.com`; those stages are
identical on both paths and cancel out of the difference).

| Pair | buffered first byte (= last byte) | streamed first byte | streamed last byte |
|---|---:|---:|---:|
| 1 | 5.07 s | **0.89 s** | 4.52 s |
| 2 | 5.33 s | **1.50 s** | 5.37 s |
| 3 | 4.37 s | **0.78 s** | 4.31 s |
| 4 | 4.59 s | **0.82 s** | 4.69 s |
| mean | **4.84 s** | **1.00 s** | 4.72 s |

**~3.8 s off the child's wait for the first spoken byte**, at the top of the
2–4 s Part 2 estimated. Total wall clock is unchanged (the audio still has
to be synthesized); only the silence before it starts is gone. All eight
bodies decode as clean MP3 with no chunk framing inside them (388 ± 20
frame syncs, zero CRLF-hex hits), i.e. Kestrel's chunked encoding is
correct on the wire and a de-framing client sees pure audio.

**Earlier, stub-TTS run (kept as the server-overhead control).** Stub answer TTS: first byte at 1.0 s, last at 3.0 s, for
both the buffered and the streaming call, so the two paths differ only in when
bytes leave the server. STT / moderation / GPT immediate; bridge pre-cached.

| Request | Framing | `curl --no-buffer` time-to-first-byte | total | body |
|---|---|---:|---:|---|
| no header | `Content-Length: 82400` | **3.00 s** (×3: 3.034 / 3.005 / 3.002) | 3.00 s | 82 400 B |
| `X-Areg-Accept-Stream: 1` | `Transfer-Encoding: chunked` | **1.17 s** (×3: 1.173 / 1.173 / 1.171) | 3.01 s | 82 400 B, `cmp` identical |
| header + buffered provider | `Content-Length: 82400` | 3.00 s | 3.00 s | buffered |

The streamed first byte lands at 1.17 s because the stub releases its first
4 KB chunk at 1000 + 2000 × 4096/48000 ≈ 1170 ms — the server adds nothing.
Both live runs above confirm it against the real providers. **What is not yet
measured:** the toy. Items 10–13 of the firmware notes' bench list are the remaining
verification — a chunk-header byte reaching the decoder is a burst of noise,
and a stalled stream must end in the canned failure clip, not a watchdog reboot.
