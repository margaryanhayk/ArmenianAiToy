// -------------------------------------------------------------
// AregVoiceMvp / content_sync.cpp — Cloud→SD story sync (bench slice)
// See content_sync.h for the scope statement and the index contract.
// Entire file compiles out unless AREG_CONTENT_SYNC_BENCH is defined.
// -------------------------------------------------------------
// config.h FIRST - it defines AREG_CONTENT_SYNC_BENCH. Testing the macro
// before including it would compile this whole file to nothing while the
// .ino still called into it (the exact latent bug found in
// ble_provisioning.cpp on 2026-08-03).
#include "config.h"

#ifdef AREG_CONTENT_SYNC_BENCH

#include "content_sync.h"

#include <Arduino.h>
#include <WiFi.h>
#include <HTTPClient.h>
#include <ArduinoJson.h>
#include <FS.h>
#include <SD.h>
#include <esp_task_wdt.h>
#include <mbedtls/sha256.h>

#include "config.h"
#include "net_transport.h"
#include "content_sync_rules.h"  // pure validation / path / decision logic
#include "content_sync_model.h"  // JSON <-> CsStory (manifest + index schemas)
#include "audio_io.h"      // audio_sd_available() — reuse the boot mount
#include "voice_client.h"  // voice_wifi_is_connected / voice_add_device_auth_headers

#ifndef AREG_HTTP_CONNECT_MS
#define AREG_HTTP_CONNECT_MS 5000
#endif
#ifndef AREG_HTTP_READ_MS
#define AREG_HTTP_READ_MS 30000
#endif

namespace {

constexpr const char *kIndexPath    = "/content_index.json";
constexpr const char *kIndexTmpPath = "/content_index.json.new";
// Free-space slack beyond sizeBytes (FAT allocation overhead headroom).
constexpr uint64_t kFreeSpaceSlack = 256ULL * 1024ULL;

// Bounded tables. static (not stack): the sync runs from the Arduino
// loop task, whose stack is 8 KB — ~2.3 KB per table would be reckless
// there, and this module is a one-shot so the .bss cost is paid once.
CsStory s_manifest[CS_MAX_STORIES];   // validated manifest items, in order
CsStory s_previous[CS_MAX_STORIES];   // entries read from the on-card index
CsStory s_active[CS_MAX_STORIES];     // what the new index will contain
int s_manifest_count = 0;
int s_previous_count = 0;
int s_active_count   = 0;
// B3 — the parent's spoken-story-intro toggle, from the manifest root.
// Cached into the index so the last-known value applies offline.
bool s_intro_enabled = true;
// The two story-shaping parent toggles, from the manifest root. Cached into
// the index alongside the intro flag so the last-known values apply offline.
bool s_pauses_enabled   = true;
bool s_variants_enabled = true;

// Slice E — bedtime music: manifest/prev/active tables + the parent's
// opt-in flag, all cached into the index like the intro toggle.
CsMusic s_music_manifest[CS_MAX_MUSIC];
CsMusic s_music_previous[CS_MAX_MUSIC];
CsMusic s_music_active[CS_MAX_MUSIC];
int s_music_manifest_count = 0;
int s_music_previous_count = 0;
int s_music_active_count   = 0;
bool s_music_enabled = false;

// Welcome flow — device-global spoken clips (greetings, menu prompts, the
// two fallback lines) plus the four parent mode switches, all cached into
// the index so the toy's opening works with no network.
CsVoice s_voice_manifest[CS_MAX_VOICE];
CsVoice s_voice_previous[CS_MAX_VOICE];
CsVoice s_voice_active[CS_MAX_VOICE];
int s_voice_manifest_count = 0;
int s_voice_previous_count = 0;
int s_voice_active_count   = 0;
// Offline games — the ONLY namespace with no CsStory/CsMusic/CsVoice-shaped
// tables. ~90 clips ship today; manifest/previous/active tables in the shape
// sync_voice() uses would cost roughly 47 KB of .bss, which this board does
// not have to spare (see CS_MAX_GAMES in content_sync_rules.h). Instead the
// pass streams: one CsGame at a time, results appended straight into this
// output array, and the previous index re-read from the card for the length
// of the pass only. The document lives at file scope solely so write_index()
// can attach it after sync_games() has returned; it is cleared as soon as
// the index is on disk.
JsonDocument s_games_index;
int s_games_active_count = 0;
// Default TRUE for all four: a backend that does not send them, or a
// pre-v4 card, must not silently stop the toy offering anything.
bool s_mode_story     = true;
bool s_mode_game      = true;
bool s_mode_riddle    = true;
bool s_mode_curiosity = true;

// ---- small helpers (duplicated from ota_apply's file-locals by repo
// convention: no shared util until a third caller) ----

void to_hex_lower(const uint8_t *in, size_t len, char *out /* 2*len+1 */) {
    static const char *kHex = "0123456789abcdef";
    for (size_t i = 0; i < len; i++) {
        out[2 * i]     = kHex[in[i] >> 4];
        out[2 * i + 1] = kHex[in[i] & 0x0F];
    }
    out[2 * len] = '\0';
}

bool hex_equals_ci(const char *a, const char *b) {
    const size_t la = strlen(a), lb = strlen(b);
    if (la != lb || la == 0) return false;
    uint8_t diff = 0;
    for (size_t i = 0; i < la; i++) {
        diff |= (uint8_t)(tolower((unsigned char)a[i]) ^ tolower((unsigned char)b[i]));
    }
    return diff == 0;
}

// Resolve a manifest URL: absolute http(s) passes through; a bare path is
// resolved against the backend base derived from AREG_BACKEND_URL. The
// item's audioUrl is used EXACTLY as the backend supplied it — including
// any ?storyId= query string — so no parameter is ever appended or
// duplicated here.
String resolve_url(const char *url) {
    if (strncmp(url, "http://", 7) == 0 || strncmp(url, "https://", 8) == 0) {
        return String(url);
    }
    String base = AREG_BACKEND_URL;
    base.replace("/api/chat/audio", "");
    return base + url;
}

void fail(const char *reason) {
    Serial.printf("[content-sync] FAIL (%s)\n", reason);
    Serial.flush();
}

void story_fail(const char *story_id, const char *reason) {
    Serial.printf("[content-sync] story %s FAILED (%s)\n", story_id, reason);
    Serial.flush();
}

// Streaming SHA-256 of an existing SD file. Returns false when the file
// can't be opened. Feeds the task watchdog per chunk (a 4.6 MB file at
// SPI speed takes a few seconds).
bool sha256_of_file(const char *path, char *out_hex /* 65 */, size_t *out_size) {
    File f = SD.open(path, FILE_READ);
    if (!f) {
        return false;
    }
    mbedtls_sha256_context sha;
    mbedtls_sha256_init(&sha);
    mbedtls_sha256_starts(&sha, 0);
    static uint8_t buf[4096];
    size_t total = 0;
    while (true) {
        esp_task_wdt_reset();
        const int n = f.read(buf, sizeof(buf));
        if (n <= 0) break;
        mbedtls_sha256_update(&sha, buf, (size_t)n);
        total += (size_t)n;
    }
    f.close();
    uint8_t digest[32];
    mbedtls_sha256_finish(&sha, digest);
    mbedtls_sha256_free(&sha);
    to_hex_lower(digest, sizeof(digest), out_hex);
    if (out_size != nullptr) *out_size = total;
    return true;
}

void ensure_dir(const char *dir) {
    if (!SD.exists(dir)) {
        SD.mkdir(dir);
    }
}

// Actual on-card size, or -1 when the file is absent/unopenable.
long sd_file_size(const char *path) {
    if (!SD.exists(path)) {
        return -1;
    }
    File f = SD.open(path, FILE_READ);
    if (!f) {
        return -1;
    }
    const long n = (long)f.size();
    f.close();
    return n;
}

// ---- on-card index ------------------------------------------------

// Reads /content_index.json into s_previous via the model layer, which
// understands BOTH the v2 stories[] shape and the pre-multi-story flat
// v1 object (migration). A malformed or absent index yields zero entries
// and is NOT an error — and never deletes anything.
void load_previous_index() {
    s_previous_count = 0;
    if (!SD.exists(kIndexPath)) {
        Serial.println("[content-sync] no existing index");
        return;
    }
    File f = SD.open(kIndexPath, FILE_READ);
    if (!f) {
        Serial.println("[content-sync] index open failed — treating as empty");
        return;
    }
    JsonDocument doc;
    const DeserializationError err = deserializeJson(doc, f);
    f.close();
    if (err != DeserializationError::Ok) {
        // Malformed index must fail SAFELY: we lose the fast path and
        // re-verify by hashing, but no cached MP3 is touched.
        Serial.println("[content-sync] index parse failed — cached files preserved");
        return;
    }
    int schema = 0;
    s_previous_count = cs_index_parse(doc, s_previous, CS_MAX_STORIES, &schema);
    s_music_previous_count = cs_index_parse_music(doc, s_music_previous, CS_MAX_MUSIC);
    s_voice_previous_count = cs_index_parse_voice(doc, s_voice_previous, CS_MAX_VOICE);
    Serial.printf("[content-sync] index v%d loaded entries=%d music=%d voice=%d\n",
                  schema, s_previous_count, s_music_previous_count,
                  s_voice_previous_count);
    if (schema < 2 && s_previous_count > 0) {
        Serial.printf("[content-sync] legacy v1 index migrated (%s)\n",
                      s_previous[0].story_id);
    }
}

// Finds a previous entry for this story id, or nullptr.
const CsStory *find_previous(const char *story_id) {
    for (int i = 0; i < s_previous_count; i++) {
        if (cs_story_ids_equal(s_previous[i].story_id, story_id)) {
            return &s_previous[i];
        }
    }
    return nullptr;
}

bool active_contains(const char *story_id) {
    for (int i = 0; i < s_active_count; i++) {
        if (cs_story_ids_equal(s_active[i].story_id, story_id)) {
            return true;
        }
    }
    return false;
}

// Writes the new index atomically: the full document goes to a .new file
// first, and the live path is only unlinked once that replacement is
// completely written and closed. A crash before that leaves the previous
// known-good index in place; a crash inside the remove/rename window
// leaves the .new file, and the next boot simply rebuilds the index from
// the manifest. No MP3 is ever at risk either way.
bool write_index() {
    JsonDocument idx;
    // AREG_STORY_ID drives ONLY the legacy compatibility mirror, so the
    // three readers that still parse the flat shape keep behaving exactly
    // as they did in the single-story build. It has no effect on which
    // stories are synced or indexed.
    cs_index_build(idx, s_active, s_active_count, AREG_STORY_ID, s_intro_enabled);
    cs_index_add_music(idx, s_music_active, s_music_active_count, s_music_enabled);
    cs_index_add_voice(idx, s_voice_active, s_voice_active_count);
    // Offline games — attached from the array sync_games() streamed into,
    // which is still alive here and is cleared right after the write.
    cs_index_add_games(idx, s_games_index.as<JsonArrayConst>());
    cs_index_add_modes(idx, s_mode_story, s_mode_game,
                       s_mode_riddle, s_mode_curiosity);
    cs_index_add_story_flags(idx, s_pauses_enabled, s_variants_enabled);

    if (SD.exists(kIndexTmpPath)) {
        SD.remove(kIndexTmpPath);
    }
    File f = SD.open(kIndexTmpPath, FILE_WRITE);
    if (!f) {
        return false;
    }
    const size_t written = serializeJson(idx, f);
    f.close();
    if (written == 0) {
        SD.remove(kIndexTmpPath);
        return false;
    }
    // FAT rename cannot overwrite in place.
    if (SD.exists(kIndexPath)) {
        SD.remove(kIndexPath);
    }
    if (!SD.rename(kIndexTmpPath, kIndexPath)) {
        SD.remove(kIndexTmpPath);
        return false;
    }
    return true;
}

// ---- verified download (stories AND clips) ------------------------

// Downloads one asset to `temp_path`, verifies SHA-256 and size, then
// promotes it to `final_path`. Returns true only when the final file is
// in place and verified. NEVER removes the existing final file unless a
// fully verified replacement is ready to take its place. `label` is the
// serial-log identity ("<storyId>" or "<storyId>:<kind>").
bool download_file_verified(const char *label, const char *url,
                            const char *temp_path, const char *final_path,
                            long size_bytes, const char *sha256_expected) {
    ensure_dir("/tmp");
    ensure_dir("/stories");

    const uint64_t free_bytes = SD.totalBytes() - SD.usedBytes();
    if (free_bytes < (uint64_t)size_bytes + kFreeSpaceSlack) {
        story_fail(label, "no_space");
        return false;
    }
    if (SD.exists(temp_path)) {
        SD.remove(temp_path);  // no resume in this slice — fresh .part
    }

    // Field fix, second iteration (2026-08-07, 92-clip games sync on the
    // live toy). Iteration 1 (setReuse on a per-call HTTPClient) still
    // died after ~14 files: the LOCAL HTTPClient was destroyed per call,
    // so its keep-alive never survived and every file paid a fresh
    // TCP+TLS handshake — socket churn until connects failed. This
    // object is now function-static: ONE client, ONE connection for the
    // whole burst (every content URL hits the same origin). The wdt
    // resets bracket the only phase that blocks without feeding it —
    // that half of the fix already stopped the TASK_WDT reboot loop in
    // the field (games summary line reached for the first time).
    // On a connection-level failure (< 0), the wedged shared TLS client
    // is hard-stopped and ONE retry runs on a clean handshake — the
    // recovery Railway's idle-close otherwise turns into a failed file.
    static HTTPClient dl;
    dl.setReuse(true);
    int dstatus = -1;
    for (int attempt = 0; attempt < 2; ++attempt) {
        if (!areg_http_begin(dl, resolve_url(url))) {
            story_fail(label, "download_begin_failed");
            return false;
        }
        voice_add_device_auth_headers(dl);
        dl.setConnectTimeout(AREG_HTTP_CONNECT_MS);
        dl.setTimeout(AREG_HTTP_READ_MS);
        esp_task_wdt_reset();
        dstatus = dl.GET();
        esp_task_wdt_reset();
        if (dstatus == 200) {
            break;
        }
        dl.end();
        if (dstatus >= 0) {
            break;      // real HTTP status (404/429/...) — retrying won't help
        }
        Serial.printf("[content-sync] %s connect failed (%d) — resetting TLS, retrying\n",
                      label, dstatus);
        areg_net_reset();
        delay(250);
    }
    if (dstatus != 200) {
        Serial.printf("[content-sync] %s download status=%d\n", label, dstatus);
        story_fail(label, "download_failed");
        return false;
    }

    File out = SD.open(temp_path, FILE_WRITE);
    if (!out) {
        dl.end();
        story_fail(label, "temp_open_failed");
        return false;
    }

    mbedtls_sha256_context sha;
    mbedtls_sha256_init(&sha);
    mbedtls_sha256_starts(&sha, 0);

    WiFiClient *stream = dl.getStreamPtr();
    static uint8_t buf[4096];
    const long size = size_bytes;
    long received = 0;
    int last_pct = -1;
    uint32_t last_data_ms = millis();
    bool io_ok = true;
    const char *io_err = "download_incomplete";

    while (received < size) {
        esp_task_wdt_reset();
        const size_t avail = stream->available();
        if (avail == 0) {
            if (!dl.connected() && stream->available() == 0) {
                io_ok = false;
                io_err = "connection_closed_early";
                break;
            }
            if (millis() - last_data_ms > (uint32_t)AREG_HTTP_READ_MS) {
                io_ok = false;
                io_err = "download_stalled";
                break;
            }
            delay(2);
            continue;
        }
        const size_t want = (avail < sizeof(buf)) ? avail : sizeof(buf);
        const size_t cap = (size_t)(size - received) < want
                               ? (size_t)(size - received) : want;
        const int n = stream->read(buf, cap);
        if (n <= 0) {
            delay(2);
            continue;
        }
        last_data_ms = millis();
        mbedtls_sha256_update(&sha, buf, (size_t)n);
        if (out.write(buf, (size_t)n) != (size_t)n) {
            io_ok = false;
            io_err = "sd_write_failed";
            break;
        }
        received += n;
        const int pct = (int)((received * 100) / size);
        if (pct / 10 != last_pct / 10) {
            last_pct = pct;
            Serial.printf("[content-sync] %s download %d%% (%ld/%ld)\n",
                          label, pct, received, size);
        }
    }
    dl.end();
    out.close();

    uint8_t digest[32];
    mbedtls_sha256_finish(&sha, digest);
    mbedtls_sha256_free(&sha);

    if (!io_ok || received != size) {
        SD.remove(temp_path);  // failed download never lingers
        story_fail(label, io_err);
        return false;
    }

    // Verify SHA-256 BEFORE touching the final path.
    char actual_hex[65];
    to_hex_lower(digest, sizeof(digest), actual_hex);
    if (!hex_equals_ci(actual_hex, sha256_expected)) {
        SD.remove(temp_path);
        Serial.printf("[content-sync] %s sha256 mismatch (got %s)\n",
                      label, actual_hex);
        story_fail(label, "sha256_mismatch");
        return false;
    }
    Serial.printf("[content-sync] %s sha256 ok\n", label);
    Serial.flush();

    // Atomic move into place (only now may an old copy go).
    if (SD.exists(final_path)) {
        SD.remove(final_path);  // FAT rename cannot overwrite in place
    }
    if (!SD.rename(temp_path, final_path)) {
        SD.remove(temp_path);
        story_fail(label, "rename_failed");
        return false;
    }
    Serial.printf("[content-sync] moved %s -> %s\n", temp_path, final_path);
    Serial.flush();
    return true;
}

bool download_story(const CsStory *story, const char *audio_url) {
    char temp_path[CS_MAX_PATH_LEN];
    if (!cs_build_temp_path(temp_path, sizeof(temp_path),
                            story->story_id, story->version)) {
        story_fail(story->story_id, "temp_path_too_long");
        return false;
    }
    return download_file_verified(story->story_id, audio_url, temp_path,
                                  story->cache_path, story->size_bytes,
                                  story->sha256);
}

// ---- per-story clip sync (B2) -------------------------------------
//
// Runs only for a story whose narration is in place (downloaded or
// already current). Each clip is independently checked / downloaded /
// verified; a clip failure marks THAT clip unverified and never affects
// the story or its sibling clips. The download URL is constructed —
// /api/devices/content-file?storyId=<id>&clip=<kind> — matching the
// backend's default fill for clips.
void sync_story_clips(CsStory *story) {
    for (int k = 0; k < story->clip_count; k++) {
        CsClip *clip = &story->clips[k];
        char clip_path[CS_MAX_PATH_LEN];
        if (!cs_build_clip_cache_path(clip_path, sizeof(clip_path),
                                      story->story_id, story->version,
                                      clip->kind)) {
            clip->verified = false;
            continue;
        }
        char label[CS_MAX_STORY_ID_LEN + CS_CLIP_KIND_LEN + 2];
        snprintf(label, sizeof(label), "%s:%s", story->story_id, clip->kind);

        // Already current? Requires a verified matching clip in the
        // previous index AND the file on the card at the exact size —
        // same metadata-is-never-enough rule the story check uses.
        const CsStory *prev = find_previous(story->story_id);
        if (prev != nullptr && prev->version == story->version) {
            for (int p = 0; p < prev->clip_count; p++) {
                const CsClip *pc = &prev->clips[p];
                if (strcmp(pc->kind, clip->kind) == 0
                    && pc->verified
                    && pc->size_bytes == clip->size_bytes
                    && hex_equals_ci(pc->sha256, clip->sha256)
                    && sd_file_size(clip_path) == clip->size_bytes) {
                    clip->verified = true;
                    break;
                }
            }
        }
        if (clip->verified) {
            Serial.printf("[content-sync] %s already current\n", label);
            continue;
        }

        char url[CS_MAX_URL_LEN];
        snprintf(url, sizeof(url),
                 "/api/devices/content-file?storyId=%s&clip=%s",
                 story->story_id, clip->kind);
        char temp_path[CS_MAX_PATH_LEN];
        if (!cs_build_clip_temp_path(temp_path, sizeof(temp_path),
                                     story->story_id, story->version,
                                     clip->kind)) {
            clip->verified = false;
            continue;
        }
        clip->verified = download_file_verified(
            label, url, temp_path, clip_path, clip->size_bytes, clip->sha256);
    }
}

// Is this story already on the card and current?
//
// Fast path: a verified index entry whose id/version/sha/size match the
// manifest, AND the file exists at exactly the recorded size. Index
// metadata alone is never enough — the file can vanish independently of
// the index, so existence and size are always re-checked on the card.
//
// Slow path (no usable index entry — first boot after a wipe, a
// malformed index, or a v1 card whose entry did not match): the existing
// file is streamed through SHA-256, which is what the single-story build
// did on EVERY boot. Keeping it only for the no-entry case is the
// deliberate change: re-hashing 8 × 4.6 MB per boot would cost tens of
// seconds of SPI reads for no new information.
bool already_current(const CsStory *story) {
    const long actual = sd_file_size(story->cache_path);
    if (actual < 0) {
        return false;   // no file → always download, whatever the index says
    }

    const CsStory *prev = find_previous(story->story_id);
    if (prev != nullptr
        && cs_entry_matches_manifest(prev, story->story_id, story->version,
                                     story->sha256, story->size_bytes)
        && actual == story->size_bytes) {
        Serial.printf("[content-sync] %s already current (index)\n", story->story_id);
        return true;
    }

    if (actual != story->size_bytes) {
        Serial.printf("[content-sync] %s cached size %ld != %ld — re-downloading\n",
                      story->story_id, actual, story->size_bytes);
        return false;
    }

    char existing_hex[65];
    size_t existing_size = 0;
    if (sha256_of_file(story->cache_path, existing_hex, &existing_size)
        && existing_size == (size_t)story->size_bytes
        && hex_equals_ci(existing_hex, story->sha256)) {
        Serial.printf("[content-sync] %s already cached PASS (hashed)\n", story->story_id);
        return true;
    }
    Serial.printf("[content-sync] %s cached copy stale/corrupt — re-downloading\n",
                  story->story_id);
    return false;
}

// ---- bedtime-music sync (Slice E) ---------------------------------
//
// Mirrors the story pass at track granularity: already-current check
// against the previous index + on-card size, else download/sha-verify/
// rename via the shared helper. A track failure never affects stories or
// its music siblings; tracks absent from the manifest but still verified
// on the card are carried forward (absence is not retirement).
void sync_music() {
    int already = 0, downloaded = 0, failed = 0;
    for (int i = 0; i < s_music_manifest_count; i++) {
        CsMusic *track = &s_music_manifest[i];
        char cache_path[CS_MAX_PATH_LEN];
        if (!cs_build_music_cache_path(cache_path, sizeof(cache_path),
                                       track->track_id, track->version)) {
            failed++;
            continue;
        }

        bool ok = false;
        // Already current? prev entry match + file at the recorded size.
        for (int p = 0; p < s_music_previous_count; p++) {
            const CsMusic *prev = &s_music_previous[p];
            if (cs_story_ids_equal(prev->track_id, track->track_id)
                && prev->version == track->version
                && prev->verified
                && prev->size_bytes == track->size_bytes
                && hex_equals_ci(prev->sha256, track->sha256)
                && sd_file_size(cache_path) == track->size_bytes) {
                ok = true;
                already++;
                Serial.printf("[content-sync] music %s already current\n", track->track_id);
                break;
            }
        }
        if (!ok) {
            char url[CS_MAX_URL_LEN];
            snprintf(url, sizeof(url),
                     "/api/devices/content-file?trackId=%s", track->track_id);
            char temp_path[CS_MAX_PATH_LEN];
            char label[CS_MAX_STORY_ID_LEN + 8];
            snprintf(label, sizeof(label), "m:%s", track->track_id);
            if (cs_build_music_temp_path(temp_path, sizeof(temp_path),
                                         track->track_id, track->version)) {
                SD.mkdir("/music");
                ok = download_file_verified(label, url, temp_path, cache_path,
                                            track->size_bytes, track->sha256);
            }
            if (ok) downloaded++; else failed++;
        }

        if (ok && s_music_active_count < CS_MAX_MUSIC) {
            s_music_active[s_music_active_count] = *track;
            s_music_active[s_music_active_count].verified = true;
            s_music_active_count++;
        }
    }

    // Carry forward verified tracks the manifest did not mention.
    for (int i = 0; i < s_music_previous_count && s_music_active_count < CS_MAX_MUSIC; i++) {
        const CsMusic *prev = &s_music_previous[i];
        if (!prev->verified) continue;
        bool present = false;
        for (int a = 0; a < s_music_active_count; a++) {
            if (cs_story_ids_equal(s_music_active[a].track_id, prev->track_id)) {
                present = true;
                break;
            }
        }
        if (present) continue;
        char cache_path[CS_MAX_PATH_LEN];
        if (!cs_build_music_cache_path(cache_path, sizeof(cache_path),
                                       prev->track_id, prev->version)
            || sd_file_size(cache_path) != prev->size_bytes) {
            continue;
        }
        s_music_active[s_music_active_count++] = *prev;
    }

    if (s_music_manifest_count > 0 || s_music_active_count > 0) {
        Serial.printf("[content-sync] music summary offered=%d already=%d "
                      "downloaded=%d failed=%d active=%d enabled=%d\n",
                      s_music_manifest_count, already, downloaded, failed,
                      s_music_active_count, s_music_enabled ? 1 : 0);
        Serial.flush();
    }
}

// ---- welcome-flow voice-clip sync ---------------------------------
//
// A direct copy of sync_music at clip granularity, and deliberately so:
// three namespaces on one contract is easier to reason about than one
// generalised sync with three shapes of caller. Same already-current
// check, same download/verify/rename helper, same carry-forward (a clip
// absent from one manifest is not retirement), same per-item isolation —
// one failed greeting never costs the child the other twenty-three.
void sync_voice() {
    int already = 0, downloaded = 0, failed = 0;
    for (int i = 0; i < s_voice_manifest_count; i++) {
        CsVoice *clip = &s_voice_manifest[i];
        char cache_path[CS_MAX_PATH_LEN];
        if (!cs_build_voice_cache_path(cache_path, sizeof(cache_path),
                                       clip->voice_id, clip->version)) {
            failed++;
            continue;
        }

        bool ok = false;
        for (int p = 0; p < s_voice_previous_count; p++) {
            const CsVoice *prev = &s_voice_previous[p];
            if (cs_story_ids_equal(prev->voice_id, clip->voice_id)
                && prev->version == clip->version
                && prev->verified
                && prev->size_bytes == clip->size_bytes
                && hex_equals_ci(prev->sha256, clip->sha256)
                && sd_file_size(cache_path) == clip->size_bytes) {
                ok = true;
                already++;
                break;
            }
        }
        if (!ok) {
            char url[CS_MAX_URL_LEN];
            snprintf(url, sizeof(url),
                     "/api/devices/content-file?voiceId=%s", clip->voice_id);
            char temp_path[CS_MAX_PATH_LEN];
            char label[CS_MAX_STORY_ID_LEN + 8];
            snprintf(label, sizeof(label), "v:%s", clip->voice_id);
            if (cs_build_voice_temp_path(temp_path, sizeof(temp_path),
                                         clip->voice_id, clip->version)) {
                SD.mkdir("/voice");
                ok = download_file_verified(label, url, temp_path, cache_path,
                                            clip->size_bytes, clip->sha256);
            }
            if (ok) downloaded++; else failed++;
        }

        if (ok && s_voice_active_count < CS_MAX_VOICE) {
            s_voice_active[s_voice_active_count] = *clip;
            s_voice_active[s_voice_active_count].verified = true;
            s_voice_active_count++;
        }
    }

    // Carry forward verified clips the manifest did not mention.
    for (int i = 0; i < s_voice_previous_count && s_voice_active_count < CS_MAX_VOICE; i++) {
        const CsVoice *prev = &s_voice_previous[i];
        if (!prev->verified) continue;
        bool present = false;
        for (int a = 0; a < s_voice_active_count; a++) {
            if (cs_story_ids_equal(s_voice_active[a].voice_id, prev->voice_id)) {
                present = true;
                break;
            }
        }
        if (present) continue;
        char cache_path[CS_MAX_PATH_LEN];
        if (!cs_build_voice_cache_path(cache_path, sizeof(cache_path),
                                       prev->voice_id, prev->version)
            || sd_file_size(cache_path) != prev->size_bytes) {
            continue;
        }
        s_voice_active[s_voice_active_count++] = *prev;
    }

    if (s_voice_manifest_count > 0 || s_voice_active_count > 0) {
        Serial.printf("[content-sync] voice summary offered=%d already=%d "
                      "downloaded=%d failed=%d voice_active=%d\n",
                      s_voice_manifest_count, already, downloaded, failed,
                      s_voice_active_count);
        Serial.flush();
    }
}

// ---- offline-game clip sync ---------------------------------------
//
// Same contract as sync_voice — already-current check against the previous
// index plus the on-card size, else download / sha-verify / atomic rename
// through the shared helper; per-item isolation, so one failed clip never
// costs the child the other eighty-nine; carry-forward, because absence
// from one manifest is not retirement.
//
// The IMPLEMENTATION differs in exactly one way, and it is the reason this
// slice exists at all: there are ~90 clips, so nothing is held in a table.
// The pass streams straight from the manifest document into the output
// index array, and re-reads the previous index from the card instead of
// keeping a parsed copy alive for the whole run. See CS_MAX_GAMES in
// content_sync_rules.h for the .bss arithmetic that forced this.
//
// The other difference is on the card, not in RAM: a game clip's filename
// carries NO version, because offline_games.cpp resolves it as
// "/games/<key>/<clip>.mp3" and a versioned name would never be found. A
// re-render therefore overwrites in place — which is safe here for the same
// reason it is safe everywhere else in this file, because the existing copy
// is only removed once a fully verified replacement is ready to take it.

// Has this pair already been written to the output array this run?
bool games_out_contains(JsonArrayConst out, const char *game_key,
                        const char *clip_id) {
    for (JsonObjectConst e : out) {
        if (cs_story_ids_equal(e["gameKey"] | "", game_key)
            && cs_story_ids_equal(e["clipId"] | "", clip_id)) {
            return true;
        }
    }
    return false;
}

// Finds this pair in the previous index, or returns false.
bool find_previous_game(JsonArrayConst prev_games, const char *game_key,
                        const char *clip_id, CsGame *out) {
    for (JsonObjectConst e : prev_games) {
        if (!cs_index_read_game(e, out)) {
            continue;
        }
        if (cs_game_pairs_equal(out, game_key, clip_id)) {
            return true;
        }
    }
    return false;
}

void sync_games(JsonArrayConst games) {
    JsonArray out = s_games_index.to<JsonArray>();
    s_games_active_count = 0;

    // The previous index is re-read here rather than carried in RAM from
    // load_previous_index(): a parsed copy of ~90 entries is ~20 KB of heap
    // and it is only needed for the length of this pass. The document is
    // scoped to this function so it is freed before write_index() builds
    // its own.
    JsonDocument prev;
    JsonArrayConst prev_games;
    if (SD.exists(kIndexPath)) {
        File f = SD.open(kIndexPath, FILE_READ);
        if (f) {
            const DeserializationError err = deserializeJson(prev, f);
            f.close();
            if (err == DeserializationError::Ok && prev["games"].is<JsonArrayConst>()) {
                prev_games = prev["games"].as<JsonArrayConst>();
            }
        }
    }

    int offered = 0, invalid = 0, already = 0, downloaded = 0, failed = 0;
    int truncated = 0, carried = 0;
    if (!games.isNull()) {
        for (JsonObjectConst item : games) {
            offered++;
            if (s_games_active_count >= CS_MAX_GAMES) {
                truncated++;
                continue;   // reported below, never fatal
            }

            CsGame g;
            if (!cs_manifest_read_game(item, &g)) {
                invalid++;
                continue;
            }
            if (games_out_contains(out, g.game_key, g.clip_id)) {
                // Field diagnostic 2026-08-07: a real boot skipped 91 of 92
                // clips through this branch (summary counters proved it) and
                // this was the only uncounted exit. Log WHAT it thinks it
                // matched so the next serial capture settles it.
                Serial.printf("[content-sync] game dup-skip %s/%s out_n=%u\n",
                              g.game_key, g.clip_id, (unsigned)out.size());
                continue;   // duplicate pair — keep the first, as everywhere
            }

            char cache_path[CS_MAX_PATH_LEN];
            if (!cs_build_game_cache_path(cache_path, sizeof(cache_path),
                                          g.game_key, g.clip_id)) {
                failed++;
                continue;
            }

            bool ok = false;
            CsGame p;
            if (find_previous_game(prev_games, g.game_key, g.clip_id, &p)
                && p.verified
                && p.version == g.version
                && p.size_bytes == g.size_bytes
                && hex_equals_ci(p.sha256, g.sha256)
                && sd_file_size(cache_path) == g.size_bytes) {
                ok = true;
                already++;
            }

            if (!ok) {
                char url[CS_MAX_URL_LEN];
                snprintf(url, sizeof(url),
                         "/api/devices/content-file?gameKey=%s&clipId=%s",
                         g.game_key, g.clip_id);
                char temp_path[CS_MAX_PATH_LEN];
                char dir_path[CS_MAX_PATH_LEN];
                char label[2 * CS_MAX_STORY_ID_LEN + 8];
                snprintf(label, sizeof(label), "g:%s/%s", g.game_key, g.clip_id);
                if (cs_build_game_temp_path(temp_path, sizeof(temp_path),
                                            g.game_key, g.clip_id)
                    && cs_build_game_dir_path(dir_path, sizeof(dir_path),
                                              g.game_key)) {
                    // The per-game subdirectory must exist before the rename,
                    // not just before the download — the .part file lives in
                    // /tmp and only the promotion touches /games.
                    ensure_dir(CS_GAMES_DIR);
                    ensure_dir(dir_path);
                    ok = download_file_verified(label, url, temp_path, cache_path,
                                                g.size_bytes, g.sha256);
                }
                if (ok) downloaded++; else failed++;
            }

            if (ok) {
                g.verified = true;
                cs_index_append_game(out, &g);
                s_games_active_count++;
            }
        }
    }

    // Carry forward verified clips the manifest did not mention. A clip
    // absent from one manifest response is not a retirement instruction, and
    // dropping its index entry would cost a needless re-download next boot.
    for (JsonObjectConst e : prev_games) {
        if (s_games_active_count >= CS_MAX_GAMES) {
            break;
        }
        CsGame p;
        if (!cs_index_read_game(e, &p) || !p.verified) {
            continue;
        }
        if (games_out_contains(out, p.game_key, p.clip_id)) {
            continue;
        }
        char cache_path[CS_MAX_PATH_LEN];
        if (!cs_build_game_cache_path(cache_path, sizeof(cache_path),
                                      p.game_key, p.clip_id)
            || sd_file_size(cache_path) != p.size_bytes) {
            continue;   // file gone or changed → drop from the index, keep the file
        }
        cs_index_append_game(out, &p);
        s_games_active_count++;
        carried++;
    }

    if (offered > 0 || s_games_active_count > 0) {
        Serial.printf("[content-sync] games summary offered=%d invalid=%d "
                      "already=%d downloaded=%d failed=%d truncated=%d "
                      "carried=%d games_active=%d\n",
                      offered, invalid, already, downloaded, failed, truncated,
                      carried, s_games_active_count);
        Serial.flush();
    }
}

// ---- the one sync attempt -----------------------------------------

// All [content-sync] serial lines here are the bench PASS/FAIL evidence
// contract — keep them stable.
void content_sync_run() {
    Serial.println("[content-sync] starting");
    Serial.printf("[content-sync] heap before=%u\n", (unsigned)ESP.getFreeHeap());
    Serial.flush();

    s_manifest_count = 0;
    s_previous_count = 0;
    s_active_count   = 0;
    s_music_manifest_count = 0;
    s_music_previous_count = 0;
    s_music_active_count   = 0;
    s_voice_manifest_count = 0;
    s_voice_previous_count = 0;
    s_voice_active_count   = 0;
    s_games_index.clear();
    s_games_active_count = 0;

    // ---- 1. Manifest ----
    HTTPClient http;
    if (!areg_http_begin(http, resolve_url("/api/devices/content-manifest"))) {
        fail("manifest_begin_failed");
        return;
    }
    voice_add_device_auth_headers(http);
    http.setConnectTimeout(AREG_HTTP_CONNECT_MS);
    http.setTimeout(AREG_HTTP_READ_MS);
    const int mstatus = http.GET();
    if (mstatus != 200) {
        http.end();
        Serial.printf("[content-sync] manifest status=%d\n", mstatus);
        fail("manifest_fetch_failed");
        return;
    }
    JsonDocument doc;
    const DeserializationError jerr = deserializeJson(doc, http.getString());
    http.end();
    if (jerr != DeserializationError::Ok) {
        fail("manifest_parse_failed");
        return;
    }
    JsonArray stories = doc["stories"].as<JsonArray>();
    // B3 — per-device intro toggle rides the manifest; absent (pre-B3
    // backend) means the shipped default (ON).
    s_intro_enabled = doc["storyIntroEnabled"] | true;
    // The two story-shaping toggles ride the same manifest root; absent (a
    // pre-toggle backend) means the shipped default (both ON).
    s_pauses_enabled   = doc["storyPausesEnabled"]    | true;
    s_variants_enabled = doc["variantEndingsEnabled"] | true;
    // Slice E — bedtime-music opt-in + track list (absent → off/none).
    s_music_enabled = doc["bedtimeMusicEnabled"] | false;
    s_music_manifest_count = cs_manifest_parse_music(
        doc["music"].as<JsonArrayConst>(), s_music_manifest, CS_MAX_MUSIC);
    // Welcome flow — the four parent mode switches and the device-global
    // spoken clips. Every field absent (a pre-welcome backend) means
    // "every mode on, no clips", which is exactly the pre-welcome toy.
    s_mode_story     = doc["storyEnabled"]     | true;
    s_mode_game      = doc["gameEnabled"]      | true;
    s_mode_riddle    = doc["riddleEnabled"]    | true;
    s_mode_curiosity = doc["curiosityEnabled"] | true;
    s_voice_manifest_count = cs_manifest_parse_voice(
        doc["voice"].as<JsonArrayConst>(), s_voice_manifest, CS_MAX_VOICE);
    // Offline games — deliberately NOT parsed into a table here (there is
    // none); the array is handed to sync_games() below and streamed.
    JsonArrayConst game_clips = doc["games"].as<JsonArrayConst>();
    const unsigned game_offered =
        game_clips.isNull() ? 0U : (unsigned)game_clips.size();
    Serial.printf("[content-sync] manifest status=200 stories=%u introEnabled=%d "
                  "music=%d musicEnabled=%d voice=%d games=%u modes=%d%d%d%d "
                  "pauses=%d variants=%d\n",
                  stories.isNull() ? 0U : (unsigned)stories.size(),
                  s_intro_enabled ? 1 : 0,
                  s_music_manifest_count, s_music_enabled ? 1 : 0,
                  s_voice_manifest_count, game_offered,
                  s_mode_story ? 1 : 0, s_mode_game ? 1 : 0,
                  s_mode_riddle ? 1 : 0, s_mode_curiosity ? 1 : 0,
                  s_pauses_enabled ? 1 : 0, s_variants_enabled ? 1 : 0);
    Serial.flush();
    if ((stories.isNull() || stories.size() == 0)
        && s_music_manifest_count == 0
        && s_voice_manifest_count == 0
        && game_offered == 0) {
        // An empty manifest is NOT a retirement instruction. The index and
        // every cached MP3 are left exactly as they are.
        Serial.println("[content-sync] no content — existing cache untouched");
        Serial.flush();
        return;
    }

    CsManifestStats stats{};
    s_manifest_count = cs_manifest_parse(stories, s_manifest, CS_MAX_STORIES, &stats);
    if (stats.truncated > 0) {
        Serial.printf("[content-sync] manifest truncated: %d offered, max %d, %d ignored\n",
                      stats.offered, CS_MAX_STORIES, stats.truncated);
        Serial.flush();
    }
    if (s_manifest_count == 0 && s_music_manifest_count == 0
        && s_voice_manifest_count == 0 && game_offered == 0) {
        Serial.println("[content-sync] no valid items — existing cache untouched");
        Serial.flush();
        return;
    }

    // ---- 2. Existing index (v2, or a migrated v1) ----
    load_previous_index();

    // ---- 3. Per-story sync ----
    int already = 0, downloaded = 0, failed = 0;
    for (int i = 0; i < s_manifest_count; i++) {
        CsStory *story = &s_manifest[i];
        Serial.printf("[content-sync] item %s v%d \"%s\" %ld bytes\n",
                      story->story_id, story->version, story->title, story->size_bytes);
        Serial.flush();

        bool ok;
        if (already_current(story)) {
            already++;
            ok = true;
        } else {
            // audioUrl was captured (bounded) at parse time and is used
            // EXACTLY as the backend supplied it — query string included.
            ok = download_story(story, story->audio_url);
            if (ok) {
                downloaded++;
            } else {
                failed++;
            }
        }

        if (ok && s_active_count < CS_MAX_STORIES) {
            // B2 — clips ride along only once the narration itself is in
            // place. A clip failure marks that clip unverified in the
            // index (playback then skips it) and never fails the story.
            sync_story_clips(story);
            s_active[s_active_count] = *story;
            s_active[s_active_count].verified = true;
            s_active_count++;
        }
    }

    // ---- 4. Carry forward stories the manifest did not mention ----
    // Conservative on purpose: absence from ONE manifest response is not
    // a retirement instruction, so a previously verified story whose file
    // is still on the card stays usable. Manifest order is preserved
    // first; carried-forward entries follow.
    int carried = 0;
    for (int i = 0; i < s_previous_count && s_active_count < CS_MAX_STORIES; i++) {
        const CsStory *prev = &s_previous[i];
        if (!prev->verified || active_contains(prev->story_id)) {
            continue;
        }
        const long actual = sd_file_size(prev->cache_path);
        if (actual < 0 || (prev->size_bytes > 0 && actual != prev->size_bytes)) {
            continue;   // file gone or changed → drop from the index, keep the file
        }
        s_active[s_active_count++] = *prev;
        carried++;
    }

    // ---- 4b. Bedtime music (Slice E) ----
    sync_music();

    // ---- 4c. Welcome-flow spoken clips ----
    sync_voice();

    // ---- 4d. Offline-game clips (streamed; no table) ----
    sync_games(game_clips);

    // ---- 5. Index written LAST, once ----
    bool index_written = false;
    if (s_active_count > 0 || s_music_active_count > 0
        || s_voice_active_count > 0 || s_games_active_count > 0) {
        index_written = write_index();
        if (!index_written) {
            // The previous index survives a failed replacement; every MP3
            // is untouched. Next boot rebuilds it.
            fail("index_write_failed");
        } else {
            Serial.println("[content-sync] index written");
        }
    } else {
        Serial.println("[content-sync] nothing verified — index left unchanged");
    }
    // The streamed games array is only needed until the index is on disk;
    // releasing it here gives the ~90-entry document's heap back for the
    // rest of the boot (a TLS handshake during audio wants 40-50 KB).
    s_games_index.clear();
    Serial.flush();

    // ---- 6. Aggregate result (no credentials, no URLs, no headers) ----
    Serial.printf("[content-sync] summary manifest_items=%d accepted_items=%d "
                  "invalid_items=%d duplicate_items=%d disabled_items=%d "
                  "truncated_items=%d already_current=%d downloaded=%d failed=%d "
                  "carried_forward=%d active_index_items=%d index_written=%d\n",
                  stats.offered, s_manifest_count, stats.invalid, stats.duplicate,
                  stats.disabled, stats.truncated, already, downloaded, failed, carried,
                  s_active_count, index_written ? 1 : 0);
    Serial.printf("[content-sync] heap after=%u\n", (unsigned)ESP.getFreeHeap());
    Serial.flush();

    if (failed > 0) {
        Serial.printf("[content-sync] PARTIAL (%d ok, %d failed)\n",
                      already + downloaded, failed);
    } else {
        Serial.println("[content-sync] PASS");
    }
    Serial.flush();
}

}  // namespace

// Bench observability: hold the one-shot until ms >= kBenchStartMs so the
// operator has time to attach the serial monitor after an upload (the
// first bench run fired inside the upload→monitor gap and was never
// seen; RST replays are not an option on this bench — EN resets drop
// COM7). Until then, a heartbeat line prints every few seconds so a
// silent monitor is immediately distinguishable from a wrong build.
static constexpr uint32_t kBenchStartMs   = 180000UL;  // 3 min arm delay
static constexpr uint32_t kStatusEveryMs  = 5000UL;
static constexpr uint32_t kSdInitRetryMs  = 30000UL;   // remount retry cadence

void content_sync_tick() {
    static bool s_done = false;
    static bool s_stamped = false;
    static uint32_t s_last_status_ms = 0;
    static uint32_t s_last_sd_init_ms = 0;
    if (s_done) {
        return;
    }

    if (!s_stamped) {
        // One-time build stamp so the serial log itself proves WHICH bench
        // image is running (a stale flash was mistaken for missing code once).
        s_stamped = true;
        Serial.println("[content-sync] bench fw built " __DATE__ " " __TIME__);
        Serial.flush();
    }

    const uint32_t now = millis();
    const bool status_due =
        (s_last_status_ms == 0) || (now - s_last_status_ms >= kStatusEveryMs);

    if (now < kBenchStartMs) {
        if (status_due) {
            s_last_status_ms = now;
            Serial.println("[content-sync] bench build enabled; waiting for "
                           "WiFi+SD; will start at ms>=180000");
            Serial.flush();
        }
        return;
    }
    if (!voice_wifi_is_connected()) {
        if (status_due) {
            s_last_status_ms = now;
            Serial.println("[content-sync] waiting wifi");
            Serial.flush();
        }
        return;
    }

    if (!audio_sd_available()) {
        // Explicit ACTIVE remount, printed BEFORE the availability re-check.
        // audio_sd_begin() (audio_io.cpp) is the exact helper the SD bench
        // proof used — idempotent and retryable; it prints its own
        // "[sd] mounted ..." / "[sd] SD.begin failed ..." reason line between
        // our start/ok/failed markers. Retried every kSdInitRetryMs so a
        // reseated card recovers WITHOUT an RST (RST drops COM7 on this
        // bench); the boot-time mount result was never visible on a monitor
        // attached post-upload.
        const bool init_due =
            (s_last_sd_init_ms == 0) || (now - s_last_sd_init_ms >= kSdInitRetryMs);
        if (init_due) {
            s_last_sd_init_ms = now;
            Serial.printf("[content-sync] sd init start cs=%d sck=%d mosi=%d miso=%d\n",
                          AREG_PIN_SD_CS, AREG_PIN_SD_SCK,
                          AREG_PIN_SD_MOSI, AREG_PIN_SD_MISO);
            Serial.flush();
            if (audio_sd_begin()) {
                Serial.println("[content-sync] sd init ok");
                Serial.flush();
                // Fall through — audio_sd_available() is true now; the sync
                // starts in THIS tick.
            } else {
                Serial.println("[content-sync] sd init failed");
                Serial.flush();
            }
        }
        if (!audio_sd_available()) {
            if (status_due) {
                s_last_status_ms = now;
                Serial.println("[content-sync] waiting sd");
                Serial.flush();
            }
            return;
        }
    }

    s_done = true;  // one attempt per boot, even if it fails (bench slice)
    content_sync_run();
}

#endif  // AREG_CONTENT_SYNC_BENCH
