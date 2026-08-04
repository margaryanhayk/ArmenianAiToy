// -------------------------------------------------------------
// AregVoiceMvp / story_select.h — pick which cached story to play.
//
// Replaces the compile-time AREG_STORY_ID as the playback source of
// truth. Reads the schema-v2 `stories[]` written by content_sync, keeps
// only entries that are actually usable on the card, and rotates through
// them so the same story never plays twice in a row.
//
// The selector itself (story_select_next) is a pure, allocation-free
// function defined inline below so the bench harness can exercise it
// without a card, a network, or NVS. Everything that touches SD or NVS
// lives in story_select.cpp.
//
// NOT here (later slices): spoken story shelf, the child naming a title,
// semantic matching, age/bedtime filtering, entitlements, retirement.
// -------------------------------------------------------------
#pragma once

#include "content_sync_rules.h"   // CsStory, bounds, id/path validation

// ---- pure selection ------------------------------------------------

/// Deterministic round-robin over `stories`, skipping whatever played
/// last. Random selection was rejected for v1: it makes the bench
/// unreproducible, can repeat back-to-back by chance, and depends on
/// boot-time RNG quality on a device that has no good entropy at that
/// point.
///
/// Rules, in order:
///   - count <= 0, or a bad output buffer          -> false
///   - count == 1                                  -> that story (a
///     single-story card must keep working, so no-repeat cannot apply)
///   - previous id absent from the list (or empty) -> the FIRST entry
///   - otherwise                                   -> the entry AFTER
///     the previous one, wrapping at the end
///
/// With count >= 2 the result is never equal to `previous_story_id`, so
/// no-repeat holds by construction rather than by retry.
///
/// Comparison is case-insensitive, matching how the backend and the
/// index treat ids.
/// As above, but skipping any id in `excluded` — the boot-scoped set of
/// stories that RESOLVED but failed to actually start playing. Rotation
/// runs over the surviving candidates, preserving index order.
///
/// The exclusion is BEST-EFFORT: if applying it would leave nothing to
/// play, it is ignored and the full list is used again. That is what
/// keeps a one-story card working when its only story failed — the child
/// gets a retry on the next press rather than silence, and there is no
/// loop because each press is a single attempt.
///
/// Consequence worth knowing: with exactly two stories where one is
/// broken, the good one replays back-to-back. Availability beats strict
/// no-repeat when the library is effectively one playable story.
inline bool story_select_next_excluding(const CsStory *stories, int count,
                                        const char *previous_story_id,
                                        const char *const *excluded,
                                        int excluded_count,
                                        char *out_story_id, size_t out_len) {
    if (stories == NULL || count <= 0 || out_story_id == NULL || out_len == 0) {
        return false;
    }

    int cand[CS_MAX_STORIES];
    int n_cand = 0;
    for (int i = 0; i < count && n_cand < CS_MAX_STORIES; i++) {
        bool is_excluded = false;
        for (int j = 0; excluded != NULL && j < excluded_count; j++) {
            if (excluded[j] != NULL
                && cs_story_ids_equal(stories[i].story_id, excluded[j])) {
                is_excluded = true;
                break;
            }
        }
        if (!is_excluded) {
            cand[n_cand++] = i;
        }
    }
    if (n_cand == 0) {
        for (int i = 0; i < count && n_cand < CS_MAX_STORIES; i++) {
            cand[n_cand++] = i;   // exclusion never starves playback
        }
    }
    if (n_cand == 0) {
        return false;
    }
    if (n_cand == 1) {
        return cs_copy_bounded(out_story_id, out_len, stories[cand[0]].story_id);
    }

    int prev_pos = -1;
    if (previous_story_id != NULL && previous_story_id[0] != '\0') {
        for (int k = 0; k < n_cand; k++) {
            if (cs_story_ids_equal(stories[cand[k]].story_id, previous_story_id)) {
                prev_pos = k;
                break;
            }
        }
    }
    const int chosen = (prev_pos >= 0) ? cand[(prev_pos + 1) % n_cand] : cand[0];
    // Refuse rather than truncate — a clipped id would address the wrong
    // story, or no story at all.
    return cs_copy_bounded(out_story_id, out_len, stories[chosen].story_id);
}

inline bool story_select_next(const CsStory *stories, int count,
                              const char *previous_story_id,
                              char *out_story_id, size_t out_len) {
    return story_select_next_excluding(stories, count, previous_story_id,
                                       NULL, 0, out_story_id, out_len);
}

// ---- pure eligibility ----------------------------------------------

/// Every eligible cache path must be absolute, sit directly under
/// /stories/, carry no traversal segment or backslash, and be bounded.
/// The index is written by our own sync, but treating it as untrusted
/// costs nothing and stops a hand-edited or corrupted card from pointing
/// playback at an arbitrary file.
inline bool story_is_safe_cache_path(const char *path) {
    if (path == NULL || path[0] != '/') {
        return false;
    }
    const size_t n = strlen(path);
    if (n == 0 || n >= CS_MAX_PATH_LEN) {
        return false;
    }
    static const char kPrefix[] = "/stories/";
    if (strncmp(path, kPrefix, sizeof(kPrefix) - 1) != 0) {
        return false;
    }
    if (strstr(path, "..") != NULL || strchr(path, '\\') != NULL) {
        return false;
    }
    return true;
}

/// Metadata + card agreement for one index entry. `actual_size` is the
/// file's real size on the card, or a negative value when the file is
/// absent/unopenable — the caller supplies it, which keeps this function
/// pure and testable while still making "the file must actually be
/// there, at the recorded size" part of the rule.
///
/// Index metadata alone is never sufficient: a file can vanish
/// independently of the index.
inline bool story_entry_eligible(const CsStory *e, long actual_size) {
    if (e == NULL || !e->verified || e->version < 1) {
        return false;
    }
    if (!cs_is_valid_story_id(e->story_id) || !story_is_safe_cache_path(e->cache_path)) {
        return false;
    }
    if (e->size_bytes <= 0) {
        return false;
    }
    return actual_size >= 0 && actual_size == e->size_bytes;
}

// ---- index-backed API (implemented in story_select.cpp) -------------

/// Loads every ELIGIBLE story from /content_index.json into `out`, in
/// index order, bounded by `max_out`. Returns the count (0 when the
/// index is absent, malformed, or holds nothing usable — never an
/// error, and never destructive).
///
/// Eligible means ALL of: valid id, `verified` true, version >= 1, a
/// bounded cache path inside the story cache directory, the file exists
/// on the card, and its actual size equals the recorded sizeBytes.
/// Duplicate ids keep the first. Index metadata alone is never enough —
/// a file can vanish independently of the index.
///
/// Reads ONLY the v2 `stories[]` array; the legacy flat root fields are
/// deliberately ignored here so a stale mirror can never override a
/// valid v2 selection.
int story_select_load_eligible(CsStory *out, int max_out);

/// Story-aware cache-path resolver. Fills `out` with the cached MP3 path
/// for EXACTLY `story_id`, or returns false.
///
/// Returns false — never a different story's path — when the id is
/// invalid, the index is missing/malformed, the id is not present, the
/// entry is unverified, its path is unsafe, the file is missing, or the
/// size disagrees. Callers cannot pass an arbitrary filesystem path
/// through it; only ids that appear in the index resolve.
bool story_select_resolve_path(const char *story_id, char *out, size_t out_len);

/// B2 — clip-path resolver. Fills `out` with the cached clip MP3 path
/// (/stories/<id>-v<n>-<kind>.mp3) for EXACTLY `story_id` + `kind`, or
/// returns false. Same never-a-different-file contract as
/// story_select_resolve_path: requires the story present in the index,
/// a VERIFIED clip entry of that kind, and the file on the card at the
/// recorded size. Kinds: "intro" | "question" | "summary".
bool story_select_resolve_clip_path(const char *story_id, const char *kind,
                                    char *out, size_t out_len);

/// B3 — the parent's spoken-story-intro toggle as cached on the card
/// (index root `introEnabled`, written by content_sync from the
/// manifest). Absent index / pre-v3 card → true, the shipped default.
bool story_select_intro_enabled();

/// Slice E — the parent's bedtime-music opt-in as cached on the card
/// (index root `musicEnabled`). Absent → false (music is opt-in).
bool story_select_music_enabled();

/// Slice E — picks the next bedtime-music track (round-robin over the
/// index's verified `music` entries whose file is on the card at the
/// recorded size; NVS cursor namespace `aregmusic`) and resolves its
/// cache path into `out`. Returns false when no usable track exists.
/// The cursor advances on pick (music has no started-gate — a rare
/// skipped track costs nothing).
bool music_select_next(char *out_path, size_t out_len);

/// Last-played story id, persisted in NVS so the rotation survives a
/// reboot. Returns false when nothing valid is stored (missing key,
/// unavailable NVS, or a stored value that no longer passes id
/// validation — a corrupt entry is ignored, never trusted).
bool story_select_load_last(char *out, size_t out_len);

/// Persists the last-played story id. Writes ONLY when the value
/// actually changes, so a paused/resumed story does not re-write flash.
/// Returns false on failure; a failure is never allowed to block
/// playback.
///
/// Call this ONLY once playback has genuinely started (see
/// audio_play_story_file's `out_started`). A story that resolved but
/// never made a sound must not become `last_id`, or the next press would
/// skip a story the child never heard.
bool story_select_save_last(const char *story_id);

// ---- boot-scoped failed-start set ----------------------------------
//
// A story that resolved but failed to start is remembered in RAM for the
// rest of this boot and skipped by the next NEW-story selection, so a
// corrupt-but-right-sized file cannot trap the rotation on itself. It is
// deliberately NOT persisted: a reboot retries it, which is safer than
// permanently skipping a story that might be fine.

/// Records a story that resolved but did not start. Bounded; ignores
/// invalid ids and silently saturates at CS_MAX_STORIES.
void story_select_mark_failed(const char *story_id);

/// Forgets every failed-start id. Called once another story has genuinely
/// started, per the "clear the exclusion after a success" rule.
void story_select_clear_failed();

/// Chooses the next story from `eligible`, honoring both the persisted
/// last-played cursor and the boot-scoped failed set. Does NOT persist
/// anything — persistence is the caller's explicit, success-gated step,
/// which also lets diagnostics run a selection without moving the
/// child's place in the rotation.
bool story_select_pick(const CsStory *eligible, int count,
                       char *out_story_id, size_t out_len);

// ---- welcome flow ---------------------------------------------------

/// Resolves EXACTLY `voice_id` to its cached device-global clip
/// (/voice/<id>-v<n>.mp3). Same never-a-different-file contract as
/// story_select_resolve_path: requires a VERIFIED index entry and the
/// file on the card at the recorded size. Returns false otherwise —
/// which the welcome flow treats as "this line does not exist yet" and
/// simply skips, so a half-synced card degrades quietly instead of
/// going silent mid-sentence.
bool voice_clip_resolve_path(const char *voice_id, char *out, size_t out_len);

/// Picks the next power-on greeting: round-robin over the index's
/// verified `voice` entries whose id starts with "greet-", skipping the
/// one stored in NVS (namespace `aregvoice`, key `last_greet`), and
/// resolves its cache path into `out_path`.
///
/// Never-repeat-the-last-one holds by construction, exactly as the story
/// and music cursors do: with one greeting it returns that one, with N it
/// returns the entry AFTER the previous, wrapping. Returns false when no
/// usable greeting exists — a toy with no greetings simply stays silent
/// at boot, which is the pre-welcome behavior.
///
/// The cursor advances on pick. A greeting is three seconds long; unlike
/// a story, a rare skipped one costs the child nothing, so this does not
/// carry the story path's started-gate.
bool voice_clip_next_greeting(char *out_path, size_t out_len);

/// One of the four parent mode switches as cached on the card (index root
/// `storyEnabled` / `gameEnabled` / `riddleEnabled` / `curiosityEnabled`),
/// selected by `mode`: 's' | 'g' | 'r' | 'c'.
///
/// Absent index, pre-v4 card, or an unrecognised letter → true, matching
/// the shipped server-side default. A toy must never silently stop
/// offering stories because its card predates this field.
///
/// These do NOT enforce anything — the backend chat gate remains the
/// enforcement point. They exist so the toy never offers a child
/// something that will then be refused.
bool story_select_mode_enabled(char mode);

// ---- "already heard" set (welcome flow) -----------------------------
//
// The toy cannot ask the server which stories a child has heard: the
// story-play report queue DELETES each event from NVS once the backend
// has accepted it, so the only surviving device-side memory is the
// single `last_id` cursor. This set is the device's own record, kept so
// the welcome flow can offer a story the child has NOT heard yet.
//
// Stored as ONE whole-set NVS blob (namespace `aregheard`), the same
// shape story_report uses for its queue — Preferences keys are capped at
// 15 characters and a key per story would multiply flash wear for no
// gain. Bounded to CS_MAX_STORIES with drop-OLDEST on overflow, so with
// a library larger than the bound it behaves as a sliding
// "recently heard" window rather than a lifetime record. That is the
// right product behavior, not a limitation: a child re-hearing a
// favourite from months ago is fine.

/// True when `story_id` is in the heard set.
bool story_heard_contains(const char *story_id);

/// Adds `story_id` to the heard set. No-op when already present (so it
/// does not re-write flash) or when the id is invalid.
///
/// Call this ONLY once playback has genuinely started, under the same
/// gate as story_select_save_last — a story that resolved but never made
/// a sound has not been heard.
void story_heard_mark(const char *story_id);

/// Number of ids currently in the heard set.
int story_heard_count();

/// Empties the heard set. Deliberately NOT called in production: when
/// every eligible story has been heard the welcome flow speaks the
/// "shall I tell it again?" line instead of quietly forgetting. Exists
/// for the bench and for a future factory-reset gesture.
void story_heard_clear();
