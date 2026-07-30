# Deploying the Areg backend on Railway (phone-first)

A step-by-step you can do entirely from a **phone browser**. Railway
builds the repo's root `Dockerfile` (see `docs/deploy.md` for the image
itself) and gives you a free HTTPS subdomain.

Prerequisite: the code is on GitHub (`margaryanhayk/armenianaitoy`), and
`railway.json` + this repo's `Dockerfile` are on the branch you deploy.

## 1. Create the service
1. Sign in at **railway.app** (GitHub login works on mobile).
2. **New Project → Deploy from GitHub repo → `armenianaitoy`**.
3. Pick the branch to deploy (usually `main` — see note at the bottom if
   the deploy config is still on a feature branch).
4. Railway auto-detects the root `Dockerfile` and starts building.

## 2. Add a persistent volume (DO THIS or you lose data)
The app writes SQLite + audio to `/data`. Without a volume, **every
redeploy wipes it.**
- Service → **Variables/Settings → Volumes → New Volume**.
- **Mount path: `/data`**.

## 3. Route the public URL to the container port
The container listens on **8080** (not Railway's default `$PORT`).
- Service → **Settings → Networking → Generate Domain**.
- When asked for the port, set **8080**. (If it auto-detected another
  port, change it to `8080`.)

## 4. Set environment variables
Service → **Variables → New Variable** for each:

| Variable | Value | Required? |
|---|---|---|
| `OpenAI__ApiKey` | your `sk-...` key | **Required** (chat 502s without it) |
| `Jwt__Keys__0` | the random key from the deploy session (paste from chat) | **Required** (startup rejects empty/default) |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Recommended (turns on prod guards) |
| `AllowedHosts` | your Railway domain, e.g. `areg-xxxx.up.railway.app` | Recommended (silences host-filter warning) |
| `GoogleAuth__ClientId` | (leave unset) | Optional — enables Google sign-in |

Notes:
- `Database__ConnectionString` and `Audio__BlobStoreRoot` are already set
  by the Dockerfile to `/data/...` — leave them unless you know why.
- Do **not** commit any of these secrets to the repo.

## 5. Verify it's up
Once the deploy is green, open in the phone browser:
- `https://<your-domain>/api/health` → should return **200** with a JSON
  body. Railway's own health check (`/api/health`, in `railway.json`) must
  pass for the deploy to go live.

## 6. Point the mobile app at it
Set the app's backend URL (no code change):
`EXPO_PUBLIC_API_BASE_URL=https://<your-domain>` — as an EAS build env
var / secret, or in `mobile/AregParent/eas.json`.

---

### Branch note
Railway deploys a specific branch. The deploy config (`railway.json`,
`docs/railway-deploy.md`) is being developed on a feature branch. To
deploy from `main`, that branch has to be merged first — or point Railway
at the feature branch temporarily for a first smoke test.

### SQLite caveat
SQLite on a single Railway volume is fine for a beta / small pilot. It
does **not** survive horizontal scaling (multiple instances) — move to
Postgres before scaling out. This matches the stopgap note in `CLAUDE.md`.
