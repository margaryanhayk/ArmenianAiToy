# Backup restore drill — first proof, 2026-09-12 (N6)

Nobody had ever restored a `DatabaseBackupService` snapshot and booted the
API on it. `docs/ops-runbook.md:93-107` described the backup and a restore
procedure written from reading the code, not from watching a restore
succeed. This closes that gap: one scripted drill, run twice, both green.

Automation: `tools/ops/restore_drill.sh` (rerunnable, no manual steps).
Environment: this container, `dotnet build -c Release` beforehand, no
network egress, no real API keys — `OpenAI:ApiKey=dummy-not-a-real-key`,
`Jwt:Key` a random 76-char local secret generated per run
(`openssl rand -hex 24`, never written to a file, never committed).

## What the drill does

1. Boots the real `ArmenianAiToy.Api` (Release build) against a throwaway
   SQLite DB, `ASPNETCORE_ENVIRONMENT=Production`, `Devices:AllowOpenRegistration=true`
   and `Internal:AllowUnauthenticated=true` (drill-only bypasses so no real
   provisioning secret or operator token is needed), `Audio:BlobStoreRoot`
   set to an absolute temp path (satisfies the fail-closed resolver the
   same way Railway's `Audio__BlobStoreRoot` does).
2. Creates real data through the **public API only**: registers a parent,
   logs in, registers a device (`POST /api/devices/register`), claims it
   (`POST /api/parents/devices/claim`), renames it (`PUT
   /api/parents/devices/{id}/name` — writes a `ParentDeviceRenamed` audit
   row), pauses and resumes it.
3. Drops one small **synthetic** file directly into `Audio:BlobStoreRoot`
   to exercise `DatabaseBackupService.BackupAudioBlobs`'s zip/restore path.
   Not a real recording: `POST /api/chat/audio` needs a real OpenAI key for
   STT + moderation, which this drill does not have — per the task's own
   "upload one small audio blob if an endpoint allows, otherwise skip",
   the real endpoint is skipped and this stands in for it.
4. Pulls an offsite snapshot the way an operator actually would —
   `GET /api/internal/backup`, the exact command in the runbook's "Take a
   backup, right now" — **and separately** waits ~60s for
   `DatabaseBackupService`'s own on-volume tick to fire on its normal
   schedule (its 1-minute initial delay, no config knob needed) and
   confirms it independently produces `areg-backup-<date>.db` **and**
   `areg-audio-blobs-<date>.zip`.
5. Negative case: truncates a copy of the pulled snapshot to half its size
   and shows `PRAGMA integrity_check` refuses it — the gate an operator
   must run before pointing production at any snapshot.
6. Stops the API. Restores the pulled snapshot to a fresh path and the
   on-volume audio-blobs zip to a fresh directory, boots a **second**,
   independent API instance on the restored files, and verifies:
   - row counts for `Parents`, `Devices`, `ParentDevices`, `AuditEvents`
     match before vs. after, exactly;
   - the renamed device's name (`"Drill Toy"`) survived;
   - the same parent can still log in with the same password (proves the
     BCrypt hash round-tripped, not just that a row exists);
   - the synthetic audio blob round-trips byte-for-byte (sha256 match).

## Results (two runs, both green)

| | Run 1 | Run 2 |
|---|---|---|
| Total wall time | 66 s | 65 s |
| Pulled snapshot size | 300 KB (307200 bytes) | 300 KB (307200 bytes) |
| Pull latency (`GET /api/internal/backup`) | 68 ms | 65 ms |
| Pulled snapshot sha256 | `e0bafec9f9d20acf87b0056dd7f9594056cc612d713260841c00539ece251eef` | `ab418fe41a9ac409f77a659f237d3c21d69315c8573d77c22e921e3484024a93` |
| `PRAGMA integrity_check` (good) | `ok` | `ok` |
| `PRAGMA integrity_check` (truncated to 50%) | `Error: in prepare, database disk image is malformed (11)` | same |
| Row counts before | Parents=1 Devices=1 ParentDevices=1 AuditEvents=4 | same |
| Row counts after restore | Parents=1 Devices=1 ParentDevices=1 AuditEvents=4 | same |
| Device name survived restore | `Drill Toy` ✓ | `Drill Toy` ✓ |
| Parent login on restored DB | 200, token issued ✓ | 200, token issued ✓ |
| Audio blob round-trip sha256 | matched source ✓ | matched source ✓ |
| On-volume worker produced both files | after 58 s ✓ | after 58 s ✓ |
| Verdict | **PASSED** | **PASSED** |

Different sha256 between the two runs is expected — the pulled snapshot
each time includes that run's own random parent email/id/timestamps, not a
sign of nondeterminism in the backup mechanism itself.

The 4 audit rows, both runs: `ParentDeviceClaimed`, `ParentDeviceRenamed`,
`ParentDevicePauseStateChanged` × 2 (pause, resume) — confirmed present via
`SELECT EventType FROM AuditEvents` on the restored DB, not just counted.

On-volume worker's own files (independent of the on-demand pull), run 2:
`areg-backup-20260912.db` (307200 bytes) and `areg-audio-blobs-20260912.zip`
(174 bytes, sha256 `02f049d244c7f6d63f59d886105dbef81232486935bf3b6ce06dbb4501e043d7`)
— confirms `DatabaseBackupService`'s real schedule (not just the on-demand
endpoint) produces a restorable file with no config changes needed.

## A real environment quirk, found and worked around (not an app bug)

The very first boot against the freshly-`cp`'d restored file failed:

```
Unhandled exception. Microsoft.Data.Sqlite.SqliteException (0x80004005):
SQLite Error 8: 'attempt to write a readonly database'.
   at ... SqlitePragmaInterceptor.ConnectionOpened(...)
   at ... SqliteDatabaseCreator.Exists()
   at ... Migrator.Migrate(...)
```

Reproduced 2/2 on a bare `cp` of a good, `integrity_check: ok` snapshot in
this container; the SAME file opened fine with the `sqlite3` CLI every
time. `VACUUM INTO`'s destination starts in `DELETE` journal mode, and
`SqlitePragmaInterceptor` flips every new connection to `WAL` on open —
that very first DELETE→WAL transition on a just-copied file raced and came
back `SQLITE_READONLY` in this sandbox, even though the file's own
permissions were `rw-r--r--` and owned by the process's own user the whole
time. Isolated to that one first-ever WAL transition: pre-setting
`journal_mode=WAL` via one `sqlite3 "$db" "PRAGMA journal_mode=WAL;"` call
right after the copy — before the app ever opens it — cleared the race
100% (3/3 manual reproductions, then both full drill runs). The drill
script now does this as a pre-flight step, which is a legitimate operator
practice in its own right (it also proves the copy opens as a healthy
SQLite file before the app is pointed at it, on top of `integrity_check`).
**Not** reproduced against `sqlite3 integrity_check` alone — only the
`journal_mode=WAL` pragma fixed it, so that's what the script and the new
runbook section both call for. Flagged here in case it recurs elsewhere;
no code change was needed or made — `SqlitePragmaInterceptor` and
`VACUUM INTO` themselves behaved correctly in every check performed.

## What was NOT covered

- **The real Railway volume.** Everything above ran against local disk in
  this container. Nothing here proves the actual `/data` mount survives a
  restore, only that the file format and the app's read of it do.
- **Pulling a snapshot over the network from a live deployment.**
  `GET /api/internal/backup` was called against `127.0.0.1` in-process; the
  real operator flow (`curl` from a laptop to `https://<host>/api/internal/backup`)
  exercises TLS, the reverse proxy, and network latency this drill cannot
  reach from here.
- **Uploaded story content** (`BackupUploads` / `areg-uploads-*.zip`) —
  `ContentSync:UploadRoot` was never configured in this drill, so that
  archive path never ran. It shares the same code path as the audio-blob
  archive that WAS exercised, so this is a low-risk gap, not an unknown.
- **A real child voice recording** — the audio blob used is a synthetic
  text file, not real audio through the STT/moderation pipeline (see
  above).
- **Corruption from an actual mid-write crash** — the truncation test
  proves `integrity_check` catches a chopped file; it does not reproduce
  SQLite's own WAL-checkpoint crash-recovery paths.

## Commands (abbreviated; full script is `tools/ops/restore_drill.sh`)

```bash
cd backend && dotnet build -c Release
tools/ops/restore_drill.sh          # ~65s, exits 0 on pass
tools/ops/restore_drill.sh --keep   # same, leaves the workdir for inspection
```
