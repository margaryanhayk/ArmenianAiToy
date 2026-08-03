# ESP32 ↔ Backend chain

Short reference for the end-to-end chat path between an ESP32 (or
browser) and the .NET backend that runs on a dev laptop. This is a
documentation-only file — no firmware is changed here. The
authoritative bench instructions for the voice prototype live at
[`esp32/AregVoiceMvp/README.md`](../esp32/AregVoiceMvp/README.md);
this doc only describes the *chain* and its known quirks.

## Repo-tracked vs local-scratch prototype

There are **two parallel firmware tracks** for this project, and
this distinction matters for any reproducibility / safety story:

- **Repo-tracked firmware** — `esp32/AregVoiceMvp/`,
  `esp32/ArmenianAiToy/`. These are committed sketches that any
  collaborator can build. The voice MVP is current; the legacy
  text sketch is stale (see below). Neither sketch carries
  credentials in tree (the `config.h` files are tracked for pins
  / timing constants, with credential lines left blank or as
  placeholders that the operator fills in locally).
- **Local-scratch prototype** — typically lives under the operator's
  `~/Documents/Arduino/` (or `%USERPROFILE%\Documents\Arduino\`)
  outside this repo. This is where iterative bench work happens
  (loading UX tweaks, alternate UI experiments, one-off Wi-Fi
  configurations). **Anything in the local-scratch tree is not
  shipped, not reviewed, and is the operator's own state.** It
  may carry real Wi-Fi passwords, real device API keys, and
  real backend IPs — those must never be copy-pasted into a
  committed config.

When this doc says "the proven chain", it refers to the
**repo-tracked** sketches plus the backend on this branch. If a
behavior only works in the local-scratch sketch, it is not
reproducible from a fresh clone — say so explicitly.

## What's in the repo

Two firmware sketches and a backend with three browser pages:

| Path | What it is | Status |
|---|---|---|
| `esp32/AregVoiceMvp/AregVoiceMvp.ino` | C1 voice bench prototype (button-to-talk → `/api/chat/audio`). Sends device-auth headers. | **Active** — matches current backend contract. |
| `esp32/ArmenianAiToy/ArmenianAiToy.ino` | Phase-1 text-chat sketch (browser → ESP32 → `/api/chat`). | **Stale.** Does not send `X-Device-Id` / `X-Api-Key`; the backend's `DeviceAuthMiddleware` rejects it with 401. Not part of any current proven flow. |
| `backend/src/ArmenianAiToy.Api/wwwroot/bench.html` | Browser dev UI: register a device + send chat messages with the issued device id/key. Served at `/bench.html`; it was moved off `/` on 2026-08-04 because a visitor to the site was landing on it. | Active. |
| `backend/src/ArmenianAiToy.Api/wwwroot/story.html` | Browser story-mode UI (handles `storySessionId` / `selectedChoice` round-trip). | Active. |
| `backend/src/ArmenianAiToy.Api/wwwroot/parent.html` | Parent dashboard (login, linked devices, conversation history, etc.). | Active. |

## Proven chain — browser → backend

Used during development from a phone on the same LAN as the dev
laptop. This is the chain exercised by `wwwroot/bench.html` and
`wwwroot/story.html`.

```
Phone (browser)
  │
  │  HTTP  http://<laptop-lan-ip>:5000/
  ▼
Backend (ArmenianAiToy.Api)
  │
  ├── GET  /                            → wwwroot/index.html (product front page)
  ├── GET  /bench.html                  → wwwroot/bench.html (dev UI)
  ├── GET  /story.html                  → wwwroot/story.html
  │
  ├── POST /api/devices/register         → returns { deviceId, apiKey }
  │       (one-time per browser; stored in localStorage)
  │
  └── POST /api/chat                     → AI response
          Headers:
            X-Device-Id: <deviceId from register>
            X-Api-Key:   <apiKey from register>
          Body:
            { message, childId?, storySessionId?, selectedChoice? }
          │
          ▼
        ChatController → ChatService
          │
          ├── pause / bedtime / mode-disabled gate (no AI call)
          │     → returns canned Armenian short reply
          │
          ├── moderation (input)
          ├── ModeDetector (Story / Game / Riddle / Curiosity / Calm)
          ├── prompt assembly (system + child context + history + memory)
          ├── OpenAI GPT-4o (via OpenAIReliabilityGate)
          ├── tail-block parse (story choices)
          ├── moderation (output)
          │
          ▼
        JSON: { response, conversationId, messageId, safetyFlag, ... }
```

The handshake is browser-driven: the page calls
`POST /api/devices/register`, stores the issued credentials in
`localStorage`, and attaches them as `X-Device-Id` / `X-Api-Key`
on every subsequent `/api/chat` call.

## Proven chain — ESP32-S3 voice bench

Used for the C1 voice MVP. Authoritative setup, pinout, and bench
demo steps live in
[`esp32/AregVoiceMvp/README.md`](../esp32/AregVoiceMvp/README.md);
this section only summarises the chain shape.

```
ESP32-S3 + INMP441 mic + MAX98357A amp + tactile button + WS2812 LED
  │
  │  WAV (16 kHz mono PCM, 44-byte header)
  │  HTTP  http://<laptop-lan-ip>:5000/api/chat/audio
  │  Headers: X-Device-Id, X-Api-Key
  ▼
Backend (AudioChatController)
  │
  ├── same pause / bedtime / mode-disabled gate (pre-STT — no upstream cost)
  ├── IAudioTranscriptionService (OpenAI Whisper, Language="hy")
  ├── ChatService.GetResponseAsync (same pipeline as text)
  ├── IAudioSynthesisService (OpenAI TTS, voice "Nova", MP3)
  ├── blob persistence (Message.AudioBlobPath)
  │
  ▼
MP3 (audio/mpeg)  ──►  minimp3 on ESP32-S3  ──►  I²S out  ──►  speaker
```

Five LED states (idle / recording / uploading / playing / error)
and one canned Armenian failure clip on any error path. Buffered
playback only — no streaming.

## Backend run

```
cd backend
dotnet run --project src/ArmenianAiToy.Api
```

By default the API binds `http://0.0.0.0:5000`. To pick a different
port (e.g. when `:5000` is taken):

```
dotnet run --project src/ArmenianAiToy.Api --urls http://0.0.0.0:5050
```

Make sure the LAN IP you give the ESP32 / phone is *your dev
laptop's* IP on the same Wi-Fi network, not `localhost` /
`127.0.0.1`.

## Credentials — what NOT to commit

These files carry secrets when used and must never be staged:

- `esp32/AregVoiceMvp/config.h` — Wi-Fi SSID/password,
  `AREG_DEVICE_ID`, `AREG_DEVICE_API_KEY`. The file is tracked
  (so pin / timing constants ship with the repo) but `.gitignore`
  does **not** exclude it. Run
  `git diff --staged -- esp32/AregVoiceMvp/config.h` before every
  commit and confirm it prints nothing. Optionally
  `git update-index --skip-worktree esp32/AregVoiceMvp/config.h`
  during bench work.
- `esp32/ArmenianAiToy/config.h` — no credentials today (only
  pins / timeouts) but treat the same way if you ever add SSID
  / device creds.
- `backend/src/ArmenianAiToy.Api/armenian_ai_toy.db*` — local
  SQLite scratch DB.
- `backend/src/ArmenianAiToy.Api/wwwroot/audio-blobs/` (and any
  other `audio-blobs/` produced by `AudioChatController`).
- Any `.env*`, JWT keys, OpenAI API keys.

The OpenAI key is provisioned with
`dotnet user-secrets set "OpenAI:ApiKey" "sk-..." --project src/ArmenianAiToy.Api`
and lives outside the repo entirely.

## Known quirks and limitations

- **Legacy text sketch is broken against the current backend.**
  `esp32/ArmenianAiToy/ArmenianAiToy.ino` forwards
  `POST /api/chat` with no `X-Device-Id` / `X-Api-Key`, so the
  `DeviceAuthMiddleware` returns 401. The browser dev UI
  (`wwwroot/bench.html` / `wwwroot/story.html`) is the
  currently-working text path; the legacy sketch needs a
  register-then-attach-headers retrofit before it talks to the
  current backend. Not in scope for this slice.
- **`192.168.X.X` is dynamic.** Whatever IP your laptop has on
  the LAN today may change tomorrow. WiFiManager exposes a
  config-portal field so the ESP32 backend URL can be re-entered
  without a reflash.
- **2.4 GHz only** for ESP32. ESP32-S3 has no 5 GHz radio. A
  merged-band SSID that prefers 5 GHz can leave the board stuck
  at `[wifi] connecting...` on otherwise-correct credentials.
- **No retries on the ESP32 voice MVP.** A dropped Wi-Fi packet
  mid-upload trips the canned error clip; next press works
  normally.
- **No streaming.** Voice responses are buffered in PSRAM and
  played after the full MP3 arrives. C2+ concern.
- **No barge-in.** Button presses during UPLOADING / PLAYING /
  ERROR are ignored.
- **HTTP only on the bench.** No TLS between ESP32 and backend
  on the dev LAN.
- **One device-id per device.** Re-running
  `POST /api/devices/register` issues a new id; the old row
  remains until a parent unlinks it.

## Quick smoke test (browser path)

1. Backend running on `http://<laptop>:5000`.
2. Phone on the same Wi-Fi, open `http://<laptop>:5000/`.
3. The page calls `POST /api/devices/register` automatically on
   first load and stores `{ deviceId, apiKey }` in
   `localStorage`.
4. Type an Armenian message → page sends
   `POST /api/chat` with the headers above → Areg replies in
   Armenian.
5. Open `http://<laptop>:5000/story.html` → tap "սկսել" → first
   story turn renders with two choice buttons → tapping a
   choice POSTs another `/api/chat` with the same
   `storySessionId` and `selectedChoice=option_a|option_b`.

If step 3 ever fails with 401, the most likely cause is a stale
`deviceId`/`apiKey` in `localStorage` left over from an older
backend DB. Clear site data for the host and reload.

## Next hardware steps

Status of the physical-toy build, broken down so the next operator
can pick the smallest useful next slice. Items marked **local-only**
exist in the operator's local-scratch prototype today; they would
need a clean re-implementation against the repo-tracked sketches
to be a shipped invariant.

| Item | Status | Notes |
|---|---|---|
| Browser dev UI (`/`, `/story.html`, `/parent.html`) | **Repo-tracked, working** | The proven text path. Phone-on-LAN reproducible from a fresh clone. |
| Device-auth register + chat | **Repo-tracked, working** | `POST /api/devices/register` → `X-Device-Id` / `X-Api-Key` on every `/api/chat`. Enforced by `DeviceAuthMiddleware`. |
| Voice MVP (button → mic → backend → speaker) | **Repo-tracked, bench-only** | `esp32/AregVoiceMvp/`. Buffered playback, no streaming, no retry, no barge-in. C1 scope per CLAUDE.md. |
| Loading UX while AI thinks | **Local-only** | Iterated in the operator's local-scratch sketch. The repo-tracked text sketch only shows static "Thinking..." text; the browser pages show their own per-page spinners. A shipped invariant would mean lifting the local prototype's UX into either the browser pages or a future v2 sketch. |
| TTS / speaker on a non-voice device | **Next recommended** | The voice MVP already does TTS via the backend (`IAudioSynthesisService` → MP3 → I²S → MAX98357A). A "TTS-only, no mic" variant — push a question via the browser, hear the answer on a speaker-attached ESP32 — is the smallest next slice that doesn't require a microphone. |
| On-device microphone for ad-hoc capture | **After TTS** | Once TTS is shipped on a non-voice device, the next slice is wiring an INMP441 (or equivalent) for ad-hoc capture without the voice MVP's full state machine. |
| Physical button as press-to-talk | **After mic** | A tactile push-to-talk button gated by `LOW`-debounced GPIO is the simplest interaction shell; the voice MVP already has the debounce pattern in `AregVoiceMvp.ino` and can be lifted into the new sketch. |
| Enclosure, battery, OTA, wake word, barge-in, retry, TLS | **Out of scope for current iteration** | Each is its own slice. None are in the proven chain today. |

The deliberate order — **TTS → mic → physical button** — is so the
operator always has a working speaker side before introducing
input complexity. A failed mic capture in a TTS-only build still
plays a clear Armenian fallback line; a failed mic capture in a
button-first build only blinks an LED.

## What this doc is NOT

- It is not the authoritative pinout / bench-bring-up guide — that
  lives at `esp32/AregVoiceMvp/README.md`. This doc only covers
  the chain shape.
- It is not a security review. The chain runs HTTP-only on the
  bench LAN; TLS / hardened device-key rotation / parent-side
  device unlink hardening are separate concerns covered (or
  deferred) in CLAUDE.md.
- It is not a promise that any specific firmware behavior is
  shipped. Anything tagged "local-only" above only works on the
  operator's bench, not from a fresh clone.
