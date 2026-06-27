# Areg Parent app (Phase D)

React Native + Expo (TypeScript) parent app for the Areg toy. It consumes the
existing parent-facing backend API — the same endpoints proven end-to-end on
real hardware during the bench session (see `../../PLATFORM-ARCHITECTURE.txt`).

## What works in this first slice

- **Sign in / create account** — `POST /api/parents/login` + `/register`.
- **Your toys** — `GET /api/parents/devices/details`, with a live
  **Online/Offline** dot (from the toy heartbeat), plus Paused/Revoked tags.
- **Add a toy** — pair by its single-use code: `POST /api/parents/devices/claim`.
- **Rename** a toy — `PUT /api/parents/devices/{id}/name`.
- **Revoke / restore** a toy (kill-switch) — `PUT /api/parents/devices/{id}/revoke`.
- JWT kept in the OS secure store; session restored on launch.

Source: `App.tsx` (login ↔ devices), `src/api.ts` (typed client),
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
- Activity / monitoring screens (conversations, today summary, flagged,
  assistant-audio replay) — all endpoints already exist.
- Per-child profiles + mode overrides; bedtime; pause; account/export.
- Navigation library (React Navigation) once there are more than two screens.
- App icon / splash / store metadata.

## Notes

- iOS builds without a Mac: use **EAS Build** (`eas build -p ios`) — cloud build.
- Endpoints ride plain HTTP today; flip `Security:RequireHttps` on the backend
  and use the HTTPS URL here once TLS (a domain + certificate) is in place.
