// Host test for the online Game/Riddle/Curiosity/Calm voice-loop decision
// logic. Runs with plain g++ — no Arduino core, no board, no cable:
//
//     g++ -std=c++17 -Wall -Wextra -o /tmp/os_test
//         esp32/AregVoiceMvp/host_tests/online_session_rules_test.cpp && /tmp/os_test
//
//   (one line; split here only so this comment stays a comment)
//
// It compiles the REAL header (online_session_rules.h) — a copy of the
// functions would only ever prove the copy right.
//
// What it does NOT cover: the HTTP upload, the mic capture, the MP3
// decode, or the button poll itself. Those need hardware; this covers only
// the decisions handle_online_chat_session makes around them.
#include "../online_session_rules.h"
#include <cstdio>

static int failures = 0;
static void check(bool ok, const char *what) {
    printf("  %-4s %s\n", ok ? "ok" : "FAIL", what);
    if (!ok) failures++;
}

int main() {
    // --- Turn cap --- (all 12 turns of a 12-turn cap may run; the 13th may not)
    check(!online_session_turn_cap_reached(1, 12), "turn 1 of 12 not capped");
    check(!online_session_turn_cap_reached(11, 12), "turn 11 of 12 not capped");
    check(!online_session_turn_cap_reached(12, 12), "turn 12 of 12 still allowed to run");
    check(online_session_turn_cap_reached(13, 12), "turn 13 of 12 IS capped");
    check(online_session_turn_cap_reached(14, 12), "past the cap stays capped");
    // KEYSTONE: max_turns <= 0 must never be read as "unbounded" — a
    // misconfigured cap fails CLOSED (ends immediately), not open (an
    // unmetered session).
    check(online_session_turn_cap_reached(1, 0), "zero cap fails closed");
    check(online_session_turn_cap_reached(1, -1), "negative cap fails closed");

    // --- Ending right after a turn's reply ---
    check(online_session_ends_after_reply(/*upload_ok=*/false, /*turn_ended=*/false),
          "upload failure always ends the session");
    check(online_session_ends_after_reply(/*upload_ok=*/true, /*turn_ended=*/true),
          "server-declared turn end ends the session");
    check(online_session_ends_after_reply(/*upload_ok=*/false, /*turn_ended=*/true),
          "both flags set still ends the session");
    check(!online_session_ends_after_reply(/*upload_ok=*/true, /*turn_ended=*/false),
          "an ordinary successful turn keeps going");

    // --- Silence bookkeeping ---
    check(online_session_note_listen_result(0, /*got_press=*/true) == 0,
          "a press resets the count to zero");
    check(online_session_note_listen_result(1, /*got_press=*/true) == 0,
          "a press resets even from a nonzero count");
    check(online_session_note_listen_result(0, /*got_press=*/false) == 1,
          "first silent window counts to one");
    check(online_session_note_listen_result(1, /*got_press=*/false) == 2,
          "second CONSECUTIVE silent window counts to two");

    check(!online_session_should_give_up_on_silence(0, 2), "zero silent windows: keep listening");
    check(!online_session_should_give_up_on_silence(1, 2),
          "ONE silent window must NOT end the session — a child pausing to "
          "think is not a child who left the room");
    check(online_session_should_give_up_on_silence(2, 2),
          "two consecutive silent windows: give up");
    check(online_session_should_give_up_on_silence(3, 2), "past the threshold still gives up");

    // A press between two silences resets the run, so it never accumulates
    // across an answered turn — exactly how the caller drives the counter
    // turn to turn.
    {
        int consecutive = 0;
        consecutive = online_session_note_listen_result(consecutive, false); // 1
        consecutive = online_session_note_listen_result(consecutive, true);  // reset
        check(consecutive == 0, "a press mid-run resets the streak");
        check(!online_session_should_give_up_on_silence(consecutive, 2),
              "so the session does not end on an answered turn");
    }

    printf(failures == 0 ? "PASS\n" : "FAIL (%d)\n", failures);
    return failures == 0 ? 0 : 1;
}
