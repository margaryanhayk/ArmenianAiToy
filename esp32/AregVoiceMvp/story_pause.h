// -------------------------------------------------------------
// AregVoiceMvp / story_pause.h — "shout it out" pauses inside an SD story.
//
// Occasionally the narration stops, Areg invites the child to shout
// something, a short silent beat passes, a resume line plays, and the
// SAME story continues from the SAME byte. That is the whole feature.
//
// THE HONESTY RULE THIS MODULE IS BUILT AROUND: nothing is heard.
// The mic is off, no audio is uploaded, no STT runs, no network call is
// made, and no clip claims to have heard, understood or judged anything.
// The resume lines are theatrical affirmations that read identically over
// an answer, a shout, or complete silence — see the `_flow` note in
// backend/content/offline-games/game-clips.json. This is deliberately an
// UNVERIFIED participation feature; do not "improve" it by recording.
//
// Mechanism — no second decoder, no new state, no new LED colour:
// audio_play_story_file() plays a whole file and already knows how to be
// cut mid-file (the barge-in callback) and resumed from a byte offset.
// A pause is therefore just a self-inflicted barge-in: the poll returns
// true when the armed moment arrives, playback stops with a resume
// offset, the two clips play, and the caller re-enters the same loop it
// uses after a Q&A barge-in. The story id, the rotation cursor, the
// heard-set and the play report are all untouched — none of them are
// keyed on segments.
//
// SD layout: the clips are ordinary Games-namespace content-sync clips at
// /games/story-pauses/<id>.mp3 (ids shout-1..4 / resume-1..4). The path is
// built with cs_build_game_cache_path() — the same helper the sync writes
// them with — so there is exactly one place that knows the layout.
//
// Everything above the "session runtime" divider is PURE: total functions
// over integers, no Arduino, no SD, no clock. That half is what
// story_select_test.cpp exercises without a card.
// -------------------------------------------------------------
#pragma once

#include <stddef.h>
#include <stdint.h>

// ---- tunables (config.h may override every one) ---------------------

// Never pause before this much of the story has played. A story needs to
// have started being a story before it is interrupted.
#ifndef AREG_STORY_PAUSE_HEAD_MS
#define AREG_STORY_PAUSE_HEAD_MS 45000UL
#endif

// Never pause inside the last stretch. A pause immediately before the
// ending is worse than no pause at all — it steps on the one moment the
// child has been waiting for.
#ifndef AREG_STORY_PAUSE_TAIL_MS
#define AREG_STORY_PAUSE_TAIL_MS 30000UL
#endif

// Minimum spacing between two pauses, and the minimum usable window a
// story must have before it earns even one. Two pauses 20 s apart would
// read as pestering rather than play.
#ifndef AREG_STORY_PAUSE_MIN_GAP_MS
#define AREG_STORY_PAUSE_MIN_GAP_MS 45000UL
#endif

// A pause must be at least this far ahead of where a segment starts.
// Without it, resuming a story that was sticky-paused a hair before a
// planned point would fire it instantly on the resume.
#ifndef AREG_STORY_PAUSE_LEAD_MS
#define AREG_STORY_PAUSE_LEAD_MS 3000UL
#endif

// The silent beat between the shout clip and the resume line. Long enough
// for a four-year-old to get a word out, short enough that a child who
// says nothing does not sit in dead air.
#ifndef AREG_STORY_PAUSE_BEAT_MS
#define AREG_STORY_PAUSE_BEAT_MS 3000UL
#endif

// Hard ceiling per story. Not a tunable in spirit — raising it is a
// product decision, not a config tweak.
#ifndef AREG_STORY_PAUSE_MAX_PER_STORY
#define AREG_STORY_PAUSE_MAX_PER_STORY 2
#endif

// How many shout/resume pairs exist on the card (shout-1..N / resume-1..N).
#ifndef AREG_STORY_PAUSE_VARIANTS
#define AREG_STORY_PAUSE_VARIANTS 4
#endif

// The Games-namespace key the clips are synced under.
#ifndef AREG_STORY_PAUSE_GAME_KEY
#define AREG_STORY_PAUSE_GAME_KEY "story-pauses"
#endif

// Assumed narration byte rate, used ONLY until a real one is measured.
// 24000 B/s = 192 kbps, the library-wide level tools/story-audio/
// Ship-StoryAudio.ps1 re-encodes every shipped story to.
//
// Deliberately assume the HIGH end of what the library ships. The estimate
// converts a file SIZE into a duration, and over-estimating the bitrate
// under-estimates the duration, which moves every planned pause EARLIER —
// the safe direction. Under-estimating it would push a pause toward the
// ending, which is the one outcome this feature must not produce.
//
// The first pause of a story is the only one that runs on the estimate: it
// is also the measurement, so pause two is planned against the story's
// real byte rate (see story_pause_measured_bytes_per_sec).
#ifndef AREG_STORY_PAUSE_BYTES_PER_SEC
#define AREG_STORY_PAUSE_BYTES_PER_SEC 24000UL
#endif

// =====================================================================
// PURE planning — no Arduino, no SD, no clock. Testable on a bare board.
// =====================================================================

/// How long `size_bytes` of narration lasts at `bytes_per_sec`, in ms.
/// 0 when either input is 0. 64-bit intermediate: a 32 MB file times 1000
/// overflows uint32 four times over.
inline uint32_t story_pause_estimated_duration_ms(uint32_t size_bytes,
                                                  uint32_t bytes_per_sec) {
    if (size_bytes == 0 || bytes_per_sec == 0) {
        return 0;
    }
    return (uint32_t)(((uint64_t)size_bytes * 1000ULL) / (uint64_t)bytes_per_sec);
}

/// Where in the story (ms from its start) the pauses for this file should
/// sit. Writes ascending points into `out_at_ms` and returns how many
/// (0 .. min(out_cap, AREG_STORY_PAUSE_MAX_PER_STORY)).
///
/// Rules, in order:
///   - no size, no rate, no output buffer          -> 0 pauses
///   - the head guard plus the tail guard eat the
///     whole story, or what is left is under one
///     minimum gap                                 -> 0 pauses (short
///     stories simply do not get pauses; a 90-second story has no room
///     for one that is neither too early nor too late)
///   - the usable window fits two minimum gaps     -> 2 pauses, at one
///     third and two thirds of the window
///   - otherwise                                   -> 1 pause, mid-window
///
/// Worked example, a 4-minute story at 192 kbps: duration 240 s, window
/// 240 - 45 - 30 = 165 s, so pauses at 100 s and 155 s — the later one
/// still 85 s from the end.
inline int story_pause_plan(uint32_t size_bytes, uint32_t bytes_per_sec,
                            uint32_t *out_at_ms, int out_cap) {
    if (out_at_ms == NULL || out_cap <= 0) {
        return 0;
    }
    const uint32_t duration_ms =
        story_pause_estimated_duration_ms(size_bytes, bytes_per_sec);
    const uint32_t head = (uint32_t)AREG_STORY_PAUSE_HEAD_MS;
    const uint32_t tail = (uint32_t)AREG_STORY_PAUSE_TAIL_MS;
    const uint32_t gap  = (uint32_t)AREG_STORY_PAUSE_MIN_GAP_MS;
    if (duration_ms <= head + tail) {
        return 0;
    }
    const uint32_t window = duration_ms - head - tail;
    if (window < gap) {
        return 0;
    }

    int cap = out_cap;
    if (cap > AREG_STORY_PAUSE_MAX_PER_STORY) {
        cap = AREG_STORY_PAUSE_MAX_PER_STORY;
    }
    if (cap >= 2 && window >= 2 * gap) {
        out_at_ms[0] = head + window / 3;
        out_at_ms[1] = head + (window * 2) / 3;
        return 2;
    }
    out_at_ms[0] = head + window / 2;
    return 1;
}

/// The next pause point to arm for a segment that starts at
/// `segment_start_ms` of story time, or 0 for "none — do not arm".
///
/// 0 is an unambiguous sentinel: every planned point is at least one head
/// guard into the story, so a real point can never be 0.
///
/// Skips points already behind the segment (a resume can land past one),
/// enforces the minimum gap after a pause that already fired, and refuses
/// outright once the per-story ceiling is reached. Stable across a
/// re-plan — it keys on times and a count, never on an index into a plan
/// array that measurement may have rebuilt.
inline uint32_t story_pause_next_at_ms(const uint32_t *plan, int plan_count,
                                       int fired_count, uint32_t last_fired_at_ms,
                                       uint32_t segment_start_ms) {
    if (plan == NULL || plan_count <= 0
        || fired_count >= AREG_STORY_PAUSE_MAX_PER_STORY) {
        return 0;
    }
    const uint32_t earliest = segment_start_ms + (uint32_t)AREG_STORY_PAUSE_LEAD_MS;
    for (int i = 0; i < plan_count; i++) {
        if (plan[i] < earliest) {
            continue;
        }
        if (fired_count > 0
            && plan[i] < last_fired_at_ms + (uint32_t)AREG_STORY_PAUSE_MIN_GAP_MS) {
            continue;
        }
        return plan[i];
    }
    return 0;
}

/// The narration's REAL byte rate, measured over a stretch that actually
/// played: `to_byte` is where playback stopped, `from_byte` where the
/// segment opened, `elapsed_ms` the wall time between them.
///
/// Returns 0 when the sample is not worth trusting — too short to average
/// out decoder start-up, or a result outside the range any plausible MP3
/// narration occupies (8 kbps..512 kbps). A rejected sample leaves the
/// caller on its previous rate, which is the conservative estimate.
inline uint32_t story_pause_measured_bytes_per_sec(uint32_t from_byte,
                                                   uint32_t to_byte,
                                                   uint32_t elapsed_ms) {
    if (elapsed_ms < 5000UL || to_byte <= from_byte) {
        return 0;
    }
    const uint32_t rate =
        (uint32_t)(((uint64_t)(to_byte - from_byte) * 1000ULL) / (uint64_t)elapsed_ms);
    if (rate < 1000UL || rate > 64000UL) {
        return 0;
    }
    return rate;
}

/// Which shout/resume pair a cursor value selects: 1..AREG_STORY_PAUSE_VARIANTS.
/// Plain modulo rotation — the same idiom offline_games.cpp uses for its
/// level-up variants — so a re-listen does not hear the identical line.
/// RAM-only and boot-scoped on purpose: persisting a cursor for this would
/// cost an NVS namespace to make a four-way rotation slightly less
/// predictable.
inline int story_pause_variant_of(uint32_t cursor) {
    return (int)(cursor % (uint32_t)AREG_STORY_PAUSE_VARIANTS) + 1;
}

// =====================================================================
// Session runtime — SD + audio + millis(). Implemented in story_pause.cpp.
// =====================================================================

/// Begin (or continue) a story session.
///
/// `enabled` is the caller's whole gate chain collapsed to one bool —
/// parent toggle, bedtime window, SD source. False parks the module: every
/// other call below becomes a cheap no-op and playback is bit-for-bit what
/// it was before this feature existed.
///
/// `new_story` false means this is a resume of the story already in
/// progress: the plan is rebuilt (the file may have been re-resolved) but
/// the per-story pause COUNT survives, so a child who pauses and resumes
/// still gets at most AREG_STORY_PAUSE_MAX_PER_STORY pauses in total.
///
/// Parks itself, quietly, when the file cannot be sized or the next
/// shout/resume pair is not on the card. A missing clip is a skipped
/// pause, never a gap in the story and never an error sound.
void story_pause_session_begin(bool new_story, bool enabled,
                               const char *story_path);

/// Arm (or disarm) the next pause for a playback segment opening at
/// `start_byte` of the file. Call immediately before each
/// audio_play_story_file() in the session loop.
void story_pause_segment_begin(uint32_t start_byte);

/// Polled from the story barge-in callback, every decode iteration.
/// True exactly once, when the armed moment arrives; latches so the
/// caller can tell a pause from a button press after playback returns.
/// O(1) — one compare on the hot path when armed, one when not.
bool story_pause_poll();

/// Consume the latch: true when the playback that just ended was stopped
/// by this module rather than by the child.
bool story_pause_take_pending();

/// Run the pause the latch reported: shout clip → silent beat → resume
/// line. `reached_byte` is the resume offset playback stopped at; it is
/// what turns this pause into the measurement that plans the next one.
/// Returns true when the child actually heard the invite.
bool story_pause_run(uint32_t reached_byte);

/// Drop any armed pause. Call when leaving the story session so a clip
/// played elsewhere (the welcome greeting, a game) can never be cut by a
/// timer armed for a story that is no longer playing.
void story_pause_disarm();
