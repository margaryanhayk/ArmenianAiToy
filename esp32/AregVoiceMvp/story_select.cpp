// -------------------------------------------------------------
// AregVoiceMvp / story_select.cpp — see story_select.h.
//
// Compiled into EVERY build (production included): selecting which
// cached story to play is normal playback behavior as of the
// story-select-from-index slice, not a bench-only path.
// -------------------------------------------------------------
#include "story_select.h"

#include <Arduino.h>
#include <ArduinoJson.h>
#include <FS.h>
#include <SD.h>
#include <Preferences.h>

#include "content_sync_model.h"   // cs_index_parse — one owner of the schema
#include "audio_io.h"             // audio_sd_available / audio_sd_has_file

namespace {

constexpr const char *kIndexPath = "/content_index.json";

// NVS namespace/key for the rotation cursor. Its own namespace so it can
// never collide with device_creds / wifi_creds / ota_state.
constexpr const char *kPrefsNamespace = "aregstory";
constexpr const char *kPrefsLastKey   = "last_id";

/// Actual on-card size, or -1 when absent/unopenable.
long sd_file_size(const char *path) {
    if (!SD.exists(path)) {
        return -1;
    }
    File f = SD.open(path, FILE_READ);
    if (!f) {
        return -1;
    }
    const long n = (long)f.size();
    f.close();
    return n;
}

/// An index entry is usable only if the CARD agrees with it. The rule
/// itself is the pure story_entry_eligible(); this only supplies the
/// on-card size it needs — and refuses to even stat an unsafe path.
bool is_eligible(const CsStory *e) {
    if (e == NULL || !story_is_safe_cache_path(e->cache_path)) {
        return false;
    }
    return story_entry_eligible(e, sd_file_size(e->cache_path));
}

/// Reads and parses the index into `out` (raw entries, unfiltered).
/// Returns the entry count, 0 on any absent/unreadable/malformed case.
int load_raw_index(CsStory *out, int max_out) {
    if (out == NULL || max_out <= 0) {
        return 0;
    }
    if (!audio_sd_available() || !SD.exists(kIndexPath)) {
        return 0;
    }
    File f = SD.open(kIndexPath, FILE_READ);
    if (!f) {
        return 0;
    }
    JsonDocument doc;
    const DeserializationError err = deserializeJson(doc, f);
    f.close();
    if (err != DeserializationError::Ok) {
        Serial.println("[story-select] index parse failed");
        Serial.flush();
        return 0;
    }
    int schema = 0;
    const int n = cs_index_parse(doc, out, max_out, &schema);
    if (schema < 2) {
        // A legacy v1 card carries exactly one story and no `verified`
        // field of its own. cs_index_parse migrates it (inferring
        // verified), and the eligibility check below still requires the
        // file to exist at the recorded size — so a v1 card keeps
        // working as a single-story card, which is precisely the
        // pre-selection behavior.
        Serial.printf("[story-select] legacy v%d index — %d entry\n", schema, n);
        Serial.flush();
    }
    return n;
}

}  // namespace

int story_select_load_eligible(CsStory *out, int max_out) {
    if (out == NULL || max_out <= 0) {
        return 0;
    }
    static CsStory raw[CS_MAX_STORIES];
    const int raw_count = load_raw_index(raw, CS_MAX_STORIES);

    int count = 0;
    for (int i = 0; i < raw_count && count < max_out; i++) {
        if (!is_eligible(&raw[i])) {
            continue;
        }
        bool dup = false;
        for (int j = 0; j < count; j++) {
            if (cs_story_ids_equal(out[j].story_id, raw[i].story_id)) {
                dup = true;
                break;
            }
        }
        if (dup) {
            continue;   // keep the first, same rule the sync uses
        }
        out[count++] = raw[i];   // index order preserved
    }
    return count;
}

bool story_select_resolve_path(const char *story_id, char *out, size_t out_len) {
    if (out == NULL || out_len == 0) {
        return false;
    }
    out[0] = '\0';
    if (!cs_is_valid_story_id(story_id)) {
        return false;
    }

    static CsStory raw[CS_MAX_STORIES];
    const int raw_count = load_raw_index(raw, CS_MAX_STORIES);

    for (int i = 0; i < raw_count; i++) {
        if (!cs_story_ids_equal(raw[i].story_id, story_id)) {
            continue;
        }
        // Found the requested id — it must independently pass every
        // eligibility rule. Returning false here (rather than falling
        // through to another entry) is what guarantees the resolver can
        // never hand back a DIFFERENT story than the one asked for.
        if (!is_eligible(&raw[i])) {
            Serial.printf("[story-select] %s present but not usable\n", story_id);
            Serial.flush();
            return false;
        }
        return cs_copy_bounded(out, out_len, raw[i].cache_path);
    }
    return false;
}

bool story_select_load_last(char *out, size_t out_len) {
    if (out == NULL || out_len == 0) {
        return false;
    }
    out[0] = '\0';

    Preferences prefs;
    if (!prefs.begin(kPrefsNamespace, /*readOnly=*/true)) {
        return false;   // never provisioned yet — not an error
    }
    char buf[CS_MAX_STORY_ID_LEN + 1];
    const size_t n = prefs.getString(kPrefsLastKey, buf, sizeof(buf));
    prefs.end();
    if (n == 0 || buf[0] == '\0') {
        return false;
    }
    // A stored value that no longer validates (corruption, a downgrade,
    // a hand-written key) is IGNORED rather than trusted — the selector
    // then simply starts from the first eligible story.
    if (!cs_is_valid_story_id(buf)) {
        Serial.println("[story-select] stored last-story id invalid — ignoring");
        Serial.flush();
        return false;
    }
    return cs_copy_bounded(out, out_len, buf);
}

bool story_select_save_last(const char *story_id) {
    if (!cs_is_valid_story_id(story_id)) {
        return false;
    }
    // Write only on a real change. Pause/resume re-enters the session
    // with the same story, and rewriting the same value every time would
    // burn flash for nothing.
    char current[CS_MAX_STORY_ID_LEN + 1];
    if (story_select_load_last(current, sizeof(current))
        && cs_story_ids_equal(current, story_id)) {
        return true;
    }

    Preferences prefs;
    if (!prefs.begin(kPrefsNamespace, /*readOnly=*/false)) {
        Serial.println("[story-select] NVS unavailable — rotation not persisted");
        Serial.flush();
        return false;   // never blocks playback; the caller ignores this
    }
    const size_t written = prefs.putString(kPrefsLastKey, story_id);
    prefs.end();
    return written > 0;
}

// ---- boot-scoped failed-start set ----------------------------------

namespace {
char s_failed[CS_MAX_STORIES][CS_MAX_STORY_ID_LEN + 1];
int  s_failed_count = 0;
}  // namespace

void story_select_mark_failed(const char *story_id) {
    if (!cs_is_valid_story_id(story_id)) {
        return;
    }
    for (int i = 0; i < s_failed_count; i++) {
        if (cs_story_ids_equal(s_failed[i], story_id)) {
            return;   // already known
        }
    }
    if (s_failed_count >= CS_MAX_STORIES) {
        return;       // bounded; saturate rather than overrun
    }
    cs_copy_bounded(s_failed[s_failed_count], sizeof(s_failed[0]), story_id);
    s_failed_count++;
    Serial.printf("[story-select] %s failed to start — excluded until reboot\n", story_id);
    Serial.flush();
}

void story_select_clear_failed() {
    if (s_failed_count == 0) {
        return;
    }
    s_failed_count = 0;
    Serial.println("[story-select] failed-start exclusions cleared");
    Serial.flush();
}

bool story_select_pick(const CsStory *eligible, int count,
                       char *out_story_id, size_t out_len) {
    char last_id[CS_MAX_STORY_ID_LEN + 1] = "";
    story_select_load_last(last_id, sizeof(last_id));   // absent/corrupt => ""

    const char *excluded[CS_MAX_STORIES];
    for (int i = 0; i < s_failed_count; i++) {
        excluded[i] = s_failed[i];
    }
    return story_select_next_excluding(eligible, count, last_id,
                                       excluded, s_failed_count,
                                       out_story_id, out_len);
}
