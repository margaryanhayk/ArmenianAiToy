# If Hayk is unavailable

*For the second key holder. Print it and keep it with your copy of the
password-manager emergency-access details. It names every secret by its
NAME only — the values are in the password manager, nowhere else.*

Areg is a storytelling toy for children aged 4–7. Each toy plays stories from
its own SD card and talks to one backend service on Railway for questions,
voice chat and the parents' dashboard. If nobody looks after it for a while,
the toys keep playing stories; what needs a person is money, safety and the
families.

---

## 1. The first hour

1. Open the password manager through **emergency access**. Everything below
   is in there.
2. Sign in to the operator console (§ 3) and look at the **System** tab:
   **Checks** should say "All configuration checks pass", **Last backup**
   should be under a day old.
3. If anything is on fire (a safety report from a parent, a runaway bill, a
   leaked key), pause the toys (§ 4) and stop the spend (§ 5) first. Both
   are reversible. Ask questions afterwards.
4. Tell the families if the toys will be paused or the service will change
   (§ 6).

## 2. Where everything lives (names only)

| What | Password-manager entry | What it controls |
|---|---|---|
| Railway login + recovery codes | "Railway — login" | The backend. Every Railway secret below is also visible in Railway → project → service → **Variables** |
| GitHub login + recovery codes | "GitHub — login" | The code (`margaryanhayk/ArmenianAiToy`); Railway deploys from it |
| Domain registrar login + 2FA recovery | "Registrar — <domain>" | `api.<domain>`, which the toys and parents use. Auto-renew must stay on |
| OpenAI login | "OpenAI — login" | Billing; speech-to-text, safety moderation, live voice |
| OpenAI API key | "OpenAI API key" | Railway `OpenAI__ApiKey` |
| Google login | "Google — login" | Gemini chat billing (AI Studio or Google Cloud) |
| Gemini key / Vertex service account | "Gemini key" or "Vertex service account" | Railway `Gemini__ApiKey` or `Gemini__Vertex__ServiceAccountJson` |
| ElevenLabs login | "ElevenLabs — login" | The story voices were made here. The custom voices live in this account |
| Resend login + API key | "Resend — login", "Resend API key" | Password-reset and verification emails; Railway `Notifications__Resend__ApiKey` |
| Telegram alert bot | "Telegram alert bot token", "Telegram alert chat id" | Railway `Alerts__WebhookUrl` (the token is inside it), `Alerts__TelegramChatId` |
| healthchecks.io, UptimeRobot logins | "healthchecks.io — login", "UptimeRobot — login" | The outside monitors that alert when backups or the site stop |
| Console token + TOTP seed (Hayk) | "Console — hayk token", "Console — hayk TOTP seed" | Railway `Internal__Operators__0__Token`, `Internal__Operators__0__TotpSecret` |
| Console token (backup bot) | "Console — backup-bot token" | Railway `Internal__Operators__1__Token`; also on the backup PC only |
| JWT signing key | "JWT signing key" | Railway `Jwt__Keys__0` (parents' sign-in) |
| Provisioning secret | "Provisioning secret" | Railway `Devices__ProvisioningSecret` (registering or re-keying a toy) |
| OTA signing keys | "OTA HMAC key, 1.3.4 and earlier (burned)", "OTA HMAC key, 1.3.5 and later" | Firmware updates. Railway `FirmwareUpdate__SigningKey` once 1.3.5 ships |
| Release `config.h` | "Release config.h" (file attachment) | Needed to build the next firmware release |
| Original voice recordings | "Voice recordings — location" | Where the source recordings for the cloned voices are kept |
| Backup PC | "Backup PC — BitLocker recovery key" | The Windows machine that runs `tools/ops/pull_backup.ps1` daily |
| Offline export drive | "Offline export — drive passphrase" | Encrypted external drive: a git mirror, release `config.h`, voice recordings, one database backup (refreshed monthly) |
| Support / security mailbox | "Mailbox — support@<domain>" | Where parents and security reports write in |

Never paste any of these into a chat, an email or a file in the repository.
If one leaks, generate a new one (`docs/railway-deploy.md` § 4.5), put it in
the password manager and Railway, and redeploy.

## 3. Get into the operator console

1. Open `https://api.<domain>/admin.html`.
2. Paste "Console — hayk token", then the 6-digit code from the
   authenticator app (or add "Console — hayk TOTP seed" to your own
   authenticator: manual entry, time-based, 6 digits).
3. Everything you open is audited under the name `hayk`. If you will be
   doing this for more than a day, add yourself as a second named operator
   instead: Railway → Variables → `Internal__Operators__2__Name`,
   `Internal__Operators__2__Token`, `Internal__Operators__2__TotpSecret`
   (generate them as in `docs/railway-deploy.md` § 4.5) → Deploy.

Locked out of the console (lost authenticator, clock wrong): Railway →
Variables → delete `Internal__Operators__0__TotpSecret` → Deploy, then sign
in with the token alone (leave the code box empty). Generate a fresh seed,
set it again, add it to your authenticator and redeploy. (Deleting
`Internal__RequireSession` does not help: the console page always asks for
the code when the operator has a seed.)

## 4. Pause the toys

- Console → **Devices** → open a toy → **Pause** (a reason is required).
  A paused toy goes fully silent, stories from its card included, at its
  next check-in (a minute or two). **Resume** on the same screen undoes it.
  There is no "pause all" button: repeat it for each toy.
- **Revoke** (same screen) is stronger: the toy's key stops working
  entirely. Use it for a lost or stolen toy or a leaked key. **Restore**
  undoes it.

## 5. Stop the spending

1. **Fleet ceiling, fastest:** Railway → Variables →
   `OpenAI__DailyCostCap__Global` = `0.01` → Deploy. Every paid AI call
   (questions, voice chat) refuses for the rest of the UTC day; stories from
   the SD card keep playing. **Never `0`** — 0 means "no ceiling at all".
   Normal value: `5`.
2. **At the vendor:** OpenAI → Settings → Limits (monthly budget), or revoke
   the API key; Google Cloud → Billing → Budgets, or delete the Gemini key.
   With a key revoked every question fails until a new key is set in
   Railway; stories from the card keep playing.
3. **Monthly bills:** Railway (Pro plan), ElevenLabs subscription, the
   domain, the password manager. Do **not** cancel ElevenLabs or let the
   domain lapse: the voices live in that account, and every toy finds the
   service by that name.
4. **Shutting down for good:** first pull a backup
   (`docs/ops-runbook.md` § Take a backup, right now) and email the families
   (§ 6); only then stop the Railway service.

## 6. Reach the families

- Console → **Parents** tab lists every parent account's email.
- Write from the support mailbox, every parent in **BCC**, in plain words:
  what is happening, whether the toy still plays its stories (it does, with
  or without the service), and what they can do.
- Parents can export or delete their own data in the dashboard
  (`parent.html` → account): export, delete a conversation or a child,
  unlink the toy, delete the account. Point them there; if they ask you to
  do it, use the same dashboard with them rather than the database.
- A parent reporting something unsafe a child heard: pause that toy (§ 4)
  first, then read the conversation in the console (**Flagged** tab or the
  device's conversations) and reply.

## 7. Where the runbooks are

- `docs/ops-runbook.md` — is it up, logs, alerts, backups and restore,
  restarts, outages.
- `docs/railway-deploy.md` § 4 — every Railway variable, what it should be,
  how to check it.
- `docs/ota-release-runbook.md` — firmware updates (do not start one alone).
- `docs/factory-provisioning-runbook.md` — registering or re-keying a toy.

*Menu names in Railway, OpenAI, Google and ElevenLabs move; if a step does
not match the screen, look for the same word nearby. Keep this page in step
with `docs/railway-deploy.md` whenever a secret is added or renamed.*
