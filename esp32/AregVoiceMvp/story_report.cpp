// -------------------------------------------------------------
// AregVoiceMvp / story_report.cpp — story-play reporting (store-and-forward)
// See story_report.h for the contract and the why-NVS rationale.
// -------------------------------------------------------------
#include "story_report.h"

#include <Preferences.h>
#include <esp_system.h>  // esp_random() — key uniqueness fallback when NVS fails
#include <string.h>

#include "config.h"
#include "voice_client.h"

// Same default + override seam as ota_foundation.cpp (config.h may not
// define it; config.h.example documents it).
#ifndef AREG_HEARTBEAT_INTERVAL_MS
#define AREG_HEARTBEAT_INTERVAL_MS 60000UL
#endif

namespace {

// Short namespace + keys (NVS keys are limited to 15 chars).
constexpr const char *kNamespace = "aregplays";
constexpr const char *kBootKey   = "boots";  // uint32 boot counter (key uniqueness)
constexpr const char *kQueueKey  = "q";      // whole-queue blob (version + count + events)

constexpr uint8_t  kPersistVersion = 1;
constexpr int      kMaxEvents      = 16;
// How soon after an event closes the tick tries to upload (prompt path).
constexpr uint32_t kPromptDelayMs  = 3000;

// One queued play event. POD — persisted by memcpy into the NVS blob, read
// back by the SAME firmware (kPersistVersion guards a future layout change).
struct PlayEvent {
    char     key[16];       // "b<boot>-<n>" — idempotency key, unique per device
    char     story_id[49];  // content-sync ids are <=48 chars, allowlisted charset
    char     source[8];     // "sd" | "pack" | "stream"
    uint8_t  finished;      // 1 = natural end
    uint8_t  this_boot;     // RAM-only meaning; forced 0 on load — a reloaded
                            //   event has no usable relative timestamp
    uint32_t at_ms;         // millis() when the play started (valid iff this_boot)
};

PlayEvent s_events[kMaxEvents];
int       s_count = 0;
int       s_open_idx = -1;     // index of the in-progress session's event, -1 = none
bool      s_loaded = false;
uint32_t  s_boot_seq = 0;
uint32_t  s_next_seq = 0;      // per-boot event counter
uint32_t  s_last_attempt_ms = 0;
bool      s_prompt_upload = false;
uint32_t  s_prompt_since_ms = 0;

void persist_queue() {
    Preferences prefs;
    if (!prefs.begin(kNamespace, /*readOnly=*/false)) {
        Serial.println("[story-report] WARNING: NVS open failed; queue not persisted");
        return;
    }
    uint8_t blob[2 + sizeof(s_events)];
    blob[0] = kPersistVersion;
    blob[1] = (uint8_t)s_count;
    memcpy(blob + 2, s_events, sizeof(PlayEvent) * (size_t)s_count);
    prefs.putBytes(kQueueKey, blob, 2 + sizeof(PlayEvent) * (size_t)s_count);
    prefs.end();
}

void ensure_loaded() {
    if (s_loaded) {
        return;
    }
    s_loaded = true;

    Preferences prefs;
    if (!prefs.begin(kNamespace, /*readOnly=*/false)) {
        Serial.println("[story-report] WARNING: NVS open failed; reporting is RAM-only this boot");
        s_boot_seq = (uint32_t)(esp_random());  // still unique-ish keys
        return;
    }
    // Boot counter: incremented once per boot so event keys never collide
    // across reboots even though the per-boot sequence restarts at 1.
    s_boot_seq = prefs.getUInt(kBootKey, 0) + 1;
    prefs.putUInt(kBootKey, s_boot_seq);

    // Reload the persisted queue (events from previous boots that never got
    // uploaded — Wi-Fi was down, or the toy was powered off first).
    const size_t len = prefs.getBytesLength(kQueueKey);
    if (len >= 2 && len <= sizeof(uint8_t) * 2 + sizeof(s_events)) {
        uint8_t blob[2 + sizeof(s_events)];
        prefs.getBytes(kQueueKey, blob, len);
        const uint8_t version = blob[0];
        int count = blob[1];
        const size_t expected = 2 + sizeof(PlayEvent) * (size_t)count;
        if (version == kPersistVersion && count >= 0 && count <= kMaxEvents
            && expected == len) {
            memcpy(s_events, blob + 2, sizeof(PlayEvent) * (size_t)count);
            s_count = count;
            for (int i = 0; i < s_count; i++) {
                // Defensive termination + RAM-only fields reset: a reloaded
                // event has no valid millis() anchor in THIS boot.
                s_events[i].key[sizeof(s_events[i].key) - 1] = '\0';
                s_events[i].story_id[sizeof(s_events[i].story_id) - 1] = '\0';
                s_events[i].source[sizeof(s_events[i].source) - 1] = '\0';
                s_events[i].this_boot = 0;
                s_events[i].at_ms = 0;
            }
        } else {
            Serial.printf("[story-report] persisted queue invalid (v=%u len=%u) — dropped\n",
                          (unsigned)version, (unsigned)len);
        }
    }
    prefs.end();
    if (s_count > 0) {
        Serial.printf("[story-report] %d queued event(s) reloaded from NVS\n", s_count);
    }
}

int closed_event_count() {
    int closed = 0;
    for (int i = 0; i < s_count; i++) {
        if (i != s_open_idx) closed++;
    }
    return closed;
}

// Upload every CLOSED event in one POST; delete them from the queue only on
// a 2xx. The open event (in-progress session) is held back — uploading it
// mid-pause would freeze it as unfinished.
void upload_closed_events() {
    static char body[2304];
    bool included[kMaxEvents] = {false};
    size_t n = 0;
    n += snprintf(body + n, sizeof(body) - n, "{\"events\":[");
    bool first = true;
    for (int i = 0; i < s_count; i++) {
        if (i == s_open_idx) {
            continue;
        }
        // Worst-case one event is ~110 chars; stop before truncation and let
        // the next tick carry the remainder (the queue is only 16 deep, so
        // this is belt-and-braces, not an expected path).
        if (sizeof(body) - n < 160) {
            break;
        }
        n += snprintf(body + n, sizeof(body) - n,
                      "%s{\"key\":\"%s\",\"storyId\":\"%s\",\"finished\":%s,\"source\":\"%s\"",
                      first ? "" : ",",
                      s_events[i].key, s_events[i].story_id,
                      s_events[i].finished ? "true" : "false", s_events[i].source);
        if (s_events[i].this_boot) {
            n += snprintf(body + n, sizeof(body) - n, ",\"secondsAgo\":%u",
                          (unsigned)((millis() - s_events[i].at_ms) / 1000u));
        }
        n += snprintf(body + n, sizeof(body) - n, "}");
        included[i] = true;
        first = false;
    }
    n += snprintf(body + n, sizeof(body) - n, "]}");
    if (first) {
        return;  // nothing closed to send
    }

    const int status = voice_post_story_plays(body);
    if (status < 200 || status >= 300) {
        // Best-effort: keep everything queued; the next tick retries. The
        // idempotency keys make the eventual re-upload safe.
        Serial.printf("[story-report] upload failed (status=%d) — will retry\n", status);
        return;
    }

    // 2xx — drop the uploaded events, keep the open one (and any overflow).
    int w = 0;
    int new_open = -1;
    for (int i = 0; i < s_count; i++) {
        if (included[i]) {
            continue;
        }
        if (i == s_open_idx) {
            new_open = w;
        }
        if (w != i) {
            s_events[w] = s_events[i];
        }
        w++;
    }
    const int uploaded = s_count - w;
    s_count = w;
    s_open_idx = new_open;
    persist_queue();
    Serial.printf("[story-report] uploaded %d event(s), %d still queued\n",
                  uploaded, s_count);
}

}  // namespace

void story_report_on_started(const char *story_id, const char *source) {
    if (story_id == nullptr || story_id[0] == '\0') {
        return;
    }
    ensure_loaded();

    if (s_count == kMaxEvents) {
        // Full — drop the oldest event; recent history beats ancient history.
        memmove(&s_events[0], &s_events[1], sizeof(PlayEvent) * (kMaxEvents - 1));
        s_count--;
        if (s_open_idx > 0) {
            s_open_idx--;
        } else if (s_open_idx == 0) {
            s_open_idx = -1;  // the open event itself was the oldest (shouldn't happen)
        }
    }

    PlayEvent &ev = s_events[s_count];
    memset(&ev, 0, sizeof(ev));
    s_next_seq++;
    snprintf(ev.key, sizeof(ev.key), "b%u-%u",
             (unsigned)s_boot_seq, (unsigned)s_next_seq);
    snprintf(ev.story_id, sizeof(ev.story_id), "%s", story_id);
    snprintf(ev.source, sizeof(ev.source), "%s",
             source != nullptr ? source : "sd");
    ev.finished = 0;
    ev.this_boot = 1;
    ev.at_ms = millis();

    // Starting a NEW story implicitly closes any still-open previous event
    // (a paused story the child abandoned) — it uploads as finished=false,
    // which is the truth.
    s_open_idx = s_count;
    s_count++;
    persist_queue();
    // A previously-open event may have just become closed — make the next
    // tick upload promptly.
    s_prompt_upload = true;
    s_prompt_since_ms = millis();
    Serial.printf("[story-report] started %s (%s) key=%s queued=%d\n",
                  ev.story_id, ev.source, ev.key, s_count);
}

void story_report_on_finished() {
    if (s_open_idx < 0 || s_open_idx >= s_count) {
        return;
    }
    s_events[s_open_idx].finished = 1;
    s_open_idx = -1;
    persist_queue();
    s_prompt_upload = true;
    s_prompt_since_ms = millis();
    Serial.println("[story-report] finished — event closed");
}

void story_report_tick() {
    ensure_loaded();
    if (closed_event_count() == 0) {
        return;
    }
    if (!voice_wifi_is_connected()) {
        return;
    }
    const uint32_t now = millis();
    bool due = false;
    if (s_prompt_upload && now - s_prompt_since_ms >= kPromptDelayMs) {
        due = true;
    }
    if (s_last_attempt_ms == 0
        || now - s_last_attempt_ms >= AREG_HEARTBEAT_INTERVAL_MS) {
        due = true;
    }
    if (!due) {
        return;
    }
    s_last_attempt_ms = now;
    s_prompt_upload = false;
    upload_closed_events();
}
