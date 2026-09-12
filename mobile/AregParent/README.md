# Areg Parent app (Phase D)

React Native + Expo (TypeScript) parent app for the Areg toy. It consumes the
existing parent-facing backend API — the same endpoints proven end-to-end on
real hardware during the bench session (see `../../PLATFORM-ARCHITECTURE.txt`).

iOS: blocked. Apple declined this project's Developer Program enrolment
(case 20000124688383, 2026-08-04, no reason given) — with no signing
identity, neither EAS nor a local build can produce an iOS build at all, so
none of the below has ever run on an iPhone. See "TestFlight on your own
iPhone" further down for the full story and the sequence to run if that
ever changes. `wwwroot/parent.html` is the documented iOS fallback (works
in any phone browser, no native module, no signing needed).

## What works on Android

Everything below is implemented and type-checks (`npx tsc --noEmit`), and
runs today in Expo Go / the web preview for everything that isn't Bluetooth.
None of it has been run on an actual Android **build** (dev client or the
release APK from `scripts/build-android.sh`) in this session — see "What was
actually verified" under the APK section below, and "State of the toy" in
the repo root `CLAUDE.md`.

- **Sign in / create account** — `POST /api/parents/login` + `/register`.
- **Your toys** — `GET /api/parents/devices/details`, live **Online/Offline**
  dot (toy heartbeat), Paused/Revoked tags, the fault chip + support code
  (E-101/E-401/E-501…) when something needs attention, the flag-gated usage
  line ("N of M questions today"); pair by code or by pasting a pairing QR's
  decoded text (`/devices/claim`), a short invite code instead
  (`/devices/redeem-invite`), or generate one to share with a second parent
  (`/devices/{id}/invite`); rename (`/name`), revoke/restore (`/revoke`),
  unlink (`DELETE /devices/{id}/link`, 2026-09-12 — factory reset that
  keeps the Device row, the toy can be claimed again), add/remove a child
  profile (`POST`/`DELETE /api/children`).
- **Story library** — freshness line (up to date / syncing / none yet /
  sync failed / crash-looping), per-story listen counts, parent-language
  descriptions (`/api/parents/stories`).
- **Activity** — per toy: Today summary with modes-used-today chips
  (`/conversations/today-summary`), conversation list
  (`/conversations/summary`), full transcript as chat bubbles
  (`/conversations/{id}`), with **▶ Listen** on Areg's spoken replies and
  **▶ Listen / ⬇ Save recording** on the child's own recordings
  (2026-09-11, C2.1/C2.2 — new this slice, native-only via `expo-audio` +
  `expo-sharing`, not available on web); delete a single conversation
  (2026-09-12, `DELETE /conversations/{id}`).
- **Safety** — Flagged view (`/conversations/flagged`) with an "all clear"
  state; tap through to the conversation.
- **Stories heard / games played** — per-toy listen and play history with
  totals, reflection answers in the child's own words.
- **Controls** — pause/resume, the four mode toggles (`/mode-flags`), the
  after-story reflection question toggle (2026-09-11, new this slice,
  `/story-questions`), and bedtime quiet-hours (`/bedtime-window`).
- **Music, story requests, activity/audit feed** — account-wide, not
  per-toy: bedtime tracks, a parent's own custom-story asks with status, and
  the parent's own recent actions.
- **What Areg can do** — the same plain-language capability list as the web
  dashboard.
- **Account** — profile + verification status, send verification, **export my
  data** (downloads JSON on web), change password, log out, delete account.
- JWT in the OS secure store (localStorage on web); session restored on launch.

Source: `App.tsx` (state navigator), `src/api.ts` (typed client),
`src/audio.ts` (conversation-audio fetch/play/save), `src/auth.ts` (token),
`src/config.ts` (backend URL), `src/knownPop.ts` + `src/pairingQr.ts`
(pairing QR PoP handoff into BLE setup), `src/screens/*`.

## Run it

```bash
cd mobile/AregParent
npm install            # first time
npx expo start         # then scan the QR with Expo Go on your phone
```

Point it at your backend by setting the base URL. The **default is the live
server** (`https://armenianaitoy-production.up.railway.app`), so an
unconfigured build still reaches a real backend; set the variable to work
against a bench backend instead:

```bash
# PowerShell
$env:EXPO_PUBLIC_API_BASE_URL = "http://192.168.1.4:5000"; npx expo start
```

The phone and the backend must be on the same Wi-Fi for the dev LAN IP to work.

## Build an installable Android APK without an EAS account

`scripts/build-android.sh` builds a local, real (non-Expo-Go) release APK —
no Expo/EAS account, no cloud build queue. It runs `expo prebuild`, signs
with a **throwaway keystore it generates on the fly** (never committed — see
the script's own comments for why the passphrase is deliberately public),
and runs a plain Gradle build.

```bash
cd mobile/AregParent
export ANDROID_HOME=/path/to/your/android/sdk   # see below
./scripts/build-android.sh
# → android/app/build/outputs/apk/release/app-release.apk
adb install -r android/app/build/outputs/apk/release/app-release.apk
```

Requires locally: Node (already needed to run the app at all), a JDK (17 is
what Android Gradle Plugin documents; the keystore-generation step of this
recipe ran fine under JDK 21 in this session's dev container too — see
"What was actually verified" below, though the Gradle build itself was
never reached there to confirm JDK 21 end to end), and the Android SDK
command-line tools with `ANDROID_HOME` set and at least `platform-tools`
plus the platform/build-tools this Expo SDK targets installed via
`sdkmanager`. The script does not fetch the SDK for you.

**What was actually verified in this repo's cloud dev container (2026-09-11):**
`npm install`, `npx tsc --noEmit`, and `npx expo prebuild --platform
android` all ran clean, and the script's keystore-generation + Gradle
signing-config patch steps were exercised directly against the generated
project. The Gradle build itself (`./gradlew assembleRelease`) was **NOT**
run and no APK was produced — that container's outbound network policy
returns 403 for `dl.google.com`, so the Android SDK cannot be downloaded
there at all. This is a network policy decision outside the script's
control, not a bug in it; run the script on a machine (or CI runner) that
already has the SDK, or one where `dl.google.com` is reachable, to get an
actual APK and its size.

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
#   BLOCKED as of 2026-08-04: Apple declined this project's Developer
#   Program enrolment (case 20000124688383, no reason given). Without an
#   approved account there is no signing identity for EAS to use, so this
#   command cannot succeed today — see "TestFlight on your own iPhone" below.
```

Profiles (`eas.json`):
- **preview** — standalone APK: install and run (includes the Bluetooth module). Best for "just put it on my phone."
- **development** — dev client + live reload (`npx expo start --dev-client`). Best for iterating + Bluetooth testing.
- **production** — store / TestFlight build. Already points at the live
  HTTPS backend.

**Before any store build, two things still need a real account (owner's,
not this session's):**
- `app.json`'s `expo.owner` field was a placeholder EAS account
  (`test111111s-team`) that cannot actually own this project — removed
  rather than left in place, so `eas build`/`submit` fail loudly (an
  ownership mismatch against the logged-in account) instead of silently
  targeting the wrong team. **Set `owner` to the real EAS account slug
  before any store build.**
- `eas.json`'s `submit.production` block is empty (`{}`) — no App Store
  Connect / Google Play credentials configured. `eas submit` will prompt
  interactively the first time; for a repeatable pipeline, fill this in
  with the real account's credentials once they exist.

The backend URL is baked in at build time from `eas.json` →
`env.EXPO_PUBLIC_API_BASE_URL`:

| Profile | URL |
|---|---|
| development / preview | `http://192.168.1.4:5000` (bench LAN — phone must be on the same Wi-Fi, and the address is DHCP so re-check it) |
| production | `https://armenianaitoy-production.up.railway.app` (live) |

## TestFlight on your own iPhone — BLOCKED (Apple declined enrolment, 2026-08-04)

**Corrected 2026-08-16 — this section previously read "Everything below is
already prepared; this is the whole sequence once the Apple Developer
Program enrolment is approved," written before Apple's decision. That
framing is now misleading: Apple declined the enrolment on 2026-08-04 (case
20000124688383, no reason given), and nothing since has changed that. The
steps below remain accurate as *the sequence to run if and when an approved
account exists* — they are not a live path today.**

**Consequence for this app, not just for TestFlight**: without an iOS build,
the Bluetooth Wi-Fi setup screen (`ProvisioningScreen.tsx`, see below)
cannot reach an iPhone at all — Expo Go/web only show the graceful fallback,
and a *dev* build needs the same Apple signing identity `production` does.
BLE Wi-Fi setup on this app is therefore **Android-only** until Apple
reverses course. This is exactly why `wwwroot/parent.html` — not this app —
is the phone surface documented as the "add-to-home-screen" fallback for
iOS in CLAUDE.md § "Parent-Facing Read-Only Monitoring Surface"; that
dashboard has no native-module dependency and works on any phone's browser.

```bash
cd mobile/AregParent
npx eas-cli login
npx eas-cli build --profile production --platform ios
#   EAS prompts for the Apple ID, creates the bundle id com.areg.parent,
#   and generates the signing certificate + provisioning profile for you.
#   ~15-25 min in the cloud. No Mac needed.

npx eas-cli submit --profile production --platform ios --latest
#   uploads the build to App Store Connect -> TestFlight
```

Then in App Store Connect → TestFlight, add your own Apple ID as an
internal tester and install via the TestFlight app on the iPhone.

Already handled so the upload doesn't stall:
- `ios.bundleIdentifier` = `com.areg.parent`.
- `ios.config.usesNonExemptEncryption: false` — answers Apple's
  export-compliance question up front, so every upload doesn't sit
  waiting on a manual reply.
- `appVersionSource: "remote"` + `autoIncrement` in `eas.json` — EAS
  bumps the build number, so re-uploads never collide.
- App icon is a real 1024×1024 PNG (App Store Connect rejects anything
  smaller).
- `updates.url` + `runtimeVersion` — over-the-air JS updates on the
  `production` channel, so small fixes ship without a new build.

Not needed for TestFlight (only for a public App Store release):
privacy policy URL, App Store screenshots, age rating, and the review
submission itself.

## Bluetooth Wi-Fi setup (needs a dev build)

The "Connect to Wi-Fi" screen (Settings → 📶) drives BLE provisioning via
`@orbital-systems/react-native-esp-idf-provisioning`. That's a **native module**,
so it does NOT run in Expo Go or the web preview — it needs an **Expo dev build**.
The code is written (`ProvisioningScreen.tsx`) but **UNVERIFIED on a device**
(this session hardened it further without hardware to test against — see
"What was actually verified" below). **Android-only in practice** — an iOS
dev build needs the same Apple signing identity that TestFlight does, and
Apple has declined this project's Developer Program enrolment (see
"TestFlight on your own iPhone" above).

- In Expo Go / web it shows a graceful fallback ("use the ESP BLE Provisioning
  app, PoP `areg-pair`"); only a dev build activates the real flow. The toy/
  firmware side is already done + verified (B.2/B.3).
- Constants match the firmware: device prefix `Areg`, PoP `areg-pair`, security 1.
- **Android 12+ runtime permissions** (2026-09-11): the native module only
  checks `BLUETOOTH_SCAN`/`BLUETOOTH_CONNECT` (API 31+) or
  `ACCESS_FINE_LOCATION` (below that) — it never asks. The screen now
  requests the right set itself before every scan, since a parent can
  revoke the grant from system Settings between runs; denial shows a
  trilingual explanation instead of a silent failure.
- **Scan timeout + retry**: the underlying library's `searchESPDevices`
  has no timeout of its own — a toy out of range or no longer in setup mode
  used to leave the screen on "Looking for your toy…" forever. A 20s
  timeout now falls back to a distinct "this is taking too long" message;
  the same primary button (back in view once the phase resets) is the
  retry action.
- **PoP from the pairing QR, with a typed fallback**: this app has no
  camera scanner, but a parent can decode the box's QR with any scanner app
  already on their phone and paste the resulting text into the new field
  above the Device ID field on the "Add a toy" screen. `pairingQr.ts`
  parses it leniently — both the pre-factory-pairing `{deviceId, claim}`
  shape (older boxes) and the current `{deviceId, claim, pop}` one (see
  CLAUDE.md "Per-toy BLE PoP + factory station") — and, when a `pop` is
  present, remembers it in-memory only (`knownPop.ts`, never persisted, to
  match the backend's own "never stored" posture on the PoP) so it
  pre-fills the Wi-Fi setup screen's PoP field a few taps later. The typed
  PoP field on that screen is always there regardless, as the fallback.

Make + run the dev build (Android, on this Windows machine):

```bash
cd mobile/AregParent
# Local build (needs Android Studio + SDK + a device/emulator):
npx expo run:android
#   — or cloud build, no toolchain needed:  eas build --profile development -p android
# then, with the dev build installed on the phone:
npx expo start --dev-client
```

iOS needs a Mac or EAS (`eas build --profile development -p ios`) — **and,
as of 2026-08-04, an approved Apple Developer Program account that this
project does not have** (Apple declined the enrolment; see "TestFlight on
your own iPhone" above), so this path is not currently runnable on iOS.
After the Android dev build launches: put the toy in setup mode (hold its
button ~5s at power-on), then Settings → 📶 Connect to Wi-Fi → Search → pick
your network → password → Send.

### Phone-side test steps (a human still has to run these)

None of this has been exercised on real hardware or a real phone — the
steps below are what "verified" would mean, not a claim that it happened.

1. Deny the Bluetooth/location permission prompt on first search → expect
   the trilingual `e_ble_permission` message, not a silent hang; grant it
   from system Settings, tap "Search for my toy" again → expect it to
   proceed normally.
2. Power the toy off (or take it out of BLE range) and search → expect the
   20s `e_ble_timeout` message, not an indefinite spinner; bring the toy
   back into setup mode and tap search again → expect a normal retry.
3. Scan the toy's box QR with any barcode-scanner app, copy the decoded
   text, paste it into the new field above Device ID on "Add a toy" →
   expect Device ID + pairing code to fill in and the "Got it" confirmation
   to show; pair the toy, then open Settings → 📶 → expect the PoP field to
   already be filled in (no retyping the 8-character code from the box).
4. Repeat step 3 with an OLDER box's QR (`{deviceId, claim}`, no `pop`
   field, if one is still around) → expect Device ID + pairing code to
   still fill in, and the PoP field on the Wi-Fi setup screen to be empty
   (typed fallback), not an error.
5. Full happy path end to end: search → connect (PoP) → pick network →
   enter password → Send → "Toy connected to Wi-Fi!" → toy actually joins.

## Still TODO (next slices)

- Per-child profiles + per-child mode overrides (endpoints exist; needs a
  child to exist on the device first).
- A camera-based QR scanner for the claim/pairing flow — today's "paste
  the decoded text from any scanner app" (see "Bluetooth Wi-Fi setup"
  above) avoids adding a new native camera dependency this session could
  not build-verify, but a real in-app scanner would be nicer.
- Navigation library (React Navigation) to replace the hand-rolled state
  navigator as screens grow.
- App icon / splash / store metadata.

## Notes

- iOS builds without a Mac: use **EAS Build** (`eas build -p ios`) — cloud
  build. Still needs an approved Apple Developer Program account, which this
  project does not currently have (Apple declined enrolment 2026-08-04); see
  "TestFlight on your own iPhone" above.
- Endpoints ride plain HTTP today; flip `Security:RequireHttps` on the backend
  and use the HTTPS URL here once TLS (a domain + certificate) is in place.
