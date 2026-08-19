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
#include <esp_heap_caps.h>
#include <FS.h>
#include <SD.h>
#include <Preferences.h>

#include "content_sync_model.h"   // cs_index_parse — one owner of the schema
#include "audio_io.h"             // audio_sd_available / audio_sd_has_file

namespace {

constexpr const char *kIndexPath = "/content_index.json";

// PARSE THE INDEX OUT OF PSRAM, NOT INTERNAL HEAP.
//
// This is the same failure that took the toy down for a whole night on
// 2026-08-14, in a different file: the index is 10 stories + 42 voice clips
// + 104 game clips, each carrying a 64-character sha256, and a JsonDocument
// for it does not fit in internal heap once anything else is running.
// content_sync.cpp already parses through an allocator like this one; this
// call site never got it, so the index parsed fine at boot and failed in
// the middle of a story, when the audio buffers hold the heap:
//
//     [story-select] index parse failed
//     [post] question 0 not on the card - asking 1 instead
//
// The toy then asked the WRONG reflection question, silently. A parse that
// only fails under memory pressure fails at exactly the moment a child is
// listening, and reports itself as missing content rather than as an
// out-of-memory error, which is why it went unexplained for days.
//
// PSRAM is 7.8 MB and idle. TLS cannot use it; plain data can.
struct PsramJsonAllocator : ArduinoJson::Allocator {
    void *allocate(size_t n) override {
        void *p = heap_caps_malloc(n, MALLOC_CAP_SPIRAM);
        return p != nullptr ? p : malloc(n);   // never worse than before
    }
    void deallocate(void *p) override { heap_caps_free(p); }
    void *reallocate(void *p, size_t n) override {
        void *q = heap_caps_realloc(p, n, MALLOC_CAP_SPIRAM);
        return q != nullptr ? q : realloc(p, n);
    }
};
// File-scope so it outlives every document that borrows it.
PsramJsonAllocator s_json_psram;

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
    JsonDocument doc(&s_json_psram);
    const DeserializationError err = deserializeJson(doc, f);
    f.close();
    if (err != DeserializationError::Ok) {
        // Name the reason. "index parse failed" on its own sent this
        // hunting for a corrupt card when the card was fine.
        Serial.printf("[story-select] index parse failed: %s (heap=%u psram=%u)\n",
                      err.c_str(),
                      (unsigned)ESP.getFreeHeap(),
                      (unsigned)ESP.getFreePsram());
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

// One shared scratch table for every index read below. All callers run
// on the single Arduino loop task (no reentrancy), and each function
// fully re-loads the index before reading it — sharing saves ~2 × the
// table (~22 KB with the B2 clip slots) versus per-function statics.
CsStory s_raw[CS_MAX_STORIES];

// ---- heard set storage ---------------------------------------------
//
// Declared up here (rather than beside the story_heard_* API at the
// bottom) because the serial-episode filter in
// story_select_load_eligible needs the whole set in ONE read —
// story_heard_contains would re-open NVS per candidate.

constexpr const char *kHeardPrefsNamespace = "aregheard";
constexpr const char *kHeardPrefsKey       = "ids";

// One fixed-size blob, mirroring story_report's whole-queue persistence.
struct HeardSet {
    char ids[CS_MAX_STORIES][CS_MAX_STORY_ID_LEN + 1];
    int  count;
};

void heard_load(HeardSet *set) {
    memset(set, 0, sizeof(*set));
    Preferences prefs;
    if (!prefs.begin(kHeardPrefsNamespace, /*readOnly=*/true)) {
        return;   // never provisioned — an empty set, not an error
    }
    const size_t read = prefs.getBytes(kHeardPrefsKey, set, sizeof(*set));
    prefs.end();
    // A short or oversized read means a downgrade, corruption, or a
    // hand-written key. Treat it as empty rather than trusting it — the
    // worst outcome is the toy offering a story that was already heard.
    if (read != sizeof(*set) || set->count < 0 || set->count > CS_MAX_STORIES) {
        memset(set, 0, sizeof(*set));
    }
}

void heard_save(const HeardSet *set) {
    Preferences prefs;
    if (!prefs.begin(kHeardPrefsNamespace, /*readOnly=*/false)) {
        Serial.println("[welcome] NVS unavailable — heard set not persisted");
        Serial.flush();
        return;   // never blocks playback
    }
    prefs.putBytes(kHeardPrefsKey, set, sizeof(*set));
    prefs.end();
}

// ---- serial episodes ------------------------------------------------

// Boot-scoped ONLY, by design — see the honest-limitation note in
// story_select.h. RAM, never NVS: persisting it would need a clock to
// know when to expire it, and the device has none.
char s_series_latch[CS_MAX_STORIES][CS_MAX_STORY_ID_LEN + 1];
int  s_series_latch_count = 0;

/// Applies the serial ordering rule to an eligible list, IN PLACE, and
/// returns the surviving count.
///
/// Best-effort, exactly like the failed-start exclusion: if the rule
/// would leave nothing to play it is ignored entirely. A card holding
/// only a fully-heard serial must not make the toy go silent.
int apply_series_rule(CsStory *list, int count) {
    if (list == NULL || count <= 0 || count > CS_MAX_STORIES) {
        return count < 0 ? 0 : count;
    }
    bool any_serial = false;
    for (int i = 0; i < count; i++) {
        if (cs_story_is_serial(&list[i])) { any_serial = true; break; }
    }
    if (!any_serial) {
        return count;   // a library with no serials pays nothing for this
    }

    HeardSet set;
    heard_load(&set);            // ONE NVS read for the whole list
    bool heard[CS_MAX_STORIES];
    for (int i = 0; i < count; i++) {
        heard[i] = false;
        for (int k = 0; k < set.count; k++) {
            if (cs_story_ids_equal(set.ids[k], list[i].story_id)) {
                heard[i] = true;
                break;
            }
        }
    }

    const char *latched[CS_MAX_STORIES];
    for (int i = 0; i < s_series_latch_count && i < CS_MAX_STORIES; i++) {
        latched[i] = s_series_latch[i];
    }

    bool keep[CS_MAX_STORIES];
    int kept = 0;
    for (int i = 0; i < count; i++) {
        keep[i] = story_series_member_allowed(list, count, i, heard,
                                              latched, s_series_latch_count);
        if (keep[i]) kept++;
    }
    if (kept == 0) {
        Serial.println("[story-select] series rule leaves nothing to play — ignoring it");
        Serial.flush();
        return count;
    }
    if (kept == count) {
        return count;
    }
    int n = 0;
    for (int i = 0; i < count; i++) {
        if (keep[i]) list[n++] = list[i];
    }
    Serial.printf("[story-select] series rule: %d of %d stories offered\n", n, count);
    Serial.flush();
    return n;
}

}  // namespace

int story_select_load_eligible(CsStory *out, int max_out) {
    if (out == NULL || max_out <= 0) {
        return 0;
    }
    const int raw_count = load_raw_index(s_raw, CS_MAX_STORIES);
    CsStory *raw = s_raw;

    int count = 0;
    for (int i = 0; i < raw_count && count < max_out; i++) {
        if (!is_eligible(&raw[i])) {
            continue;
        }
        // Variant endings — an alternate-ending file is NOT a rotation
        // member. It starts part-way through its story, so letting one reach
        // story_select_pick (or the welcome flow's offer loop, which reads
        // this same list) would mean narrating half a story to a child as
        // though it were the whole thing. Reachable only via
        // story_select_resolve_playback_path, by the base story it belongs to.
        if (cs_story_is_variant(&raw[i])) {
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
    // Serial support — the LAST step, so it filters a list that has
    // already passed every card-agreement check. An episode whose file is
    // missing was never a candidate, and must not block its successors.
    return apply_series_rule(out, count);
}

bool story_select_resolve_path(const char *story_id, char *out, size_t out_len) {
    if (out == NULL || out_len == 0) {
        return false;
    }
    out[0] = '\0';
    if (!cs_is_valid_story_id(story_id)) {
        return false;
    }

    const int raw_count = load_raw_index(s_raw, CS_MAX_STORIES);
    CsStory *raw = s_raw;

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

bool story_select_resolve_playback_path(const char *story_id,
                                        char *out, size_t out_len,
                                        bool *out_is_variant) {
    if (out_is_variant != NULL) {
        *out_is_variant = false;
    }
    if (out == NULL || out_len == 0) {
        return false;
    }
    out[0] = '\0';
    if (!cs_is_valid_story_id(story_id)) {
        return false;
    }

    // Cheapest gates first: the parent toggle (one small index read), then
    // the heard-set (one NVS read), and only then the index scan. A toy with
    // the toggle off, or a child on a FIRST listen, pays almost nothing —
    // which is every session until a story is heard twice.
    // AREG_VARIANT_ENDINGS_ENABLED — DEFAULT OFF, same reasoning as the
    // mid-story pauses (owner report 2026-08-10). Swapping the ending of a
    // story on a re-listen has never been bench-run, and the endings
    // themselves are approved as TEXT but not yet re-rendered. Until both
    // are true, a re-listen plays the story the child already knows.
#ifndef AREG_VARIANT_ENDINGS_ENABLED
#define AREG_VARIANT_ENDINGS_ENABLED 0
#endif
    if (AREG_VARIANT_ENDINGS_ENABLED
        && story_variant_endings_enabled() && story_heard_contains(story_id)) {
        const int raw_count = load_raw_index(s_raw, CS_MAX_STORIES);
        for (int i = 0; i < raw_count; i++) {
            if (!cs_story_is_variant(&s_raw[i])
                || !cs_story_ids_equal(s_raw[i].alt_of, story_id)) {
                continue;
            }
            // First match wins, mirroring the duplicate rule everywhere else
            // on this card. A variant that is present but unusable (half
            // synced, size mismatch, unverified) falls back to the base
            // story rather than making the toy silent — the child still
            // gets their story, just the ending they already know.
            if (!is_eligible(&s_raw[i])) {
                Serial.printf("[story-select] variant %s present but not usable\n",
                              s_raw[i].story_id);
                Serial.flush();
                break;
            }
            if (cs_copy_bounded(out, out_len, s_raw[i].cache_path)) {
                if (out_is_variant != NULL) {
                    *out_is_variant = true;
                }
                Serial.printf("[story-select] %s heard before — playing variant %s\n",
                              story_id, s_raw[i].story_id);
                Serial.flush();
                return true;
            }
            break;
        }
    }

    // No variant wanted, none cached, or none usable — the authored story.
    // This re-reads the index into s_raw, so the scratch table above being
    // clobbered is expected and harmless.
    return story_select_resolve_path(story_id, out, out_len);
}

bool story_select_resolve_clip_path(const char *story_id, const char *kind,
                                    char *out, size_t out_len) {
    if (out == NULL || out_len == 0) {
        return false;
    }
    out[0] = '\0';
    if (!cs_is_valid_story_id(story_id) || !cs_is_valid_clip_kind(kind)) {
        return false;
    }

    const int raw_count = load_raw_index(s_raw, CS_MAX_STORIES);
    CsStory *raw = s_raw;

    for (int i = 0; i < raw_count; i++) {
        if (!cs_story_ids_equal(raw[i].story_id, story_id)) {
            continue;
        }
        for (int k = 0; k < raw[i].clip_count; k++) {
            const CsClip *clip = &raw[i].clips[k];
            if (strcmp(clip->kind, kind) != 0) {
                continue;
            }
            if (!clip->verified || clip->size_bytes <= 0) {
                return false;   // exact kind found but not usable
            }
            char path[CS_MAX_PATH_LEN];
            if (!cs_build_clip_cache_path(path, sizeof(path),
                                          raw[i].story_id, raw[i].version, kind)) {
                return false;
            }
            // Card must agree with the metadata — same rule as narration.
            if (sd_file_size(path) != clip->size_bytes) {
                return false;
            }
            return cs_copy_bounded(out, out_len, path);
        }
        return false;   // story found, clip kind absent
    }
    return false;
}

bool story_select_intro_enabled() {
    if (!audio_sd_available() || !SD.exists(kIndexPath)) {
        return true;   // no card / no index — shipped default is ON
    }
    File f = SD.open(kIndexPath, FILE_READ);
    if (!f) {
        return true;
    }
    JsonDocument doc;
    const DeserializationError err = deserializeJson(doc, f);
    f.close();
    if (err != DeserializationError::Ok) {
        return true;
    }
    return cs_index_intro_enabled(doc);
}

bool story_select_music_enabled() {
    if (!audio_sd_available() || !SD.exists(kIndexPath)) {
        return false;   // opt-in: absent card/index means OFF
    }
    File f = SD.open(kIndexPath, FILE_READ);
    if (!f) {
        return false;
    }
    JsonDocument doc;
    const DeserializationError err = deserializeJson(doc, f);
    f.close();
    if (err != DeserializationError::Ok) {
        return false;
    }
    return cs_index_music_enabled(doc);
}

bool music_select_next(char *out_path, size_t out_len) {
    if (out_path == NULL || out_len == 0) {
        return false;
    }
    out_path[0] = '\0';
    if (!audio_sd_available() || !SD.exists(kIndexPath)) {
        return false;
    }
    File f = SD.open(kIndexPath, FILE_READ);
    if (!f) {
        return false;
    }
    JsonDocument doc;
    const DeserializationError err = deserializeJson(doc, f);
    f.close();
    if (err != DeserializationError::Ok) {
        return false;
    }

    static CsMusic tracks[CS_MAX_MUSIC];
    const int raw = cs_index_parse_music(doc, tracks, CS_MAX_MUSIC);

    // Eligible = verified + file on the card at the recorded size.
    int eligible[CS_MAX_MUSIC];
    char paths[CS_MAX_MUSIC][CS_MAX_PATH_LEN];
    int n = 0;
    for (int i = 0; i < raw && n < CS_MAX_MUSIC; i++) {
        if (!tracks[i].verified || tracks[i].size_bytes <= 0) {
            continue;
        }
        if (!cs_build_music_cache_path(paths[n], sizeof(paths[n]),
                                       tracks[i].track_id, tracks[i].version)) {
            continue;
        }
        if (sd_file_size(paths[n]) != tracks[i].size_bytes) {
            continue;
        }
        eligible[n++] = i;
    }
    if (n == 0) {
        return false;
    }

    // Round-robin cursor, own NVS namespace so it never collides with the
    // story rotation.
    char last_id[CS_MAX_STORY_ID_LEN + 1] = "";
    Preferences prefs;
    if (prefs.begin("aregmusic", /*readOnly=*/true)) {
        prefs.getString("last_id", last_id, sizeof(last_id));
        prefs.end();
    }
    int prev_pos = -1;
    for (int k = 0; k < n; k++) {
        if (cs_story_ids_equal(tracks[eligible[k]].track_id, last_id)) {
            prev_pos = k;
            break;
        }
    }
    const int chosen = (prev_pos >= 0) ? (prev_pos + 1) % n : 0;

    if (prefs.begin("aregmusic", /*readOnly=*/false)) {
        prefs.putString("last_id", tracks[eligible[chosen]].track_id);
        prefs.end();
    }
    Serial.printf("[music] selected %s\n", tracks[eligible[chosen]].track_id);
    Serial.flush();
    return cs_copy_bounded(out_path, out_len, paths[chosen]);
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

// ---- welcome flow ---------------------------------------------------

namespace {

constexpr const char *kVoicePrefsNamespace = "aregvoice";
constexpr const char *kVoicePrefsLastKey   = "last_greet";

// ONE scratch table shared by both voice-clip readers below. They are
// never re-entrant (both are called from the single blocking welcome
// flow), and a table each would cost ~4 KB of .bss for nothing.
CsVoice s_voice_scratch[CS_MAX_VOICE];

/// Opens + parses the index into `doc`. Shared by every welcome-flow
/// reader below, all of which need the whole document rather than the
/// story array the story path already has a loader for.
bool load_index_doc(JsonDocument &doc) {
    if (!audio_sd_available() || !SD.exists(kIndexPath)) {
        return false;
    }
    File f = SD.open(kIndexPath, FILE_READ);
    if (!f) {
        return false;
    }
    const DeserializationError err = deserializeJson(doc, f);
    f.close();
    return err == DeserializationError::Ok;
}

}  // namespace

// ---- story feature toggles (index schema v6) ------------------------
//
// Placed here rather than beside story_select_intro_enabled because they
// use load_index_doc, which is declared just above. The intro/music
// accessors predate that helper and open the file themselves; they are
// left alone rather than refactored — this slice has no business
// rewriting a working read path.

bool story_pauses_enabled() {
    JsonDocument doc;
    if (!load_index_doc(doc)) {
        return true;   // no card / no index — shipped default is ON
    }
    return cs_index_pauses_enabled(doc);
}

bool story_variant_endings_enabled() {
    JsonDocument doc;
    if (!load_index_doc(doc)) {
        return true;   // no card / no index — shipped default is ON
    }
    return cs_index_variants_enabled(doc);
}

bool voice_clip_resolve_path(const char *voice_id, char *out, size_t out_len) {
    if (out == NULL || out_len == 0) {
        return false;
    }
    out[0] = '\0';
    if (!cs_is_valid_story_id(voice_id)) {
        return false;
    }
    JsonDocument doc;
    if (!load_index_doc(doc)) {
        return false;
    }

    const int raw = cs_index_parse_voice(doc, s_voice_scratch, CS_MAX_VOICE);
    for (int i = 0; i < raw; i++) {
        const CsVoice *clip = &s_voice_scratch[i];
        if (!cs_story_ids_equal(clip->voice_id, voice_id)) {
            continue;
        }
        if (!clip->verified || clip->size_bytes <= 0) {
            return false;
        }
        char path[CS_MAX_PATH_LEN];
        if (!cs_build_voice_cache_path(path, sizeof(path),
                                       clip->voice_id, clip->version)) {
            return false;
        }
        // The card has to agree with the index — a clip file can vanish
        // independently of the entry that describes it.
        if (sd_file_size(path) != clip->size_bytes) {
            return false;
        }
        return cs_copy_bounded(out, out_len, path);
    }
    return false;   // never a different clip's path
}

bool voice_clip_next_greeting(char *out_path, size_t out_len) {
    if (out_path == NULL || out_len == 0) {
        return false;
    }
    out_path[0] = '\0';
    JsonDocument doc;
    if (!load_index_doc(doc)) {
        return false;
    }

    const int raw = cs_index_parse_voice(doc, s_voice_scratch, CS_MAX_VOICE);

    // Eligible = a greeting id + verified + the file on the card at the
    // recorded size. Same three-part rule the story and music selectors
    // use; index metadata alone is never enough.
    //
    // Only the INDICES are kept — an array of built paths here would be
    // ~3 KB of stack in a function called from the boot path, and the
    // chosen path is cheap to rebuild once at the end.
    int eligible[CS_MAX_VOICE];
    int n = 0;
    for (int i = 0; i < raw && n < CS_MAX_VOICE; i++) {
        const CsVoice *clip = &s_voice_scratch[i];
        if (!cs_voice_is_greeting(clip->voice_id)) continue;
        if (!clip->verified || clip->size_bytes <= 0) continue;
        char path[CS_MAX_PATH_LEN];
        if (!cs_build_voice_cache_path(path, sizeof(path),
                                       clip->voice_id, clip->version)) {
            continue;
        }
        if (sd_file_size(path) != clip->size_bytes) continue;
        eligible[n++] = i;
    }
    if (n == 0) {
        return false;   // no greetings yet — the toy stays silent at boot
    }

    char last_id[CS_MAX_STORY_ID_LEN + 1] = "";
    Preferences prefs;
    if (prefs.begin(kVoicePrefsNamespace, /*readOnly=*/true)) {
        prefs.getString(kVoicePrefsLastKey, last_id, sizeof(last_id));
        prefs.end();
    }
    int prev_pos = -1;
    for (int k = 0; k < n; k++) {
        if (cs_story_ids_equal(s_voice_scratch[eligible[k]].voice_id, last_id)) {
            prev_pos = k;
            break;
        }
    }
    // Unknown/absent previous → first. Otherwise the one AFTER it,
    // wrapping — which is what makes "never the same greeting twice in a
    // row" true by construction rather than by retrying.
    const CsVoice *pick = &s_voice_scratch[eligible[(prev_pos >= 0) ? (prev_pos + 1) % n : 0]];

    if (prefs.begin(kVoicePrefsNamespace, /*readOnly=*/false)) {
        prefs.putString(kVoicePrefsLastKey, pick->voice_id);
        prefs.end();
    }
    Serial.printf("[welcome] greeting %s\n", pick->voice_id);
    Serial.flush();
    return cs_build_voice_cache_path(out_path, out_len,
                                     pick->voice_id, pick->version);
}

bool story_select_mode_enabled(char mode) {
    const char *key = NULL;
    switch (mode) {
        case 's': key = "storyEnabled";     break;
        case 'g': key = "gameEnabled";      break;
        case 'r': key = "riddleEnabled";    break;
        case 'c': key = "curiosityEnabled"; break;
        default:  return true;   // unknown letter — never invent a refusal
    }
    JsonDocument doc;
    if (!load_index_doc(doc)) {
        return true;   // no card / no index → the shipped default (on)
    }
    return cs_index_mode_enabled(doc, key);
}

// ---- "already heard" set --------------------------------------------
//
// Storage (HeardSet / heard_load / heard_save) lives in the anonymous
// namespace at the top of this file, because the serial-episode filter
// needs it before this point.

bool story_heard_contains(const char *story_id) {
    if (!cs_is_valid_story_id(story_id)) {
        return false;
    }
    HeardSet set;
    heard_load(&set);
    for (int i = 0; i < set.count; i++) {
        if (cs_story_ids_equal(set.ids[i], story_id)) {
            return true;
        }
    }
    return false;
}

void story_heard_mark(const char *story_id) {
    if (!cs_is_valid_story_id(story_id)) {
        return;
    }
    HeardSet set;
    heard_load(&set);
    for (int i = 0; i < set.count; i++) {
        if (cs_story_ids_equal(set.ids[i], story_id)) {
            return;   // already known — do not re-write flash
        }
    }
    if (set.count >= CS_MAX_STORIES) {
        // Drop the OLDEST. The set is a sliding window once the library
        // outgrows the bound, which is the honest product behavior.
        for (int i = 1; i < CS_MAX_STORIES; i++) {
            memcpy(set.ids[i - 1], set.ids[i], sizeof(set.ids[0]));
        }
        set.count = CS_MAX_STORIES - 1;
    }
    cs_copy_bounded(set.ids[set.count], sizeof(set.ids[0]), story_id);
    set.count++;
    heard_save(&set);
    Serial.printf("[welcome] heard %s (%d known)\n", story_id, set.count);
    Serial.flush();

    // Serial support — latch the SERIES, not the episode. The episode is
    // already excluded by the heard set; latching the series is what stops
    // the very next press walking straight into episode 2. Resolved from
    // the index here so the playback loop never has to know about series.
    char series[CS_MAX_STORY_ID_LEN + 1];
    if (story_series_of(story_id, series, sizeof(series))) {
        story_series_latch(series);
    }
}

// ---- serial episodes -------------------------------------------------

void story_series_latch(const char *series_id) {
    if (!cs_is_valid_story_id(series_id)) {
        return;
    }
    for (int i = 0; i < s_series_latch_count; i++) {
        if (cs_story_ids_equal(s_series_latch[i], series_id)) {
            return;   // already latched this boot
        }
    }
    if (s_series_latch_count >= CS_MAX_STORIES) {
        return;       // bounded; saturate rather than overrun
    }
    cs_copy_bounded(s_series_latch[s_series_latch_count],
                    sizeof(s_series_latch[0]), series_id);
    s_series_latch_count++;
    Serial.printf("[story-select] series %s: next episode waits until reboot\n",
                  series_id);
    Serial.flush();
}

bool story_series_is_latched(const char *series_id) {
    if (!cs_is_valid_story_id(series_id)) {
        return false;
    }
    for (int i = 0; i < s_series_latch_count; i++) {
        if (cs_story_ids_equal(s_series_latch[i], series_id)) {
            return true;
        }
    }
    return false;
}

void story_series_clear_latches() {
    s_series_latch_count = 0;
}

bool story_series_of(const char *story_id, char *out, size_t out_len) {
    if (out == NULL || out_len == 0) {
        return false;
    }
    out[0] = '\0';
    if (!cs_is_valid_story_id(story_id)) {
        return false;
    }
    const int raw_count = load_raw_index(s_raw, CS_MAX_STORIES);
    for (int i = 0; i < raw_count; i++) {
        if (!cs_story_ids_equal(s_raw[i].story_id, story_id)) {
            continue;
        }
        if (!cs_story_is_serial(&s_raw[i])) {
            return false;   // found, but a standalone story
        }
        return cs_copy_bounded(out, out_len, s_raw[i].series_id);
    }
    return false;
}

int story_heard_count() {
    HeardSet set;
    heard_load(&set);
    return set.count;
}

void story_heard_clear() {
    HeardSet set;
    memset(&set, 0, sizeof(set));
    heard_save(&set);
}

// ---- per-story reflection-question cursor ---------------------------
//
// Lives here rather than in the .ino because this file already owns
// every per-story fact the toy persists — the rotation cursor, the heard
// set, the series latch — and already carries the fixed-blob NVS idiom
// this copies. The .ino stays a flow file with no NVS code in it.

namespace {

// One slot per story, so this table and the heard set can never fall out
// of step. Guarded rather than hardcoded so a build flag can shrink it;
// config.h carries the same guarded default as the public record. The
// override that reaches EVERY translation unit is the build flag —
// config.h is not included here, deliberately (it would also expose this
// file's AREG_VARIANT_ENDINGS_ENABLED default to config.h, which is a
// separate decision and not this slice's to make).
#ifndef AREG_QUESTION_CURSOR_SLOTS
#define AREG_QUESTION_CURSOR_SLOTS CS_MAX_STORIES
#endif

constexpr const char *kQuestionPrefsNamespace = "aregqidx";
constexpr const char *kQuestionPrefsKey       = "cursors";

// Parallel arrays rather than a struct-per-entry: it keeps the blob's
// layout obvious and the whole thing is one stack local, never .bss.
struct QuestionCursorSet {
    char    ids[AREG_QUESTION_CURSOR_SLOTS][CS_MAX_STORY_ID_LEN + 1];
    uint8_t last_asked[AREG_QUESTION_CURSOR_SLOTS];
    int     count;
};

void question_cursor_load(QuestionCursorSet *set) {
    memset(set, 0, sizeof(*set));
    Preferences prefs;
    if (!prefs.begin(kQuestionPrefsNamespace, /*readOnly=*/true)) {
        return;   // never provisioned — an empty set, not an error
    }
    const size_t read = prefs.getBytes(kQuestionPrefsKey, set, sizeof(*set));
    prefs.end();
    // Same rule the heard set applies: a short or oversized read means a
    // downgrade, corruption or a hand-written key. Treat it as empty
    // rather than trusting it — the worst outcome is a child being asked
    // question 0 again, which is not worth a single risky byte.
    if (read != sizeof(*set) || set->count < 0
        || set->count > AREG_QUESTION_CURSOR_SLOTS) {
        memset(set, 0, sizeof(*set));
    }
}

/// Position of `story_id` in the set, or -1.
int question_cursor_find(const QuestionCursorSet *set, const char *story_id) {
    for (int i = 0; i < set->count; i++) {
        if (cs_story_ids_equal(set->ids[i], story_id)) {
            return i;
        }
    }
    return -1;
}

}  // namespace

int story_question_cursor_next(const char *story_id) {
    if (!cs_is_valid_story_id(story_id)) {
        return 0;
    }
    QuestionCursorSet set;
    question_cursor_load(&set);
    const int at = question_cursor_find(&set, story_id);
    if (at < 0) {
        return 0;   // first listen — question 0, the one most stories have
    }
    // A stored index outside the range (a downgrade from a build with more
    // kinds, or a corrupt byte) is clamped by the modulo rather than
    // trusted, so it can never index cs_question_clip_kind out of bounds.
    return (set.last_asked[at] + 1) % CS_QUESTION_KINDS;
}

void story_question_cursor_commit(const char *story_id, int index) {
    if (!cs_is_valid_story_id(story_id)
        || index < 0 || index >= CS_QUESTION_KINDS) {
        return;
    }
    QuestionCursorSet set;
    question_cursor_load(&set);

    const int at = question_cursor_find(&set, story_id);
    if (at >= 0) {
        if (set.last_asked[at] == (uint8_t)index) {
            return;   // already there — do not re-write flash
        }
        set.last_asked[at] = (uint8_t)index;
    } else {
        if (set.count >= AREG_QUESTION_CURSOR_SLOTS) {
            // Drop the OLDEST, exactly as the heard set does. Refusing to
            // record would be worse: the evicted story simply starts its
            // rotation again, while a refusal would freeze every story
            // added after the table filled on question 0 forever.
            for (int i = 1; i < AREG_QUESTION_CURSOR_SLOTS; i++) {
                memcpy(set.ids[i - 1], set.ids[i], sizeof(set.ids[0]));
                set.last_asked[i - 1] = set.last_asked[i];
            }
            set.count = AREG_QUESTION_CURSOR_SLOTS - 1;
        }
        cs_copy_bounded(set.ids[set.count], sizeof(set.ids[0]), story_id);
        set.last_asked[set.count] = (uint8_t)index;
        set.count++;
    }

    Preferences prefs;
    if (!prefs.begin(kQuestionPrefsNamespace, /*readOnly=*/false)) {
        Serial.println("[post] NVS unavailable — question cursor not persisted");
        Serial.flush();
        return;   // never blocks the flow; the child still got their question
    }
    prefs.putBytes(kQuestionPrefsKey, &set, sizeof(set));
    prefs.end();
}
