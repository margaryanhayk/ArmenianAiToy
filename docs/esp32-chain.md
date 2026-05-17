# ESP32 ↔ Backend chain

Short reference for the end-to-end chat path between an ESP32 (or
browser) and the .NET backend that runs on a dev laptop. This is a
documentation-only file — no firmware is changed here. The
authoritative bench instructions for the voice prototype live at
[`esp32/AregVoiceMvp/README.md`](../esp32/AregVoiceMvp/README.md);
this doc only describes the *chain* and its known quirks.

## What's in the repo

Two firmware sketches and a backend with three browser pages:

| Path | What it is | Status |
|---|---|---|
| `esp32/AregVoiceMvp/AregVoiceMvp.ino` | C1 voice bench prototype (button-to-talk → `/api/chat/audio`). Sends device-auth headers. | **Active** — matches current backend contract. |
| `esp32/ArmenianAiToy/ArmenianAiToy.ino` | Phase-1 text-chat sketch (browser → ESP32 → `/api/chat`). | **Stale.** Does not send `X-Device-Id` / `X-Api-Key`; the backend's `DeviceAuthMiddleware` rejects it with 401. Not part of any current proven flow. |
| `backend/src/ArmenianAiToy.Api/wwwroot/index.html` | Browser dev UI: register a device + send chat messages with the issued device id/key. | Active. |
| `backend/src/ArmenianAiToy.Api/wwwroot/story.html` | Browser story-mode UI (handles `storySessionId` / `selectedChoice` round-trip). | Active. |
| `backend/src/ArmenianAiToy.Api/wwwroot/parent.html` | Parent dashboard (login, linked devices, conversation history, etc.). | Active. |

## Proven chain — browser → backend

Used during development from a phone on the same LAN as the dev
laptop. This is the chain exercised by `wwwroot/index.html` and
`wwwroot/story.html`.

```
Phone (browser)
  │
  │  HTTP  http://<laptop-lan-ip>:5000/
  ▼
Backend (ArmenianAiToy.Api)
  │
  ├── GET  /                            → wwwroot/index.html
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
  (`wwwroot/index.html` / `wwwroot/story.html`) is the
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
