// Three offline SD-card games (bench scaffold — AREG_OFFLINE_GAMES_BENCH).
//
// Siblings of offline_quiz.{h,cpp}: no Wi-Fi, no STT, no model, no mic —
// pre-rendered Armenian MP3s on the card, the GREEN/RED answer buttons,
// and zero latency. Same loop discipline as the quiz (clip → answer
// window → feedback → re-ask ONCE → quiet exit; never badger).
//
// The Armenian source for every clip below is
// backend/content/offline-games/game-clips.json — that file's `id` scheme
// IS the contract this module resolves against. Nothing here invents an
// id; a clip that is not on the card is a logged no-op (the game degrades,
// it never crashes).
//
// SD layout
//   <AREG_GAMES_CLIP_DIR>/<game-key>/<clip-id>.mp3
//   e.g. /games/mind-reader/q-root.mp3, /games/who-first/win-green.mp3
//
//   The per-game subdirectory is NOT cosmetic: four of the five games in
//   game-clips.json each define a clip called `intro`, so a flat
//   /games/<id>.mp3 layout would collide. The game key is the JSON's own
//   top-level key, so no new naming vocabulary is introduced. Both the
//   root and the subdir names are #defines (config.h-overridable) so the
//   layout can move without touching this code.
//
// The three games
//
//   1. MIND-READER  («Ես կգուշակեմ») — the CHILD thinks of one of 16
//      animals; the toy walks a 4-deep yes/no tree and guesses.
//      GREEN/yes appends '1', RED/no appends '0'. A node's clip id is the
//      path so far (q-root, q-1, q-10, q-101), a guess leaf's id is the
//      full 4-bit path (g-1010). The tree is IMPLICIT in the ids — there
//      is no table in RAM, only a 5-byte path string.
//      Confirm window after the guess: GREEN = right → win, RED = wrong →
//      lose (the toy losing is delightful; see the clip text).
//
//   2. TWO-PLAYER BUZZER («Ո՞վ առաջինը») — a question clip from the
//      EXISTING /quiz bank plays, then `go`; the FIRST press inside the
//      window takes the round and that colour's celebration plays.
//      The round is about SPEED, not correctness: nothing is verified
//      against the quiz clip's answer suffix and no answer is revealed.
//      5 rounds → end-both → close.
//
//   3. BUTTON SIMON («Կրկնի՛ր իմ հետևից») — the toy plays a tone
//      sequence (GREEN tone / RED tone), the child echoes it on the
//      buttons. Length grows 2 → AREG_SIMON_MAX_LEN; any wrong press
//      ends the session with the warm `miss` clip and the reached length
//      is the result. Within-session ramp ONLY — nothing is persisted,
//      so a new session always starts at length 2 (explicit product rule).
//
// Honesty rules these games are written to (game-clips.json `_toneRules`):
//   - The toy addresses COLOURS («կանաչ» / «կարմիր»), never a child. This
//     module therefore has no notion of player identity at all — the
//     buzzer knows only which BUTTON was pressed first.
//   - No clip announces a loser, and this code never selects one.
//   - The toy claims only what the buttons measured. The mic is off in
//     every game here, so nothing may claim to have heard the child.
//
// Bench-only: production builds compile ZERO bytes of this, exactly like
// offline_quiz. Requires the answer buttons; a build with the flag but no
// AREG_PIN_BUTTON_YES / _NO pins logs once and does nothing.
#pragma once

// PROMOTED TO PRODUCTION 2026-08-19 (owner decision, menu redesign):
// the engine below compiles into EVERY build, because "game" in the toy's
// menu now plays one of these for real instead of promising a game and
// telling a story. Only offline_games_tick -- the bench-only 30-second
// AUTO-START that once made the toy take itself over after every boot and
// eat the main button for minutes (see button-dead-diagnosis-20260818.md)
// -- stays behind the bench flag. A game must start because a child asked,
// never because a timer fired.

#ifdef AREG_OFFLINE_GAMES_BENCH
// One game per boot, 30 s after boot, called from the IDLE branch of
// loop() — the same shape as offline_quiz_tick / sd_playback_tick. Which
// game runs is chosen at BUILD time by AREG_OFFLINE_GAMES_PICK (see
// offline_games.cpp). Runtime selection UX is a bench-day decision — see
// the TODO in the .cpp; deliberately NOT invented here, because it would
// need a new input gesture or LED vocabulary.
void offline_games_tick();
#endif // AREG_OFFLINE_GAMES_BENCH

// Runs the build-time-picked game once and returns. Deliberately does NOT
// touch the one-game-per-boot latch offline_games_tick owns, so a caller
// that wants a game (the "what next" menu, a bench harness) can ask for
// one without the arm delay and without consuming the boot's tick.
void offline_games_run_picked();

// True exactly once after a game session has ended, then false again.
//
// A latch rather than a return value: each game has many quiet exits (a
// missing clip, no press inside the answer window, the SD card gone), and
// hooking every one of them would be a dozen edits that a future game
// would have to remember to repeat. One flag set where a game RETURNS
// catches all of them by construction.
bool offline_games_consume_finished();

// Plain entry points. Each runs one complete session and returns; each
// self-gates on the answer buttons and the SD card, so calling one on a
// build that has neither is a logged no-op.
void offline_games_run_mindreader();
void offline_games_run_buzzer();
void offline_games_run_simon();

// Menu entry point: runs the NEXT game in a per-boot rotation
// (mind-reader -> buzzer -> simon -> ...), so a child who asks twice in an
// evening gets two different games. RAM cursor only -- across a power
// cycle starting from the first game again is the right behaviour, not a
// bug (mirrors Simon's own always-start-at-2 rule).
void offline_games_run_next();

// True when the games can actually run HERE AND NOW: answer buttons
// compiled in, SD up, and the first game's entry clip verified on the
// card. The menu must never say yes to a game it cannot start.
bool offline_games_available();
