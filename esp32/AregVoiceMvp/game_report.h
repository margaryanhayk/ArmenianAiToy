// -------------------------------------------------------------
// AregVoiceMvp / game_report.h — offline game-play reporting
// (store-and-forward)
//
// WHY: the three offline games run entirely from SD clips and the answer
// buttons and make NO network call of their own, so without this a child
// could play all afternoon and the parent dashboard would show nothing —
// the same gap story_report.h closes for SD story playback, and the same
// fix: queue one tiny event per session in NVS, upload the queue to
// POST /api/devices/game-plays whenever Wi-Fi is up, delete an event only
// after a 2xx.
//
// UNLIKE stories, a game session never pauses and resumes across a
// power cycle — offline_games.cpp's run_* functions run one session
// start-to-finish inside a single call and return. So there is no
// started/finished pair to track: a session enqueues ALREADY CLOSED, the
// moment it ends, or not at all if nothing was actually measured (see the
// call sites in offline_games.cpp for what counts as "nothing measured").
//
// HONESTY: every field this module can carry is a count, a bounded
// outcome token, or a timestamp (CLAUDE.md § Game honesty). There is no
// free-text field and never will be — these games have no microphone.
//
// TRANSPORT: at-least-once, same idempotency-key shape as story_report
// ("b<boot>-<n>"). Same Preferences/NVS idiom as wifi_creds / device_creds
// / ota_state / story_report (own namespace, keys ≤ 15 chars).
// -------------------------------------------------------------
#pragma once

#include <Arduino.h>

// Enqueue one finished session. `game_key` is one of the offline-game SD
// directory names ("mind-reader" / "who-first" / "button-simon" — the
// same keys backend GamePlayReportRequest.AllowedGameKeys uses).
//
// `rounds` / `score` are negative to mean "not measured" (omitted from
// the upload, never sent as 0 — zero is a real count for some games).
// `outcome` is nullptr to mean "the toy claims no outcome" (who-first
// ALWAYS passes nullptr here — see offline_games_rules.h) or one of
// "won" / "lost" / "stopped".
void game_report_on_finished(const char *game_key, int rounds,
                              const char *outcome, int score);

// Idle-loop cadence: upload queued events when Wi-Fi is up. Prompt (a few
// seconds) after a session closes; otherwise every AREG_HEARTBEAT_INTERVAL_MS
// while the queue is non-empty. Deletes uploaded events only on a 2xx.
// Never blocks a voice turn or a game — call from the IDLE branch only,
// same as story_report_tick().
void game_report_tick();
