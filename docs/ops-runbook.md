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
{"status":"ok","service":"ArmenianAiToy API","database":"ok","openai":"ok"}
```

**Read the two fields separately — they mean different things.**

- `status` / `database` decide **200 vs 503**, and they reflect *only* whether
  the database is reachable. Railway's healthcheck watches this.
- `openai` is **advisory and never changes the status code.** It reads
  `degraded` when the reliability gate's circuit breaker is currently open,
  meaning recent real failures. It is deliberately not fatal: OpenAI is shared
  by every instance, so failing the healthcheck during an OpenAI outage would
  pull the whole fleet out of the load balancer at once — an outage we caused
  ourselves on top of the one we were having.

So: **`openai: degraded` is not a reason to restart anything.**

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

## Restore

Stop the service, put the snapshot at `/data/armenian_ai_toy.db`, remove any
`-wal` / `-shm` sidecars beside it, start. Migrations re-apply themselves and
are no-ops if the snapshot is current.

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
`tools/quality-evidence/clean-clone-boot-20260812.md`.*
