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
Service → **Variables → New Variable**. This is the full production
checklist, read from the code on 2026-10-03 (`Program.cs`,
`DependencyInjection.cs`, `appsettings.json`, `SystemStatusReport.cs`, the
`Dockerfile`). Anything not listed keeps its shipped default and needs no
variable.

Rules for every secret:
- **Generate it on your own computer** (§ 4.5) and paste it straight into
  the password manager, then into Railway. Never into a chat, an email, a
  ticket, a screenshot or a file in the repo. A key that was ever pasted into
  a chat is burned: generate a new one.
- Write down **names and dates only, never values**
  (`tools/quality-evidence/credential-rotation-2026-10.md`).

"System tab" = `https://<host>/admin.html` → **System** (sign in first).
"Health" = `https://<host>/api/health`. The order to set these in (which
ones share a deploy) is the owner-this-week package in
`docs/review/fix-plan-2026-10-02.md`.

### 4.1 Platform settings (not variables)

| Setting | Required? | Expected | How to verify |
|---|---|---|---|
| Volume | **Required** | mount path `/data`; write down its size and usage | Service → Volume shows `/data`. After a redeploy, the System tab's **Last backup** and a parent login both survive — the app itself cannot tell an unmounted `/data` from a mounted one |
| Region | **Required** (decision) | write it down; move (e.g. to EU West) only before any outside family's data exists, after pulling a backup | Service → Settings → Region; Claude copies it into `docs/privacy-parents.md` |
| Public port | **Required** | `8080` (§ 3) | Health returns 200 |
| Custom domain | Before email and `security.txt` | `api.<domain>`, port 8080, CNAME at DNS as **DNS-only** (no Cloudflare proxy: the toy pins Let's Encrypt's root) | `curl https://api.<domain>/api/health` → 200. Never delete the railway.app domain while a toy has it compiled in |

### 4.2 Must be set

| Variable | Required? | Value | How to verify |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | **Required** | `Production` | System tab footer reads `Production` |
| `OpenAI__ApiKey` | **Required** | secret — keep in vault | System → Configured: **OpenAI key: set**. Health `"openai":"ok"` |
| `Jwt__Keys__0` | **Required** | secret — generate locally (§ 4.5), never paste from a chat | The app boots (it refuses to start without a key); a parent can log in |
| `AllowedHosts` | **Required** | `armenianaitoy-production.up.railway.app;healthcheck.railway.app` — append `;api.<domain>` once the domain exists | The deploy goes live. Railway's own healthcheck calls with Host `healthcheck.railway.app`; leave it out and every deploy fails its healthcheck (Railway keeps the old one serving — delete the variable and redeploy). Set it in a deploy of its own. Logs tab: no "AllowedHosts is unset" warning |
| `ForwardedHeaders__Enabled` | **Required** | `true` (the shipped `KnownNetworks` already covers Railway's proxy) | System → **Forwarded headers: set**, check `forwarded_headers` gone. 11 wrong passwords from a phone on mobile data get "too many" while the laptop at home still logs in |
| `Internal__Operators__0__Name` | **Required** | `hayk` | After sign-in, the console header shows `hayk` |
| `Internal__Operators__0__Token` | **Required** | secret — keep in vault | Console sign-in works |
| `Internal__Operators__0__TotpSecret` | **Required** | secret (base32) — keep in vault and in the authenticator app | A wrong 6-digit code is refused |
| `Internal__Operators__1__Name` | **Required** (off-site backup) | `backup-bot` | — |
| `Internal__Operators__1__Token` | **Required** (off-site backup) | secret — vault + the backup machine's token file only. **No** `TotpSecret` (a scheduled job cannot type a code) | `tools/ops/pull_backup.ps1` saves a file with `integrity_check=ok` |
| `Internal__RequireSession` | **Required** | `true` | A token alone gets 404 on every console data call; token + code signs in. Lost the authenticator → delete `Internal__Operators__0__TotpSecret`, redeploy, sign in with the token alone, then set a fresh seed (deleting this variable does not help: the console page always asks for the code when a seed is set) |
| `Devices__ProvisioningSecret` | **Required** | secret — keep in vault | `tools/factory/provision_toy.py` gets 201; a wrong secret gets 401 |
| `OpenAI__DailyCostCap__Global` | **Required** | `5` (USD per day, whole fleet) | System → **Fleet daily cap** card $5.00, check `global_cap_off` gone |
| `Alerts__WebhookUrl` | **Required** | secret (it embeds the bot token): `https://api.telegram.org/bot<TOKEN>/sendMessage` | System → **Alerts webhook: set**, check `alerts_off` gone |
| `Alerts__TelegramChatId` | With Telegram | the chat id (not a secret; keep it in the vault anyway) | A real alert arrives — `docs/ops-runbook.md` § Alerting |
| `Notifications__Transport` | **Required** before any outside parent | `resend` (the default `log` sends nothing) | No System-tab check yet: a password reset to an inbox that is not yours arrives |
| `Notifications__Resend__ApiKey` | With `resend` | secret — a sending-only key limited to the domain | Same email test |
| `Notifications__Resend__FromAddress` | With `resend` | `Areg <noreply@<domain>>` | Same email test |
| `Notifications__PasswordResetLinkBase` | With `resend` | `https://api.<domain>/parent.html` | The link in the reset email opens the reset form |

### 4.3 Chat provider (per the provider decision)

| Variable | Required? | Value | How to verify |
|---|---|---|---|
| `AI__ChatProvider` | **Required** | `gemini` or `openai` (the repo default is `openai`; Railway decides) | System → Models: **Chat (every reply)** row |
| `Gemini__ApiKey` | If chat = gemini on AI Studio | secret (`GEMINI_API_KEY` is also read — keep only one) | System → **Gemini key: set** |
| `Gemini__Backend` | If chat = gemini | unset (= `ai-studio`) or `vertex` | Chat row note `backend: …`; the `gemini_terms` check shows while on AI Studio |
| `Gemini__Vertex__ServiceAccountJson` | If `vertex` | secret | System → **Vertex service account: set** |
| `Gemini__Vertex__ProjectId`, `Gemini__Vertex__Location` | If `vertex` | the project id; the location from Google's answer (default `global`) | `docs/ops-runbook.md` § Move Gemini chat to Vertex AI |
| `Gemini__Model` | Optional | from the bake-off (default `gemini-3.6-flash`) | System → Models: **Chat (every reply)** row shows the model |
| `Gemini__SafetyThreshold` | Optional | from the bake-off (empty = `BLOCK_LOW_AND_ABOVE`) | — |
| `AI__ChatCostPerMTokensIn`, `AI__ChatCostPerMTokensOut` | Optional | the provider's list price, USD per 1M tokens | System → spend estimates |

### 4.4 Leave unset, or delete

| Variable | Expected | Why / how to verify |
|---|---|---|
| `AI__TtsProvider` | unset or `openai` | Never `elevenlabs` (vendor terms, `docs/legal/vendor-terms-and-ai-toy-laws-2026-09.md`). System → **Text-to-speech (live voice)** provider `openai` |
| `ElevenLabs__ApiKey` / `ELEVENLABS_API_KEY` | optional secret | Only feeds the System tab's **Credits left**; it does not switch live voice |
| `AI__TranscriptionProvider`, `AI__ModerationProvider` | unset (`openai`) | Moderation stays on OpenAI, fail-closed. An unknown value refuses boot |
| `Database__ConnectionString` | unset | The `Dockerfile` sets `Data Source=/data/armenian_ai_toy.db`. If set, it must be under `/data`. Health `"database":"ok"` |
| `Audio__BlobStoreRoot` | unset | The `Dockerfile` sets `/data/audio-blobs`. Health `"audioStore":"ok"`, System → **Audio storage: set** |
| `StoryAudio__CacheRoot` | unset | The `Dockerfile` sets `/data/story-audio-cache` |
| `StoryAudio__SigningKey` | optional secret; **keep it** if it already exists | Unset = the Wi-Fi story stream is refused and toys play only what is on their SD card (a story not yet synced does not play) |
| `ContentSync__UploadRoot` | unset (console uploads disabled) or a path under `/data` (`/data/content-uploads`) | Unset = the console's story upload answers "Uploads are not configured on this deployment" |
| `OpenAI__TranscriptionModel`, `StoryQa__TranscriptionModel`, `Devices__VoiceIntentTranscriptionModel` | unset | The repo ships `gpt-transcribe`; a Railway value wins. System → the three **Speech-to-text** rows read `gpt-transcribe`, no `model_retirement` check |
| `OpenAI__ChatModel`, `OpenAI__TtsModel`, `OpenAI__TtsVoice`, `OpenAI__ModerationModel`, `StoryQa__AnswerModel` | unset unless benchmarked | System → Models shows what is live |
| `Internal__AdminToken` | **delete** | Legacy shared token: no name, no MFA. The old token then gets 404 |
| `Internal__AllowUnauthenticated`, `Devices__AllowOpenRegistration`, `Metrics__AllowUnauthenticatedScrape`, `StoryAudio__AllowUnauthenticated` | must not exist | Dev/bench bypasses |
| `Jwt__Key`, `JWT__KEY`, `Jwt__Keys__1` and up | **delete** at the JWT rotation | Once outside parents exist, keep the old key as `Jwt__Keys__1` for 30 days instead. A browser logged in before the rotation must log in again |
| `FirmwareUpdate__SigningKey` | **not now** | Set on the evening the bench toy is cable-flashed with 1.3.5 (`docs/ota-release-runbook.md` § 8) |
| `FirmwareUpdate__Enabled` | unset (false) | — |
| `FirmwareUpdate__ImagePath` | **not now** | `/app/firmware/areg-current.bin` once OTA goes live — absolute, set once (`docs/ota-release-runbook.md` § 3) |
| `Security__RequireHttps` | unset | Untested against Railway's internal healthcheck |
| `OpenAI__DailyCostCap__Enabled`, `OpenAI__DailyCostCap__Default` | unset (`true` / `0.25` USD per toy per day) | `Enabled=false` also turns off the fleet ceiling (`Global` is only checked while it is true). A `0.50` left over from `docs/deploy.md` doubles the per-toy cap the $5 fleet ceiling is sized against — delete it. System: no `cost_cap_off` check |
| `Usage__Tiers__Enabled` | unset (false) | No price is decided. `true` also bypasses the fleet ceiling |
| `Retention__Messages__MaxAgeDays` | unset (90) | 0 or less turns the 90-day purge off and logs a boot warning |
| `Backup__Database__*`, `Backup__AudioBlobs__*` | unset | Defaults: a daily DB snapshot and audio zip under `/data/backups`. System → **Last backup** |
| `Metrics__ScrapeToken` | optional secret | Only if a Prometheus scraper exists. System → **Metrics token** |
| `GoogleAuth__ClientId` | optional | Enables Google sign-in; not a secret |

Railway injects `RAILWAY_GIT_COMMIT_SHA` itself (the System tab footer's
commit).

### 4.5 Generate every secret on your own computer

Python 3, in PowerShell or a terminal. Paste each output straight into the
password manager. Add `| Set-Clipboard` (PowerShell) or `| pbcopy` (macOS)
to keep it off the screen.

| Secret | Goes into | Command |
|---|---|---|
| JWT signing key (64 chars) | `Jwt__Keys__0` | `python -c "import secrets;print(secrets.token_urlsafe(48))"` |
| Operator token (run once per operator) | `Internal__Operators__0__Token`, `Internal__Operators__1__Token` | `python -c "import secrets;print(secrets.token_urlsafe(48))"` |
| TOTP seed (base32, 32 chars) | `Internal__Operators__0__TotpSecret` | `python -c "import base64,secrets;print(base64.b32encode(secrets.token_bytes(20)).decode())"` — in the authenticator app: enter key manually, time-based, 6 digits (SHA-1, 30 s, what `Totp.cs` checks) |
| Provisioning secret | `Devices__ProvisioningSecret` | `python -c "import secrets;print(secrets.token_urlsafe(48))"` |
| OTA HMAC key, 1.3.5 and later | the vault now; later `AREG_MANIFEST_HMAC_KEY` in the 1.3.5 release `config.h` and `FirmwareUpdate__SigningKey` | `python -c "import secrets;print(secrets.token_hex(32))"` — 64 hex characters. Both sides use the string's own bytes as the key (backend UTF-8, firmware `strlen`), and hex never needs escaping inside a C string |

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
`Dockerfile`, `docs/railway-deploy.md`) is on `main` — point Railway's
service at `main` (or whichever branch you deploy from) directly.

### SQLite caveat
SQLite on a single Railway volume is fine for a beta / small pilot. It
does **not** survive horizontal scaling (multiple instances) — move to
Postgres before scaling out. This matches the stopgap note in `CLAUDE.md`.
