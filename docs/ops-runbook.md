# Ops runbook

*One page, for the moment something is wrong and you do not want to read
architecture. Every command here has been run against a live instance.*

Deployment is a single Docker image on Railway (`railway.json`,
`Dockerfile`). All persistent state lives under **`/data`** — the SQLite
database, the audio blobs, the TTS cache, and the backups. If `/data` is not
a mounted volume, everything is lost on redeploy.

---

## Is it up?

```bash
curl -s https://<host>/api/health
```

```json
{"status":"ok","service":"ArmenianAiToy API","database":"ok","openai":"ok","audioStore":"ok"}
```

**Read the fields separately — they mean different things.**

- `status` / `database` decide **200 vs 503**, and they reflect *only* whether
  the database is reachable. Railway's healthcheck watches this.
- `openai` is **advisory and never changes the status code.** It reads
  `degraded` when the reliability gate's circuit breaker is currently open,
  meaning recent real failures. It is deliberately not fatal: OpenAI is shared
  by every instance, so failing the healthcheck during an OpenAI outage would
  pull the whole fleet out of the load balancer at once — an outage we caused
  ourselves on top of the one we were having.
- `audioStore` (2026-09-11) is **also advisory and never changes the status
  code.** It reads `unconfigured` when `Audio:BlobStoreRoot` is unset or a
  relative path in a non-Development environment. The app still boots and
  everything else still works — voice CHAT specifically refuses with 503
  (see below) rather than write recordings somewhere that gets wiped on the
  next redeploy.

So: **`openai: degraded` is not a reason to restart anything. `audioStore:
unconfigured` means set the variable below — restarting will not fix it.**

### Voice recordings are not durable → set `Audio__BlobStoreRoot`

`POST /api/chat/audio` refuses every turn with **503** when
`Audio:BlobStoreRoot` is unset or relative in a non-Development environment —
the same fail-closed contract `ContentSync:UploadRoot` already has. Set it to
an absolute path on the mounted volume:

```
Audio__BlobStoreRoot=/data/audio-blobs
```

Development is unaffected (it keeps the historical relative default next to
the binary, no configuration needed). See
`ArmenianAiToy.Api.Security.AudioBlobStoreRootResolver` for the exact rule.

### After the N10 deploy every parent logs in once (`Jwt:RequireSecurityStamp`)

Since 2026-09-12 (N10) every parent JWT carries the parent's security
stamp (`sst` claim) and is rejected once the stamp rotates — a password
change, a password-reset completion or a dormancy anonymization signs
that account out everywhere. Tokens issued BEFORE that deploy carry no
stamp, so with the shipped default (`Jwt:RequireSecurityStamp=true`) the
first deploy rejects every existing session with the usual 401: each
parent logs in once on the dashboard and once in the app, and nothing
else changes. That is a one-time effect of that first deploy, not
something that recurs.

If that one-time re-login is unacceptable at deploy time, set

```
Jwt__RequireSecurityStamp=false
```

BEFORE deploying: tokens without the claim then pass (a token WITH a
rotated-away stamp is still rejected either way, so the password-change
protection holds for every session started after the deploy). Remove
the variable — or set it to `true` — within 30 days: that is the token
lifetime, after which no pre-N10 token can exist and the switch does
nothing. It is a rollback switch, not a mode; do not leave it off.

### Parents locking each other out → set `ForwardedHeaders__Enabled`

Behind Railway's edge proxy, `Connection.RemoteIpAddress` is the proxy's own
IP for every request unless forwarded-header processing is turned on — the
per-IP auth rate limiter (`AuthRateLimiter`) then keys every parent into one
shared bucket, so a handful of families can lock each other out of
login/register. A boot-time warning names this when it applies. Set:

```
ForwardedHeaders__Enabled=true
ForwardedHeaders__KnownNetworks=<Railway's internal proxy CIDR>
```

(`ForwardedHeaders__KnownProxies` also works for a fixed proxy IP; on a
managed host like Railway there is no single stable IP to pin, so
`KnownNetworks` — a CIDR — is the pinnable unit.) Development is unaffected;
see `ArmenianAiToy.Api.Security.ForwardedHeadersConfig` for the exact rule.

## Where are the logs?

Stdout, as JSON, one object per line. On Railway that is the deploy log.
There is no file sink and no rotation in this repo — log retention is the
host's problem, deliberately, because a file sink would create a second
PII-adjacent surface with no owner.

Useful filters:

```bash
# gates tripping (paused / bedtime / disabled mode)
… | grep aat_chat_gate_trip
# OpenAI trouble
… | grep -E "OpenAIReliabilityGate|circuit"
# what an operator did in the console
… | grep InternalConsole
```

Each line carries the ASP.NET request scope (`RequestId`, `RequestPath`), so
one request's lines can be pulled together.

## Alerting

Opt-in webhook alerter (2026-09-12, N5) — `AlertingService`, a background
worker beside `RetentionPurgeService`/`DatabaseBackupService`. **Off by
default**: `Alerts:WebhookUrl` empty means nothing is ever posted.

```json
"Alerts": {
  "WebhookUrl": "",
  "CooldownMinutes": 30,
  "HealthCheckIntervalSeconds": 60,
  "CostCapTripsThresholdPerHour": 5
}
```

Set `Alerts__WebhookUrl` (Railway env var, same double-underscore-for-colon
convention as everything else here) to enable it. Works with either:

- A **Slack incoming webhook** URL — Slack reads the `text` field and
  ignores the rest.
- Any **generic JSON receiver** — the full body is
  `{ "text": "...", "key": "...", "severity": "...", "at": "..." }`.

Signals (each cooldown-gated per `key` so a flapping condition cannot
spam):

| Key | Fires when |
|---|---|
| `health_db_unhealthy` / `health_db_recovered` | DB liveness check (same probe `/api/health` uses) transitions |
| `health_audio_store_unconfigured` / `health_audio_store_recovered` | `Audio:BlobStoreRoot` fail-closed state transitions |
| `cost_cap_trips_high` | `aat_openai_cost_cap_trip_total` trips in the last rolling hour exceed `CostCapTripsThresholdPerHour` |
| `openai_circuit_open` | `aat_chat_openai_circuit_trip_total` increments (a closed→open transition) |
| `moderation_unavailable` | `aat_moderation_failclosed_total` increments — moderation is fail-closing, chat turns are being blocked |
| `backup_stale` | the newest `areg-backup-*.db` snapshot is older than 36h, or absent |

Delivery: a plain `HttpClient` (10s timeout, one retry); a failed POST is
logged at Warning and never retried in a loop. Each alert actually
delivered increments `aat_alerts_sent_total{key}` (bounded keys — see the
table above). Cooldown state is in-memory only and resets on restart.

## OpenAI is down or rate-limiting

Nothing to do. It is handled:

- One retry on 429 / timeout / 5xx; never on auth failures.
- A circuit breaker opens after 5 failures in 30 seconds and stays open 60
  seconds, then lets one probe through.
- Children hear the sanitized fallback, not an error.
- Stories keep playing — they come off the SD card and need no network at all.

Watch `aat_chat_openai_circuit_trip_total` and the health endpoint's `openai`
field. Restarting the service does **not** help and throws away the breaker
state that is protecting the upstream.

## Take a backup, right now

```bash
curl -H "Authorization: Bearer $ADMIN_TOKEN" \
     https://<host>/api/internal/backup -o areg-$(date +%F).db
```

A fresh consistent SQLite snapshot, streamed. This is the **only** defence
against losing the volume — the automatic daily snapshots live on the same
volume as the database they protect. Pull one weekly and keep it somewhere
else.

**Audio blobs are not in it.** `/data/audio-blobs` holds child voice
recordings and is the one part that cannot be regenerated. Nothing backs it up
today.

## Restore procedure

Proven end-to-end for the first time 2026-09-12 — a scripted local drill
(register a parent, register + claim a device, rename it, pull a snapshot,
restore it to a fresh path, boot the API on it, verify row counts and a
parent login) passed twice. See
`tools/quality-evidence/backup-restore-drill-20260912.md` and
`tools/ops/restore_drill.sh`. **Not yet proven against the real Railway
volume or a network pull** — that step is still the operator's.

1. **Get a snapshot.** Either the newest `/data/backups/areg-backup-*.db`
   already on the volume, or a fresh offsite pull (see "Take a backup,
   right now" above) — the offsite pull is preferred, since it does not
   depend on the volume you may be trying to recover from.
2. **Check it before trusting it.**
   ```bash
   sqlite3 areg-2026-09-12.db "PRAGMA integrity_check;"
   ```
   Must print exactly `ok`. Anything else (e.g. `database disk image is
   malformed`) means this snapshot is not safe to restore from — get an
   older one and repeat this check.
3. **Stop the service.** Restoring under live traffic risks the running
   process writing to the file out from under you.
4. **Replace the live database file.** The path is whatever
   `Database__ConnectionString` is set to on this deployment — the
   Railway default is `/data/armenian_ai_toy.db` (`Dockerfile`); confirm
   with `railway variables` if unsure, never assume.
   ```bash
   rm -f /data/armenian_ai_toy.db-wal /data/armenian_ai_toy.db-shm
   cp areg-2026-09-12.db /data/armenian_ai_toy.db
   ```
   The `-wal`/`-shm` removal matters: those are sidecars of the file you
   are REPLACING, not the snapshot — leaving them behind risks SQLite
   trying to replay stale WAL frames against the restored file.
5. **Restart the service.** Migrations re-apply and are no-ops if the
   snapshot is current.
6. **Verify.**
   ```bash
   curl -s https://<host>/api/health   # expect "database":"ok"
   ```
   Then log in as a real parent account from before the incident (or ask
   one to) to confirm the data is not just present but usable — a restore
   that boots green but silently corrupted a password hash or a foreign
   key is worse than an obvious failure.

**Audio blobs are a separate, manual restore** — the offsite pull above
covers the database only. If `/data/audio-blobs` needs restoring too, it
comes from `DatabaseBackupService`'s own on-volume
`areg-audio-blobs-*.zip` (same `/data/backups` directory, same retention);
unzip it to `Audio:BlobStoreRoot` after step 4, before restarting.

## Get into the operator console

`https://<host>/admin.html`. The page loads for anyone; it is useless without
a token, because every call it makes needs one.

**Every `/api/internal/*` route 404s when no token is configured** — that is
the shipped default and it is deliberate: a scanner learns nothing, not even
that the route exists. To enable, set `Internal:Operators` (named, revocable
per person) or the legacy `Internal:AdminToken`.

`GET /api/internal/whoami` tells you which operator a token resolves to.

Reading a child's conversation from the console writes an audit row naming
the operator, what they opened and when. That is intended — look if you need
to, and know the record exists.

## Restart

Railway redeploys on push and restarts on failure (max 10 retries). A manual
restart is safe: migrations are guarded by a file lock so concurrent boots do
not race, and SQLite runs in WAL mode.

**Before restarting, be sure it is the answer.** It will not fix an OpenAI
outage, and it clears in-memory state that is doing useful work: the circuit
breaker, per-device rate-limit buckets, and the daily cost counters — which
means a device at its spending cap gets a fresh allowance.

## The toys during an outage

Mostly fine. Stories, greetings, games and bedtime lines are all on the SD
card and play with no backend at all. What stops is the online part: asking a
question mid-story, the reflection conversation, and play reporting — the toy
queues those events in flash and uploads them when the backend returns.

## Common causes of a boot failure

| symptom | cause |
|---|---|
| `ArgumentException: Value cannot be an empty string (Parameter 'key')` | `OpenAI:ApiKey` unset. A dummy value is enough to boot. |
| `Jwt signing key not configured` | `Jwt:Key` unset, or under 32 characters. |
| Fails immediately outside Development complaining about the connection string | `Database__ConnectionString` unset, or still the dev default. Intentional — it stops production silently running on a dev-named file. |
| Boots, but every toy 401s | Devices revoked, or the database is a restored snapshot predating their registration. |

---

*Verified 2026-08-12 against a live instance; the health output above is
copied from it, not written from memory. See
`tools/quality-evidence/clean-clone-boot-20260812.md`. The `audioStore` field
and the `Audio__BlobStoreRoot` section (2026-09-11) are NOT yet re-verified
against a live instance — confirmed by unit test only
(`AudioBlobStoreRootResolverTests`, `AudioChatControllerTests`).*
