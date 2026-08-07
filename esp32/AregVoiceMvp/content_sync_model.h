// -------------------------------------------------------------
// AregVoiceMvp / content_sync_model.h — JSON <-> CsStory conversion for
// Cloud→SD multi-story sync.
//
// The middle layer between content_sync_rules.h (pure, no dependencies)
// and content_sync.cpp (SD + HTTP). Everything here reads or writes an
// in-memory ArduinoJson document and NEVER touches the card, the
// network, or any global state — so the bench harness
// (content_sync_test.cpp) can round-trip the manifest and index schemas
// with no hardware attached.
//
// Serial logging IS used for per-item reject reasons: it is the bench
// evidence channel and is available wherever this compiles.
// -------------------------------------------------------------
#pragma once

#include <ArduinoJson.h>

#include "content_sync_rules.h"

/// Counters for one manifest parse. `accepted` is how many entries were
/// written to the output table; the rest are diagnostics for the
/// aggregate summary line.
struct CsManifestStats {
    int offered;     ///< items the backend sent
    int accepted;    ///< valid, non-duplicate, enabled items stored
    int invalid;     ///< rejected for a field-level reason
    int duplicate;   ///< same storyId as an earlier accepted item
    int disabled;    ///< enabled:false (retirement is NOT implemented)
    int truncated;   ///< offered beyond CS_MAX_STORIES, never examined
};

/// Parses `stories` into `out`, in manifest order, stopping at
/// `max_out`. Every item is validated independently: one bad item is
/// skipped and counted, never fatal to its siblings. Returns the number
/// of accepted items (also in stats->accepted).
///
/// Rejects an item when storyId fails the allowlist, audioUrl is empty
/// or too long, sha256 is not 64 hex chars, sizeBytes is out of range,
/// the cache path would not fit, or the id duplicates an earlier one.
int cs_manifest_parse(JsonArrayConst stories, CsStory *out, int max_out,
                      CsManifestStats *stats);

/// Reads an index document into `out`. Understands the v2 stories[]
/// shape and MIGRATES the pre-multi-story flat v1 object. Returns the
/// number of entries; writes the detected schema version to
/// *out_schema when non-null. A malformed or unrecognized document
/// yields 0 and is never an error — the caller keeps every cached file.
int cs_index_parse(JsonDocument &doc, CsStory *out, int max_out, int *out_schema);

/// Builds the v3 index document from `active` (stories[] with per-story
/// clips[], plus the root `introEnabled` flag the parent toggle caches
/// on the card).
///
/// Also writes the LEGACY COMPATIBILITY MIRROR (flat storyId/version/
/// sha256/file/sizeBytes) pointing at the entry whose id equals
/// `mirror_story_id`, or the first entry when that id is absent. Three
/// readers still parse the flat shape — see content_sync.h — and this
/// slice must not change playback behavior. Pass nullptr to omit the
/// mirror entirely.
void cs_index_build(JsonDocument &doc, const CsStory *active, int count,
                    const char *mirror_story_id, bool intro_enabled = true);

/// Reads the root `introEnabled` flag from an index document. Absent
/// (every pre-v3 card) → true, the shipped default.
bool cs_index_intro_enabled(JsonDocument &doc);

/// Slice E — parses the manifest's `music` array (validated per item,
/// dedup keeps first). Returns the accepted count.
int cs_manifest_parse_music(JsonArrayConst music, CsMusic *out, int max_out);

/// Slice E — reads the index's `music` array (absent on pre-music cards
/// → 0, never an error).
int cs_index_parse_music(JsonDocument &doc, CsMusic *out, int max_out);

/// Slice E — appends the music section + the root `musicEnabled` flag to
/// an index document ALREADY built by cs_index_build. Separate call so
/// cs_index_build's signature (and its existing callers/tests) stay
/// untouched.
void cs_index_add_music(JsonDocument &doc, const CsMusic *music, int count,
                        bool music_enabled);

/// Reads the root `musicEnabled` flag (absent → false; music is opt-in).
bool cs_index_music_enabled(JsonDocument &doc);

// ---- welcome flow (index schema v4) --------------------------------

/// Parses the manifest's `voice` array — the device-global spoken clips
/// (greetings, menu prompts, fallback lines). Validated per item, dedupe
/// keeps first. Returns the accepted count.
int cs_manifest_parse_voice(JsonArrayConst voice, CsVoice *out, int max_out);

/// Reads the index's `voice` array (absent on every pre-v4 card → 0,
/// never an error).
int cs_index_parse_voice(JsonDocument &doc, CsVoice *out, int max_out);

/// Appends the voice section to an index document ALREADY built by
/// cs_index_build. Separate call for the same reason cs_index_add_music
/// is separate: cs_index_build's signature, callers and tests stay
/// untouched.
void cs_index_add_voice(JsonDocument &doc, const CsVoice *voice, int count);

/// Appends the four parent mode flags to an index document. Cached on the
/// card so the "what shall we do?" prompt offers only permitted modes even
/// with no network.
void cs_index_add_modes(JsonDocument &doc, bool story, bool game,
                        bool riddle, bool curiosity);

/// Reads one root mode flag by its key ("storyEnabled", "gameEnabled",
/// "riddleEnabled", "curiosityEnabled"). Absent (every pre-v4 card) →
/// true, matching the shipped server-side default; a toy must never
/// silently stop offering stories because its card predates this field.
bool cs_index_mode_enabled(JsonDocument &doc, const char *key);

// ---- story feature toggles (index schema v6) ------------------------

/// Appends the two parent story-feature flags — in-story pauses and
/// variant endings — to an index document ALREADY built by
/// cs_index_build. A separate call for the same reason
/// cs_index_add_music / cs_index_add_modes are separate: cs_index_build's
/// signature, callers and tests stay untouched.
void cs_index_add_story_flags(JsonDocument &doc, bool pauses, bool variants);

/// Reads the root `pausesEnabled` flag. Absent (every pre-v6 card) → true,
/// the shipped default.
bool cs_index_pauses_enabled(JsonDocument &doc);

/// Reads the root `variantsEnabled` flag. Absent (every pre-v6 card) →
/// true, the shipped default. Harmless on a card with no alternate
/// endings cached: nothing resolves, so the base narration plays.
bool cs_index_variants_enabled(JsonDocument &doc);

// ---- offline-game clips (index schema v7) ---------------------------
//
// Item-at-a-time, unlike every other namespace here. The stories / music /
// voice helpers fill a TABLE; these fill ONE CsGame, because ~90 game clips
// would not fit in .bss three times over. See the CS_MAX_GAMES comment in
// content_sync_rules.h for the numbers.

/// Validates ONE manifest `games[]` item into `out`. Returns false (and
/// logs the reject reason) for a disabled item, an id failing the
/// allowlist on either half of the pair, a bad hash, or an out-of-range
/// size. Never partially fills `out`.
bool cs_manifest_read_game(JsonObjectConst item, CsGame *out);

/// Validates ONE index `games[]` entry into `out`. Same allowlist and hash
/// rules as the manifest reader — a card can be hand-edited, so nothing is
/// trusted just because it was already written.
bool cs_index_read_game(JsonObjectConst entry, CsGame *out);

/// Appends one clip to an index `games` array. The CsGame's char ARRAYS
/// are what make ArduinoJson COPY the strings rather than link to them —
/// do not "simplify" this to take const char* parameters.
void cs_index_append_game(JsonArray arr, const CsGame *game);

/// Attaches a prepared `games` array (built with cs_index_append_game) to
/// an index document ALREADY built by cs_index_build. Separate call for the
/// same reason cs_index_add_music / cs_index_add_voice are separate.
/// A null or empty array writes nothing, so a deployment with no game clips
/// produces an index byte-identical to the pre-v7 one apart from the schema
/// number.
void cs_index_add_games(JsonDocument &doc, JsonArrayConst games);
