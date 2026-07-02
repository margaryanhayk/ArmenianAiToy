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
#include "voice_client.h"

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
    if (!http.begin(url)) {
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
// firmware_update handler — MANIFEST CHECK ONLY (no download/apply)
// -------------------------------------------------------------

static void handle_firmware_update(const char *command_id) {
    HTTPClient http;
    if (!http.begin(api_url("/api/devices/firmware-manifest"))) {
        ack_command(command_id, "failed", "manifest_begin_failed", nullptr);
        return;
    }
    voice_add_device_auth_headers(http);
    http.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    http.setTimeout(AREG_HTTP_READ_MS);

    const int status = http.GET();
    if (status != 200) {
        http.end();
        Serial.printf("[ota] manifest fetch failed status=%d\n", status);
        Serial.flush();
        ack_command(command_id, "failed", "manifest_fetch_failed", nullptr);
        return;
    }

    JsonDocument doc;
    const DeserializationError err = deserializeJson(doc, http.getString());
    http.end();
    if (err != DeserializationError::Ok) {
        ack_command(command_id, "failed", "manifest_parse_failed", nullptr);
        return;
    }

    const bool available = doc["updateAvailable"] | false;
    if (!available) {
        Serial.printf("[ota] manifest: no update (running %s)\n", AREG_FW_VERSION);
        Serial.flush();
        ack_command(command_id, "ok", nullptr,
                    "{\"status\":\"manifest_checked\",\"updateAvailable\":false}");
        return;
    }

    // Update offered — log EVERYTHING the apply step would need, then stop.
    // SKELETON: no download, no sha256 run, no flash write, no reboot.
    const char *version   = doc["version"]    | "?";
    const char *board     = doc["boardModel"] | "";
    const char *minVer    = doc["minVersion"] | "";
    const char *url       = doc["url"]        | "";
    const long  sizeBytes = doc["sizeBytes"]  | 0L;
    const char *sha256    = doc["sha256"]     | "";
    const char *signature = doc["signature"]  | "";
    const char *expiresAt = doc["expiresAt"]  | "";
    Serial.printf("[ota] UPDATE OFFERED %s -> %s (board=%s min=%s)\n",
                  AREG_FW_VERSION, version, board, minVer);
    Serial.printf("[ota]   url=%s size=%ld\n", url, sizeBytes);
    Serial.printf("[ota]   sha256=%s\n", sha256);
    Serial.printf("[ota]   signature=%s expiresAt=%s\n",
                  signature[0] ? signature : "(placeholder)", expiresAt);
    Serial.printf("[ota]   WOULD download to inactive slot + verify sha256 + "
                  "set boot + reboot — SKELETON: not applying.\n");
    Serial.flush();

    char diag[160];
    snprintf(diag, sizeof(diag),
             "{\"status\":\"manifest_checked\",\"updateAvailable\":true,"
             "\"offeredVersion\":\"%.32s\"}", version);
    ack_command(command_id, "ok", nullptr, diag);
}

// -------------------------------------------------------------
// Command poll
// -------------------------------------------------------------

static void poll_commands() {
    HTTPClient http;
    if (!http.begin(api_url("/api/devices/commands"))) {
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

        // At-least-once dedup: a re-delivered command we already handled
        // (lost/failed ack) is NOT re-run — it is only re-acked so the
        // backend queue converges. Duplicate acks are server-side no-ops.
        if (dedup_contains(id)) {
            Serial.printf("[ota] duplicate delivery of %s — re-ack only\n", id);
            Serial.flush();
            ack_command(id, "ok", nullptr, "{\"status\":\"deduped\"}");
            continue;
        }
        dedup_add(id);  // BEFORE handling: the dangerous-logic re-run guard

        if (strcmp(type, "firmware_update") == 0) {
            handle_firmware_update(id);
        } else {
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
    if (!voice_wifi_is_connected()) {
        return;  // the Wi-Fi reconnect tick owns recovery
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
