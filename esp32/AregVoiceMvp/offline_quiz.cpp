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
constexpr int      kMaxQuestions  = 60;   // q01..q60 scanned; gaps are fine

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

// Find question NN as /quiz/qNN-{y,n}.mp3 (plain quiz) or
// /quiz/vkNN-{y,n}.mp3 (the Vardan-vs-Katrin 3-voice rounds).
// Returns true and fills path + expected ('Y'/'N') + is_vk.
bool find_question(int n, char *path, size_t path_len, char *expected,
                   bool *is_vk) {
    snprintf(path, path_len, "/quiz/q%02d-y.mp3", n);
    if (audio_sd_has_file(path)) { *expected = 'Y'; *is_vk = false; return true; }
    snprintf(path, path_len, "/quiz/q%02d-n.mp3", n);
    if (audio_sd_has_file(path)) { *expected = 'N'; *is_vk = false; return true; }
    snprintf(path, path_len, "/quiz/vk%02d-y.mp3", n);
    if (audio_sd_has_file(path)) { *expected = 'Y'; *is_vk = true; return true; }
    snprintf(path, path_len, "/quiz/vk%02d-n.mp3", n);
    if (audio_sd_has_file(path)) { *expected = 'N'; *is_vk = true; return true; }
    return false;
}

// Rotated variant playback (owner rule 2026-08-05: replays never hear
// the same reaction twice running). Tries /quiz/<family>-<i>.mp3 with a
// per-family RAM cursor (boot-scoped — a diagnostic-grade memory is
// enough for the bench; NVS persistence can come with the real game
// slice), falling back to the un-numbered /quiz/<family>.mp3. Missing
// clips are logged no-ops, same self-gating rule as everywhere.
struct VariantCursor { const char *family; uint8_t next; };
VariantCursor s_cursors[] = {
    { "win-vardan", 0 }, { "win-katrin", 0 },
    { "lose-vardan", 0 }, { "lose-katrin", 0 },
    { "wrong-areg", 0 },
};

bool play_variant(const char *family) {
    VariantCursor *cur = nullptr;
    for (auto &c : s_cursors) {
        if (strcmp(c.family, family) == 0) { cur = &c; break; }
    }
    char path[64];
    if (cur != nullptr) {
        // Up to 3 numbered variants; start at the cursor, wrap, advance
        // on the one that exists so consecutive plays rotate.
        for (int probe = 0; probe < 3; probe++) {
            const int idx = ((cur->next + probe) % 3) + 1;
            snprintf(path, sizeof(path), "/quiz/%s-%d.mp3", family, idx);
            if (audio_sd_has_file(path)) {
                cur->next = (uint8_t)((idx) % 3);
                return play_clip(path);
            }
        }
    }
    snprintf(path, sizeof(path), "/quiz/%s.mp3", family);
    return play_clip(path);
}

// The VK reaction flow (owner rule 2026-08-05): after every answer
// someone REACTS. Right → the WINNER celebrates. Wrong → the kid the
// child PICKED admits it (warm-sad), then Areg's try-again line.
// -y means Vardan was right (GREEN), -n means Katrin (RED).
void play_vk_feedback(bool right, char expected, char pressed) {
    const char *vardan_or_katrin;
    if (right) {
        vardan_or_katrin = (expected == 'Y') ? "win-vardan" : "win-katrin";
        play_variant(vardan_or_katrin);
    } else {
        vardan_or_katrin = (pressed == 'Y') ? "lose-vardan" : "lose-katrin";
        play_variant(vardan_or_katrin);
        play_variant("wrong-areg");
    }
}

// One question: ask → answer → react. The re-ask loop is bounded at one
// repeat for a wrong answer and one for a timeout — never badger.
// Returns false when the quiz should end (double timeout = child left).
bool run_question(const char *path, char expected, bool is_vk) {
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
            if (is_vk) play_vk_feedback(true, expected, answer);
            else play_clip(kWinClip);
            return true;
        }
        Serial.printf("[quiz] wrong (%c, expected %c)\n", answer, expected);
        if (is_vk) play_vk_feedback(false, expected, answer);
        else play_clip(kWrongClip);
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
    bool is_vk = false;
    for (int n = 1; n <= kMaxQuestions; n++) {
        if (!find_question(n, path, sizeof(path), &expected, &is_vk)) continue;
        asked++;
        Serial.printf("[quiz] %s%02d expected=%c\n", is_vk ? "vk" : "q", n, expected);
        if (!run_question(path, expected, is_vk)) break;
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
