// Host test for the one verdict a content-sync attempt reports (added
// 2026-10-27, AI logic review F6). Runs with plain g++ — no Arduino core:
//
//     g++ -std=c++17 -Wall -Wextra -o /tmp/csv_test
//         esp32/AregVoiceMvp/host_tests/content_sync_verdict_rules_test.cpp && /tmp/csv_test
//
// It compiles the REAL header (content_sync_rules.h). What it does NOT
// cover: that content_sync.cpp actually counts every namespace's item
// failures into the `item_failures` argument (Arduino-only file; the
// bench step is "block one voice clip URL, expect status=partial and
// next in 300s").
#include "../content_sync_rules.h"
#include <cstdio>
#include <cstring>

static int failures = 0;
static void check(bool ok, const char *what) {
    printf("  %-4s %s\n", ok ? "ok" : "FAIL", what);
    if (!ok) failures++;
}
static bool is(const char *verdict, const char *want) { return strcmp(verdict, want) == 0; }

int main() {
    check(is(cs_sync_verdict(false, 0, 0, true), "ok"),
          "nothing failed, index written: ok");
    check(is(cs_sync_verdict(false, 1, 0, true), "partial"),
          "one story narration failed: partial");
    check(is(cs_sync_verdict(false, 0, 1, true), "partial"),
          "stories fine, ONE clip/voice/music/game item failed: partial (was 'ok' before F6)");
    check(is(cs_sync_verdict(false, 0, 78, true), "partial"),
          "the 2026-08-07 field case (78 game clips failed, all stories fine): partial");
    check(is(cs_sync_verdict(false, 0, 0, false), "failed"),
          "everything downloaded but the index would not write: failed");
    check(is(cs_sync_verdict(false, 2, 3, false), "partial"),
          "item failures AND no index: partial still reported (the reasons were recorded per item)");
    check(is(cs_sync_verdict(true, 0, 0, true), "failed"),
          "a whole-attempt failure outranks a clean item count");
    check(is(cs_sync_verdict(true, 1, 5, false), "failed"),
          "a whole-attempt failure outranks everything");
    printf(failures == 0 ? "PASS\n" : "FAIL (%d)\n", failures);
    return failures == 0 ? 0 : 1;
}
