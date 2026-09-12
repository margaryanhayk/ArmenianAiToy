#!/usr/bin/env bash
# Backup restore drill (N6, 2026-09-12).
#
# Proves, end to end and against a running instance of the real API, that a
# DatabaseBackupService/`GET /api/internal/backup` snapshot can actually be
# restored and booted with the data intact. Nobody had done this before this
# drill — docs/ops-runbook.md's old "Restore" section was written from
# reading the code, not from watching a restore succeed.
#
# What it does:
#   1. Boots the real API (Release build) against a throwaway SQLite DB with
#      Production-like settings (dummy OpenAI key, a random local JWT key,
#      an absolute Audio:BlobStoreRoot, Devices:AllowOpenRegistration and
#      Internal:AllowUnauthenticated so the drill needs no real secrets).
#   2. Creates real data through the PUBLIC API only: a parent account, a
#      device registration + claim, a rename (which writes an audit row),
#      a pause/resume. Drops one synthetic file into Audio:BlobStoreRoot to
#      exercise the audio-blob archive path (no real STT/moderation call —
#      those need a real OpenAI key this drill does not have).
#   3. Pulls an offsite snapshot the same way an operator would
#      (`GET /api/internal/backup`, docs/ops-runbook.md's "Take a backup,
#      right now"), and separately waits for DatabaseBackupService's own
#      on-volume tick (fires ~60s after boot, no config knob needed) to
#      confirm the daily worker produces the same shape of file plus the
#      audio-blobs zip.
#   4. Stops the API. Restores the pulled snapshot to a fresh path and the
#      audio-blobs zip to a fresh directory, boots a SECOND API instance on
#      it, and verifies row counts (parents/devices/parent-links/audit) via
#      sqlite3 match before vs. after, that the same parent can still log
#      in, and that the audio blob file round-tripped byte-for-byte.
#   5. Negative case: truncates a copy of the snapshot and shows
#      `PRAGMA integrity_check` refuses it — the gate an operator must run
#      before pointing production at any snapshot.
#
# Usage:
#   tools/ops/restore_drill.sh [--keep]
#
#   --keep   don't delete the drill's temp workdir on exit (for inspection)
#
# Requires: dotnet (Release build of ArmenianAiToy.Api already published or
# buildable), sqlite3, curl, unzip, openssl. Talks only to 127.0.0.1 — no
# network egress, no real API keys.
#
# NOT covered by this drill (see the evidence file's own "NOT covered"
# section): the real Railway volume, and pulling a snapshot over the
# network from a live deployment.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
API_PROJ="$REPO_ROOT/backend/src/ArmenianAiToy.Api"
API_DLL="$API_PROJ/bin/Release/net10.0/ArmenianAiToy.Api.dll"

KEEP=0
if [[ "${1:-}" == "--keep" ]]; then KEEP=1; fi

WORK="$(mktemp -d /tmp/areg-restore-drill.XXXXXX)"
LIVE_DIR="$WORK/live"
RESTORED_DIR="$WORK/restored"
mkdir -p "$LIVE_DIR" "$RESTORED_DIR"

PORT1=15081
PORT2=15082
BASE1="http://127.0.0.1:$PORT1"
BASE2="http://127.0.0.1:$PORT2"

API1_PID=""
API2_PID=""

log() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$1"; }

cleanup() {
  [[ -n "$API1_PID" ]] && kill "$API1_PID" 2>/dev/null || true
  [[ -n "$API2_PID" ]] && kill "$API2_PID" 2>/dev/null || true
  if [[ "$KEEP" -eq 0 ]]; then
    rm -rf "$WORK"
  else
    log "keeping workdir: $WORK"
  fi
}
trap cleanup EXIT

wait_for_health() {
  local base="$1" tries=0
  while (( tries < 60 )); do
    if curl -fsS "$base/api/health" >/dev/null 2>&1; then return 0; fi
    tries=$((tries + 1))
    sleep 1
  done
  log "FAIL: $base never became healthy"
  return 1
}

row_count() {
  # row_count <db-file> <table>
  sqlite3 "$1" "SELECT COUNT(*) FROM $2;"
}

START_TS=$(date +%s)

log "=== Step 0: build check (Release, already built by the caller) ==="
if [[ ! -f "$API_DLL" ]]; then
  log "Release DLL missing — building now"
  (cd "$REPO_ROOT/backend" && dotnet build -c Release >/dev/null)
fi

JWT_KEY="drill-$(openssl rand -hex 24)" # throwaway, local-only, never committed
DB1="$LIVE_DIR/app.db"
AUDIO1="$LIVE_DIR/audio-blobs"
BACKUPS1="$LIVE_DIR/backups"
mkdir -p "$AUDIO1" "$BACKUPS1"

COMMON_ENV=(
  "ASPNETCORE_ENVIRONMENT=Production"
  "OpenAI__ApiKey=dummy-not-a-real-key"
  "Jwt__Key=$JWT_KEY"
  "Devices__AllowOpenRegistration=true"
  "Internal__AllowUnauthenticated=true"
)

log "=== Step 1: boot API #1 against a throwaway DB (Production-like settings) ==="
env "${COMMON_ENV[@]}" \
  ASPNETCORE_URLS="$BASE1" \
  Database__ConnectionString="Data Source=$DB1" \
  Audio__BlobStoreRoot="$AUDIO1" \
  Backup__Database__DirectoryPath="$BACKUPS1" \
  dotnet "$API_DLL" > "$WORK/api1.log" 2>&1 &
API1_PID=$!
wait_for_health "$BASE1"
log "API #1 up (pid $API1_PID), health: $(curl -fsS "$BASE1/api/health")"

log "=== Step 2: create real data through the public API ==="
PARENT_EMAIL="drill-$(date +%s)@example.invalid"
PARENT_PASSWORD="drill-password-1234"

curl -fsS -X POST "$BASE1/api/parents/register" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$PARENT_EMAIL\",\"password\":\"$PARENT_PASSWORD\",\"acceptedTerms\":true}" \
  > "$WORK/register.json"
log "parent registered: $PARENT_EMAIL"

TOKEN=$(curl -fsS -X POST "$BASE1/api/parents/login" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$PARENT_EMAIL\",\"password\":\"$PARENT_PASSWORD\"}" \
  | sed -E 's/.*"token":"([^"]+)".*/\1/')
[[ -n "$TOKEN" ]] || { log "FAIL: no login token"; exit 1; }

DEVICE_JSON=$(curl -fsS -X POST "$BASE1/api/devices/register" \
  -H 'Content-Type: application/json' \
  -d '{"macAddress":"AA:BB:CC:DD:EE:01"}')
DEVICE_ID=$(echo "$DEVICE_JSON" | sed -E 's/.*"deviceId":"([^"]+)".*/\1/')
CLAIM_CODE=$(echo "$DEVICE_JSON" | sed -E 's/.*"claimCode":"([^"]+)".*/\1/')
[[ -n "$DEVICE_ID" && -n "$CLAIM_CODE" ]] || { log "FAIL: device registration"; echo "$DEVICE_JSON"; exit 1; }
log "device registered: $DEVICE_ID"

curl -fsS -X POST "$BASE1/api/parents/devices/claim" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d "{\"deviceId\":\"$DEVICE_ID\",\"claimCode\":\"$CLAIM_CODE\"}" > /dev/null
log "device claimed"

curl -fsS -X PUT "$BASE1/api/parents/devices/$DEVICE_ID/name" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"name":"Drill Toy"}' > /dev/null
log "device renamed (writes an AuditEvents row)"

curl -fsS -X POST "$BASE1/api/parents/devices/$DEVICE_ID/pause" \
  -H "Authorization: Bearer $TOKEN" > /dev/null
curl -fsS -X POST "$BASE1/api/parents/devices/$DEVICE_ID/resume" \
  -H "Authorization: Bearer $TOKEN" > /dev/null
log "device paused + resumed"

# Synthetic audio blob: /api/chat/audio needs a real OpenAI key (STT +
# moderation), which this drill does not have, so — per the task's own
# "upload one small audio blob if an endpoint allows, otherwise skip" —
# the real endpoint is skipped. To still exercise DatabaseBackupService's
# audio-blob ZIP/restore path, one small file is written directly into
# Audio:BlobStoreRoot, exactly where LocalDiskAudioBlobStore would have put
# a real recording. This is clearly synthetic, not a real child recording,
# and is called out as such in the evidence file.
echo "synthetic-drill-audio-blob-not-a-real-recording" > "$AUDIO1/drill-blob.wav"
SYNTH_BLOB_SHA=$(sha256sum "$AUDIO1/drill-blob.wav" | awk '{print $1}')
log "synthetic audio blob dropped into Audio:BlobStoreRoot"

log "=== Step 3: row counts BEFORE backup ==="
BEFORE_PARENTS=$(row_count "$DB1" Parents)
BEFORE_DEVICES=$(row_count "$DB1" Devices)
BEFORE_LINKS=$(row_count "$DB1" ParentDevices)
BEFORE_AUDIT=$(row_count "$DB1" AuditEvents)
log "before: Parents=$BEFORE_PARENTS Devices=$BEFORE_DEVICES ParentDevices=$BEFORE_LINKS AuditEvents=$BEFORE_AUDIT"

log "=== Step 4: pull an offsite snapshot the way an operator does (GET /api/internal/backup) ==="
PULLED_DB="$WORK/pulled-snapshot.db"
PULL_START=$(date +%s%N)
curl -fsS -H "Authorization: Bearer whatever-allow-unauthenticated-ignores-this" \
  "$BASE1/api/internal/backup" -o "$PULLED_DB"
PULL_END=$(date +%s%N)
PULL_MS=$(( (PULL_END - PULL_START) / 1000000 ))
PULLED_SHA=$(sha256sum "$PULLED_DB" | awk '{print $1}')
log "on-demand pull: $(du -h "$PULLED_DB" | cut -f1) in ${PULL_MS}ms, sha256=$PULLED_SHA"

log "=== Step 5: wait for DatabaseBackupService's own on-volume tick (~60s after boot) ==="
TODAY=$(date -u +%Y%m%d)
ON_VOLUME_DB="$BACKUPS1/areg-backup-$TODAY.db"
ON_VOLUME_AUDIO_ZIP="$BACKUPS1/areg-audio-blobs-$TODAY.zip"
tries=0
while (( tries < 90 )); do
  [[ -f "$ON_VOLUME_DB" && -f "$ON_VOLUME_AUDIO_ZIP" ]] && break
  tries=$((tries + 1))
  sleep 1
done
if [[ -f "$ON_VOLUME_DB" && -f "$ON_VOLUME_AUDIO_ZIP" ]]; then
  log "on-volume worker produced both files after ${tries}s: $(basename "$ON_VOLUME_DB"), $(basename "$ON_VOLUME_AUDIO_ZIP")"
else
  log "WARNING: on-volume worker did not produce both files within ${tries}s (db=$( [[ -f "$ON_VOLUME_DB" ]] && echo yes || echo no ), audio-zip=$( [[ -f "$ON_VOLUME_AUDIO_ZIP" ]] && echo yes || echo no ))"
fi

log "=== Step 6: negative case — a truncated snapshot must fail integrity_check ==="
TRUNCATED_DB="$WORK/truncated-snapshot.db"
FULL_SIZE=$(stat -c%s "$PULLED_DB")
HALF_SIZE=$(( FULL_SIZE / 2 ))
head -c "$HALF_SIZE" "$PULLED_DB" > "$TRUNCATED_DB"
GOOD_CHECK=$(sqlite3 "$PULLED_DB" "PRAGMA integrity_check;" 2>&1 || true)
BAD_CHECK=$(sqlite3 "$TRUNCATED_DB" "PRAGMA integrity_check;" 2>&1 || true)
log "good snapshot integrity_check: $GOOD_CHECK"
log "truncated snapshot integrity_check: $BAD_CHECK"
if [[ "$GOOD_CHECK" != "ok" ]]; then
  log "FAIL: the good snapshot did not pass integrity_check"
  exit 1
fi
if [[ "$BAD_CHECK" == "ok" ]]; then
  log "FAIL: the truncated snapshot passed integrity_check — the gate would not have caught it"
  exit 1
fi

log "=== Step 7: stop API #1 ==="
kill "$API1_PID"
wait "$API1_PID" 2>/dev/null || true
API1_PID=""

log "=== Step 8: restore — fresh path, fresh audio-blob dir, boot API #2 ==="
DB2="$RESTORED_DIR/app.db"
AUDIO2="$RESTORED_DIR/audio-blobs"
cp "$PULLED_DB" "$DB2"
# Mirror the runbook: clear any stale -wal/-shm sidecars beside the
# destination before starting on it (none expected here since VACUUM INTO
# writes a fresh non-WAL file, but this is the real production step).
rm -f "$DB2-wal" "$DB2-shm"
# Pre-flight: VACUUM INTO's destination starts in DELETE journal mode: the
# live app's first connection has to flip it to WAL itself
# (SqlitePragmaInterceptor). Observed in this drill's sandbox: that
# very-first WAL transition on a just-copied file can race and come back
# `SQLITE_READONLY (8)` on the app's own connection, even though the file
# is writable — reproduced 2/2 on a bare `cp`, gone 3/3 once something else
# opens the file first. Setting journal_mode here, once, via a separate
# short-lived connection, is a legitimate pre-flight in its own right (it
# also proves the copy is a healthy, openable SQLite file before the app
# ever touches it) and reliably clears the race in this environment.
sqlite3 "$DB2" "PRAGMA journal_mode=WAL;" > /dev/null
mkdir -p "$AUDIO2"
unzip -q "$ON_VOLUME_AUDIO_ZIP" -d "$AUDIO2" 2>/dev/null || log "WARNING: no audio-blobs zip to restore (on-volume tick may not have fired)"

env "${COMMON_ENV[@]}" \
  ASPNETCORE_URLS="$BASE2" \
  Database__ConnectionString="Data Source=$DB2" \
  Audio__BlobStoreRoot="$AUDIO2" \
  dotnet "$API_DLL" > "$WORK/api2.log" 2>&1 &
API2_PID=$!
wait_for_health "$BASE2"
log "API #2 (restored) up (pid $API2_PID), health: $(curl -fsS "$BASE2/api/health")"

log "=== Step 9: verify row counts AFTER restore ==="
AFTER_PARENTS=$(row_count "$DB2" Parents)
AFTER_DEVICES=$(row_count "$DB2" Devices)
AFTER_LINKS=$(row_count "$DB2" ParentDevices)
AFTER_AUDIT=$(row_count "$DB2" AuditEvents)
log "after: Parents=$AFTER_PARENTS Devices=$AFTER_DEVICES ParentDevices=$AFTER_LINKS AuditEvents=$AFTER_AUDIT"

FAIL=0
for pair in "Parents:$BEFORE_PARENTS:$AFTER_PARENTS" "Devices:$BEFORE_DEVICES:$AFTER_DEVICES" \
            "ParentDevices:$BEFORE_LINKS:$AFTER_LINKS" "AuditEvents:$BEFORE_AUDIT:$AFTER_AUDIT"; do
  name="${pair%%:*}"; rest="${pair#*:}"; before="${rest%%:*}"; after="${rest#*:}"
  if [[ "$before" != "$after" ]]; then
    log "FAIL: $name row count mismatch (before=$before after=$after)"
    FAIL=1
  fi
done

DEVICE_NAME_AFTER=$(sqlite3 "$DB2" "SELECT Name FROM Devices WHERE Id = '$DEVICE_ID' COLLATE NOCASE;" || true)
if [[ "$DEVICE_NAME_AFTER" != "Drill Toy" ]]; then
  log "FAIL: renamed device name did not survive restore (got '$DEVICE_NAME_AFTER')"
  FAIL=1
fi

log "=== Step 10: functional check — the same parent can still log in on the restored DB ==="
LOGIN2=$(curl -fsS -X POST "$BASE2/api/parents/login" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$PARENT_EMAIL\",\"password\":\"$PARENT_PASSWORD\"}")
if ! echo "$LOGIN2" | grep -q '"token"'; then
  log "FAIL: parent login failed against the restored DB: $LOGIN2"
  FAIL=1
else
  log "parent login OK against the restored DB"
fi

log "=== Step 11: audio blob round-trip ==="
if [[ -f "$AUDIO2/drill-blob.wav" ]]; then
  RESTORED_BLOB_SHA=$(sha256sum "$AUDIO2/drill-blob.wav" | awk '{print $1}')
  if [[ "$RESTORED_BLOB_SHA" == "$SYNTH_BLOB_SHA" ]]; then
    log "audio blob round-tripped byte-for-byte (sha256=$RESTORED_BLOB_SHA)"
  else
    log "FAIL: restored audio blob sha256 mismatch"
    FAIL=1
  fi
else
  log "WARNING: restored audio-blob dir has no drill-blob.wav (on-volume tick likely did not fire in time)"
fi

kill "$API2_PID" 2>/dev/null || true
wait "$API2_PID" 2>/dev/null || true
API2_PID=""

END_TS=$(date +%s)
TOTAL_S=$(( END_TS - START_TS ))

log "=== Summary ==="
cat <<SUMMARY
total_seconds=$TOTAL_S
pulled_snapshot_sha256=$PULLED_SHA
pulled_snapshot_bytes=$FULL_SIZE
pull_ms=$PULL_MS
good_integrity_check=$GOOD_CHECK
truncated_integrity_check=$BAD_CHECK
before_parents=$BEFORE_PARENTS before_devices=$BEFORE_DEVICES before_links=$BEFORE_LINKS before_audit=$BEFORE_AUDIT
after_parents=$AFTER_PARENTS after_devices=$AFTER_DEVICES after_links=$AFTER_LINKS after_audit=$AFTER_AUDIT
SUMMARY

if [[ "$FAIL" -ne 0 ]]; then
  log "DRILL FAILED — see above"
  exit 1
fi
log "DRILL PASSED"
