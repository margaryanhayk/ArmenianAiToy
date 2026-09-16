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

    printf(failures == 0 ? "PASS\n" : "FAIL (%d)\n", failures);
    return failures == 0 ? 0 : 1;
}
