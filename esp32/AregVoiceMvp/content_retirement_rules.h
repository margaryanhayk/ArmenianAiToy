// -------------------------------------------------------------
// AregVoiceMvp / content_retirement_rules.h — pure retirement + orphan-
// sweep decisions for content_sync.cpp (2026-09-11).
//
// No Arduino/SD/JSON dependency, exactly like device_creds_rules.h and
// online_session_rules.h — host-testable with plain g++.
//
// Absence from the manifest, and enabled:false, both already mean "not
// offered", and something "not offered" is carried forward on the card
// FOREVER (content_sync.cpp's carry-forward loops) — deliberately, since
// one manifest response is not a retirement instruction (CLAUDE.md).
// retired:true is the one signal that IS an instruction: drop the index
// entry (content_sync.cpp) and, once safe, delete the cached file too.
// -------------------------------------------------------------
#pragma once

#include <stddef.h>

// Should a previously-cached item whose id the manifest marked
// retired:true be deleted THIS sync attempt?
//
// Not when it is the story the child paused mid-way through right now:
// deleting it would break resume and orphan the playback position for a
// session that is still live. The next sync attempt — after the session
// ends and the offset is back to zero — retires it safely. Only stories
// can be "paused mid-way"; every other namespace passes false and always
// retires immediately (content_sync only ever runs from ST_IDLE, so
// "never inside a session" already holds structurally there).
inline bool content_retirement_should_delete(bool is_retired, bool is_paused_here) {
    return is_retired && !is_paused_here;
}

// Orphan-sweep budget: never remove more than `max_per_boot` files in one
// boot, however many unreferenced files are found. A bug that flags
// everything as orphaned can, at worst, chew through one bounded slice
// of the card per boot — never wipe it in a single pass. 0 (or negative)
// disables the sweep entirely — the feature ships off until an operator
// opts in, the same posture as RetentionPurgeService's dormancy passes.
inline bool content_orphan_sweep_budget_reached(int removed_so_far, int max_per_boot) {
    if (max_per_boot <= 0) return true;
    return removed_so_far >= max_per_boot;
}

// A ".part" temp download left over from a run that never finished
// (crash, panic, power loss) is only safe to reclaim once it is
// definitely not the CURRENT boot's own in-flight download — sweeping a
// live temp file out from under an active transfer would corrupt it.
//
// The firmware has no monotonic identity for "this boot" that survives a
// reboot, but it does have the SD card's own file timestamp once the
// clock is set (TLS to the backend already requires correct wall-clock
// time, so by the time a sync attempt runs, NTP has long since landed).
// A `.part` file is "older than one boot" exactly when its last-write
// time is BEFORE this boot started: it was already sitting there when
// the current boot's own downloads began, so nothing this boot could
// have written it.
//
// Either timestamp reading before the epoch (<= 0) means the clock is
// not trustworthy yet — that is written UNKNOWN, not "recent", and this
// fails CLOSED: never mistaken for stale, never swept.
inline bool content_orphan_part_file_is_stale(long file_mtime_epoch,
                                               long boot_start_epoch) {
    if (file_mtime_epoch <= 0 || boot_start_epoch <= 0) return false;
    return file_mtime_epoch < boot_start_epoch;
}
