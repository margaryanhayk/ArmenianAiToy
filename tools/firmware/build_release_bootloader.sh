#!/usr/bin/env bash
# Build the Areg RELEASE bootloader (Secure Boot V2 + flash encryption,
# logging off) from esp32/bootloader-release/. Run once per hardware
# generation, on the build machine, with ESP-IDF v5.5.4 exported
# (`. $IDF_PATH/export.sh`). The output is UNSIGNED; it is signed offline
# with BOTH keys by tools/firmware/sign_release.py and never signed in the
# repo. docs/firmware-security.md s2.3 / s6.1.
#
#   tools/firmware/build_release_bootloader.sh
#
# Checks: a clean tree (the recorded build must be reproducible from a
# commit); the IDF checkout is the pinned commit (the one the pinned Arduino
# core 3.3.8's precompiled libs were built from -- versions.txt
# "esp-idf: v5.5.4 735507283d"); the compiler and IDF's own esptool are the
# deps.lock pins (TOOLCHAIN, IDF_ESPTOOL -- both decide the bytes); the
# bootloader fits (<= 0x7000 unsigned, so <= 0x8000 once the 4 KB signature
# sector is appended); the resolved sdkconfig has the values the design
# measured. Prints a RELEASED.md row with all of it.
set -euo pipefail
# Never let the ESP-IDF component manager fetch anything (see build_idf.sh).
export IDF_COMPONENT_MANAGER=0

PINNED_IDF_COMMIT="735507283d5b2f9fb363a1901172dbd9e847945d"
MAX_UNSIGNED=$((0x7000))

REPO="$(cd "$(dirname "$0")/../.." && pwd)"
PROJ="$REPO/esp32/bootloader-release"
LOCK="$REPO/esp32/AregVoiceIdf/deps.lock"

die() { echo "FAIL - $*" >&2; exit 1; }

pin() { grep "^$1=" "$LOCK" | cut -d= -f2; }
TOOLCHAIN="$(pin TOOLCHAIN)"; IDF_ESPTOOL="$(pin IDF_ESPTOOL)"
[ -n "$TOOLCHAIN" ] && [ -n "$IDF_ESPTOOL" ] || die "deps.lock lacks TOOLCHAIN / IDF_ESPTOOL"
"${PYTHON:-python3}" "$REPO/tools/firmware/release_checks.py" clean-tree esp32/bootloader-release esp32/AregVoiceMvp/partitions.csv tools/firmware \
  || die "bootloader from a dirty tree -- commit first (RELEASED.md records a commit)"

[ -n "${IDF_PATH:-}" ] || die "IDF_PATH is not set -- export ESP-IDF v5.5.4 first (. \$IDF_PATH/export.sh)"
command -v idf.py >/dev/null || die "idf.py not on PATH -- export ESP-IDF v5.5.4 first"
head="$(git -C "$IDF_PATH" rev-parse HEAD 2>/dev/null || true)"
[ "$head" = "$PINNED_IDF_COMMIT" ] || die "ESP-IDF at $IDF_PATH is ${head:-not a git checkout}, expected v5.5.4 $PINNED_IDF_COMMIT"
# The frozen-for-life bootloader is built from IDF itself: an edited file, an
# untracked component or a submodule (mbedtls, micro-ecc...) at another
# commit must not pass on HEAD alone.
idf_status="$(git -C "$IDF_PATH" status --porcelain --ignore-submodules=none)" || die "git status failed in $IDF_PATH"
[ -z "$idf_status" ] || die "ESP-IDF checkout at $IDF_PATH has local changes (git -C \$IDF_PATH status --ignore-submodules=none)"
idf_subs="$(git -C "$IDF_PATH" submodule status --recursive)" || die "git submodule status failed in $IDF_PATH"
if grep -q '^[-+U]' <<<"$idf_subs"; then
  die "ESP-IDF submodules not at v5.5.4's pinned commits (git -C \$IDF_PATH submodule update --init --recursive)"
fi
[ ! -e "$PROJ/dependencies.lock" ] && [ ! -e "$PROJ/managed_components" ] \
  || die "component-manager output in $PROJ -- delete dependencies.lock / managed_components"
gcc_v="$(xtensa-esp32s3-elf-gcc --version 2>/dev/null | head -1 || true)"
case "$gcc_v" in
  *" $TOOLCHAIN) "*) ;;
  *) die "compiler is '${gcc_v:-missing}', pinned $TOOLCHAIN (deps.lock)" ;;
esac
idf_esptool_v="$(python -m esptool version 2>/dev/null | tail -1 || true)"
[ "$idf_esptool_v" = "$IDF_ESPTOOL" ] || die "IDF's esptool is '${idf_esptool_v:-missing}', pinned $IDF_ESPTOOL (deps.lock)"
commit="$(git -C "$REPO" rev-parse --short=12 HEAD)"
cmp -s "$PROJ/partitions.csv" "$REPO/esp32/AregVoiceMvp/partitions.csv" \
  || die "esp32/bootloader-release/partitions.csv differs from the sketch's partitions.csv"

cd "$PROJ"
# A fresh sdkconfig every time: the defaults file is the only input.
rm -f sdkconfig
idf.py -B build bootloader partition-table

cfg="build/config/sdkconfig.h"
want() { grep -q "^#define $1 $2\$" "$cfg" || die "resolved config: expected $1 $2 (see $cfg)"; }
want CONFIG_SECURE_BOOT_V2_ENABLED 1
want CONFIG_SECURE_FLASH_ENC_ENABLED 1
want CONFIG_SECURE_FLASH_ENCRYPTION_AES256 1
want CONFIG_SECURE_FLASH_ENCRYPTION_MODE_RELEASE 1
want CONFIG_SECURE_DISABLE_ROM_DL_MODE 1
want CONFIG_BOOTLOADER_LOG_LEVEL_NONE 1
want CONFIG_BOOTLOADER_APP_ROLLBACK_ENABLE 1
want CONFIG_ESPTOOLPY_NO_STUB 1
want CONFIG_ESPTOOLPY_FLASHMODE '"dio"'
want CONFIG_ESPTOOLPY_FLASHMODE_QIO 1
want CONFIG_BOOTLOADER_COMPILER_OPTIMIZATION_SIZE 1
want CONFIG_PARTITION_TABLE_OFFSET 0x8000
# Anti-rollback is the owner's one-time decision (docs/firmware-security.md
# s11 rule 8): it follows sdkconfig.defaults here and nothing else.
anti_rollback=off
if grep -q '^CONFIG_BOOTLOADER_APP_ANTI_ROLLBACK=y' sdkconfig.defaults; then
  want CONFIG_BOOTLOADER_APP_ANTI_ROLLBACK 1
  anti_rollback=on
elif grep -q '^#define CONFIG_BOOTLOADER_APP_ANTI_ROLLBACK 1' "$cfg"; then
  die "anti-rollback on without sdkconfig.defaults saying so"
fi
if grep -q "^#define CONFIG_BOOTLOADER_COMPILE_TIME_DATE 1" "$cfg"; then die "the bootloader must not embed a build date (reproducible bytes)"; fi
if grep -q "^#define CONFIG_SECURE_BOOT_ENABLE_AGGRESSIVE_KEY_REVOKE 1" "$cfg"; then die "aggressive revoke must stay OFF"; fi
if grep -q "^#define CONFIG_SECURE_BOOT_BUILD_SIGNED_BINARIES 1" "$cfg"; then die "the bootloader is never signed by the build"; fi

bl="build/bootloader/bootloader.bin"
[ -f "$bl" ] || die "no $bl"
size=$(stat -c %s "$bl")
[ "$size" -le "$MAX_UNSIGNED" ] || die "bootloader is $(printf 0x%x "$size") bytes, over 0x7000 -- it would not fit below the partition table once signed"

mkdir -p out
cp "$bl" out/bootloader-unsigned.bin
cp build/partition_table/partition-table.bin out/partition-table.bin
sha=$(sha256sum out/bootloader-unsigned.bin | cut -d' ' -f1)
pt_sha=$(sha256sum out/partition-table.bin | cut -d' ' -f1)
printf 'bootloader-unsigned.bin  %s bytes (0x%x)  sha256 %s\n' "$size" "$size" "$sha"
printf 'partition-table.bin      %s bytes          sha256 %s\n' "$(stat -c %s out/partition-table.bin)" "$pt_sha"
echo "anti-rollback            $anti_rollback (write it into the row's Status note)"
echo "RELEASED.md row:"
printf '| %s | <you> | %s | %s | %s | 0x%x (%s B) | `%s` | `%s` | CANDIDATE |\n' "$(date -u +%F)" "${PINNED_IDF_COMMIT:0:10}" \
  "$TOOLCHAIN" "esptool $idf_esptool_v, repo $commit" "$size" "$size" "$sha" "$pt_sha"
echo "Record it in esp32/bootloader-release/RELEASED.md (Status CANDIDATE). sign_release.py signs only a"
echo "recorded build: PILOT (you rebuilt and compared; --pilot, for the 2 pilot boards) or APPROVED (after the"
echo "hardware pilot and the anti-rollback decision -- the only status real toys accept). Then, OFFLINE:"
echo "  python3 tools/firmware/sign_release.py bootloader --keyfile sb_primary.pem --backup-keyfile sb_backup.pem \\"
echo "      --input esp32/bootloader-release/out/bootloader-unsigned.bin --out-dir <bundle> [--pilot]"
