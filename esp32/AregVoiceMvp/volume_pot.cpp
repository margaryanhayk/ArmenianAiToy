#include "volume_pot.h"

#if AREG_HAS_VOLUME_POT

// Cached so the story decode loops can ask for the gain on every iteration
// without paying for an ADC conversion. volume_pot_tick() is the only reader
// of the hardware, and it is rate-limited.
static float s_gain = AREG_VOLUME_FIXED_GAIN;
static uint32_t s_last_mv = 0;
static uint32_t s_last_read_ms = 0;

// One sample set -> gain. Touches no state, so both begin() and tick() can
// use it and differ only in what they do with the result.
static float read_gain_now(uint32_t *out_mv) {
    // This is the firmware's first use of the SAR ADC, and a single read of
    // it wobbles by tens of millivolts. Mapped straight to gain that wobble
    // is audible as the sound breathing, so four reads are averaged; they
    // cost well under a millisecond together.
    // Four reads was enough on an idle bench and nowhere near enough with the
    // amplifier running. Measured on the toy 2026-08-15 with the knob held at
    // its top stop: the wiper swung 2929..3149 mV DURING PLAYBACK — ~200 mV,
    // six times the deadband — so the gain re-published on almost every tick
    // and flooded the log. The pot divides the 3V3 rail, and at full volume
    // the class-D amp pulls its current in bursts off that same rail, so the
    // knob is partly measuring the amplifier. Sixteen reads plus the wider
    // deadband absorb it; a 100 nF from wiper to GND is the hardware half and
    // is the better fix if it is ever fitted.
    uint32_t sum = 0;
    for (int i = 0; i < 16; i++) {
        sum += analogReadMilliVolts(AREG_PIN_VOLUME_POT);
    }
    const uint32_t mv = sum / 16;
    if (out_mv != nullptr) {
        *out_mv = mv;
    }

    // Mapped across the full 0-3300 mV rail span. Under ADC_11db the S3 tops
    // out a little below 3300 mV, so the last sliver of knob travel saturates
    // at max gain — deliberately the harmless direction, since it means max is
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
    // No internal pull is enabled here, deliberately — the pot is itself the
    // divider, and a pull-up would sit in parallel with its upper leg and bend
    // the mapping. The pin define (see volume_pot.h) is what guarantees this
    // pin is actually driven; nothing here can check that.

    // Read once, now, so the FIRST sound already comes out at the knob's
    // position. Without this the greeting or the first story would open at the
    // compiled default and only correct a fifth of a second later — an audible
    // jump, at the one moment someone is certain to be listening.
    s_gain = read_gain_now(&s_last_mv);
    s_last_read_ms = millis();
    Serial.printf("[volume] gain %.2f (%u mV)\n", s_gain, (unsigned)s_last_mv);
    Serial.flush();
}

float volume_pot_gain() {
    return s_gain;
}

bool volume_pot_tick() {
    const uint32_t now = millis();
    if ((now - s_last_read_ms) < AREG_VOLUME_READ_MS) {
        return false;
    }
    s_last_read_ms = now;

    uint32_t mv = 0;
    const float candidate = read_gain_now(&mv);

    // Deadband: publish only a real move of the knob. The averaging above
    // reduces the ADC noise but does not remove it, and without this the
    // residual re-publishes a slightly different gain on every read — the
    // sound would breathe several times a second for a whole story.
    const float delta = (candidate > s_gain) ? (candidate - s_gain)
                                             : (s_gain - candidate);
    if (delta <= AREG_VOLUME_DEADBAND_GAIN) {
        return false;
    }

    s_gain = candidate;
    s_last_mv = mv;
    // Gated by the deadband above, so a still knob prints nothing and this
    // cannot flood the log during a four-minute story.
    Serial.printf("[volume] gain %.2f (%u mV)\n", s_gain, (unsigned)mv);
    return true;
}

#endif // AREG_HAS_VOLUME_POT
