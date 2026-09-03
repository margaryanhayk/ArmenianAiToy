# Release 1.3.4 — 2026-09-02

| Field | Value |
|---|---|
| Version | 1.3.4 |
| Build tag | 2026-09-02-release |
| OTA image | AregVoiceMvp.ino.bin |
| Size | 1,638,720 B (52.1% of the 3 MB OTA slot) |
| SHA-256 | b212721cfb11f30026d9a9c5e2318e742c3ee9a123a4f638dadedc6b5b5bb6da |
| Gate | PASS (check_release_image.py, --expect 1.3.4 --forbid 1.3.3) |

Everything since 1.3.2, one image: hold-to-menu (1.3.3), button on
GPIO18, damaged-clip self-heal, volume gain clamp 1.0, index parse on
PSRAM, menu-as-front-door + story browser + rotating reflection
question, BLE provisioning ON by default, SD bus at 4 MHz
(AREG_SD_SPI_HZ) + the read self-test, scheduled sync retry.

Gate note: the image legitimately contains one GUID-shaped string,
258EAFA5-E914-47DA-95CA-C5AB0DC85B11 — the Espressif WiFiProv BLE
provisioning service UUID (a library constant identical in every
BLE-provisioning ESP32 firmware). Allowlisted by exact value in
check_release_image.py; the owner's device id was confirmed ABSENT.

The bin is NOT committed here (only areg-current.bin under the backend
is tracked); this NOTES.md is the versioned record, per 1.3.3's pattern.
