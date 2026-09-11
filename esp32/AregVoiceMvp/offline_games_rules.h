// Pure decision logic for the three offline games (offline_games.cpp). No
// Arduino, no SD, no buttons — same split as online_session_rules.h and
// content_report_rules.h, and for the same reason: the two decisions below
// are small enough to hide a bug (a game offered with no clip on the card
// silently stalls; a game-honesty violation is a token the parent
// dashboard would show as a lie), so they are host-tested on plain g++
// rather than trusted to a bench session.
//
// Because this header carries no platform dependency, it compiles and runs
// on a host with plain g++.
#pragma once

// --- Round-robin selection ---------------------------------------------
//
// Games are indexed 0=mind-reader, 1=who-first (the two-player buzzer),
// 2=button-simon — the same order offline_games.cpp's game_key_for_index()
// uses, and the same three keys game-clips.json defines. `avail[i]` is
// true when game i's intro clip is verified present on THIS card; a game
// missing its intro clip must never be offered (a child asked for "game"
// and heard nothing is the exact defect the auto-start bug taught).
//
// `last_pick` is the index most recently played (persisted in NVS across
// boots), or -1 when nothing has ever been picked. Returns the next
// AVAILABLE game after last_pick in round-robin order, or -1 when no game
// on the card has its intro clip (offline_games_available() then says no).
inline int offline_games_pick_next(const bool avail[3], int last_pick) {
    if (!avail[0] && !avail[1] && !avail[2]) {
        return -1;
    }
    int idx = (last_pick >= 0 && last_pick <= 2) ? last_pick : -1;
    for (int step = 0; step < 3; step++) {
        idx = (idx + 1) % 3;
        if (avail[idx]) {
            return idx;
        }
    }
    return -1;  // unreachable given the all-false check above; kept honest
}

// --- Game-honesty outcome mapping ---------------------------------------
//
// The toy claims only what the buttons measured (CLAUDE.md § Game
// honesty). These two functions are the WHOLE of that claim for the two
// games that report an outcome; who-first (the buzzer) reports none at
// all — see AllowedOutcomes in backend GamePlayReportRequest.cs, which
// mind-reader and button-simon must stay inside.

// Mind-reader reports from AREG's own side: the toy GUESSED, so 'Y' (the
// child confirmed the guess) means the toy was right -- "won" -- and 'N'
// means the toy was wrong, which means the CHILD won -- "lost". A verdict
// the child never gave (0, e.g. the confirm window timed out twice) is
// nothing measured, so nullptr — never invent a result.
inline const char *offline_games_mindreader_outcome(char verdict) {
    if (verdict == 'Y') return "won";
    if (verdict == 'N') return "lost";
    return nullptr;
}

// Button Simon: reaching the ceiling with no wrong press is the best
// outcome the toy can report — "won". Any session that ends any other
// way it was actually scored (a wrong press, or the window closing after
// at least one echoed sequence) is "stopped": the length reached is the
// honest description, not a loss.
inline const char *offline_games_simon_outcome(bool reached_ceiling_no_miss) {
    return reached_ceiling_no_miss ? "won" : "stopped";
}
