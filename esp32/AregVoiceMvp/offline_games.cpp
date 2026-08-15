// -------------------------------------------------------------
// AregVoiceMvp / offline_games.cpp — three offline SD games (bench-only)
// See offline_games.h for the scope statement, the SD layout and the
// honesty rules. Entire file compiles out unless AREG_OFFLINE_GAMES_BENCH
// is defined.
// -------------------------------------------------------------
#ifdef AREG_OFFLINE_GAMES_BENCH

#include "offline_games.h"

#include <Arduino.h>
#include <FS.h>
#include <SD.h>
#include <esp_random.h>
#include <esp_task_wdt.h>

#include "config.h"
#include "audio_io.h"        // audio_sd_begin/available/has_file + audio_play_story_file
#include "answer_buttons.h"  // GREEN/RED answer buttons

// ---- SD layout -------------------------------------------------------
// Root for every offline-game clip. A #define so the layout can move
// without touching this file (config.h may override).
#ifndef AREG_GAMES_CLIP_DIR
#define AREG_GAMES_CLIP_DIR "/games"
#endif
// Per-game subdirectory = the game's own top-level key in
// backend/content/offline-games/game-clips.json. Required, not cosmetic:
// four games there each define a clip called `intro`.
#ifndef AREG_GAMES_DIR_MINDREADER
#define AREG_GAMES_DIR_MINDREADER "mind-reader"
#endif
#ifndef AREG_GAMES_DIR_BUZZER
#define AREG_GAMES_DIR_BUZZER "who-first"
#endif
#ifndef AREG_GAMES_DIR_SIMON
#define AREG_GAMES_DIR_SIMON "button-simon"
#endif

// The answer window is deliberately the SAME knob the quiz uses — one
// "how long does a child get to press" number across every offline game.
#ifndef AREG_QUIZ_ANSWER_WINDOW_MS
#define AREG_QUIZ_ANSWER_WINDOW_MS 10000UL
#endif

// Button Simon: sequence length ramp. Within-session only — nothing is
// persisted, so every session starts at the floor again (product rule).
#ifndef AREG_SIMON_START_LEN
#define AREG_SIMON_START_LEN 2
#endif
#ifndef AREG_SIMON_MAX_LEN
#define AREG_SIMON_MAX_LEN 6
#endif
// Gap between the tones the toy plays, so a repeated tone is audibly two.
#ifndef AREG_SIMON_TONE_GAP_MS
#define AREG_SIMON_TONE_GAP_MS 250UL
#endif

// Two-player buzzer: how many rounds before the closing clips.
#ifndef AREG_BUZZER_ROUNDS
#define AREG_BUZZER_ROUNDS 5
#endif

namespace {

constexpr uint32_t kStartMs       = 30000UL;  // 30 s arm delay (monitor attach)
constexpr uint32_t kStatusEveryMs = 5000UL;
constexpr int      kMaxQuizQuestions = 60;    // q01..q60 scanned; gaps are fine

// One shared path scratch buffer for the whole module (RAM discipline —
// the welcome-flow slice's rule). Every path built here is short:
// "/games/mind-reader/q-root.mp3" is 29 bytes.
char s_path[64];

bool s_done = false;
uint32_t s_last_status_ms = 0;

// Set where a game RETURNS, consumed by loop(). Separate from s_done: that
// one is the boot latch ("a game has already run"), this one is the edge
// ("a game just ended"), and conflating them would make the menu fire once
// per boot instead of once per game.
bool s_finished = false;

// ---- shared primitives (mirroring offline_quiz.cpp) ------------------

// Play one clip to natural end through the existing decoder path.
// Missing clip is a logged no-op (clips ship separately; the game must
// degrade, not crash — same self-gating rule as handle_post_story_flow).
//
// Returns "did the child actually hear this" — which is `out_started`, NOT
// audio_play_story_file's own return value. That return means INTERRUPTED,
// and with barge_in == nullptr a clip that plays perfectly to its natural
// end returns FALSE. `out_started` is the flag story_select added for
// exactly this distinction: true only once the decoder initialised and the
// first frame reached I2S.
bool play_path(const char *path) {
    if (!audio_sd_has_file(path)) {
        Serial.printf("[games] clip missing: %s\n", path);
        return false;
    }
    audio_speaker_begin();
    bool started = false;
    audio_play_story_file(path, 0, nullptr, nullptr, &started);
    if (!started) Serial.printf("[games] clip did not start: %s\n", path);
    return started;
}

// Build <root>/<game>/<id>.mp3 into the shared scratch and play it.
bool play_clip(const char *game_dir, const char *clip_id) {
    snprintf(s_path, sizeof(s_path), AREG_GAMES_CLIP_DIR "/%s/%s.mp3",
             game_dir, clip_id);
    return play_path(s_path);
}

// Wait up to the answer window for a GREEN/RED press. Returns 'Y', 'N',
// or 0 on timeout. Feeds the task watchdog while polling — byte-for-byte
// the quiz's wait_for_answer().
char wait_for_answer() {
    const uint32_t started = millis();
    while (millis() - started < AREG_QUIZ_ANSWER_WINDOW_MS) {
        const char a = answer_buttons_poll();
        if (a != 0) return a;
        delay(AREG_BUTTON_POLL_MS);
        esp_task_wdt_reset();
    }
    return 0;
}

// Common preflight: buttons + card. Returns false (already logged) when
// this build or this card cannot run a game.
bool ready(const char *tag) {
    if (!answer_buttons_present()) {
        Serial.printf("[games] %s: no answer buttons on this build — skipping\n", tag);
        return false;
    }
    if (!audio_sd_available() && !audio_sd_begin()) {
        Serial.printf("[games] %s: SD not available — skipping\n", tag);
        return false;
    }
    return true;
}

// ---------------------------------------------------------------------
// 1. MIND-READER — walk the implicit 4-deep tree by clip id
// ---------------------------------------------------------------------
//
// The tree lives entirely in the clip ids: no node table, no animal
// table, nothing in RAM but a 5-byte path string. GREEN/yes appends '1',
// RED/no appends '0' (the convention the intro clip states aloud).

constexpr int kTreeDepth = 4;

// Ask the node whose id is derived from `path` ("" → q-root, "1" → q-1).
// Returns 'Y'/'N', or 0 when the child has stopped answering. The re-ask
// is bounded at ONE repeat, then the game closes quietly — the same
// "never badger" rule as the quiz and the welcome flow.
char ask_node(const char *path) {
    char clip_id[8];
    if (path[0] == '\0') {
        snprintf(clip_id, sizeof(clip_id), "q-root");
    } else {
        snprintf(clip_id, sizeof(clip_id), "q-%s", path);
    }
    for (int attempt = 1; attempt <= 2; attempt++) {
        if (!play_clip(AREG_GAMES_DIR_MINDREADER, clip_id)) return 0;  // no clip — end
        const char a = wait_for_answer();
        if (a != 0) return a;
        if (attempt == 1) {
            Serial.println("[games] mind-reader: no answer — asking once more");
            continue;
        }
        Serial.println("[games] mind-reader: still no answer — closing quietly");
    }
    return 0;
}

void run_mindreader() {
    if (!ready("mind-reader")) return;

    play_clip(AREG_GAMES_DIR_MINDREADER, "intro");

    char path[kTreeDepth + 1] = { 0 };   // the whole "tree state" — 5 bytes
    int depth = 0;
    while (depth < kTreeDepth) {
        const char a = ask_node(path);
        if (a == 0) {
            Serial.printf("[games] mind-reader: ended at depth %d\n", depth);
            return;                       // quiet exit, no goodbye badgering
        }
        path[depth++] = (a == 'Y') ? '1' : '0';
        path[depth] = '\0';
    }

    char guess_id[8];
    snprintf(guess_id, sizeof(guess_id), "g-%s", path);
    Serial.printf("[games] mind-reader: guessing %s\n", guess_id);
    if (!play_clip(AREG_GAMES_DIR_MINDREADER, guess_id)) return;

    // Confirm window. GREEN = "you got it", RED = "you didn't". A timeout
    // is NOT scored either way: the toy re-asks the guess once (the guess
    // clip carries the «Հա՝ կանաչ, ոչ՝ կարմիր» mapping) and then leaves.
    char verdict = wait_for_answer();
    if (verdict == 0) {
        Serial.println("[games] mind-reader: no verdict — asking once more");
        if (!play_clip(AREG_GAMES_DIR_MINDREADER, guess_id)) return;
        verdict = wait_for_answer();
    }
    if (verdict == 'Y') {
        Serial.println("[games] mind-reader: guessed right");
        play_clip(AREG_GAMES_DIR_MINDREADER, "win");
    } else if (verdict == 'N') {
        Serial.println("[games] mind-reader: guessed wrong — child wins");
        play_clip(AREG_GAMES_DIR_MINDREADER, "lose");
    } else {
        Serial.println("[games] mind-reader: still no verdict — closing quietly");
    }
    // The `replay` clip is deliberately NOT played: it invites another
    // round, and honouring that invitation needs a selection gesture this
    // slice does not have. TODO(bench): wire replay once game selection
    // exists.
}

// ---------------------------------------------------------------------
// 2. TWO-PLAYER BUZZER — first press takes the round
// ---------------------------------------------------------------------
//
// This code has NO notion of player identity. It knows only which BUTTON
// was pressed first and plays that COLOUR's celebration. Nothing here can
// name a child, and there is no loser branch to take.

// Find question NN in the EXISTING quiz bank — /quiz/qNN-{y,n}.mp3 or the
// 3-voice /quiz/vkNN-{y,n}.mp3 rounds. Same probe order as the quiz's
// find_question(), duplicated rather than shared because that one is
// file-static behind a DIFFERENT build flag (AREG_OFFLINE_QUIZ_BENCH) and
// calling it would couple the two flags together.
//
// The answer suffix is read but DISCARDED: a buzzer round is about speed,
// not correctness, and nothing here reveals or verifies the answer.
bool find_quiz_question_path(int n) {
    snprintf(s_path, sizeof(s_path), "/quiz/q%02d-y.mp3", n);
    if (audio_sd_has_file(s_path)) return true;
    snprintf(s_path, sizeof(s_path), "/quiz/q%02d-n.mp3", n);
    if (audio_sd_has_file(s_path)) return true;
    snprintf(s_path, sizeof(s_path), "/quiz/vk%02d-y.mp3", n);
    if (audio_sd_has_file(s_path)) return true;
    snprintf(s_path, sizeof(s_path), "/quiz/vk%02d-n.mp3", n);
    if (audio_sd_has_file(s_path)) return true;
    return false;
}

void run_buzzer() {
    if (!ready("buzzer")) return;

    play_clip(AREG_GAMES_DIR_BUZZER, "intro");

    int next_question = 1;
    int rounds_played = 0;
    int silent_rounds = 0;
    for (int round = 1; round <= AREG_BUZZER_ROUNDS; round++) {
        // A quiz question gives the round something to listen to while the
        // players wait for «Հիմա՛». It is optional: with no quiz bank on
        // the card the round is just the `go` clip.
        while (next_question <= kMaxQuizQuestions
               && !find_quiz_question_path(next_question)) {
            next_question++;
        }
        if (next_question <= kMaxQuizQuestions) {
            play_path(s_path);
            next_question++;
        }

        if (!play_clip(AREG_GAMES_DIR_BUZZER, "go")) break;   // no clip — end

        const char first = wait_for_answer();
        if (first == 0) {
            // Nobody pressed. Say nothing about it (there is no clip for a
            // dead round, and inventing one would be the toy commenting on
            // children). Two silent rounds = the players left.
            Serial.println("[games] buzzer: no press this round");
            if (++silent_rounds >= 2) {
                Serial.println("[games] buzzer: closing quietly");
                return;
            }
            continue;
        }
        silent_rounds = 0;
        rounds_played++;
        Serial.printf("[games] buzzer: round %d to %s\n",
                      round, (first == 'Y') ? "green" : "red");
        play_clip(AREG_GAMES_DIR_BUZZER,
                  (first == 'Y') ? "win-green" : "win-red");
    }

    // Honest close: end-both claims only counted presses, close celebrates
    // both colours. Skipped entirely if no round was actually played, so
    // the toy never says «Ես հաշվեցի ձեր սեղմումները» about zero presses.
    if (rounds_played > 0) {
        play_clip(AREG_GAMES_DIR_BUZZER, "end-both");
        play_clip(AREG_GAMES_DIR_BUZZER, "close");
    }
    Serial.printf("[games] buzzer: rounds played %d\n", rounds_played);
}

// ---------------------------------------------------------------------
// 3. BUTTON SIMON — echo the tone sequence
// ---------------------------------------------------------------------
//
// Each button has its own tone. There is no parameterised tone helper in
// audio_io (audio_play_thinking_earcon is a single fixed 440 Hz earcon and
// synth_write_tone is file-static), so the two tones are SHORT CLIPS on
// the card, resolved by id exactly like every other clip here.
//
// TODO(content): game-clips.json has no `tone-green` / `tone-red` entries
// yet — they are non-verbal, so there is no Armenian text to review, only
// two short renders to add. Until they are on the card this game logs the
// missing clip and exits quietly (which is the correct degradation, but it
// means Simon cannot be bench-tested before those two files exist).
constexpr const char *kToneGreen = "tone-green";
constexpr const char *kToneRed   = "tone-red";

// Play the tone for one step. 'Y' = green, 'N' = red.
bool play_tone(char step) {
    return play_clip(AREG_GAMES_DIR_SIMON,
                     (step == 'Y') ? kToneGreen : kToneRed);
}

void run_simon() {
    if (!ready("simon")) return;

    play_clip(AREG_GAMES_DIR_SIMON, "intro");

    char seq[AREG_SIMON_MAX_LEN];        // the whole game state — 6 bytes
    int len = AREG_SIMON_START_LEN;
    if (len < 1) len = 1;
    int reached = 0;                     // longest sequence genuinely echoed
    uint8_t level_up_variant = 0;        // rotates level-up-1/2/3

    // Fresh random sequence per session. esp_random() is the hardware RNG —
    // no seeding ritual needed, and nothing about the sequence is stored.
    for (int i = 0; i < AREG_SIMON_MAX_LEN; i++) {
        seq[i] = (esp_random() & 1u) ? 'Y' : 'N';
    }

    bool missed = false;
    while (len <= AREG_SIMON_MAX_LEN && !missed) {
        for (int i = 0; i < len; i++) {
            if (!play_tone(seq[i])) {
                Serial.println("[games] simon: tone clip missing — cannot play");
                return;
            }
            delay(AREG_SIMON_TONE_GAP_MS);
            esp_task_wdt_reset();
        }
        if (!play_clip(AREG_GAMES_DIR_SIMON, "your-turn")) return;

        bool timed_out = false;
        for (int i = 0; i < len; i++) {
            const char pressed = wait_for_answer();
            if (pressed == 0) {
                Serial.println("[games] simon: no press — closing quietly");
                timed_out = true;
                break;
            }
            if (pressed != seq[i]) {
                Serial.printf("[games] simon: wrong at step %d/%d\n", i + 1, len);
                missed = true;
                break;
            }
        }
        if (timed_out) return;           // child walked away; no miss clip
        if (missed) break;

        reached = len;
        Serial.printf("[games] simon: echoed %d\n", reached);
        if (len < AREG_SIMON_MAX_LEN) {
            // level-up-1/2/3 rotate so a long session never hears the same
            // congratulation twice running (the quiz's variant rule).
            char clip_id[12];
            snprintf(clip_id, sizeof(clip_id), "level-up-%d",
                     (int)(level_up_variant % 3) + 1);
            level_up_variant++;
            play_clip(AREG_GAMES_DIR_SIMON, clip_id);
        }
        len++;
    }

    if (missed) {
        // Warm, zero shame — and the SESSION ENDS here. The reached length
        // is the result; there is no shame in it and no score is stored.
        play_clip(AREG_GAMES_DIR_SIMON, "miss");
    } else {
        // Ceiling reached: the best celebration. `best` claims a long
        // sequence, which is honest — it is derived from measured presses.
        play_clip(AREG_GAMES_DIR_SIMON, "best");
    }
    play_clip(AREG_GAMES_DIR_SIMON, "done");
    Serial.printf("[games] simon: session result — longest echoed %d\n", reached);
}

}  // namespace

// ---- entry points ----------------------------------------------------

// Each sets the finished latch on the way out, so every quiet exit inside
// a game is covered without touching the games themselves.
void offline_games_run_mindreader() { run_mindreader(); s_finished = true; Serial.flush(); }
void offline_games_run_buzzer()     { run_buzzer();     s_finished = true; Serial.flush(); }
void offline_games_run_simon()      { run_simon();      s_finished = true; Serial.flush(); }

bool offline_games_consume_finished() {
    const bool f = s_finished;
    s_finished = false;
    return f;
}

// Which game a bench build runs. 1 = mind-reader, 2 = buzzer, 3 = simon.
//
// TODO(bench day): decide how a CHILD picks a game. Deliberately not
// invented here — every option (long-press cycle, a spoken menu clip, a
// third button) either adds an input gesture or an LED meaning the toy
// does not have today, and the hard rule for this slice is no new state
// machine and no new LED vocabulary. Until that decision, the game is a
// build-time choice, exactly like which bench harness is compiled in.
#ifndef AREG_OFFLINE_GAMES_PICK
#define AREG_OFFLINE_GAMES_PICK 1
#endif

// The build-time pick, on its own so it can be run without consuming the
// boot latch. Leaving s_done alone is the whole point: the "what next"
// menu can replay a game later in the same boot.
void offline_games_run_picked() {
#if AREG_OFFLINE_GAMES_PICK == 2
    offline_games_run_buzzer();
#elif AREG_OFFLINE_GAMES_PICK == 3
    offline_games_run_simon();
#else
    offline_games_run_mindreader();
#endif
}

void offline_games_tick() {
    if (s_done) return;
    const uint32_t now = millis();
    if (now < kStartMs) {
        if (now - s_last_status_ms >= kStatusEveryMs) {
            s_last_status_ms = now;
            Serial.printf("[games] armed (pick=%d), starting in %lus\n",
                          (int)AREG_OFFLINE_GAMES_PICK,
                          (unsigned long)((kStartMs - now) / 1000UL));
        }
        return;
    }
    s_done = true;   // one game per boot, whatever the outcome
    offline_games_run_picked();
}

#endif // AREG_OFFLINE_GAMES_BENCH
