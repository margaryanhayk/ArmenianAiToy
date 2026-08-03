// -------------------------------------------------------------
// AregVoiceMvp / content_sync_model.cpp — see content_sync_model.h.
//
// Compiled into EVERY build. It used to be gated behind the content-sync
// bench flags, but as of story-select-from-index, production playback
// reads the same index through cs_index_parse — one owner of the schema
// rather than a second reader that could drift from it.
//
// cs_manifest_parse stays unreferenced in production builds; the ESP32
// core compiles with -ffunction-sections/-fdata-sections and links with
// --gc-sections, so it is dropped from the image.
// -------------------------------------------------------------
#include "content_sync_model.h"

#include <Arduino.h>

int cs_manifest_parse(JsonArrayConst stories, CsStory *out, int max_out,
                      CsManifestStats *stats) {
    CsManifestStats local{};
    if (stats == nullptr) {
        stats = &local;
    }
    *stats = CsManifestStats{};

    if (out == nullptr || max_out <= 0) {
        return 0;
    }

    const int offered = stories.isNull() ? 0 : (int)stories.size();
    stats->offered = offered;

    const int cap_by_rule = cs_accepted_count(offered);
    const int cap = cap_by_rule < max_out ? cap_by_rule : max_out;
    stats->truncated = offered > cap ? offered - cap : 0;

    int count = 0;
    int examined = 0;
    for (JsonObjectConst item : stories) {
        if (examined >= cap) {
            break;   // truncation is reported, never fatal
        }
        examined++;

        const char *story_id  = item["storyId"]  | "";
        const int   version   = item["version"]  | 1;
        const char *title     = item["title"]    | "";
        const char *audio_url = item["audioUrl"] | "";
        const char *sha256    = item["sha256"]   | "";
        const long  size      = item["sizeBytes"] | 0L;
        const bool  enabled   = item["enabled"]  | false;

        if (!enabled) {
            // Retirement (deleting a cached copy) is deliberately NOT
            // implemented: the file stays, the story just does not enter
            // the active index.
            stats->disabled++;
            Serial.printf("[content-sync] item #%d disabled — skip\n", examined - 1);
            continue;
        }
        if (!cs_is_valid_story_id(story_id)) {
            stats->invalid++;
            Serial.printf("[content-sync] item #%d rejected (bad_story_id)\n", examined - 1);
            continue;
        }
        if (audio_url[0] == '\0' || strlen(audio_url) >= CS_MAX_URL_LEN) {
            stats->invalid++;
            Serial.printf("[content-sync] item %s rejected (bad_audio_url)\n", story_id);
            continue;
        }
        if (!cs_is_sha256_hex(sha256)) {
            stats->invalid++;
            Serial.printf("[content-sync] item %s rejected (bad_sha256)\n", story_id);
            continue;
        }
        if (!cs_is_valid_size(size)) {
            stats->invalid++;
            Serial.printf("[content-sync] item %s rejected (bad_size)\n", story_id);
            continue;
        }

        bool dup = false;
        for (int i = 0; i < count; i++) {
            if (cs_story_ids_equal(out[i].story_id, story_id)) {
                dup = true;
                break;
            }
        }
        if (dup) {
            stats->duplicate++;
            Serial.printf("[content-sync] item %s duplicate — keeping first\n", story_id);
            continue;
        }

        CsStory *dst = &out[count];
        memset(dst, 0, sizeof(*dst));
        // A truncated id or hash would silently address the WRONG file,
        // so truncation is rejected rather than stored.
        if (!cs_copy_bounded(dst->story_id, sizeof(dst->story_id), story_id)
            || !cs_copy_bounded(dst->sha256, sizeof(dst->sha256), sha256)
            || !cs_copy_bounded(dst->audio_url, sizeof(dst->audio_url), audio_url)) {
            stats->invalid++;
            continue;
        }
        cs_copy_bounded(dst->title, sizeof(dst->title), title);  // truncation OK
        dst->version    = cs_normalize_version(version);
        dst->size_bytes = size;
        dst->verified   = false;
        if (!cs_build_cache_path(dst->cache_path, sizeof(dst->cache_path),
                                 dst->story_id, dst->version)) {
            stats->invalid++;
            Serial.printf("[content-sync] item %s rejected (path_too_long)\n", story_id);
            continue;
        }

        // B2 — per-story clips. Same per-item fail-closed rule at clip
        // granularity: a bad clip is skipped and never takes the story or
        // its valid sibling clips with it. Duplicate kinds keep the first.
        dst->clip_count = 0;
        if (item["clips"].is<JsonArrayConst>()) {
            for (JsonObjectConst c : item["clips"].as<JsonArrayConst>()) {
                if (dst->clip_count >= CS_MAX_CLIPS) break;
                const char *kind = c["kind"]      | "";
                const char *csha = c["sha256"]    | "";
                const long  csz  = c["sizeBytes"] | 0L;
                if (!cs_is_valid_clip_kind(kind) || !cs_is_sha256_hex(csha)
                    || !cs_is_valid_size(csz)) {
                    Serial.printf("[content-sync] %s clip rejected (kind=%s)\n",
                                  dst->story_id, kind);
                    continue;
                }
                bool cdup = false;
                for (int k = 0; k < dst->clip_count; k++) {
                    if (strcmp(dst->clips[k].kind, kind) == 0) { cdup = true; break; }
                }
                if (cdup) continue;
                CsClip *clip = &dst->clips[dst->clip_count];
                memset(clip, 0, sizeof(*clip));
                if (!cs_copy_bounded(clip->kind, sizeof(clip->kind), kind)
                    || !cs_copy_bounded(clip->sha256, sizeof(clip->sha256), csha)) {
                    continue;
                }
                clip->size_bytes = csz;
                clip->verified   = false;
                dst->clip_count++;
            }
        }
        count++;
    }

    stats->accepted = count;
    return count;
}

int cs_index_parse(JsonDocument &doc, CsStory *out, int max_out, int *out_schema) {
    if (out == nullptr || max_out <= 0) {
        if (out_schema != nullptr) *out_schema = 0;
        return 0;
    }

    const int schema = doc["schemaVersion"] | 1;
    if (out_schema != nullptr) {
        *out_schema = schema;
    }

    int count = 0;

    if (schema >= 2 && doc["stories"].is<JsonArrayConst>()) {
        for (JsonObjectConst e : doc["stories"].as<JsonArrayConst>()) {
            if (count >= max_out) break;
            const char *sid  = e["storyId"]   | "";
            const char *sha  = e["sha256"]    | "";
            const char *path = e["cachePath"] | "";
            if (!cs_is_valid_story_id(sid) || !cs_is_sha256_hex(sha) || path[0] != '/') {
                continue;
            }
            CsStory *dst = &out[count];
            memset(dst, 0, sizeof(*dst));
            if (!cs_copy_bounded(dst->story_id, sizeof(dst->story_id), sid)
                || !cs_copy_bounded(dst->sha256, sizeof(dst->sha256), sha)
                || !cs_copy_bounded(dst->cache_path, sizeof(dst->cache_path), path)) {
                continue;
            }
            cs_copy_bounded(dst->title, sizeof(dst->title), e["title"] | "");
            dst->version    = cs_normalize_version(e["version"] | 1);
            dst->size_bytes = e["sizeBytes"] | 0L;
            dst->verified   = e["verified"] | false;

            // v3 clips[] — absent on every v2 card, yielding clip_count 0
            // (story plays without intro/reflection, the pre-B2 behavior).
            dst->clip_count = 0;
            if (e["clips"].is<JsonArrayConst>()) {
                for (JsonObjectConst c : e["clips"].as<JsonArrayConst>()) {
                    if (dst->clip_count >= CS_MAX_CLIPS) break;
                    const char *kind = c["kind"]      | "";
                    const char *csha = c["sha256"]    | "";
                    const long  csz  = c["sizeBytes"] | 0L;
                    if (!cs_is_valid_clip_kind(kind) || !cs_is_sha256_hex(csha)
                        || !cs_is_valid_size(csz)) {
                        continue;
                    }
                    CsClip *clip = &dst->clips[dst->clip_count];
                    memset(clip, 0, sizeof(*clip));
                    if (!cs_copy_bounded(clip->kind, sizeof(clip->kind), kind)
                        || !cs_copy_bounded(clip->sha256, sizeof(clip->sha256), csha)) {
                        continue;
                    }
                    clip->size_bytes = csz;
                    clip->verified   = c["verified"] | false;
                    dst->clip_count++;
                }
            }
            count++;
        }
        return count;
    }

    // ---- v1 migration: the flat single-object index -----------------
    // Everything needed is present (storyId/version/sha256/file/
    // sizeBytes); the only absent field is "verified", which the v1
    // writer never wrote. It is inferred TRUE because v1 wrote its index
    // only after a full SHA-256 verification — and the caller still
    // re-checks existence and size before trusting it, so an inferred
    // true that turns out wrong costs a re-download, never a bad file.
    const char *sid  = doc["storyId"] | "";
    const char *sha  = doc["sha256"]  | "";
    const char *path = doc["file"]    | "";
    if (cs_is_valid_story_id(sid) && cs_is_sha256_hex(sha) && path[0] == '/') {
        CsStory *dst = &out[0];
        memset(dst, 0, sizeof(*dst));
        if (cs_copy_bounded(dst->story_id, sizeof(dst->story_id), sid)
            && cs_copy_bounded(dst->sha256, sizeof(dst->sha256), sha)
            && cs_copy_bounded(dst->cache_path, sizeof(dst->cache_path), path)) {
            dst->version    = cs_normalize_version(doc["version"] | 1);
            dst->size_bytes = doc["sizeBytes"] | 0L;
            dst->verified   = true;
            return 1;
        }
    }
    return 0;
}

void cs_index_build(JsonDocument &doc, const CsStory *active, int count,
                    const char *mirror_story_id, bool intro_enabled) {
    doc.clear();
    doc["schemaVersion"] = CS_INDEX_SCHEMA_VERSION;
    // Parent story-intro toggle, cached on the card so the last-known
    // value applies offline too. Root-level: it is per-device, not
    // per-story.
    doc["introEnabled"] = intro_enabled;
    JsonArray arr = doc["stories"].to<JsonArray>();
    if (active == nullptr || count < 0) {
        count = 0;
    }
    for (int i = 0; i < count; i++) {
        JsonObject e = arr.add<JsonObject>();
        e["storyId"]   = active[i].story_id;
        e["version"]   = active[i].version;
        e["title"]     = active[i].title;
        e["sha256"]    = active[i].sha256;
        e["sizeBytes"] = active[i].size_bytes;
        e["cachePath"] = active[i].cache_path;
        e["verified"]  = active[i].verified;
        if (active[i].clip_count > 0) {
            JsonArray clips = e["clips"].to<JsonArray>();
            for (int k = 0; k < active[i].clip_count && k < CS_MAX_CLIPS; k++) {
                JsonObject c = clips.add<JsonObject>();
                c["kind"]      = active[i].clips[k].kind;
                c["sha256"]    = active[i].clips[k].sha256;
                c["sizeBytes"] = active[i].clips[k].size_bytes;
                c["verified"]  = active[i].clips[k].verified;
            }
        }
    }

    if (mirror_story_id == nullptr || count == 0) {
        return;
    }
    int mirror = -1;
    for (int i = 0; i < count; i++) {
        if (cs_story_ids_equal(active[i].story_id, mirror_story_id)) {
            mirror = i;
            break;
        }
    }
    if (mirror < 0) {
        mirror = 0;   // configured story absent → first entry, as before
    }
    doc["storyId"]   = active[mirror].story_id;
    doc["version"]   = active[mirror].version;
    doc["sha256"]    = active[mirror].sha256;
    doc["file"]      = active[mirror].cache_path;
    doc["sizeBytes"] = active[mirror].size_bytes;
}

bool cs_index_intro_enabled(JsonDocument &doc) {
    return doc["introEnabled"] | true;
}

int cs_manifest_parse_music(JsonArrayConst music, CsMusic *out, int max_out) {
    if (out == nullptr || max_out <= 0 || music.isNull()) {
        return 0;
    }
    int count = 0;
    for (JsonObjectConst item : music) {
        if (count >= max_out) break;
        const char *track_id = item["trackId"]   | "";
        const int   version  = item["version"]   | 1;
        const char *title    = item["title"]     | "";
        const char *sha256   = item["sha256"]    | "";
        const long  size     = item["sizeBytes"] | 0L;
        const bool  enabled  = item["enabled"]   | false;
        if (!enabled || !cs_is_valid_story_id(track_id)
            || !cs_is_sha256_hex(sha256) || !cs_is_valid_size(size)) {
            Serial.printf("[content-sync] music item rejected (%s)\n", track_id);
            continue;
        }
        bool dup = false;
        for (int i = 0; i < count; i++) {
            if (cs_story_ids_equal(out[i].track_id, track_id)) { dup = true; break; }
        }
        if (dup) continue;
        CsMusic *dst = &out[count];
        memset(dst, 0, sizeof(*dst));
        if (!cs_copy_bounded(dst->track_id, sizeof(dst->track_id), track_id)
            || !cs_copy_bounded(dst->sha256, sizeof(dst->sha256), sha256)) {
            continue;
        }
        cs_copy_bounded(dst->title, sizeof(dst->title), title);  // truncation OK
        dst->version    = cs_normalize_version(version);
        dst->size_bytes = size;
        dst->verified   = false;
        count++;
    }
    return count;
}

int cs_index_parse_music(JsonDocument &doc, CsMusic *out, int max_out) {
    if (out == nullptr || max_out <= 0 || !doc["music"].is<JsonArrayConst>()) {
        return 0;
    }
    int count = 0;
    for (JsonObjectConst e : doc["music"].as<JsonArrayConst>()) {
        if (count >= max_out) break;
        const char *tid = e["trackId"] | "";
        const char *sha = e["sha256"]  | "";
        if (!cs_is_valid_story_id(tid) || !cs_is_sha256_hex(sha)) {
            continue;
        }
        CsMusic *dst = &out[count];
        memset(dst, 0, sizeof(*dst));
        if (!cs_copy_bounded(dst->track_id, sizeof(dst->track_id), tid)
            || !cs_copy_bounded(dst->sha256, sizeof(dst->sha256), sha)) {
            continue;
        }
        cs_copy_bounded(dst->title, sizeof(dst->title), e["title"] | "");
        dst->version    = cs_normalize_version(e["version"] | 1);
        dst->size_bytes = e["sizeBytes"] | 0L;
        dst->verified   = e["verified"] | false;
        count++;
    }
    return count;
}

void cs_index_add_music(JsonDocument &doc, const CsMusic *music, int count,
                        bool music_enabled) {
    doc["musicEnabled"] = music_enabled;
    if (music == nullptr || count <= 0) {
        return;
    }
    JsonArray arr = doc["music"].to<JsonArray>();
    for (int i = 0; i < count; i++) {
        JsonObject e = arr.add<JsonObject>();
        e["trackId"]   = music[i].track_id;
        e["version"]   = music[i].version;
        e["title"]     = music[i].title;
        e["sha256"]    = music[i].sha256;
        e["sizeBytes"] = music[i].size_bytes;
        e["verified"]  = music[i].verified;
    }
}

bool cs_index_music_enabled(JsonDocument &doc) {
    return doc["musicEnabled"] | false;
}
