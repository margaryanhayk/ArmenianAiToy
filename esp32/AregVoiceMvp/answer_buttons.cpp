#include "answer_buttons.h"

#if AREG_HAS_ANSWER_BUTTONS

// Mirrors the main button's polled-debounce shape (AregVoiceMvp.ino):
// press/release edges are defined by AREG_BUTTON_DEBOUNCE_MS of stable
// contact; no interrupts.
struct AnswerButtonState {
    uint8_t pin;
    bool pressed;
    uint32_t last_edge_ms;
    uint8_t raw_last;
};

static AnswerButtonState s_yes = { AREG_PIN_BUTTON_YES, false, 0, HIGH };
static AnswerButtonState s_no  = { AREG_PIN_BUTTON_NO,  false, 0, HIGH };

void answer_buttons_begin() {
    pinMode(s_yes.pin, INPUT_PULLUP);
    pinMode(s_no.pin, INPUT_PULLUP);
    s_yes.raw_last = digitalRead(s_yes.pin);
    s_no.raw_last  = digitalRead(s_no.pin);
    s_yes.pressed = false;
    s_no.pressed = false;
}

// Returns true on a press edge for this one button.
static bool poll_one(AnswerButtonState &b) {
    uint8_t raw = digitalRead(b.pin);
    if (raw != b.raw_last) {
        b.last_edge_ms = millis();
        b.raw_last = raw;
    }
    if ((millis() - b.last_edge_ms) < AREG_BUTTON_DEBOUNCE_MS) {
        return false;
    }
    bool now_pressed = (raw == LOW);
    if (now_pressed != b.pressed) {
        b.pressed = now_pressed;
        return now_pressed;
    }
    return false;
}

char answer_buttons_poll() {
    // YES checked first; a genuinely simultaneous press resolves to YES,
    // which is the forgiving direction for a child.
    if (poll_one(s_yes)) return 'Y';
    if (poll_one(s_no))  return 'N';
    return 0;
}

#endif // AREG_HAS_ANSWER_BUTTONS
