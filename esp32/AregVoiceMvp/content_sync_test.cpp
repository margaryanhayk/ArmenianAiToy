// -------------------------------------------------------------
// AregVoiceMvp / content_sync_test.cpp — see content_sync_test.h.
// Entire file compiles out unless AREG_CONTENT_SYNC_TEST_BENCH is set.
// -------------------------------------------------------------
#ifdef AREG_CONTENT_SYNC_TEST_BENCH

#include "content_sync_test.h"

#include <Arduino.h>
#include <ArduinoJson.h>

#include "content_sync_rules.h"
#include "content_sync_model.h"

namespace {

int s_pass = 0;
int s_fail = 0;

void check(bool condition, const char *name) {
    if (condition) {
        s_pass++;
        Serial.printf("[cs-test] PASS %s\n", name);
    } else {
        s_fail++;
        Serial.printf("[cs-test] FAIL %s\n", name);
    }
    Serial.flush();
}

// Shared parse targets. Every test declared its own
// `static CsStory out[CS_MAX_STORIES]` until CS_MAX_CLIPS grew from 5 to
// 7 for the welcome flow's offer lines: eleven copies of a table that had
// just gained ~2.7 KB each overflowed DRAM and the bench build stopped
// linking. Tests run strictly one after another, so one set of buffers is
// all that was ever needed.
CsStory g_story_scratch[CS_MAX_STORIES];
CsStory g_story_scratch2[CS_MAX_STORIES];   // for round-trip compares
CsVoice g_voice_scratch[CS_MAX_VOICE];

const char *kShaA = "4ba0969646dfcb34ede49b3c82ac234a55299ab6789354f0ccbc6beb64f7e631";
const char *kShaB = "b1b0969646dfcb34ede49b3c82ac234a55299ab6789354f0ccbc6beb64f7e631";
const char *kShaC = "c2c0969646dfcb34ede49b3c82ac234a55299ab6789354f0ccbc6beb64f7e631";

// Adds one manifest item to `arr`.
void add_item(JsonArray arr, const char *id, int version, const char *sha,
              long size, const char *url, bool enabled = true,
              const char *title = "t") {
    JsonObject o = arr.add<JsonObject>();
    o["storyId"]   = id;
    o["version"]   = version;
    o["title"]     = title;
    o["audioUrl"]  = url;
    o["sha256"]    = sha;
    o["sizeBytes"] = size;
    o["enabled"]   = enabled;
}

// ---- 1. story-id validation ---------------------------------------

void test_story_id_rules() {
    check(cs_is_valid_story_id("anban-huri"), "id_accepts_kebab_case");
    check(cs_is_valid_story_id("little_cloud_2"), "id_accepts_underscore_and_digits");

    check(!cs_is_valid_story_id(""), "id_rejects_empty");
    check(!cs_is_valid_story_id(nullptr), "id_rejects_null");

    // Traversal shapes are unrepresentable, not merely filtered: '.', '/'
    // and '\\' are simply not in the allowlist.
    check(!cs_is_valid_story_id(".."), "id_rejects_dotdot");
    check(!cs_is_valid_story_id("../../etc/passwd"), "id_rejects_traversal_unix");
    check(!cs_is_valid_story_id("..\\..\\windows"), "id_rejects_traversal_windows");
    check(!cs_is_valid_story_id("/etc/shadow"), "id_rejects_absolute_path");
    check(!cs_is_valid_story_id("a/b"), "id_rejects_slash");
    check(!cs_is_valid_story_id("c:file"), "id_rejects_colon");
    check(!cs_is_valid_story_id("a.b"), "id_rejects_dot");

    check(!cs_is_valid_story_id("has space"), "id_rejects_space");
    check(!cs_is_valid_story_id("tab\there"), "id_rejects_tab");
    check(!cs_is_valid_story_id("nl\nhere"), "id_rejects_newline");
    check(!cs_is_valid_story_id("bell\x07"), "id_rejects_control_char");

    // Uppercase is rejected so two ids the backend considers identical can
    // never become two different filenames on a case-preserving card.
    check(!cs_is_valid_story_id("Anban-Huri"), "id_rejects_uppercase");

    char too_long[CS_MAX_STORY_ID_LEN + 8];
    memset(too_long, 'a', sizeof(too_long) - 1);
    too_long[sizeof(too_long) - 1] = '\0';
    check(!cs_is_valid_story_id(too_long), "id_rejects_over_max_length");

    char at_max[CS_MAX_STORY_ID_LEN + 1];
    memset(at_max, 'a', CS_MAX_STORY_ID_LEN);
    at_max[CS_MAX_STORY_ID_LEN] = '\0';
    check(cs_is_valid_story_id(at_max), "id_accepts_exactly_max_length");
}

// ---- 2. sha / size validation --------------------------------------

void test_sha_and_size_rules() {
    check(cs_is_sha256_hex(kShaA), "sha_accepts_64_hex");
    check(cs_is_sha256_hex("ABCDEF0123456789abcdef0123456789ABCDEF0123456789abcdef0123456789"),
          "sha_accepts_mixed_case_hex");
    check(!cs_is_sha256_hex(""), "sha_rejects_empty");
    check(!cs_is_sha256_hex("abc123"), "sha_rejects_truncated");
    check(!cs_is_sha256_hex(nullptr), "sha_rejects_null");
    {
        char non_hex[65];
        memset(non_hex, 'z', 64);
        non_hex[64] = '\0';
        check(!cs_is_sha256_hex(non_hex), "sha_rejects_64_non_hex");
    }
    {
        char too_long[70];
        memset(too_long, 'a', 69);
        too_long[69] = '\0';
        check(!cs_is_sha256_hex(too_long), "sha_rejects_over_64");
    }

    check(cs_is_valid_size(1), "size_accepts_one");
    check(cs_is_valid_size(CS_MAX_STORY_BYTES), "size_accepts_max");
    check(!cs_is_valid_size(0), "size_rejects_zero");
    check(!cs_is_valid_size(-1), "size_rejects_negative");
    check(!cs_is_valid_size(CS_MAX_STORY_BYTES + 1), "size_rejects_over_max");

    check(cs_normalize_version(0) == 1, "version_clamps_zero_to_one");
    check(cs_normalize_version(-5) == 1, "version_clamps_negative_to_one");
    check(cs_normalize_version(3) == 3, "version_passes_through");
}

// ---- 3. cache-path construction ------------------------------------

void test_cache_paths() {
    char a[CS_MAX_PATH_LEN], b[CS_MAX_PATH_LEN], c[CS_MAX_PATH_LEN];

    check(cs_build_cache_path(a, sizeof(a), "anban-huri", 1), "path_builds");
    check(strcmp(a, "/stories/anban-huri-v1.mp3") == 0, "path_exact_format");

    // Different ids must never collide.
    check(cs_build_cache_path(b, sizeof(b), "little-cloud", 1), "path_builds_second");
    check(strcmp(a, b) != 0, "path_unique_per_story_id");

    // Different versions must be distinguishable.
    check(cs_build_cache_path(c, sizeof(c), "anban-huri", 2), "path_builds_v2");
    check(strcmp(a, c) != 0, "path_unique_per_version");
    check(strcmp(c, "/stories/anban-huri-v2.mp3") == 0, "path_version_in_name");

    // An invalid id can never produce a path.
    check(!cs_build_cache_path(a, sizeof(a), "../evil", 1), "path_refuses_traversal_id");
    check(!cs_build_cache_path(a, sizeof(a), "", 1), "path_refuses_empty_id");

    // A buffer too small must fail rather than truncate into another file.
    char tiny[8];
    check(!cs_build_cache_path(tiny, sizeof(tiny), "anban-huri", 1),
          "path_refuses_short_buffer");

    // Temp paths are unique per story AND version, so two syncs in one run
    // can never share a .part file.
    char t1[CS_MAX_PATH_LEN], t2[CS_MAX_PATH_LEN], t3[CS_MAX_PATH_LEN];
    check(cs_build_temp_path(t1, sizeof(t1), "anban-huri", 1), "temp_builds");
    check(cs_build_temp_path(t2, sizeof(t2), "little-cloud", 1), "temp_builds_second");
    check(cs_build_temp_path(t3, sizeof(t3), "anban-huri", 2), "temp_builds_v2");
    check(strcmp(t1, t2) != 0, "temp_unique_per_story_id");
    check(strcmp(t1, t3) != 0, "temp_unique_per_version");
    check(strcmp(t1, "/tmp/anban-huri-v1.mp3.part") == 0, "temp_exact_format");
}

// ---- 4. manifest parsing -------------------------------------------

void test_manifest_three_stories() {
    JsonDocument doc;
    JsonArray arr = doc["stories"].to<JsonArray>();
    add_item(arr, "anban-huri", 1, kShaA, 4654560, "/api/devices/content-file?storyId=anban-huri");
    add_item(arr, "little-cloud", 1, kShaB, 446880, "/api/devices/content-file?storyId=little-cloud");
    add_item(arr, "hedgehog-apple", 2, kShaC, 333333, "/api/devices/content-file?storyId=hedgehog-apple");

    CsStory *out = g_story_scratch;
    CsManifestStats st{};
    const int n = cs_manifest_parse(doc["stories"].as<JsonArrayConst>(),
                                    out, CS_MAX_STORIES, &st);

    check(n == 3, "manifest_parses_three");
    check(st.accepted == 3 && st.invalid == 0, "manifest_stats_three_accepted");
    // Manifest ORDER is preserved.
    check(strcmp(out[0].story_id, "anban-huri") == 0, "manifest_order_0");
    check(strcmp(out[1].story_id, "little-cloud") == 0, "manifest_order_1");
    check(strcmp(out[2].story_id, "hedgehog-apple") == 0, "manifest_order_2");
    check(out[2].version == 2, "manifest_keeps_version");
    check(out[0].size_bytes == 4654560, "manifest_keeps_size");
    // The query string is preserved EXACTLY — nothing appended, nothing
    // duplicated.
    check(strcmp(out[1].audio_url,
                 "/api/devices/content-file?storyId=little-cloud") == 0,
          "manifest_preserves_query_string");
    check(strcmp(out[0].cache_path, "/stories/anban-huri-v1.mp3") == 0,
          "manifest_builds_cache_path");
}

void test_manifest_legacy_single_story() {
    // The pre-multi-story backend shape: one item, bare audioUrl.
    JsonDocument doc;
    JsonArray arr = doc["stories"].to<JsonArray>();
    add_item(arr, "anban-huri", 1, kShaA, 4654560, "/api/devices/content-file");

    CsStory *out = g_story_scratch;
    CsManifestStats st{};
    const int n = cs_manifest_parse(doc["stories"].as<JsonArrayConst>(),
                                    out, CS_MAX_STORIES, &st);
    check(n == 1, "manifest_legacy_single_item");
    check(strcmp(out[0].audio_url, "/api/devices/content-file") == 0,
          "manifest_legacy_bare_url_preserved");
}

void test_manifest_empty() {
    JsonDocument doc;
    doc["stories"].to<JsonArray>();
    CsStory *out = g_story_scratch;
    CsManifestStats st{};
    const int n = cs_manifest_parse(doc["stories"].as<JsonArrayConst>(),
                                    out, CS_MAX_STORIES, &st);
    check(n == 0 && st.offered == 0, "manifest_empty_yields_zero");
}

void test_manifest_invalid_item_does_not_block_siblings() {
    JsonDocument doc;
    JsonArray arr = doc["stories"].to<JsonArray>();
    add_item(arr, "good-one", 1, kShaA, 1000, "/u");
    add_item(arr, "bad-sha", 1, "abc123", 1000, "/u");          // truncated sha
    add_item(arr, "", 1, kShaB, 1000, "/u");                     // empty id
    add_item(arr, "../evil", 1, kShaB, 1000, "/u");              // traversal id
    add_item(arr, "zero-size", 1, kShaB, 0, "/u");               // zero size
    add_item(arr, "huge", 1, kShaB, CS_MAX_STORY_BYTES + 1, "/u"); // oversized
    add_item(arr, "no-url", 1, kShaB, 1000, "");                 // empty url
    add_item(arr, "good-two", 1, kShaC, 2000, "/u");

    CsStory *out = g_story_scratch;
    CsManifestStats st{};
    const int n = cs_manifest_parse(doc["stories"].as<JsonArrayConst>(),
                                    out, CS_MAX_STORIES, &st);

    check(n == 2, "manifest_keeps_only_valid_siblings");
    check(strcmp(out[0].story_id, "good-one") == 0, "manifest_valid_first_survives");
    check(strcmp(out[1].story_id, "good-two") == 0, "manifest_valid_last_survives");
    check(st.invalid == 6, "manifest_counts_six_invalid");
}

void test_manifest_disabled_item_skipped() {
    JsonDocument doc;
    JsonArray arr = doc["stories"].to<JsonArray>();
    add_item(arr, "on-one", 1, kShaA, 1000, "/u", true);
    add_item(arr, "off-one", 1, kShaB, 1000, "/u", false);

    CsStory *out = g_story_scratch;
    CsManifestStats st{};
    const int n = cs_manifest_parse(doc["stories"].as<JsonArrayConst>(),
                                    out, CS_MAX_STORIES, &st);
    check(n == 1, "manifest_disabled_not_synced");
    check(st.disabled == 1, "manifest_counts_disabled");
    check(strcmp(out[0].story_id, "on-one") == 0, "manifest_enabled_sibling_kept");
}

void test_manifest_duplicates() {
    JsonDocument doc;
    JsonArray arr = doc["stories"].to<JsonArray>();
    add_item(arr, "anban-huri", 1, kShaA, 111, "/first");
    add_item(arr, "anban-huri", 2, kShaB, 222, "/second");   // exact duplicate id
    add_item(arr, "other-story", 1, kShaC, 333, "/third");

    CsStory *out = g_story_scratch;
    CsManifestStats st{};
    const int n = cs_manifest_parse(doc["stories"].as<JsonArrayConst>(),
                                    out, CS_MAX_STORIES, &st);
    check(n == 2, "manifest_duplicate_dropped");
    check(st.duplicate == 1, "manifest_counts_duplicate");
    check(out[0].size_bytes == 111, "manifest_duplicate_keeps_first");

    check(cs_story_ids_equal("Anban-Huri", "anban-huri"), "ids_equal_case_insensitive");
    check(!cs_story_ids_equal("anban-huri", "little-cloud"), "ids_differ");
}

void test_manifest_truncation() {
    JsonDocument doc;
    JsonArray arr = doc["stories"].to<JsonArray>();
    const int offered = CS_MAX_STORIES + 3;
    for (int i = 0; i < offered; i++) {
        char id[16];
        snprintf(id, sizeof(id), "story-%d", i);
        add_item(arr, id, 1, kShaA, 1000 + i, "/u");
    }

    CsStory *out = g_story_scratch;
    CsManifestStats st{};
    const int n = cs_manifest_parse(doc["stories"].as<JsonArrayConst>(),
                                    out, CS_MAX_STORIES, &st);

    check(n == CS_MAX_STORIES, "manifest_truncates_to_max");
    check(st.offered == offered, "manifest_reports_offered");
    check(st.truncated == 3, "manifest_reports_truncated_count");
    check(strcmp(out[0].story_id, "story-0") == 0, "manifest_truncation_keeps_first");

    check(cs_accepted_count(offered) == CS_MAX_STORIES, "accepted_count_caps");
    check(cs_truncated_count(offered) == 3, "truncated_count_math");
    check(cs_accepted_count(2) == 2, "accepted_count_under_cap");
    check(cs_truncated_count(2) == 0, "truncated_count_zero_under_cap");
}

// ---- 5. index serialize / parse / migrate ---------------------------

void fill(CsStory *s, const char *id, int version, const char *sha, long size) {
    memset(s, 0, sizeof(*s));
    cs_copy_bounded(s->story_id, sizeof(s->story_id), id);
    cs_copy_bounded(s->sha256, sizeof(s->sha256), sha);
    cs_copy_bounded(s->title, sizeof(s->title), "T");
    s->version = version;
    s->size_bytes = size;
    s->verified = true;
    cs_build_cache_path(s->cache_path, sizeof(s->cache_path), id, version);
}

void test_index_round_trip_three() {
    static CsStory active[3];
    fill(&active[0], "anban-huri", 1, kShaA, 4654560);
    fill(&active[1], "little-cloud", 1, kShaB, 446880);
    fill(&active[2], "hedgehog-apple", 2, kShaC, 333333);

    JsonDocument built;
    cs_index_build(built, active, 3, "anban-huri");

    String json;
    serializeJson(built, json);
    check(json.length() > 0, "index_serializes");
    // Track the CONSTANT, not a literal. Written as a literal "2" this
    // assertion silently rotted at the v3 and v4 bumps — what this test is
    // actually about is the round trip, not which number is current.
    check(json.indexOf("\"schemaVersion\":" + String(CS_INDEX_SCHEMA_VERSION)) >= 0,
          "index_has_current_schema_version");
    check(json.indexOf("hedgehog-apple") >= 0, "index_contains_third_story");

    // Legacy compatibility mirror: the flat fields the three existing
    // readers still parse must point at the CONFIGURED story.
    check(built["storyId"].as<const char *>() != nullptr, "index_mirror_present");
    check(strcmp(built["storyId"] | "", "anban-huri") == 0, "index_mirror_targets_configured");
    check(strcmp(built["file"] | "", "/stories/anban-huri-v1.mp3") == 0,
          "index_mirror_file_matches");

    JsonDocument reparsed;
    check(deserializeJson(reparsed, json) == DeserializationError::Ok, "index_reparses");

    CsStory *back = g_story_scratch2;
    int schema = 0;
    const int n = cs_index_parse(reparsed, back, CS_MAX_STORIES, &schema);
    check(schema == CS_INDEX_SCHEMA_VERSION, "index_round_trip_schema");
    check(n == 3, "index_round_trip_count");
    check(strcmp(back[0].story_id, "anban-huri") == 0, "index_round_trip_order_0");
    check(strcmp(back[2].story_id, "hedgehog-apple") == 0, "index_round_trip_order_2");
    check(back[2].version == 2, "index_round_trip_version");
    check(back[1].size_bytes == 446880, "index_round_trip_size");
    check(back[0].verified, "index_round_trip_verified_flag");
    check(strcmp(back[1].cache_path, "/stories/little-cloud-v1.mp3") == 0,
          "index_round_trip_cache_path");
}

void test_index_mirror_falls_back_to_first() {
    static CsStory active[2];
    fill(&active[0], "little-cloud", 1, kShaB, 446880);
    fill(&active[1], "hedgehog-apple", 1, kShaC, 333333);

    JsonDocument built;
    cs_index_build(built, active, 2, "not-present-story");
    check(strcmp(built["storyId"] | "", "little-cloud") == 0,
          "index_mirror_falls_back_to_first");
}

void test_index_legacy_migration() {
    // Exactly the shape the single-story build wrote.
    const char *legacy =
        "{\"storyId\":\"anban-huri\",\"version\":1,"
        "\"sha256\":\"4ba0969646dfcb34ede49b3c82ac234a55299ab6789354f0ccbc6beb64f7e631\","
        "\"file\":\"/stories/anban-huri-v1.mp3\",\"sizeBytes\":4654560}";
    JsonDocument doc;
    check(deserializeJson(doc, legacy) == DeserializationError::Ok, "legacy_index_parses");

    CsStory *out = g_story_scratch;
    int schema = 0;
    const int n = cs_index_parse(doc, out, CS_MAX_STORIES, &schema);

    check(schema == 1, "legacy_index_detected_as_v1");
    check(n == 1, "legacy_index_migrates_one_entry");
    check(strcmp(out[0].story_id, "anban-huri") == 0, "legacy_index_story_id");
    check(strcmp(out[0].cache_path, "/stories/anban-huri-v1.mp3") == 0,
          "legacy_index_preserves_existing_mp3_path");
    check(out[0].size_bytes == 4654560, "legacy_index_size");
    // v1 never wrote "verified"; it is inferred true because v1 only wrote
    // its index after a full SHA-256 verification.
    check(out[0].verified, "legacy_index_infers_verified");
}

void test_index_malformed_and_hostile() {
    CsStory *out = g_story_scratch;
    int schema = 0;

    // Legacy object missing the file field.
    {
        JsonDocument doc;
        deserializeJson(doc, "{\"storyId\":\"anban-huri\",\"version\":1}");
        check(cs_index_parse(doc, out, CS_MAX_STORIES, &schema) == 0,
              "legacy_index_missing_fields_yields_zero");
    }
    // Legacy object with a relative (non-absolute) file path.
    {
        JsonDocument doc;
        deserializeJson(doc,
            "{\"storyId\":\"anban-huri\",\"sha256\":"
            "\"4ba0969646dfcb34ede49b3c82ac234a55299ab6789354f0ccbc6beb64f7e631\","
            "\"file\":\"relative/path.mp3\"}");
        check(cs_index_parse(doc, out, CS_MAX_STORIES, &schema) == 0,
              "legacy_index_relative_path_rejected");
    }
    // An empty object is simply "no entries", never an error.
    {
        JsonDocument doc;
        deserializeJson(doc, "{}");
        check(cs_index_parse(doc, out, CS_MAX_STORIES, &schema) == 0,
              "empty_index_yields_zero");
    }
    // v2 with a hostile cachePath / bad id — entry dropped, siblings kept.
    {
        JsonDocument doc;
        deserializeJson(doc,
            "{\"schemaVersion\":2,\"stories\":["
            "{\"storyId\":\"../evil\",\"version\":1,\"sha256\":"
            "\"4ba0969646dfcb34ede49b3c82ac234a55299ab6789354f0ccbc6beb64f7e631\","
            "\"cachePath\":\"/stories/x.mp3\",\"verified\":true},"
            "{\"storyId\":\"good\",\"version\":1,\"sha256\":"
            "\"b1b0969646dfcb34ede49b3c82ac234a55299ab6789354f0ccbc6beb64f7e631\","
            "\"cachePath\":\"/stories/good-v1.mp3\",\"verified\":true}]}");
        const int n = cs_index_parse(doc, out, CS_MAX_STORIES, &schema);
        check(n == 1, "v2_index_drops_hostile_entry");
        check(strcmp(out[0].story_id, "good") == 0, "v2_index_keeps_valid_sibling");
    }
}

// ---- 6. already-current decision (metadata half) --------------------

void test_already_current_metadata() {
    CsStory entry;
    fill(&entry, "anban-huri", 1, kShaA, 4654560);

    check(cs_entry_matches_manifest(&entry, "anban-huri", 1, kShaA, 4654560),
          "current_matches_identical");

    check(!cs_entry_matches_manifest(&entry, "anban-huri", 2, kShaA, 4654560),
          "current_rejects_version_change");
    check(!cs_entry_matches_manifest(&entry, "anban-huri", 1, kShaB, 4654560),
          "current_rejects_sha_change");
    check(!cs_entry_matches_manifest(&entry, "anban-huri", 1, kShaA, 999),
          "current_rejects_size_change");
    check(!cs_entry_matches_manifest(&entry, "little-cloud", 1, kShaA, 4654560),
          "current_rejects_other_story");
    check(!cs_entry_matches_manifest(nullptr, "anban-huri", 1, kShaA, 4654560),
          "current_rejects_null_entry");

    // An unverified entry can never authorize skipping a download.
    entry.verified = false;
    check(!cs_entry_matches_manifest(&entry, "anban-huri", 1, kShaA, 4654560),
          "current_rejects_unverified_entry");

    // Hash comparison tolerates case, since an older writer may not have
    // lowercased.
    CsStory upper;
    fill(&upper, "anban-huri", 1, kShaA, 10);
    for (char *p = upper.sha256; *p; ++p) {
        if (*p >= 'a' && *p <= 'f') *p = (char)(*p - 'a' + 'A');
    }
    check(cs_entry_matches_manifest(&upper, "anban-huri", 1, kShaA, 10),
          "current_sha_compare_case_insensitive");
}

void test_copy_bounded() {
    char dst[8];
    check(cs_copy_bounded(dst, sizeof(dst), "abc") && strcmp(dst, "abc") == 0,
          "copy_bounded_fits");
    check(!cs_copy_bounded(dst, sizeof(dst), "0123456789"), "copy_bounded_reports_truncation");
    check(strlen(dst) == sizeof(dst) - 1, "copy_bounded_nul_terminates");
}

// ---- welcome flow: voice clips, mode flags, offer kinds (schema v4) ----

void test_clip_kinds_and_bounds() {
    check(cs_is_valid_clip_kind("intro"), "clipkind_intro");
    check(cs_is_valid_clip_kind("question2"), "clipkind_question2");
    check(cs_is_valid_clip_kind("offer"), "clipkind_offer");
    check(cs_is_valid_clip_kind("reoffer"), "clipkind_reoffer");
    check(cs_is_valid_clip_kind("serialnext"), "clipkind_serialnext");
    // "serialnext" is exactly CS_CLIP_KIND_LEN, so the buffer must hold it
    // without truncating — a clipped kind would never resolve on the card.
    check(strlen(CS_CLIP_KIND_SERIALNEXT) == CS_CLIP_KIND_LEN,
          "clipkind_serialnext_exactly_fills_the_bound");
    check(!cs_is_valid_clip_kind("serial-next"), "clipkind_rejects_serial_next_hyphen");

    check(!cs_is_valid_clip_kind("Intro"), "clipkind_rejects_uppercase");
    check(!cs_is_valid_clip_kind("../x"), "clipkind_rejects_traversal");
    // The name the backend deliberately did NOT use, because it is 11
    // characters against a 10-character bound.
    check(!cs_is_valid_clip_kind("offer-again"), "clipkind_rejects_offer_again");

    char path[CS_MAX_PATH_LEN];
    check(cs_build_clip_cache_path(path, sizeof(path), "anban-huri", 2, "offer")
              && strcmp(path, "/stories/anban-huri-v2-offer.mp3") == 0,
          "clip_path_offer");
    check(cs_build_clip_cache_path(path, sizeof(path), "anban-huri", 2, "reoffer")
              && strcmp(path, "/stories/anban-huri-v2-reoffer.mp3") == 0,
          "clip_path_reoffer");
    check(cs_build_clip_cache_path(path, sizeof(path), "tsivik-ep1", 1, "serialnext")
              && strcmp(path, "/stories/tsivik-ep1-v1-serialnext.mp3") == 0,
          "clip_path_serialnext");

    // A story may now legitimately carry all seven kinds at once.
    JsonDocument doc;
    JsonArray arr = doc["stories"].to<JsonArray>();
    add_item(arr, "anban-huri", 1, kShaA, 100, "/u");
    JsonArray clips = arr[0]["clips"].to<JsonArray>();
    const char *kinds[] = {"intro", "question", "question1", "question2",
                           "summary", "offer", "reoffer"};
    for (int i = 0; i < 7; i++) {
        JsonObject c = clips.add<JsonObject>();
        c["kind"]      = kinds[i];
        c["sha256"]    = kShaB;
        c["sizeBytes"] = 10 + i;
    }
    CsStory *out = g_story_scratch;
    CsManifestStats stats{};
    const int n = cs_manifest_parse(doc["stories"].as<JsonArrayConst>(),
                                    out, CS_MAX_STORIES, &stats);
    check(n == 1 && out[0].clip_count == 7, "story_carries_all_seven_clip_kinds");
}

void test_voice_manifest_parse() {
    JsonDocument doc;
    JsonArray arr = doc["voice"].to<JsonArray>();
    auto add_voice = [&](const char *id, int version, const char *sha,
                         long size, bool enabled) {
        JsonObject o = arr.add<JsonObject>();
        o["voiceId"]   = id;
        o["version"]   = version;
        o["sha256"]    = sha;
        o["sizeBytes"] = size;
        o["enabled"]   = enabled;
    };
    add_voice("greet-01", 1, kShaA, 100, true);
    add_voice("", 1, kShaB, 100, true);              // no id
    add_voice("bad-sha", 1, "xx", 100, true);        // bad sha
    add_voice("no-size", 1, kShaB, 0, true);         // no size
    add_voice("greet-01", 2, kShaC, 200, true);      // duplicate
    add_voice("off-clip", 1, kShaB, 100, false);     // disabled
    add_voice("ask-any", 1, kShaC, 300, true);

    CsVoice *out = g_voice_scratch;
    const int n = cs_manifest_parse_voice(doc["voice"].as<JsonArrayConst>(),
                                          out, CS_MAX_VOICE);
    check(n == 2, "voice_manifest_keeps_only_valid");
    check(strcmp(out[0].voice_id, "greet-01") == 0 && out[0].size_bytes == 100,
          "voice_manifest_duplicate_keeps_first");
    check(strcmp(out[1].voice_id, "ask-any") == 0, "voice_manifest_keeps_ask_any");

    check(cs_voice_is_greeting("greet-01"), "voice_greeting_prefix_matches");
    check(!cs_voice_is_greeting("ask-any"), "voice_greeting_prefix_excludes_ask");
    check(!cs_voice_is_greeting("greeting-01"), "voice_greeting_prefix_is_exact");

    char path[CS_MAX_PATH_LEN];
    check(cs_build_voice_cache_path(path, sizeof(path), "greet-01", 3)
              && strcmp(path, "/voice/greet-01-v3.mp3") == 0,
          "voice_cache_path");
    check(!cs_build_voice_cache_path(path, sizeof(path), "../x", 1),
          "voice_cache_path_rejects_traversal");
    check(cs_build_voice_temp_path(path, sizeof(path), "greet-01", 3)
              && strcmp(path, "/tmp/v-greet-01-v3.mp3.part") == 0,
          "voice_temp_path_prefixed");
}

void test_voice_manifest_truncation() {
    JsonDocument doc;
    JsonArray arr = doc["voice"].to<JsonArray>();
    for (int i = 0; i < CS_MAX_VOICE + 5; i++) {
        char id[24];
        snprintf(id, sizeof(id), "greet-%02d", i);
        JsonObject o = arr.add<JsonObject>();
        o["voiceId"]   = id;
        o["version"]   = 1;
        o["sha256"]    = kShaA;
        o["sizeBytes"] = 100;
        o["enabled"]   = true;
    }
    CsVoice *out = g_voice_scratch;
    const int n = cs_manifest_parse_voice(doc["voice"].as<JsonArrayConst>(),
                                          out, CS_MAX_VOICE);
    check(n == CS_MAX_VOICE, "voice_manifest_truncates_without_overrun");
}

void test_ask_voice_id() {
    char id[16];
    check(cs_build_ask_voice_id(id, sizeof(id), true, true, true, true)
              && strcmp(id, "ask-sgrc") == 0, "ask_id_all_four");
    check(cs_build_ask_voice_id(id, sizeof(id), true, false, false, false)
              && strcmp(id, "ask-s") == 0, "ask_id_story_only");
    // Fixed s,g,r,c order — otherwise the backend would have to ship every
    // permutation instead of 15 combinations.
    check(cs_build_ask_voice_id(id, sizeof(id), false, false, true, true)
              && strcmp(id, "ask-rc") == 0, "ask_id_fixed_letter_order");
    check(cs_build_ask_voice_id(id, sizeof(id), true, false, true, false)
              && strcmp(id, "ask-sr") == 0, "ask_id_skips_disabled");
    // Nothing enabled → no honest prompt exists; the caller must not fall
    // back to something that offers a mode the parent switched off.
    check(!cs_build_ask_voice_id(id, sizeof(id), false, false, false, false),
          "ask_id_refuses_when_nothing_enabled");
    char tiny[4];
    check(!cs_build_ask_voice_id(tiny, sizeof(tiny), true, true, true, true),
          "ask_id_refuses_on_truncation");
    // Every id it can produce must be a legal id (it becomes an SD path).
    check(cs_is_valid_story_id("ask-sgrc"), "ask_id_is_a_valid_id");
}

void test_index_v4_round_trip() {
    static CsStory stories[1];
    memset(stories, 0, sizeof(stories));
    cs_copy_bounded(stories[0].story_id, sizeof(stories[0].story_id), "anban-huri");
    cs_copy_bounded(stories[0].sha256, sizeof(stories[0].sha256), kShaA);
    cs_copy_bounded(stories[0].cache_path, sizeof(stories[0].cache_path),
                    "/stories/anban-huri-v1.mp3");
    stories[0].version    = 1;
    stories[0].size_bytes = 100;
    stories[0].verified   = true;

    static CsVoice voice[2];
    memset(voice, 0, sizeof(voice));
    cs_copy_bounded(voice[0].voice_id, sizeof(voice[0].voice_id), "greet-01");
    cs_copy_bounded(voice[0].sha256, sizeof(voice[0].sha256), kShaB);
    voice[0].version = 1; voice[0].size_bytes = 10; voice[0].verified = true;
    cs_copy_bounded(voice[1].voice_id, sizeof(voice[1].voice_id), "just-story");
    cs_copy_bounded(voice[1].sha256, sizeof(voice[1].sha256), kShaC);
    voice[1].version = 2; voice[1].size_bytes = 20; voice[1].verified = true;

    JsonDocument idx;
    cs_index_build(idx, stories, 1, "anban-huri", true);
    cs_index_add_voice(idx, voice, 2);
    cs_index_add_modes(idx, true, false, true, false);

    String serialized;
    serializeJson(idx, serialized);
    JsonDocument reread;
    check(deserializeJson(reread, serialized) == DeserializationError::Ok,
          "index_v4_serializes");
    check((reread["schemaVersion"] | 0) == CS_INDEX_SCHEMA_VERSION,
          "index_v4_schema_version");

    CsVoice *back = g_voice_scratch;
    const int n = cs_index_parse_voice(reread, back, CS_MAX_VOICE);
    check(n == 2 && strcmp(back[0].voice_id, "greet-01") == 0
              && back[0].verified && back[1].version == 2,
          "index_v4_voice_round_trip");

    check(cs_index_mode_enabled(reread, "storyEnabled"), "index_v4_mode_story_on");
    check(!cs_index_mode_enabled(reread, "gameEnabled"), "index_v4_mode_game_off");
    check(cs_index_mode_enabled(reread, "riddleEnabled"), "index_v4_mode_riddle_on");
    check(!cs_index_mode_enabled(reread, "curiosityEnabled"), "index_v4_mode_curiosity_off");
}

// KEYSTONE: a card written by the PREVIOUS firmware must keep working.
// v4 is a superset, so a v3 document has to parse as "no voice clips,
// every mode enabled" — never as "no modes", which would leave a toy
// silently unable to offer anything after a downgrade or a stale card.
void test_index_v3_forward_compatible() {
    static CsStory stories[1];
    memset(stories, 0, sizeof(stories));
    cs_copy_bounded(stories[0].story_id, sizeof(stories[0].story_id), "anban-huri");
    cs_copy_bounded(stories[0].sha256, sizeof(stories[0].sha256), kShaA);
    cs_copy_bounded(stories[0].cache_path, sizeof(stories[0].cache_path),
                    "/stories/anban-huri-v1.mp3");
    stories[0].version = 1; stories[0].size_bytes = 100; stories[0].verified = true;

    JsonDocument idx;
    cs_index_build(idx, stories, 1, "anban-huri", true);
    idx["schemaVersion"] = 3;          // pretend it was written by the old build
    String serialized;
    serializeJson(idx, serialized);

    JsonDocument reread;
    deserializeJson(reread, serialized);

    CsVoice *back = g_voice_scratch;
    check(cs_index_parse_voice(reread, back, CS_MAX_VOICE) == 0,
          "v3_index_yields_no_voice_clips");
    check(cs_index_mode_enabled(reread, "storyEnabled")
              && cs_index_mode_enabled(reread, "gameEnabled")
              && cs_index_mode_enabled(reread, "riddleEnabled")
              && cs_index_mode_enabled(reread, "curiosityEnabled"),
          "v3_index_treats_every_mode_as_enabled");

    // And the stories still parse — the card is not wiped by a bump.
    CsStory *out = g_story_scratch;
    int schema = 0;
    check(cs_index_parse(reread, out, CS_MAX_STORIES, &schema) == 1,
          "v3_index_stories_still_parse");
}

// ---- serial episodes (schema v5) ------------------------------------

void test_series_manifest_parse() {
    JsonDocument doc;
    JsonArray arr = doc["stories"].to<JsonArray>();
    add_item(arr, "tsivik-ep1", 1, kShaA, 100, "/u");
    arr[0]["seriesId"]    = "tsivik";
    arr[0]["seriesIndex"] = 1;
    // Half-set: an id with no position. Must degrade to a STANDALONE
    // story, never to a half-set episode the selector cannot order.
    add_item(arr, "tsivik-ep2", 1, kShaB, 100, "/u");
    arr[1]["seriesId"] = "tsivik";
    // Half-set the other way: a position with no id.
    add_item(arr, "little-cloud", 1, kShaC, 100, "/u");
    arr[2]["seriesIndex"] = 4;
    // Malformed id (uppercase would become a second SD-visible series).
    add_item(arr, "hedgehog-apple", 1, kShaA, 100, "/u");
    arr[3]["seriesId"]    = "Tsivik";
    arr[3]["seriesIndex"] = 2;
    // Non-positive position.
    add_item(arr, "khosogh-dzuk", 1, kShaB, 100, "/u");
    arr[4]["seriesId"]    = "tsivik";
    arr[4]["seriesIndex"] = 0;

    CsStory *out = g_story_scratch;
    CsManifestStats stats{};
    const int n = cs_manifest_parse(doc["stories"].as<JsonArrayConst>(),
                                    out, CS_MAX_STORIES, &stats);

    // KEYSTONE: a bad series pairing drops the PAIRING, never the story.
    check(n == 5, "series_bad_pairing_keeps_every_story");
    check(cs_story_is_serial(&out[0]) && strcmp(out[0].series_id, "tsivik") == 0
              && out[0].series_index == 1,
          "series_valid_pair_stored");
    check(!cs_story_is_serial(&out[1]), "series_id_without_index_is_standalone");
    check(!cs_story_is_serial(&out[2]), "series_index_without_id_is_standalone");
    check(!cs_story_is_serial(&out[3]), "series_uppercase_id_rejected");
    check(!cs_story_is_serial(&out[4]), "series_zero_index_rejected");
}

void test_series_index_round_trip() {
    static CsStory active[3];
    fill(&active[0], "tsivik-ep1", 1, kShaA, 100);
    cs_copy_bounded(active[0].series_id, sizeof(active[0].series_id), "tsivik");
    active[0].series_index = 1;
    fill(&active[1], "tsivik-ep6", 1, kShaB, 100);
    cs_copy_bounded(active[1].series_id, sizeof(active[1].series_id), "tsivik");
    active[1].series_index = 6;   // gaps are legal — only order matters
    fill(&active[2], "anban-huri", 1, kShaC, 100);   // standalone

    JsonDocument built;
    cs_index_build(built, active, 3, "anban-huri");
    check((built["schemaVersion"] | 0) == CS_INDEX_SCHEMA_VERSION,
          "index_v5_schema_version");
    // A standalone story writes NO series keys, so a serial-free library
    // still produces the same index it produced before this slice.
    check(!built["stories"][2]["seriesId"].is<const char *>(),
          "index_standalone_writes_no_series_keys");

    String json;
    serializeJson(built, json);
    JsonDocument reread;
    check(deserializeJson(reread, json) == DeserializationError::Ok, "index_v5_reparses");

    CsStory *back = g_story_scratch2;
    int schema = 0;
    const int n = cs_index_parse(reread, back, CS_MAX_STORIES, &schema);
    check(n == 3 && schema == CS_INDEX_SCHEMA_VERSION, "index_v5_round_trip_count");
    check(cs_story_is_serial(&back[0]) && back[0].series_index == 1,
          "index_v5_series_round_trip_first");
    check(cs_story_is_serial(&back[1]) && back[1].series_index == 6,
          "index_v5_series_round_trip_gap_preserved");
    check(!cs_story_is_serial(&back[2]), "index_v5_standalone_stays_standalone");
}

// KEYSTONE: a card written by the PREVIOUS firmware must keep working.
// v5 is a superset, so a v4 document has to parse as "every story is
// standalone" — the exact pre-serial behaviour — and never as an
// unorderable half-set that could take stories out of the rotation.
void test_index_v4_forward_compatible() {
    static CsStory stories[1];
    fill(&stories[0], "anban-huri", 1, kShaA, 100);

    JsonDocument idx;
    cs_index_build(idx, stories, 1, "anban-huri", true);
    idx["schemaVersion"] = 4;          // pretend it was written by the old build
    String serialized;
    serializeJson(idx, serialized);

    JsonDocument reread;
    deserializeJson(reread, serialized);

    CsStory *out = g_story_scratch;
    int schema = 0;
    check(cs_index_parse(reread, out, CS_MAX_STORIES, &schema) == 1,
          "v4_index_stories_still_parse");
    check(schema == 4, "v4_index_detected_as_v4");
    check(!cs_story_is_serial(&out[0]), "v4_index_stories_are_standalone");
    // And the v4 fields it DOES carry are untouched by the bump.
    check(cs_index_intro_enabled(reread), "v4_index_intro_flag_still_read");
    check(cs_index_mode_enabled(reread, "storyEnabled"), "v4_index_modes_still_default_on");
}

// ---- variant endings (schema v6) ------------------------------------

void test_variant_manifest_parse() {
    JsonDocument doc;
    JsonArray arr = doc["stories"].to<JsonArray>();
    add_item(arr, "anban-huri", 1, kShaA, 100, "/u");           // ordinary
    add_item(arr, "anban-huri-alt", 1, kShaB, 100, "/u");
    arr[1]["altOf"] = "anban-huri";                             // valid variant
    add_item(arr, "khosogh-dzuk", 1, kShaC, 100, "/u");
    arr[2]["altOf"] = "Anban-Huri";                             // uppercase — refused
    add_item(arr, "little-cloud", 1, kShaA, 100, "/u");
    arr[3]["altOf"] = "little-cloud";                           // self-reference
    add_item(arr, "hedgehog-apple", 1, kShaB, 100, "/u");
    arr[4]["altOf"] = "../../etc";                              // traversal shape

    CsStory *out = g_story_scratch;
    CsManifestStats stats{};
    const int n = cs_manifest_parse(doc["stories"].as<JsonArrayConst>(),
                                    out, CS_MAX_STORIES, &stats);

    // A bad altOf clears the FIELD here and never drops the story. (The
    // backend drops such an item outright; on a hand-edited card the
    // weaker response is the right one — the file is still a real story.)
    check(n == 5, "variant_bad_alt_keeps_every_story");
    check(!cs_story_is_variant(&out[0]), "variant_ordinary_story_is_not_a_variant");
    check(cs_story_is_variant(&out[1])
              && strcmp(out[1].alt_of, "anban-huri") == 0,
          "variant_valid_alt_stored");
    check(!cs_story_is_variant(&out[2]), "variant_uppercase_alt_rejected");
    check(!cs_story_is_variant(&out[3]), "variant_self_reference_rejected");
    check(!cs_story_is_variant(&out[4]), "variant_traversal_alt_rejected");
}

void test_variant_index_round_trip() {
    static CsStory active[3];
    fill(&active[0], "anban-huri", 1, kShaA, 100);            // ordinary
    fill(&active[1], "anban-huri-alt", 1, kShaB, 100);
    cs_copy_bounded(active[1].alt_of, sizeof(active[1].alt_of), "anban-huri");
    fill(&active[2], "khosogh-dzuk", 1, kShaC, 100);          // ordinary

    JsonDocument built;
    cs_index_build(built, active, 3, "anban-huri");
    cs_index_add_story_flags(built, /*pauses=*/false, /*variants=*/true);
    check((built["schemaVersion"] | 0) == CS_INDEX_SCHEMA_VERSION,
          "index_v6_schema_version");
    // An ordinary story writes NO altOf key, so a variant-free library still
    // produces the same index it produced before this slice.
    check(!built["stories"][0]["altOf"].is<const char *>(),
          "index_ordinary_writes_no_alt_key");

    String json;
    serializeJson(built, json);
    JsonDocument reread;
    check(deserializeJson(reread, json) == DeserializationError::Ok, "index_v6_reparses");

    CsStory *back = g_story_scratch2;
    int schema = 0;
    const int n = cs_index_parse(reread, back, CS_MAX_STORIES, &schema);
    check(n == 3 && schema == CS_INDEX_SCHEMA_VERSION, "index_v6_round_trip_count");
    check(!cs_story_is_variant(&back[0]), "index_v6_ordinary_stays_ordinary");
    check(cs_story_is_variant(&back[1])
              && strcmp(back[1].alt_of, "anban-huri") == 0,
          "index_v6_variant_round_trip");
    // The two root flags survive independently — they are separate keys, and
    // a copy/paste of the reader is the obvious way to make one shadow the
    // other.
    check(!cs_index_pauses_enabled(reread), "index_v6_pauses_flag_round_trip");
    check(cs_index_variants_enabled(reread), "index_v6_variants_flag_round_trip");
}

// KEYSTONE: a card written by the PREVIOUS firmware must keep working.
// v6 is a superset, so a v5 document has to parse as "no story is a variant,
// both features on" — the exact pre-variant behaviour. Getting the flag
// fallbacks backwards would silently strip the pauses and the alternate
// endings from every toy whose card predates this build.
void test_index_v5_forward_compatible() {
    static CsStory stories[1];
    fill(&stories[0], "anban-huri", 1, kShaA, 100);

    JsonDocument idx;
    cs_index_build(idx, stories, 1, "anban-huri", true);
    idx["schemaVersion"] = 5;          // pretend it was written by the old build
    String serialized;
    serializeJson(idx, serialized);

    JsonDocument reread;
    deserializeJson(reread, serialized);

    CsStory *out = g_story_scratch;
    int schema = 0;
    check(cs_index_parse(reread, out, CS_MAX_STORIES, &schema) == 1,
          "v5_index_stories_still_parse");
    check(schema == 5, "v5_index_detected_as_v5");
    check(!cs_story_is_variant(&out[0]), "v5_index_stories_are_not_variants");
    // Absent root flags default ON, matching the shipped server-side default.
    check(cs_index_pauses_enabled(reread), "v5_index_pauses_defaults_on");
    check(cs_index_variants_enabled(reread), "v5_index_variants_defaults_on");
    // And the v5 fields it DOES carry are untouched by the bump.
    check(cs_index_intro_enabled(reread), "v5_index_intro_flag_still_read");
    check(cs_index_mode_enabled(reread, "storyEnabled"), "v5_index_modes_still_default_on");
    check(!cs_story_is_serial(&out[0]), "v5_index_standalone_still_standalone");
}

// ---- offline-game clips (schema v7) ---------------------------------

// KEYSTONE: the built path must be byte-for-byte what offline_games.cpp
// already resolves — AREG_GAMES_CLIP_DIR "/%s/%s.mp3". That module is
// bench-gated behind a DIFFERENT flag, so the two cannot include each
// other; this literal is the only thing keeping them in step. Sync the
// clips to a path the games module does not look at and every game goes
// silent while the sync reports a clean PASS.
void test_game_paths() {
    char path[CS_MAX_PATH_LEN];

    check(cs_build_game_cache_path(path, sizeof(path), "mind-reader", "q-root")
              && strcmp(path, "/games/mind-reader/q-root.mp3") == 0,
          "game_cache_path_matches_offline_games_layout");
    check(cs_build_game_cache_path(path, sizeof(path), "who-first", "win-green")
              && strcmp(path, "/games/who-first/win-green.mp3") == 0,
          "game_cache_path_second_game");

    // No "-v<version>" anywhere in the name — deliberate; the games module
    // resolves by key and id alone.
    check(strstr(path, "-v") == NULL, "game_cache_path_carries_no_version");

    check(cs_build_game_dir_path(path, sizeof(path), "button-simon")
              && strcmp(path, "/games/button-simon") == 0,
          "game_dir_path");
    check(cs_build_game_temp_path(path, sizeof(path), "mind-reader", "g-1010")
              && strcmp(path, "/tmp/g-mind-reader-g-1010.mp3.part") == 0,
          "game_temp_path_prefixed_to_avoid_collisions");

    // Both halves reach the path, so both are allowlisted — traversal is
    // unrepresentable, not filtered.
    check(!cs_build_game_cache_path(path, sizeof(path), "..", "q-root"),
          "game_path_rejects_traversal_key");
    check(!cs_build_game_cache_path(path, sizeof(path), "mind-reader", "../x"),
          "game_path_rejects_traversal_clip");
    check(!cs_build_game_cache_path(path, sizeof(path), "Mind-Reader", "q-root"),
          "game_path_rejects_uppercase_key");
    check(!cs_build_game_cache_path(path, sizeof(path), "mind-reader", ""),
          "game_path_rejects_empty_clip");
    check(!cs_build_game_cache_path(path, sizeof(path), "", "q-root"),
          "game_path_rejects_empty_key");

    // Refuse-on-truncation: a short buffer never yields a shortened path
    // pointing at a different file.
    char tiny[8];
    check(!cs_build_game_cache_path(tiny, sizeof(tiny), "mind-reader", "q-root"),
          "game_path_refuses_truncation");
}

void add_game_item(JsonArray arr, const char *key, const char *clip,
                   int version, const char *sha, long size,
                   bool enabled = true) {
    JsonObject o = arr.add<JsonObject>();
    o["gameKey"]   = key;
    o["clipId"]    = clip;
    o["version"]   = version;
    o["sha256"]    = sha;
    o["sizeBytes"] = size;
    o["enabled"]   = enabled;
}

void test_game_manifest_read() {
    JsonDocument doc;
    JsonArray arr = doc.to<JsonArray>();
    add_game_item(arr, "mind-reader", "q-root", 2, kShaA, 40000);
    add_game_item(arr, "mind-reader", "bad", 1, "nothex", 40000);
    add_game_item(arr, "MIND-READER", "q-1", 1, kShaB, 40000);   // uppercase key
    add_game_item(arr, "mind-reader", "q/1", 1, kShaB, 40000);   // slash in id
    add_game_item(arr, "who-first", "go", 1, kShaB, 0);          // no size
    add_game_item(arr, "who-first", "intro", 1, kShaC, 40000, false);  // disabled

    CsGame g;
    int accepted = 0;
    for (JsonObjectConst item : arr) {
        if (cs_manifest_read_game(item, &g)) accepted++;
    }
    check(accepted == 1, "game_manifest_accepts_only_the_valid_item");

    // And the accepted one round-trips its fields intact.
    JsonDocument one;
    JsonArray oneArr = one.to<JsonArray>();
    add_game_item(oneArr, "sound-detective", "r01-sound", 3, kShaA, 51200);
    check(cs_manifest_read_game(oneArr[0].as<JsonObjectConst>(), &g)
              && strcmp(g.game_key, "sound-detective") == 0
              && strcmp(g.clip_id, "r01-sound") == 0
              && g.version == 3 && g.size_bytes == 51200
              && !g.verified,
          "game_manifest_fields_round_trip");
}

// KEYSTONE: the (gameKey, clipId) PAIR is the identity. Four of the five
// games each ship a clip called "intro"; if anything compared clip ids
// alone, three of those four would be treated as duplicates and quietly
// never reach the card.
void test_game_pair_is_the_identity() {
    CsGame a{};
    cs_copy_bounded(a.game_key, sizeof(a.game_key), "mind-reader");
    cs_copy_bounded(a.clip_id, sizeof(a.clip_id), "intro");

    check(cs_game_pairs_equal(&a, "mind-reader", "intro"), "game_pair_matches_itself");
    check(cs_game_pairs_equal(&a, "MIND-READER", "INTRO"),
          "game_pair_match_is_case_insensitive");
    check(!cs_game_pairs_equal(&a, "who-first", "intro"),
          "game_pair_same_clip_different_game_is_not_equal");
    check(!cs_game_pairs_equal(&a, "mind-reader", "go"),
          "game_pair_same_game_different_clip_is_not_equal");
}

void test_index_v7_games_round_trip() {
    JsonDocument idx;
    cs_index_build(idx, nullptr, 0, nullptr, true);

    JsonDocument gamesDoc;
    JsonArray games = gamesDoc.to<JsonArray>();
    CsGame g{};
    // Written from ONE reused struct, exactly as sync_games does — this is
    // what proves cs_index_append_game copies rather than links. If it
    // linked, all three entries would end up naming the last clip.
    cs_copy_bounded(g.game_key, sizeof(g.game_key), "mind-reader");
    cs_copy_bounded(g.clip_id, sizeof(g.clip_id), "intro");
    cs_copy_bounded(g.sha256, sizeof(g.sha256), kShaA);
    g.version = 1; g.size_bytes = 40000; g.verified = true;
    cs_index_append_game(games, &g);

    cs_copy_bounded(g.game_key, sizeof(g.game_key), "who-first");
    cs_copy_bounded(g.sha256, sizeof(g.sha256), kShaB);
    g.size_bytes = 50000;
    cs_index_append_game(games, &g);            // same clip id, other game

    cs_copy_bounded(g.game_key, sizeof(g.game_key), "button-simon");
    cs_copy_bounded(g.clip_id, sizeof(g.clip_id), "your-turn");
    cs_copy_bounded(g.sha256, sizeof(g.sha256), kShaC);
    g.version = 4; g.size_bytes = 12345;
    cs_index_append_game(games, &g);

    cs_index_add_games(idx, games);

    String serialized;
    serializeJson(idx, serialized);
    check(serialized.indexOf("\"schemaVersion\":" + String(CS_INDEX_SCHEMA_VERSION)) >= 0,
          "index_v7_schema_version");

    JsonDocument reread;
    deserializeJson(reread, serialized);
    JsonArrayConst back = reread["games"].as<JsonArrayConst>();
    check(!back.isNull() && back.size() == 3, "index_v7_games_round_trip_count");

    CsGame r;
    check(cs_index_read_game(back[0].as<JsonObjectConst>(), &r)
              && strcmp(r.game_key, "mind-reader") == 0
              && strcmp(r.clip_id, "intro") == 0
              && r.size_bytes == 40000 && r.verified,
          "index_v7_first_entry_intact");
    check(cs_index_read_game(back[1].as<JsonObjectConst>(), &r)
              && strcmp(r.game_key, "who-first") == 0
              && strcmp(r.clip_id, "intro") == 0
              && r.size_bytes == 50000,
          "index_v7_same_clip_id_other_game_kept_separately");
    check(cs_index_read_game(back[2].as<JsonObjectConst>(), &r)
              && strcmp(r.game_key, "button-simon") == 0
              && strcmp(r.clip_id, "your-turn") == 0
              && r.version == 4,
          "index_v7_third_entry_intact");

    // A hand-edited entry is not trusted just because it is already on the
    // card — the same allowlist applies on the way back in.
    JsonDocument hostile;
    JsonObject bad = hostile.to<JsonObject>();
    bad["gameKey"] = "../etc"; bad["clipId"] = "passwd"; bad["sha256"] = kShaA;
    check(!cs_index_read_game(bad, &r),
          "index_v7_rejects_hand_edited_traversal_entry");
}

// KEYSTONE: a card written by the PREVIOUS firmware must keep working. v7 is
// a superset, so a v6 document has to parse as "no game clips indexed" — the
// exact pre-games behaviour, whose only cost is that the next sync
// re-downloads them.
void test_index_v6_forward_compatible() {
    static CsStory stories[1];
    fill(&stories[0], "anban-huri", 1, kShaA, 100);

    JsonDocument idx;
    cs_index_build(idx, stories, 1, "anban-huri", true);
    cs_index_add_story_flags(idx, true, true);
    idx["schemaVersion"] = 6;          // pretend it was written by the old build
    String serialized;
    serializeJson(idx, serialized);

    JsonDocument reread;
    deserializeJson(reread, serialized);

    CsStory *out = g_story_scratch;
    int schema = 0;
    check(cs_index_parse(reread, out, CS_MAX_STORIES, &schema) == 1,
          "v6_index_stories_still_parse");
    check(schema == 6, "v6_index_detected_as_v6");
    check(reread["games"].isNull(), "v6_index_has_no_games_section");
    // And every v6 field it DOES carry survives the bump untouched.
    check(cs_index_intro_enabled(reread), "v6_index_intro_flag_still_read");
    check(cs_index_pauses_enabled(reread), "v6_index_pauses_flag_still_read");
    check(cs_index_variants_enabled(reread), "v6_index_variants_flag_still_read");
    check(cs_index_mode_enabled(reread, "storyEnabled"), "v6_index_modes_still_default_on");

    // Symmetrically: an index built with no game clips writes no games
    // section at all, so a games-less deployment's card is byte-identical to
    // a pre-v7 one apart from the schema number.
    JsonDocument empty;
    cs_index_build(empty, stories, 1, "anban-huri", true);
    JsonDocument none;
    JsonArray noneArr = none.to<JsonArray>();
    cs_index_add_games(empty, noneArr);
    check(empty["games"].isNull(), "no_game_clips_writes_no_games_section");
}

void run_all() {
    s_pass = 0;
    s_fail = 0;
    Serial.println("[cs-test] ---- content-sync decision-logic tests ----");
    Serial.printf("[cs-test] CS_MAX_STORIES=%d CS_MAX_STORY_ID_LEN=%d "
                  "CS_MAX_PATH_LEN=%d CS_MAX_URL_LEN=%d schema=%d\n",
                  CS_MAX_STORIES, CS_MAX_STORY_ID_LEN, CS_MAX_PATH_LEN,
                  CS_MAX_URL_LEN, CS_INDEX_SCHEMA_VERSION);
    Serial.printf("[cs-test] heap before=%u\n", (unsigned)ESP.getFreeHeap());
    Serial.flush();

    test_story_id_rules();
    test_sha_and_size_rules();
    test_cache_paths();
    test_manifest_three_stories();
    test_manifest_legacy_single_story();
    test_manifest_empty();
    test_manifest_invalid_item_does_not_block_siblings();
    test_manifest_disabled_item_skipped();
    test_manifest_duplicates();
    test_manifest_truncation();
    test_index_round_trip_three();
    test_index_mirror_falls_back_to_first();
    test_index_legacy_migration();
    test_index_malformed_and_hostile();
    test_already_current_metadata();
    test_clip_kinds_and_bounds();
    test_voice_manifest_parse();
    test_voice_manifest_truncation();
    test_ask_voice_id();
    test_index_v4_round_trip();
    test_index_v3_forward_compatible();
    test_series_manifest_parse();
    test_series_index_round_trip();
    test_index_v4_forward_compatible();
    test_variant_manifest_parse();
    test_variant_index_round_trip();
    test_index_v5_forward_compatible();
    test_game_paths();
    test_game_manifest_read();
    test_game_pair_is_the_identity();
    test_index_v7_games_round_trip();
    test_index_v6_forward_compatible();
    test_copy_bounded();

    Serial.printf("[cs-test] heap after=%u\n", (unsigned)ESP.getFreeHeap());
    Serial.printf("[cs-test] TOTAL pass=%d fail=%d\n", s_pass, s_fail);
    Serial.println(s_fail == 0 ? "[cs-test] RESULT PASS" : "[cs-test] RESULT FAIL");
    Serial.println("[cs-test] NOT covered here (needs the hardware bench): real SD "
                   "atomicity, real/failed HTTP downloads, the file-exists+size half "
                   "of already-current, reboot after partial sync.");
    Serial.flush();
}

constexpr uint32_t kStartMs       = 20000UL;  // 20 s arm delay (monitor attach)
constexpr uint32_t kStatusEveryMs = 5000UL;

}  // namespace

void content_sync_test_tick() {
    static bool s_done = false;
    static uint32_t s_last_status_ms = 0;
    if (s_done) {
        return;
    }
    const uint32_t now = millis();
    if (now < kStartMs) {
        if (s_last_status_ms == 0 || now - s_last_status_ms >= kStatusEveryMs) {
            s_last_status_ms = now;
            Serial.println("[cs-test] armed; running at ms>=20000");
            Serial.flush();
        }
        return;
    }
    s_done = true;
    run_all();
}

#endif  // AREG_CONTENT_SYNC_TEST_BENCH
