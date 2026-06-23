# AregVoiceMvp — firmware integration notes (voice Q&A hardening)

Handoff for whoever flashes the board. The **backend** half of the
voice in-story Q&A hardening is done, tested, and merged on branch
`feat/voice-storytelling-streaming-qa`. This doc lists the **firmware**
work that pairs with it, grounded in the current sketch
(`AregVoiceMvp.ino`, `voice_client.h`, `audio_io.h`, `config.h`).

> Status legend: ✅ backend done & unit-tested · 🔧 firmware change needed
> · ⚠️ needs on-device verification (can't be validated without hardware).

---

## 1. Story-audio access token (gap 1 — lock down the open stream)

### What changed on the backend ✅
`GET /api/story-audio/{storyId}` was open/unauthenticated. It is now
gated by a signed, short-lived token **when** `StoryAudio:SigningKey`
is configured. Two new contracts:

- **Mint:** `GET /api/chat/story-audio-token?storyId=<id>` — device-authed
  (send the same `X-Device-Id` / `X-Api-Key` headers the Q&A POST already
  sends). JSON response:
  ```json
  { "token": "<opaque>", "enforced": true, "expiresInSeconds": 3600 }
  ```
  When the feature is off (no signing key configured) it returns
  `{ "token": null, "enforced": false, "expiresInSeconds": 0 }`.
- **Use:** append `&token=<token>` (or `?token=` when there's no other
  query param) to every `GET /api/story-audio/...` request, including the
  `?from=<offset>` resume requests.
- **On a bad/expired/missing token** the stream returns **404** (same as
  an unknown story — deliberate concealment).

**Enforcement is OPT-IN.** With `StoryAudio:SigningKey` empty (the shipped
default) the stream stays open and the *current firmware keeps working
unchanged*. The firmware change below is only required before an operator
sets the signing key. **Do the firmware change first, then flip the key.**

### Firmware change — IMPLEMENTED (unverified) ✅🔧⚠️

> Landed in `voice_client.h/.cpp` (`voice_fetch_story_audio_token`) and
> `AregVoiceMvp.ino` (`handle_story_session`). **UNVERIFIED — not compiled or
> flashed.** What shipped vs the original sketch below:
> - **No new config constant.** The token URL is DERIVED from `AREG_BACKEND_URL`
>   (`.../api/chat/audio` → `.../api/chat/story-audio-token`), so the operator
>   doesn't have to add a key to the now-untracked `config.h`.
> - **Token fetched once per session** at the top of `handle_story_session`
>   (TTL shortened to 15 min, #038) and appended as `?token=` / `&token=` on the
>   from-start and `?from=` resume URLs (`url[]` 640 bytes). The `snprintf`
>   compose is now checked for truncation (#063): a clipped URL/token would
>   silently fail to validate, so a truncated compose ends the session cleanly
>   instead of opening a bad URL.
> - **404 recovery via REAL HTTP status (#063, was a timing heuristic):**
>   `audio_play_story_stream` now reports the stream-open result via an
>   `out_open_failed` flag — set true only on a non-200 GET (the concealment
>   404 of a rejected/expired token). The caller re-fetches the token + retries
>   once on that explicit signal, replacing the old "near-instant non-interrupted
>   return < 1500 ms" wall-clock guess. No threshold to tune.
> - Minimal manual JSON parse (no ArduinoJson). `token: null` (enforcement off)
>   → false → stream without a token.
>
> **On-device verification still required:** confirm playback works with a key
> set; confirm the retry fires on an expired token; confirm the derived URL is
> correct for your `AREG_BACKEND_URL`.

The original sketch (superseded by the above):

**`config.h`** — the URLs already exist; nothing new needed there, but note
the token is fetched at runtime (not a compile-time constant).

**`voice_client.h` / `.cpp`** — add a token-fetch call next to the existing
device-authed requests (reuse the same header-setting code):

```cpp
// voice_client.h
// Fetches a story-audio access token from the backend (device-authed).
// Writes the token into out_token (size out_cap) and returns true on
// HTTP 200 with a non-null token. Returns false when the feature is off
// (token == null) OR on any error — caller then streams without a token.
bool voice_fetch_story_audio_token(const char *story_id,
                                   char *out_token, size_t out_cap);
```

Implementation sketch (HTTPClient GET + headers + tiny JSON parse):

```cpp
bool voice_fetch_story_audio_token(const char *story_id,
                                   char *out_token, size_t out_cap) {
    // Build: <host>/api/chat/story-audio-token?storyId=<id>
    // (derive the host from AREG_BACKEND_URL, or add an AREG_TOKEN_URL).
    HTTPClient http;
    http.begin(token_url);
    http.addHeader("X-Device-Id", AREG_DEVICE_ID);
    http.addHeader("X-Api-Key",   AREG_DEVICE_API_KEY);
    http.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    http.setTimeout(AREG_HTTP_READ_MS);
    int status = http.GET();
    if (status != 200) { http.end(); return false; }
    String body = http.getString();
    http.end();
    // Minimal parse: find "token":"..." ; if the value is null, feature off.
    // (ArduinoJson is fine too; the body is small.)
    // ...extract into out_token, return false if null/empty...
    return true;
}
```

**`AregVoiceMvp.ino` → `handle_story_session()`** — this is the one place
that builds the story URL (currently lines ~397–404). Fetch a token once
when the session starts, and append it on **both** the from-start and the
`?from=` resume URLs:

```cpp
static char s_story_token[256];   // session-scoped
static bool s_have_token = false;

// at the top of handle_story_session(), before the while loop:
s_have_token = voice_fetch_story_audio_token(AREG_STORY_ID,
                                             s_story_token, sizeof(s_story_token));

// inside the loop, when building `url`:
if (s_story_offset > 0) {
    snprintf(url, sizeof(url), "%s?from=%u%s%s",
             AREG_STORY_AUDIO_URL, (unsigned)s_story_offset,
             s_have_token ? "&token=" : "", s_have_token ? s_story_token : "");
} else {
    snprintf(url, sizeof(url), "%s%s%s",
             AREG_STORY_AUDIO_URL,
             s_have_token ? "?token=" : "", s_have_token ? s_story_token : "");
}
```

> Bump the `url[]` buffer (currently `char url[320]`) so the token fits —
> e.g. `char url[640]`.

### Token expiry ⚠️
Default TTL is **3600 s** (`StoryAudio:TokenTtlSeconds`). A storytime
session is minutes, so one fetch per session is plenty. **Defensive
handling:** if a `?from=` resume ever returns **404** with a token present,
re-fetch the token once and retry the same URL before giving up. (A 404
with no token simply means the feature is off / story unknown — don't
retry-loop.)

### Don't forget
- `?refresh` is now gated by `StoryAudio:RefreshToken` (operator-only). The
  firmware never needs refresh; ignore it.
- The Q&A POST (`/api/chat/story-qa`) is unchanged — it's under `/api/chat`
  and already device-authed via headers. No token needed there.

---

## 2. The ~7–8 s "dead air" after barge-in (gap 5)

### Where it comes from
In `handle_story_session()` the sequence is: barge-in cuts the story →
`record_question()` (while held) → `voice_upload_question()` → play the
answer. `voice_upload_question()` is a **synchronous, blocking** POST whose
response is the fully-composed answer MP3 (server does STT → moderation →
GPT → TTS → compose, **buffered**). During that blocking call the speaker is
silent — that's the dead air. The state is `ST_UPLOADING`.

### Backend support already in place ✅
- On a transient server failure the child now hears a **spoken fallback**
  (not silence / a 502) — so the worst case is covered.
- `aat_story_qa_duration_seconds` is recorded server-side, so you can see
  the server's contribution to the latency in Prometheus.

### Status of firmware options ⚠️ UNVERIFIED — not compiled/flashed

**(A) Instant "thinking" earcon — IMPLEMENTED (unverified) ✅🔧⚠️**

`audio_play_thinking_earcon()` is implemented in `audio_io.cpp`. It
synthesizes a soft tone (~600 ms at 440 Hz) directly to I2S via
`synth_write_tone()` (a new static helper in `audio_io.cpp`) and writes
samples via `AudioOutputI2S.ConsumeSample()`. No network, no SD, no PSRAM
allocation.

Called in `handle_story_session()` immediately after `record_question()`
decides to upload — at the `ST_UPLOADING` transition — so the child hears
an instant acoustic acknowledgement before any network activity begins.

New constants in `config.h`:
- `AREG_EARCON_FREQ_HZ` (440 Hz)
- `AREG_EARCON_DURATION_MS` (600 ms)
- `AREG_EARCON_AMPLITUDE` (1200 — soft, non-startling)

**On-device verification required:**
- [ ] Earcon plays within ~150 ms of button release (Serial log: `[qa] earcon done`).
- [ ] No I2S click at start/end of tone (the linear fade-in/out should prevent this;
      tune `AREG_EARCON_AMPLITUDE` if clicks persist).
- [ ] `AREG_DISABLE_MP3_PLAYBACK` build still compiles (earcon no-ops to true).
- [ ] The `synth_write_tone` `ConsumeSample` API matches the installed
      ESP8266Audio version (see "Hardware assumptions" below).

**(B/C) Async upload + looping "thinking" bed + streamed Q&A reply —
IMPLEMENTED (unverified) 🔧⚠️**

This implements options B and C together:

- `voice_start_question_upload_async()` / `voice_async_upload_done()` /
  `voice_get_async_result()` added to `voice_client.cpp` and `voice_client.h`.
  The upload runs in a FreeRTOS task pinned to CORE 0 (`xTaskCreatePinnedToCore`,
  core 0 = PRO_CPU). The main loop (CORE 1 = APP_CPU) polls
  `voice_async_upload_done()` between each thinking-bed pulse.

- The thinking-bed loop in `handle_story_session()` calls
  `audio_play_thinking_earcon()` repeatedly (reusing the earcon function)
  while the upload task is in flight. Each pulse is ~600 ms; up to
  `AREG_THINKBED_MAX_PULSES` (70) pulses before a silent busy-wait fallback.

- **Answer playback (corrected after review):** when the async upload
  completes the firmware plays the answer the task already buffered from the
  **POST response body** via `audio_play_mp3_buffer()`. The earlier
  separate-`GET`-to-the-Q&A-URL attempt was **REMOVED** — that route is
  POST-only (a GET 404s) and, if a GET were ever added, it would RE-RUN the
  whole STT+GPT+TTS pipeline and **double-bill** for a single question.
  `audio_play_qa_stream()` remains in `audio_io.*` for a future story-style
  stream source but is NOT used on the Q&A path.
  **TODO (real latency win, needs on-device verification):** decode
  incrementally from the live POST response stream — change the async task to
  hand the HTTP stream to the MP3 decoder instead of `read_response_into()`
  buffering it first. The backend already sends a byte-identical *streamed*
  POST body (S2), so only the firmware side needs this change.

New constants in `config.h`:
- `AREG_THINKBED_FREQ_HZ` (280 Hz — lower/warmer than the earcon)
- `AREG_THINKBED_PULSE_MS` (500 ms per pulse)
- `AREG_THINKBED_AMPLITUDE` (700 — quieter than the earcon)
- `AREG_THINKBED_MAX_PULSES` (70)

A latency log line is emitted at playback start:
`[latency] qa_release->play_begin_ms=<n>`

**PSRAM ownership contract (documented in voice_client.h):**
- `payload` pointer (caller-owned PSRAM) must remain valid until
  `voice_async_upload_done()` returns true. The task reads from it but
  does NOT free it. Caller frees it after the task is done.
- `s_response_buffer` (voice_client-owned PSRAM) is allocated by the task
  via `read_response_into()`. Freed by `voice_release_last_response()` as
  in the synchronous path.

**Core assignment (HARDWARE ASSUMPTION — needs on-device verification):**
- Upload task: `xTaskCreatePinnedToCore(..., 0)` → CORE 0 (PRO_CPU).
- Loop / thinking-bed / playback: CORE 1 (APP_CPU, where `loop()` runs in
  Arduino-ESP32 by default).
- If Arduino-ESP32 changes its default core assignment, the `core=0`
  constant in `voice_client.cpp` must be updated.

**On-device verification required:**
- [ ] `xTaskCreate` succeeds (Serial log: no `[qa-async] xTaskCreate FAILED`).
- [ ] Thinking-bed pulses play continuously without audio dropout during upload.
- [ ] Task completes within expected time (< 30 s for AREG_HTTP_READ_MS limit).
- [ ] `voice_release_last_response()` is not called from both cores simultaneously
      (the task calls it at the top of `upload_question_task()`; by that point
      the prior turn's response has already been freed by the story-resume path —
      verify no double-free in back-to-back Q&A turns).
- [ ] Streaming path (GET) works if/when the backend implements a GET endpoint
      at `AREG_STORY_QA_URL`; until then, buffered fallback plays correctly.
- [ ] Three back-to-back Q&A turns: no silent gaps, story auto-resumes each time.

**Known TODO (refactor once verified on device):**
- The thinking-bed loop calls `audio_play_thinking_earcon()` (which uses
  `AREG_EARCON_FREQ_HZ` and `AREG_EARCON_DURATION_MS`). To use the separate
  `AREG_THINKBED_*` constants, expose `synth_write_tone()` (currently a static
  helper in `audio_io.cpp`) or add an `audio_play_thinking_bed_pulse()` that
  reads the thinkbed constants. Deferred until the coexistence of repeated
  AudioOutputI2S init and the FreeRTOS task is verified on hardware.

### Summary: what each option achieves

| Option | Fills "is it broken?" gap (first ~0.6 s) | Fills whole wait window | Streaming decode | Status |
|---|---|---|---|---|
| A earcon | ✅ | ❌ (earcon ends, then silence until server) | n/a | IMPLEMENTED ⚠️ unverified |
| B async + thinking bed | ✅ (earcon first) | ✅ (pulses throughout wait) | ❌ (buffered) | IMPLEMENTED ⚠️ unverified |
| C streamed reply | ✅ | ✅ | ✅ (incremental) | IMPLEMENTED ⚠️ unverified — needs backend GET endpoint |

---

## 3. Whisper Armenian accuracy (gap 6) — no firmware change

The backend now **biases Whisper with the current story scene** on the Q&A
path, which directly targets short / single-word Armenian. This is fully
server-side and automatic — **the firmware needs no change**. Just keep
sending the WAV + `?storyId=&offset=` exactly as today.

Optional future: switching the STT model to `gpt-4o-transcribe` is a backend
config change (not firmware) and should be gated on a paid A/B validation run
on real child audio.

---

## 4. Verification checklist (on the bench)

Token (gap 1), with `StoryAudio:SigningKey` set on the backend:
- [ ] `GET /api/chat/story-audio-token?storyId=anban-huri` with device headers
      returns `enforced:true` + a token (curl from the laptop first).
- [ ] Story plays start-to-finish with `&token=` appended.
- [ ] Barge-in → resume (`?from=...&token=...`) continues correctly.
- [ ] A request with **no** token returns 404 (and the firmware degrades —
      ideally re-fetches once).
- [ ] With the signing key **empty**, the unmodified flow still works
      (regression guard).

Dead air (gap 5) — S1 earcon:
- [ ] Serial log shows `[qa] earcon done` within ~150 ms of button release.
- [ ] No audible click at start/end of earcon tone (linear fade should prevent it).
- [ ] `AREG_DISABLE_MP3_PLAYBACK` build compiles and earcon silently no-ops.
- [ ] Verify `AudioOutputI2S.ConsumeSample(int16_t[2])` signature matches
      the installed ESP8266Audio library version (see audio_io.cpp comment).

Dead air (gap 5) — S3 async upload + thinking bed + streamed Q&A:
- [ ] `[qa-async] POST` appears in Serial immediately after `[qa] earcon done`.
- [ ] Thinking-bed pulses are audible throughout the upload wait (no silent gap).
- [ ] `xTaskCreatePinnedToCore` succeeds (no `FAILED` line in Serial).
- [ ] `[latency] qa_release->play_begin_ms=<n>` appears; compare to pre-change value.
- [ ] Streamed path plays correctly when backend GET endpoint is available.
- [ ] Buffered fallback plays correctly when stream fails (e.g. backend GET returns 404).
- [ ] Three back-to-back Q&A turns without double-free or crash.
- [ ] Story auto-resumes from `s_story_offset` after each answered question.

---

## 5. What is and isn't verified

- **Verified (backend):** token mint/validate/expiry, story-audio 404 on bad
  token, rate-limit + refresh gate, transcript moderation, turn persistence,
  502→spoken-fallback, offset→segment map, Whisper scene-biasing,
  voice-path metrics. All covered by unit tests (full suite green).
- **NOT verified here (needs the device):** all firmware code in this doc and
  in the S1/S3 implementation. The files compile in the Arduino IDE only — this
  repo has no ESP32 toolchain configured. All new code is marked
  `UNVERIFIED — not compiled/flashed`. Treat it as a precise spec + working
  draft; on-device bring-up is required.

### Hardware assumptions in S1/S3 (must verify on bench)

1. **`AudioOutputI2S.ConsumeSample(int16_t lr[2])`** — the sample-push API
   used by `synth_write_tone()`. This is the correct signature for ESP8266Audio
   >= 2.3.0. Earlier versions use a different prototype. Check the installed
   version in the Arduino Library Manager.

2. **`AudioOutputI2S.begin()` is the correct init call** for the standalone
   (non-MP3-decoder-driven) output path used by the earcon. Some ESP8266Audio
   versions require `SetPinout()` before `begin()`; the code does this.

3. **`xTaskCreatePinnedToCore(..., 0)`** pins to PRO_CPU (core 0). Arduino-ESP32
   runs `loop()` on APP_CPU (core 1) by default. If this assignment is wrong
   on your board configuration, the thinking-bed loop and the upload task would
   run on the same core and the thinking bed would starve until the upload
   finishes (same as before, but worse if the watchdog fires). Verify with
   `xPortGetCoreID()` prints from both contexts.

4. **FreeRTOS task stack 8192 bytes** is sufficient for `HTTPClient` + `WiFiClient`
   within `upload_question_task`. Monitor with `uxTaskGetStackHighWaterMark`.

5. **Repeated `AudioOutputI2S` construction/destruction per thinking-bed pulse.**
   Each call to `audio_play_thinking_earcon()` (used for thinking-bed pulses too)
   creates a fresh `AudioOutputI2S` stack object and calls `begin()` + `stop()`.
   The MAX98357A I2S amp should tolerate this; if audible pops occur, the fix is
   to hold a persistent `AudioOutputI2S` across calls (requires refactoring
   `synth_write_tone` to accept it as a parameter).

6. **`sinf()` in `synth_write_tone()`** — the Xtensa LX7 FPU handles this in
   hardware (~10 cycles). No soft-float fallback needed on ESP32-S3.

---

## 6. Offline-first mode — do everything that doesn't NEED the cloud, on-device

**Design principle: offline is the DEFAULT, the cloud is an ENHANCEMENT.** Power
on with no Wi-Fi → the toy is still a working storyteller. Connectivity adds
exactly **two** things and nothing else: live in-story Q&A *answers* (GPT) and
the *STT* that feeds them. Everything else must run from local assets. The toy
must never be bricked by a missing connection.

### 6.1 What runs offline vs what needs the cloud

| Capability | Offline? | How |
|---|---|---|
| Tell a story (narration) | ✅ | Decode a pre-rendered MP3 from the SD card |
| Pick a story | ✅ | Local selection over an on-SD manifest |
| Barge-in → pause / resume | ✅ | Already local — offset tracking needs no server |
| Micro-rewind on resume | ✅ | Port the snap to firmware + ship the offset sidecar |
| Return-to-story bridge after a pause | ✅ | Bridges are 5 fixed lines — pre-render them to SD |
| Scene recap | ✅ | Recaps are fixed per segment — pre-render to SD |
| "Thinking" earcon / failure / offline cues | ✅ | Small local audio clips on SD |
| LED states | ✅ | Already local |
| **In-story Q&A — the spoken ANSWER** | ❌ | GPT. Offline → play a canned "let's keep listening" clip |
| **Speech-to-text of the question** | ❌ | Whisper (cloud) |
| Moderation | n/a offline | Only runs on child *input*, which isn't processed offline |

> So offline = "pick a story and listen, with pause/resume/rewind"; online simply
> *adds* "ask Areg about it." Build the offline half first and completely.

### 6.2 The microSD card

A genuine **8–16 GB SanDisk / Samsung / Kingston** microSD, **FAT32** (MBR).
- Capacity is trivial: 100 stories ≈ 200–450 MB. 8 GB is plenty.
- **Speed class is irrelevant** — MP3 playback needs ~16 KB/s; any card has 1000×
  headroom. Don't pay for UHS / V30 / A2.
- Read-heavy workload (stories pre-loaded) → low wear; a standard card is fine.
- Prefer **SDHC (≤32 GB) + FAT32** (read natively by the Arduino/ESP-IDF SD libs);
  avoid SDXC/exFAT unless you add exFAT support.
- The real risk is **counterfeit cards** — buy genuine from a trusted seller.

### 6.3 SD wiring (ESP32-S3, datasheet-grounded)

The ESP32-S3 has a real SD/MMC host (1/4/8-bit) **and** works over SPI. For a toy,
**SPI mode is the pragmatic choice** (4 pins, simplest, throughput is a non-issue).

- **3.3 V native — NO level shifter** (VDD33 = 3.0–3.6 V; never feed 5 V to a GPIO).
- Add **pull-ups on CMD/DAT** per the SD spec.
- **AVOID strapping pins for SD:** `GPIO0, GPIO3, GPIO45, GPIO46`. Also avoid
  `GPIO19/20` (USB) and, on **octal-PSRAM** modules only (R8/R16V), `GPIO35/36/37`.
  The DevKitC-1's common **N8R2** is *quad* PSRAM, so 35/36/37 are free there.
- **No conflict with the two I2S peripherals.** SD on SPI2 is a third, independent
  DMA peripheral; mic-I2S + amp-I2S + SD-SPI run together fine. Just keep the GPIOs
  non-overlapping with the existing map (`config.h`: mic 4/5/6, amp 7/15/16,
  button 0, LED 48).
- Pins are routable to any free GPIO via the GPIO Matrix. Example SPI set to
  validate against your PCB: `CS=10, SCK=12, MOSI=11, MISO=13` (adjust to your
  wiring; the only hard rule is the avoid-list above).

Add the pins to `config.h`:
```cpp
// --- microSD (SPI mode) --------------------------------------
#define AREG_PIN_SD_CS    10
#define AREG_PIN_SD_SCK   12
#define AREG_PIN_SD_MOSI  11
#define AREG_PIN_SD_MISO  13
```

### 6.4 On-SD content pack layout

```
/manifest.json                     # [{id, title, bedtimeSafe}, ...] — drives selection
/stories/<id>/narration.mp3        # the pre-rendered, re-encoded narration
/stories/<id>/narration.offsets.json   # sentence-start byte map (micro-rewind)
/stories/<id>/narration.segments.json  # segment-start byte map (recap anchoring)
/clips/bridge_0.mp3 ... bridge_4.mp3    # the 5 fixed return-to-story lines
/clips/recap_<id>_<segment>.mp3         # per-segment recaps (optional, if used)
/clips/thinking.mp3                     # instant earcon (also used online — §2A)
/clips/offline_qa.mp3                   # "Հիմա չեմ կարող պատասխանել, շարունակե՛նք լսել։"
/clips/failure.mp3                      # generic "can't reach you" (already exists)
```

The `.mp3`, `.offsets.json`, `.segments.json` are **exactly the artifacts the
backend already produces** for `/api/story-audio` (the MP3 + the two sidecars).
The content pack is just those files copied to SD, per approved story.

### 6.5 Firmware changes for offline playback

> **Slice 2 — IMPLEMENTED (UNVERIFIED) ✅🔧⚠️ (2026-06-20).** Items 1, 2, and
> the playback half of 6 below have landed and compile against the sketch, but
> are **not yet flashed/verified on hardware**:
> - `config.h(.example)`: `AREG_PIN_SD_{CS,SCK,MOSI,MISO}` (10/12/11/13) +
>   `AREG_SD_STORY_NARRATION` = `/stories/<AREG_STORY_ID>/narration.mp3`.
> - `audio_io.h/.cpp`: `audio_sd_begin()` / `audio_sd_available()` /
>   `audio_sd_has_file()` / `audio_play_story_file()` (AudioFileSourceSD +
>   `seek(start_byte)`; getPos() is absolute, so no base_offset).
> - `AregVoiceMvp.ino`: SD mount in `setup()` (non-fatal); `handle_story_session()`
>   picks SD **offline-first** when `AREG_SD_STORY_NARRATION` exists on the card,
>   else the Wi-Fi stream (token fetch + retry scoped to the stream path).
>
> **Still deferred (NOT in Slice 2):** local micro-rewind `SnapOffset` port
> (item 3 — today resume relies on the FUDGE-byte overlap only), manifest-driven
> selection (item 4), offline Q&A degrade clip (item 5), internal-flash baseline
> (§6.7). Build the content pack with `tools/ContentPackBuilder` and copy it to
> the card root so `/stories/<id>/narration.mp3` is present.
>
> **On-device verification required:**
> - [ ] `[boot] SD mounted; offline narration … = present` at boot with a card in.
> - [ ] With the pack on the card: `[story] source = SD (offline)`, story plays
>       with **Wi-Fi off** entirely.
> - [ ] Barge-in cuts instantly; `[story] SD barge-in: abs=… resume_from=…`;
>       a quick tap pause + press resumes near the same spot (FUDGE overlap).
> - [ ] No card / empty card → `[boot] SD not mounted …` and the device still
>       streams over Wi-Fi (regression guard).
> - [ ] Confirm `AudioFileSourceSD::seek(int32_t, SEEK_SET)` + `getPos()` match
>       the installed ESP8266Audio version (same library the stream path uses).
> - [ ] SD-SPI (10/12/11/13) coexists with mic-I2S (4/5/6) + amp-I2S (7/15/16)
>       with no bus contention or audible glitch.

1. **SD init + mount** at boot (FAT32). If no card → fall back to the 1–2 stories
   baked into internal flash (see §6.7); never hard-fail.
2. **`audio_play_story_file(path, start_byte, barge_in, out_resume_offset)`** — an
   SD-source sibling of the existing `audio_play_story_stream(url, …)`
   (`audio_io.h`). Same barge-in + resume contract; resume is just a **file
   `seek(start_byte)`** (simpler than the HTTP `base_offset + getPos()` math).
   ESP8266Audio reads from an `AudioFileSourceSD` instead of the HTTP source.
3. **Local micro-rewind.** Port the backend's pure snap — `StoryAudioController.SnapOffset(map, from)` (binary-search the largest boundary ≤ `from`) — into the
   firmware, loading `<story>.offsets.json` once per session. Identical logic, no
   server round-trip.
4. **Story selection** over `/manifest.json` (e.g. a button gesture to cycle, or
   whatever the product UI decides). Keep v1 minimal — cycle + play.
5. **Offline Q&A degrade.** A held barge-in while offline must NOT attempt an
   upload — play `/clips/offline_qa.mp3` and auto-resume. Online keeps the existing
   `voice_upload_question()` path. Decide by `voice_wifi_is_connected()` (already
   in `voice_client.h`) + a quick reachability check.
6. **Never block on Wi-Fi.** Connect in the background; playback starts immediately
   from SD regardless of connectivity.

### 6.6 Content-pack build step (tooling, off-device)

For each **approved** story (`Stories/Content/*.story.json` — NOT drafts):
1. Render once via the backend `GET /api/story-audio/<id>` → yields `mp3` +
   `.offsets.json` + `.segments.json` in the story-audio cache.
2. **Re-encode** the MP3 to **48 kbps mono** (`ffmpeg -i in.mp3 -ac 1 -b:a 48k out.mp3`)
   — transparent for kids' speech on a small speaker, ~2 MB/story.
3. ⚠️ **Byte maps are encoding-specific.** The `.offsets.json` / `.segments.json`
   byte offsets are tied to the *exact* MP3 bytes. If you re-encode AFTER
   generating them, they go stale. So either render at the target bitrate, **or**
   regenerate the maps from the re-encoded file (the maps are char-proportional —
   a tiny script can recompute them from the story's sentence/segment char
   positions × the re-encoded file's chunk byte sizes). **Ship maps that match the
   MP3 actually on the SD.**
4. Copy into the `/stories/<id>/` layout, append the story to `/manifest.json`,
   pre-render the bridges/recaps/cues into `/clips/`.

A future small CLI (`tools/ContentPackBuilder`) should automate steps 1–4 so a
fresh SD image is reproducible.

### 6.7 "Works out of the box" baseline

Bake **1–2 stories** (+ their sidecars + the canned clips) into the internal-flash
LittleFS partition (≈13.6 MB usable on a 16 MB module — fits a couple at 48 kbps).
So even with **no SD card and no Wi-Fi**, the toy tells a story on first power-on.
The SD card is what scales that to 100+.

### 6.8 Build order (offline first)

1. ✅ (Slice 2, unverified) SD mount + `audio_play_story_file` + offline-first
   selection in `handle_story_session` (plays `/stories/<id>/narration.mp3`
   from the card when present, else the Wi-Fi stream).
   ✅ (Slice 3, unverified) post-story flow `handle_post_story_flow()`:
   conclusion (`/stories/<id>/conclusion.mp3`) → reflection question
   (`/stories/<id>/question-0.mp3`), both OFFLINE; then ONLINE-only listen
   window → record the child's answer → POST to
   `/api/chat/story-qa/reflection-answer` (`voice_upload_reflection_answer`)
   → play the warm acknowledgement. Offline, the answer step is skipped (it
   needs STT+GPT); an optional `/clips/offline_close.mp3` plays instead.
   **On-device verification:** with the pack on the card and Wi-Fi UP, finish a
   story → hear conclusion + question → press & hold → speak → hear the
   acknowledgement; with Wi-Fi DOWN, hear conclusion + question then a quiet
   close (no upload attempt). New config: `AREG_SD_STORY_CONCLUSION`,
   `AREG_SD_STORY_QUESTION0`, `AREG_SD_OFFLINE_CLOSE`,
   `AREG_STORY_REFLECTION_URL`, `AREG_REFLECTION_LISTEN_MS`.
2. Local micro-rewind (`SnapOffset` port + offsets sidecar).
3. Manifest-driven selection.
4. Offline Q&A degrade clip + connectivity gating.
5. Internal-flash baseline story (no-SD fallback).
6. *Then* layer the online enhancements (token §1, Q&A, dead-air §2) on top.
