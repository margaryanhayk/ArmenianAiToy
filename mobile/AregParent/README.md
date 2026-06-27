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

## Build a real app on your phone (EAS — no Mac needed for Android)

The browser/Expo Go preview can't run native modules (Bluetooth) and Expo Go
can hit SDK-version mismatches. The real way to get the app on a phone is an
**EAS cloud build** (`eas.json` is configured here). Runs from Windows.

```bash
cd mobile/AregParent
npx eas-cli login          # free Expo account (sign up at expo.dev)
npx eas-cli init           # links this app to your Expo project (writes projectId)

# Android — easiest, builds in the cloud, gives you an installable .apk:
npx eas-cli build --profile preview --platform android
#   → ~10–15 min → install the .apk on an Android phone → open it. Bluetooth works.

# iPhone — needs an Apple Developer account ($99/yr, Apple's rule). No Mac needed:
npx eas-cli build --profile development --platform ios
#   → EAS walks you through Apple credentials + registering your device.
```

Profiles (`eas.json`):
- **preview** — standalone APK: install and run (includes the Bluetooth module). Best for "just put it on my phone."
- **development** — dev client + live reload (`npx expo start --dev-client`). Best for iterating + Bluetooth testing.
- **production** — store build; set its `EXPO_PUBLIC_API_BASE_URL` to your HTTPS backend.

The backend URL is baked in at build time from `eas.json` → `env.EXPO_PUBLIC_API_BASE_URL`
(currently the dev LAN IP `http://192.168.1.4:5000`). The phone must be on the
same Wi-Fi as that backend for it to connect; switch to your public HTTPS URL
for a real release.

## Bluetooth Wi-Fi setup (needs a dev build)

The "Connect to Wi-Fi" screen (Settings → 📶) drives BLE provisioning via
`@orbital-systems/react-native-esp-idf-provisioning`. That's a **native module**,
so it does NOT run in Expo Go or the web preview — it needs an **Expo dev build**.
The code is written (`ProvisioningScreen.tsx`) but **UNVERIFIED on a device**.

- In Expo Go / web it shows a graceful fallback ("use the ESP BLE Provisioning
  app, PoP `areg-pair`"); only a dev build activates the real flow. The toy/
  firmware side is already done + verified (B.2/B.3).
- Constants match the firmware: device prefix `Areg`, PoP `areg-pair`, security 1.

Make + run the dev build (Android, on this Windows machine):

```bash
cd mobile/AregParent
# Local build (needs Android Studio + SDK + a device/emulator):
npx expo run:android
#   — or cloud build, no toolchain needed:  eas build --profile development -p android
# then, with the dev build installed on the phone:
npx expo start --dev-client
```

iOS needs a Mac or EAS (`eas build --profile development -p ios`). After it
launches: put the toy in setup mode (hold its button ~5s at power-on), then
Settings → 📶 Connect to Wi-Fi → Search → pick your network → password → Send.

## Still TODO (next slices)

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
