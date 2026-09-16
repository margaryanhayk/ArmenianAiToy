#!/usr/bin/env bash
#
# bench_session.sh — everything the computer can do for a bench session,
# in one command. Written 2026-09-16.
#
# WHAT THIS DOES NOT DO: press the button, listen to the audio, or decide
# whether the toy sounds right. Those are yours. This script exists so the
# only thing you have to get right by hand is the cable.
#
# Steps, in order, each skippable:
#   1. pull the latest main
#   2. enable fast mode in your config.h   (AREG_QA_STREAM_PLAYBACK)
#   3. compile with the canonical FQBN
#   4. upload to the toy
#   5. load the SD card with the whole current library
#
# Your config.h is never replaced — only one commented-out line is
# uncommented, and a timestamped backup is written first. The device key is
# read from the environment and is never printed, logged, or passed on a
# command line.
#
# USAGE
#   tools/firmware/bench_session.sh --port COM7
#   tools/firmware/bench_session.sh --port /dev/ttyUSB0 --sd /media/AREG
#   tools/firmware/bench_session.sh --port COM7 --skip-pull
#   tools/firmware/bench_session.sh --check          # prerequisites only
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

PORT=""
SD_MOUNT=""
SKIP_PULL=0
SKIP_FLASH=0
CHECK_ONLY=0

say()  { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
ok()   { printf '    \033[32mOK\033[0m   %s\n' "$*"; }
warn() { printf '    \033[33mWARN\033[0m %s\n' "$*"; }
die()  { printf '\n\033[31mSTOP: %s\033[0m\n' "$*" >&2; exit 1; }

while [ $# -gt 0 ]; do
  case "$1" in
    --port)       PORT="${2:-}"; shift 2 ;;
    --sd)         SD_MOUNT="${2:-}"; shift 2 ;;
    --backend)    BACKEND="${2:-}"; shift 2 ;;
    --skip-pull)  SKIP_PULL=1; shift ;;
    --skip-flash) SKIP_FLASH=1; shift ;;
    --check)      CHECK_ONLY=1; shift ;;
    -h|--help)    sed -n '2,40p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *)            die "unknown argument: $1  (try --help)" ;;
  esac
done

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

command -v python3 >/dev/null && ok "python3" || warn "python3 not found — step 5 unavailable"

if [ -f "$CONFIG_H" ]; then
  ok "config.h present"
else
  die "no config.h at $CONFIG_H
       Copy it from config.h.example and fill in your Wi-Fi and backend URL first.
       It is gitignored on purpose — real credentials must never be committed."
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

[ "$CHECK_ONLY" = "1" ] && { say "Check only — stopping here."; exit 0; }

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
say "Step 2 — enable fast mode ($FLAG)"

if grep -qE "^[[:space:]]*#define[[:space:]]+$FLAG" "$CONFIG_H"; then
  ok "already enabled — nothing to do"
elif grep -qE "^[[:space:]]*//[[:space:]]*#define[[:space:]]+$FLAG" "$CONFIG_H"; then
  BACKUP="$CONFIG_H.bak.$(date +%Y%m%d-%H%M%S)"
  cp "$CONFIG_H" "$BACKUP"
  # Uncomment ONLY that one line. Nothing else in the file is touched.
  perl -pi -e "s{^(\\s*)//\\s*(#define\\s+$FLAG\\b)}{\$1\$2}" "$CONFIG_H"
  grep -qE "^[[:space:]]*#define[[:space:]]+$FLAG" "$CONFIG_H" \
    || { cp "$BACKUP" "$CONFIG_H"; die "patch did not apply; config.h restored from $BACKUP"; }
  ok "enabled (backup: $(basename "$BACKUP"))"
else
  warn "$FLAG not found in your config.h at all."
  warn "Add this line by hand, then re-run:    #define $FLAG 1"
  die "cannot continue without it — this is the whole point of the session"
fi

printf '    Effect, measured (docs/latency-plan.md): first sound 5.7 s -> 2.7 s.\n'
printf '    The backend already streams by default and only to a toy that asks,\n'
printf '    so no other unit is affected.\n'

# ---------------------------------------------------------------- step 3
if [ "$SKIP_FLASH" = "0" ]; then
  say "Step 3 — compile"
  cd "$REPO_ROOT"
  arduino-cli compile --fqbn "$FQBN" "$SKETCH_DIR" || die "compile failed — fix it before flashing"
  ok "compiled"
  printf '    Expect roughly 40%% of the program-storage slot.\n'
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
    printf '    Watch the serial log for AREG_FW_VERSION and AREG_FW_BUILD.\n'
    printf '    Check AREG_FW_BUILD before blaming hardware for anything tonight.\n'
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
cat <<'SHEET'

================================================================
  Computer side done. The rest is you, the toy, and your ears.
================================================================

In the parent dashboard, switch ON for this toy:
    variant endings . bedtime music . after-story questions
  Without these the new audio never plays.

Then, in this order. Stop at the first thing that fails.

  1. Power on. Press the button. A story plays.
  2. Press during the story and ask a question.
     COUNT THE SECONDS to the first sound. Expect ~3, not ~6.
     Do it five times. One fast answer is luck.
  3. Hold 2 s. Say «խաղանք». A game starts.
  4. Stay silent for two windows. The session ends.
     THE BUTTON MUST STILL WORK. If it does not, that is the
     most important bug you can find tonight — write down
     exactly what you did just before.
  5. Play a story a second time and listen for the new ending.
  6. Bedtime music, at bedtime volume, on the toy's speaker.

If the answer audio sounds chopped or clicky:
    set  StoryQa__StreamAnswerAudio=false  on Railway.
    That disables it for every toy at once, with no reflash.

Full sheet with expected results: docs/bench-test-session.md
Record what you find in tools/quality-evidence/, dated.

SHEET
