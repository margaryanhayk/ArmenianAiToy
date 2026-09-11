// -------------------------------------------------------------
// AregVoiceMvp / game_report.cpp — offline game-play reporting
// (store-and-forward). See game_report.h for the contract.
// -------------------------------------------------------------
#include "game_report.h"

#include <Preferences.h>
#include <esp_system.h>  // esp_random() — key uniqueness fallback when NVS fails
#include <string.h>

#include "config.h"
#include "voice_client.h"

// Same default + override seam as story_report.cpp.
#ifndef AREG_HEARTBEAT_INTERVAL_MS
#define AREG_HEARTBEAT_INTERVAL_MS 60000UL
#endif

namespace {

// Own namespace + keys (NVS keys are limited to 15 chars), distinct from
// story_report's "aregplays" and offline_games.cpp's "areggame" cursor.
constexpr const char *kNamespace = "areggplays";
constexpr const char *kBootKey   = "boots";  // uint32 boot counter (key uniqueness)
constexpr const char *kQueueKey  = "q";      // whole-queue blob (version + count + events)

constexpr uint8_t  kPersistVersion = 1;
// Smaller than story_report's 16: a child plays far fewer game rounds than
// stories in a session, and this only needs to hold what queued up while
// Wi-Fi was down. Keeps the .bss cost of this module proportionate — see
// the upload buffer below, sized to this same bound.
constexpr int      kMaxEvents      = 8;
constexpr uint32_t kPromptDelayMs  = 3000;

// One queued play event. POD — persisted by memcpy into the NVS blob, read
// back by the SAME firmware (kPersistVersion guards a future layout
// change), same idiom as story_report's PlayEvent.
struct GamePlayEvent {
    char     key[16];       // "b<boot>-<n>" — idempotency key, unique per device
    char     game_key[16];  // "mind-reader" / "who-first" / "button-simon"
    char     outcome[8];    // "" | "won" | "lost" | "stopped"
    int16_t  rounds;        // -1 = not reported
    int16_t  score;         // -1 = not reported
    uint8_t  this_boot;     // RAM-only meaning; forced 0 on load
    uint32_t at_ms;         // millis() when the session closed (valid iff this_boot)
};

GamePlayEvent s_events[kMaxEvents];
int      s_count = 0;
bool     s_loaded = false;
uint32_t s_boot_seq = 0;
uint32_t s_next_seq = 0;
uint32_t s_last_attempt_ms = 0;
bool     s_prompt_upload = false;
uint32_t s_prompt_since_ms = 0;

void persist_queue() {
    Preferences prefs;
    if (!prefs.begin(kNamespace, /*readOnly=*/false)) {
        Serial.println("[game-report] WARNING: NVS open failed; queue not persisted");
        return;
    }
    uint8_t blob[2 + sizeof(s_events)];
    blob[0] = kPersistVersion;
    blob[1] = (uint8_t)s_count;
    memcpy(blob + 2, s_events, sizeof(GamePlayEvent) * (size_t)s_count);
    prefs.putBytes(kQueueKey, blob, 2 + sizeof(GamePlayEvent) * (size_t)s_count);
    prefs.end();
}

void ensure_loaded() {
    if (s_loaded) {
        return;
    }
    s_loaded = true;

    Preferences prefs;
    if (!prefs.begin(kNamespace, /*readOnly=*/false)) {
        Serial.println("[game-report] WARNING: NVS open failed; reporting is RAM-only this boot");
        s_boot_seq = (uint32_t)(esp_random());
        return;
    }
    s_boot_seq = prefs.getUInt(kBootKey, 0) + 1;
    prefs.putUInt(kBootKey, s_boot_seq);

    const size_t len = prefs.getBytesLength(kQueueKey);
    if (len >= 2 && len <= sizeof(uint8_t) * 2 + sizeof(s_events)) {
        uint8_t blob[2 + sizeof(s_events)];
        prefs.getBytes(kQueueKey, blob, len);
        const uint8_t version = blob[0];
        int count = blob[1];
        const size_t expected = 2 + sizeof(GamePlayEvent) * (size_t)count;
        if (version == kPersistVersion && count >= 0 && count <= kMaxEvents
            && expected == len) {
            memcpy(s_events, blob + 2, sizeof(GamePlayEvent) * (size_t)count);
            s_count = count;
            for (int i = 0; i < s_count; i++) {
                s_events[i].key[sizeof(s_events[i].key) - 1] = '\0';
                s_events[i].game_key[sizeof(s_events[i].game_key) - 1] = '\0';
                s_events[i].outcome[sizeof(s_events[i].outcome) - 1] = '\0';
                s_events[i].this_boot = 0;
                s_events[i].at_ms = 0;
            }
        } else {
            Serial.printf("[game-report] persisted queue invalid (v=%u len=%u) — dropped\n",
                          (unsigned)version, (unsigned)len);
        }
    }
    prefs.end();
    if (s_count > 0) {
        Serial.printf("[game-report] %d queued event(s) reloaded from NVS\n", s_count);
    }
}

// Upload every queued event in one POST; delete them from the queue only
// on a 2xx. Every event here is already closed (see game_report.h), so —
// unlike story_report — nothing is ever held back mid-upload.
void upload_queued_events() {
    // Sized for kMaxEvents (8) at well under 150 bytes/event worst case —
    // see the 200-byte per-event guard below, which stops before this
    // would ever truncate.
    static char body[1280];
    size_t n = 0;
    n += snprintf(body + n, sizeof(body) - n, "{\"events\":[");
    bool first = true;
    // How many events (from the front) actually made it into the body —
    // NOT necessarily s_count, if the belt-and-braces size guard below
    // ever trips. Only THIS MANY may be dropped after a 2xx; anything
    // past it stays queued for the next tick, exactly like story_report's
    // `included[]` bookkeeping.
    int included_count = 0;
    for (int i = 0; i < s_count; i++) {
        // Worst-case one event is well under 150 chars; stop before
        // truncation and let the next tick carry the remainder (the queue
        // is only 16 deep, so this is belt-and-braces).
        if (sizeof(body) - n < 200) {
            break;
        }
        n += snprintf(body + n, sizeof(body) - n,
                      "%s{\"key\":\"%s\",\"gameKey\":\"%s\"",
                      first ? "" : ",", s_events[i].key, s_events[i].game_key);
        if (s_events[i].rounds >= 0) {
            n += snprintf(body + n, sizeof(body) - n, ",\"rounds\":%d",
                          (int)s_events[i].rounds);
        }
        if (s_events[i].outcome[0] != '\0') {
            n += snprintf(body + n, sizeof(body) - n, ",\"outcome\":\"%s\"",
                          s_events[i].outcome);
        }
        if (s_events[i].score >= 0) {
            n += snprintf(body + n, sizeof(body) - n, ",\"score\":%d",
                          (int)s_events[i].score);
        }
        if (s_events[i].this_boot) {
            n += snprintf(body + n, sizeof(body) - n, ",\"secondsAgo\":%u",
                          (unsigned)((millis() - s_events[i].at_ms) / 1000u));
        }
        n += snprintf(body + n, sizeof(body) - n, "}");
        first = false;
        included_count++;
    }
    n += snprintf(body + n, sizeof(body) - n, "]}");
    if (first) {
        return;  // nothing queued to send (shouldn't happen — caller checked)
    }

    const int status = voice_post_game_plays(body);
    if (status < 200 || status >= 300) {
        Serial.printf("[game-report] upload failed (status=%d) — will retry\n", status);
        return;
    }

    // 2xx — drop only the events that were actually sent (the front
    // `included_count`), keep any overflow queued for the next tick.
    const int remaining = s_count - included_count;
    if (remaining > 0) {
        memmove(&s_events[0], &s_events[included_count],
                sizeof(GamePlayEvent) * (size_t)remaining);
    }
    s_count = remaining;
    persist_queue();
    Serial.printf("[game-report] uploaded %d event(s), %d still queued\n",
                  included_count, s_count);
}

}  // namespace

void game_report_on_finished(const char *game_key, int rounds,
                              const char *outcome, int score) {
    if (game_key == nullptr || game_key[0] == '\0') {
        return;
    }
    ensure_loaded();

    if (s_count == kMaxEvents) {
        // Full — drop the oldest event; recent history beats ancient history.
        memmove(&s_events[0], &s_events[1], sizeof(GamePlayEvent) * (kMaxEvents - 1));
        s_count--;
    }

    GamePlayEvent &ev = s_events[s_count];
    memset(&ev, 0, sizeof(ev));
    s_next_seq++;
    snprintf(ev.key, sizeof(ev.key), "b%u-%u",
             (unsigned)s_boot_seq, (unsigned)s_next_seq);
    snprintf(ev.game_key, sizeof(ev.game_key), "%s", game_key);
    if (outcome != nullptr) {
        snprintf(ev.outcome, sizeof(ev.outcome), "%s", outcome);
    }
    ev.rounds = (int16_t)((rounds < 0) ? -1 : (rounds > 32767 ? 32767 : rounds));
    ev.score  = (int16_t)((score  < 0) ? -1 : (score  > 32767 ? 32767 : score));
    ev.this_boot = 1;
    ev.at_ms = millis();

    s_count++;
    persist_queue();
    s_prompt_upload = true;
    s_prompt_since_ms = millis();
    Serial.printf("[game-report] played %s key=%s queued=%d\n",
                  ev.game_key, ev.key, s_count);
}

void game_report_tick() {
    ensure_loaded();
    if (s_count == 0) {
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
    upload_queued_events();
}
