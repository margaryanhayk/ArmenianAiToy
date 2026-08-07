// -------------------------------------------------------------
// AregVoiceMvp / ota_apply.h — real OTA apply (download → inactive slot)
//
// The apply half of the OTA foundation. Called by ota_foundation's
// firmware_update handler. Pipeline:
//   fetch manifest → gates (HMAC signature, board, minVersion, strict
//   upgrade, size) → persist NVS state → stream download with SHA-256 →
//   Arduino Update into the INACTIVE OTA slot → verify sha256 →
//   Update.end() (native image validation + boot-partition switch) →
//   persist OTA_STATE_REBOOTING → ESP.restart().
//
// SUCCESS NEVER RETURNS (the device reboots). The post-reboot check-in,
// final ack, and mark-valid/rollback live in ota_foundation.cpp.
//
// NOT in this slice: Secure Boot/eFuse, SD sync, staged rollout,
// production TLS (Stage A runs over the HTTP LAN bench; the transport
// seam below is where Stage B swaps in WiFiClientSecure + pinned CA).
// -------------------------------------------------------------
#pragma once

#include <Arduino.h>
// Same include-order guard as ota_foundation.h: config.h overrides must be
// visible before the #ifndef defaults below in EVERY translation unit.
#include "config.h"

// Shared HMAC key for MANIFEST verification (must equal the backend's
// FirmwareUpdate:SigningKey). Empty (default) = verification SKIPPED with a
// loud log — acceptable only on the Stage-A bench; release builds must set
// it. This authenticates the manifest, not the image (image integrity =
// sha256 pinned inside the signed manifest).
#ifndef AREG_MANIFEST_HMAC_KEY
#define AREG_MANIFEST_HMAC_KEY ""
#endif

// How long the NEW image may try to check in (ack the pending command)
// before it self-invalidates and lets the bootloader roll back.
//
// This is measured from BOOT (millis()), not from the first attempt, so it
// must exceed the WORST-CASE time from power-on to the first successful
// backend call — not the typical one. On this device that path includes the
// Wi-Fi join, the whole of setup(), and whatever the loop does before the
// check-in gets a turn.
//
// 300000 (5 min) was demonstrably too tight: 1.1.0 rolled back in the field
// on 2026-08-07 with `rollback_no_checkin` on a toy whose radio, flash and
// bootloader all worked. Raised to 15 min. The cost of a longer deadline is
// only that a genuinely broken image takes longer to roll back — and it
// still rolls back; the cost of a short one is rolling back a HEALTHY image,
// which is what actually happened.
#ifndef AREG_OTA_CHECKIN_DEADLINE_MS
#define AREG_OTA_CHECKIN_DEADLINE_MS 900000UL  // 15 min
#endif

// Retry cadence for the post-reboot check-in ack.
#ifndef AREG_OTA_ACK_RETRY_MS
#define AREG_OTA_ACK_RETRY_MS 10000UL  // 10 s
#endif

// Hard ceiling on an accepted image: the 3 MB OTA slot from the 8 MB table.
#ifndef AREG_OTA_MAX_IMAGE_BYTES
#define AREG_OTA_MAX_IMAGE_BYTES 0x300000L
#endif

enum OtaApplyOutcome {
    // Manifest said no update — caller acks ok/manifest_checked.
    OTA_APPLY_NO_UPDATE = 0,
    // A validation gate refused (err_out says why). Deterministic + cheap;
    // no NVS terminal state — a re-delivered command re-refuses identically.
    OTA_APPLY_REFUSED = 1,
    // Post-gate failure (download / sha256 / image validation). NVS is left
    // in OTA_STATE_FAILED with applied_cmd stamped, so the same command can
    // NEVER re-trigger a download loop; caller acks failed/err_out.
    OTA_APPLY_FAILED = 2,
    // (Success does not return — the device reboots into the new slot.)
};

// Run the full apply pipeline for one firmware_update command.
// err_out receives the machine-readable reason on REFUSED/FAILED.
OtaApplyOutcome ota_apply_run(const char *command_id, char *err_out, size_t err_cap);
