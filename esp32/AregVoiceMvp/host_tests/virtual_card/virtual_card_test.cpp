// Virtual card test: the REAL content_index_io.cpp, story_select.cpp,
// content_report.cpp and content_sync_model.cpp against an in-memory SD card
// and NVS. Nothing here is a copy of firmware logic - the sources are staged
// from the sketch folder by run_virtual_card.py and only the hardware edge
// (SD, flash, Arduino) is replaced. See README.md.
//
// Born from the 2026-10-29 bench: `index parse failed: IncompleteInput` seven
// times in the first 30 s, the same file fine at 3 min, and a toy that fell
// back to a Wi-Fi stream and went silent. These tests pin the cure (retry,
// then the previous copy) and the story rotation that sits on top of it.
//
// What it does NOT cover: real SPI timing, a real dying card, the 90 KB heap
// the toy actually has, or anything the child hears.
#include <Arduino.h>
#include <ArduinoJson.h>
#include <map>
#include <string>
#include <vector>

#include "SD.h"
#include "Preferences.h"
#include "esp_task_wdt.h"
#include "content_sync_rules.h"
#include "content_sync_model.h"
#include "content_index_io.h"
#include "story_select.h"
#include "content_report.h"

bool g_sd_available = true;
bool audio_sd_available() { return g_sd_available; }
bool audio_sd_has_file(const char *p) { return SD.exists(p); }

static int failures = 0;
static void check(bool ok, const std::string &what, const std::string &detail = "") {
    printf("  %-4s %s%s%s\n", ok ? "ok" : "FAIL", what.c_str(), detail.empty() ? "" : "  -- ", detail.c_str());
    if (!ok) failures++;
}

static const char *kPrimary = "/content_index.json";
static const char *kBackup  = "/content_index.json.prev";
static const char *kSha = "4ba0969646dfcb34ede49b3c82ac234a55299ab6789354f0ccbc6beb64f7e631";

static void mkstory(CsStory *s, const char *id, int version, long size) {
    memset(s, 0, sizeof(*s));
    cs_copy_bounded(s->story_id, sizeof(s->story_id), id);
    cs_copy_bounded(s->sha256, sizeof(s->sha256), kSha);
    cs_copy_bounded(s->title, sizeof(s->title), u8"Խոսող ձուկը ետ մյուս");
    s->version = version; s->size_bytes = size; s->verified = true;
    cs_build_cache_path(s->cache_path, sizeof(s->cache_path), id, version);
    const char *kinds[] = {"intro", "question", "question1", "question2", "summary", "offer", "reoffer"};
    for (int i = 0; i < CS_MAX_CLIPS; i++) {
        CsClip *c = &s->clips[i]; memset(c, 0, sizeof(*c));
        cs_copy_bounded(c->kind, sizeof(c->kind), kinds[i]);
        cs_copy_bounded(c->sha256, sizeof(c->sha256), kSha);
        c->size_bytes = 30000; c->verified = true;
    }
    s->clip_count = CS_MAX_CLIPS;
}

struct Card { std::string index_json; std::vector<CsStory> stories; };

// The card the bench toy reported: 16 stories x 7 clips, 42 voice, 104 games, 4 music.
static Card build_card(int n_stories, int n_voice, int n_games, int n_music, bool intro_enabled = true) {
    Card c; c.stories.resize(n_stories);
    for (int i = 0; i < n_stories; i++) {
        char id[48]; snprintf(id, sizeof(id), "story-%02d-sample-name", i + 1);
        mkstory(&c.stories[i], id, 1 + (i % 11), 2000000L + 250000L * i);
    }
    JsonDocument doc;
    cs_index_build(doc, c.stories.data(), n_stories, n_stories ? c.stories[0].story_id : nullptr, intro_enabled);
    cs_index_add_modes(doc, true, true, true, true);
    cs_index_add_story_flags(doc, true, true);
    cs_index_add_questions_flag(doc, true);
    std::vector<CsVoice> voice(n_voice);
    for (int i = 0; i < n_voice; i++) {
        CsVoice &v = voice[i]; memset(&v, 0, sizeof(v));
        snprintf(v.voice_id, sizeof(v.voice_id), i < 8 ? "greet-%02d" : "line-%02d", i);
        v.version = 1; cs_copy_bounded(v.sha256, sizeof(v.sha256), kSha); v.size_bytes = 20000; v.verified = true;
    }
    if (n_voice) cs_index_add_voice(doc, voice.data(), n_voice);
    std::vector<CsMusic> music(n_music);
    for (int i = 0; i < n_music; i++) {
        CsMusic &m = music[i]; memset(&m, 0, sizeof(m));
        snprintf(m.track_id, sizeof(m.track_id), "bedtime-%02d", i); m.version = 1;
        cs_copy_bounded(m.title, sizeof(m.title), "Bedtime"); cs_copy_bounded(m.sha256, sizeof(m.sha256), kSha);
        m.size_bytes = 900000; m.verified = true;
    }
    if (n_music) cs_index_add_music(doc, music.data(), n_music, false);
    JsonDocument gdoc; JsonArray garr = gdoc.to<JsonArray>();
    for (int i = 0; i < n_games; i++) {
        CsGame g; memset(&g, 0, sizeof(g));
        snprintf(g.game_key, sizeof(g.game_key), "game-%02d", i / 8);
        snprintf(g.clip_id, sizeof(g.clip_id), "clip-%03d", i);
        g.version = 1; cs_copy_bounded(g.sha256, sizeof(g.sha256), kSha); g.size_bytes = 15000; g.verified = true;
        cs_index_append_game(garr, &g);
    }
    if (n_games) cs_index_add_games(doc, garr);
    serializeJson(doc, c.index_json);
    return c;
}

// A healthy card: primary index, an intact previous copy, and every story file.
static void install_card(const Card &c, bool with_backup = true) {
    SD.files.clear();
    SD.opens = 0;
    g_sd_available = true;
    Serial.clear();
    // Time passes between scenarios: longer than either memo, so what one
    // scenario taught the reader about a bad card cannot leak into the next.
    g_host_millis += CS_INDEX_PRIMARY_SKIP_MS + CS_INDEX_DEAD_MEMO_MS + 1;
    SD.put(kPrimary, c.index_json);
    if (with_backup) SD.put(kBackup, c.index_json);
    for (auto &s : c.stories) SD.put_sized(s.cache_path, s.size_bytes);
}

static int eligible_count() {
    CsStory el[CS_MAX_STORIES];
    return story_select_load_eligible(el, CS_MAX_STORIES);
}

int main() {
    Serial.echo = false;   // assertions read Serial.captured; keep the output to our own lines

    const Card full = build_card(16, 42, 104, 4);
    printf("realistic index = %zu bytes (16 stories x 7 clips, 42 voice, 104 games, 4 music)\n", full.index_json.size());

    // ---- a healthy card ---------------------------------------------------
    install_card(full);
    {
        const int feeds0 = g_wdt_feeds;
        check(eligible_count() == 16, "healthy card: all 16 stories eligible");
        check(g_wdt_feeds - feeds0 >= 2, "a read feeds the task watchdog on both sides", "feeds=" + std::to_string(g_wdt_feeds - feeds0));
        check(!Serial.saw("index parse failed"), "a healthy read prints no failure line");
        content_report_refresh();
        check(content_report_available(), "content report reads the full card");
    }

    // ---- the bench symptom: a short read, then fine ------------------------
    install_card(full);
    SD.files[kPrimary]->short_reads_left = 2;     // the first two reads come back half-empty
    {
        const uint32_t t0 = g_host_millis;
        check(eligible_count() == 16, "two short reads, third attempt succeeds: 16 stories, no silent toy");
        check(SD.files[kPrimary]->opens == 3, "it really did try three times", "opens=" + std::to_string(SD.files[kPrimary]->opens));
        check(Serial.saw("index read recovered on attempt 3"), "the recovery is named on the serial line");
        check(!Serial.saw("index parse failed"), "a recovered read is not reported as a failure");
        check(g_host_millis - t0 == (uint32_t)(CS_INDEX_RETRY_GAP_MS * 2), "the retries cost two settle gaps and nothing more");
    }

    // ---- short reads that outlast the retries: the previous copy ------------
    install_card(full);
    SD.files[kPrimary]->short_reads_left = CS_INDEX_READ_TRIES;
    check(eligible_count() == 16, "primary fails every attempt, previous copy intact: 16 stories");
    check(Serial.saw("using the previous copy (primary unreadable)"), "falling back is named on the serial line");

    // ---- permanent damage ---------------------------------------------------
    install_card(full);
    SD.files[kPrimary]->fail_after = full.index_json.size() / 2;       // a dying card
    check(eligible_count() == 16, "damaged primary, intact backup: stories come from the backup (was 0 before 2026-10-29)");
    install_card(full);
    SD.files[kPrimary]->bytes.resize(full.index_json.size() / 2);       // truncated on the card
    check(eligible_count() == 16, "truncated primary, intact backup: stories come from the backup");
    install_card(full, /*with_backup=*/false);
    SD.files[kPrimary]->bytes.resize(full.index_json.size() / 2);
    check(eligible_count() == 0, "truncated primary and no backup: no stories, no crash");
    check(Serial.saw("index parse failed: IncompleteInput"), "the failure line keeps the exact phrase bench_watch.py looks for");

    // ---- the primary is missing (the power-loss window) ----------------------
    install_card(full);
    SD.files.erase(kPrimary);
    check(eligible_count() == 16, "primary missing, backup intact: readers read through to it");
    check(Serial.saw("using the previous copy (primary missing)"), "and say why");

    // ---- nothing on the card ------------------------------------------------
    {
        JsonDocument doc;
        SD.files.clear(); SD.opens = 0; Serial.clear();
        check(content_index_load(doc, "test") == CONTENT_INDEX_ABSENT, "no index at all is ABSENT, not an error");
        check(Serial.captured.empty(), "and says nothing");
        install_card(full);
        g_sd_available = false;
        SD.opens = 0;
        check(content_index_load(doc, "test") == CONTENT_INDEX_ABSENT, "no card is ABSENT");
        check(SD.opens == 0, "and touches nothing");
        g_sd_available = true;
    }

    // ---- a dying card must not be hammered ----------------------------------
    install_card(full, false);
    SD.files[kPrimary]->bytes.resize(100);
    {
        JsonDocument doc;
        check(content_index_load(doc, "test") == CONTENT_INDEX_UNREADABLE, "unreadable index is reported UNREADABLE");
        const int opens_after_first = SD.files[kPrimary]->opens;
        check(opens_after_first == CS_INDEX_READ_TRIES, "it tried exactly the planned number of times", "opens=" + std::to_string(opens_after_first));
        check(content_index_load(doc, "test") == CONTENT_INDEX_UNREADABLE, "a second caller right after also gets UNREADABLE");
        check(SD.files[kPrimary]->opens == opens_after_first, "but without touching the card again (the boot chain asks several times)");
        g_host_millis += CS_INDEX_DEAD_MEMO_MS + 1;
        check(content_index_load(doc, "test") == CONTENT_INDEX_UNREADABLE && SD.files[kPrimary]->opens > opens_after_first,
              "after the memo window it looks again");
        SD.files[kPrimary]->bytes = full.index_json;     // the card recovers
        g_host_millis += CS_INDEX_DEAD_MEMO_MS + 1;
        check(content_index_load(doc, "test") == CONTENT_INDEX_OK, "and a recovered card is read normally");
    }

    // ---- repair (content sync only) -------------------------------------------
    install_card(full);
    SD.files[kPrimary]->fail_after = 50;
    {
        JsonDocument doc;
        check(content_index_load(doc, "content-sync", /*fresh=*/true) == CONTENT_INDEX_OK_FROM_BACKUP, "sync reads the backup when the primary is damaged");
        check(content_index_repair_from_backup(), "repair succeeds");
        check(SD.exists(kPrimary) && !SD.exists(kBackup), "the backup now sits in the primary's place");
        check(SD.files[kPrimary]->bytes == full.index_json && SD.files[kPrimary]->fail_after == (size_t)-1,
              "and it is the good copy");
        JsonDocument doc2;
        check(content_index_load(doc2, "story-select") == CONTENT_INDEX_OK,
              "the next ordinary read is a plain OK: the repair also cleared the skip memo");
        check(!content_index_repair_from_backup(), "with no backup left, repair does nothing");
    }

    // ---- a damaged primary must not be re-read on every single load ------------------
    // A quick press makes four or more loads before any sound, the menu seven
    // (independent review, 2026-10-29).
    install_card(full);
    SD.files[kPrimary]->fail_after = full.index_json.size() / 2;       // permanently damaged
    {
        JsonDocument d1;
        check(content_index_load(d1, "story-select") == CONTENT_INDEX_OK_FROM_BACKUP, "first load: primary fails, the previous copy is used");
        const int primary_opens = SD.files[kPrimary]->opens;
        check(primary_opens == CS_INDEX_READ_TRIES, "and the primary was tried exactly the planned number of times");
        Serial.clear();
        const uint32_t t0 = g_host_millis;
        for (int i = 0; i < 7; i++) {
            JsonDocument d;
            check(content_index_load(d, "story-select") == CONTENT_INDEX_OK_FROM_BACKUP, "later load " + std::to_string(i + 1) + ": still served from the previous copy");
        }
        check(SD.files[kPrimary]->opens == primary_opens, "seven more loads never touched the damaged primary");
        check(g_host_millis == t0, "and cost no settling time at all");
        check(!Serial.saw("using the previous copy"), "and did not repeat the log line");
        g_host_millis += CS_INDEX_PRIMARY_SKIP_MS + 1;
        JsonDocument d2;
        check(content_index_load(d2, "story-select") == CONTENT_INDEX_OK_FROM_BACKUP && SD.files[kPrimary]->opens > primary_opens,
              "after the window the primary gets another look");
    }
    {   // the story-picking path itself: a menu's worth of loads costs one retry penalty, not seven
        install_card(full);
        SD.files[kPrimary]->fail_after = full.index_json.size() / 2;
        const uint32_t t0 = g_host_millis;
        for (int i = 0; i < 8; i++) eligible_count();
        check(g_host_millis - t0 == (uint32_t)(CS_INDEX_RETRY_GAP_MS * (CS_INDEX_READ_TRIES - 1)),
              "eight story loads on a damaged primary settle only once", "ms=" + std::to_string(g_host_millis - t0));
    }

    // ---- the shortcut must never hide a recovered primary from content sync ------------
    install_card(full);
    SD.files[kPrimary]->short_reads_left = CS_INDEX_READ_TRIES;        // three bad reads, then healthy again
    {
        JsonDocument d1;
        check(content_index_load(d1, "story-select") == CONTENT_INDEX_OK_FROM_BACKUP, "primary fails three times, previous copy used");
        JsonDocument d2;
        check(content_index_load(d2, "story-select") == CONTENT_INDEX_OK_FROM_BACKUP, "the card is healthy again, but readers keep the shortcut for the window");
        JsonDocument d3;
        check(content_index_load(d3, "content-sync", /*fresh=*/true) == CONTENT_INDEX_OK,
              "content sync reads afresh, finds the primary healthy, and has nothing to repair");
        JsonDocument d4;
        check(content_index_load(d4, "story-select") == CONTENT_INDEX_OK, "and the shortcut is gone for everyone");
    }
    install_card(full);
    SD.files[kPrimary]->short_reads_left = CS_INDEX_READ_TRIES;
    {
        JsonDocument d1;
        content_index_load(d1, "story-select");                          // sets the skip memo
        SD.files[kBackup]->bytes.resize(100);                            // now the previous copy goes bad, the primary is fine
        JsonDocument d2;
        check(content_index_load(d2, "story-select") == CONTENT_INDEX_OK,
              "a shortcut that leads nowhere falls back to the primary instead of giving up");
    }
    install_card(full, /*with_backup=*/false);
    SD.files[kPrimary]->bytes.resize(100);
    {
        JsonDocument d1;
        check(content_index_load(d1, "story-select") == CONTENT_INDEX_UNREADABLE, "total failure sets the dead-card memo");
        SD.files[kPrimary]->bytes = full.index_json;                     // the card recovers
        JsonDocument d2;
        check(content_index_load(d2, "story-select") == CONTENT_INDEX_UNREADABLE, "an ordinary reader inside the window still gets UNREADABLE");
        JsonDocument d3;
        check(content_index_load(d3, "content-sync", /*fresh=*/true) == CONTENT_INDEX_OK,
              "content sync is not held back by another caller's total-failure memo");
    }

    // ---- what the parent chose must survive a bad read ---------------------------
    {
        const Card no_intro = build_card(3, 0, 0, 0, /*intro_enabled=*/false);
        install_card(no_intro);
        check(!story_select_intro_enabled(), "healthy: the parent's 'intro off' is honoured");
        SD.files[kPrimary]->bytes.resize(no_intro.index_json.size() / 2);
        check(!story_select_intro_enabled(), "damaged primary, intact backup: 'intro off' STILL honoured (it used to revert to ON)");
        install_card(no_intro, false);
        SD.files[kPrimary]->bytes.resize(no_intro.index_json.size() / 2);
        check(story_select_intro_enabled(), "nothing readable: the shipped default (ON) applies");
    }

    // ---- content report on a damaged primary --------------------------------------
    install_card(full);
    SD.files[kPrimary]->bytes.resize(full.index_json.size() / 3);
    content_report_refresh();
    check(content_report_available(), "content report also reads through to the backup");

    // ---- SHIP A6: rotation across power cycles --------------------------------------
    for (int stories = 2; stories <= 5; stories++) {
        Card c = build_card(stories, 0, 0, 0); install_card(c); g_nvs.clear();
        std::vector<std::string> seq; bool ok_all = true;
        for (int i = 0; i < 60; i++) {
            story_select_clear_failed();           // each press is a fresh boot: RAM gone, NVS kept
            CsStory el[CS_MAX_STORIES];
            int k = story_select_load_eligible(el, CS_MAX_STORIES);
            char id[CS_MAX_STORY_ID_LEN + 1] = {0};
            if (!story_select_pick(el, k, id, sizeof(id))) { ok_all = false; break; }
            story_select_save_last(id);
            seq.push_back(id);
        }
        int repeats = 0; std::map<std::string, int> cnt;
        for (size_t i = 0; i < seq.size(); i++) { cnt[seq[i]]++; if (i && seq[i] == seq[i - 1]) repeats++; }
        int mn = 1 << 30, mx = 0; for (auto &kv : cnt) { mn = std::min(mn, kv.second); mx = std::max(mx, kv.second); }
        check(ok_all && repeats == 0 && (int)cnt.size() == stories && mx - mn <= 1,
              "A6: " + std::to_string(stories) + " stories, 60 power cycles, no back-to-back repeat, even spread",
              "repeats=" + std::to_string(repeats) + " distinct=" + std::to_string(cnt.size()) + " spread=" + std::to_string(mx - mn));
    }
    {   // a damaged primary must not break the rotation either
        Card c = build_card(3, 0, 0, 0); install_card(c); g_nvs.clear();
        std::vector<std::string> seq;
        for (int i = 0; i < 30; i++) {
            if (i % 5 == 2) SD.files[kPrimary]->short_reads_left = 1;     // a flaky read every few presses
            story_select_clear_failed();
            CsStory el[CS_MAX_STORIES];
            int k = story_select_load_eligible(el, CS_MAX_STORIES);
            char id[CS_MAX_STORY_ID_LEN + 1] = {0};
            if (!story_select_pick(el, k, id, sizeof(id))) break;
            story_select_save_last(id); seq.push_back(id);
        }
        int repeats = 0;
        for (size_t i = 1; i < seq.size(); i++) if (seq[i] == seq[i - 1]) repeats++;
        check(seq.size() == 30 && repeats == 0, "A6: a flaky read every fifth press still gives 30 picks and no repeat", "repeats=" + std::to_string(repeats));
    }
    {   // a story whose file is missing from the card is never chosen
        Card c = build_card(3, 0, 0, 0); install_card(c); g_nvs.clear();
        SD.files.erase(c.stories[1].cache_path);
        std::vector<std::string> seq;
        for (int i = 0; i < 30; i++) {
            story_select_clear_failed();
            CsStory el[CS_MAX_STORIES];
            int k = story_select_load_eligible(el, CS_MAX_STORIES);
            char id[CS_MAX_STORY_ID_LEN + 1] = {0};
            if (!story_select_pick(el, k, id, sizeof(id))) break;
            story_select_save_last(id); seq.push_back(id);
        }
        int repeats = 0; bool used_missing = false;
        for (size_t i = 0; i < seq.size(); i++) { if (i && seq[i] == seq[i - 1]) repeats++; if (seq[i] == c.stories[1].story_id) used_missing = true; }
        check(seq.size() == 30 && repeats == 0 && !used_missing, "A6: a story with its file missing is never chosen, and the other two alternate");
    }
    {   // a story that never started is offered again (the cursor only moves after sound)
        Card c = build_card(3, 0, 0, 0); install_card(c); g_nvs.clear();
        story_select_clear_failed();
        CsStory el[CS_MAX_STORIES];
        int k = story_select_load_eligible(el, CS_MAX_STORIES);
        char a[CS_MAX_STORY_ID_LEN + 1] = {0}, b[CS_MAX_STORY_ID_LEN + 1] = {0};
        story_select_pick(el, k, a, sizeof(a));
        story_select_pick(el, k, b, sizeof(b));
        check(strcmp(a, b) == 0, "A6: a story that never started is offered again");
    }

    printf(failures == 0 ? "PASS\n" : "FAIL (%d)\n", failures);
    return failures == 0 ? 0 : 1;
}
