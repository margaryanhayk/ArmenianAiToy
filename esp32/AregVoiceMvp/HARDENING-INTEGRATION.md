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

### Firmware change 🔧

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

### Firmware options 🔧⚠️

**(A) Instant "thinking" earcon — quick win, firmware-only, low risk.**
Play a short cue the moment recording ends, *before* the blocking upload, so
the child gets immediate acknowledgement instead of silence:

```cpp
// in handle_story_session(), right after record_question() decides to upload:
transition_to(ST_UPLOADING);
audio_speaker_begin();
audio_play_thinking_earcon();      // ~0.4–0.8 s: a soft chime, or a tiny
                                   // pre-rendered «Հըմմ…» MP3 in PROGMEM
VoiceTurnResult turn = voice_upload_question(...);
```
Caveat: because the upload is synchronous on the same core, the earcon
plays then returns to silence for the *rest* of the wait. It removes the
"is it broken?" gap at the front but doesn't fill the whole window. Still
the highest value-per-effort step. Add a new `audio_play_thinking_earcon()`
to `audio_io` (generate a short tone to I2S, or decode a tiny embedded MP3
via the existing `audio_play_mp3_buffer`). Consider reusing/!extending the
`ST_UPLOADING` LED so the visual + audio cue agree.

**(B) Async upload + looping "thinking" bed — full mitigation, higher effort.**
Run `voice_upload_question()` on the second core (FreeRTOS task) while core 0
loops a low "thinking" hum until the response arrives. This fills the entire
window but needs care around the PSRAM response-buffer ownership
(`voice_release_last_response()`) and I2S handoff. Only worth it if (A) +
measurement show the perceived latency is still too long.

**(C) Stream the reply — best perceived latency, backend + firmware.**
Have the server stream the answer MP3 as its TTS chunks become ready (chunked
transfer) and decode it incrementally on the device. The firmware already has
streaming MP3 decode for the story (`audio_play_story_stream`, ESP8266Audio
HTTP source) — the same technique applies to a streamed Q&A response. This is
the real fix for the *whole* window but is the largest change and was
**deliberately not done backend-side without a device to verify against** (a
half-streamed change the current firmware can't consume would regress the
working buffered path). Pair it with the firmware streaming-decode work in
one device-side session.

### Recommended order
1. Add an on-device latency log for `release → answer-play-begin` (mirror the
   existing `[latency]` line) so you can measure before/after.
2. Ship **(A)** the earcon. Re-measure perceived latency with a child.
3. Only if still poor, do **(B)** or **(C)**.

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

Dead air (gap 5):
- [ ] Serial shows `release → answer-play-begin` ms before the change.
- [ ] Earcon plays within ~150 ms of the button release.
- [ ] Three back-to-back Q&A turns: no silent gaps, story auto-resumes each
      time.

---

## 5. What is and isn't verified

- **Verified (backend):** token mint/validate/expiry, story-audio 404 on bad
  token, rate-limit + refresh gate, transcript moderation, turn persistence,
  502→spoken-fallback, offset→segment map, Whisper scene-biasing,
  voice-path metrics. All covered by unit tests (full suite green).
- **NOT verified here (needs the device):** everything in this doc's firmware
  snippets. They are written against the current sketch's structure but have
  **not been compiled or flashed** — treat the code as a precise spec, not
  drop-in source.

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

1. SD mount + `audio_play_story_file` + play one hard-coded story from SD.
2. Local micro-rewind (`SnapOffset` port + offsets sidecar).
3. Manifest-driven selection.
4. Offline Q&A degrade clip + connectivity gating.
5. Internal-flash baseline story (no-SD fallback).
6. *Then* layer the online enhancements (token §1, Q&A, dead-air §2) on top.
