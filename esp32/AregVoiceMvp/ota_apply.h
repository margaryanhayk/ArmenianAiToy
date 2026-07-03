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
#ifndef AREG_OTA_CHECKIN_DEADLINE_MS
#define AREG_OTA_CHECKIN_DEADLINE_MS 300000UL  // 5 min
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
