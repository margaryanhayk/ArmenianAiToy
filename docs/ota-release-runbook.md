# OTA release runbook

How to cut a firmware release, push it to a toy over the air, confirm the toy
took it, and get back if it didn't.

Written 2026-08-07 for release **1.1.0**. The OTA machinery it drives is the
Proof-2 (backend contract) + Proof-3 (real apply) work bench-verified on real
ESP32-S3 hardware in July 2026 — see `backend/docs/ota-bench-evidence.md`.
This document adds no new OTA code; it is the operating procedure for what
already exists.

---

## 0. Read this before you push anything

**The three things that make this safe to attempt:**

1. **Every refusal happens before a single byte is written to flash.**
   Signature, board, minVersion, downgrade and size are all checked against the
   manifest *first*. A wrong signing key, a wrong board, a stale version — all
   of them end as a logged refusal and an ack. The toy keeps running the
   firmware it has.
2. **A bad image cannot survive a reboot.** The new image is written to the
   *inactive* slot, sha256-verified before finalize, and boots in
   `pending_verify`. It only becomes permanent after it successfully checks in
   with the backend. If it cannot check in within
   `AREG_OTA_CHECKIN_DEADLINE_MS` (**15 minutes** since 1.1.1 — 5 was
   demonstrably too tight in the field, `config.h`), it self-
   invalidates and the bootloader rolls back to the old image automatically.
   **No human action is required for that rollback.**
3. **Nothing is pushed by configuration alone.** Enabling the release only makes
   the backend *offer* it. The toy applies only when an operator enqueues a
   `firmware_update` command for that specific device.

**The one thing that can go wrong quietly:** the update is never *offered*, and
nothing anywhere says so. `updateAvailable:false` is the same response for
"you're up to date" and "your config excludes this device". See § 7.

---

## 1. Release 1.1.0 — the facts

| Field | Value |
|---|---|
| Version | `1.1.0` |
| Build tag | `2026-08-07-release` |
| Board | `areg-s3-n8` (compiled into the image) |
| OTA image | `AregVoiceMvp.ino.bin` |
| Size | `1320608` bytes |
| SHA-256 | `474eb351bc0ccbde60f0eafd65b072b80904b39e0e943fffb226d3e736762b71` |
| Slot usage | 42% of the 3 MB app slot (`0x300000`) |
| Manifest HMAC key compiled in | the rotated key — see § 8 |

Built from `esp32/AregVoiceMvp` with:

```
arduino-cli compile \
  --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" \
  --build-property "compiler.cpp.extra_flags=-DAREG_CONTENT_SYNC_BENCH" \
  --output-dir "esp32/AregVoiceMvp/release/1.1.0" \
  "esp32/AregVoiceMvp"
```

**What is in it:** welcome flow, story serial ordering ("Tsivik plays in
order"), story pauses, and cloud→SD content sync (stories *and* the 92 game
clips). **What is not in it:** the three offline games — see § 11.

### `AregVoiceMvp.ino.bin` vs `AregVoiceMvp.ino.merged.bin`

The build produces both. They are not interchangeable.

- **`AregVoiceMvp.ino.bin`** (1.3 MB) — app-only. **This is the OTA image.**
- **`AregVoiceMvp.ino.merged.bin`** (8 MB) — bootloader + partition table +
  app, for a cable flash with esptool at offset `0x0`. Serving this over OTA
  writes a whole flash layout into a 3 MB app slot; it fails the size gate
  (`AREG_OTA_MAX_IMAGE_BYTES` = 3,145,728) and, if it didn't, would produce an
  unbootable slot.

---

## 2. Cut a release

1. **Set the identity.** In `esp32/AregVoiceMvp/config.h` (gitignored, local):

   ```c
   #define AREG_FW_VERSION        "1.2.0"
   #define AREG_FW_BUILD          "2026-09-01-release"
   #define AREG_MANIFEST_HMAC_KEY "<the fleet key — see § 8>"
   ```

   **Do not try to pass the version as a `-D` build flag.** `config.h` uses a
   plain `#define` (not `#ifndef`) and is included first, so it overrides any
   command-line define; the build would silently produce the old version. The
   header default in `ota_foundation.h` is likewise dead whenever `config.h`
   defines it. Editing `config.h` is the only seam that works.

2. **Build** with the command in § 1 (new `--output-dir`).

3. **Confirm the image really carries the new identity** — cheap, and catches
   the override trap above:

   ```
   python -c "import re;d=open('release/1.2.1/AregVoiceMvp.ino.bin','rb').read();print(sorted(set(m.decode() for m in re.findall(rb'1\.[0-9]\.[0-9]',d))))"
   ```

4. **Run the release gate. This is not optional.**

   ```
   python tools/firmware/check_release_image.py \
     esp32/AregVoiceMvp/release/1.2.1/AregVoiceMvp.ino.bin \
     --expect-version 1.2.1 --forbid-version 1.2.0
   ```

   **`python`, not `python3`.** On the Windows release machine `python3` is not
   a real interpreter — it is the Microsoft Store stub, which prints
   "Python was not found" and exits 49. That is a *skipped gate wearing the
   costume of a failed one*, on the one step this document calls not optional.
   `python` and `py` both work. Step 3 above already uses `python`.

   It exits non-zero and refuses the image if it carries a real device API key
   (`dtk_<32 hex>`) or a device id, if the expected version is missing, if the
   old version is still inside, or if you pointed it at the 8 MB
   `.merged.bin`. No toolchain needed — plain Python, so there is no excuse
   to skip it.

   A pass looks like this (verified against the field 1.2.0 image, 2026-08-14):

   ```
   size    1,297,904 B (41.3% of the 3,145,728 B OTA slot)
     ok    version 1.2.0 present

   PASS - no credentials found; safe to stage.
   ```

   **This step exists because step 1 was not followed.** On 2026-08-13 a 1.2.1
   image was built and pushed carrying the owner's real device id and API key.
   It was caught by inspection before it shipped; had it shipped, every
   factory-fresh toy that installed it would have authenticated to the backend
   as that one toy, because `device_creds` falls back to the compiled values
   when NVS is empty. The rule against this was already in this repo, in
   prose, and prose is what got skipped. If the gate refuses your image,
   restore the `AREG_DEVICE_ID` / `AREG_DEVICE_API_KEY` placeholders from
   `config.h.example`, rebuild, and revoke the leaked key.

5. **Hash and size** the app-only image:

   ```
   sha256sum release/1.2.1/AregVoiceMvp.ino.bin
   stat -c %s  release/1.2.1/AregVoiceMvp.ino.bin
   ```

6. **Stage it into the backend image — over `areg-current.bin`, keeping that
   exact name:**

   ```
   cp release/1.2.1/AregVoiceMvp.ino.bin \
      backend/src/ArmenianAiToy.Api/firmware/areg-current.bin
   ```

   **Do not stage under a versioned filename** (`areg-1.2.1.bin`). Railway's
   `FirmwareUpdate__ImagePath` points at `/app/firmware/areg-current.bin` and
   is set once, never per release (§ 3). A versioned copy leaves
   `areg-current.bin` holding the OLD bytes while `appsettings.json` advertises
   the NEW sha256 — so every toy downloads the previous firmware, fails the
   sha check, and refuses to flash. That is precisely the failure § 3 warns
   about, and until 2026-08-14 this step caused it: it said `areg-1.2.0.bin`.

   No image is ever lost by overwriting: the previous release stays in git
   history, and `git show <prev-commit>:backend/src/ArmenianAiToy.Api/firmware/areg-current.bin`
   recovers it.

   `ArmenianAiToy.Api.csproj` copies `firmware\*.bin` to the build output, so
   the file lands at `/app/firmware/` in the container. **The `.bin` must be
   committed** — Railway builds the image from the repo, and a file that is not
   in git is not in the container. (`.dockerignore` does not exclude it;
   `**/bin/` only matches directories literally named `bin`.)

7. **Update `appsettings.json`** → `FirmwareUpdate`: `LatestVersion`,
   `SizeBytes`, `Sha256`. Leave `Enabled:false`, `ImagePath:""`,
   `SigningKey:""` — those are operator/env concerns (§ 3).

8. **Deploy** (push → Railway rebuilds). Deploying changes nothing on its own:
   `Enabled` is still false.

---

## 3. Railway environment variables

Railway uses `__` for config nesting.

| Variable | Value | Notes |
|---|---|---|
| `FirmwareUpdate__Enabled` | `true` | The go-live switch. Deliberately not in the repo. |
| `FirmwareUpdate__ImagePath` | `/app/firmware/areg-current.bin` | **Must be absolute.** The endpoint 404s on a relative path. The shipped filename is deliberately NOT versioned, so this is set once and never again: a forgotten per-release edit serves the OLD image under the NEW manifest's sha, and the device rejects it. |
| `FirmwareUpdate__SigningKey` | see § 8 | Secret. Never committed. |

Already set from earlier work, needed here: `Internal__AdminToken` (or
`Internal__Operators`), without which the enqueue endpoint in § 5 is a 404.

To take the fleet out of update mode, set `FirmwareUpdate__Enabled=false`. The
manifest immediately returns no-update; in-flight downloads are unaffected.

---

## 4. Pre-flight: can *this* toy actually take an OTA?

Do this once, before the first real release. As of 2026-08-07 the owner's toy
("First Toy") reports firmware `1.0.1`, `lastOtaStatus: idle`, `otaHealth: ok`,
and is heartbeating — but its `boardModel`, `firmwareBuild` and
`firmwareReportedAt` are all **null**, meaning it has never sent an
OTA-identity heartbeat. So we know its version but we have **not** observed it
using the OTA path at all.

Two things are genuinely unknown until tested:

- **Does the flashed image contain the OTA *apply* client, or only the Proof-2
  skeleton?** Both exist in the history. They are trivially distinguishable:

  | What you see after enqueuing `firmware_update` | Meaning |
  |---|---|
  | ack `ok`, result `{"status":"manifest_checked"}`, no reboot, version stays `1.0.1` | **Skeleton only.** It checks the manifest and deliberately does not apply. This toy needs one cable flash. |
  | toy goes quiet ~1–3 min, then reappears reporting `1.1.0` | Apply client present, update took. |
  | ack `failed` with a named error (`manifest_sig_invalid`, `no_downgrade`, …) | Apply client present, a gate refused. Fix the cause and re-enqueue. |
  | no ack at all after ~15 min | Not polling — offline, revoked, or no OTA client. |

- **Which manifest HMAC key was compiled into it?** It was flashed in July and
  `config.h` was lost and reconstructed on 2026-08-06, so we cannot read it
  off disk with confidence. See § 8.

---

## 5. Push the update to one toy

1. Confirm the toy is being **offered** the update. From the operator console
   (`admin.html` → Devices) check it is online and reporting a version older
   than `LatestVersion`. If you want to see the manifest itself, use the toy's
   own credentials:

   ```
   curl -s https://<host>/api/devices/firmware-manifest \
     -H "X-Device-Id: <id>" -H "X-Api-Key: <key>"
   ```

   `updateAvailable:true` is the go/no-go. If it is `false`, stop and read § 7.

2. Enqueue the command (operator-gated; fail-closed 404 if the admin token is
   unset):

   ```
   curl -s -X POST https://<host>/api/internal/devices/<deviceId>/commands \
     -H "Authorization: Bearer <admin token>" \
     -H "Content-Type: application/json" \
     -d '{"type":"firmware_update","ttlSeconds":3600}'
   ```

3. Wait. The toy polls on its heartbeat cadence (~60 s), downloads ~1.3 MB,
   verifies, flashes and reboots. Budget 2–5 minutes on a normal home Wi-Fi.

**Do it while the toy is idle and you are in the room.** Command polling only
runs in the IDLE branch — never during a story or a voice turn — so a busy toy
just picks it up later.

---

## 6. Confirm the toy took it

In order of strength:

1. **The device row.** `admin.html` → Devices, or
   `GET /api/internal/devices`. Success looks like `firmwareVersion: 1.1.0`,
   `lastOtaStatus: confirmed`, `otaHealth: ok`, and a fresh `lastSeenAt`.
   `confirmed` is the meaningful word: it means the new image checked in with
   the backend and cancelled its own rollback.
2. **The command ack.** The `firmware_update` command should be `Acked` with
   result `ok` and `ackFirmwareVersion: 1.1.0`. The ack is sent by the *new*
   image after it boots — that is the design, and it is why there is no ack
   before the reboot.
3. **Behaviour.** Power-cycle the toy: 1.1.0 greets on boot (welcome flow) and
   offers a story by name.

`lastOtaStatus` is sticky — it is the device's verbatim *last attempt* outcome
and the toy re-reports it every heartbeat, so it will not clear itself. A
device showing `failed:...` but a fresh `lastSeenAt` is a healthy device that
had a failed attempt earlier; `otaHealth` is the derived current-health field
and is the one to trust for "is this toy okay right now".

---

## 7. When `updateAvailable` is false and you expected true

The gates, in the order the backend applies them:

| Gate | Silent-failure cause |
|---|---|
| `Enabled` | env var not set / not redeployed |
| `LatestVersion` non-empty and `Url` non-empty | one of them blank ⇒ no offer, no error |
| **Board** | `FirmwareUpdate:BoardModel` is set **and** the device's stored `BoardModel` differs — **including when the device's is null** |
| Version | device is not strictly older than `LatestVersion` |

**The board gate is the trap that would have hit this rollout**, which is why
`BoardModel` ships empty. Verified locally on 2026-08-07: with
`BoardModel=areg-s3-n8` configured and a device that reports no board (exactly
the owner's toy), the manifest returns `updateAvailable:false` with no error
and no log line. Only pin `BoardModel` once toys actually report one *and*
there is more than one board to protect.

---

## 8. Signing key rotation — the ordering trap

The device decides whether to verify, based on **its own compiled key**:

- Device key empty → it logs `signature check SKIPPED` and applies whatever the
  server sent. The server's key is irrelevant.
- Device key set → it verifies. The server **must** sign with that same key, or
  the toy refuses with `manifest_sig_invalid`.

So the key the server must use is the key in the **old** firmware — the one
being replaced — not the one in the new image.

**For the 1.0.1 → 1.1.0 hop:** set
`FirmwareUpdate__SigningKey=bench-manifest-hmac-key-1`. That is the bench key
recorded in the `config.h` OTA block, which is dated 2026-07-03 and pairs it
with `AREG_FW_VERSION "1.0.1"` — exactly the version the live toy reports — so
it is the highest-probability match. It is a guess, but a cheap and safe one:
if it is wrong the toy acks `manifest_sig_invalid` and nothing is flashed.

If it does refuse, try in this order, re-enqueuing after each change:

1. `FirmwareUpdate__SigningKey` unset — succeeds if the toy has no key.
2. Any other candidate key from an older `config.h`.
3. Give up and cable-flash 1.1.0. One flash and the problem is gone forever,
   because from 1.1.0 onward the key is known.

**Release 1.1.0 embeds a new, strong key** (32 random bytes; it is in the local
`config.h` and was handed to the owner separately — it is not in this repo).
So once the toy reports `1.1.0`:

> Set `FirmwareUpdate__SigningKey` to the **new** key. Every release after
> 1.1.0 is signed with it.

Doing that flip too early only means a still-1.0.1 toy stops being able to
update — recoverable, not destructive. Doing it too late means the *next*
release refuses; also recoverable.

Honest limit: the HMAC key sits in plaintext inside the firmware binary.
It defends against a tampered or spoofed manifest on the network. It does not
defend against someone who has the `.bin`. Real image signing is Secure Boot
v2, which is a separate, later, deliberate step.

---

## 9. Rollback

**Automatic, and it is the main one.** A new image that boots but cannot check
in with the backend within 15 minutes self-invalidates and the bootloader
returns to the previous image. Power-cycling during that window does not
defeat it. The old image then notices the version mismatch and acks
`failed/rollback_no_checkin`. You do not have to do anything.

**Stop the bleeding for everything else:**

```
FirmwareUpdate__Enabled=false      # nothing more is offered
```

**Un-ship a bad release that toys are already running.** There is no downgrade
path over the air — `compare_semver(new, running) <= 0` is refused
unconditionally, on purpose, and there is no `allowDowngrade` flag in this
slice. To move a toy off a bad-but-working 1.1.0 you must either:

- ship **1.1.1** with the fix (the normal answer — cut it as in § 2), or
- cable-flash `AregVoiceMvp.ino.merged.bin` with esptool at `0x0`.

A toy that is bricked hard enough not to boot at all can only be recovered by
cable. That is the failure this whole design is built to avoid, and the reason
the check-in gate exists.

---

## 10. What is proven, and what is not

**Proven on real hardware (July 2026, `backend/docs/ota-bench-evidence.md`):**
happy-path 1.0.0 → 1.0.1 including the observed `pending_verify` state;
bad-sha256 refusal (full download, no reboot, no brick); wrong-board gating.

**Proven locally on 2026-08-07 (backend side of *this* release, verbatim
transcript in the slice report):** manifest offers 1.1.0 with the correct size
and sha256 to a device shaped exactly like the owner's toy (version-only, no
board reported); the manifest signature recomputes byte-for-byte against the
canonical string the device rebuilds; `GET /api/devices/firmware-image` streams
1,320,608 bytes whose sha256 equals both the manifest and the built `.bin`;
same-version and wrong-board requests return no-update; unauthenticated and
wrong-key requests to the image endpoint return 401.

**Not proven, and worth knowing:**

- **This release has never run on hardware.** 1.1.0 has not been flashed or
  booted on the toy. It compiles and it is the same code the July bench runs
  exercised, but "compiles" is not "works".
- **The owner's toy has never been observed doing an OTA.** Its
  `firmwareReportedAt` is null (§ 4).
- **Rollback has not been exercised on hardware.** The poison/dead-backend
  rollback test and the corrupted-image test were deliberately not run in July.
  The mechanism is the ESP-IDF native one
  (`CONFIG_BOOTLOADER_APP_ROLLBACK_ENABLE=y`, verified present), not something
  written here — but it is untested in this product.
- **Stage-B TLS for the OTA transport has not been done.** `ota_http_begin()`
  is the single seam where a pinned CA would go.
- The content-sync half of this release ships behind a flag named
  `-DAREG_CONTENT_SYNC_BENCH` (§ 11).

---

## 11. Known rough edges

- **`AREG_CONTENT_SYNC_BENCH` is load-bearing, and its name lies.** Cloud→SD
  sync — stories *and* the 92 game clips — compiles to zero bytes without it.
  A build without this flag downloads nothing, so it must be in every release
  until the flag is promoted to default. Worth a follow-up slice: a flag called
  `_BENCH` that ships in production is a trap for whoever cuts the next release.
- **The three offline games cannot ship yet.** Two independent blockers:
  `offline_games_tick()` starts a game **automatically 30 s after every boot**
  with the game chosen at *build* time (`AREG_OFFLINE_GAMES_PICK`), so a child
  would get an unprompted mind-reader session on every power-on and could never
  reach the other two; and the GREEN/RED answer buttons the games need
  (`AREG_PIN_BUTTON_YES` / `_NO`) are commented out in `config.h`, so with the
  flag on they would be a logged no-op anyway. The blocker is the open
  "how does a child pick a game" decision already recorded as a TODO in
  `offline_games.cpp` — not the games themselves. **The game *clips* do sync**
  in 1.1.0, so the content half is ready when the input decision lands.
- **`FirmwareUpdate:ImagePath` must be absolute** while every other content
  path in the product (`ContentSync:AudioRoot`, story audio, game clips) is
  app-relative. That asymmetry is why the image path can only live in an env
  var and cannot be expressed in committed config, and it means the path has to
  be re-typed for every deployment. Making it accept an app-relative path, like
  `AudioRoot` does, would remove one hand-typed absolute path from the release
  procedure. Not changed here — it is a behaviour change to a shipped endpoint.
- **The version cannot be overridden from the build command line** because
  `config.h` uses a plain `#define` (§ 2). Switching those four lines to
  `#ifndef` would make `-D` work and make CI-built releases possible.

## 11. Field log

**2026-08-07 — 1.1.0, first real OTA to the owner's toy: ROLLED BACK (by design).**
Command polled 41 s after enqueue, then silence (correct: no ack before
reboot), then the OLD image acked `failed / rollback_no_checkin`,
`{"status":"rolled_back","attemptedVersion":"1.1.0"}` at +5m41s.

Proven for the first time on real hardware: the toy carries the REAL apply
client, the download+flash+reboot path works, and the bootloader auto-rollback
works. The toy was never bricked and never left the network.

Root cause (best evidence): the new image never got to its check-in inside the
old 5-minute deadline. `handle_welcome_flow()` runs at the END of `setup()` and
on the ordinary path plays a whole 3-4 minute story WITHOUT returning to
`loop()` — so on the one boot that decides confirm-vs-rollback, the deadline
could expire before the first attempt. Content sync (which downloads the whole
library inside a single `loop()` iteration) is the second candidate.

Fixed in 1.1.1: `setup()` runs an early check-in when an OTA outcome is
pending and skips the greeting for that one boot; `story_report_tick()` and
`content_sync_tick()` are held while an outcome is pending; deadline raised to
15 minutes; and both acks now carry a `bootDiag` block (`rst` =
`esp_reset_reason()`, uptime, heap, wifi, rssi, sd, boots) so the NEXT failure
is diagnosable without a cable — a reset reason of 4/5/6/7 means a panic or
watchdog, i.e. look at the new code, not at the timing.

**2026-08-07 — 1.1.2: OVER-THE-AIR UPDATE PROVEN END TO END.**

```
status  Acked | result ok | ackFirmwareVersion 1.1.2
{"status":"ota_applied","version":"1.1.2","partition":"app1",
 "bootDiag":{"rst":3,"up":4,"heap":124228,"wifi":3,"rssi":-43,"sd":1,"boots":1}}
device fw 1.1.2 | lastOtaStatus confirmed | otaHealth ok
```

The toy downloaded, verified, flashed into app1, rebooted, checked in
within 4 seconds and marked itself valid. No cable involved.

Two real faults were cleared to get here, both worth remembering:

1. **401, mistaken for a crash.** Three earlier rollbacks were the new
   image failing to AUTHENTICATE, not failing to boot. The device
   identity lived only in the compile-time config (`config.h`,
   gitignored), which had been restored from a stale build cache. Fixed
   by burning the identity into NVS (`AREG_PROVISION_IDENTITY_ONCE`) —
   `device_creds` is NVS-first, so images now ship with PLACEHOLDER
   credentials and carry no secret. An OTA image reaches every toy; it
   must never contain one toy's identity.
2. **Signature mismatch, working as designed.** The first signed
   attempt was REFUSED (`manifest signature invalid`) because the
   server's `FirmwareUpdate__SigningKey` did not match the
   `AREG_MANIFEST_HMAC_KEY` compiled into the running image. The toy
   refused before downloading anything. **Rule: during a key rotation,
   sign with the key in the firmware that must ACCEPT the update — the
   OLD one — and only switch after the new image reports in.**

Content sync on the same boot: 8 stories already current, 43/43 voice
clips present, game clips downloading with per-clip sha256 verified.
