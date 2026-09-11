// Host test for the offline-games decision logic. Runs with plain g++ — no
// Arduino core, no board, no cable:
//
//     g++ -std=c++17 -Wall -Wextra -o /tmp/og_test
//         esp32/AregVoiceMvp/host_tests/offline_games_rules_test.cpp && /tmp/og_test
//
//   (one line; split here only so this comment stays a comment)
//
// It compiles the REAL header (offline_games_rules.h) — a copy of the
// functions would only ever prove the copy right.
//
// What it does NOT cover: the SD clip check, the answer-button poll, or
// the NVS read/write around the persisted cursor. Those need hardware;
// this covers only the decisions offline_games.cpp makes around them.
#include "../offline_games_rules.h"
#include <cstdio>
#include <cstring>

static int failures = 0;
static void check(bool ok, const char *what) {
    printf("  %-4s %s\n", ok ? "ok" : "FAIL", what);
    if (!ok) failures++;
}

int main() {
    // --- Round-robin selection ---
    {
        const bool all[3] = {true, true, true};
        check(offline_games_pick_next(all, -1) == 0,
              "never played before -> starts at mind-reader (0)");
        check(offline_games_pick_next(all, 0) == 1,
              "after mind-reader -> who-first (1)");
        check(offline_games_pick_next(all, 1) == 2,
              "after who-first -> button-simon (2)");
        check(offline_games_pick_next(all, 2) == 0,
              "after button-simon -> wraps to mind-reader (0)");
    }
    {
        const bool none[3] = {false, false, false};
        check(offline_games_pick_next(none, -1) == -1,
              "no game has its intro clip -> nothing to offer");
        check(offline_games_pick_next(none, 0) == -1,
              "still nothing to offer even with a stale last-pick");
    }
    {
        // KEYSTONE: a game with no intro clip on the card must NEVER be
        // picked — the exact "offered a game, heard nothing" defect.
        const bool only_simon[3] = {false, false, true};
        check(offline_games_pick_next(only_simon, -1) == 2,
              "only button-simon has a clip -> picks button-simon");
        check(offline_games_pick_next(only_simon, 2) == 2,
              "the only available game rotates back to itself, not a missing one");
    }
    {
        const bool mindreader_and_simon[3] = {true, false, true};
        check(offline_games_pick_next(mindreader_and_simon, 0) == 2,
              "who-first missing -> skips straight from mind-reader to button-simon");
        check(offline_games_pick_next(mindreader_and_simon, 2) == 0,
              "wraps from button-simon back to mind-reader, skipping who-first");
    }
    {
        // A last-pick outside [0,2] (corrupt NVS, a hand-written key) must
        // be treated exactly like "never played" — never crash, never pick
        // a phantom fourth game.
        const bool all[3] = {true, true, true};
        check(offline_games_pick_next(all, 99) == 0,
              "out-of-range last-pick is treated as unknown");
        check(offline_games_pick_next(all, -5) == 0,
              "negative-but-not-(-1) last-pick is treated as unknown");
    }

    // --- Mind-reader outcome (toy's own perspective) ---
    check(strcmp(offline_games_mindreader_outcome('Y'), "won") == 0,
          "child confirms the guess -> the toy won");
    check(strcmp(offline_games_mindreader_outcome('N'), "lost") == 0,
          "child rejects the guess -> the toy lost (the CHILD won)");
    check(offline_games_mindreader_outcome(0) == nullptr,
          "no verdict measured -> nothing claimed");
    check(offline_games_mindreader_outcome('Q') == nullptr,
          "an unrecognised value -> nothing claimed, never guessed at");

    // --- Button Simon outcome ---
    check(strcmp(offline_games_simon_outcome(true), "won") == 0,
          "reached the ceiling with no miss -> won");
    check(strcmp(offline_games_simon_outcome(false), "stopped") == 0,
          "anything else scored -> stopped, never a loss");

    printf(failures == 0 ? "PASS\n" : "FAIL (%d)\n", failures);
    return failures == 0 ? 0 : 1;
}
