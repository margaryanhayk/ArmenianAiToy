#include "volume_pot.h"

#if AREG_HAS_VOLUME_POT

// Cached so the story decode loops can ask for the gain on every iteration
// without paying for an ADC conversion. volume_pot_tick() is the only reader
// of the hardware, and it is rate-limited.
static float s_gain = AREG_VOLUME_FIXED_GAIN;
static uint32_t s_last_mv = 0;
static uint32_t s_last_read_ms = 0;
// Serial is a blocking device once its TX buffer fills, and this code runs on
// the decode hot path. See the note in volume_pot_tick().
static uint32_t s_last_log_ms = 0;
static uint8_t  s_pending_hits = 0;   // consecutive out-of-deadband reads
static constexpr uint32_t kLogEveryMs = 3000;

// One sample set -> gain. Touches no state, so both begin() and tick() can
// use it and differ only in what they do with the result.
static float read_gain_now(uint32_t *out_mv) {
    // This is the firmware's first use of the SAR ADC, and a single read of
    // it wobbles by tens of millivolts. Mapped straight to gain that wobble
    // is audible as the sound breathing, so four reads are averaged; they
    // cost well under a millisecond together.
    // Four reads was enough on an idle bench and nowhere near enough with the
    // amplifier running. Measured on the toy 2026-08-15 with the knob held at
    // its top stop: the wiper swung 2929..3149 mV DURING PLAYBACK Ã¢â‚¬â€ ~200 mV,
    // six times the deadband Ã¢â‚¬â€ so the gain re-published on almost every tick
    // and flooded the log. The pot divides the 3V3 rail, and at full volume
    // the class-D amp pulls its current in bursts off that same rail, so the
    // knob is partly measuring the amplifier. Sixteen reads plus the wider
    // deadband absorb it; a 100 nF from wiper to GND is the hardware half and
    // is the better fix if it is ever fitted.
    // MEDIAN, not mean. 2026-08-16: averaging sixteen samples still left the
    // gain swinging 1.71..2.07 during a story -- 0.36, wider than even the
    // widened 0.22 deadband, so the loudness audibly breathed. A mean is
    // dragged by exactly the thing this signal has: occasional large spikes
    // from the amplifier's current bursts on the shared 3V3 rail. A median
    // ignores them outright -- half the samples would have to be corrupted at
    // once to move it. Same sixteen conversions, same cost, far steadier
    // number.
    //
    // The real fix is 100 nF from the wiper to GND. This is what can be done
    // in software until that exists.
    uint32_t s[16];
    for (int i = 0; i < 16; i++) {
        s[i] = analogReadMilliVolts(AREG_PIN_VOLUME_POT);
    }
    for (int i = 1; i < 16; i++) {           // insertion sort, 16 items
        const uint32_t k = s[i];
        int j = i - 1;
        while (j >= 0 && s[j] > k) { s[j + 1] = s[j]; j--; }
        s[j + 1] = k;
    }
    const uint32_t mv = (s[7] + s[8]) / 2;
    if (out_mv != nullptr) {
        *out_mv = mv;
    }

    // Mapped across the full 0-3300 mV rail span. Under ADC_11db the S3 tops
    // out a little below 3300 mV, so the last sliver of knob travel saturates
    // at max gain Ã¢â‚¬â€ deliberately the harmless direction, since it means max is
    // always reachable. Mapping to a narrower span instead would leave the top
    // of the travel doing nothing at all, which reads as a broken knob.
    float t = (float)mv / 3300.0f;
    if (t < 0.0f) t = 0.0f;
    if (t > 1.0f) t = 1.0f;
    return AREG_VOLUME_MIN_GAIN + t * (AREG_VOLUME_MAX_GAIN - AREG_VOLUME_MIN_GAIN);
}

void volume_pot_begin() {
    analogReadResolution(12);
    // Resolution and attenuation are both set explicitly rather than trusting
    // the core's defaults: those have moved across ESP32 core releases, and a
    // silently different attenuation would rescale every sound the toy makes.
    analogSetPinAttenuation(AREG_PIN_VOLUME_POT, ADC_11db);
    // No internal pull is enabled here, deliberately Ã¢â‚¬â€ the pot is itself the
    // divider, and a pull-up would sit in parallel with its upper leg and bend
    // the mapping. The pin define (see volume_pot.h) is what guarantees this
    // pin is actually driven; nothing here can check that.

    // Read once, now, so the FIRST sound already comes out at the knob's
    // position. Without this the greeting or the first story would open at the
    // compiled default and only correct a fifth of a second later Ã¢â‚¬â€ an audible
    // jump, at the one moment someone is certain to be listening.
    s_gain = read_gain_now(&s_last_mv);
    s_last_read_ms = millis();
    Serial.printf("[volume] gain %.2f (%u mV)\n", s_gain, (unsigned)s_last_mv);
    Serial.flush();
}

float volume_pot_gain() {
    return s_gain;
}

// The deadband is WIDER while audio is playing, and that is the whole fix.
//
// 2026-08-16, measured on the toy across three attempts. Averaging 16 samples
// did not settle it; a median of 16 did not settle it; requiring two agreeing
// reads did not settle it. During a story the gain still swung 1.67..2.08.
// Three filters aimed at NOISE all failed for the same reason: this is not
// noise. The pot divides the 3V3 rail, the class-D amp draws its current off
// that same rail, and a loud passage SAGS it. The wiper voltage really does
// drop, so the toy really does read a lower knob position. No amount of
// filtering un-sags a rail.
//
// What separates the two cases is size. A child turning the knob moves it a
// long way and leaves it there; rail sag moves it a little and comes back. So
// while something is playing, only a large deliberate move is believed --
// about a sixth of the travel. When nothing is playing the rail is steady and
// the normal, fine deadband applies, so the knob stays precise where precision
// is possible.
//
// The proper fix is hardware: 100 nF from the wiper to GND, and better still a
// pot that is not referenced to the rail the amplifier loads. Recorded here so
// the next person does not spend another evening filtering physics.
static bool tick_with_deadband(float deadband);

bool volume_pot_tick() {
    return tick_with_deadband(AREG_VOLUME_DEADBAND_GAIN);
}

bool volume_pot_tick_playing() {
    return tick_with_deadband(AREG_VOLUME_DEADBAND_PLAYING);
}

static bool tick_with_deadband(float deadband) {
    const uint32_t now = millis();
    if ((now - s_last_read_ms) < AREG_VOLUME_READ_MS) {
        return false;
    }
    s_last_read_ms = now;

    uint32_t mv = 0;
    const float candidate = read_gain_now(&mv);

    // Deadband: publish only a real move of the knob. The averaging above
    // reduces the ADC noise but does not remove it, and without this the
    // residual re-publishes a slightly different gain on every read Ã¢â‚¬â€ the
    // sound would breathe several times a second for a whole story.
    const float delta = (candidate > s_gain) ? (candidate - s_gain)
                                             : (s_gain - candidate);
    if (delta <= deadband) {
        s_pending_hits = 0;
        return false;
    }

    // TWO CONSECUTIVE READS must agree before the gain moves. A hand on the
    // knob holds the new position for as long as it takes to turn it, so a
    // real move always survives two reads 200 ms apart; a noise spike almost
    // never does. This is what lets the deadband stay narrow enough that the
    // knob still feels continuous, instead of being widened until only a
    // quarter-turn registers.
    if (++s_pending_hits < 2) {
        return false;
    }
    s_pending_hits = 0;

    s_gain = candidate;
    s_last_mv = mv;

    // NO PRINT ON THE HOT PATH. 2026-08-16: the owner heard the story stutter
    // -- "sdsdsdsd" -- and this line was the cause, not the ADC.
    //
    // volume_pot_tick() runs INSIDE the MP3 decode loop so the knob works
    // mid-story. The deadband was supposed to make this print rare; against a
    // pot that is partly measuring the amplifier's own rail it was not rare at
    // all -- 530 prints in a single story, measured. Each one is a float
    // printf over a 115200 baud line, and when the TX buffer fills Serial
    // BLOCKS. Blocking the decoder is exactly how an MP3 stutters.
    //
    // The print was a bench aid for the night the knob was built. It is
    // rate-limited to one line every 3 s now, which keeps it useful for
    // diagnosis and cannot starve the decoder. The gain itself still updates
    // on every real move -- the sound follows the knob, the log does not.
    if ((now - s_last_log_ms) >= kLogEveryMs) {
        s_last_log_ms = now;
        Serial.printf("[volume] gain %.2f (%u mV)\n", s_gain, (unsigned)mv);
    }
    return true;
}

#endif // AREG_HAS_VOLUME_POT
