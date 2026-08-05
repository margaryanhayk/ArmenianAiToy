// -------------------------------------------------------------
// AregVoiceMvp / offline_quiz.cpp — offline true/false quiz (bench-only)
// See offline_quiz.h for the scope statement. Entire file compiles out
// unless AREG_OFFLINE_QUIZ_BENCH is defined.
// -------------------------------------------------------------
#ifdef AREG_OFFLINE_QUIZ_BENCH

#include "offline_quiz.h"

#include <Arduino.h>
#include <FS.h>
#include <SD.h>
#include <esp_task_wdt.h>

#include "config.h"
#include "audio_io.h"        // audio_sd_begin/available/has_file + audio_play_story_file
#include "answer_buttons.h"  // GREEN/RED answer buttons

namespace {

constexpr uint32_t kStartMs       = 30000UL;  // 30 s arm delay (monitor attach)
constexpr uint32_t kStatusEveryMs = 5000UL;
constexpr int      kMaxQuestions  = 20;

#ifndef AREG_QUIZ_ANSWER_WINDOW_MS
#define AREG_QUIZ_ANSWER_WINDOW_MS 10000UL   // how long to wait for a press
#endif

constexpr const char *kWinClip   = "/quiz/win.mp3";
constexpr const char *kWrongClip = "/quiz/wrong.mp3";
constexpr const char *kDoneClip  = "/quiz/done.mp3";

bool s_done = false;
uint32_t s_last_status_ms = 0;

// Play one clip to natural end through the existing decoder path.
// Missing clip is a logged no-op (clips ship separately; the game must
// degrade, not crash — same self-gating rule as handle_post_story_flow).
bool play_clip(const char *path) {
    if (!audio_sd_has_file(path)) {
        Serial.printf("[quiz] clip missing: %s\n", path);
        return false;
    }
    audio_speaker_begin();
    return audio_play_story_file(path, 0, nullptr, nullptr);
}

// Wait up to the answer window for a GREEN/RED press. Returns 'Y', 'N',
// or 0 on timeout. Feeds the task watchdog while polling.
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

// Find question NN as either /quiz/qNN-y.mp3 or /quiz/qNN-n.mp3.
// Returns true and fills path + expected ('Y'/'N') when found.
bool find_question(int n, char *path, size_t path_len, char *expected) {
    snprintf(path, path_len, "/quiz/q%02d-y.mp3", n);
    if (audio_sd_has_file(path)) { *expected = 'Y'; return true; }
    snprintf(path, path_len, "/quiz/q%02d-n.mp3", n);
    if (audio_sd_has_file(path)) { *expected = 'N'; return true; }
    return false;
}

// One question: ask → answer → react. The re-ask loop is bounded at one
// repeat for a wrong answer and one for a timeout — never badger.
// Returns false when the quiz should end (double timeout = child left).
bool run_question(const char *path, char expected) {
    for (int attempt = 1; attempt <= 2; attempt++) {
        if (!play_clip(path)) return true;   // unreadable clip — skip on

        const char answer = wait_for_answer();
        if (answer == 0) {
            if (attempt == 1) {
                Serial.println("[quiz] no answer — asking once more");
                continue;
            }
            Serial.println("[quiz] still no answer — closing quietly");
            return false;                     // child walked away
        }
        if (answer == expected) {
            Serial.printf("[quiz] RIGHT (%c)\n", answer);
            play_clip(kWinClip);
            return true;
        }
        Serial.printf("[quiz] wrong (%c, expected %c)\n", answer, expected);
        play_clip(kWrongClip);
        // Second attempt of the same question comes around the loop;
        // a second wrong answer just moves on — no third ask.
    }
    return true;
}

void run_quiz() {
    if (!answer_buttons_present()) {
        Serial.println("[quiz] no answer buttons on this build — skipping");
        return;
    }
    if (!audio_sd_available() && !audio_sd_begin()) {
        Serial.println("[quiz] SD not available — skipping");
        return;
    }

    int asked = 0;
    char path[64];
    char expected = 0;
    for (int n = 1; n <= kMaxQuestions; n++) {
        if (!find_question(n, path, sizeof(path), &expected)) continue;
        asked++;
        Serial.printf("[quiz] q%02d expected=%c\n", n, expected);
        if (!run_question(path, expected)) break;
    }

    if (asked == 0) {
        Serial.println("[quiz] no /quiz/qNN-{y,n}.mp3 clips on card");
    } else {
        play_clip(kDoneClip);
    }
    Serial.printf("[quiz] done, questions asked: %d\n", asked);
    Serial.flush();
}

}  // namespace

void offline_quiz_tick() {
    if (s_done) return;
    const uint32_t now = millis();
    if (now < kStartMs) {
        if (now - s_last_status_ms >= kStatusEveryMs) {
            s_last_status_ms = now;
            Serial.printf("[quiz] armed, starting in %lus\n",
                          (unsigned long)((kStartMs - now) / 1000UL));
        }
        return;
    }
    s_done = true;   // one quiz per boot, whatever the outcome
    run_quiz();
}

#endif // AREG_OFFLINE_QUIZ_BENCH
