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
