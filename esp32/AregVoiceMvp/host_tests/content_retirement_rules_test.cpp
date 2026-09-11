// Host test for the content-retirement / orphan-sweep decision logic.
// Runs with plain g++ — no Arduino core, no board, no cable:
//
//     g++ -std=c++17 -Wall -Wextra -o /tmp/cr_test
//         esp32/AregVoiceMvp/host_tests/content_retirement_rules_test.cpp && /tmp/cr_test
//
//   (one line; split here only so this comment stays a comment)
//
// It compiles the REAL header (content_retirement_rules.h) — a copy of
// the functions would only ever prove the copy right.
//
// What it does NOT cover: the actual SD.remove() calls, the manifest
// parse that populates a "retired this round" id list, or the file
// enumeration a real orphan sweep walks. Those need hardware; this
// covers only the three yes/no decisions content_sync.cpp makes around
// them.
#include "../content_retirement_rules.h"
#include <cstdio>

static int failures = 0;
static void check(bool ok, const char *what) {
    printf("  %-4s %s\n", ok ? "ok" : "FAIL", what);
    if (!ok) failures++;
}

int main() {
    // --- content_retirement_should_delete ---
    check(!content_retirement_should_delete(/*is_retired=*/false, /*is_paused_here=*/false),
          "not retired, not paused: never delete");
    check(!content_retirement_should_delete(false, true),
          "not retired, paused: never delete (nothing to retire anyway)");
    check(content_retirement_should_delete(/*is_retired=*/true, /*is_paused_here=*/false),
          "retired and not the paused story: delete");
    // KEYSTONE: a retired story the child paused mid-way through is
    // spared, or resume breaks and the playback position is orphaned.
    check(!content_retirement_should_delete(/*is_retired=*/true, /*is_paused_here=*/true),
          "retired but paused mid-way RIGHT NOW: spared, not deleted");

    // --- content_orphan_sweep_budget_reached ---
    check(!content_orphan_sweep_budget_reached(0, 5), "0 of 5 removed: budget open");
    check(!content_orphan_sweep_budget_reached(4, 5), "4 of 5 removed: budget open");
    check(content_orphan_sweep_budget_reached(5, 5), "5 of 5 removed: budget reached");
    check(content_orphan_sweep_budget_reached(6, 5), "past the cap: still reached");
    // KEYSTONE: 0 (or negative) must read as "the sweep is off", not
    // "unbounded" — a misconfigured/default cap fails CLOSED.
    check(content_orphan_sweep_budget_reached(0, 0), "zero budget fails closed (sweep off)");
    check(content_orphan_sweep_budget_reached(0, -1), "negative budget fails closed");

    // --- content_orphan_part_file_is_stale ---
    check(content_orphan_part_file_is_stale(/*file_mtime=*/1000, /*boot_start=*/2000),
          "file written before this boot started: stale");
    check(!content_orphan_part_file_is_stale(/*file_mtime=*/2500, /*boot_start=*/2000),
          "file written after this boot started: not stale (this boot's own download)");
    check(!content_orphan_part_file_is_stale(/*file_mtime=*/2000, /*boot_start=*/2000),
          "exactly at boot start: not proven older, so not stale");
    // KEYSTONE: no trustworthy clock (NTP not landed yet) must never be
    // read as "definitely old" — fails CLOSED, the file is left alone.
    check(!content_orphan_part_file_is_stale(/*file_mtime=*/0, /*boot_start=*/2000),
          "file mtime unknown (<=0): fails closed, never swept");
    check(!content_orphan_part_file_is_stale(/*file_mtime=*/1000, /*boot_start=*/0),
          "boot start unknown (<=0): fails closed, never swept");
    check(!content_orphan_part_file_is_stale(-5, -5),
          "both unknown: fails closed");

    printf(failures == 0 ? "PASS\n" : "FAIL (%d)\n", failures);
    return failures == 0 ? 0 : 1;
}
