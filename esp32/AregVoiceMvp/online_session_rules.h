#pragma once

// Pure decision logic for the online Game / Riddle / Curiosity / Calm voice
// loop (handle_online_chat_session in AregVoiceMvp.ino). No Arduino, no
// HTTP, no mic/speaker — same split content_report_rules.h and
// content_sync_rules.h use, and for the same reason: the loop itself is a
// thin shell of Serial prints and hardware calls around a handful of small
// decisions, and THOSE are what a bug hides in — ending a game the instant
// a five-year-old pauses to think, or never ending one at all.
//
// Because this header carries no platform dependency, it compiles and runs
// on a host with plain g++, so the decisions are tested without hardware.

#include <stddef.h>

// --- Turn cap --------------------------------------------------------
//
// Turns are 1-indexed at the call site (`for (int turn_no = 1; ...)`),
// matching every other counted loop in this firmware. Exactly `max_turns`
// turns are allowed to run (turn_no in [1, max_turns]); the (max_turns+1)th
// must not open another upload — the cap bounds the session's OpenAI
// cost, not just its length.
inline bool online_session_turn_cap_reached(int turn_no, int max_turns) {
    return max_turns <= 0 || turn_no > max_turns;
}

// --- Ending right after a turn's reply played -------------------------
//
// True when the session must end BEFORE ever opening a listen window:
//   - the upload failed (upload_ok == false) — the child heard the canned
//     failure clip; there is nothing to listen for.
//   - the backend's own X-Areg-Turn-End header said this turn closed the
//     conversation — a Game round the child stopped with a stop word, or
//     ANY parent-gate canned clip (unclaimed / paused / bedtime /
//     mode-disabled / cost-cap). Nobody can "answer" a goodbye or a
//     gate reply with a next turn, so listening for one would just
//     stall until the window times out.
inline bool online_session_ends_after_reply(bool upload_ok, bool turn_ended_header) {
    return !upload_ok || turn_ended_header;
}

// --- Silence bookkeeping ----------------------------------------------
//
// Called once per listen window with whether a press was captured.
// Returns the UPDATED consecutive-silence count: any press resets it to
// zero, a silent window increments it. The caller compares the result
// against online_session_should_give_up_on_silence()'s threshold.
inline int online_session_note_listen_result(int prior_consecutive_silence, bool got_press) {
    if (got_press) {
        return 0;
    }
    return prior_consecutive_silence + 1;
}

// True once enough CONSECUTIVE silent windows have piled up that the
// session should close quietly rather than keep listening. The default
// of two (not one) mirrors the rest of this firmware's own "ask twice"
// pattern (AREG_WELCOME_MAX_TRIES in the welcome flow) — a single
// unanswered window is a child pausing to think, not a child who left
// the room.
inline bool online_session_should_give_up_on_silence(
        int consecutive_silence, int max_consecutive_silence) {
    return consecutive_silence >= max_consecutive_silence;
}
