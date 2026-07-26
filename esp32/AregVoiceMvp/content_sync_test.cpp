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

    static CsStory out[CS_MAX_STORIES];
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

    static CsStory out[CS_MAX_STORIES];
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
    static CsStory out[CS_MAX_STORIES];
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

    static CsStory out[CS_MAX_STORIES];
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

    static CsStory out[CS_MAX_STORIES];
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

    static CsStory out[CS_MAX_STORIES];
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

    static CsStory out[CS_MAX_STORIES];
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
    check(json.indexOf("\"schemaVersion\":2") >= 0, "index_has_schema_v2");
    check(json.indexOf("hedgehog-apple") >= 0, "index_contains_third_story");

    // Legacy compatibility mirror: the flat fields the three existing
    // readers still parse must point at the CONFIGURED story.
    check(built["storyId"].as<const char *>() != nullptr, "index_mirror_present");
    check(strcmp(built["storyId"] | "", "anban-huri") == 0, "index_mirror_targets_configured");
    check(strcmp(built["file"] | "", "/stories/anban-huri-v1.mp3") == 0,
          "index_mirror_file_matches");

    JsonDocument reparsed;
    check(deserializeJson(reparsed, json) == DeserializationError::Ok, "index_reparses");

    static CsStory back[CS_MAX_STORIES];
    int schema = 0;
    const int n = cs_index_parse(reparsed, back, CS_MAX_STORIES, &schema);
    check(schema == 2, "index_round_trip_schema");
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

    static CsStory out[CS_MAX_STORIES];
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
    static CsStory out[CS_MAX_STORIES];
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
