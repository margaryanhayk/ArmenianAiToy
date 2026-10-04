#!/usr/bin/env bash
# Off-site database backup pull (C166, 2026-10-03). Linux/macOS twin of
# tools/ops/pull_backup.ps1 — same flow, same file names, same exit codes.
#
# The automatic DatabaseBackupService snapshots live on the SAME Railway
# volume as the database they protect, so losing the volume loses both. This
# script is the off-platform half: run it daily (cron, Task Scheduler) on a
# machine you control and it keeps the newest N verified snapshots there.
#
# What it does, in order — any failure stops it with a non-zero exit and
# touches nothing already on disk:
#   1. Reads the operator token from a file that only you can read (refuses a
#      group/world-readable file). The token is never printed, never put on a
#      command line (curl reads it from stdin), never logged.
#   2. POST /api/internal/session with that token → a short-lived session
#      token. Works whether or not Internal:RequireSession is on. The
#      operator must have NO TotpSecret: a cron job cannot type a code.
#   3. GET /api/internal/backup with the session token → a fresh,
#      self-consistent SQLite snapshot (one audit row per pull, server side).
#   4. PRAGMA integrity_check via Python's sqlite3 module, plus a schema
#      sanity check (__EFMigrationsHistory must exist — an empty 0-table file
#      would otherwise pass integrity_check). Only then is the .part file
#      renamed to areg-backup-<UTC>Z.db (mode 600).
#   5. Deletes all but the newest --keep snapshots (default 30) — only after
#      a verified pull, so a failing run never thins out good backups.
#   6. Pings the healthchecks.io URL, if one is given — only on success. A
#      run that fails simply does not ping; healthchecks.io raises the alarm
#      once the period + grace pass.
#
# Covers the DATABASE only. The audio-blob and uploads zips are on the volume
# with no pull endpoint yet (docs/ops-runbook.md § Off-site backup pull).
#
# Usage:
#   tools/ops/pull_backup.sh --base-url https://<host> --token-file <path> \
#       --out-dir <dir> [--keep 30] [--ping-url https://hc-ping.com/<uuid>]
#
# Every flag also reads from the environment (flag wins):
#   AREG_BACKUP_BASE_URL  AREG_BACKUP_TOKEN_FILE  AREG_BACKUP_DIR
#   AREG_BACKUP_KEEP      AREG_BACKUP_PING_URL
#
# Exit codes: 0 ok (a failed ping only warns) · 1 usage/config · 2 session
# or download refused · 3 the snapshot failed its integrity check.
#
# Requires: bash, curl, python3 (stdlib sqlite3).

set -euo pipefail
umask 077
export LC_ALL=C   # bytewise sort and character classes below

BASE_URL="${AREG_BACKUP_BASE_URL:-}"
TOKEN_FILE="${AREG_BACKUP_TOKEN_FILE:-}"
OUT_DIR="${AREG_BACKUP_DIR:-}"
KEEP="${AREG_BACKUP_KEEP:-30}"
PING_URL="${AREG_BACKUP_PING_URL:-}"

log() { printf '[%s] %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$1"; }
die() { log "FAIL - $2" >&2; exit "$1"; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --base-url)   BASE_URL="${2:-}"; shift 2 ;;
    --token-file) TOKEN_FILE="${2:-}"; shift 2 ;;
    --out-dir)    OUT_DIR="${2:-}"; shift 2 ;;
    --keep)       KEEP="${2:-}"; shift 2 ;;
    --ping-url)   PING_URL="${2:-}"; shift 2 ;;
    -h|--help)    sed -n '2,44p' "$0"; exit 0 ;;
    *)            die 1 "unknown argument: $1 (see --help)" ;;
  esac
done

[[ -n "$BASE_URL" ]]   || die 1 "--base-url (or AREG_BACKUP_BASE_URL) is required"
[[ -n "$TOKEN_FILE" ]] || die 1 "--token-file (or AREG_BACKUP_TOKEN_FILE) is required"
[[ -n "$OUT_DIR" ]]    || die 1 "--out-dir (or AREG_BACKUP_DIR) is required"
[[ "$KEEP" =~ ^[1-9][0-9]*$ ]] || die 1 "--keep must be a whole number >= 1"
command -v curl >/dev/null    || die 1 "curl is not installed"
command -v python3 >/dev/null || die 1 "python3 is not installed"
BASE_URL="${BASE_URL%/}"

# --- 1. token: a user-only file, read once, never echoed -------------------
[[ -f "$TOKEN_FILE" ]] || die 1 "token file not found: $TOKEN_FILE"
python3 -c 'import os,sys; sys.exit(1 if os.stat(sys.argv[1]).st_mode & 0o077 else 0)' "$TOKEN_FILE" \
  || die 1 "token file $TOKEN_FILE is readable by other users — run: chmod 600 '$TOKEN_FILE'"
TOKEN="$(tr -d '\r\n' < "$TOKEN_FILE")"
TOKEN="${TOKEN#"${TOKEN%%[![:space:]]*}"}"; TOKEN="${TOKEN%"${TOKEN##*[![:space:]]}"}"
[[ -n "$TOKEN" ]] || die 1 "token file $TOKEN_FILE is empty"
# The token travels inside a curl config line on stdin; refuse anything that
# could break out of its quotes (generated tokens are [A-Za-z0-9_-] anyway).
[[ "$TOKEN" =~ ^[!-~]+$ && "$TOKEN" != *'"'* && "$TOKEN" != *'\'* ]] \
  || die 1 "token file $TOKEN_FILE holds unexpected characters (expected one printable token)"

mkdir -p "$OUT_DIR"
STAMP="$(date -u +%Y%m%d-%H%M%S)"
FINAL="$OUT_DIR/areg-backup-${STAMP}Z.db"
PART="$FINAL.part"
SESSION_JSON="$(mktemp "$OUT_DIR/.session.XXXXXX")"
trap 'rm -f "$PART" "$SESSION_JSON"' EXIT
[[ ! -e "$FINAL" ]] || die 1 "$FINAL already exists — two runs in the same second?"

# curl with the bearer header supplied on stdin (a curl config), so no token
# ever appears in a process listing.
curl_bearer() {
  local bearer="$1"; shift
  printf 'header = "Authorization: Bearer %s"\n' "$bearer" \
    | curl --config - --silent --show-error --max-time 600 "$@"
}

# --- 2. session exchange -------------------------------------------------
log "opening a console session at $BASE_URL"
status="$(curl_bearer "$TOKEN" -X POST -H 'Content-Type: application/json' --data '{}' \
  -o "$SESSION_JSON" -w '%{http_code}' "$BASE_URL/api/internal/session")" \
  || die 2 "could not reach $BASE_URL"
case "$status" in
  200) ;;
  404) die 2 "session refused (HTTP 404): wrong token, the operator was removed, or the console is off" ;;
  401) die 2 "session refused (HTTP 401): this operator has a TotpSecret — the backup bot must be a separate operator without one" ;;
  *)   die 2 "session refused (HTTP $status)" ;;
esac
SESSION="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1])).get("sessionToken") or "")' "$SESSION_JSON" 2>/dev/null || true)"
rm -f "$SESSION_JSON"
TOKEN=""
[[ -n "$SESSION" ]] || die 2 "the session response carried no sessionToken"

# --- 3. download ---------------------------------------------------------
log "pulling the snapshot"
status="$(curl_bearer "$SESSION" -o "$PART" -w '%{http_code}' "$BASE_URL/api/internal/backup")" \
  || die 2 "the download did not complete"
SESSION=""
[[ "$status" == "200" ]] || die 2 "backup refused (HTTP $status)"

# --- 4. integrity --------------------------------------------------------
verdict="$(python3 - "$PART" <<'PY'
import os, sqlite3, sys
path = sys.argv[1]
if os.path.getsize(path) == 0:
    print("empty file"); sys.exit(1)
try:
    con = sqlite3.connect(path)
    rows = [r[0] for r in con.execute("PRAGMA integrity_check")]
    has_history = con.execute(
        "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'"
    ).fetchone()[0] == 1
    tables = con.execute("SELECT count(*) FROM sqlite_master WHERE type='table'").fetchone()[0]
    con.close()
except sqlite3.DatabaseError as e:
    print(f"not a readable SQLite database ({e})"); sys.exit(1)
if rows != ["ok"]:
    print(f"integrity_check reported {len(rows)} problem(s), first: {rows[0]}"); sys.exit(1)
if not has_history:
    print("integrity ok but __EFMigrationsHistory is missing (not an Areg database)"); sys.exit(1)
print(f"integrity_check=ok, {tables} tables")
PY
)" || die 3 "snapshot rejected: $verdict"

mv -f "$PART" "$FINAL"
chmod 600 "$FINAL"
log "saved $FINAL ($(wc -c < "$FINAL" | tr -d ' ') bytes; $verdict)"

# --- 5. keep the newest N ------------------------------------------------
removed=0
while IFS= read -r old; do
  rm -f -- "$old"; removed=$((removed + 1))
done < <(find "$OUT_DIR" -maxdepth 1 -type f -name 'areg-backup-*Z.db' | sort -r | tail -n +"$((KEEP + 1))")
log "kept the newest $KEEP, removed $removed older"

# --- 6. heartbeat to the external monitor, success only -----------------
if [[ -n "$PING_URL" ]]; then
  if printf 'url = "%s"\n' "$PING_URL" \
       | curl --config - --silent --show-error --fail --max-time 10 --retry 3 -o /dev/null; then
    log "pinged the monitor"
  else
    log "WARN - backup is fine but the monitor ping failed; it will alert if pings stay missing" >&2
  fi
fi
exit 0
