# Factory provisioning runbook

How to give one toy its identity: a device id, a backend API key, and a
per-device BLE pairing code (PoP) — and how to print the label that goes on
its box.

Written 2026-09-11 for the factory-pairing slice. Replaces the single shared
BLE pairing code (`areg-pair`) every toy used to advertise; see CLAUDE.md §
"State of the toy" for the summary and `esp32/AregVoiceMvp/README.md`'s bench
checklist for what has and has not been heard on real hardware.

---

## 0. What gets stored, what gets printed

Three secrets, three different fates. Confusing any two of them is the
mistake this section exists to prevent.

| Secret | Where it lives after provisioning | Ever printed? | Ever stored on the backend? |
|---|---|---|---|
| **Device API key** (`X-Api-Key`) | Toy's NVS (`aregdev`/`apikey`) only | **Never.** Not on the label, not in a terminal, not in a log line. | Yes — PBKDF2 hash (`Device.ApiKeyHash`) |
| **Claim code** | Printed on the label / box (QR + text) | Yes — that's the point | Yes — PBKDF2 hash (`Device.ClaimCodeHash`) |
| **BLE PoP** | Toy's NVS (`aregdev`/`pop`) + printed on the label | Yes — that's the point | **Never, not even hashed** — see below |

**Why the PoP is never stored, not even hashed.** The backend is not a party
to BLE provisioning: the parent's phone presents the PoP straight to the
TOY, and the toy's own BLE stack (Espressif's `WiFiProv`/`SECURITY_1`) is
what checks it. There is no verification step on the backend that would ever
read a stored PoP back — persisting one would be a secret at rest with no
matching use. `DeviceRegistrationResponse.Pop` is returned exactly ONCE, at
registration, and this station is the only thing that ever sees it besides
the label it prints.

**What the label carries:** a QR code encoding `{deviceId, claim, pop}` (the
same JSON the toy's own printed QR encodes — a parent's app scans this to
claim the toy AND, on the Wi-Fi setup screen, to read the PoP) plus the same
three fields printed in large text, for anyone whose phone can't scan.

---

## 1. Before you start

- A backend reachable from the bench, with `Devices:ProvisioningSecret` set
  (production) — or `Devices:AllowOpenRegistration=true` for a dev/bench
  backend only. **Never ship a unit provisioned against an
  AllowOpenRegistration backend.**
- `AREG_PROVISIONING_SECRET` exported in the station's shell, matching that
  backend's `Devices:ProvisioningSecret`.
- Python deps: `pip install -r tools/factory/requirements.txt` (esptool,
  Espressif's own `esp-idf-nvs-partition-gen`, qrcode, reportlab, requests —
  see that file's comments for why each is there).
- A label printer (or just print the PDF), and a way to affix it to the box.
- **The toy connected over USB, on the port you're about to name.** The
  factory station writes ONLY the `nvs` partition — the firmware image
  itself must already be flashed (the normal release build; see
  `docs/ota-release-runbook.md` for how that image is cut). Provisioning a
  unit with no firmware on it yet will flash cleanly and then never heartbeat
  (nothing is running to send one).

## 2. Provision one toy

```bash
export AREG_PROVISIONING_SECRET=<the real secret, not the dev bypass>
python3 tools/factory/provision_toy.py \
    --backend-url http://<backend-host>:<port> \
    --mac <the toy's real MAC address> \
    --port /dev/ttyUSB0
```

What happens, in order:

1. **Register.** `POST /api/devices/register` mints `{deviceId, apiKey,
   claimCode, pop}`. A MAC that's already registered FAILS here (factory
   provisioning is for brand-new units only — re-registration mints neither
   a claim code nor a PoP, by design; see `CLAUDE.md` § Consumer platform).
2. **Build the NVS image.** A CSV (namespace `aregdev`, keys `devid` /
   `apikey` / `pop` — see `esp32/AregVoiceMvp/device_creds_rules.h`, the
   single source of truth those three strings are checked against on both
   sides) is turned into `nvs.bin` by Espressif's own `nvs_partition_gen`.
3. **Flash it.** `esptool write-flash` at the `nvs` partition's OWN offset,
   read live from `esp32/AregVoiceMvp/partitions.csv` — never hand-typed,
   because that table has already moved once (B.2, `huge_app` → `custom`).
4. **Verify.** Watches the toy's own serial log after the reset for its
   first `[heartbeat] status=200`. A non-200 status FAILS loudly — it means
   the identity just burned doesn't authenticate, which is a reportable
   defect, not a flake. **This step needs the toy's Wi-Fi already reachable**
   (BLE-provisioned already, or a bench `config.h` fallback on THIS image) —
   see the caveat in § 4.
5. **Render the label.** `out/<deviceId>/qr.png` + `out/<deviceId>/label.pdf`.

On success:

```
[done] tools/factory/out/<deviceId>
[done] out-dir also contains the device's plaintext key (nvs.bin) --
       treat like a filled-in config.h: never commit, delete after the batch.
```

**`tools/factory/out/<deviceId>/nvs.bin` holds the device's API key in
plaintext.** Same rule as a filled-in `config.h` (README, "Do not commit
`config.h`"): never commit it, never upload it anywhere that isn't this one
toy's flash. Delete the batch's `out/` directory once every label in it is
printed and every image is flashed.

Print `label.pdf`, affix it to the box, move to the next unit.

## 3. No serial port on the bench (register + flash later)

Drop `--port`. The script still registers, builds `nvs.bin`, and renders the
label; it prints the exact `esptool` command to run by hand once a port is
available:

```
python3 -m esptool --chip esp32s3 --port <PORT> write-flash 0x9000 tools/factory/out/<deviceId>/nvs.bin
```

(`0x9000` here is whatever this repo's `partitions.csv` says today — read it
from the script's own printed output, don't copy this example.)

## 4. Testing the label pipeline without hardware (dry run)

```bash
python3 tools/factory/provision_toy.py --dry-run sample_response.json --out-dir /tmp/label_test
```

`sample_response.json` is any JSON object shaped like a real
`POST /api/devices/register` response (`deviceId`, `claimCode`, `pop`,
`qrPayload` — `apiKey` is accepted but ignored). No network call, no
`esptool`, no serial port. Use this to check label layout changes, or to
demo the pipeline away from the bench.

## 5. Troubleshooting

| Symptom | Likely cause |
|---|---|
| `registration failed: HTTP 401` | Wrong/missing `AREG_PROVISIONING_SECRET`, or the backend isn't in `AllowOpenRegistration` and no secret is configured. |
| `registration failed: HTTP 409` (or "response is missing 'claimCode'") | This MAC is already registered — factory provisioning is for new units only. If you're re-flashing a returned/repaired unit, that's a deliberate re-provision (`X-Force-Rotate`), not this script's job — see `CLAUDE.md` § Consumer platform's #011 note, and re-derive its NVS image by hand. |
| `esptool write-flash failed` | Wrong `--port`, or the toy is mid-boot / needs a manual BOOT-button hold — same as any other esptool session. |
| `no heartbeat seen on <port> within 45s` | The toy has no Wi-Fi yet (BLE not provisioned, and no bench `config.h` fallback on this build), or the backend named by `--backend-url` isn't reachable from the toy's network — those are two different networks whenever the bench laptop and the toy aren't on the same LAN. | 
| `toy heartbeat came back non-200` | The burned identity doesn't authenticate — do not ship this unit. Re-run provisioning against a fresh MAC-registration rather than reusing the failed one. |

## 6. How to re-issue a toy's PoP

You can't — not without a fresh identity. The PoP is minted once, at
registration, and never stored anywhere the backend could hand it back out
(§ 0). A toy whose label is lost or unreadable has two options:

- **The claim code and QR still work** (they're hashed and stored — see the
  table in § 0), so pairing to a parent account is unaffected.
- **The BLE PoP is gone for good.** Re-provisioning the SAME unit means a
  fresh `X-Force-Rotate` registration (new MAC entry is not an option — MAC
  is fixed hardware) is not how this works either, since re-registration by
  design mints no new PoP (`CLAUDE.md` § Consumer platform, #011). The
  practical fix is the same one a lost device key already needs: treat it as
  a support case, not a one-command re-issue. If this turns out to be a
  common failure mode in the field, that's a product decision (a rotation
  endpoint) for the owner, not something to script around here.
