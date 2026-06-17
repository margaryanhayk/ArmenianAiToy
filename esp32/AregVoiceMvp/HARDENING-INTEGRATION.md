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
