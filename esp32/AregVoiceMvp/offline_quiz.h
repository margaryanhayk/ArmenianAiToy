// Offline true/false quiz game (bench scaffold — AREG_OFFLINE_QUIZ_BENCH).
//
// The fully-offline game the 3-button revision enables: the toy plays a
// pre-recorded question clip from SD, the child answers with the GREEN
// (yes) or RED (no) button, and the toy plays a win or try-again clip —
// no Wi-Fi, no STT, no model, zero latency, and the answer is genuinely
// VERIFIED (the truth is encoded in the clip's filename).
//
// SD layout (all clips are owner-reviewed, pre-rendered MP3s):
//   /quiz/q01-y.mp3 … /quiz/q20-y.mp3   question whose answer is YES
//   /quiz/q01-n.mp3 … /quiz/q20-n.mp3   question whose answer is NO
//   (each NN exists in exactly ONE of the two forms)
//   /quiz/win.mp3     right answer  («Ապրե՛ս» in the storyteller voice)
//   /quiz/wrong.mp3   wrong answer  (warm and short — «try again» tone)
//   /quiz/done.mp3    optional goodbye after the last question
//
// Flow per question (owner-approved loop, 2026-08-05):
//   play question → wait for GREEN/RED up to the answer window
//     right   → win clip → next question
//     wrong   → wrong clip → SAME question once more → then move on
//     timeout → re-ask once → second timeout ends the quiz quietly
//               (the "never badger" rule — same as the welcome flow)
//
// Bench-only: production builds compile ZERO bytes of this. The tick
// runs one quiz per boot, 30 s after boot, from the IDLE branch — the
// same pattern as sd_playback_tick. Requires the answer buttons; a
// build with the flag but no pins logs once and does nothing.
#pragma once

#ifdef AREG_OFFLINE_QUIZ_BENCH
void offline_quiz_tick();
#endif
