# Device API key hashing at rest

## Why

Before this slice, every `Device` row stored its API key as plaintext
in the `Devices.ApiKey` column. A DB exfil (backup leak, dump in error
logs, SQL injection on a future feature) handed the attacker the
production credential for every paired device — equivalent to a
password file in cleartext.

This slice replaces plaintext storage with PBKDF2-SHA256 hashed
storage, modeled after the existing
`ParentPasswordResetToken.TokenHash` and parent BCrypt password
discipline. Hashes are irreversible; even a full table copy yields no
usable credentials.

User-visible behavior is unchanged for devices that have just been
registered. Existing legacy plaintext rows continue to authenticate
and are lazy-upgraded on first successful use.

## Hash format

```
v1:pbkdf2-sha256:<iterations>:<saltBase64>:<derivedHashBase64>
```

- **Version prefix** (`v1`) lets a future v2 (e.g. Argon2 if/when it
  becomes available in the BCL) coexist with v1 rows without a flag-
  day migration. `DeviceApiKeyHasher.Verify` accepts v1 only and
  fails closed on any other prefix.
- **Algorithm** (`pbkdf2-sha256`) is the .NET BCL primitive
  `Rfc2898DeriveBytes.Pbkdf2` — no new NuGet dependency.
- **Iterations** are stored per row. Default 50,000.
- **Salt** is per-row, 16 bytes from `RandomNumberGenerator`.
- **Derived hash** is 32 bytes.
- **Constant-time compare** via
  `CryptographicOperations.FixedTimeEquals` on the byte arrays —
  not the strings — so the byte-decode itself cannot leak a timing
  signal.

Device API keys carry 128 bits of entropy (`dtk_{Guid:N}`), so the
50K iterations are not an entropy stretcher — they exist to slow
down a leaked-DB rainbow attempt targeting a small number of
high-value devices. Verify cost is roughly 15–25 ms on a modern CPU;
the chat path is rate-limited to 30/min/device by `ChatRateLimiter`,
so worst-case per-device per-minute auth cost is ~0.75 s.

## Schema changes

Migration `20260519120000_AddDeviceApiKeyHash`:

- `Devices.ApiKey` changed from `TEXT NOT NULL` to `TEXT NULL`. The
  legacy unique index is dropped and replaced with a filtered unique
  index (`WHERE "ApiKey" IS NOT NULL`) so the steady state of many
  null values does not conflict with uniqueness.
- `Devices.ApiKeyHash` added as `TEXT NULL`. No index — auth lookups
  go by `Devices.Id` (the primary key), and a per-row salt makes a
  hash-keyed lookup pointless.

`Down()` restores the column types but cannot recover plaintext for
any row that has already been upgraded — rolling back requires re-
registering devices or restoring from backup.

## Registration behavior

`POST /api/devices/register` accepts the same payload and returns
the same `DeviceRegistrationResponse` shape `(DeviceId, ApiKey)`.
The internal write path branches three ways:

1. **New MAC.** Generate `dtk_{Guid:N}` plaintext, hash via
   `DeviceApiKeyHasher.Hash`, store with `ApiKey=null,
   ApiKeyHash=<hash>`, return the plaintext in the response. The
   plaintext is never persisted.
2. **Existing MAC, legacy plaintext row**
   (`ApiKey != null && ApiKeyHash == null`). Return the existing
   plaintext verbatim, then upgrade the row by setting
   `ApiKey=null, ApiKeyHash=Hash(existingPlaintext)`. Preserves the
   pre-slice idempotency contract: a firmware that re-registers
   without ever talking to the auth endpoint still gets back its
   working key.
3. **Existing MAC, hashed row** (`ApiKeyHash != null`).
   **Rotate** to a fresh `dtk_{Guid:N}` plaintext, write the new
   hash, return the new plaintext. The old plaintext is
   unrecoverable from the hash, so the only way to keep
   re-registration idempotent for an authenticated firmware is to
   hand it a new key. This is a documented behavior change from the
   pre-slice "return the same key forever."

In all three branches, `LastSeenAt` and `FirmwareVersion` are
updated, and a single info log line records what happened.

## Authentication behavior

`DeviceService.ValidateDeviceAsync(deviceId, apiKey)` (called from
`DeviceAuthMiddleware`) now:

1. Loads the device by id alone — the hashed compare cannot run in
   SQL because of the per-row salt.
2. If `ApiKeyHash` is a well-formed v1 hash, verifies via
   `DeviceApiKeyHasher.Verify`. Match → return device. Mismatch →
   return null.
3. Else if `ApiKey` (legacy plaintext) is non-empty, compares via
   `DeviceApiKeyHasher.ConstantTimeEquals`. Match → **lazy-upgrade**
   the row to hash (`ApiKey=null, ApiKeyHash=Hash(apiKey)`) and
   return the device. Mismatch → return null.
4. Else (no credential at all on the row) → return null.

The lazy upgrade is best-effort: a `SaveChangesAsync` failure during
the upgrade is logged at Warning but the request itself still
succeeds. The next successful auth will retry the upgrade.

`ParentService.LinkDeviceAsync` (called when a parent enters a
device API key on the dashboard to claim the device) uses the same
hash-aware verification path. Without that change, every device
registered after this slice would refuse to link, because the
SQL plaintext equality would never match a hashed row.

## Identity-leak posture

The "no existence leak via auth response" contract is preserved:

- Wrong device id and right-id-wrong-key both return null →
  `DeviceAuthMiddleware` returns the same 401 with the same body.
- Wrong-key cost on a legacy row is dominated by
  `ConstantTimeEquals` (constant-time bytes compare); on a hashed
  row, dominated by the PBKDF2 50K-iter derive. An attacker probing
  with random ids and keys cannot distinguish "device exists" from
  "device does not exist" beyond the cost of `FindAsync` itself,
  which is the same regardless of which branch runs.

## Data export

`GET /api/parents/export` adds `Device.ApiKeyHash` to the
`excludedFields` list. The DTO shape (`ParentExportDevice`) has no
counterpart property, so neither the plaintext column nor the hash
column ever appears in the export body. Pinned by the existing
`ParentControllerExportTests` exclusion-list assertion.

## Logs and audit

- Info-level log lines for new registration, legacy upgrade on
  re-register, rotation on re-register, and legacy upgrade on auth.
- No raw plaintext is ever logged. The structured templates carry
  only `DeviceId` / `MacAddress`.
- No new `AuditEventType` value. Registration was deliberately out
  of scope of audit before this slice and remains so — the audit
  surface is for sensitive parent actions, not device lifecycle.

## What was not changed

- `DeviceAuthMiddleware` itself — the credential check lives in
  `DeviceService.ValidateDeviceAsync`, and the middleware contract
  is "this method returns a device or null."
- ESP32 firmware. The wire shape of `/api/devices/register` and
  the `X-Api-Key` header semantics are unchanged from the
  firmware's perspective.
- OpenAI provider/model config.
- Moderation, safety, or chat behavior.
- Rate-limit policies.

## Manual smoke test (optional)

Run on an isolated `:5050` instance against a disposable SQLite DB
so the operator's `:5000` is undisturbed:

```bash
# Terminal 1 — isolated backend
cd backend
ASPNETCORE_URLS=http://0.0.0.0:5050 \
  Database__ConnectionString="Data Source=$LOCALAPPDATA/Temp/areg-hash-smoke.db" \
  dotnet run --project src/ArmenianAiToy.Api

# Terminal 2 — register a new device, capture the plaintext key
curl -s -X POST http://localhost:5050/api/devices/register \
  -H 'Content-Type: application/json' \
  -d '{"macAddress":"AA:BB:CC:DD:EE:01"}' | jq .
# → {"deviceId":"<guid>","apiKey":"dtk_<hex>"}

# Verify the DB row has hash, no plaintext
sqlite3 "$LOCALAPPDATA/Temp/areg-hash-smoke.db" \
  'SELECT Id, ApiKey, substr(ApiKeyHash,1,30) FROM Devices;'
# → ApiKey is empty/NULL, ApiKeyHash begins "v1:pbkdf2-sha256:50000:"

# Authenticate against the chat endpoint with the returned plaintext
curl -i -X POST http://localhost:5050/api/chat \
  -H "X-Device-Id: <guid>" -H "X-Api-Key: dtk_<hex>" \
  -H 'Content-Type: application/json' \
  -d '{"text":"բարև"}'
# → 200 OK (or moderation/Story envelope; not 401)

# Wrong key → 401
curl -i -X POST http://localhost:5050/api/chat \
  -H "X-Device-Id: <guid>" -H "X-Api-Key: dtk_wrong" \
  -H 'Content-Type: application/json' \
  -d '{"text":"բարև"}'
# → 401

# Re-register the same MAC; key rotates
curl -s -X POST http://localhost:5050/api/devices/register \
  -H 'Content-Type: application/json' \
  -d '{"macAddress":"AA:BB:CC:DD:EE:01"}' | jq .
# → deviceId same; apiKey is NEW; the old plaintext key no longer authenticates.
```

When printing or sharing logs, mask the returned key like
`dtk_****last4` — never paste the full plaintext into chat history,
issue trackers, or documentation.

## Test coverage

- `DeviceApiKeyHasherTests` — pure-static helper unit tests (Hash,
  Verify, IsHash, ConstantTimeEquals; positive, negative, and
  malformed-storage edge cases).
- `DeviceServiceTests` — registration (new / legacy upgrade /
  rotation) and validation (hashed valid / hashed wrong / legacy
  valid + upgrade / legacy wrong / hash-precedence-over-stale-
  plaintext / unknown id).
- Existing `ParentControllerExportTests` extended to pin the new
  exclusion-list entry.
