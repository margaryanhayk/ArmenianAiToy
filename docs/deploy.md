# Deploying the Areg backend

This document covers building and running the .NET 10 backend in a
container. It is intentionally minimal — one image, one volume,
environment-variable secrets, no orchestration, no HTTPS. TLS,
metrics scraping policy, load balancing, and high-availability
concerns live with whatever reverse proxy / scheduler hosts the
image.

ESP32 firmware is out of scope for this image.

For a validated Windows non-Docker deployment path (publish + run
the exe directly), see `docs/windows-publish-deploy.md`. To run
that published exe as an auto-starting background service on
Windows, see `docs/windows-service-deploy.md`. Both runbooks —
the publish path and the NSSM service path including cold-reboot
auto-start — are validated end-to-end for hosts where Docker is
unavailable. Docker remains the canonical production posture; the
Windows-publish and Windows-service runbooks are siblings, not
replacements.

## What's in the image

- The `ArmenianAiToy.Api` publish output and its three project
  dependencies (`Domain`, `Application`, `Infrastructure`).
- The .NET 10 ASP.NET Core runtime.
- Listens on `http://0.0.0.0:8080` inside the container.
- Runs as the non-root user that ships with
  `mcr.microsoft.com/dotnet/aspnet:10.0` (UID `$APP_UID`).

## What's NOT in the image (by design)

- Test projects (`backend/tests/`, top-level `tests/`).
- Tooling (`tools/`, including `BenchmarkAll` and
  `StoryModelBakeoff`).
- Repo docs (`docs/`, `*.md`, the status PDF).
- ESP32 firmware (`esp32/`).
- Any SQLite DB files (`*.db`, `*.db-shm`, `*.db-wal`).
- Audio-blob runtime artifacts (`audio-blobs/`).
- Local agent / IDE / VCS metadata (`.claude/`, `.vs/`,
  `.vscode/`, `.idea/`, `.git/`).
- Dev configuration overlays (`appsettings.Development.json`,
  `launchSettings.json`, `secrets.json`, `.env*`).

The full filter is in `.dockerignore` at the repo root.

## Build

From the repository root:

```bash
docker build -t areg-backend:dev .
```

The build context is the repo root; `.dockerignore` keeps the
context lean. The build is a standard multi-stage `dotnet
publish -c Release` followed by a copy into the runtime base
image.

## Required environment variables

| Variable                   | Why                                                                                    |
| -------------------------- | -------------------------------------------------------------------------------------- |
| `OpenAI__ApiKey`           | Real OpenAI key. Image will start without it, but every chat call fails 502.           |
| `Jwt__Keys__0`             | Primary HS256 signing key for parent JWTs. Must NOT be the legacy insecure default. The validator rejects empty / poisoned key sets at startup. The scalar legacy `Jwt__Key` is still honored as a one-element list — see § JWT key rotation in `CLAUDE.md`. |

## Recommended environment variables

| Variable                                  | Default in image       | Notes                                                                   |
| ----------------------------------------- | ---------------------- | ----------------------------------------------------------------------- |
| `Database__ConnectionString`              | `Data Source=/data/armenian_ai_toy.db` | SQLite path. Keep it inside `/data` so it lives on the mounted volume. |
| `Audio__BlobStoreRoot`                    | `/data/audio-blobs`    | Local-disk audio blob store. Same volume as the DB.                     |
| `ASPNETCORE_URLS`                         | `http://0.0.0.0:8080`  | Override only if you need a different in-container port.                |
| `ASPNETCORE_ENVIRONMENT`                  | `Production` (image default) | Sets logging verbosity + Swagger UI gating to production posture.   |

## Optional environment variables

| Variable                              | Default     | Effect when set                                                          |
| ------------------------------------- | ----------- | ------------------------------------------------------------------------ |
| `GoogleAuth__ClientId`                | empty       | Enables `POST /api/parents/google-login`. Empty → endpoint returns 404.  |
| `Metrics__ScrapeToken`                | empty       | Bearer token required on `GET /metrics`. With both this and `AllowUnauthenticatedScrape=false`, `/metrics` returns 404 to unauthenticated callers (fail-closed concealment). |
| `Metrics__AllowUnauthenticatedScrape` | `false`     | `true` opens `/metrics` to any caller. Use ONLY behind a private network. |
| `Notifications__Transport`            | `log`       | `log` → emails are written to stdout via `LoggingNotifier`. `smtp` requires the full `Notifications__Smtp__*` block. |
| `OpenAI__DailyCostCap__Enabled`       | `true`      | Whole-day per-device chat cost cap.                                      |
| `OpenAI__DailyCostCap__Default`       | `0.50`      | USD/day per device.                                                      |
| `Retention__Messages__MaxAgeDays`     | `90`        | Message/conversation TTL. Set `<= 0` to disable the retention worker.    |

Other knobs (rate limits, retention sub-passes, dormancy windows,
JWT issuer/audience) are documented in `CLAUDE.md` and follow the
same `Section__Key__Subkey` double-underscore convention.

## Volumes

The image declares a single `VOLUME ["/data"]`. Everything the
app writes persistently lives there:

- `/data/armenian_ai_toy.db` (+ `-shm`, `-wal`)
- `/data/audio-blobs/...`

Mount a host directory at `/data` so state survives container
restarts. The mounted directory must be writable by `$APP_UID`
(the aspnet:10 image's default non-root user). On Linux:

```bash
mkdir -p /srv/areg/data
chown -R 1654:1654 /srv/areg/data
```

(`1654` is the current `$APP_UID` on the aspnet:10 image. If
Microsoft bumps the UID in a future image revision, adjust
accordingly.)

## Migrations

`db.Database.Migrate()` runs on startup (see `Program.cs`). On a
fresh volume, the initial schema is created end-to-end. On an
existing volume, any unapplied migrations apply in order. The
most recent migration is
`20260519120000_AddDeviceApiKeyHash`, which makes
`Devices.ApiKey` nullable and adds `Devices.ApiKeyHash`; legacy
plaintext rows continue to authenticate and lazy-upgrade on first
successful auth.

DBs created with `EnsureCreated()` (i.e. pre-`Migrate()` cut-over)
need the baseline-adoption procedure in `CLAUDE.md § Database
migrations` before mounting. Fresh dev databases should be
deleted before first run.

## Run

A minimal invocation:

```bash
docker run --rm -d \
  --name areg-backend \
  -p 5000:8080 \
  -v /srv/areg/data:/data \
  -e OpenAI__ApiKey="sk-..." \
  -e Jwt__Keys__0="<at-least-32-chars-of-random>" \
  areg-backend:dev
```

The toy/firmware then talks to `http://<host>:5000`. To use
HTTPS, terminate TLS at a reverse proxy (nginx, Caddy, Traefik,
or your platform's ingress) and forward to the container's
plaintext 8080.

## Sanity-check the running container

Inside the host after starting the container:

```bash
# 1. Health probe — should return 200 ok.
curl -sf http://localhost:5000/api/health
# → {"status":"ok"}

# 2. Logs (JSON, ISO-8601 UTC timestamps).
docker logs --tail 50 areg-backend

# 3. Metrics scrape (requires Metrics__ScrapeToken to be set or
#    AllowUnauthenticatedScrape=true). With neither, /metrics
#    returns 404 by design — see § Metrics in CLAUDE.md.
curl -sf -H "Authorization: Bearer <token>" \
  http://localhost:5000/metrics | head -20
```

## What the image will NOT do for you

- Provide HTTPS / TLS. Put a reverse proxy in front of it.
- Manage secrets. Use Docker secrets / k8s secrets / your
  platform's secret store. Never bake credentials into the
  image.
- Back up `/data`. SQLite is single-file; a periodic
  `cp armenian_ai_toy.db <backup>` while the container is
  stopped (or via `.backup` from `sqlite3`) is the simplest
  starting point.
- Rotate the JWT signing key for you. See § JWT key rotation in
  `CLAUDE.md`; `Jwt__Keys__0` is the primary, `Jwt__Keys__1+`
  are accepted for validation during rotation.
- Send email. The default `Notifications__Transport=log` writes
  password-reset / verification mail to stdout. Switch to
  `smtp` and configure the full `Notifications__Smtp__*` block
  to actually deliver mail.

## Live build / run verification

Slice author note: `docker build` and `docker run` were not
exercised live on the host that produced this image because
Docker was not installed on the build machine when the slice
landed. Use the procedure above to validate on any host where
Docker is available. The Dockerfile is a standard multi-stage
`dotnet publish` + `aspnet:10.0` runtime — there is nothing
unusual in the image structure that depends on host-specific
behavior.

If you hit a build error, the most likely cause is a base-image
tag drift: Microsoft occasionally retires older `10.0.x` patch
tags as new ones land. Pin to `10.0` (the rolling major-minor
tag) as the Dockerfile does, or pin to a specific patch tag
once you've validated it.
