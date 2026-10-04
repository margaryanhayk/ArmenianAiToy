# Credential rotation and production settings — template, 2026-10

The record of the owner-this-week package
(`docs/review/fix-plan-2026-10-02.md`): which Railway variables are set,
which leaked credentials were rotated and how the old one was proven dead,
and the vendor letters' ticket numbers.

**Dates, variable NAMES, ticket numbers and one-line outcomes only.** Never a
key, token, password, seed, chat id, ping URL or any other value — not even a
fragment. The values live in the password manager (`docs/continuity.md`).

Status: **TEMPLATE — nothing recorded yet.**

## 1. Railway platform

| Item | Value to record | Date checked |
|---|---|---|
| Volume mounted at `/data` | yes / no; size; usage | [FILL IN] |
| Region | [FILL IN: region name] | [FILL IN] |
| Plan | [FILL IN: Pro / other] | [FILL IN] |
| Volume backups | Daily (kept 6 d) on / off; Weekly (kept 27 d) on / off; first manual backup taken | [FILL IN] |
| Encryption at rest | Railway's written answer: [FILL IN one line] | [FILL IN] |
| Custom domain | `api.<domain>` live, health 200 | [FILL IN] |

## 2. Railway variables (names only — `docs/railway-deploy.md` § 4)

Mark each: **set** (value in the vault), **unset**, or **deleted**. For an
"unset / leave alone" row, write what it is set to only if it is a
non-secret value that differs from the checklist.

| Variable | State | Date |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | [FILL IN] | [FILL IN] |
| `OpenAI__ApiKey` | [FILL IN] | [FILL IN] |
| `Jwt__Keys__0` | [FILL IN] | [FILL IN] |
| `Jwt__Key` / `JWT__KEY` / `Jwt__Keys__1`+ | [FILL IN: deleted?] | [FILL IN] |
| `AllowedHosts` | [FILL IN] | [FILL IN] |
| `ForwardedHeaders__Enabled` | [FILL IN] | [FILL IN] |
| `Internal__Operators__0__Name` / `__Token` / `__TotpSecret` | [FILL IN] | [FILL IN] |
| `Internal__Operators__1__Name` / `__Token` (backup-bot, no TOTP) | [FILL IN] | [FILL IN] |
| `Internal__RequireSession` | [FILL IN] | [FILL IN] |
| `Internal__AdminToken` | [FILL IN: deleted / never existed] | [FILL IN] |
| `Devices__ProvisioningSecret` | [FILL IN] | [FILL IN] |
| `OpenAI__DailyCostCap__Global` | [FILL IN] | [FILL IN] |
| `Alerts__WebhookUrl` / `Alerts__TelegramChatId` | [FILL IN] | [FILL IN] |
| `Notifications__Transport` / `__Resend__ApiKey` / `__Resend__FromAddress` / `__PasswordResetLinkBase` | [FILL IN] | [FILL IN] |
| `AI__ChatProvider` / `Gemini__*` | [FILL IN] | [FILL IN] |
| `AI__TtsProvider` | [FILL IN: unset / openai] | [FILL IN] |
| `Database__ConnectionString`, `Audio__BlobStoreRoot`, `StoryAudio__CacheRoot` | [FILL IN: unset / what they point at] | [FILL IN] |
| `OpenAI__TranscriptionModel`, `StoryQa__TranscriptionModel`, `Devices__VoiceIntentTranscriptionModel` | [FILL IN: unset / model name] | [FILL IN] |
| `FirmwareUpdate__SigningKey` | [FILL IN: unset until 1.3.5] | [FILL IN] |

System tab after the last deploy (`admin.html` → System): checks still
showing [FILL IN: none / codes]; Live voice provider [FILL IN]; Fleet daily
cap [FILL IN].

## 3. Rotations

| Credential | Why | New one generated locally and stored in the vault | Old one proven dead (how) | Date |
|---|---|---|---|---|
| Bench toy device key | Leaked in public git history (C151) | `provision_toy.py --rotate-existing` | `POST /api/devices/heartbeat` with the old id/key → 401; toy serial `[heartbeat] status=200` on the new key | [FILL IN] |
| Home Wi-Fi password | Committed in `config.h` on 2026-06-14 (C151) | Router admin; also changed wherever reused | Toy re-provisioned over BLE with the new password | [FILL IN] |
| JWT signing key | Pasted into a chat (C179) | `Jwt__Keys__0` | A browser logged in before the change had to log in again | [FILL IN] |
| Console access | Shared `Internal__AdminToken` → named operators + TOTP (C085) | `Internal__Operators__0/1__*` | Old shared token → 404; a wrong TOTP code refused | [FILL IN] |
| Provisioning secret | New (C170) | `Devices__ProvisioningSecret` | A wrong secret → 401 on `POST /api/devices/register` | [FILL IN] |
| OTA HMAC key | Old key sits in a public binary (C087) | Stored as "OTA HMAC key, 1.3.5 and later"; **not** on Railway until the 1.3.5 bench flash | Old key treated as burned | [FILL IN] |
| Other vendor keys ever pasted into a chat | [FILL IN: names only, e.g. "Resend API key"] | [FILL IN] | [FILL IN] | [FILL IN] |

Other device rows revoked in the console with reason "credential in public
git history":

| Device id (first 8 characters) | Date |
|---|---|
| [FILL IN] | [FILL IN] |

## 4. Repository

| Item | Date |
|---|---|
| Visibility changed to private | [FILL IN] |
| Railway redeploy after the change builds and goes live | [FILL IN] |
| Unauthenticated `GET https://api.github.com/repos/margaryanhayk/ArmenianAiToy` → 404 | [FILL IN] |

## 5. Monitoring, email, backups

| Item | Date | Note (one line) |
|---|---|---|
| Telegram browser `sendMessage` test arrived | [FILL IN] | |
| UptimeRobot keyword monitor on `/api/health`, test notification arrived | [FILL IN] | |
| Optional end-to-end alert (relative `Audio__BlobStoreRoot` flip): "unconfigured" and "recovered" both arrived | [FILL IN] | |
| Resend domain verified (SPF/DKIM/DMARC) | [FILL IN] | |
| Password-reset and verification emails reached a non-owner inbox | [FILL IN] | |
| healthchecks.io check created (1 day, grace 6 h, Telegram) | [FILL IN] | |
| `pull_backup.ps1` first manual run: file saved, `integrity_check=ok`, check green | [FILL IN] | |
| Task Scheduler job daily 03:30 | [FILL IN] | |
| healthchecks.io green 3 days running | [FILL IN] | |
| Redeploy-persistence check: Last backup and a parent login survive a redeploy | [FILL IN] | |
| Rate-limit split: phone on mobile data → "too many", laptop still logs in | [FILL IN] | |

## 6. Vendor letters

| Letter | Sent via | Date | Ticket number | Reply date | Outcome (one line) |
|---|---|---|---|---|---|
| 4b ElevenLabs (`docs/legal/vendor-terms-and-ai-toy-laws-2026-09.md`) | [FILL IN] | [FILL IN] | [FILL IN] | [FILL IN] | [FILL IN] |
| 4c OpenAI ZDR — sales form | sales contact form | [FILL IN] | [FILL IN] | [FILL IN] | [FILL IN] |
| 4c OpenAI ZDR — support chat | help.openai.com | [FILL IN] | [FILL IN] | [FILL IN] | [FILL IN] |
| Railway — encryption at rest (C078) | support | [FILL IN] | [FILL IN] | [FILL IN] | [FILL IN] |

Until OpenAI confirms Zero Data Retention in writing, no child outside the
owner's family uses the toy. No answer after 3 weeks, or a refusal: tell
Claude (speech-to-text alternative, `docs/ai-landscape-2026-09.md` O2).

## 7. Continuity

| Item | Date |
|---|---|
| Password manager with emergency access set up; second holder named (relationship only here, not the name) | [FILL IN] |
| Printed `docs/continuity.md` given to the second holder | [FILL IN] |
| Offline export to an encrypted drive (git mirror, release `config.h`, voice recordings, one DB backup) | [FILL IN] |
