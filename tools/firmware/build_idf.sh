#!/usr/bin/env bash
# Build the Areg firmware with ESP-IDF (esp32/AregVoiceIdf, same sources as
# the Arduino sketch) in one of the two security profiles.
#
#   tools/firmware/build_idf.sh dev
#   tools/firmware/build_idf.sh release --version 1.4.0 [--forbid-version 1.3.4]
#
# Prerequisites: ESP-IDF v5.5.4 exported (`. $IDF_PATH/export.sh`); the
# pinned dependencies (tools/firmware/idf_fetch_deps.py, run automatically
# when $AREG_IDF_DEPS is empty). Every build asserts the pinned IDF commit,
# compiler (deps.lock TOOLCHAIN) and IDF's own esptool (deps.lock
# IDF_ESPTOOL -- it runs elf2image, so it decides the .bin bytes), and
# re-verifies every fetched dependency byte for byte
# (tools/firmware/release_checks.py idf-deps).
#
# RELEASE additionally refuses a dirty tree (the image and release.json are
# stamped with HEAD's commit) and a manifest HMAC key found in any firmware
# image ever committed to this repository (release_checks.py clean-tree /
# hmac-fresh), and needs in the environment:
#   AREG_BACKEND_BASE_URL    https://... (compiled in)
#   AREG_MANIFEST_HMAC_KEY   the fleet manifest key (compiled in; never echoed)
#   AREG_SB_TRUSTED_DIGESTS  optional; default esp32/security/sb_trusted_digests.txt
#                            (the owner's two public-key digests).
#
# SIGNING. A release stops at app-unsigned.bin by DEFAULT: the Secure Boot
# private key never sits on this online, repo-connected machine
# (esp32/security/README.md, docs/firmware-security.md s7). Carry
# app-unsigned.bin to the OFFLINE laptop: tools/firmware/sign_release.py app.
# Inline signing exists only for TEST runs: AREG_SB_SIGNING_KEY set AND a
# "TEST ONLY" trusted-digests file -> signed here, every output labelled TEST.
# AREG_SB_SIGNING_KEY with a real digests file is REFUSED. Inline signing
# needs esptool 5.2.0's espsecure (dash-style sign-data / verify-signature;
# the IDF env's espsecure.py 4.x does not have them) -- set ESPSECURE to it
# (esp32/AregVoiceIdf/README.md "One-time setup"). --unsigned forces the
# default even when AREG_SB_SIGNING_KEY is set.
#   ESPTOOL / ESPSECURE      optional commands (default: `python -m esptool`
#                            = IDF's esptool, `espsecure`).
#
# Outputs (esp32/AregVoiceIdf/out/<profile>/, gitignored):
#   dev:     app.bin
#   release: app-unsigned.bin (padded --secure-pad-v2) + release.json
#            (sha256s, sizes, version, IDF commit, toolchain, IDF esptool,
#            core, deps.lock sha256, full source commit); a TEST run also
#            app-signed.bin.
set -euo pipefail
# The ESP-IDF component manager is ON by default and would resolve
# arduino-esp32 3.3.8's idf_component.yml (version RANGES: mdns, esp_modem,
# esp-dsp, esp-sr, libsodium, ... none recorded in deps.lock) from
# components.espressif.com into esp32/AregVoiceIdf/managed_components/ -- an
# image that could change between two builds of one commit. Every dependency
# comes from deps.lock instead (review round 3).
export IDF_COMPONENT_MANAGER=0

PINNED_IDF_COMMIT="735507283d5b2f9fb363a1901172dbd9e847945d"
OTA_SLOT=$((0x300000))

REPO="$(cd "$(dirname "$0")/../.." && pwd)"
PROJ="$REPO/esp32/AregVoiceIdf"
GATE="$REPO/tools/firmware/check_release_image.py"
CHECKS="$REPO/tools/firmware/release_checks.py"

die() { echo "FAIL - $*" >&2; exit 1; }

profile="${1:-}"; shift || true
version=""; forbid=""; force_unsigned=0
while [ $# -gt 0 ]; do
  case "$1" in
    --version) version="${2:-}"; shift 2 ;;
    --forbid-version) forbid="${2:-}"; shift 2 ;;
    --unsigned) force_unsigned=1; shift ;;
    *) die "unknown argument: $1" ;;
  esac
done
case "$profile" in dev|release) ;; *) die "usage: build_idf.sh dev|release [--version X.Y.Z] [--forbid-version X.Y.Z] [--unsigned]" ;; esac

ESPTOOL="${ESPTOOL:-python -m esptool}"
ESPSECURE="${ESPSECURE:-espsecure}"
PY="${PYTHON:-python3}"
pin() { grep "^$1=" "$PROJ/deps.lock" | cut -d= -f2; }
TOOLCHAIN="$(pin TOOLCHAIN)"; IDF_ESPTOOL="$(pin IDF_ESPTOOL)"
[ -n "$TOOLCHAIN" ] && [ -n "$IDF_ESPTOOL" ] || die "deps.lock lacks TOOLCHAIN / IDF_ESPTOOL"

if [ "$profile" = "release" ]; then
  # Signing policy first (configuration only, no toolchain needed).
  [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "release needs --version MAJOR.MINOR.PATCH"
  trusted="${AREG_SB_TRUSTED_DIGESTS:-$REPO/esp32/security/sb_trusted_digests.txt}"
  [ -f "$trusted" ] || die "no trusted digests file ($trusted) -- the owner creates esp32/security/ from the PUBLIC keys (see esp32/security/README.md)"
  test_digests=0
  grep -q "TEST ONLY" "$trusted" && test_digests=1
  key="${AREG_SB_SIGNING_KEY:-}"
  sign_inline=0
  if [ -n "$key" ] && [ "$force_unsigned" -eq 0 ]; then
    [ "$test_digests" -eq 1 ] || die "AREG_SB_SIGNING_KEY is set but $trusted is not a TEST ONLY digests file. The Secure Boot private key never sits on the build machine -- unset it and sign offline: tools/firmware/sign_release.py app --keyfile sb_primary.pem --input $PROJ/out/release/app-unsigned.bin --expect-version $version --out-dir <bundle>"
    [ -f "$key" ] || die "AREG_SB_SIGNING_KEY: no such file"
    $ESPSECURE --help 2>/dev/null | grep -q 'espsecure v5\.2\.0' \
      || die "ESPSECURE ('$ESPSECURE') is not esptool 5.2.0's espsecure (IDF's espsecure.py 4.x lacks sign-data / verify-signature): python3 -m venv ~/areg-esptool && ~/areg-esptool/bin/pip install esptool==5.2.0; export ESPSECURE=~/areg-esptool/bin/espsecure"
    sign_inline=1
  fi
  # Provenance, before minutes of compiling: the stamp must be true.
  "$PY" "$CHECKS" clean-tree || die "release from a dirty tree -- commit first"
fi

# ---- the tools that decide the bytes ----------------------------------------
[ -n "${IDF_PATH:-}" ] || die "IDF_PATH is not set -- export ESP-IDF v5.5.4 first (. \$IDF_PATH/export.sh)"
command -v idf.py >/dev/null || die "idf.py not on PATH -- export ESP-IDF v5.5.4 first"
head="$(git -C "$IDF_PATH" rev-parse HEAD 2>/dev/null || true)"
[ "$head" = "$PINNED_IDF_COMMIT" ] || die "ESP-IDF at $IDF_PATH is ${head:-not a git checkout}, expected v5.5.4 $PINNED_IDF_COMMIT"
# HEAD alone would pass an edited IDF file, an untracked component under
# $IDF_PATH/components or a submodule (mbedtls, the Wi-Fi/BT/PHY libraries,
# micro-ecc) at another commit.
idf_status="$(git -C "$IDF_PATH" status --porcelain --ignore-submodules=none)" || die "git status failed in $IDF_PATH"
[ -z "$idf_status" ] || die "ESP-IDF checkout at $IDF_PATH has local changes (git -C \$IDF_PATH status --ignore-submodules=none)"
idf_subs="$(git -C "$IDF_PATH" submodule status --recursive)" || die "git submodule status failed in $IDF_PATH"
if grep -q '^[-+U]' <<<"$idf_subs"; then
  die "ESP-IDF submodules not at v5.5.4's pinned commits (git -C \$IDF_PATH submodule update --init --recursive)"
fi
# Component-manager output from an earlier unpinned run (managed_components/,
# dependencies.lock) would be compiled in. Not gitignored on purpose: the
# clean-tree check catches them too.
[ ! -e "$PROJ/dependencies.lock" ] && [ ! -e "$PROJ/managed_components" ] \
  || die "component-manager output in $PROJ -- delete dependencies.lock / managed_components; deps come only from deps.lock"
gcc_v="$(xtensa-esp32s3-elf-gcc --version 2>/dev/null | head -1 || true)"
case "$gcc_v" in
  *" $TOOLCHAIN) "*) ;;   # "xtensa-esp-elf-gcc (crosstool-NG esp-14.2.0_20260121) 14.2.0"
  *) die "compiler is '${gcc_v:-missing}', pinned $TOOLCHAIN (deps.lock) -- reinstall ESP-IDF v5.5.4's tools" ;;
esac
idf_esptool_v="$(python -m esptool version 2>/dev/null | tail -1 || true)"
[ "$idf_esptool_v" = "$IDF_ESPTOOL" ] \
  || die "IDF's esptool (python -m esptool) is '${idf_esptool_v:-missing}', pinned $IDF_ESPTOOL (deps.lock) -- it runs elf2image and decides the .bin bytes"

# ---- pinned dependencies -----------------------------------------------------
export AREG_IDF_DEPS="${AREG_IDF_DEPS:-${XDG_CACHE_HOME:-$HOME/.cache}/areg-idf-deps}"
if [ ! -f "$AREG_IDF_DEPS/components/arduino/CMakeLists.txt" ]; then
  "$PY" "$REPO/tools/firmware/idf_fetch_deps.py" --deps "$AREG_IDF_DEPS"
fi
# Every build: pinned commit, NO modified or untracked file (only the IDF
# wrapper CMakeLists.txt, compared with the repo's), the network_provisioning
# tree hash, the Arduino libs' sdkconfig sha256 -- commit equality alone would
# pass a hand-edited file at the right commit.
"$PY" "$CHECKS" idf-deps --deps "$AREG_IDF_DEPS" || die "pinned dependencies drifted -- rerun idf_fetch_deps.py"

build_tag="$(git -C "$REPO" rev-parse --short=12 HEAD 2>/dev/null || echo unknown)"
build_commit="$(git -C "$REPO" rev-parse HEAD 2>/dev/null || echo unknown)"
if [ "$profile" = "dev" ] && [ -n "$(git -C "$REPO" status --porcelain -- esp32/AregVoiceMvp esp32/AregVoiceIdf 2>/dev/null)" ]; then
  build_tag="$build_tag-dirty"   # bench builds may come from a dirty tree; their stamp says so
fi
# __DATE__/__TIME__ (e.g. content_sync's one-line build stamp) come from the
# commit time, not the wall clock, so two builds of one commit are identical.
SOURCE_DATE_EPOCH="${SOURCE_DATE_EPOCH:-$(git -C "$REPO" log -1 --format=%ct 2>/dev/null || echo 0)}"
export SOURCE_DATE_EPOCH
B="$PROJ/build-$profile"
OUT="$PROJ/out/$profile"
mkdir -p "$OUT"

if [ "$profile" = "dev" ]; then
  args=(-DAREG_PROFILE=dev "-DAREG_FW_BUILD=idf-dev-$build_tag")
  [ -n "$version" ] && args+=("-DAREG_FW_VERSION=$version")
  (cd "$PROJ" && idf.py -B "$B" -DSDKCONFIG="$B/sdkconfig" "${args[@]}" build)
  cp "$B/AregVoiceMvp.bin" "$OUT/app.bin"
  size=$(stat -c %s "$OUT/app.bin")
  [ "$size" -le "$OTA_SLOT" ] || die "app.bin is $size B, over the 3 MB OTA slot"
  printf 'dev  app.bin %s B (%d%% of the 3 MB slot) sha256 %s\n' "$size" $((size * 100 / OTA_SLOT)) "$(sha256sum "$OUT/app.bin" | cut -d' ' -f1)"
  echo "Bench flash: (cd esp32/AregVoiceIdf && idf.py -B build-dev flash), or the normal Arduino upload."
  exit 0
fi

# ---- release -------------------------------------------------------------------
[ -n "${AREG_BACKEND_BASE_URL:-}" ] || die "set AREG_BACKEND_BASE_URL (https://...)"
[ -n "${AREG_MANIFEST_HMAC_KEY:-}" ] || die "set AREG_MANIFEST_HMAC_KEY (the fleet manifest key; never echoed)"
"$PY" "$CHECKS" hmac-fresh || die "use a NEW manifest HMAC key (backend FirmwareUpdate and every release together)"

# A FRESH build directory every release, not just a fresh sdkconfig: ninja
# does not track SOURCE_DATE_EPOCH, so an incremental build after a new commit
# keeps objects whose __DATE__/__TIME__ came from the PREVIOUS commit (seen
# 2026-10-08: the core's "Software Info" stamp), and the image then differs
# from a clean build of the same commit -- "rebuild from the tag and compare"
# would fail. The defaults files are the only configuration input.
rm -rf "$B"
(cd "$PROJ" && idf.py -B "$B" -DSDKCONFIG="$B/sdkconfig" -DAREG_PROFILE=release \
    "-DAREG_FW_VERSION=$version" "-DAREG_BACKEND_BASE_URL=$AREG_BACKEND_BASE_URL" \
    "-DAREG_FW_BUILD=release-$build_tag" build)
grep -q '^#define CONFIG_APP_REPRODUCIBLE_BUILD 1$' "$B/config/sdkconfig.h" || die "release config is not reproducible"
# Anti-rollback is decided ONCE, in the frozen release bootloader
# (esp32/bootloader-release/sdkconfig.defaults; docs/firmware-security.md s11
# rule 8). When that bootloader enforces it, every release app must carry a
# secure version (the bootloader refuses an app below the eFuse counter, and
# the app bumps the counter once it is confirmed).
if grep -q '^CONFIG_BOOTLOADER_APP_ANTI_ROLLBACK=y' "$REPO/esp32/bootloader-release/sdkconfig.defaults"; then
  grep -q '^#define CONFIG_BOOTLOADER_APP_ANTI_ROLLBACK 1$' "$B/config/sdkconfig.h" \
    || die "the release bootloader enforces anti-rollback: set CONFIG_BOOTLOADER_APP_ANTI_ROLLBACK=y in esp32/AregVoiceIdf/sdkconfig.defaults.release"
  grep -q '^#define CONFIG_BOOTLOADER_APP_SECURE_VERSION ' "$B/config/sdkconfig.h" \
    || die "the release bootloader enforces anti-rollback: set CONFIG_BOOTLOADER_APP_SECURE_VERSION=<n> in esp32/AregVoiceIdf/sdkconfig.defaults.release"
elif grep -q '^#define CONFIG_BOOTLOADER_APP_ANTI_ROLLBACK 1$' "$B/config/sdkconfig.h"; then
  die "the release app enables anti-rollback but esp32/bootloader-release does not -- decide it in the bootloader first"
fi
if grep -qE '^#define CONFIG_(SECURE_BOOT|SECURE_FLASH_ENC_ENABLED|NVS_ENCRYPTION|SECURE_DISABLE_ROM_DL_MODE) 1$' "$B/config/sdkconfig.h"; then
  die "release APP config must not set CONFIG_SECURE_*/CONFIG_NVS_ENCRYPTION (see sdkconfig.defaults.release)"
fi

# The exact elf2image arguments IDF used (build.ninja), re-run twice: once to
# prove they reproduce IDF's own .bin byte for byte, once with --secure-pad-v2.
cmdline="$(grep -o 'elf2image .* -o [^ ]*/AregVoiceMvp\.bin' "$B/build.ninja" | head -1)"
[ -n "$cmdline" ] || die "could not find the elf2image command in $B/build.ninja"
read -r -a e2i <<<"$(echo "$cmdline" | sed -e 's/^elf2image //' -e 's/ -o [^ ]*$//')"
$ESPTOOL --chip esp32s3 elf2image "${e2i[@]}" -o "$OUT/check.bin" "$B/AregVoiceMvp.elf" >/dev/null
cmp -s "$OUT/check.bin" "$B/AregVoiceMvp.bin" || die "re-running elf2image did not reproduce IDF's AregVoiceMvp.bin -- args drifted"
rm -f "$OUT/check.bin"
$ESPTOOL --chip esp32s3 elf2image "${e2i[@]}" --secure-pad-v2 -o "$OUT/app-unsigned.bin" "$B/AregVoiceMvp.elf" >/dev/null

gate_args=(--expect-version "$version" --profile release)
[ -n "$forbid" ] && gate_args+=(--forbid-version "$forbid")
"$PY" "$GATE" "$OUT/app-unsigned.bin" "${gate_args[@]}" || die "release gate refused app-unsigned.bin"

sha_u=$(sha256sum "$OUT/app-unsigned.bin" | cut -d' ' -f1)
signed_json="null"
rm -f "$OUT/app-signed.bin"
if [ "$sign_inline" -eq 1 ]; then
  $ESPSECURE sign-data --version 2 --keyfile "$key" --output "$OUT/app-signed.bin" "$OUT/app-unsigned.bin" >/dev/null
  $ESPSECURE verify-signature --version 2 --keyfile "$key" "$OUT/app-signed.bin" | grep -q "verification successful" \
    || die "espsecure verify-signature failed on app-signed.bin"
  "$PY" "$GATE" "$OUT/app-signed.bin" "${gate_args[@]}" --require-sbv2 --trusted-digests "$trusted" \
    || die "release gate refused app-signed.bin"
  sha_s=$(sha256sum "$OUT/app-signed.bin" | cut -d' ' -f1)
  size_s=$(stat -c %s "$OUT/app-signed.bin")
  signed_json="{\"file\": \"app-signed.bin\", \"sha256\": \"$sha_s\", \"size\": $size_s}"
fi

deps_sha=$(sha256sum "$PROJ/deps.lock" | cut -d' ' -f1)
cat > "$OUT/release.json" <<JSON
{
  "version": "$version",
  "profile": "release",
  "test_signed": $([ "$sign_inline" -eq 1 ] && echo true || echo false),
  "app_unsigned": {"file": "app-unsigned.bin", "sha256": "$sha_u", "size": $(stat -c %s "$OUT/app-unsigned.bin")},
  "app_signed": $signed_json,
  "idf_commit": "$PINNED_IDF_COMMIT",
  "toolchain": "$TOOLCHAIN",
  "idf_esptool": "$idf_esptool_v",
  "arduino_core": "3.3.8",
  "deps_lock_sha256": "$deps_sha",
  "source_commit": "$build_commit",
  "source_tree": "clean"
}
JSON
if [ "$sign_inline" -eq 0 ]; then
  printf 'release app-unsigned.bin %s B sha256 %s\n' "$(stat -c %s "$OUT/app-unsigned.bin")" "$sha_u"
  echo "Stopped at $OUT/app-unsigned.bin (the default). Sign it OFFLINE:"
  echo "  python3 tools/firmware/sign_release.py app --keyfile sb_primary.pem --input app-unsigned.bin --out-dir <bundle> --expect-version $version"
  exit 0
fi
printf 'release app-signed.bin %s B (%d%% of the 3 MB slot) sha256 %s\n' "$size_s" $((size_s * 100 / OTA_SLOT)) "$sha_s"
echo "*** TEST-SIGNED: verified only against a TEST digests file. NOT A RELEASE. Never stage it. ***"
