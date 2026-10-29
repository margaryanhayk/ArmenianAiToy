// -------------------------------------------------------------
// AregVoiceMvp / content_index_io.h
//
// ONE place that reads /content_index.json off the SD card.
//
// Why it exists (2026-10-29, bench): story-select and content-report printed
// `index parse failed: IncompleteInput` seven times in the first 30 s after a
// boot, then content-sync parsed the very same file three minutes later. A
// short read from a card is not a bad file. Each reader opened the file once,
// gave up on the first failure and reported "no cached stories", so the toy
// fell back to the Wi-Fi stream and went silent. And the previous-generation
// copy (<primary>.prev) that content sync keeps for power-loss safety was only
// ever used when the primary was MISSING, never when it was damaged.
//
// This reader retries a failed read, then falls back to the previous copy,
// and only then says the index is unreadable. The order is the pure plan in
// content_sync_rules.h (cs_index_next_step), pinned by a host test.
//
// Read-only: nothing here changes the card. Only content sync, which owns the
// writes to the index, may call content_index_repair_from_backup().
// -------------------------------------------------------------
#pragma once

#include <ArduinoJson.h>

enum ContentIndexStatus {
    CONTENT_INDEX_ABSENT = 0,      // no index on the card, or no card: not an error
    CONTENT_INDEX_OK,              // the primary parsed (possibly after a retry)
    CONTENT_INDEX_OK_FROM_BACKUP,  // the primary did not; the previous copy did
    CONTENT_INDEX_UNREADABLE       // a file is there, but no attempt parsed it
};

/// True when `doc` holds a parsed index (from either file).
inline bool content_index_ok(ContentIndexStatus s) {
    return s == CONTENT_INDEX_OK || s == CONTENT_INDEX_OK_FROM_BACKUP;
}

/// Reads and parses the card index into `doc`, which the caller built on the
/// PSRAM allocator (g_json_psram - never internal heap). `who` tags the serial
/// lines ("story-select"). Feeds the task watchdog around every attempt.
///
/// After a total failure, callers inside CS_INDEX_DEAD_MEMO_MS return
/// UNREADABLE at once instead of repeating the same slow read.
ContentIndexStatus content_index_load(JsonDocument &doc, const char *who);

/// After CONTENT_INDEX_OK_FROM_BACKUP: make the card agree with what was just
/// read by moving the previous copy into the primary's place (the damaged file
/// is removed first). If power is lost between the two steps the primary is
/// missing and the existing restore rule in content sync finishes the job.
/// Only content sync should call this. Returns true when the primary is now
/// the good copy.
bool content_index_repair_from_backup();
