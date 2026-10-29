// Host test for the content-index backup/restore decision logic added
// 2026-09-14 (P2 — content index publish has a power-loss gap). Runs with
// plain g++ — no Arduino core, no board, no cable, no SD card:
//
//     g++ -std=c++17 -Wall -Wextra -o /tmp/csidx_test
//         esp32/AregVoiceMvp/host_tests/content_sync_index_rules_test.cpp && /tmp/csidx_test
//
//   (one line; split here only so this comment stays a comment)
//
// It compiles the REAL header (content_sync_rules.h) — a copy of the
// function would only ever prove the copy right.
//
// What it does NOT cover: the actual SD.rename()/SD.remove() calls in
// write_index() and load_previous_index() (content_sync.cpp), or a real
// power loss on hardware. It covers only the one yes/no decision those two
// functions make around the primary/backup index files.
#include "../content_sync_rules.h"
#include <cstdio>

static int failures = 0;
static void check(bool ok, const char *what) {
    printf("  %-4s %s\n", ok ? "ok" : "FAIL", what);
    if (!ok) failures++;
}

int main() {
    // --- cs_index_should_restore_backup ---
    check(!cs_index_should_restore_backup(/*primary_exists=*/true, /*backup_exists=*/true),
          "both present (steady state after a completed publish): no restore");
    check(!cs_index_should_restore_backup(/*primary_exists=*/true, /*backup_exists=*/false),
          "primary present, no backup yet (very first sync ever): no restore");
    // KEYSTONE: this is the exact power-loss shape write_index() can leave
    // behind — crashed between renaming the outgoing primary to the backup
    // path and renaming the verified .new file into the primary's path.
    check(cs_index_should_restore_backup(/*primary_exists=*/false, /*backup_exists=*/true),
          "primary missing, backup present: RESTORE (the power-loss gap this closes)");
    // Neither file exists (a genuinely fresh card, or the backup restore
    // itself was unavailable): nothing to restore FROM — falls through to
    // the pre-existing "no existing index" path, not a crash.
    check(!cs_index_should_restore_backup(/*primary_exists=*/false, /*backup_exists=*/false),
          "neither present: no restore (nothing to restore from)");

    // --- cs_index_next_step: retry, then the previous copy (2026-10-29) ---
    // The bench toy saw seven IncompleteInput failures in 30 s, then read the
    // same file fine. A short read is not a bad file: retry, then fall back.
    const int N = CS_INDEX_READ_TRIES;
    check(N >= 2, "a failed read is retried at least once");
    check(CS_INDEX_RETRY_GAP_MS * (N - 1) < 1000,
          "the settle time between attempts adds under a second to a healthy-but-slow read");
    check(cs_index_next_step(true, 0, true, 0) == CS_INDEX_STEP_READ_PRIMARY,
          "primary present, nothing tried yet: read the primary");
    check(cs_index_next_step(true, 1, true, 0) == CS_INDEX_STEP_READ_PRIMARY,
          "primary failed once: try it again before giving up on it");
    check(cs_index_next_step(true, N - 1, true, 0) == CS_INDEX_STEP_READ_PRIMARY,
          "primary failed all but one attempt: still its last try");
    // KEYSTONE: a damaged-but-present primary with an intact previous copy.
    check(cs_index_next_step(true, N, true, 0) == CS_INDEX_STEP_READ_BACKUP,
          "primary failed every attempt, backup present: READ THE BACKUP (the gap this closes)");
    check(cs_index_next_step(true, N, true, 1) == CS_INDEX_STEP_READ_BACKUP,
          "backup failed once: it gets its retries too");
    check(cs_index_next_step(true, N, true, N) == CS_INDEX_STEP_UNREADABLE,
          "both files failed every attempt: UNREADABLE (not 'absent' - something is on the card)");
    check(cs_index_next_step(true, N, false, 0) == CS_INDEX_STEP_UNREADABLE,
          "primary failed every attempt and there is no backup: UNREADABLE");
    // The power-loss window the old restore rule handles for the sync: readers
    // may now read straight through to the backup too.
    check(cs_index_next_step(false, 0, true, 0) == CS_INDEX_STEP_READ_BACKUP,
          "primary missing, backup present: read the backup");
    check(cs_index_next_step(false, 0, true, N) == CS_INDEX_STEP_UNREADABLE,
          "primary missing and the backup unreadable: UNREADABLE");
    check(cs_index_next_step(false, 0, false, 0) == CS_INDEX_STEP_ABSENT,
          "neither file: ABSENT (a fresh card is not an error)");
    // The plan always terminates, and spends exactly N attempts on each file
    // that exists, whatever the card does.
    bool exact = true;
    for (int pe = 0; pe < 2; pe++) for (int be = 0; be < 2; be++) {
        int pf = 0, bf = 0, steps = 0;
        CsIndexStep last = CS_INDEX_STEP_READ_PRIMARY;
        for (;;) {
            last = cs_index_next_step(pe, pf, be, bf);
            if (last == CS_INDEX_STEP_ABSENT || last == CS_INDEX_STEP_UNREADABLE) break;
            if (last == CS_INDEX_STEP_READ_PRIMARY) pf++; else bf++;   // every attempt fails
            if (++steps > 2 * N) { exact = false; break; }
        }
        if (steps != (pe ? N : 0) + (be ? N : 0)) exact = false;
        if (last != ((pe || be) ? CS_INDEX_STEP_UNREADABLE : CS_INDEX_STEP_ABSENT)) exact = false;
    }
    check(exact, "with every read failing, the plan spends exactly N attempts per file that exists, then stops");

    printf(failures == 0 ? "PASS\n" : "FAIL (%d)\n", failures);
    return failures == 0 ? 0 : 1;
}
