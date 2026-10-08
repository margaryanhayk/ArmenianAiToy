# Firmware security -- locking the toy's chip (ESP32-S3)

Status (2026-10-08): **implemented in the repo, emulator-verified, NOT yet
run on real hardware.** Secure Boot V2, flash encryption (release mode),
NVS encryption, JTAG off and ROM download mode off, plus the firmware,
build, signing, release-gate and factory-station changes that go with them.
Nothing has been flashed and no real eFuse has been burned -- there is no
hardware in the build container. The hardware pilot (section 12) is the
next step and is the owner's.

Closes review items C088 (no flash encryption / Secure Boot), C087 (fleet
HMAC key readable over USB), C150 (shared BLE PoP "areg-pair"); answers N031
(core version); C089 (single CA) is closed in code (five root CAs, section
4). The second review round (same day) is applied: the TLS trust set, the
half-secured-toy guard, https-only release URLs, legacy-store mode for toys
on the old partition table, the inactive-slot retirement, the pilot gate at
the station, offline-only signing and build provenance. The third review
round (same day) is applied too: no plain-TCP audio stream in a release
image, a fresh OTA image stays PENDING_VERIFY until its check-in, a failed
BLE setup purges the Wi-Fi password at once, the station never boots a
half-burned chip, confirms steps 6+7 once and re-reads the MAC before every
burn, the bootloader / partition table / otadata that get frozen are pinned
to `RELEASED.md` (APPROVED) and Arduino's `boot_app0`, a fail-closed backend
gate keeps a locked-fleet image away from field toys, the ESP-IDF component
manager is off and the IDF checkout must be pristine, and the crash-dump
partition is kept (encrypted). **Still the owner's, before the first
locked toy:** make the repository private, move the served image off git,
decide anti-rollback and the crash-dump partition, set
`FirmwareUpdate:BoardModel`, run the hardware pilot, mark the bootloader
row APPROVED (sections 11-12).

---

## 1. What this protects, from whom

**A buyer or second-hand owner with a USB cable** (the owner's worry: "I
don't want someone who buys my toy to get my codes or modify it"). Before
this change `esptool read-flash` gave anyone the whole firmware, the toy's
device API key, the family's Wi-Fi password (twice), the BLE pairing code
and the fleet-wide OTA manifest key, and `esptool write-flash` let them run
their own firmware. **On a locked toy none of that works:** ROM download
mode is permanently off (esptool cannot even connect), JTAG is off, the
flash is ciphertext under a per-device key nobody holds, and the chip only
boots images signed by the owner's key.

**Someone who opens the case and reads the flash chip with a clip**: gets
ciphertext only. App, bootloader, partition table and otadata are
flash-encrypted (XTS-AES-256, per-device key, read-protected in eFuse); the
app store `nvs_sec` is NVS-encrypted with keys derived inside the chip's
HMAC peripheral from a read-protected eFuse key. They can write garbage
(denial of service) but cannot forge code (Secure Boot) or forge `nvs_sec`
entries (tampered / plaintext / wrong-key images read as "not found", never
as garbage -- QEMU cases F-I).

**A well-equipped lab** (fault injection, side channels): can probably
still open ONE toy (section 13). Per-device keys keep that to one toy.

**The network** (unchanged, but strengthened): TLS against five pinned root
CAs (three CA organisations, so a CA move cannot strand a locked toy), https
only in a release image, the device API key, the manifest HMAC -- and now
even a full backend/TLS compromise cannot make a toy run unsigned code.

**What does NOT protect "the code" today: the repository is public.** The
firmware source, this design and every committed firmware image (with the
manifest HMAC key inside) are world-readable on GitHub regardless of any
chip setting. Making the repository private is the owner's step (section
11).

## 2. The decisions

| Topic | Decision |
|---|---|
| Secure Boot | V2, RSA-3072-PSS (the only S3 scheme). **Two** key digests burned (primary slot 0, offline backup slot 1), **slot 2 revoked at the factory**. Bootloader signed by BOTH keys; apps by the primary only. Aggressive revoke OFF. |
| Flash encryption | Release mode, XTS-AES-256, **host-generated per-device key**, images **pre-encrypted on the station** and written to a BLANK chip before any eFuse is burned. Key shredded after verification -- no escrow. |
| NVS encryption | HMAC scheme (eFuse key `HMAC_UP` in BLOCK_KEY4) on a dedicated `nvs_sec` partition. ALL app state lives there. The default `nvs` is ESP-IDF scratch and is physically erased after every Wi-Fi provisioning. |
| Debug / download | Pad JTAG + USB-JTAG hard-disabled; USB serial console kept (output only -- a test pins that the firmware never reads it); ROM download mode **permanently disabled** as the very last factory step. |
| OTA | Server hosts SIGNED images. The toy verifies the signature BEFORE switching boot partition; the bootloader verifies again every boot. A signed in-image version marker blocks a downgrade OVER THE AIR. Manifest HMAC kept (not load-bearing). Anti-rollback eFuse OFF in the candidate bootloader -- an **owner decision before the first lock** (section 11): without it, a flash clip can still boot an OLDER owner-signed release left in flash (erase otadata; the bootloader then "tries OTA 0"). The app therefore erases the older image's first sector once an update is confirmed (RELEASE only, `ota_slot_rules.h`). |
| TLS trust | Five root CAs (ISRG Root X1/X2, GTS Root R1/R4, DigiCert Global Root G2; three organisations). A RELEASE build `#error`s below 3 (`AREG_CA_ANCHOR_COUNT`) and the release gate counts the PEM blocks in the image. |
| Factory window | A RELEASE image stays OFFLINE -- no backend call, no BLE Wi-Fi setup -- until ROM download mode is disabled (step 9). A toy that escapes the station half-way (Secure Boot + flash encryption on, download mode open: RAM code over the cable could use its HMAC_UP key to decrypt `nvs_sec`) is visibly dead and never receives a family's Wi-Fi. |
| Partitions | Table stays at 0x8000; 2 x 3 MB OTA slots kept; `nvs_sec` (64 KB) added where `coredump` was; `coredump` moved 64 KB down and flagged `encrypted` (owner decision before the first lock, section 11); `spiffs` 64 KB smaller and flagged `encrypted`. Existing toys need ONE cable conversion; their NVS is lost (SD card kept). |
| Toolchain | arduino-esp32 **3.3.8** pinned (the release image `#error`s on any other core); release bootloader from **ESP-IDF v5.5.4 @ 735507283d** (the commit 3.3.8's libs were built from); compiler `esp-14.2.0_20260121` and IDF's own esptool 4.12.0 (it runs elf2image) asserted by every build (`deps.lock`); esptool **5.2.0** (signing, factory), esp-idf-nvs-partition-gen **0.3.0**. |
| Canonical release build | `tools/firmware/build_idf.sh release` (ESP-IDF project, pinned deps re-verified byte for byte, byte-reproducible). It stops UNSIGNED by default, refuses a dirty tree and a manifest HMAC key found in any committed image. arduino-cli stays the bench build and a supported release fallback (`tools/firmware/build_release.py`, which checks the installed library versions + source hashes against `deps.lock`). |

## 3. The two security profiles

| | DEV (default) | RELEASE |
|---|---|---|
| Who | bench boards | every toy that leaves the house |
| How | normal Arduino upload, or `build_idf.sh dev` | `build_idf.sh release` (or `build_release.py`), signed offline, factory station |
| Bootloader | stock (Arduino / IDF dev) | `esp32/bootloader-release` (SBv2 + FE release, logs OFF), dual-signed |
| eFuses | untouched | burned by the station (section 8) |
| `nvs_sec` | plaintext; on a board still on the pre-2026-10-08 table (no `nvs_sec`): **legacy mode**, app state stays in the default `nvs` (`store=legacy`, no purge) | NVS-encrypted (HMAC); no `nvs_sec` = store refused |
| Bench flags | allowed | `#error` |
| Fallback identity / Wi-Fi from `config.h` | yes | none (no identity = offline SD-only toy, logged) |
| Shared PoP `areg-pair` | yes (no per-device PoP) | never compiled; no per-device PoP = BLE setup refused |
| Console | as before | SSID, backend URL and stream URLs redacted |
| URLs | http or https | https only: `areg_http_begin` refuses any other URL before a request (device key never in cleartext) |
| Network audio streams | story stream (SD fallback) + Q&A URL stream | **none**: ESP8266Audio's `AudioFileSourceHTTPStream` holds a plain `NetworkClient` -- `HTTPClient::begin(client, "https://...")` only sets port 443, it never adds TLS -- so it is not even compiled in (both stream functions refuse; the gate refuses its strings). Stories play from the SD card only; a story not on the card ends the session |
| Fresh OTA image | confirmed by the check-in | same; and it stays PENDING_VERIFY until then (`verifyRollbackLater()` returns true), so ANY reset before the check-in -- a panic in `setup()` included -- rolls back |
| Before factory step 9 | n/a | offline + BLE setup refused (`[sec] factory not finished ...`) |
| OTA signature check | only on a Secure Boot chip | always |
| Older image after a confirmed OTA | kept | first sector erased (physical-downgrade guard) |
| Board model | `areg-s3-n8` | `areg-s3-n8-sb`. The backend reads the staged image's OWN marker at startup (`FirmwareImageMarker`): an `-sb` image is offered ONLY to devices reporting `areg-s3-n8-sb`, even with `FirmwareUpdate:BoardModel` EMPTY (production's "offer to every toy") -- a field toy never gets a locked-fleet image. Still set `FirmwareUpdate:BoardModel` (section 12 step 0); one backend offers ONE image at a time -- while it serves the `-sb` image, DEV and field toys get no updates |

`security_profile.h` is the compile-time guard. A RELEASE build refuses:
`AREG_TLS_INSECURE`, `AREG_PROVISION_IDENTITY_ONCE`,
`AREG_PROVISION_WIFI_ONCE/FORCE`, `AREG_BLE_POP`, any `AREG_PROV_POP`, every
bench build flag, a core other than 3.3.8; and asserts an `https://` backend,
empty compiled Wi-Fi, placeholder device id/key, a non-empty manifest HMAC
key, a `-sb` board model and at least 3 TLS trust anchors
(`AREG_CA_ANCHOR_COUNT`, itself `static_assert`ed against the PEM text). It REQUIRES `AREG_CONTENT_SYNC_BENCH` (the one
load-bearing "BENCH" flag -- without it a toy downloads nothing) and
`AREG_USE_BLE_PROVISIONING` (a release image has no other way to get Wi-Fi).

## 4. Firmware changes (esp32/AregVoiceMvp)

| File | What |
|---|---|
| `security_profile.h` | profile selection, `AREG_PROFILE_NAME`, the release guard above, profile-aware board-model default |
| `secure_store_rules.h` + `secure_store.{h,cpp}` | decides Encrypted / PlaintextDev / LegacyDefaultNvs / refuse from profile + eFuses (FE on, SB on, HMAC_UP in KEY4) + whether the table has `nvs_sec`; opens `nvs_sec` (HMAC scheme, keys derived never generated); `areg_prefs_begin()` is the only way to open app NVS; boot-time physical purge of the default `nvs` when requested (never in legacy mode -- there `nvs` IS the identity); `secure_store_wipe()` (not wired to a command in v1) |
| 33 `Preferences::begin` call sites | now `areg_prefs_begin(...)` -> `nvs_sec` (aregdev, aregwifi, aregota, csync, areggplays, areggame, aregplays, aregstory, aregheard, aregvoice, aregqidx, aregmusic, aregstate) |
| `AregVoiceMvp.ino` | `secure_store_begin()` -> `WiFi.persistent(false)` -> `security_posture_print()` -> identity load, all before any provisioning burn; the 5 s "forget Wi-Fi" gesture also requests the purge |
| `ble_provisioning.cpp` | RELEASE: refused while download mode is open (`[prov] factory not finished ...`); per-device PoP or refuse (`[prov] no per-device PoP - BLE setup refused`); credential receipt requests the purge; a session that ends without success, or gets no retry within 3 min of a failed validation, reboots the toy from IDLE (`esp_wifi_restore()` first) so the purge erases the plaintext Wi-Fi copy NOW (`[prov] setup did not finish - rebooting ...`); SSID redacted; **N031 fix** `extern "C" bool btInUse(void) { return true; }` for cores 3.3.7-3.3.11 |
| `voice_client.cpp`, `net_transport.cpp` | RELEASE: no compile-time identity/Wi-Fi fallback; no identity, or download mode still open => NO backend call (`areg_backend_allowed()`); https only (`areg_url_allowed()`); SSID / URLs redacted; the TLS-insecure banner only exists in an insecure build |
| `audio_io.cpp`, `AregVoiceMvp.ino` | RELEASE: `AudioFileSourceHTTPStream` is neither included nor constructed -- `audio_play_story_stream` reports an open failure, `audio_play_qa_stream` returns false, and no story token is fetched for a stream that cannot play (`[story] stream refused - a release toy plays stories from the SD card only`). DEV keeps both streams (bench / fallback test) |
| `tls_trust_anchors.{h,cpp}` | the five root CAs (one PEM string, `setCACert`); `AREG_CA_ANCHOR_COUNT` checked against the text at compile time |
| `ota_slot_rules.h` + `ota_foundation.cpp` | RELEASE: after a confirmed check-in (and at every boot with no OTA outcome pending) erase the first 4 KB sector of the inactive OTA slot if it still holds an image (raw read, so ciphertext is not mistaken for data). BOTH profiles: `verifyRollbackLater()` returns true, so Arduino's `initArduino()` no longer marks a fresh OTA image VALID before `setup()`; it stays PENDING_VERIFY until the check-in's `esp_ota_mark_app_valid_cancel_rollback()` (an image PENDING_VERIFY with NO OTA record cannot prove itself -- typically it cannot read the store, e.g. a locked-fleet image that reached an unsecured field toy -- and rolls back at once, `[ota] pending_verify with no OTA record - rolling back`; kept only if nothing valid is left to roll back to) |
| `sbv2_rules.h` + `ota_sig_verify.{h,cpp}` | Secure Boot V2 block parsing (pure) + streaming SHA-256 / mbedTLS RSA-PSS verify against the chip's non-revoked eFuse digests |
| `fw_marker_rules.h` + `fw_version_marker.{h,cpp}` | `AREGFWV1:<ver>:<board>:<profile>` in every image; streaming scanner (magic split across chunks handled) |
| `ota_apply.cpp` | refuses a non-signed shape before download (`image_shape_invalid`); after the sha256 check and BEFORE `Update.end()`: signature (`image_sig_invalid`) and marker (`image_marker_mismatch`: version == manifest, > running, board equal, release toy needs a release image) |
| `security_posture_rules.h` + `security_posture.{h,cpp}` | `security_network_allowed()` (RELEASE: only once `DIS_DOWNLOAD_MODE` is burned; then `[sec] factory not finished (download mode open) - offline, BLE setup disabled`); one line per boot: `[sec] profile=.. sb=.. fe=.. dl=.. jtag=.. nvs=.. hmac=.. sbslots=.. revoked=.. sb_rel=.. fe_rel=.. store=..`; `[sec] key_fp=xxxxxxxx` (4 bytes of SHA-256 of the device key) ONLY while download mode is open |

The pure parts have host tests (`host_tests/secure_store_rules_test.cpp`,
`sbv2_rules_test.cpp` with a committed TEST signature sector,
`fw_marker_rules_test.cpp`, `security_posture_rules_test.cpp`,
`ota_slot_rules_test.cpp`); the source-level rules (https-only, no plain-TCP
audio stream reachable in a release build, anchors, offline-before-step-9,
legacy never purges, retire only after confirm, PENDING_VERIFY until the
check-in, failed BLE setup purges at once) are
`tools/firmware/test_firmware_source_rules.py`.

## 5. Partition table (frozen on every locked toy)

```
nvs,        data, nvs,      0x9000,   0x5000,            ESP-IDF scratch, plaintext
otadata,    data, ota,      0xe000,   0x2000,
app0,       app,  ota_0,    0x10000,  0x300000,
app1,       app,  ota_1,    0x310000, 0x300000,
spiffs,     data, spiffs,   0x610000, 0x1D0000, encrypted (unused)
coredump,   data, coredump, 0x7E0000, 0x10000,  encrypted
nvs_sec,    data, nvs,      0x7F0000, 0x10000,           ALL app state
```

`nvs` must stay FIRST among NVS partitions (Arduino's `initArduino()` erases
the first nvs-subtype partition on an init error), NVS rows never carry
`encrypted`. `coredump` carries `encrypted`: ESP-IDF v5.5.4 writes it with
`esp_flash_write_encrypted` on a flash-encrypted chip
(`core_dump_flash.c`), so a locked toy's crash dump is ciphertext under its
per-device key, readable only by the app itself (an earlier draft of this
design dropped the row on the wrong belief that it would hold plaintext).
Because the table can never change on a locked toy, dropping it would have
removed crash dumps from the field fleet for good; keeping or dropping it
is an owner decision before the first lock (section 11). Every rule is a
test: `tools/firmware/test_partitions.py`. Release bootloader measured
0x6000 unsigned / 0x7000 signed -- fits under the table at 0x8000 only with
bootloader logging OFF.

## 6. Builds, signing, gate

```
build machine                              offline signing laptop                 factory station
-------------                              ----------------------                 ---------------
build_release_bootloader.sh  (once/gen.) -> sign_release.py bootloader           secure_provision.py
  bootloader-unsigned.bin                     (primary, then --append backup)       (public keys only)
build_idf.sh release --unsigned          -> sign_release.py app (primary)       -> bundle: bootloader-signed.bin,
  app-unsigned.bin (--secure-pad-v2)          sign_release.py add (table,           partition-table.bin, boot_app0.bin,
  + release gate                              boot_app0) -> bundle.json             app-signed.bin, bundle.json
                                                                                  OTA server: app-signed.bin
```

- **App**: `tools/firmware/build_idf.sh release --version X` (needs ESP-IDF
  v5.5.4 exported, `AREG_BACKEND_BASE_URL`, a NEW `AREG_MANIFEST_HMAC_KEY`).
  Before compiling it refuses a dirty tree (`release_checks.py clean-tree`:
  the image and `release.json` carry HEAD's full commit) and an HMAC key
  that occurs in any firmware image ever committed to the repository
  (`hmac-fresh`); it asserts the compiler and IDF's esptool against
  `deps.lock` and re-verifies every dependency (pinned commit, no modified
  or untracked file except the IDF wrapper `CMakeLists.txt`, the
  `network_provisioning` tree hash, the libs' sdkconfig sha256). It turns
  the ESP-IDF component manager OFF (`IDF_COMPONENT_MANAGER=0` -- IDF's
  default would resolve arduino-esp32's `idf_component.yml` version ranges
  from the registry into `managed_components/`) and refuses a
  `dependencies.lock` / `managed_components` left in the project; the IDF
  checkout must be v5.5.4 with no local change and every submodule at its
  pinned commit (`git status --ignore-submodules=none`, `git submodule
  status --recursive`). When the release bootloader enforces anti-rollback
  (section 11 rule 8A) it requires `CONFIG_BOOTLOADER_APP_ANTI_ROLLBACK` +
  `CONFIG_BOOTLOADER_APP_SECURE_VERSION` in the app config. It re-runs
  IDF's exact `elf2image` and requires a byte-equal `.bin` (proves the
  arguments), then pads with `--secure-pad-v2`, runs the gate and STOPS at
  `app-unsigned.bin` + `release.json` -- the default; the private key never
  touches this machine. Inline signing exists for TEST runs only
  (`AREG_SB_SIGNING_KEY` + a `TEST ONLY` digests file, esptool 5.2.0's
  `espsecure` via `ESPSECURE`); with the real digests file it is refused.
  Byte-reproducible: two clean builds in different directories produced
  the same image (verified 2026-10-08, again after the review fixes).
- **Bootloader**: `tools/firmware/build_release_bootloader.sh` -- refuses a
  dirty tree, checks the IDF commit (and that the checkout and its
  submodules are pristine), compiler, IDF esptool and the resolved config
  (anti-rollback exactly as `esp32/bootloader-release/sdkconfig.defaults`
  says), asserts <= 0x7000, prints the `RELEASED.md` row (sha256s,
  toolchain, esptool, commit) with Status `CANDIDATE`. **Status ladder**:
  `CANDIDATE` (built) -> `PILOT` (the owner rebuilt and compared; may be
  signed with `--pilot` for the two pilot boards) -> `APPROVED` (after the
  hardware pilot AND the anti-rollback / crash-dump decisions; the only
  status a real toy accepts). Anything else (e.g. `SUPERSEDED`) is never
  signable.
- **Sign** (offline): `tools/firmware/sign_release.py app|bootloader|add`.
  Refuses a key whose digest is not in `esp32/security/sb_trusted_digests.txt`,
  a TEST key, an image the gate refuses, a bootloader without the backup
  key, an `espsecure` that is not 5.2.0, and an `--out-dir` inside the
  repository that git would track (signed images never go into git).
  `bootloader` signs ONLY an input whose sha256 is the
  `bootloader-unsigned.bin` of a `RELEASED.md` row with Status `APPROVED`
  (`--pilot`: `PILOT` too; a TEST key: any recorded row) -- a signature says
  who signed, not WHICH bootloader is frozen into a toy for life (e.g.
  `build_idf.sh` also produces an app-config bootloader without
  `CONFIG_SECURE_BOOT`, which never verifies the app; it would sign just as
  well). `add` takes `partition-table.bin` only if it is the table that same
  `RELEASED.md` row records (sign the bootloader first) and `boot_app0.bin`
  only if it is arduino-esp32 3.3.8's (sha256 `f94c5d78...`). Writes
  `bundle.json` (sha256 of everything, versions, IDF commit, core, the
  bootloader's `RELEASED.md` status). After the
  hardware pilot: `sign_release.py add --pilot-evidence
  tools/quality-evidence/chip-security-pilot-YYYYMMDD.md` records the
  evidence (the file must name this bundle's signed bootloader sha256 and
  contain the line `OTA release+1 on locked board: PASS`) -- the station
  refuses `provision` without it (section 8).
- **Gate**: `tools/firmware/check_release_image.py` (still stdlib only):
  `--profile release` (ESP32-S3 header dio/80m/8MB, exactly one
  `AREGFWV1:<ver>:<board>-sb:release` marker, no `areg-pair` / TLS-insecure /
  fallback-credential / bench strings, **>= 3 whole PEM trust anchors**),
  `--require-sbv2 --trusted-digests` (signed shape, >= 1 block verifying
  with a trusted key, no block that does not), `--bootloader` (<= 0x8000,
  exactly two verifying blocks = both keys, and the unsigned body -- the
  image minus its 4 KB signature sector -- a `RELEASED.md` build with
  Status >= `--min-status`, default `APPROVED`). A release image also must
  not carry ESP8266Audio's plain-TCP stream (`Can't open HTTP request`,
  `AudioFileSourceHTTPStream::`).
- **Stage** `app-signed.bin` from PRIVATE storage (a Railway volume +
  `FirmwareUpdate__ImagePath`), never from git; the manifest's
  `sizeBytes`/`sha256` are of the SIGNED file. `FirmwareUpdate:BoardModel`
  must already be `areg-s3-n8-sb` (section 12 step 0). Backstop in code:
  at startup the backend reads the marker of the image at
  `FirmwareUpdate:ImagePath` and offers an `-sb` image only to devices
  reporting that exact board model, whatever `BoardModel` says (a `Url`
  pointing to an EXTERNAL host cannot be inspected -- serve the image
  through `/api/devices/firmware-image`).

## 7. Key custody (the crown jewels)

- **Preparing the offline laptop** (once, before any key exists on it):
  a fresh Linux install with `python3-venv` and `openssl`, then Wi-Fi off
  for good. `sign_release.py` needs the repository and esptool 5.2.0's
  `espsecure`, which arrive by USB stick: on an ONLINE Linux PC with the
  same Python version run `pip wheel -w wheels -r
  tools/factory/requirements.txt` in the repository (`pip wheel`, not
  `pip download`: esptool 5.2.0 is published as an sdist only, and building
  it offline would need setuptools, which `pip download` does not fetch --
  checked 2026-10-08), copy the repository and `wheels/` over, then on the
  laptop `python3 -m venv ~/areg-venv && . ~/areg-venv/bin/activate && pip
  install --no-index --find-links wheels -r tools/factory/requirements.txt`
  (a venv because current Debian/Ubuntu refuse a system-wide `pip install`).
  Every later signing session brings a fresh copy of the repository the
  same way (`RELEASED.md` and the gate change).
- Generate both keys on an **offline** laptop: `openssl genrsa -out
  sb_primary.pem 3072`, same for `sb_backup.pem`.
- Primary: encrypted USB stick, used only on that laptop; passphrase not
  stored with it. Backup: a second encrypted stick + a sealed paper copy of
  its passphrase, **in a different building**. The backup comes out twice in
  normal life: to dual-sign the bootloader (once per hardware generation)
  and for a revocation.
- Only PUBLIC material leaves the laptop: `sb_primary.pub.pem`,
  `sb_backup.pub.pem`, `sb_trusted_digests.txt` -> `esp32/security/`
  (see its README). Run the `openssl` / `espsecure` commands in a folder
  on the encrypted stick, never inside the repository; as a last net the
  root `.gitignore` ignores every `*.pem` except `*.pub.pem`. The factory never holds a private key (`espefuse
  burn-key-digest` works from the public PEM -- verified), and neither does
  the build machine (`build_idf.sh release` refuses `AREG_SB_SIGNING_KEY`
  unless the digests file is a TEST one).
- Lose BOTH private keys = no firmware update ever again for every locked
  toy. Leak one = attacker-signed firmware accepted until revoked by OTA
  (procedure: ship an app signed by the backup, then a later release burns
  `SECURE_BOOT_KEY_REVOKE0` from the app -- documented, not built in v1).

## 8. Factory procedure (owner, one toy at a time)

Station: a dedicated Linux laptop; `pip install -r tools/factory/requirements.txt`;
the signed bundle directory; `esp32/security/` public keys. Keys are made in
`/dev/shm/areg-factory/<mac>/` (RAM, mode 700) and shredded at the end.

1. **Rehearse first, every new bundle** (nothing touches a chip):
   `python3 tools/factory/secure_provision.py virt --bundle <B> --response sample.json`
   -- runs steps 3-4 for real and executes the eFuse burns of steps 6, 7, 9
   against virtual eFuses, checking every summary. Rehearsals are never
   pilot-gated and accept a `CANDIDATE` bootloader row.
2. Plug in a NEW toy (virgin chip) by USB. Run
   `python3 tools/factory/secure_provision.py provision --bundle <B> --backend-url https://<host> --port /dev/ttyACM0`
   (needs `AREG_PROVISIONING_SECRET`; `https://` only -- the secret goes out
   and the toy's new device key comes back), or the same through
   `provision_toy.py --profile release --bundle <B> ...` (`--profile` is
   required there: no unlocked toy by omission). `provision` (and `repair`)
   REFUSE a bundle without hardware-pilot evidence (section 6 "Sign") and a
   bootloader whose `RELEASED.md` row is not `APPROVED`; the two pilot
   boards themselves are provisioned with `--pilot-board` (`PILOT` row
   accepted, no evidence; recorded as such, NEVER shipped). Labels and
   records go to `~/areg-factory-out/` (mode 700, outside the repository).
3. The station does, stopping at the first failure:
   - **0** verifies the bundle (shas, both signatures, public keys == trusted digests, >= 3 TLS anchors, the pilot evidence; the bootloader is an `APPROVED` `RELEASED.md` build, the partition table is the one recorded with it AND parses to `partitions.csv` -- `nvs_sec` where step 4 builds it -- and `boot_app0.bin` is Arduino 3.3.8's; refuses TEST material);
   - **1** reads the chip: ESP32-S3, 8 MB, MAC (esptool and eFuse MAC must agree); eFuses must be virgin -- otherwise STOP;
   - **2** registers the toy (device id, key, claim code, PoP);
   - **3-4** makes the per-device flash-encryption key and NVS HMAC key, encrypts the four images, builds the encrypted `nvs_sec` (identity), deletes the CSV;
   - **5** erases and writes the still-blank chip;
   - **6** asks you ONCE to type `BURN <mac>` for steps 6 AND 7 together, re-reads the chip's MAC from its eFuses (a swapped or replugged board stops here, nothing burned), then burns flash encryption + the HMAC key + JTAG off; checks the summary;
   - **7** with no further prompt: MAC re-check, burns both Secure Boot digests, revokes slot 2, enables Secure Boot; checks the full table (RD_DIS=19, purposes, digests);
   - **8** resets the toy and reads its console: `sb=1 fe=release dl=enabled nvs=encrypted store=encrypted`, the device id, the key fingerprint, and `[sec] factory not finished (download mode open) - offline, BLE setup disabled` (the image keeps a half-secured toy offline) must all be there;
   - **9** asks a second time, re-checks the MAC, then disables ROM download mode for ever; console must show `dl=disabled sb_rel=1 fe_rel=1`, no key fingerprint and no factory-window line;
   - **10** shreds the workdir; **11** writes `~/areg-factory-out/<deviceId>/factory-record.json` (no secrets) and prints the label.
   **Never unplug or reset the toy between steps 6 and 7.** The release
   bootloader is built with Secure Boot and download-mode lock enabled: if it
   ever boots normally after step 6 (flash encryption on) but before step 7
   (Secure Boot on), it enables Secure Boot BY ITSELF from its own signature
   blocks and burns `DIS_DOWNLOAD_MODE` -- the toy is then locked before
   step 8 could verify it, with KEY_PURPOSE_5 not write-protected (checked
   in the ESP-IDF v5.5.4 source, `bootloader_utility.c` ->
   `esp_secure_boot_v2_permanently_enable` and
   `esp32s3/secure_boot_secure_features.c`). What the station does about
   it, in code (review round 3): every esptool call passes `--after
   no-reset` (esptool's default `hard-reset` IS a normal boot -- it used to
   follow read-mac, flash-id, erase-flash and write-flash); espefuse only
   ever resets INTO download mode; ONE confirmation covers steps 6 and 7,
   so no human wait sits inside the window; a normal boot (the step 8 / 9
   console read) is refused in code while flash encryption is on and
   Secure Boot is not verified; and `repair` reads the eFuses before
   anything else and, on such a half-burned chip, finishes step 7 before
   any normal boot. A single combined espefuse batch would remove the
   window entirely; it is not used because only the two-step order was
   proven in QEMU -- revisit after the hardware pilot.
   A run that stops BEFORE step 6 (network, cable, gate) is simply re-run:
   nothing irreversible happened, and the station reuses the registration it
   already made.
4. **Any stop after registration** (step 2 onwards) writes
   `~/areg-factory-out/<deviceId>/INCOMPLETE.json` (device id, MAC, last completed step,
   whether eFuses were burned, time -- no secrets) and prints **DO NOT
   SHIP**. A successful finish removes it. Then:
   - stopped BEFORE step 6 (nothing burned): re-run `provision` (it reuses
     the registration);
   - stopped at step 6, 7 or 8 (or step 9's confirmation declined): the toy
     is HALF-secured -- Secure Boot / flash encryption on, ROM download mode
     OPEN. Do NOT start over. Run `secure_provision.py repair --bundle <B>
     --port <P>` (finishes only the missing burns; `--reflash` rewrites the
     encrypted images with `esptool --no-stub write-flash --force` -- the
     only place `--force` is allowed). Repair works only on the same
     station, before step 9, with the workdir still in `/dev/shm` (a reboot
     of the station loses it);
   - **if the toy cannot be finished** (repair impossible, workdir lost,
     station rebooted): **revoke it** -- `POST
     /api/internal/devices/<deviceId>/revoke` (operator console: Devices ->
     the toy -> Revoke) -- keep `INCOMPLETE.json`, set the toy aside and
     never ship it. Its firmware already keeps it offline (no backend call,
     no BLE setup while download mode is open), so it can never receive a
     family's Wi-Fi; the revocation kills its device key on the backend too.
5. Converting an already-built toy (`--rotate-existing --mac <MAC>`): keeps
   the device id and parent link, mints a new key, mints a new PoP locally
   (the backend never checks PoPs) and prints a PoP-only sticker. The toy's
   NVS is lost (Wi-Fi, cursors, queues); the SD card is kept.

`--dry-run` / `virt` modes print or emulate every command; nothing in this
repo ever runs a real burn without the typed confirmation.

## 9. What is irreversible

| Step | Burn | After it |
|---|---|---|
| 6 | FE key (KEY0/1), HMAC key (KEY4), `SPI_BOOT_CRYPT_CNT=7`, download-mode cache/encrypt off, JTAG off, direct boot off, write-protect DIS_ICACHE | flash encryption can never be turned off; the chip only runs images encrypted with ITS key |
| 7 | Secure Boot digests (KEY2/3), `SECURE_BOOT_KEY_REVOKE2`, `SECURE_BOOT_EN`, write-protect RD_DIS + KEY_PURPOSE_5 | the chip only boots images signed by the primary or backup key |
| 9 | `DIS_DOWNLOAD_MODE` | no cable access ever again: no flashing, no erase, no eFuse reading. OTA is the only update path |

Deliberately NOT burned: aggressive revoke, REVOKE0/1 (kept for a future
signed revocation), secure download mode, `DIS_USB_SERIAL_JTAG` (the
console stays). Anti-rollback is not enabled in the candidate bootloader --
that is an open OWNER DECISION, and it must be made before the first lock
because the bootloader can never change afterwards (section 11, rule 8).

Note on "downgrade by writing the flash": it does NOT need the per-device
flash-encryption key. Erasing `otadata` needs no key (with both copies
invalid the v5.5.4 bootloader logs "No factory image, trying OTA 0" and
boots `ota_0`), and this toy's own XTS ciphertext can be written back to the
same address. Without anti-rollback, anyone with a flash clip can therefore
boot ANY older owner-signed release still present in that toy's flash.

## 10. Recovery and returned toys

- A locked toy can only be updated by **signed OTA**. No re-flash, no erase.
- A returned toy whose firmware still reaches the backend: fix by OTA.
- A toy whose firmware cannot reach the backend (broken Wi-Fi stack, broken
  TLS trust, broken OTA path in a shipped release): **unrecoverable --
  replace it.** Hence the release rules in section 11.
- A toy that left the station half-way (INCOMPLETE.json, section 8.4): it
  stays offline by design; revoke it and replace it.
- "Factory reset" for a new owner: the parent unlinks in the app (keeps the
  Device row); the toy keeps its identity. A full wipe of `nvs_sec`
  (`secure_store_wipe()`) exists in the firmware but is not wired to a
  command in v1 -- a wiped locked toy could only be re-identified at the
  factory, which no longer has a cable path to it.
- Support reads the console only: the `[sec]` line tells whether the toy is
  locked and whether its store opened.

## 11. Gating rules before the FIRST locked toy

Enforced in code (a build, the gate or the station refuses otherwise):

1. **TLS trust**: five root CAs, three organisations (C089). RELEASE
   `#error`s below 3 (`AREG_CA_ANCHOR_COUNT`, checked against the PEM text);
   the release gate counts whole PEM blocks in the image and refuses < 3, so
   the station's bundle check refuses such an image too. Changing the set:
   add the new root in a release that still trusts the old one, prove it on
   a locked bench toy, only then move the backend.
2. **Every release proves on a locked board that it can OTA to release+1**:
   the station refuses `provision`/`repair` with a bundle whose
   `bundle.json` has no pilot evidence naming its signed bootloader and the
   line `OTA release+1 on locked board: PASS` (section 6 "Sign").
3. **A NEW manifest HMAC key** with the first secured release: release
   builds refuse a key that occurs in any firmware image ever committed to
   the repository (`release_checks.py hmac-fresh`).
4. **Release images never go into git**: `backend/src/ArmenianAiToy.Api/firmware/*.bin`
   is gitignored; `sign_release.py` refuses an output directory git would
   track. Private keys never go into git either: `.gitignore` ignores every
   `*.pem` except `*.pub.pem`; factory output (`tools/factory/out/`) is
   ignored and no longer the default (`~/areg-factory-out`).
5. **Only an APPROVED bootloader + its table reach a real toy** (review
   round 3): `sign_release.py bootloader` signs only a `RELEASED.md` row
   with Status `APPROVED` (`--pilot`: `PILOT`), the gate's `--bootloader`
   and the station check the same, and the partition table must be the one
   recorded in that row. The owner sets `APPROVED` only after rules 8-10
   below -- the anti-rollback, crash-dump and pilot decisions are thereby
   code gates, not paragraphs.

**OWNER, before pushing this branch or locking any toy:**

6. **Make the repository private** -- it is PUBLIC today (checked
   2026-10-08: `visibility=public`), so the whole firmware source, this
   design and every committed image (with the current manifest HMAC key
   inside `areg-current.bin`) are world-readable, whatever the chip does.
   GitHub -> the repository -> Settings -> General -> Danger Zone -> Change
   visibility -> Private. Treat everything ever committed as published:
   rotate any key or secret that ever appeared in git history (optionally
   purge history with `git filter-repo` once private).
7. **Serve firmware from private storage**: put the served image on a
   Railway volume (e.g. `/data/firmware/areg-current.bin`), set
   `FirmwareUpdate__ImagePath` to it, then
   `git rm --cached backend/src/ArmenianAiToy.Api/firmware/areg-current.bin`
   and commit.
8. **Decide anti-rollback -- it can never change after the first lock.**
   Today the candidate bootloader has it OFF, so a flash clip can boot any
   older owner-signed release still in that toy's flash (section 9 note).
   - **(A, recommended)** turn it on. The complete recipe (every step is
     enforced by a script, so a half-done switch fails loudly):
     1. `esp32/bootloader-release/sdkconfig.defaults`: replace
        `# CONFIG_BOOTLOADER_APP_ANTI_ROLLBACK is not set` with
        `CONFIG_BOOTLOADER_APP_ANTI_ROLLBACK=y` and update that file's header
        comment. `build_release_bootloader.sh` follows this file: it then
        REQUIRES anti-rollback in the resolved config (and still refuses it
        whenever the file does not say so).
     2. `esp32/AregVoiceIdf/sdkconfig.defaults.release`: add
        `CONFIG_BOOTLOADER_APP_ANTI_ROLLBACK=y` and
        `CONFIG_BOOTLOADER_APP_SECURE_VERSION=<n>` (start at 0).
        `build_idf.sh release` refuses a release app without both once the
        bootloader enforces anti-rollback, and refuses anti-rollback in the
        app without the bootloader.
     3. `build_release.py` (arduino-cli) then refuses to make a release at
        all (the precompiled core cannot give an app a secure version):
        `build_idf.sh release` becomes the only release path.
     4. Rebuild the bootloader (`build_release_bootloader.sh`; measured
        0x7000 unsigned / 0x8000 signed -- ZERO margin, which the gate's
        0x8000 limit still allows) and record the new row in
        `esp32/bootloader-release/RELEASED.md` (mark the old row
        `SUPERSEDED`; note "anti-rollback on" in the Status cell).
     5. Re-prove in QEMU and on the pilot (section 12), then `APPROVED`.
     6. On every later release that must block older ones, bump
        `CONFIG_BOOTLOADER_APP_SECURE_VERSION`; a confirmed OTA then burns
        the `SECURE_VERSION` eFuse and older releases never boot again.
     Not done here: it changes the frozen bootloader and the release build,
     which is the owner's call.
   - **(B, minimum -- IMPLEMENTED)** after an OTA is confirmed (and at every
     boot with no OTA pending) a RELEASE toy erases the first sector of the
     older image in the inactive slot (`ota_slot_rules.h`), so erasing
     otadata can no longer boot it. This stops an attacker who did not dump
     this toy's flash before the update; it does NOT stop one who did and
     writes that old ciphertext back (only A does).
9. **Decide the crash-dump partition -- it can never change after the
   first lock.** The table keeps an `encrypted` `coredump` (0x7E0000, 64 KB,
   taken from the unused `spiffs`): a locked toy's crash dump is then
   ciphertext under its per-device key that only the app can read (a
   future firmware could upload a summary). To DROP it instead: delete the
   row, give `spiffs` back its 64 KB (0x1E0000), update
   `tools/firmware/test_partitions.py`, the copy in
   `esp32/bootloader-release/`, `docs/hardware/audit-mcu.md` and section 5
   ("dropped by choice; an encrypted coredump was possible"), rebuild the
   bootloader project and record the new table sha256 in `RELEASED.md`.
10. **Run the hardware pilot** (section 12) -- the station enforces rule 2.

## 12. Hardware pilot (owner; 2 sacrificial DevKitC-1 N8R8 boards)

**Step 0 -- before staging ANY signed image, including the pilot's:** set
`FirmwareUpdate__BoardModel=areg-s3-n8-sb` on Railway (or use a separate
staging backend). Production ships `FirmwareUpdate:BoardModel` EMPTY, which
means "offer to every toy": a field toy on 1.3.2 (no signature or marker
check) would install a signed release image, refuse its store and stay
offline until cable-flashed. The backend now also refuses that in code (it
reads the staged image's `-sb` marker at startup and offers it only to
`areg-s3-n8-sb` devices -- section 6 "Stage"), but only for an image served
from `FirmwareUpdate:ImagePath`; set the variable anyway. (Third line: a
release image that does reach an unsecured toy cannot open its store, finds
no OTA record while PENDING_VERIFY and rolls back to the previous image at
its first boot -- unproven on hardware, and only for toys whose OLD firmware
applied the update with Arduino's `Update`, which leaves it PENDING_VERIFY.) While it is
`areg-s3-n8-sb`, DEV and field toys get no updates (one board model per
backend).

Board 1: `secure_provision.py provision --pilot-board ...` -- the FULL flow
including step 9 (a release image is deliberately offline until download
mode is disabled, so BLE and OTA can only be exercised after step 9; the
board is sacrificial) -> BLE setup with the per-device PoP -> check the
default `nvs` was purged (`[sec] default nvs purged` on the next boot) ->
OTA to release+1 signed by the primary -> after the check-in, `[ota]
retired the older image in app0` -> OTA of an unsigned and of a
rogue-signed image must end `image_sig_invalid` -> OTA again -> **rollback
proof** (review round 3): OTA a pilot-only release+2 that panics in
`setup()`; the next boot must come back on the previous image (`[ota]
ROLLBACK detected`), never a crash loop, and the first boot of every good
OTA must log `img_state=pending_verify` -> **the bootloader, not only the
app, refuses unsigned code** (QEMU case E on silicon): a pilot-only test
app -- signed, so it boots, and built ONLY for this board -- writes an
UNSIGNED image into the inactive slot without `ota_sig_verify` and selects
it in otadata; the reset must fall back to the signed image (never run the
unsigned one), then destroy that test app's bundle -> **abandoned BLE
setup**: send a wrong Wi-Fi password and walk away; within ~3 minutes the
toy logs `[prov] setup did not finish - rebooting` and the next boot
`[sec] default nvs purged`. Board 2:
revocation drill (backup-signed OTA, then burn REVOKE0 from an app); also
stop one run between steps 7 and 9 on purpose and check the console says
`factory not finished` and BLE setup is refused. Also prove on silicon what
QEMU could not: QIO flash + octal PSRAM boot under the release bootloader,
Arduino `Update` writing to an encrypted slot, USB-Serial-JTAG auto-reset
after the burns, real burn reliability, BLE + Wi-Fi + purge, the five-CA TLS
handshake against the live backend (mbedTLS parses all five roots into
INTERNAL RAM on every handshake: about 5-6 KB more than the single root --
log `heap_caps_get_free_size(MALLOC_CAP_INTERNAL)` during a voice turn and a
content sync and confirm the headroom).

Record the evidence in `tools/quality-evidence/chip-security-pilot-YYYYMMDD.md`;
it must name the signed bootloader's sha256 and contain the exact line
`OTA release+1 on locked board: PASS`. Then
`sign_release.py add --out-dir <bundle> --pilot-evidence tools/quality-evidence/chip-security-pilot-YYYYMMDD.md`
-- only such a bundle can `provision` a real toy.

## 13. Residual risks (honest)

1. Lab-grade fault injection / side channels can likely open ONE toy: its
   device key (revoke it), its family's Wi-Fi, the firmware, the old HMAC key.
   Not other toys (per-device keys), not the signing key.
2. **The repository is public** (section 11 rule 6): source, design, old
   images and the old HMAC key are readable regardless of the chip until
   the owner makes it private; everything ever committed stays published.
3. No cable recovery (section 10).
4. The bootloader is frozen (IDF v5.5.4, no logs; anti-rollback per the
   owner's decision, section 11 rule 8) for the life of the hardware; every
   future Arduino/IDF major must boot under it.
5. Signing-key custody is the single most important secret now.
6. The factory PC sees per-device keys for about a minute.
7. NVS encryption is not rollback-protected: a hardware attacker can restore
   an older `nvs_sec` image of the SAME toy (cannot forge new entries).
8. A runtime exploit (MP3/JSON/HTTP parser) runs with the toy's RAM secrets;
   Secure Boot does not stop runtime bugs.
9. The SD card is plaintext and removable (audio can be swapped).
10. Not verified on hardware (section 12).
11. **Physical downgrade** (flash clip): without anti-rollback, erasing
    otadata or writing back this toy's own older ciphertext boots an older
    owner-signed release still in its flash; with a parser bug in that
    release this becomes code execution, then the HMAC_UP-derived NVS keys,
    the device key and the family's Wi-Fi. Mitigated (inactive slot retired
    after every confirmed OTA) but only closed by anti-rollback (section 11
    rule 8).
12. A toy that escapes the station half-way is half-secured (download mode
    open). Mitigated: its firmware stays offline and refuses BLE setup, the
    station writes INCOMPLETE.json and says to revoke it (section 8.4).
13. A release toy has no network audio stream (review round 3): a story
    that is not on its SD card cannot play. A TLS stream source (an
    `AudioFileSource` over `areg_http_begin`'s pinned-root client, resuming
    with `?from=`) would bring it back; it is not built -- TLS + MP3 decode
    at once needs internal RAM the toy may not have (87 % of DIRAM used),
    which only the pilot can measure.
14. The backend's `-sb` image gate inspects only an image served from
    `FirmwareUpdate:ImagePath`; a `Url` on an external host is offered by
    `BoardModel` alone (section 6 "Stage").

## 14. What to order for this (links from the owner list)

| Item | Qty | Link |
|---|---|---|
| ESP32-S3-DevKitC-1 **N8R8** (same chip as the toy) -- sacrificial pilot boards, on top of the board for unit #1 | 4 (2 for the pilot, 2 spare) | https://www.aliexpress.com/item/1005003819366900.html |
| USB **data** cable for the board | 2 | https://www.aliexpress.com/w/wholesale-usb-c-data-cable-1.5m.html |
| USB sticks: the two encrypted key sticks + one that carries only public files and images to and from the offline laptop (SanDisk official store only) | 3 | https://www.aliexpress.com/store/1102960672 |
| An old laptop that never goes online again after setup (offline signing machine, section 7) | 1 | (any; reuse one) |
| A Linux laptop for the factory station (section 8; online, so never the signing laptop) | 1 | (any; reuse one) |

## 15. Verified here (2026-10-08) / not verified

Verified in the build container (no hardware), after the THIRD review
round. Release builds ran from scratch clones with the working tree
committed there (the release path refuses a dirty tree); every key used was
a TEST key outside the repository.
- DEV and RELEASE firmware built with `idf.py` (ESP-IDF v5.5.4 +
  arduino-esp32 3.3.8 as a component): dev app 1,654,800 B (52 % of the
  3 MB slot), release app 1,703,936 B padded / 1,708,032 B signed (54 %).
- Byte-reproducible: three from-scratch release builds (two clones in
  different directories with differently-located dependency trees, plus the
  inline TEST-signed run) gave the identical `app-unsigned.bin` (sha256
  `b00cedb2...` for the snapshot commit; the commit is stamped into the
  image, so every commit has its own hash). They ran with `IDF_COMPONENT_MANAGER` UNSET in the
  environment (the scripts set it themselves): no `managed_components/`, no
  `dependencies.lock`, the clone stayed clean. `build_idf.sh` refused a
  stray `dependencies.lock`, a stray `managed_components/`, and (both
  scripts) an untracked component inside the ESP-IDF checkout.
- In the linked ELFs of all four builds (IDF dev/release, arduino-cli
  dev/release): `verifyRollbackLater` and `btInUse` are strong (`T`)
  symbols; `AudioFileSourceHTTPStream` has 0 symbols and none of its strings
  in both RELEASE images, and is present in both DEV images (so the gate's
  needle is real). The release app config keeps
  `CONFIG_BOOTLOADER_APP_ROLLBACK_ENABLE` and coredump-to-flash.
- Release bootloader rebuilt from the new tree: byte-identical bootloader
  (`ebabd354...`, 0x6000); the new partition table (`a3a0b813...`, encrypted
  coredump) equals the stdlib encoder's output for `partitions.csv`, and is
  recorded in `RELEASED.md` as `CANDIDATE` (old row `SUPERSEDED`).
- TEST signing offline (`sign_release.py app` / `bootloader` / `add`) and
  inline (`build_idf.sh` with a TEST digests file); `espsecure
  verify-signature` OK for the app and both bootloader blocks; gate PASS on
  the signed app ("5 TLS trust anchors"); FAIL on the unsigned app under
  `--require-sbv2`, a DEV image as release (now also naming the plain-TCP
  stream) and the committed 1.3.4 image (1 anchor). Bootloader gate: FAIL
  at the default `APPROVED` (the row is `CANDIDATE`), PASS with
  `--min-status CANDIDATE`. The app-config bootloader `build_idf.sh` also
  makes (19,984 B, no `CONFIG_SECURE_BOOT`), dual-signed with TEST keys:
  the gate refuses it ("not a build recorded") and `sign_release.py`
  refuses it raw and padded. With two NON-TEST keys `sign_release.py
  bootloader` refused `CANDIDATE` (with and without `--pilot`) and `PILOT`
  without `--pilot`, and signed `PILOT --pilot` and `APPROVED`.
- arduino-cli (core 3.3.8): DEV compile OK (1,665,670 B); `build_release.py`
  RELEASE end-to-end in a clean clone, gate PASS, TEST-signed, verify OK. A
  release build with `AREG_TLS_INSECURE`, an `http://` backend, an old
  config.h's `AREG_PROV_POP`, or only 2 trust anchors fails to compile with
  the guard's message.
- All host tests (`g++`, 11 suites) and Python tests (gate 32, partitions
  13, source rules 17, release tools 33, factory station 58 incl. the real
  `espefuse --virt` rehearsal, SD loader 20); backend `dotnet test` 3206
  (9 new: the `-sb` image gate).
- The factory flow against virtual eFuses with the real TEST bundle of this
  tree (signed app, dual-signed bootloader, pinned table + boot_app0): steps
  0-11 executed by the real tools, the MAC re-read before every burn, every
  summary asserted, a second run on the same virtual chip refused; the
  dry-run prints `--after no-reset` on every esptool command.

Not verified: anything on real silicon (section 12) -- in particular the
release bootloader booting QIO flash + octal PSRAM, Arduino `Update`
writing into an encrypted slot, the inactive-slot erase, the PENDING_VERIFY
rollback of a crashing image, the BLE purge reboot, the encrypted core
dump, the station's console checks and MAC re-reads against a real chip,
`--after no-reset` keeping a real chip in download mode, real eFuse burns,
BLE provisioning with the `btInUse()` fix, legacy-store mode on a real
old-table toy, the five-root TLS handshake against the live backend and its
internal-heap cost; the backend gate against a live Railway deployment. Not
done: anti-rollback and the crash-dump decision (owner, section 11 rules
8-9), the single combined 6+7 espefuse batch (needs QEMU + pilot proof), a
TLS stream source for release toys, the pilot-only test apps of section 12,
the repository visibility and the served-image move (owner, section 11).

## 16. Open before the pilot (final independent review, 2026-10-08)

The final review judged the code ready to commit and the procedure safe to
run on a SPARE board. These should-fix items stay open before any family's
toy is locked:

1. `secure_provision.py` steps 8–9 read the toy's console on `--port`, but the
   firmware prints its `[sec]` posture lines on the native USB-Serial/JTAG
   port (HWCDC), not UART0. Point the station at the right port, or print on
   both, before relying on those checks.
2. Before the irreversible burns, check that the image's compiled backend URL
   matches the `--backend-url` the device key was minted on.
3. Rule 2 (§11) asks every release to prove OTA to release+1 on a locked board;
   the pilot-evidence check compares only the bootloader, not the app.
4. The §12 pilot-only builds (release+1, the crashing release+2, the
   unsigned-slot test app, the revocation app) are not built yet. Ask Claude
   for them before the pilot.
5. The station does not check at run time that esptool/espefuse/espsecure are
   the versions the burn plan was verified with (5.2.0).
6. Backend board-model guard: it checks the image it read at startup only. It
   fails open if the image on the volume changes after startup or if `Url`
   points to an external host. Compare against `FirmwareUpdate:Sha256` or
   re-read on serve (backend change; needs owner approval).

## Appendix A -- design evidence (Espressif QEMU `-M esp32s3`, emulated eFuses, TEST keys only)

The flow of section 8 (steps 5 -> 6 -> 7 -> 8 -> 9, espefuse/esptool over a
socket into QEMU's ROM download mode), with the exact commands the station
now runs:

- **A** good: ROM `Valid secure boot key blocks: 0 1` / `secure boot verification succeeded`; app posture `sb=1 fe=1 ... hmac_key4=1 revoked=0x4`; `nvs_sec` secure init OK; identity read back; runtime write OK.
- **B** unsigned app -> refused (reset loop). **C** app signed by an untrusted key -> refused. **D** app signed by the backup only -> boots.
- **E** rogue-signed image in app1 with otadata selecting it -> the bootloader falls back to app0; no code from it runs.
- **F/G/H/I** tampered / plaintext / erased / wrong-HMAC-key `nvs_sec` -> `ESP_ERR_NVS_NOT_FOUND`, never garbage.
- **J** after `DIS_DOWNLOAD_MODE`: `sb_release_ok=1 fe_release_ok=1 dl=disabled`; download strap -> ROM `Download boot modes disabled`; esptool cannot connect.
- **K1/K2** slot 0 revoked: the dual-signed bootloader still verifies; a primary-only app is refused; a backup-signed app boots.
- **L** bootloader signed by the primary only + slot 0 revoked -> ROM `secure boot verification failed` (brick) -- why the bootloader is dual-signed.
- **M** the round-2 table (incl. `spiffs ... encrypted`, no `coredump` row) + Arduino `boot_app0.bin` at 0xe000 -> boots. The round-3 table (encrypted `coredump` at 0x7E0000, `spiffs` 64 KB smaller) was NOT re-run in QEMU; the pilot boots it.
- esptool 5.2.0 refuses `write-flash` to a flash-encrypted chip without `--force`; writing ciphertext to a BLANK chip first needs no `--force` (the order the station uses).
- Bootloader sizes by log level (unsigned / signed): INFO 0xA000/0xB000, WARN/ERROR 0x9000/0xA000, NONE **0x6000/0x7000** (fits), NONE + anti-rollback 0x7000/0x8000 (zero margin).
- App-side: `CONFIG_SECURE_DISABLE_ROM_DL_MODE` in an APP config would make the app burn `DIS_DOWNLOAD_MODE` at first boot (ESP-IDF v5.5.4 `components/efuse/src/esp_efuse_startup.c:116`), skipping step 8 -- which is why only the release BOOTLOADER carries the `CONFIG_SECURE_*` options.
