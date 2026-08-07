// -------------------------------------------------------------
// AregVoiceMvp / story_select_test.cpp — see story_select_test.h.
// Entire file compiles out unless AREG_STORY_SELECT_TEST_BENCH is set.
// -------------------------------------------------------------
#ifdef AREG_STORY_SELECT_TEST_BENCH

#include "story_select_test.h"

#include <Arduino.h>

#include "config.h"        // before story_pause.h: a bench override of a pause
                           // knob must reach the test, not just the runtime
#include "story_select.h"
#include "story_pause.h"   // shout-it-out pause planning (pure half only)

namespace {

int s_pass = 0;
int s_fail = 0;

void check(bool condition, const char *name) {
    if (condition) {
        s_pass++;
        Serial.printf("[sel-test] PASS %s\n", name);
    } else {
        s_fail++;
        Serial.printf("[sel-test] FAIL %s\n", name);
    }
    Serial.flush();
}

const char *kSha = "4ba0969646dfcb34ede49b3c82ac234a55299ab6789354f0ccbc6beb64f7e631";

void make(CsStory *s, const char *id, long size = 1000, int version = 1,
          bool verified = true) {
    memset(s, 0, sizeof(*s));
    cs_copy_bounded(s->story_id, sizeof(s->story_id), id);
    cs_copy_bounded(s->sha256, sizeof(s->sha256), kSha);
    cs_copy_bounded(s->title, sizeof(s->title), "T");
    s->version    = version;
    s->size_bytes = size;
    s->verified   = verified;
    cs_build_cache_path(s->cache_path, sizeof(s->cache_path), id, version);
}

// Convenience: run one selection round.
bool pick(const CsStory *list, int n, const char *prev, char *out, size_t len) {
    return story_select_next(list, n, prev, out, len);
}

// ---- 1-5, 26-27, 29: round-robin selection --------------------------

void test_zero_stories() {
    char out[CS_MAX_STORY_ID_LEN + 1] = "sentinel";
    CsStory none[1];
    make(&none[0], "a");
    check(!pick(none, 0, "", out, sizeof(out)), "zero_stories_no_selection");
    check(!pick(nullptr, 3, "", out, sizeof(out)), "null_list_no_selection");
}

void test_one_story_always_selected() {
    CsStory one[1];
    make(&one[0], "anban-huri");
    char out[CS_MAX_STORY_ID_LEN + 1];

    check(pick(one, 1, "", out, sizeof(out)) && strcmp(out, "anban-huri") == 0,
          "one_story_selected_when_no_previous");
    // KEYSTONE for the single-story card: no-repeat must NOT starve a
    // one-story library — the same story is the only correct answer.
    check(pick(one, 1, "anban-huri", out, sizeof(out)) && strcmp(out, "anban-huri") == 0,
          "one_story_reselected_despite_no_repeat");
}

void test_two_stories_alternate() {
    CsStory two[2];
    make(&two[0], "a-one");
    make(&two[1], "b-two");
    char out[CS_MAX_STORY_ID_LEN + 1];

    check(pick(two, 2, "", out, sizeof(out)) && strcmp(out, "a-one") == 0,
          "two_first_pick_is_first");
    check(pick(two, 2, "a-one", out, sizeof(out)) && strcmp(out, "b-two") == 0,
          "two_alternate_a_to_b");
    check(pick(two, 2, "b-two", out, sizeof(out)) && strcmp(out, "a-one") == 0,
          "two_alternate_b_to_a");
}

void test_three_stories_rotate() {
    CsStory three[3];
    make(&three[0], "a-one");
    make(&three[1], "b-two");
    make(&three[2], "c-three");
    char out[CS_MAX_STORY_ID_LEN + 1];

    check(pick(three, 3, "", out, sizeof(out)) && strcmp(out, "a-one") == 0,
          "three_first_pick_is_first");
    check(pick(three, 3, "a-one", out, sizeof(out)) && strcmp(out, "b-two") == 0,
          "three_rotate_a_to_b");
    check(pick(three, 3, "b-two", out, sizeof(out)) && strcmp(out, "c-three") == 0,
          "three_rotate_b_to_c");
    check(pick(three, 3, "c-three", out, sizeof(out)) && strcmp(out, "a-one") == 0,
          "three_wraps_c_to_a");
}

/// Drives the rotation the way the device does — feeding each result back
/// as the next "previous" — and asserts the full A,B,C,A,B,C sequence
/// plus the invariant that no consecutive pair repeats.
void test_rotation_sequence_and_no_repeat() {
    CsStory three[3];
    make(&three[0], "a-one");
    make(&three[1], "b-two");
    make(&three[2], "c-three");

    const char *expected[6] = {"a-one", "b-two", "c-three", "a-one", "b-two", "c-three"};
    char prev[CS_MAX_STORY_ID_LEN + 1] = "";
    char out[CS_MAX_STORY_ID_LEN + 1];
    bool sequence_ok = true;
    bool no_repeat_ok = true;

    for (int i = 0; i < 6; i++) {
        if (!pick(three, 3, prev, out, sizeof(out))) {
            sequence_ok = false;
            break;
        }
        if (strcmp(out, expected[i]) != 0) sequence_ok = false;
        if (prev[0] != '\0' && strcmp(out, prev) == 0) no_repeat_ok = false;
        cs_copy_bounded(prev, sizeof(prev), out);
    }
    check(sequence_ok, "rotation_sequence_a_b_c_a_b_c");
    check(no_repeat_ok, "no_back_to_back_repeat_over_six_picks");
}

void test_unknown_previous_selects_first() {
    CsStory three[3];
    make(&three[0], "a-one");
    make(&three[1], "b-two");
    make(&three[2], "c-three");
    char out[CS_MAX_STORY_ID_LEN + 1];

    check(pick(three, 3, "not-in-list", out, sizeof(out)) && strcmp(out, "a-one") == 0,
          "unknown_previous_selects_first");
    check(pick(three, 3, nullptr, out, sizeof(out)) && strcmp(out, "a-one") == 0,
          "null_previous_selects_first");
}

void test_previous_match_is_case_insensitive() {
    CsStory two[2];
    make(&two[0], "a-one");
    make(&two[1], "b-two");
    char out[CS_MAX_STORY_ID_LEN + 1];
    // Matches how the backend and index treat ids.
    check(pick(two, 2, "A-ONE", out, sizeof(out)) && strcmp(out, "b-two") == 0,
          "previous_id_matched_case_insensitively");
}

void test_index_order_preserved() {
    // Selection walks the list in the order given; it never sorts.
    CsStory three[3];
    make(&three[0], "zebra");
    make(&three[1], "apple");
    make(&three[2], "mango");
    char out[CS_MAX_STORY_ID_LEN + 1];
    check(pick(three, 3, "", out, sizeof(out)) && strcmp(out, "zebra") == 0,
          "order_preserved_first_is_index_order");
    check(pick(three, 3, "zebra", out, sizeof(out)) && strcmp(out, "apple") == 0,
          "order_preserved_second_is_index_order");
}

// ---- 30: output bounds ---------------------------------------------

void test_output_bounds() {
    CsStory one[1];
    make(&one[0], "a-fairly-long-story-identifier");
    char tiny[8];
    check(!pick(one, 1, "", tiny, sizeof(tiny)), "refuses_too_small_output_buffer");
    check(!pick(one, 1, "", nullptr, 32), "refuses_null_output_buffer");
    check(!pick(one, 1, "", tiny, 0), "refuses_zero_length_buffer");

    char exact[CS_MAX_STORY_ID_LEN + 1];
    check(pick(one, 1, "", exact, sizeof(exact)), "accepts_adequate_buffer");
}

// ---- 8-11: eligibility (pure half) ---------------------------------

void test_eligibility_rules() {
    CsStory e;
    make(&e, "anban-huri", 4654560);

    check(story_entry_eligible(&e, 4654560), "eligible_when_size_matches");

    // 9. Missing file: caller reports a negative size.
    check(!story_entry_eligible(&e, -1), "ineligible_when_file_missing");
    // 10. Size mismatch.
    check(!story_entry_eligible(&e, 4654559), "ineligible_when_size_mismatch");
    check(!story_entry_eligible(&e, 0), "ineligible_when_actual_size_zero");
    // 8. Unverified.
    {
        CsStory u;
        make(&u, "anban-huri", 4654560, 1, /*verified=*/false);
        check(!story_entry_eligible(&u, 4654560), "ineligible_when_unverified");
    }
    // Version must be >= 1.
    {
        CsStory v;
        make(&v, "anban-huri", 4654560);
        v.version = 0;
        check(!story_entry_eligible(&v, 4654560), "ineligible_when_version_zero");
    }
    // Recorded size must be positive.
    {
        CsStory z;
        make(&z, "anban-huri", 4654560);
        z.size_bytes = 0;
        check(!story_entry_eligible(&z, 0), "ineligible_when_recorded_size_zero");
    }
    // Invalid id.
    {
        CsStory b;
        make(&b, "anban-huri", 1000);
        cs_copy_bounded(b.story_id, sizeof(b.story_id), "../evil");
        check(!story_entry_eligible(&b, 1000), "ineligible_when_story_id_invalid");
    }
    check(!story_entry_eligible(nullptr, 1000), "ineligible_when_null");
}

// ---- 11: cache-path safety -----------------------------------------

void test_cache_path_safety() {
    check(story_is_safe_cache_path("/stories/anban-huri-v1.mp3"), "path_accepts_normal");

    check(!story_is_safe_cache_path("/stories/../secret.bin"), "path_rejects_traversal");
    check(!story_is_safe_cache_path("/etc/passwd"), "path_rejects_outside_stories");
    check(!story_is_safe_cache_path("stories/rel.mp3"), "path_rejects_relative");
    check(!story_is_safe_cache_path("/stories/a\\b.mp3"), "path_rejects_backslash");
    check(!story_is_safe_cache_path(""), "path_rejects_empty");
    check(!story_is_safe_cache_path(nullptr), "path_rejects_null");
    check(!story_is_safe_cache_path("/storiesX/a.mp3"), "path_rejects_prefix_lookalike");
    {
        char too_long[CS_MAX_PATH_LEN + 16];
        memset(too_long, 'a', sizeof(too_long) - 1);
        too_long[sizeof(too_long) - 1] = '\0';
        memcpy(too_long, "/stories/", 9);
        check(!story_is_safe_cache_path(too_long), "path_rejects_over_max_length");
    }

    // An entry whose path is unsafe is ineligible even when everything
    // else agrees — the traversal guard is not bypassable via size match.
    CsStory e;
    make(&e, "anban-huri", 1000);
    cs_copy_bounded(e.cache_path, sizeof(e.cache_path), "/stories/../../evil.mp3");
    check(!story_entry_eligible(&e, 1000), "ineligible_when_cache_path_unsafe");
}

// ---- failed-start exclusion (the safer child-facing policy) ---------

void test_failed_story_is_skipped() {
    CsStory three[3];
    make(&three[0], "a-one");
    make(&three[1], "b-two");
    make(&three[2], "c-three");
    char out[CS_MAX_STORY_ID_LEN + 1];

    // The scenario from the policy: A was last genuinely played, the
    // selector chose B, B resolved but never made a sound. last_id is
    // still A, B is excluded, so the next NEW-story request must land on
    // C — not on B again, and not on A.
    const char *excluded[1] = {"b-two"};
    check(story_select_next_excluding(three, 3, "a-one", excluded, 1,
                                      out, sizeof(out))
              && strcmp(out, "c-three") == 0,
          "failed_story_skipped_next_advances_past_it");

    // Once C starts, the exclusion is cleared and normal rotation resumes
    // from C.
    check(pick(three, 3, "c-three", out, sizeof(out)) && strcmp(out, "a-one") == 0,
          "rotation_resumes_after_exclusion_cleared");
}

void test_exclusion_never_starves_playback() {
    // KEYSTONE for a one-story card: the only story failed, but excluding
    // it would leave nothing to play, so the exclusion is ignored and the
    // child gets a retry rather than silence.
    CsStory one[1];
    make(&one[0], "only-story");
    char out[CS_MAX_STORY_ID_LEN + 1];
    const char *excluded[1] = {"only-story"};
    check(story_select_next_excluding(one, 1, "only-story", excluded, 1,
                                      out, sizeof(out))
              && strcmp(out, "only-story") == 0,
          "one_story_retried_when_exclusion_would_empty_list");

    // Same rule with every story excluded: fall back to the full list
    // rather than returning nothing.
    CsStory two[2];
    make(&two[0], "a-one");
    make(&two[1], "b-two");
    const char *all[2] = {"a-one", "b-two"};
    check(story_select_next_excluding(two, 2, "", all, 2, out, sizeof(out)),
          "all_excluded_still_selects_something");
}

void test_exclusion_two_story_prefers_playable() {
    // With two stories where one is broken, the good one replays rather
    // than the child hearing nothing. Availability beats strict no-repeat
    // when the library is effectively one playable story.
    CsStory two[2];
    make(&two[0], "a-one");
    make(&two[1], "b-two");
    char out[CS_MAX_STORY_ID_LEN + 1];
    const char *excluded[1] = {"b-two"};
    check(story_select_next_excluding(two, 2, "a-one", excluded, 1,
                                      out, sizeof(out))
              && strcmp(out, "a-one") == 0,
          "two_stories_one_broken_replays_the_good_one");
}

void test_exclusion_matching_and_bounds() {
    CsStory three[3];
    make(&three[0], "a-one");
    make(&three[1], "b-two");
    make(&three[2], "c-three");
    char out[CS_MAX_STORY_ID_LEN + 1];

    // Case-insensitive, like every other id comparison.
    const char *excluded_upper[1] = {"B-TWO"};
    check(story_select_next_excluding(three, 3, "a-one", excluded_upper, 1,
                                      out, sizeof(out))
              && strcmp(out, "c-three") == 0,
          "exclusion_matched_case_insensitively");

    // A null entry inside the exclusion array must not crash or exclude.
    const char *with_null[2] = {nullptr, "b-two"};
    check(story_select_next_excluding(three, 3, "a-one", with_null, 2,
                                      out, sizeof(out))
              && strcmp(out, "c-three") == 0,
          "exclusion_tolerates_null_entry");

    // A null array is simply "no exclusions".
    check(story_select_next_excluding(three, 3, "a-one", nullptr, 3,
                                      out, sizeof(out))
              && strcmp(out, "b-two") == 0,
          "null_exclusion_array_is_no_exclusion");

    // Excluding a story that is not in the list changes nothing.
    const char *unknown[1] = {"not-present"};
    check(story_select_next_excluding(three, 3, "a-one", unknown, 1,
                                      out, sizeof(out))
              && strcmp(out, "b-two") == 0,
          "excluding_unknown_id_is_a_noop");
}

void test_excluded_previous_still_rotates() {
    // The previous story itself being excluded must not wedge selection.
    CsStory three[3];
    make(&three[0], "a-one");
    make(&three[1], "b-two");
    make(&three[2], "c-three");
    char out[CS_MAX_STORY_ID_LEN + 1];
    const char *excluded[1] = {"a-one"};
    // previous == the excluded story: it is not among the candidates, so
    // selection restarts at the first candidate rather than failing.
    check(story_select_next_excluding(three, 3, "a-one", excluded, 1,
                                      out, sizeof(out))
              && strcmp(out, "b-two") == 0,
          "excluded_previous_restarts_at_first_candidate");
}

// ---- serial episodes: pure ordering rule ----------------------------

// Builds a three-episode series plus one standalone story, in the order
// the index would hold them.
void make_series(CsStory *list) {
    make(&list[0], "tsivik-ep1");
    cs_copy_bounded(list[0].series_id, sizeof(list[0].series_id), "tsivik");
    list[0].series_index = 1;
    make(&list[1], "tsivik-ep2");
    cs_copy_bounded(list[1].series_id, sizeof(list[1].series_id), "tsivik");
    list[1].series_index = 2;
    make(&list[2], "tsivik-ep6");
    cs_copy_bounded(list[2].series_id, sizeof(list[2].series_id), "tsivik");
    list[2].series_index = 6;      // gaps are legal — only order matters
    make(&list[3], "anban-huri");  // standalone
}

void test_series_only_the_lowest_unheard_is_offered() {
    CsStory list[4];
    make_series(list);
    bool heard[4] = {false, false, false, false};

    check(story_series_member_allowed(list, 4, 0, heard, nullptr, 0),
          "series_first_episode_offered");
    check(!story_series_member_allowed(list, 4, 1, heard, nullptr, 0),
          "series_second_episode_waits");
    check(!story_series_member_allowed(list, 4, 2, heard, nullptr, 0),
          "series_last_episode_waits");
    // KEYSTONE: the serial rule must change nothing for the rest of the
    // library. A standalone story is always offered.
    check(story_series_member_allowed(list, 4, 3, heard, nullptr, 0),
          "series_standalone_story_unaffected");
}

void test_series_advances_as_episodes_are_heard() {
    CsStory list[4];
    make_series(list);
    bool heard[4] = {true, false, false, false};   // ep1 done

    check(!story_series_member_allowed(list, 4, 0, heard, nullptr, 0),
          "series_heard_episode_not_reoffered");
    check(story_series_member_allowed(list, 4, 1, heard, nullptr, 0),
          "series_advances_to_second_episode");
    check(!story_series_member_allowed(list, 4, 2, heard, nullptr, 0),
          "series_still_holds_back_the_last");

    // With the gap-indexed one the only unheard member, it becomes due —
    // non-contiguous indexes must not strand an episode.
    bool all_but_last[4] = {true, true, false, false};
    check(story_series_member_allowed(list, 4, 2, all_but_last, nullptr, 0),
          "series_gap_index_becomes_due");

    // Whole series heard: nothing from it is offered. The best-effort
    // fallback that keeps the toy from going silent lives in
    // apply_series_rule (needs NVS), not in this pure rule.
    bool everything[4] = {true, true, true, false};
    check(!story_series_member_allowed(list, 4, 0, everything, nullptr, 0)
              && !story_series_member_allowed(list, 4, 1, everything, nullptr, 0)
              && !story_series_member_allowed(list, 4, 2, everything, nullptr, 0),
          "series_fully_heard_offers_nothing");
    check(story_series_member_allowed(list, 4, 3, everything, nullptr, 0),
          "series_fully_heard_standalone_still_offered");
}

// KEYSTONE for the "one new episode at a time" gate: once an episode of a
// series has started this boot, no sibling may follow it — otherwise a
// child could sit through the whole serial in one afternoon.
void test_series_boot_latch_blocks_the_next_episode() {
    CsStory list[4];
    make_series(list);
    bool heard[4] = {true, false, false, false};   // ep1 played this boot
    const char *latched[1] = {"tsivik"};

    check(!story_series_member_allowed(list, 4, 1, heard, latched, 1),
          "series_latch_blocks_next_episode");
    check(story_series_member_allowed(list, 4, 3, heard, latched, 1),
          "series_latch_does_not_touch_standalone_stories");

    // A latch on a DIFFERENT series is irrelevant.
    const char *other[1] = {"some-other-series"};
    check(story_series_member_allowed(list, 4, 1, heard, other, 1),
          "series_latch_is_per_series");
}

void test_series_malformed_pairings_are_standalone() {
    CsStory list[3];
    make(&list[0], "half-a");
    cs_copy_bounded(list[0].series_id, sizeof(list[0].series_id), "tsivik");
    list[0].series_index = 0;                     // id without a position
    make(&list[1], "half-b");
    list[1].series_index = 3;                     // position without an id
    make(&list[2], "tsivik-ep1");
    cs_copy_bounded(list[2].series_id, sizeof(list[2].series_id), "tsivik");
    list[2].series_index = 1;
    bool heard[3] = {false, false, false};

    check(!cs_story_is_serial(&list[0]) && !cs_story_is_serial(&list[1]),
          "series_half_set_pairs_are_not_serial");
    // A half-set entry must not participate in ordering at all — it is an
    // ordinary story that happens to have junk metadata.
    check(story_series_member_allowed(list, 3, 0, heard, nullptr, 0)
              && story_series_member_allowed(list, 3, 1, heard, nullptr, 0),
          "series_half_set_pairs_always_offered");
    check(story_series_member_allowed(list, 3, 2, heard, nullptr, 0),
          "series_half_set_sibling_does_not_block_a_real_episode");
}

void test_series_bounds_and_nulls() {
    CsStory list[4];
    make_series(list);
    bool heard[4] = {false, false, false, false};

    check(!story_series_member_allowed(nullptr, 4, 0, heard, nullptr, 0),
          "series_null_list_refused");
    check(!story_series_member_allowed(list, 4, -1, heard, nullptr, 0),
          "series_negative_index_refused");
    check(!story_series_member_allowed(list, 4, 4, heard, nullptr, 0),
          "series_out_of_range_index_refused");
    // A null heard array means "nothing heard yet", not a crash.
    check(story_series_member_allowed(list, 4, 0, nullptr, nullptr, 0),
          "series_null_heard_array_tolerated");
}

// ---- shout-it-out story pauses (pure planning only) -----------------
//
// Only the arithmetic is testable here. Whether a pause SOUNDS right —
// whether the beat is long enough for a four-year-old, whether the resume
// line lands over silence — is a listening question, and whether the
// estimated byte rate matches a real MP3 is a bench question. Both are
// named in the not-covered note at the end of this file rather than
// dressed up as assertions.

// Bytes of a story that lasts `seconds` at the assumed library rate.
uint32_t story_bytes_for(uint32_t seconds) {
    return seconds * (uint32_t)AREG_STORY_PAUSE_BYTES_PER_SEC;
}

void test_pause_plan_long_story_gets_two() {
    uint32_t at[AREG_STORY_PAUSE_MAX_PER_STORY];
    const uint32_t four_min = story_bytes_for(240);
    const int n = story_pause_plan(four_min, AREG_STORY_PAUSE_BYTES_PER_SEC,
                                   at, AREG_STORY_PAUSE_MAX_PER_STORY);
    check(n == 2, "pause_four_minute_story_plans_two");
    check(at[0] < at[1], "pause_points_ascending");
    // KEYSTONE — the two rules the feature exists to respect: never in the
    // opening, never near the ending.
    check(at[0] >= AREG_STORY_PAUSE_HEAD_MS, "pause_never_in_the_opening");
    check(at[1] + AREG_STORY_PAUSE_TAIL_MS <= 240000UL, "pause_never_near_the_ending");
    check(at[1] - at[0] >= AREG_STORY_PAUSE_MIN_GAP_MS, "pause_points_are_spaced");
}

void test_pause_plan_medium_story_gets_one() {
    uint32_t at[AREG_STORY_PAUSE_MAX_PER_STORY];
    // 2:00 — room for one pause that is neither too early nor too late,
    // but not for two.
    const int n = story_pause_plan(story_bytes_for(120),
                                   AREG_STORY_PAUSE_BYTES_PER_SEC,
                                   at, AREG_STORY_PAUSE_MAX_PER_STORY);
    check(n == 1, "pause_two_minute_story_plans_one");
    check(at[0] >= AREG_STORY_PAUSE_HEAD_MS
              && at[0] + AREG_STORY_PAUSE_TAIL_MS <= 120000UL,
          "pause_single_point_inside_the_guards");
}

void test_pause_plan_short_story_gets_none() {
    uint32_t at[AREG_STORY_PAUSE_MAX_PER_STORY];
    // 1:40 — head + tail already eat 75 s, leaving under one minimum gap.
    check(story_pause_plan(story_bytes_for(100), AREG_STORY_PAUSE_BYTES_PER_SEC,
                           at, AREG_STORY_PAUSE_MAX_PER_STORY) == 0,
          "pause_short_story_plans_none");
    // A story shorter than the guards themselves must not underflow into a
    // huge window.
    check(story_pause_plan(story_bytes_for(30), AREG_STORY_PAUSE_BYTES_PER_SEC,
                           at, AREG_STORY_PAUSE_MAX_PER_STORY) == 0,
          "pause_tiny_story_plans_none");
}

void test_pause_plan_bounds_and_nulls() {
    uint32_t at[AREG_STORY_PAUSE_MAX_PER_STORY];
    check(story_pause_plan(0, AREG_STORY_PAUSE_BYTES_PER_SEC, at, 2) == 0,
          "pause_zero_size_plans_none");
    check(story_pause_plan(story_bytes_for(240), 0, at, 2) == 0,
          "pause_zero_rate_plans_none");
    check(story_pause_plan(story_bytes_for(240), AREG_STORY_PAUSE_BYTES_PER_SEC,
                           nullptr, 2) == 0,
          "pause_null_output_plans_none");
    check(story_pause_plan(story_bytes_for(240), AREG_STORY_PAUSE_BYTES_PER_SEC,
                           at, 0) == 0,
          "pause_zero_capacity_plans_none");
    // A caller with room for one gets one, not a buffer overrun.
    check(story_pause_plan(story_bytes_for(600), AREG_STORY_PAUSE_BYTES_PER_SEC,
                           at, 1) == 1,
          "pause_capacity_one_respected");
    // A very long story still never exceeds the per-story ceiling.
    check(story_pause_plan(story_bytes_for(3600), AREG_STORY_PAUSE_BYTES_PER_SEC,
                           at, AREG_STORY_PAUSE_MAX_PER_STORY)
              <= AREG_STORY_PAUSE_MAX_PER_STORY,
          "pause_ceiling_holds_for_a_long_story");
}

void test_pause_next_at_selection() {
    const uint32_t plan[2] = { 100000UL, 155000UL };

    check(story_pause_next_at_ms(plan, 2, 0, 0, 0) == 100000UL,
          "pause_next_from_story_start_is_the_first_point");
    // A segment that opens PAST a point (a resume landed there) skips it.
    check(story_pause_next_at_ms(plan, 2, 0, 0, 120000UL) == 155000UL,
          "pause_point_behind_the_segment_is_skipped");
    // KEYSTONE — the ceiling. Two fired means no third, ever.
    check(story_pause_next_at_ms(plan, 2, AREG_STORY_PAUSE_MAX_PER_STORY,
                                 100000UL, 0) == 0,
          "pause_ceiling_blocks_a_third");
    // Minimum spacing after a pause that already fired.
    const uint32_t close[2] = { 100000UL, 110000UL };
    check(story_pause_next_at_ms(close, 2, 1, 100000UL, 100000UL) == 0,
          "pause_too_soon_after_the_last_one_is_refused");
    // Nothing armed when a resume lands within the lead of the next point,
    // so a resumed story never fires one instantly.
    check(story_pause_next_at_ms(plan, 2, 0, 0, 99000UL) == 155000UL,
          "pause_inside_the_lead_window_is_skipped");
    check(story_pause_next_at_ms(nullptr, 2, 0, 0, 0) == 0,
          "pause_null_plan_arms_nothing");
    check(story_pause_next_at_ms(plan, 0, 0, 0, 0) == 0,
          "pause_empty_plan_arms_nothing");
}

void test_pause_rate_measurement() {
    // 24000 B over 1 s is the assumed rate — but a 1 s sample is too short
    // to trust, so it is refused.
    check(story_pause_measured_bytes_per_sec(0, 24000UL, 1000UL) == 0,
          "pause_short_sample_refused");
    // 100 s of a 192 kbps file.
    check(story_pause_measured_bytes_per_sec(0, 2400000UL, 100000UL) == 24000UL,
          "pause_measures_the_real_rate");
    // Offsets are absolute, so a mid-file segment measures the same rate.
    check(story_pause_measured_bytes_per_sec(1000000UL, 3400000UL, 100000UL) == 24000UL,
          "pause_measurement_is_offset_relative");
    check(story_pause_measured_bytes_per_sec(3400000UL, 1000000UL, 100000UL) == 0,
          "pause_backwards_sample_refused");
    // Nonsense rates (a stalled decoder, a corrupt clock) never replace a
    // sane estimate.
    check(story_pause_measured_bytes_per_sec(0, 1000UL, 100000UL) == 0,
          "pause_implausibly_slow_sample_refused");
    check(story_pause_measured_bytes_per_sec(0, 30000000UL, 100000UL) == 0,
          "pause_implausibly_fast_sample_refused");
}

void test_pause_variant_rotation() {
    check(story_pause_variant_of(0) == 1 && story_pause_variant_of(1) == 2
              && story_pause_variant_of(2) == 3 && story_pause_variant_of(3) == 4,
          "pause_variants_rotate_in_order");
    check(story_pause_variant_of(4) == 1, "pause_variants_wrap");
    // Every value is a real clip id (shout-1..shout-N), never a 0 or an
    // out-of-range index that would build a path to a file that cannot exist.
    bool in_range = true;
    for (uint32_t c = 0; c < 32; c++) {
        const int v = story_pause_variant_of(c);
        if (v < 1 || v > AREG_STORY_PAUSE_VARIANTS) in_range = false;
    }
    check(in_range, "pause_variant_always_addresses_a_real_clip");
}

void run_all() {
    s_pass = 0;
    s_fail = 0;
    Serial.println("[sel-test] ---- story-selection tests ----");
    Serial.printf("[sel-test] CS_MAX_STORIES=%d CS_MAX_STORY_ID_LEN=%d CS_MAX_PATH_LEN=%d\n",
                  CS_MAX_STORIES, CS_MAX_STORY_ID_LEN, CS_MAX_PATH_LEN);
    Serial.flush();

    test_zero_stories();
    test_one_story_always_selected();
    test_two_stories_alternate();
    test_three_stories_rotate();
    test_rotation_sequence_and_no_repeat();
    test_unknown_previous_selects_first();
    test_previous_match_is_case_insensitive();
    test_index_order_preserved();
    test_output_bounds();
    test_eligibility_rules();
    test_cache_path_safety();
    test_failed_story_is_skipped();
    test_exclusion_never_starves_playback();
    test_exclusion_two_story_prefers_playable();
    test_exclusion_matching_and_bounds();
    test_excluded_previous_still_rotates();
    test_series_only_the_lowest_unheard_is_offered();
    test_series_advances_as_episodes_are_heard();
    test_series_boot_latch_blocks_the_next_episode();
    test_series_malformed_pairings_are_standalone();
    test_series_bounds_and_nulls();
    test_pause_plan_long_story_gets_two();
    test_pause_plan_medium_story_gets_one();
    test_pause_plan_short_story_gets_none();
    test_pause_plan_bounds_and_nulls();
    test_pause_next_at_selection();
    test_pause_rate_measurement();
    test_pause_variant_rotation();

    Serial.printf("[sel-test] TOTAL pass=%d fail=%d\n", s_pass, s_fail);
    Serial.println(s_fail == 0 ? "[sel-test] RESULT PASS" : "[sel-test] RESULT FAIL");
    Serial.println("[sel-test] NOT covered here (needs the hardware bench): real NVS "
                   "durability across a power cycle, real SD index reads, "
                   "story_select_load_eligible / _resolve_path end to end, and "
                   "pause/resume in the live state machine. Serial episodes: "
                   "only the PURE ordering rule is covered — the NVS heard-set "
                   "read, the boot latch surviving a real session, and the "
                   "serialnext clip actually playing all need the bench. "
                   "Story pauses: only the arithmetic is covered — arming "
                   "against the real millis() clock, the barge-in seam "
                   "actually stopping playback, resume landing on the same "
                   "byte, whether the assumed 24000 B/s matches the real "
                   "MP3s, and whether a 3 s beat is right for a child are "
                   "ALL bench/listening questions.");
    Serial.flush();
}

constexpr uint32_t kStartMs       = 20000UL;  // 20 s arm delay (monitor attach)
constexpr uint32_t kStatusEveryMs = 5000UL;

}  // namespace

void story_select_test_tick() {
    static bool s_done = false;
    static uint32_t s_last_status_ms = 0;
    if (s_done) {
        return;
    }
    const uint32_t now = millis();
    if (now < kStartMs) {
        if (s_last_status_ms == 0 || now - s_last_status_ms >= kStatusEveryMs) {
            s_last_status_ms = now;
            Serial.println("[sel-test] armed; running at ms>=20000");
            Serial.flush();
        }
        return;
    }
    s_done = true;
    run_all();
}

#endif  // AREG_STORY_SELECT_TEST_BENCH
