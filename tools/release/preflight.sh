#!/usr/bin/env bash
#
# preflight.sh — every release gate this repo has, in one command.
#
# WHAT THIS DOES NOT DO: listen to audio, press the button, flash a toy, or
# compile the firmware (needs arduino-cli + the ESP32 core). A GREEN here means
# "every automated gate that could run, passed" — the human gates still apply.
#
# Gates (each prints PASS / FAIL / WARN / SKIP the moment it finishes):
#   dotnet-build      restore + Release build of backend/ArmenianAiToy.slnx
#   dotnet-test       dotnet test (no skips allowed)          [skip: --quick]
#   claude-md-count   CLAUDE.md "dotnet test # N tests" == what ran
#   claude-md-size    CLAUDE.md under 24 KB (it rides on every tool call)
#   firmware-host-tests  esp32/AregVoiceMvp/host_tests/*_test.cpp (g++ C++17)
#   firmware-virtual-card  real sketch sources on a PC against a fake SD card
#                     (host_tests/virtual_card/; SKIP without a compiler or ArduinoJson 7)
#   python-tests      the unittest suites ci.yml runs, plus this script's own
#   story-audio       tools/story-audio/check_story_audio.py
#   music-audio       tools/story-audio/check_music_audio.py
#   speaker-maps      tools/story-voices/check_speaker_map.py
#   ambience-anchors  tools/story-audio/check_ambience_anchors.py
#   secret-scan       key-shaped strings + credential files in the working tree
#   release-image     tools/firmware/check_release_image.py   [only with --image]
#   listen-test       tools/listen/listen_test.py status      [WARN, never FAIL]
#
# USAGE
#   tools/release/preflight.sh                      # everything
#   tools/release/preflight.sh --quick              # build only, no dotnet test
#   tools/release/preflight.sh --no-dotnet          # skip all three dotnet gates
#   tools/release/preflight.sh --image path/to/AregVoiceMvp.ino.bin
#
# Full output of every gate goes to a log directory (path printed first and
# last), never to the screen. Gates keep running after a failure; the one
# exception is dotnet-test, which is skipped when the build failed.
# Exit code 0 = GREEN, 1 = RED. No secret value is ever printed or logged.
#
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HELPER="$REPO_ROOT/tools/release/preflight_checks.py"
LISTEN_SCRIPT="${PREFLIGHT_LISTEN_SCRIPT:-$REPO_ROOT/tools/listen/listen_test.py}"
QUICK=0; NO_DOTNET=0; IMAGE=""

if [ -t 1 ] && [ -z "${NO_COLOR:-}" ]; then
  B=$'\033[1m'; G=$'\033[32m'; Y=$'\033[33m'; R=$'\033[31m'; D=$'\033[2m'; Z=$'\033[0m'
else B=''; G=''; Y=''; R=''; D=''; Z=''; fi
say()  { printf '\n%s==> %s%s\n' "$B" "$*" "$Z"; }
ok()   { printf '    %sPASS%s %-19s %s\n' "$G" "$Z" "$1" "${2:-}"; }
warn() { printf '    %sWARN%s %-19s %s\n' "$Y" "$Z" "$1" "${2:-}"; }
bad()  { printf '    %sFAIL%s %-19s %s\n' "$R" "$Z" "$1" "${2:-}"; }
skip() { printf '    %sSKIP%s %-19s %s\n' "$D" "$Z" "$1" "${2:-}"; }
die()  { printf '\n%sSTOP: %s%s\n' "$R" "$*" "$Z" >&2; exit 2; }

while [ $# -gt 0 ]; do
  case "$1" in
    --quick)      QUICK=1; shift ;;
    --no-dotnet)  NO_DOTNET=1; shift ;;
    --image)      [ -n "${2:-}" ] || die "--image needs a path"; IMAGE="$2"; shift 2 ;;
    -h|--help)    sed -n '2,32p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *)            die "unknown argument: $1  (try --help)" ;;
  esac
done
# Resolve --image against the caller's cwd before we cd to the repo root.
if [ -n "$IMAGE" ] && [ -d "$(dirname "$IMAGE")" ]; then
  IMAGE="$(cd "$(dirname "$IMAGE")" && pwd)/$(basename "$IMAGE")"
fi

export PYTHONIOENCODING=utf-8 PYTHONUTF8=1   # Armenian text in gate output on Windows
PY=""
for c in python3 python; do
  if command -v "$c" >/dev/null 2>&1 && "$c" -c 'import sys; sys.exit(0 if sys.version_info[0] >= 3 else 1)' >/dev/null 2>&1; then PY="$c"; break; fi
done

LOGDIR="$(mktemp -d "${TMPDIR:-/tmp}/areg-preflight-$(date +%Y%m%d-%H%M%S).XXXXXX")" || die "cannot create a log directory"
cd "$REPO_ROOT" || die "cannot cd to $REPO_ROOT"

NAMES=(); STATUSES=(); SECS=(); DETAILS=(); T0=$SECONDS
begin() { T0=$SECONDS; }
record() {   # name STATUS detail
  NAMES+=("$1"); STATUSES+=("$2"); SECS+=("$((SECONDS - T0))"); DETAILS+=("${3:-}")
  case "$2" in PASS) ok "$1" "${3:-}" ;; FAIL) bad "$1" "${3:-}" ;; WARN) warn "$1" "${3:-}" ;; *) skip "$1" "${3:-}" ;; esac
  T0=$SECONDS
}
first_line() {   # first line of $2 matching regex $1, trimmed to 90 chars; fails if none
  local out; out="$(grep -E -m1 "$1" "$2" 2>/dev/null | cut -c1-90 | sed 's/[[:space:]]*$//')"
  [ -n "$out" ] && printf '%s' "$out"
}

say "Areg preflight — $(git rev-parse --short HEAD 2>/dev/null || echo 'no-git') on $(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo '?')"
printf '    logs: %s\n' "$LOGDIR"

# ------------------------------------------------------------- dotnet gates
dotnet_gates() {
  if [ "$NO_DOTNET" = 1 ]; then
    for g in dotnet-build dotnet-test claude-md-count; do record "$g" SKIP "--no-dotnet"; done; return
  fi
  if ! command -v dotnet >/dev/null 2>&1; then
    record dotnet-build FAIL "dotnet not found (install SDK 10, or pass --no-dotnet)"
    record dotnet-test SKIP "no build"; record claude-md-count SKIP "no test run"; return
  fi
  local log="$LOGDIR/dotnet-build.log"
  begin
  if ( cd "$REPO_ROOT/backend" && dotnet restore ArmenianAiToy.slnx && dotnet build ArmenianAiToy.slnx --no-restore -c Release ) >"$log" 2>&1; then
    local w; w="$(grep -Eo '[0-9]+ Warning\(s\)' "$log" | tail -1)"
    record dotnet-build PASS "Release build ok, ${w:-0 Warning(s)}"
  else
    record dotnet-build FAIL "$(first_line 'error ' "$log" || echo 'see log')"
    record dotnet-test SKIP "build failed"; record claude-md-count SKIP "no test run"; return
  fi
  if [ "$QUICK" = 1 ]; then
    record dotnet-test SKIP "--quick (build only)"; record claude-md-count SKIP "--quick"; return
  fi
  log="$LOGDIR/dotnet-test.log"
  begin
  ( cd "$REPO_ROOT/backend" && dotnet test ArmenianAiToy.slnx --no-build -c Release ) >"$log" 2>&1; local rc=$?
  local total="" failed="" skipped=""
  [ -n "$PY" ] && read -r total failed skipped < <("$PY" "$HELPER" test-summary --log "$log" 2>/dev/null | tr -d '\r') || true
  if [ -z "$total" ]; then
    record dotnet-test FAIL "exit $rc and no test totals found (see log)"
  elif [ "$rc" != 0 ] || [ "$failed" != 0 ]; then
    record dotnet-test FAIL "$failed failed of $total (exit $rc)"
  elif [ "$skipped" != 0 ]; then
    record dotnet-test FAIL "$skipped skipped test(s) — repo rule: never skip a test to get green"
  else
    record dotnet-test PASS "$total passed, 0 failed, 0 skipped"
  fi
  begin
  if [ -z "$PY" ]; then record claude-md-count FAIL "python not found"
  elif [ -z "$total" ] || [ "$rc" != 0 ] || [ "$failed" != 0 ] || [ "$skipped" != 0 ]; then
    record claude-md-count SKIP "dotnet-test did not pass cleanly; count not compared"
  else
    local msg; msg="$("$PY" "$HELPER" claude-md-count --claude-md "$REPO_ROOT/CLAUDE.md" --log "$log" 2>&1)"
    [ $? = 0 ] && record claude-md-count PASS "$msg" || record claude-md-count FAIL "$msg"
  fi
}

# --------------------------------------------------- firmware host tests
host_tests() {
  local cxx="" c
  for c in "${CXX:-}" g++ c++ clang++; do
    [ -n "$c" ] && command -v "$c" >/dev/null 2>&1 && { cxx="$c"; break; }
  done
  begin
  if [ -z "$cxx" ]; then
    # No g++/clang++ on the PATH - e.g. a Windows PC that only has the Visual
    # Studio C++ tools. The runner finds whatever compiler there is and exits
    # 77 when there really is none, which stays a SKIP.
    if [ -n "$PY" ]; then
      local rlog="$LOGDIR/firmware-host-tests.log" rrc
      "$PY" "$REPO_ROOT/esp32/AregVoiceMvp/host_tests/virtual_card/run_virtual_card.py" --host-tests >"$rlog" 2>&1; rrc=$?
      case "$rrc" in
        0)  record firmware-host-tests PASS "$(grep -c '^\[.*\] PASS' "$rlog") suites compiled and passed (run_virtual_card.py --host-tests)"; return ;;
        77) ;;   # no compiler at all: SKIP below
        *)  record firmware-host-tests FAIL "failed: see firmware-host-tests.log"; return ;;
      esac
    fi
    record firmware-host-tests SKIP "no C++ compiler (g++) on PATH"; return
  fi
  local dir="$REPO_ROOT/esp32/AregVoiceMvp/host_tests" log="$LOGDIR/firmware-host-tests.log" bin="$LOGDIR/host_test_bin"
  local f n=0 bad=""
  : >"$log"
  for f in "$dir"/*_test.cpp; do
    [ -e "$f" ] || break
    n=$((n + 1)); f="$(basename "$f")"; echo "=== $f" >>"$log"
    ( cd "$dir" && "$cxx" -std=c++17 -I.. -o "$bin" "$f" && "$bin" ) >>"$log" 2>&1 || bad="$bad $f"
  done
  rm -f "$bin" "$bin.exe"
  if [ "$n" = 0 ]; then record firmware-host-tests FAIL "no *_test.cpp found in host_tests/"
  elif [ -n "$bad" ]; then record firmware-host-tests FAIL "failed:$bad"
  else record firmware-host-tests PASS "$n suites compiled and passed ($cxx -std=c++17)"; fi
}

# ------------------------------- firmware on a PC with a fake SD card (virtual card)
# Builds the REAL sketch sources (story selection, index reading, the on-device
# self-test suites) against an in-memory SD card. g++, clang++ or the Visual
# Studio C++ Build Tools, plus ArduinoJson 7; the runner exits 77 when either
# is missing and that is a SKIP, not a failure.
virtual_card() {
  local log="$LOGDIR/firmware-virtual-card.log"
  begin
  [ -n "$PY" ] || { record firmware-virtual-card SKIP "python3 not found"; return; }
  "$PY" "$REPO_ROOT/esp32/AregVoiceMvp/host_tests/virtual_card/run_virtual_card.py" >"$log" 2>&1; local rc=$?
  case "$rc" in
    0)  record firmware-virtual-card PASS "$(tr -d '\r' <"$log" | awk -F'[()]' '/PASS \(/ {split($2, a, " "); s += a[1]} END {print s}') checks on the real sketch sources, fake SD card" ;;
    77) record firmware-virtual-card SKIP "$(head -1 "$log" | tr -d '\r')" ;;
    *)  record firmware-virtual-card FAIL "exit $rc: $(first_line '(FAIL|BUILD FAILED)' "$log" || echo 'see log')" ;;
  esac
}

# ------------------------------------------------------------ python gates
# py_gate NAME PASS-LINE-REGEX script [args]   (runs from the repo root)
py_gate() {
  local name="$1" pass_re="$2" log="$LOGDIR/$1.log"; shift 2
  begin
  [ -n "$PY" ] || { record "$name" FAIL "python3 not found"; return; }
  "$PY" "$@" >"$log" 2>&1; local rc=$?
  if [ "$rc" = 0 ]; then record "$name" PASS "$(first_line "$pass_re" "$log" || true)"
  else record "$name" FAIL "exit $rc: $(first_line '^ *(FAIL|BROKEN|no |Traceback|[A-Za-z]*Error)' "$log" || echo 'see log')"; fi
}

python_tests() {
  local log="$LOGDIR/python-tests.log" bad="" t
  begin
  [ -n "$PY" ] || { record python-tests FAIL "python3 not found"; return; }
  : >"$log"
  # The same suites ci.yml runs (python-tool-tests job), plus this script's own.
  for t in tools/firmware/test_check_release_image.py tools/factory/test_load_sd_card.py \
           tools/listen/test_listen_test.py tools/firmware/test_bench_watch.py \
           tools/release/test_preflight_checks.py; do
    if [ ! -f "$REPO_ROOT/$t" ]; then
      bad="$bad missing:$(basename "$t")"; continue   # a missing suite fails the gate, whichever it is
    fi
    echo "=== $t" >>"$log"
    ( cd "$(dirname "$REPO_ROOT/$t")" && "$PY" "$(basename "$t")" -v ) >>"$log" 2>&1 || bad="$bad $(basename "$t")"
  done
  if [ -n "$bad" ]; then record python-tests FAIL "failed:$bad"
  else record python-tests PASS "$(grep -Ec '^Ran [0-9]+ tests?' "$log") suites, $(grep -E '^Ran [0-9]+ tests?' "$log" | awk '{s+=$2} END {print s}') tests OK"; fi
}

dotnet_gates
begin
if [ -z "$PY" ]; then record claude-md-size FAIL "python3 not found"
else
  msg="$("$PY" "$HELPER" claude-md-size --claude-md "$REPO_ROOT/CLAUDE.md" 2>&1)"
  [ $? = 0 ] && record claude-md-size PASS "$msg" || record claude-md-size FAIL "$msg"
fi
host_tests
virtual_card
python_tests
py_gate story-audio     '^PASS'    tools/story-audio/check_story_audio.py
py_gate music-audio     '^PASS'    tools/story-audio/check_music_audio.py
py_gate speaker-maps    '^PASS'    tools/story-voices/check_speaker_map.py
py_gate ambience-anchors '^checked' tools/story-audio/check_ambience_anchors.py

# scanned-count line is the PASS detail; on FAIL the first FINDING (file:line + rule, never a value) leads.
begin
if [ -z "$PY" ]; then record secret-scan FAIL "python3 not found"
else
  "$PY" "$HELPER" secret-scan --root "$REPO_ROOT" >"$LOGDIR/secret-scan.log" 2>&1; rc=$?
  if [ "$rc" = 0 ]; then record secret-scan PASS "$(tail -1 "$LOGDIR/secret-scan.log" | sed 's/;.*//')"
  else record secret-scan FAIL "$(grep -c '^FINDING' "$LOGDIR/secret-scan.log") finding(s), first: $(first_line '^FINDING' "$LOGDIR/secret-scan.log" | sed 's/^FINDING //')"; fi
fi

if [ -n "$IMAGE" ]; then
  py_gate release-image '^PASS' tools/firmware/check_release_image.py "$IMAGE"
else
  begin; record release-image SKIP "no --image given (OTA image gate not run)"
fi

begin
if [ -f "$LISTEN_SCRIPT" ] && [ -n "$PY" ]; then
  "$PY" "$LISTEN_SCRIPT" status >"$LOGDIR/listen-test.log" 2>&1; rc=$?
  if [ "$rc" = 0 ]; then record listen-test PASS "status exit 0"
  else record listen-test WARN "status exit $rc — unheard audio is a human gate (listen-test.log)"; fi
else
  record listen-test SKIP "tools/listen/listen_test.py not present"
fi

# ----------------------------------------------------------------- summary
say "Summary"
printf '    %-6s %-20s %5s  %s\n' STATUS GATE "SECS" DETAIL
RED=0; PASSED=0; NOTES=()
for i in "${!NAMES[@]}"; do
  s="${STATUSES[$i]}"
  case "$s" in PASS) col="$G"; PASSED=$((PASSED + 1)) ;; FAIL) col="$R"; RED=$((RED + 1)) ;; WARN) col="$Y"; NOTES+=("${NAMES[$i]}") ;; *) col="$D"; NOTES+=("${NAMES[$i]}") ;; esac
  printf '    %s%-6s%s %-20s %5s  %s\n' "$col" "$s" "$Z" "${NAMES[$i]}" "${SECS[$i]}" "$(printf '%s' "${DETAILS[$i]}" | cut -c1-72)"
done
printf '\n    logs: %s\n' "$LOGDIR"
if [ "$RED" -gt 0 ]; then
  printf '\n%sPREFLIGHT: RED%s  (%s failed, %s passed)\n' "$R" "$Z" "$RED" "$PASSED"
  for i in "${!NAMES[@]}"; do [ "${STATUSES[$i]}" = FAIL ] && printf '    %s — %s\n' "${NAMES[$i]}" "${DETAILS[$i]}"; done
  exit 1
fi
printf '\n%sPREFLIGHT: GREEN%s  (%s passed' "$G" "$Z" "$PASSED"
[ "${#NOTES[@]}" -gt 0 ] && printf '; not a pass: %s' "${NOTES[*]}"
printf ')\n'
exit 0
