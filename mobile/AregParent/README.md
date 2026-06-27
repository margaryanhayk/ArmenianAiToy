# Areg Parent app (Phase D)

React Native + Expo (TypeScript) parent app for the Areg toy. It consumes the
existing parent-facing backend API — the same endpoints proven end-to-end on
real hardware during the bench session (see `../../PLATFORM-ARCHITECTURE.txt`).

## What works

- **Sign in / create account** — `POST /api/parents/login` + `/register`.
- **Your toys** — `GET /api/parents/devices/details`, live **Online/Offline**
  dot (toy heartbeat), Paused/Revoked tags; pair by code
  (`/devices/claim`), rename (`/name`), revoke/restore (`/revoke`).
- **Activity** — per toy: Today summary (`/conversations/today-summary`),
  conversation list (`/conversations/summary`), full transcript as chat
  bubbles (`/conversations/{id}`).
- **Safety** — Flagged view (`/conversations/flagged`) with an "all clear"
  state; tap through to the conversation.
- **Controls** — pause/resume, the four mode toggles
  (`/mode-flags`), and bedtime quiet-hours (`/bedtime-window`).
- **Account** — profile + verification status, send verification, **export my
  data** (downloads JSON on web), change password, log out, delete account.
- JWT in the OS secure store (localStorage on web); session restored on launch.

Source: `App.tsx` (state navigator), `src/api.ts` (typed client),
`src/auth.ts` (token), `src/config.ts` (backend URL), `src/screens/*`.

## Run it

```bash
cd mobile/AregParent
npm install            # first time
npx expo start         # then scan the QR with Expo Go on your phone
```

Point it at your backend by setting the base URL (defaults to the dev LAN IP):

```bash
# PowerShell
$env:EXPO_PUBLIC_API_BASE_URL = "http://192.168.1.4:5000"; npx expo start
```

The phone and the backend must be on the same Wi-Fi for the dev LAN IP to work.

## Still TODO (next slices)

- **Bluetooth Wi-Fi setup screen** — the "connect the toy to Wi-Fi" flow. This
  needs a native module (Espressif provisioning, e.g.
  `react-native-esp-idf-provisioning`), so it requires an **Expo dev build**
  (not Expo Go) and on-device testing. The toy side is already done + verified
  (firmware B.2/B.3). Until then, set up Wi-Fi with the **ESP BLE Provisioning**
  app (PoP `areg-pair`).
- Per-child profiles + per-child mode overrides (endpoints exist; needs a
  child to exist on the device first).
- Assistant-audio replay in the transcript (`/messages/{id}/audio`).
- Navigation library (React Navigation) to replace the hand-rolled state
  navigator as screens grow.
- App icon / splash / store metadata.

## Notes

- iOS builds without a Mac: use **EAS Build** (`eas build -p ios`) — cloud build.
- Endpoints ride plain HTTP today; flip `Security:RequireHttps` on the backend
  and use the HTTPS URL here once TLS (a domain + certificate) is in place.
