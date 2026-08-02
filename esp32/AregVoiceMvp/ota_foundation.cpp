// -------------------------------------------------------------
// AregVoiceMvp / ota_foundation.cpp — OTA foundation SKELETON (Proof 2)
//
// Implements the phone-home loop against the backend contract
// (feat/ota-backend-contract): poll commands, handle firmware_update by
// CHECKING the manifest (no download, no flash), ack every command.
// See ota_foundation.h for the full scope statement.
// -------------------------------------------------------------
#include "ota_foundation.h"

#include <HTTPClient.h>
#include <ArduinoJson.h>
#include <esp_ota_ops.h>

#include "config.h"
#include "net_transport.h"
#include "voice_client.h"
#include "ota_apply.h"   // real apply pipeline (reboots on success)
#include "ota_state.h"   // persisted cross-reboot OTA state (NVS)

// Fallbacks mirrored from config.h.example so this module compiles even on
// a config.h that predates them.
#ifndef AREG_HEARTBEAT_INTERVAL_MS
#define AREG_HEARTBEAT_INTERVAL_MS 60000UL
#endif
#ifndef AREG_HTTP_CONNECT_MS
#define AREG_HTTP_CONNECT_MS 5000
#endif
#ifndef AREG_HTTP_READ_MS
#define AREG_HTTP_READ_MS 30000
#endif

// -------------------------------------------------------------
// Module state
// -------------------------------------------------------------

static bool     s_boot_poll_done = false;
static uint32_t s_last_poll_ms   = 0;

// Persisted OTA state, loaded once at boot (save-through on every change).
static OtaPersist s_ota;
static bool       s_ota_loaded = false;
// Post-reboot check-in throttle (see ota_checkin_tick).
static uint32_t   s_last_checkin_ms = 0;

// Dedup ring of recently HANDLED command ids (at-least-once transport: a
// Sent command is re-delivered until acked, so a lost ack re-delivers it).
// A ring hit skips the handler — duplicate delivery can never re-run
// potentially dangerous logic (the guard future real-OTA relies on) — but
// still RE-ACKS, so the backend queue converges. RAM-only: after a reboot
// the ring is empty, which is safe because every handler here is
// idempotent (manifest check), and the backend acks terminal commands as
// safe no-ops.
static const int kDedupSlots = 8;
static char s_handled_ids[kDedupSlots][40] = {};
static int  s_handled_next = 0;

static bool dedup_contains(const char *id) {
    for (int i = 0; i < kDedupSlots; i++) {
        if (s_handled_ids[i][0] != '\0' && strcmp(s_handled_ids[i], id) == 0) {
            return true;
        }
    }
    return false;
}

static void dedup_add(const char *id) {
    snprintf(s_handled_ids[s_handled_next], sizeof(s_handled_ids[0]), "%s", id);
    s_handled_next = (s_handled_next + 1) % kDedupSlots;
}

// Derive an API URL from AREG_BACKEND_URL (same trick as the heartbeat /
// story-audio-token helpers, so there is no new config constant).
static String api_url(const char *path) {
    String url = AREG_BACKEND_URL;
    url.replace("/api/chat/audio", path);
    return url;
}

const char *ota_running_partition_label() {
    const esp_partition_t *running = esp_ota_get_running_partition();
    return (running != nullptr) ? running->label : "unknown";
}

// -------------------------------------------------------------
// Ack
// -------------------------------------------------------------

// POST /api/devices/commands/{id}/ack. diagnostics_json must be a valid
// JSON object string (or nullptr for none). Returns true on HTTP 2xx.
static bool ack_command(const char *command_id, const char *result,
                        const char *error, const char *diagnostics_json) {
    String url = api_url("/api/devices/commands/");
    url += command_id;
    url += "/ack";

    HTTPClient http;
    if (!areg_http_begin(http, url)) {
        return false;
    }
    voice_add_device_auth_headers(http);
    http.addHeader("Content-Type", "application/json");
    http.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    http.setTimeout(AREG_HTTP_READ_MS);

    // ArduinoJson for serialization so error strings are safely escaped.
    JsonDocument doc;
    doc["result"] = result;
    doc["firmwareVersion"] = AREG_FW_VERSION;
    if (error != nullptr) {
        doc["error"] = error;
    }
    if (diagnostics_json != nullptr) {
        JsonDocument diag;
        if (deserializeJson(diag, diagnostics_json) == DeserializationError::Ok) {
            doc["diagnostics"] = diag;
        }
    }
    String body;
    serializeJson(doc, body);

    const int status = http.POST(body);
    http.end();
    Serial.printf("[ota] ack %s result=%s status=%d\n", command_id, result, status);
    Serial.flush();
    return status >= 200 && status < 300;
}

// -------------------------------------------------------------
// firmware_update handler — REAL APPLY (via ota_apply.cpp)
// -------------------------------------------------------------

// Persistent idempotency: the same command id must never re-trigger a
// download/reboot loop across reboots or rollbacks. Terminal outcomes are
// stamped into NVS (applied_cmd) — a re-delivered id is only RE-ACKED.
static void handle_firmware_update(const char *command_id) {
    if (s_ota.applied_cmd[0] != '\0' && strcmp(command_id, s_ota.applied_cmd) == 0) {
        // Already reached a terminal outcome for THIS command → re-ack it.
        if (s_ota.state == OTA_STATE_CONFIRMED) {
            char diag[128];
            snprintf(diag, sizeof(diag),
                     "{\"status\":\"ota_applied\",\"version\":\"%.15s\","
                     "\"partition\":\"%s\",\"rerun\":true}",
                     s_ota.pending_ver, ota_running_partition_label());
            ack_command(command_id, "ok", nullptr, diag);
        } else {
            ack_command(command_id, "failed",
                        s_ota.last_error[0] ? s_ota.last_error : "previously_failed",
                        "{\"status\":\"previously_terminal\"}");
        }
        return;
    }
    if (s_ota.state == OTA_STATE_REBOOTING || s_ota.state == OTA_STATE_DOWNLOADING) {
        // A different command while an attempt is in flight — refuse loudly.
        ack_command(command_id, "failed", "ota_busy", nullptr);
        return;
    }

    char err[32] = {0};
    const OtaApplyOutcome outcome = ota_apply_run(command_id, err, sizeof(err));
    // (Success never returns — the device reboots into pending-verify.)
    switch (outcome) {
        case OTA_APPLY_NO_UPDATE:
            ack_command(command_id, "ok", nullptr,
                        "{\"status\":\"manifest_checked\",\"updateAvailable\":false}");
            break;
        case OTA_APPLY_REFUSED:
            ack_command(command_id, "failed", err, "{\"status\":\"refused\"}");
            break;
        case OTA_APPLY_FAILED:
        default:
            // ota_apply persisted OTA_STATE_FAILED + applied_cmd; reload so the
            // in-RAM view matches and re-deliveries re-ack instead of re-run.
            ota_state_load(s_ota);
            ack_command(command_id, "failed", err, "{\"status\":\"apply_failed\"}");
            break;
    }
}

// -------------------------------------------------------------
// Boot-state normalization + post-reboot check-in
// -------------------------------------------------------------

// Runs once, on the first tick after boot (works without Wi-Fi).
static void ota_boot_init() {
    ota_state_load(s_ota);

    // Crash mid-download: the boot slot never switched, so the attempt is
    // simply FAILED — stamp it terminal so the re-delivered command re-acks
    // instead of re-downloading forever on a crashy link.
    if (s_ota.state == OTA_STATE_DOWNLOADING) {
        s_ota.state = OTA_STATE_FAILED;
        snprintf(s_ota.last_error, sizeof(s_ota.last_error), "interrupted");
        snprintf(s_ota.applied_cmd, sizeof(s_ota.applied_cmd), "%s", s_ota.cmd_id);
        ota_state_save(s_ota);
    }
    if (s_ota.state == OTA_STATE_REBOOTING) {
        s_ota.boot_attempts++;
        ota_state_save(s_ota);
    }

    // Bench-visible proof of the native rollback capability: on the first
    // boot after an OTA this must print pending_verify.
    const esp_partition_t *running = esp_ota_get_running_partition();
    esp_ota_img_states_t img_state = ESP_OTA_IMG_UNDEFINED;
    esp_ota_get_state_partition(running, &img_state);
    const char *img_state_name =
        (img_state == ESP_OTA_IMG_PENDING_VERIFY) ? "pending_verify"
        : (img_state == ESP_OTA_IMG_VALID)        ? "valid"
        : (img_state == ESP_OTA_IMG_NEW)          ? "new"
        : (img_state == ESP_OTA_IMG_ABORTED)      ? "aborted"
                                                  : "undefined";
    Serial.printf("[ota] running fw=%s partition=%s img_state=%s ota_state=%s "
                  "(pending_ver=%s cmd=%s boots=%u)\n",
                  AREG_FW_VERSION, ota_running_partition_label(), img_state_name,
                  ota_state_name(s_ota.state),
                  s_ota.pending_ver[0] ? s_ota.pending_ver : "-",
                  s_ota.cmd_id[0] ? s_ota.cmd_id : "-",
                  (unsigned)s_ota.boot_attempts);
    Serial.flush();
}

// While OTA_STATE_REBOOTING, this owns the tick (command polling is paused):
//  * NEW image (pending_ver == ours): ack the original command from NVS; ONLY
//    a 2xx ack marks the app valid (backend check-in IS the health gate). If
//    the deadline passes without a successful ack, self-invalidate so the
//    bootloader rolls back.
//  * OLD image (version mismatch → the bootloader rolled us back): record the
//    rollback terminally and ack failed.
static void ota_checkin_tick() {
    const bool is_new_image =
        (strcmp(s_ota.pending_ver, AREG_FW_VERSION) == 0);

    if (!is_new_image) {
        // Rollback happened — we are the OLD image again.
        Serial.printf("[ota] ROLLBACK detected (attempted %s, running %s)\n",
                      s_ota.pending_ver, AREG_FW_VERSION);
        s_ota.state = OTA_STATE_ROLLED_BACK;
        snprintf(s_ota.last_error, sizeof(s_ota.last_error), "rollback_no_checkin");
        snprintf(s_ota.applied_cmd, sizeof(s_ota.applied_cmd), "%s", s_ota.cmd_id);
        ota_state_save(s_ota);
        char diag[128];
        snprintf(diag, sizeof(diag),
                 "{\"status\":\"rolled_back\",\"attemptedVersion\":\"%.15s\"}",
                 s_ota.pending_ver);
        // Best-effort: if this ack is lost, the re-delivered command re-acks
        // via the applied_cmd guard in handle_firmware_update.
        ack_command(s_ota.cmd_id, "failed", "rollback_no_checkin", diag);
        return;
    }

    // NEW image: throttle check-in attempts.
    const uint32_t now = millis();
    if (s_last_checkin_ms != 0 && (now - s_last_checkin_ms) < AREG_OTA_ACK_RETRY_MS) {
        // Between attempts — enforce the rollback deadline meanwhile.
        if (now > (uint32_t)AREG_OTA_CHECKIN_DEADLINE_MS) {
            Serial.println("[ota] check-in DEADLINE passed — self-invalidating "
                           "so the bootloader rolls back");
            Serial.flush();
            if (esp_ota_mark_app_invalid_rollback_and_reboot() != ESP_OK) {
                ESP.restart();  // a reset while pending-verify also rolls back
            }
        }
        return;
    }
    s_last_checkin_ms = now;

    // Refresh presence + firmware report first (best-effort), then the ack
    // that decides validity.
    voice_send_heartbeat();
    char diag[128];
    snprintf(diag, sizeof(diag),
             "{\"status\":\"ota_applied\",\"version\":\"%.15s\",\"partition\":\"%s\"}",
             AREG_FW_VERSION, ota_running_partition_label());
    if (ack_command(s_ota.cmd_id, "ok", nullptr, diag)) {
        if (esp_ota_mark_app_valid_cancel_rollback() == ESP_OK) {
            Serial.println("[ota] image marked VALID (confirmed)");
        } else {
            // Already valid (e.g. re-run after a crash post-confirm) — fine.
            Serial.println("[ota] mark-valid returned non-OK (already valid?)");
        }
        s_ota.state = OTA_STATE_CONFIRMED;
        snprintf(s_ota.applied_cmd, sizeof(s_ota.applied_cmd), "%s", s_ota.cmd_id);
        ota_state_save(s_ota);
    } else {
        Serial.printf("[ota] check-in ack failed (deadline in %lu s)\n",
                      (unsigned long)((AREG_OTA_CHECKIN_DEADLINE_MS > now)
                                          ? (AREG_OTA_CHECKIN_DEADLINE_MS - now) / 1000
                                          : 0));
        if (now > (uint32_t)AREG_OTA_CHECKIN_DEADLINE_MS) {
            Serial.println("[ota] check-in DEADLINE passed — self-invalidating "
                           "so the bootloader rolls back");
            Serial.flush();
            if (esp_ota_mark_app_invalid_rollback_and_reboot() != ESP_OK) {
                ESP.restart();
            }
        }
    }
}

// -------------------------------------------------------------
// Command poll
// -------------------------------------------------------------

static void poll_commands() {
    HTTPClient http;
    if (!areg_http_begin(http, api_url("/api/devices/commands"))) {
        return;
    }
    voice_add_device_auth_headers(http);
    http.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    http.setTimeout(AREG_HTTP_READ_MS);

    const int status = http.GET();
    if (status != 200) {
        http.end();
        Serial.printf("[ota] command poll status=%d\n", status);
        Serial.flush();
        return;  // best-effort; the next interval retries
    }

    JsonDocument doc;
    const DeserializationError err = deserializeJson(doc, http.getString());
    http.end();
    if (err != DeserializationError::Ok) {
        Serial.printf("[ota] command poll parse error: %s\n", err.c_str());
        Serial.flush();
        return;
    }

    JsonArray commands = doc["commands"].as<JsonArray>();
    if (commands.isNull() || commands.size() == 0) {
        return;  // steady state — print nothing
    }
    Serial.printf("[ota] %u command(s) pending\n", (unsigned)commands.size());
    Serial.flush();

    for (JsonObject cmd : commands) {
        const char *id   = cmd["id"]   | "";
        const char *type = cmd["type"] | "";
        // expiresAt is enforced SERVER-side (an expired command is never
        // delivered — pinned by backend tests); the device has no synced
        // wall clock, so it only logs the field.
        const char *expiresAt = cmd["expiresAt"] | "";
        if (id[0] == '\0') {
            continue;
        }
        Serial.printf("[ota] command id=%s type=%s expiresAt=%s\n",
                      id, type, expiresAt[0] ? expiresAt : "(none)");
        Serial.flush();

        if (strcmp(type, "firmware_update") == 0) {
            // firmware_update idempotency lives in NVS (survives the reboot the
            // apply performs and any rollback) — the RAM ring below would both
            // be wiped by the reboot AND wrongly re-ack "ok" for a failed
            // apply, so this type deliberately bypasses it.
            handle_firmware_update(id);
        } else if (dedup_contains(id)) {
            // At-least-once dedup for NON-firmware commands: a re-delivered
            // command we already handled (lost/failed ack) is NOT re-run — it
            // is only re-acked so the backend queue converges. Duplicate acks
            // are server-side no-ops.
            Serial.printf("[ota] duplicate delivery of %s — re-ack only\n", id);
            Serial.flush();
            ack_command(id, "ok", nullptr, "{\"status\":\"deduped\"}");
        } else {
            dedup_add(id);  // BEFORE handling: the dangerous-logic re-run guard
            // Unknown type: ack failed so the queue clears loudly instead of
            // re-delivering forever. (The backend also rejects unknown types
            // at enqueue, so this is a forward-compat safety net.)
            Serial.printf("[ota] unsupported command type '%s'\n", type);
            Serial.flush();
            ack_command(id, "failed", "unsupported_type", nullptr);
        }
    }
}

// -------------------------------------------------------------
// Tick
// -------------------------------------------------------------

void ota_foundation_tick() {
    // Boot-state load + normalization first — needs no network, and the
    // rollback-detection log must appear even if Wi-Fi is still joining.
    if (!s_ota_loaded) {
        s_ota_loaded = true;
        ota_boot_init();
    }
    if (!voice_wifi_is_connected()) {
        return;  // the Wi-Fi reconnect tick owns recovery
    }

    // An OTA outcome is pending (we just rebooted into a new image, or the
    // bootloader rolled us back): the check-in owns the tick until resolved.
    // Command polling stays paused so nothing else can start mid-verdict.
    if (s_ota.state == OTA_STATE_REBOOTING) {
        ota_checkin_tick();
        return;
    }

    const uint32_t now = millis();
    if (!s_boot_poll_done) {
        // First tick with the link up — boot poll, so a command enqueued
        // while the toy was off is picked up right away.
        s_boot_poll_done = true;
        s_last_poll_ms = now;
        Serial.printf("[ota] boot poll (fw=%s build=%s board=%s partition=%s)\n",
                      AREG_FW_VERSION, AREG_FW_BUILD, AREG_BOARD_MODEL,
                      ota_running_partition_label());
        Serial.flush();
        poll_commands();
        return;
    }
    if (now - s_last_poll_ms >= AREG_HEARTBEAT_INTERVAL_MS) {  // rollover-safe
        s_last_poll_ms = now;
        poll_commands();
    }
}
