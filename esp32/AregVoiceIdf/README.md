# AregVoiceIdf -- the firmware as an ESP-IDF project

Same sources as the Arduino sketch (`../AregVoiceMvp`): nothing is copied
into the repo. At configure time the sketch is staged into the build
directory (what arduino-cli does with its own `build/sketch/`), and every
staged file is a configure dependency, so `idf.py build` picks up edits.

**Which build is canonical**

| Use | Build |
|---|---|
| RELEASE image for locked toys | **this project**, `tools/firmware/build_idf.sh release` -- pinned ESP-IDF v5.5.4, arduino-esp32 3.3.8 and libraries (`deps.lock`), byte-reproducible (verified: two clean builds in different directories, identical `.bin`) |
| Bench (DEV) | either: Arduino IDE / arduino-cli as before, or `build_idf.sh dev` |
| RELEASE on the Windows machine without IDF | `tools/firmware/build_release.py` (arduino-cli) is a supported fallback -- same RELEASE profile and gate, but library versions are that machine's, so not reproducible elsewhere |

The release BOOTLOADER is a separate project, `../bootloader-release`
(Secure Boot V2 + flash encryption, logs off), built once per hardware
generation. See `docs/firmware-security.md`.

## One-time setup (build machine)

```bash
# ESP-IDF v5.5.4 (the commit the 3.3.8 core's precompiled libs were built from)
git clone --depth 1 --branch v5.5.4 --recurse-submodules --shallow-submodules \
    https://github.com/espressif/esp-idf.git ~/esp/esp-idf-v5.5.4
~/esp/esp-idf-v5.5.4/install.sh esp32s3
. ~/esp/esp-idf-v5.5.4/export.sh
# never let IDF's component manager fetch unpinned components (build_idf.sh
# exports this itself; needed only for direct idf.py use):
export IDF_COMPONENT_MANAGER=0
# pinned Arduino core + libraries + the libs' sdkconfig, OUTSIDE the repo
python3 tools/firmware/idf_fetch_deps.py          # -> ~/.cache/areg-idf-deps
# esptool 5.2.0's espsecure, ONLY for TEST-key inline signing (the IDF env's
# espsecure.py 4.x lacks the dash-style sign-data / verify-signature):
python3 -m venv ~/areg-esptool && ~/areg-esptool/bin/pip install esptool==5.2.0
export ESPSECURE=~/areg-esptool/bin/espsecure
```

Every build asserts the compiler (`deps.lock` TOOLCHAIN =
`esp-14.2.0_20260121`) and IDF's own esptool (IDF_ESPTOOL = 4.12.0 -- it
runs elf2image, and comes from Espressif's downloaded constraints file, so
a later install could differ) and re-verifies every fetched dependency
(commit, no modified/untracked file except the IDF wrapper, content hashes).

`components.espressif.com` is not needed: `build_idf.sh` disables the
component manager (`IDF_COMPONENT_MANAGER=0`; IDF turns it on by default and
it would resolve arduino-esp32's `idf_component.yml` version ranges into an
unpinned `managed_components/`) and refuses a `dependencies.lock` /
`managed_components` left in this directory by an earlier direct `idf.py`
run. Every dependency is pinned in `deps.lock` and fetched from GitHub /
dl.espressif.com, commit- and sha256-checked. The ESP-IDF checkout itself
must be exactly v5.5.4 with no local change and every submodule at its
pinned commit (`git status --ignore-submodules=none`, `git submodule
status --recursive`), for the app and the release bootloader alike.

## Build

```bash
tools/firmware/build_idf.sh dev
AREG_BACKEND_BASE_URL=https://... AREG_MANIFEST_HMAC_KEY=... \
  tools/firmware/build_idf.sh release --version 1.4.0 --forbid-version 1.3.4
# -> out/release/app-unsigned.bin + release.json; then sign OFFLINE:
#    tools/firmware/sign_release.py app ...
```

`build_idf.sh release` stops at `app-unsigned.bin` by default -- the Secure
Boot private key never sits on this machine. It refuses a dirty tree (the
image and `release.json` carry HEAD's commit) and a manifest HMAC key found
in any firmware image ever committed to the repository. Inline signing is
for TEST runs only: `AREG_SB_SIGNING_KEY` + a `TEST ONLY` trusted-digests
file; `AREG_SB_SIGNING_KEY` with the real digests file is refused.

## Profiles

- `sdkconfig.defaults` -- common: arduino-esp32 3.3.8's own esp32s3 config
  (fetched) + the FQBN (8 MB QIO 80 MHz, octal PSRAM, 240 MHz, NimBLE,
  Wi-Fi provisioning, this sketch's `partitions.csv`).
- `sdkconfig.defaults.dev` -- nothing extra; bench flags allowed in config.h.
- `sdkconfig.defaults.release` -- reproducible build. It deliberately does
  NOT set `CONFIG_SECURE_*`/`CONFIG_NVS_ENCRYPTION` for the app (read the
  file for why); Secure Boot, flash encryption, JTAG and download-mode lock
  are the release bootloader's and the factory eFuses' job, NVS encryption
  is `secure_store.cpp`'s.

Release values reach the code through a generated `areg_build_defs.h`
(force-included into the sketch only): `AREG_SECURITY_PROFILE_RELEASE`,
the version, the https backend URL, the manifest HMAC key (read from the
environment, never stored in CMakeCache), `AREG_CONTENT_SYNC_BENCH`. A
RELEASE build always uses `config.h.example`, never a developer's local
`config.h`.
