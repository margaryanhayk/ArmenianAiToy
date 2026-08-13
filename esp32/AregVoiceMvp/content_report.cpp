#include "content_report.h"

#include <Arduino.h>
#include <ArduinoJson.h>
#include <FS.h>
#include <SD.h>
#include <string.h>

#include "audio_io.h"            // audio_sd_available()
#include "content_report_rules.h"  // pure buffer arithmetic (host-testable)

namespace {

constexpr const char *kIndexPath = "/content_index.json";

// Sized from the real library, not from the theoretical maximum. The
// shipped ids are 4-16 characters, so the ten-story library occupies about
// 120 B; 320 carries it with 2.5x headroom and holds sixteen ids of that
// size. The absolute worst case — CS_MAX_STORIES ids at the 48-character
// allowlist limit — is ~880 B and would truncate, which cr_append_story()
// does at an ENTRY boundary: half an id would reach the backend as an
// unrecognised story and be counted as missing, which is the one wrong
// answer this feature must not give.
//
// The size is deliberate rather than generous: this string ends up in a
// stack buffer in voice_send_heartbeat(), on the 8 KB Arduino loop task.
constexpr size_t kStoriesBufLen = 320;

char s_stories[kStoriesBufLen];
char s_sent_stories[kStoriesBufLen];

int  s_schema       = 0;
int  s_game_clips   = 0;
int  s_voice_clips  = 0;
int  s_music_tracks = 0;

int  s_sent_schema       = -1;
int  s_sent_game_clips   = -1;
int  s_sent_voice_clips  = -1;
int  s_sent_music_tracks = -1;

bool s_available   = false;
bool s_ever_sent   = false;

}  // namespace

void content_report_refresh() {
    s_available   = false;
    s_stories[0]  = '\0';
    s_schema      = 0;
    s_game_clips  = 0;
    s_voice_clips = 0;
    s_music_tracks = 0;

    if (!audio_sd_available() || !SD.exists(kIndexPath)) {
        return;
    }
    File f = SD.open(kIndexPath, FILE_READ);
    if (!f) {
        return;
    }
    JsonDocument doc;
    const DeserializationError err = deserializeJson(doc, f);
    f.close();
    if (err) {
        Serial.printf("[content-report] index parse failed: %s\n", err.c_str());
        return;
    }

    s_schema = doc["schemaVersion"] | 1;

    size_t used = 0;
    JsonArrayConst stories = doc["stories"];
    if (!stories.isNull()) {
        for (JsonObjectConst e : stories) {
            // Only VERIFIED entries. An unverified one is a story whose
            // sha256 did not match, which will not play; counting it would
            // report a library that is not there.
            if (!(e["verified"] | false)) {
                continue;
            }
            cr_append_story(s_stories, kStoriesBufLen, used,
                            e["storyId"] | "", e["version"] | 0);
        }
    }

    // Clip namespaces are counts only — no surface names an individual
    // clip, and 104 game-clip ids would be a kilobyte on every heartbeat.
    JsonArrayConst games = doc["games"];
    if (!games.isNull()) s_game_clips = (int)games.size();
    JsonArrayConst voice = doc["voice"];
    if (!voice.isNull()) s_voice_clips = (int)voice.size();
    JsonArrayConst music = doc["music"];
    if (!music.isNull()) s_music_tracks = (int)music.size();

    s_available = true;
    Serial.printf("[content-report] schema=%d stories=[%s] games=%d voice=%d music=%d\n",
                  s_schema, s_stories, s_game_clips, s_voice_clips, s_music_tracks);
}

bool content_report_available() {
    return s_available;
}

bool content_report_changed() {
    if (!s_available) {
        return false;
    }
    if (!s_ever_sent) {
        return true;   // once per boot, even if the card is unchanged
    }
    return s_schema != s_sent_schema
        || s_game_clips != s_sent_game_clips
        || s_voice_clips != s_sent_voice_clips
        || s_music_tracks != s_sent_music_tracks
        || strncmp(s_stories, s_sent_stories, kStoriesBufLen) != 0;
}

void content_report_mark_sent() {
    if (!s_available) {
        return;
    }
    strncpy(s_sent_stories, s_stories, kStoriesBufLen - 1);
    s_sent_stories[kStoriesBufLen - 1] = '\0';
    s_sent_schema       = s_schema;
    s_sent_game_clips   = s_game_clips;
    s_sent_voice_clips  = s_voice_clips;
    s_sent_music_tracks = s_music_tracks;
    s_ever_sent = true;
}

size_t content_report_json_fields(char *out, size_t len) {
    if (out == nullptr || len == 0 || !s_available) {
        return 0;
    }
    // The story ids are drawn from the content-sync allowlist (a-z0-9-_),
    // so nothing here can need JSON escaping — same reasoning the firmware
    // identity fields in the heartbeat body already rely on.
    const int n = snprintf(out, len,
                           "\"contentIndexSchema\":%d,\"contentStories\":\"%s\","
                           "\"contentGameClips\":%d,\"contentVoiceClips\":%d,"
                           "\"contentMusicTracks\":%d",
                           s_schema, s_stories, s_game_clips, s_voice_clips,
                           s_music_tracks);
    if (n <= 0 || (size_t)n >= len) {
        out[0] = '\0';   // never emit a half-written field
        return 0;
    }
    return (size_t)n;
}
