#!/usr/bin/env bash
#
# bench_session.sh — everything the computer can do for a bench session,
# in one command. Written 2026-09-16, reworked 2026-10-22 so the evening
# starts with the verified behaviour (regression) and ends with the SHIP.md
# evidence, not with a feature nobody has ever flashed.
#
# WHAT THIS DOES NOT DO: press the button, listen to the audio, or decide
# whether the toy sounds right. Those are yours. This script exists so the
# only thing you have to get right by hand is the cable.
#
# Steps, in order, each skippable:
#   0. prerequisites: esp32 core 3.3.6, a NEW firmware label, stray flags
#   1. pull the latest main
#   2. set the firmware label (--label) / enable fast mode (--stream)
#   3. compile with the canonical FQBN
#   4. upload to the toy
#   5. load the SD card with the whole current library
#
# Your config.h is never replaced. By DEFAULT it is not edited at all. Only
# with --label (the two AREG_FW_* define lines) and --stream (one commented
# line uncommented) are single lines rewritten, and a timestamped backup is
# written first. The device key is read from the environment and is never
# printed, logged, or passed on a command line.
#
# USAGE
#   tools/firmware/bench_session.sh --port COM7 --label 1.3.5-bench
#   tools/firmware/bench_session.sh --port /dev/ttyUSB0 --sd /media/AREG
#   tools/firmware/bench_session.sh --port COM7 --skip-pull
#   tools/firmware/bench_session.sh --port COM7 --stream     # 2nd flash only
#   tools/firmware/bench_session.sh --check          # prerequisites only
#
# --label VER   rewrite AREG_FW_VERSION to VER and AREG_FW_BUILD to
#               bench-<today> in config.h. Needed unless config.h already
#               carries a label that is NOT one of the old ones (1.0.0,
#               1.3.2, 1.3.3, 1.3.4): tonight's logs must be distinguishable
#               from the 1.3.3 logs of 2026-08/09. The device reads
#               1.3.5-bench as 1.3.5 (tolerant parse, ota_apply.cpp).
# --stream      uncomment AREG_QA_STREAM_PLAYBACK. OFF by default: the first
#               flash is the regression build, and the chunked streaming
#               de-framer has never run on hardware (only its pre-negotiation
#               predecessor has). Use it for the optional second flash (N5).
#
# For the SD step, export these first (never pass them as flags — they would
# land in your shell history):
#   export AREG_DEVICE_ID=...
#   export AREG_DEVICE_KEY=...
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SKETCH_DIR="$REPO_ROOT/esp32/AregVoiceMvp"
CONFIG_H="$SKETCH_DIR/config.h"
FLAG="AREG_QA_STREAM_PLAYBACK"
FQBN="esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc"
BACKEND="${AREG_BACKEND:-https://armenianaitoy-production.up.railway.app}"
WANT_CORE="3.3.6"
OLD_LABELS=" 1.0.0 1.3.2 1.3.3 1.3.4 "

PORT=""
SD_MOUNT=""
SKIP_PULL=0
SKIP_FLASH=0
CHECK_ONLY=0
STREAM=0
LABEL=""

say()  { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
ok()   { printf '    \033[32mOK\033[0m   %s\n' "$*"; }
warn() { printf '    \033[33mWARN\033[0m %s\n' "$*"; }
die()  { printf '\n\033[31mSTOP: %s\033[0m\n' "$*" >&2; exit 1; }

while [ $# -gt 0 ]; do
  case "$1" in
    --port)       PORT="${2:-}"; shift 2 ;;
    --sd)         SD_MOUNT="${2:-}"; shift 2 ;;
    --backend)    BACKEND="${2:-}"; shift 2 ;;
    --label)      LABEL="${2:-}"; shift 2 ;;
    --stream)     STREAM=1; shift ;;
    --skip-pull)  SKIP_PULL=1; shift ;;
    --skip-flash) SKIP_FLASH=1; shift ;;
    --check)      CHECK_ONLY=1; shift ;;
    -h|--help)    awk 'NR>1 && /^#/ {sub(/^# ?/, ""); print; next} NR>1 {exit}' "${BASH_SOURCE[0]}"; exit 0 ;;
    *)            die "unknown argument: $1  (try --help)" ;;
  esac
done

# The string inside the quotes of `#define NAME "..."` in config.h, or empty.
cfg_str() {
  { grep -E "^[[:space:]]*#define[[:space:]]+$1[[:space:]]+\"" "$CONFIG_H" || true; } \
    | head -1 | sed -E 's/^[^"]*"([^"]*)".*/\1/'
}
cfg_defined() {   # is `#define NAME` active (not commented out) in config.h?
  grep -qE "^[[:space:]]*#define[[:space:]]+$1([[:space:]]|\$)" "$CONFIG_H"
}

# ---------------------------------------------------------------- step 0
say "Step 0 — prerequisites"

command -v git >/dev/null || die "git not found"
ok "git"

if command -v arduino-cli >/dev/null; then
  ok "arduino-cli ($(arduino-cli version 2>/dev/null | head -1))"
else
  warn "arduino-cli NOT found — steps 3 and 4 will be skipped"
  SKIP_FLASH=1
fi

command -v python3 >/dev/null && ok "python3" || warn "python3 not found — step 5 and bench_watch.py unavailable"

if [ -f "$CONFIG_H" ]; then
  ok "config.h present"
else
  die "no config.h at $CONFIG_H
       Copy it from config.h.example and fill in your Wi-Fi and backend URL first.
       It is gitignored on purpose — real credentials must never be committed."
fi

# --- ESP32 core. config.h.example:529-533: Arduino-ESP32 3.3.7 and 3.3.8
# crash on the S3 inside btdm_controller_init the moment BLE provisioning
# starts (LoadProhibited boot loop; arduino-esp32 #12436 / #12357). BLE
# provisioning is ON by default (config.h.example:527), and the release
# machine builds with 3.3.6 (docs/ota-release-runbook.md). A toy that
# boot-loops on the bench is the core, not the firmware.
CORE_VER=""
if command -v arduino-cli >/dev/null; then
  CORE_VER="$(arduino-cli core list 2>/dev/null | awk '$1=="esp32:esp32"{print $2; exit}' || true)"
fi
BLE_ON=0
cfg_defined AREG_USE_BLE_PROVISIONING && BLE_ON=1
if [ "$SKIP_FLASH" = "1" ]; then
  :   # nothing will be compiled by this run
elif [ -z "$CORE_VER" ]; then
  warn "the esp32:esp32 core is not installed:  arduino-cli core install esp32:esp32@$WANT_CORE"
elif [ "$CORE_VER" = "$WANT_CORE" ]; then
  ok "esp32 core $CORE_VER"
elif [ "$BLE_ON" = "1" ] && { [ "$CORE_VER" = "3.3.7" ] || [ "$CORE_VER" = "3.3.8" ]; }; then
  die "esp32 core is $CORE_VER and BLE provisioning is on in your config.h.
       3.3.7 / 3.3.8 boot-loop in btdm_controller_init (config.h.example:529-533).
       Fix:  arduino-cli core install esp32:esp32@$WANT_CORE
       (then re-run). CLAUDE.md names 3.3.8: that is what the cloud container
       compiled with; the release machine and the bench PC need $WANT_CORE."
else
  warn "esp32 core is $CORE_VER; the release machine builds with $WANT_CORE"
fi

# --- Firmware label. Unless config.h sets AREG_FW_VERSION the toy reports
# 1.0.0 (ota_foundation.h default); if it still says 1.3.3 tonight's logs
# are indistinguishable from the 2026-08/09 logs.
FW_VER="$(cfg_str AREG_FW_VERSION)"
FW_BUILD="$(cfg_str AREG_FW_BUILD)"
LABEL_OK=1
if [ -n "$LABEL" ]; then
  [[ "$LABEL" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?$ ]] \
    || die "--label must look like 1.3.5-bench (MAJOR.MINOR.PATCH with an optional -suffix)"
  ok "label will be set to $LABEL (build bench-$(date +%Y%m%d)) in step 2"
elif [ -z "$FW_VER" ]; then
  LABEL_OK=0
  warn "config.h does not set AREG_FW_VERSION: the toy would report fw=1.0.0 build=dev"
elif [[ "$OLD_LABELS" == *" $FW_VER "* ]]; then
  LABEL_OK=0
  warn "config.h still says AREG_FW_VERSION \"$FW_VER\" (build \"$FW_BUILD\"): tonight's logs"
  warn "would look like the old ones. Pass  --label 1.3.5-bench  (or edit the two lines)."
else
  ok "label: fw=$FW_VER build=${FW_BUILD:-?}"
fi

# A bench build flag left in config.h once ate the button for two evenings.
#
# AREG_CONTENT_SYNC_BENCH is deliberately EXCLUDED from this check. Despite
# the "BENCH" in its name it is load-bearing: a build without it downloads
# nothing, silently, forever — no story, game clip or voice clip ever reaches
# the card. config.h.example defines it on purpose so it cannot be forgotten.
# Warning about it would talk you into breaking content sync.
BENCH_RE='^[[:space:]]*#define[[:space:]]+AREG_[A-Z0-9_]*(BENCH|_ONCE|_FORCE)'
BENCH_HITS="$(grep -nE "$BENCH_RE" "$CONFIG_H" | grep -v 'AREG_CONTENT_SYNC_BENCH' || true)"
if [ -n "$BENCH_HITS" ]; then
  warn "$(printf '%s\n' "$BENCH_HITS" | wc -l | tr -d ' ') bench/one-shot flag(s) are ENABLED in your config.h:"
  printf '%s\n' "$BENCH_HITS" | sed 's/^/         /'
  warn "These change startup behaviour. Comment them out unless you want them tonight."
  warn "(AREG_CONTENT_SYNC_BENCH is not listed — it is load-bearing, leave it ON.)"
else
  ok "no stray bench/one-shot flags enabled"
fi

# The three optional feature gates. Their dashboard toggles do NOTHING unless
# the matching firmware gate is compiled in (default 0 in both places).
state() { if cfg_defined "$1"; then printf 'ON'; else printf 'off'; fi; }
printf '    Optional gates in config.h:  %s %s   AREG_VARIANT_ENDINGS_ENABLED %s   AREG_STORY_PAUSES_ENABLED %s\n' \
  "$FLAG" "$(state "$FLAG")" "$(state AREG_VARIANT_ENDINGS_ENABLED)" "$(state AREG_STORY_PAUSES_ENABLED)"
if cfg_defined "$FLAG" && [ "$STREAM" = "0" ]; then
  warn "$FLAG is ENABLED in your config.h (an earlier run of this script did that)."
  warn "The regression flash should NOT carry streaming: comment the line out by hand,"
  warn "or you will not know whether a click comes from streaming or from the rest."
fi

[ "$CHECK_ONLY" = "1" ] && { say "Check only — stopping here."; exit 0; }

if [ "$LABEL_OK" = "0" ] && [ "$SKIP_FLASH" = "0" ]; then
  die "no new firmware label. Re-run with  --label 1.3.5-bench  (rewrites the two
       lines in config.h, backup first) — or edit AREG_FW_VERSION / AREG_FW_BUILD by hand."
fi

# ---------------------------------------------------------------- step 1
if [ "$SKIP_PULL" = "0" ]; then
  say "Step 1 — pull the latest main"
  cd "$REPO_ROOT"
  if [ -n "$(git status --porcelain)" ]; then
    warn "you have uncommitted changes; leaving the branch alone"
    git status --short | sed 's/^/         /'
  else
    git checkout main >/dev/null 2>&1 || die "could not switch to main"
    git pull --ff-only || die "pull failed — resolve by hand, then re-run with --skip-pull"
    ok "main is at $(git rev-parse --short HEAD)"
  fi
else
  say "Step 1 — skipped"
fi

# ---------------------------------------------------------------- step 2
say "Step 2 — config.h edits (only what you asked for)"

if [ -n "$LABEL" ]; then
  BUILD_LABEL="bench-$(date +%Y%m%d)"
  cfg_str AREG_FW_VERSION | grep -q . && cfg_str AREG_FW_BUILD | grep -q . \
    || die "config.h has no AREG_FW_VERSION / AREG_FW_BUILD define to rewrite. Add:
       #define AREG_FW_VERSION \"$LABEL\"
       #define AREG_FW_BUILD   \"$BUILD_LABEL\""
  BACKUP="$CONFIG_H.bak.$(date +%Y%m%d-%H%M%S)"
  cp "$CONFIG_H" "$BACKUP"
  LABEL="$LABEL" BUILD_LABEL="$BUILD_LABEL" perl -pi -e '
    s{^(\s*#define\s+AREG_FW_VERSION\s+)"[^"]*"}{$1 . "\"" . $ENV{LABEL} . "\""}e;
    s{^(\s*#define\s+AREG_FW_BUILD\s+)"[^"]*"}{$1 . "\"" . $ENV{BUILD_LABEL} . "\""}e;' "$CONFIG_H"
  [ "$(cfg_str AREG_FW_VERSION)" = "$LABEL" ] && [ "$(cfg_str AREG_FW_BUILD)" = "$BUILD_LABEL" ] \
    || { cp "$BACKUP" "$CONFIG_H"; die "label patch did not apply; config.h restored from $BACKUP"; }
  ok "label set: fw=$LABEL build=$BUILD_LABEL (backup: $(basename "$BACKUP"))"
fi

if [ "$STREAM" = "1" ]; then
  if grep -qE "^[[:space:]]*#define[[:space:]]+$FLAG" "$CONFIG_H"; then
    ok "$FLAG already enabled — nothing to do"
  elif grep -qE "^[[:space:]]*//[[:space:]]*#define[[:space:]]+$FLAG" "$CONFIG_H"; then
    BACKUP="$CONFIG_H.bak.$(date +%Y%m%d-%H%M%S)"
    cp "$CONFIG_H" "$BACKUP"
    # Uncomment ONLY that one line. Nothing else in the file is touched.
    perl -pi -e "s{^(\\s*)//\\s*(#define\\s+$FLAG\\b)}{\$1\$2}" "$CONFIG_H"
    grep -qE "^[[:space:]]*#define[[:space:]]+$FLAG" "$CONFIG_H" \
      || { cp "$BACKUP" "$CONFIG_H"; die "patch did not apply; config.h restored from $BACKUP"; }
    ok "$FLAG enabled (backup: $(basename "$BACKUP"))"
  else
    warn "$FLAG not found in your config.h at all."
    warn "Add this line by hand, then re-run:    #define $FLAG 1"
    die "cannot continue with --stream without it"
  fi
  printf '    Effect, measured (docs/latency-plan.md): first sound 5.7 s -> 2.7 s.\n'
  printf '    The backend already streams by default and only to a toy that asks,\n'
  printf '    so no other unit is affected. The chunked de-framer has never run on\n'
  printf '    hardware: listen for clicks. bench_watch.py scores it (first_byte_ms).\n'
elif [ -z "$LABEL" ]; then
  printf '    config.h left exactly as it is.\n'
fi
if [ "$STREAM" = "0" ]; then
  printf '    %s is NOT turned on (first flash = regression build). Use --stream for flash #2.\n' "$FLAG"
fi

# ---------------------------------------------------------------- step 3
if [ "$SKIP_FLASH" = "0" ]; then
  say "Step 3 — compile"
  cd "$REPO_ROOT"
  # The newest firmware commit (5cf65b6, 2026-09-14) says in its own message
  # "NOT compile-verified". A compile error here is a FINDING, not a bench
  # failure: send the error text back, do not debug it at the bench.
  arduino-cli compile --fqbn "$FQBN" "$SKETCH_DIR" || die "compile failed — a finding, not a bench failure: keep the error text, do not flash"
  ok "compiled"
  printf '    Expect roughly 1.64 MB, about 52%% of the 3 MB program slot (BLE provisioning on;\n'
  printf '    the 1.3.4 image was 1,638,720 B = 52.1%%). It is NOT the old "1.26 MB / 40%%".\n'
  printf '    If it says 96-97%%, the partition scheme is wrong, NOT the firmware.\n'
else
  say "Step 3 — skipped"
fi

# ---------------------------------------------------------------- step 4
if [ "$SKIP_FLASH" = "0" ]; then
  if [ -z "$PORT" ]; then
    warn "no --port given; skipping upload"
    command -v arduino-cli >/dev/null && arduino-cli board list 2>/dev/null | sed 's/^/         /' || true
  else
    say "Step 4 — upload to $PORT"
    arduino-cli upload -p "$PORT" --fqbn "$FQBN" "$SKETCH_DIR" || die "upload failed"
    ok "flashed"
    printf '    Watch the serial log for AREG_FW_VERSION and AREG_FW_BUILD (they print once\n'
    printf '    Wi-Fi is up). Check AREG_FW_BUILD before blaming hardware for anything tonight.\n'
  fi
fi

# ---------------------------------------------------------------- step 5
say "Step 5 — load the SD card"

if [ -z "$SD_MOUNT" ]; then
  warn "no --sd given; skipping. The toy will sync over Wi-Fi ~180 s after boot instead."
elif [ ! -d "$SD_MOUNT" ]; then
  die "--sd path is not a directory: $SD_MOUNT"
elif [ -z "${AREG_DEVICE_ID:-}" ] || [ -z "${AREG_DEVICE_KEY:-}" ]; then
  warn "AREG_DEVICE_ID / AREG_DEVICE_KEY are not set in the environment; skipping."
  warn "Export them (do not pass them as flags — shell history) and re-run with --sd."
else
  # The loader reads the key from the environment itself; it is never echoed here.
  python3 "$REPO_ROOT/tools/factory/load_sd_card.py" \
      --backend "$BACKEND" --mount "$SD_MOUNT" \
    || die "SD load failed — nothing was left half-written; read the printed reason"
  ok "card loaded and sha-verified"
fi

# ---------------------------------------------------------------- done
WATCH_PORT="${PORT:-COM7}"
LOG_NAME="tools/quality-evidence/bench-$(date +%Y%m%d-%H%M).log"

cat <<'SHEET'

================================================================
  Computer side done. The rest is you, the toy, and your ears.
================================================================
SHEET
printf '\nBEFORE you press anything, in a second terminal at the repo root:\n'
printf '    python3 tools/firmware/bench_watch.py --port %s --log %s\n' "$WATCH_PORT" "$LOG_NAME"
cat <<'SHEET'
It scores the evening live (PASS / NOT YET per SHIP item) and, when you press
Ctrl+C at the very end, writes tools/quality-evidence/bench-<date>.md. A
power-cycle is fine: it waits for the port and reconnects.

Dashboard, for this toy: NOT paused, outside the bedtime window, all modes on,
after-story questions ON. (The watcher shouts if the toy is silent on purpose.)
Daily cost cap: the repo default is $0.25 per toy per day (~30 in-story
questions). R6 + A4 + C1 below come close. If the toy answers with the
daily-limit clip, that is the cap, not a bug: raise it on Railway first.

PART 1 — regression: the behaviour already seen on 1.3.2 / 1.3.3.
         Stop at the first failure and write down what you did.
  R1  power on: the log shows YOUR new fw= / build= and [heartbeat] status=200
  R2  quick press: a story plays to the end, no tone / "aaaa"
  R3  quick press mid-story: it pauses; press again: it resumes the SAME story
  R4  GREEN / RED during a story: next / previous story starts from the top
  R5  let a story end: summary clip -> ONE question -> you answer (press and
      hold, speak) -> reaction -> conclusion
  R6  press and HOLD during a story, ask something, let go: the answer plays
  R7  hold 2 s in idle, press GREEN: [button] hold, a chosen story plays
  R8  turn the knob mid-story: volume follows
  R9  3 minutes after boot: [content-sync] manifest status=200, index written,
      NO "index parse failed" (the watcher lists it as a problem if it appears)
  R10 after EVERY flow above the main button must still work
      (the watcher flags a toy that idles with its state not IDLE)

PART 2 — SHIP evidence (each step says which SHIP.md item it feeds):
  A6  five story starts from idle, one quick press each. A press DURING a
      story is a pause or a question, not a new story, so between starts
      either let the story end or, once you HEAR it begin, unplug and
      replug the toy. Do at least one power-cycle. Needs >=3 selections,
      0 adjacent repeats, >=1 power-cycle between selections.          -> A6
  A4  five presses in the MIDDLE of a story, each held while you ask a
      question. Film the toy with a phone (or use a stopwatch) and WRITE the
      seconds from press to silence. The log counts cuts and answers; it
      cannot time the stop.                                            -> A4
  C1  ten voice chains in a row without touching reset: alternate an
      in-story question and, when a story ends, the after-story answer. The
      watcher prints  CHAIN #n ... run n/10.                       -> C1, D3
  C3  with the toy idle, switch the ROUTER off for 30 s, back on, wait for
      WIFI ... got_ip, then press, start a story, press again, ask something:
      it must complete a turn with no reboot.                          -> C3
  C4  phone SPL app, 1 m from the speaker, two knob settings. Write: app,
      distance, room, dB(A) for each setting.                          -> C4
  A1  R2 + R5 with your ears on: write 3 lines — Armenian only / the
      after-story flow was heard / no crash.                           -> A1

PART 3 — never run on hardware (optional; see docs/bench-test-session.md):
  N1  .prev on the card; delete the index, keep .prev, reboot: restored
  N2  hold, say «խաղանք»: [welcome] intent= must NOT be "unknown"
  N3  hold, say «հանելուկ»: riddle loop; silence x2 ends it; BUTTON ALIVE
  N4  the Calm phrase outside the bedtime window
  N5  second flash with --stream: five questions, listen for clicks
  N6  variant endings / story pauses ONLY work with
        #define AREG_VARIANT_ENDINGS_ENABLED 1
        #define AREG_STORY_PAUSES_ENABLED 1
      in config.h. The dashboard toggles alone do nothing.

If the answer audio sounds chopped or clicky (streaming flash only):
    set  StoryQa__StreamAnswerAudio=false  on Railway.
    That disables it for every toy at once, with no reflash.

Full sheet with expected log lines: docs/bench-test-session.md
Evidence: the watcher's .md and .log land in tools/quality-evidence/.

SHEET
