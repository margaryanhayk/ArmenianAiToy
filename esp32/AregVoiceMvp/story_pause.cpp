// -------------------------------------------------------------
// AregVoiceMvp / story_pause.cpp — see story_pause.h.
//
// Compiled into EVERY build (production included). A shout-it-out pause
// is normal playback behaviour, not a bench harness — but it is fully
// gated: with the parent toggle off, inside the bedtime window, on the
// Wi-Fi stream, or on a card without the clips, this module parks itself
// and the story plays exactly as it did before the feature existed.
// -------------------------------------------------------------
#include "config.h"        // must precede story_pause.h: it may override the knobs
#include "story_pause.h"

#include <Arduino.h>
#include <FS.h>
#include <SD.h>
#include <esp_task_wdt.h>

#include "content_sync_rules.h"   // cs_build_game_cache_path — one owner of /games layout
#include "audio_io.h"             // audio_sd_available / _has_file / audio_play_story_file

namespace {

// ---- session state (all RAM, all boot-scoped) ----------------------
bool     s_enabled       = false;   // the gate chain, resolved once per session
uint32_t s_bytes_per_sec = (uint32_t)AREG_STORY_PAUSE_BYTES_PER_SEC;
uint32_t s_size_bytes    = 0;

uint32_t s_plan[AREG_STORY_PAUSE_MAX_PER_STORY];
int      s_plan_count    = 0;

int      s_fired_count   = 0;       // per STORY, survives a sticky-pause resume
uint32_t s_last_fired_at_ms = 0;    // story time of the last pause that fired

// Armed-segment state.
bool     s_armed             = false;
uint32_t s_segment_start_byte = 0;
uint32_t s_segment_start_millis = 0;
uint32_t s_segment_start_story_ms = 0;
uint32_t s_fire_after_ms     = 0;   // millis() since segment start
uint32_t s_armed_at_story_ms = 0;

bool     s_pending = false;         // latch: the last stop was ours

// Which shout/resume pair plays next. Survives across stories within a
// boot so two stories in a row do not open with the same invite.
uint32_t s_variant_cursor = 0;

// Path scratch. One buffer, reused — the welcome-flow slice's RAM rule.
// "/games/story-pauses/resume-1.mp3" is 33 bytes.
char s_path[CS_MAX_PATH_LEN];

/// Actual on-card size, or 0 when absent/unopenable. Deliberately a local
/// copy of story_select.cpp's helper rather than a shared export: ten
/// lines of SD stat is not worth coupling two modules over.
uint32_t sd_file_size(const char *path) {
    if (path == NULL || !audio_sd_available() || !SD.exists(path)) {
        return 0;
    }
    File f = SD.open(path, FILE_READ);
    if (!f) {
        return 0;
    }
    const uint32_t n = (uint32_t)f.size();
    f.close();
    return n;
}

/// Build "/games/story-pauses/<prefix>-<n>.mp3" into the shared scratch.
bool build_clip_path(const char *prefix, int variant) {
    char clip_id[16];
    const int written = snprintf(clip_id, sizeof(clip_id), "%s-%d", prefix, variant);
    if (written <= 0 || (size_t)written >= sizeof(clip_id)) {
        return false;
    }
    return cs_build_game_cache_path(s_path, sizeof(s_path),
                                    AREG_STORY_PAUSE_GAME_KEY, clip_id);
}

/// Are BOTH halves of pair `variant` on the card? Checked before the
/// module arms anything, so a half-synced card never produces a story
/// that stops for an invite and then has nothing to resume with.
bool pair_present(int variant) {
    if (!build_clip_path("shout", variant) || !audio_sd_has_file(s_path)) {
        return false;
    }
    if (!build_clip_path("resume", variant) || !audio_sd_has_file(s_path)) {
        return false;
    }
    return true;
}

/// Play one clip to its natural end. No barge-in poll is passed: these
/// clips are a few seconds long, and handing them the story's poll would
/// let a pause timer cut its own invite. Returns whether it was heard.
bool play_clip(const char *prefix, int variant) {
    if (!build_clip_path(prefix, variant) || !audio_sd_has_file(s_path)) {
        Serial.printf("[story-pause] clip missing: %s-%d\n", prefix, variant);
        Serial.flush();
        return false;
    }
    audio_speaker_begin();
    bool started = false;
    audio_play_story_file(s_path, 0, nullptr, nullptr, &started);
    return started;
}

/// Byte offset -> story time, at whatever rate we currently believe.
uint32_t byte_to_story_ms(uint32_t byte_offset) {
    if (s_bytes_per_sec == 0) {
        return 0;
    }
    return (uint32_t)(((uint64_t)byte_offset * 1000ULL) / (uint64_t)s_bytes_per_sec);
}

void rebuild_plan() {
    s_plan_count = story_pause_plan(s_size_bytes, s_bytes_per_sec,
                                    s_plan, AREG_STORY_PAUSE_MAX_PER_STORY);
}

void park(const char *reason) {
    s_enabled = false;
    s_armed = false;
    s_pending = false;
    s_plan_count = 0;
    Serial.printf("[story-pause] off (%s)\n", reason);
    Serial.flush();
}

}  // namespace

void story_pause_session_begin(bool new_story, bool enabled,
                               const char *story_path) {
    s_armed = false;
    s_pending = false;
    if (new_story) {
        s_fired_count = 0;
        s_last_fired_at_ms = 0;
        // A fresh estimate per story: last story's measured rate says
        // nothing about this file's bitrate.
        s_bytes_per_sec = (uint32_t)AREG_STORY_PAUSE_BYTES_PER_SEC;
    }
    s_enabled = false;
    s_plan_count = 0;
    s_size_bytes = 0;

    if (!enabled || story_path == NULL || story_path[0] == '\0') {
        return;   // gated off upstream; silent, this is the ordinary case
    }
    s_size_bytes = sd_file_size(story_path);
    if (s_size_bytes == 0) {
        park("story file not sizeable");
        return;
    }
    if (!pair_present(story_pause_variant_of(s_variant_cursor))) {
        park("shout/resume clips not on the card");
        return;
    }

    s_enabled = true;
    rebuild_plan();
    if (s_plan_count == 0) {
        Serial.printf("[story-pause] no pause fits this story (%u B ~ %us)\n",
                      (unsigned)s_size_bytes,
                      (unsigned)(story_pause_estimated_duration_ms(
                                     s_size_bytes, s_bytes_per_sec) / 1000UL));
    } else if (s_plan_count == 1) {
        Serial.printf("[story-pause] planned 1 pause at %us (fired %d)\n",
                      (unsigned)(s_plan[0] / 1000UL), s_fired_count);
    } else {
        Serial.printf("[story-pause] planned 2 pauses at %us / %us (fired %d)\n",
                      (unsigned)(s_plan[0] / 1000UL),
                      (unsigned)(s_plan[1] / 1000UL), s_fired_count);
    }
    Serial.flush();
}

void story_pause_segment_begin(uint32_t start_byte) {
    s_armed = false;
    s_pending = false;
    if (!s_enabled || s_plan_count == 0) {
        return;
    }
    s_segment_start_byte = start_byte;
    s_segment_start_story_ms = byte_to_story_ms(start_byte);
    const uint32_t at = story_pause_next_at_ms(s_plan, s_plan_count,
                                               s_fired_count, s_last_fired_at_ms,
                                               s_segment_start_story_ms);
    if (at == 0) {
        return;
    }
    s_armed_at_story_ms = at;
    s_fire_after_ms = at - s_segment_start_story_ms;
    s_segment_start_millis = millis();
    s_armed = true;
    Serial.printf("[story-pause] armed: %us into this segment (story %us)\n",
                  (unsigned)(s_fire_after_ms / 1000UL), (unsigned)(at / 1000UL));
    Serial.flush();
}

bool story_pause_poll() {
    if (!s_armed) {
        return false;
    }
    if ((millis() - s_segment_start_millis) < s_fire_after_ms) {
        return false;
    }
    s_armed = false;
    s_pending = true;
    return true;
}

bool story_pause_take_pending() {
    const bool p = s_pending;
    s_pending = false;
    return p;
}

bool story_pause_run(uint32_t reached_byte) {
    // Measure before anything else: the numbers describe the stretch that
    // just played, and the clip playback below would pollute the elapsed.
    const uint32_t elapsed_ms = millis() - s_segment_start_millis;
    const uint32_t measured = story_pause_measured_bytes_per_sec(
        s_segment_start_byte, reached_byte, elapsed_ms);
    if (measured != 0 && measured != s_bytes_per_sec) {
        Serial.printf("[story-pause] measured %u B/s (was %u) — re-planning\n",
                      (unsigned)measured, (unsigned)s_bytes_per_sec);
        s_bytes_per_sec = measured;
        rebuild_plan();
    }

    s_fired_count++;
    // Story time of where we actually are, at the rate we now believe —
    // not the planned time, which was an estimate.
    s_last_fired_at_ms = byte_to_story_ms(reached_byte);

    const int variant = story_pause_variant_of(s_variant_cursor);
    s_variant_cursor++;
    Serial.printf("[story-pause] pause %d/%d at byte %u (~%us) — shout-%d\n",
                  s_fired_count, (int)AREG_STORY_PAUSE_MAX_PER_STORY,
                  (unsigned)reached_byte,
                  (unsigned)(s_last_fired_at_ms / 1000UL), variant);
    Serial.flush();

    const bool heard = play_clip("shout", variant);
    if (!heard) {
        // The invite never made a sound, so there is nothing to resume
        // from and nothing to affirm. Say nothing and let the story
        // continue — a resume line after silence would be Areg answering
        // himself.
        Serial.println("[story-pause] invite did not play — continuing the story");
        Serial.flush();
        return false;
    }

    // The beat. Deliberately nothing but a wait: the mic stays off, no
    // button is polled, nothing is recorded. The child shouts, or does
    // not, and the story continues either way.
    const uint32_t beat_until = millis() + (uint32_t)AREG_STORY_PAUSE_BEAT_MS;
    while ((int32_t)(beat_until - millis()) > 0) {
        delay(50);
        esp_task_wdt_reset();
    }

    play_clip("resume", variant);
    return true;
}

void story_pause_disarm() {
    s_armed = false;
    s_pending = false;
}
