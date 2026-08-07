// -------------------------------------------------------------
// AregVoiceMvp / content_sync_rules.h — pure decision logic for
// Cloud→SD multi-story sync.
//
// Header-only and dependency-free ON PURPOSE: no Arduino.h, no SD, no
// HTTP, no ArduinoJson. Everything here is a total function over plain
// C strings and integers, so the bench test harness
// (content_sync_test.cpp) can exercise it without a card, a network, or
// a backend — and so the rules that guard an SD path can be reviewed in
// one place.
//
// Nothing here is emitted unless a translation unit calls it, so
// production builds (which include neither content_sync.cpp nor the test
// harness) stay byte-identical.
//
// The IO-dependent half of a sync decision (does the file exist? what
// does it actually measure on the card?) deliberately stays in
// content_sync.cpp — this header only answers the questions that can be
// answered from metadata alone.
// -------------------------------------------------------------
#pragma once

#include <stddef.h>
#include <stdio.h>
#include <string.h>

// ---- bounds -------------------------------------------------------

// Maximum manifest items processed in one sync. Justification: the bench
// card is 7.5 GB and a full story is ~4.6 MB, so 8 stories (~37 MB) is
// far inside the card; the bounded in-memory table below costs ~2.3 KB
// of .bss; and the manifest JSON for 8 items is ~2 KB of heap. The real
// ceiling is wall-clock download time on a one-shot-per-boot sync, not
// storage. A backend offering more is TRUNCATED (never a crash, never a
// partial-item read) — see cs_truncated_count().
// Raised 8 -> 16 on 2026-08-03. The 8 dated from when the backend offered
// exactly ONE story; the first real library (12) silently lost four of them
// to truncation ("12 offered, max 8, 4 ignored") - the toy worked, so nothing
// looked broken, the stories were just quietly absent.
//
// Cost is bounded and was measured, not guessed: three CsStory arrays of this
// size live in RAM, ~443 bytes each, so 8 -> 16 costs ~10 KB against ~245 KB
// free on the S3. SD space is a non-issue: 12 stories are 11.5 MB on an 8 GB
// card. Headroom for the next batch without revisiting this.
#ifndef CS_MAX_STORIES
#define CS_MAX_STORIES 16
#endif

// A story id becomes part of an SD filename, so it is length-bounded
// well below the path budget: "/stories/" (9) + id + "-v" + version
// digits (<= 5) + ".mp3" (4) must fit CS_MAX_PATH_LEN with room to
// spare. Real ids are ~14 chars ("anban-huri", "hedgehog-apple").
#ifndef CS_MAX_STORY_ID_LEN
#define CS_MAX_STORY_ID_LEN 48
#endif

#ifndef CS_MAX_PATH_LEN
#define CS_MAX_PATH_LEN 96
#endif

// Titles are informational (logged, mirrored into the index). Armenian
// UTF-8 runs ~2 bytes/char, so this holds ~30 characters; longer titles
// are truncated on a UTF-8 boundary-agnostic basis because the value is
// never parsed, only displayed.
#ifndef CS_MAX_TITLE_LEN
#define CS_MAX_TITLE_LEN 64
#endif

// Upper bound on a single story download. The largest real asset today
// is 4.65 MB; 32 MB leaves generous headroom while making a nonsense
// sizeBytes (or a hostile one) fail fast before any SD write.
#ifndef CS_MAX_STORY_BYTES
#define CS_MAX_STORY_BYTES (32L * 1024L * 1024L)
#endif

// Bound on a stored audioUrl. The backend emits
// "/api/devices/content-file?storyId=<id>" (~50 chars); 128 leaves room
// for an absolute CDN URL without an unbounded copy.
#ifndef CS_MAX_URL_LEN
#define CS_MAX_URL_LEN 128
#endif

// Bumped when the on-card index shape changes. v1 = the flat
// single-object index written before multi-story; v2 = the stories[]
// array (plus a legacy mirror, see content_sync.cpp); v3 = v2 plus
// per-story clips[] and the root introEnabled flag; v4 = v3 plus the
// device-global voice[] clips and the four parent mode flags. Every bump
// has been a SUPERSET — an older reader ignores the extra fields, a newer
// reader treats their absence as the shipped default ("no clips",
// "intro on", "no voice clips", "every mode enabled"), so no destructive
// migration has ever been needed and a card never has to be wiped.
//
// v5 = v4 plus the per-story seriesId/seriesIndex that let «Ծիվիկ»-style
// episodes play in order. Same superset contract: a v4 card parses as "every
// story is standalone", which is exactly the pre-serial behaviour.
//
// v6 = v5 plus the per-story altOf (alternate endings) and the two root
// parent toggles pausesEnabled / variantsEnabled. Superset again: a v5 card
// parses as "no story is a variant, both features on" — which is exactly the
// pre-variant behaviour, because a toy with no alt files cached plays the
// base narration whatever the toggles say.
//
// v7 = v6 plus the root games[] section — the offline-game clips delivered
// over Wi-Fi instead of by hand-copying the card. Superset again, and this
// bump is the cheapest of the lot: NOTHING reads games[] except the sync
// itself (offline_games.cpp resolves a clip purely by whether the file is
// on the card), so a v6 card parses as "no game clips indexed" and the only
// consequence is that the next sync re-downloads them.
#define CS_INDEX_SCHEMA_VERSION 7

// Per-story clip slots (intro / question / question1 / question2 /
// summary / offer / reoffer / serialnext). Fixed small array — an
// open-ended list would multiply the three CS_MAX_STORIES tables' RAM for
// no product reason.
//
// NOT bumped to 8 when "serialnext" was added, deliberately. There are now
// eight legal kinds but no story wants all eight: a serial EPISODE ships
// intro/offer/reoffer/serialnext (4, or 6 with a summary + question), and a
// standalone story never ships serialnext. A ninth slot would cost ~84 B ×
// CS_MAX_STORIES × 5 tables ≈ 6.7 KB of .bss on a board that also wants
// 40-50 KB free for a TLS handshake during audio. If a story ever DOES
// configure eight kinds, the parse loop keeps the first seven in manifest
// order and drops the eighth with no log — check here first if a clip
// mysteriously never plays.
//
// 5 -> 7 for the welcome flow's spoken offer lines. Cost was computed,
// not guessed: CsClip is ~84 bytes padded, so two extra slots add ~168 B
// per CsStory, and five CsStory[CS_MAX_STORIES] tables exist across the
// image (s_manifest / s_previous / s_active in content_sync.cpp, s_raw in
// story_select.cpp, eligible in the .ino) — about 13.4 KB of .bss.
// VERIFY on the bench against the "[content-sync] heap" and "[alive] heap="
// serial lines before shipping, the way the CS_MAX_STORIES 8 -> 16 bump was.
#ifndef CS_MAX_CLIPS
#define CS_MAX_CLIPS 7
#endif

// Longest allowed kind is "serialnext" (10) — exactly this bound, with no
// slack left. The kind reaches an SD filename, so it is allowlisted
// (below), not merely length-bounded. This bound is why the welcome flow's
// second offer line is "reoffer" and not "offer-again" (11) — the backend
// pins the same limit in
// ContentSyncVoiceTests.AllowedClipKinds_AllFitTheFirmwareKindLength.
#define CS_CLIP_KIND_LEN 10

// ---- one clip, as held in memory ----------------------------------

/// Compact on purpose: no URL (the download URL is CONSTRUCTED as
/// /api/devices/content-file?storyId=<id>&clip=<kind>, matching the
/// backend's default fill — a config that points clips at an external
/// CDN is not supported on-device in this slice) and no separate cache
/// path (derived via cs_build_clip_cache_path from the owning story).
struct CsClip {
    char kind[CS_CLIP_KIND_LEN + 1];
    char sha256[65];
    long size_bytes;
    bool verified;   // a full SHA-256 has matched for THIS clip file
};

// Slice E — bedtime-music slots. Small fixed table; a track must never
// enter the story rotation, so music lives in its own namespace, its own
// SD directory (/music) and its own index section.
#ifndef CS_MAX_MUSIC
#define CS_MAX_MUSIC 8
#endif

struct CsMusic {
    char track_id[CS_MAX_STORY_ID_LEN + 1];  // same allowlist as story ids
    int  version;
    char title[CS_MAX_TITLE_LEN + 1];
    char sha256[65];
    long size_bytes;
    bool verified;
};

// Welcome-flow — device-global spoken clips: the power-on greetings, the
// "what shall we do?" prompts, and the two fallback lines. A THIRD
// namespace beside stories and music, with its own SD directory (/voice)
// and its own index section, for the same reason music got one: these are
// not stories and must never enter the story rotation.
//
// 48 slots holds the launch set — 39 greetings + ask-sgrc + ask-any +
// say-again + just-story = 43 — with headroom. The manifest may legally
// offer more; the existing truncate-and-report path handles it, the same
// way an over-long story manifest is handled.
//
// Every slot costs ~384 bytes of .bss (CsVoice is ~128 bytes padded and
// three tables live in content_sync.cpp: manifest / previous / active).
// MEASURED, not estimated, on the canonical FQBN:
//
//     32 slots -> 157,680 B free      48 slots -> 149,232 B free
//     64 slots -> 140,784 B free
//
// The first draft of this slice used 48 slots AND a table per function
// and landed at 110,512 B free — too little on a board that also wants
// ~40-50 KB for a TLS handshake during audio. The bound was not the
// problem; the duplicated tables were. With those shared, 48 is
// comfortable. Same budget still kills a title field: a device-global
// clip has no display surface anywhere on the toy.
//
// Do NOT pad the greeting pool just because slots are free. A child
// notices two greetings that say the same thing sooner than a missing
// one, so near-duplicates make the rotation feel SMALLER.
//
// Only two "ask" variants ship rather than all 15 combinations of the
// four mode flags: a parent disabling Story on a storytelling toy is
// rare, and the other three modes have no offline content yet, so the
// missing variants fall back to the generic ask-any.
#ifndef CS_MAX_VOICE
#define CS_MAX_VOICE 48
#endif

struct CsVoice {
    char voice_id[CS_MAX_STORY_ID_LEN + 1];  // same allowlist as story ids
    int  version;
    char sha256[65];
    long size_bytes;
    bool verified;
};

// Offline games — the pre-rendered clips the button games play from the
// card. A FOURTH namespace beside stories, music and voice, and the only
// one addressed by a PAIR: the game key AND the clip id. Four of the five
// games in backend/content/offline-games/game-clips.json each define a clip
// called `intro`, so the game key is what keeps them apart — on the wire and
// as an SD subdirectory.
//
// THERE IS NO CsGame TABLE, and that is the whole design. ~90 clips ship
// today; three CsGame[90] tables in the shape sync_voice() uses would cost
// roughly 47 KB of .bss on a board that must keep 40-50 KB free for a TLS
// handshake during audio — more than the welcome-flow slice's first draft
// spent, and that draft had to be rewritten. So the games pass STREAMS:
// content_sync.cpp holds one CsGame at a time and appends each result
// straight into the output index array. This struct exists to give that one
// item bounded, NUL-terminated storage (and, because its members are char
// ARRAYS rather than const char*, ArduinoJson copies them into the index
// document instead of linking to memory that is about to go away).
//
// The cap below bounds the INDEX, not RAM: a hostile or runaway manifest
// must not grow the output document without limit. 160 leaves nearly 2x
// headroom over the shipped 90.
#ifndef CS_MAX_GAMES
#define CS_MAX_GAMES 160
#endif

/// The games root. MUST stay equal to AREG_GAMES_CLIP_DIR in
/// offline_games.cpp — that module resolves
/// "<root>/<game-key>/<clip-id>.mp3" and this sync's whole purpose is to
/// put files exactly where it already looks. Deliberately NOT #included
/// from there: offline_games.cpp is bench-gated behind a different flag, so
/// the two are kept in step by the round-trip assertion in
/// content_sync_test.cpp instead.
#define CS_GAMES_DIR "/games"

struct CsGame {
    char game_key[CS_MAX_STORY_ID_LEN + 1];  // same allowlist as story ids
    char clip_id[CS_MAX_STORY_ID_LEN + 1];   // same allowlist as story ids
    int  version;
    char sha256[65];
    long size_bytes;
    bool verified;
};

/// The greeting pool is identified by ID PREFIX, not by a manifest field:
/// "greet-01", "greet-02", … So adding greeting #25 is a backend config
/// edit with no firmware change, which is the whole point of putting the
/// role in the id.
inline bool cs_voice_is_greeting(const char *voice_id) {
    return voice_id != NULL && strncmp(voice_id, "greet-", 6) == 0;
}

// Exact-id voice clips the welcome flow looks up by name. Everything
// except the greeting pool is an exact match.
#define CS_VOICE_ID_ASK_ANY    "ask-any"
#define CS_VOICE_ID_SAY_AGAIN  "say-again"
#define CS_VOICE_ID_JUST_STORY "just-story"

/// Builds the "what shall we do?" clip id for a set of enabled modes:
/// "ask-" plus the enabled letters in the FIXED order s,g,r,c — so
/// story+riddle is always "ask-sr", never "ask-rs", and the backend only
/// has to ship 15 recordings instead of every permutation.
///
/// Returns false when no mode is enabled (there is nothing to ask about,
/// and the caller must not fall back to a generic prompt that would offer
/// something the parent switched off) or when `out` is too small.
inline bool cs_build_ask_voice_id(char *out, size_t out_len,
                                  bool story, bool game,
                                  bool riddle, bool curiosity) {
    if (out == NULL || out_len == 0) {
        return false;
    }
    char scratch[16];
    size_t n = 0;
    scratch[n++] = 'a';
    scratch[n++] = 's';
    scratch[n++] = 'k';
    scratch[n++] = '-';
    const size_t prefix = n;
    if (story)     scratch[n++] = 's';
    if (game)      scratch[n++] = 'g';
    if (riddle)    scratch[n++] = 'r';
    if (curiosity) scratch[n++] = 'c';
    if (n == prefix) {
        return false;   // nothing enabled — no honest prompt exists
    }
    scratch[n] = '\0';
    if (n + 1 > out_len) {
        return false;
    }
    memcpy(out, scratch, n + 1);
    return true;
}

// ---- one story, as held in memory ---------------------------------

struct CsStory {
    char story_id[CS_MAX_STORY_ID_LEN + 1];
    int  version;
    char title[CS_MAX_TITLE_LEN + 1];
    char sha256[65];
    long size_bytes;
    char cache_path[CS_MAX_PATH_LEN];
    /// Used EXACTLY as the backend supplied it (including any ?storyId=
    /// query string). Only meaningful for manifest entries; index entries
    /// leave it empty.
    char audio_url[CS_MAX_URL_LEN];
    bool verified;   // a full SHA-256 has matched for THIS file at some point

    /// B2 — per-story clips (intro / question / summary). clip_count is
    /// 0 for stories that ship none, which is also what every pre-B2
    /// index yields on parse — the story then plays without intro or
    /// reflection, exactly the pre-B2 behavior.
    CsClip clips[CS_MAX_CLIPS];
    int    clip_count;

    /// Serial support — the series this story is an EPISODE of, and its
    /// 1-based position in it. EMPTY id + 0 index means a standalone
    /// story, which is what every pre-v5 index and every non-serial
    /// manifest item yields on parse.
    ///
    /// Both-or-neither, enforced at parse: an id without a positive index
    /// (or the reverse) is stored as "standalone", never half-set. The
    /// selector's ordering rule reads both, and a half-set pair would make
    /// it order episodes by a position nobody supplied.
    ///
    /// Costs 49 + 4 bytes (56 padded) per CsStory, across every
    /// CsStory[CS_MAX_STORIES] table in the image. MEASURED at the
    /// canonical FQBN rather than estimated, the way the CS_MAX_STORIES and
    /// CS_MAX_CLIPS bumps were:
    ///
    ///     production build   231,728 -> 229,144 B free   (-2,584)
    ///     cs+sel test bench  185,624 -> 176,592 B free   (-9,032)
    ///
    /// The production figure is the one that matters — it is the build a
    /// child's toy runs, and it still leaves far more than the ~40-50 KB a
    /// TLS handshake wants during audio.
    char series_id[CS_MAX_STORY_ID_LEN + 1];
    int  series_index;

    /// Variant endings — the story this entry is an ALTERNATE ENDING of.
    /// EMPTY means an ordinary story, which is what every pre-v6 index and
    /// every non-variant manifest item yields on parse.
    ///
    /// A non-empty alt_of changes what the entry IS, not just what it says
    /// about itself: the entry is excluded from the rotation, never offered
    /// by name in the welcome flow, and never marked heard. It is reachable
    /// only through story_select_resolve_playback_path, which looks a variant
    /// up BY the base story it is already about to play. Each variant is a
    /// FULL alternate file (base narration cut at the branch point plus the
    /// new ending, assembled offline), so playback is "open this file instead"
    /// — the device never splices audio.
    ///
    /// Validated by the same allowlist a story id is, and refused when it
    /// names the entry itself. The backend applies the identical rule before
    /// it reaches the wire, but a card can be hand-edited, so it is re-applied
    /// on parse.
    ///
    /// Costs 49 bytes (52 padded) per CsStory across every
    /// CsStory[CS_MAX_STORIES] table in the image. MEASURED at the canonical
    /// FQBN rather than estimated, the way the CS_MAX_STORIES, CS_MAX_CLIPS
    /// and series bumps were — see the README's size table.
    char alt_of[CS_MAX_STORY_ID_LEN + 1];
};

// ---- validation ---------------------------------------------------

/// Strict allowlist: lowercase ASCII letters, digits, '-' and '_'.
/// Everything else is rejected, which is what makes it safe to splice
/// into an SD path: '.', '/', '\\', ':', spaces and control characters
/// can never appear, so "..", absolute paths and traversal segments are
/// unrepresentable rather than merely filtered.
///
/// Uppercase is rejected too. The backend emits kebab-case ids and
/// treats duplicates case-insensitively; accepting mixed case here would
/// let "Anban-Huri" and "anban-huri" become two different FILENAMES on a
/// case-preserving card while the backend considered them one story.
inline bool cs_is_valid_story_id(const char *id) {
    if (id == NULL || id[0] == '\0') {
        return false;
    }
    size_t n = 0;
    for (const char *p = id; *p != '\0'; ++p, ++n) {
        if (n >= CS_MAX_STORY_ID_LEN) {
            return false;
        }
        const char c = *p;
        const bool ok = (c >= 'a' && c <= 'z')
                     || (c >= '0' && c <= '9')
                     || c == '-' || c == '_';
        if (!ok) {
            return false;
        }
    }
    return n > 0;
}

/// Serial support — true when this entry is an EPISODE of a series, i.e.
/// BOTH halves are present and well-formed. Everything downstream asks
/// this rather than testing the two fields separately, so "half-set" has
/// exactly one meaning, in one place. A series id is validated by the same
/// allowlist a story id is: it is compared as an id, and the backend
/// (ContentManifestService) applies the identical rule before it ever
/// reaches the wire.
inline bool cs_story_is_serial(const CsStory *s) {
    return s != NULL && s->series_index >= 1 && cs_is_valid_story_id(s->series_id);
}

/// Variant endings — true when this entry is an ALTERNATE ENDING of another
/// story rather than a story in its own right. Everything downstream asks
/// this rather than testing alt_of directly, so "is a variant" has exactly
/// one meaning, in one place.
///
/// This is the guard that keeps the rotation honest. A variant file starts
/// PART-WAY through its story; if one ever reached story_select_pick, a child
/// would be told half a story as though it were the whole thing. Every place
/// that builds a playable list filters on it.
inline bool cs_story_is_variant(const CsStory *s) {
    return s != NULL && cs_is_valid_story_id(s->alt_of);
}

/// Exactly 64 hex characters. A truncated or non-hex hash would make
/// every download "fail" on the device with no way to tell config rot
/// from a bad transfer, so it is rejected at parse time instead.
inline bool cs_is_sha256_hex(const char *sha) {
    if (sha == NULL) {
        return false;
    }
    size_t n = 0;
    for (const char *p = sha; *p != '\0'; ++p, ++n) {
        if (n >= 64) {
            return false;   // too long
        }
        const char c = *p;
        const bool hex = (c >= '0' && c <= '9')
                      || (c >= 'a' && c <= 'f')
                      || (c >= 'A' && c <= 'F');
        if (!hex) {
            return false;
        }
    }
    return n == 64;
}

/// Positive and inside the per-story ceiling.
inline bool cs_is_valid_size(long size_bytes) {
    return size_bytes > 0 && size_bytes <= CS_MAX_STORY_BYTES;
}

/// Version numbers reach a filename, so they are clamped to a sane
/// positive range rather than trusted. Mirrors the backend, which
/// clamps a non-positive version to 1.
inline int cs_normalize_version(int version) {
    if (version < 1) {
        return 1;
    }
    if (version > 99999) {
        return 99999;
    }
    return version;
}

/// Case-insensitive, matching the backend's duplicate rule
/// (ContentManifestService uses OrdinalIgnoreCase). Ids that reach here
/// have already passed cs_is_valid_story_id, so this is only ever
/// comparing lowercase input; the fold exists so an index written by an
/// older/looser build still compares correctly.
inline bool cs_story_ids_equal(const char *a, const char *b) {
    if (a == NULL || b == NULL) {
        return false;
    }
    size_t i = 0;
    for (;; ++i) {
        char ca = a[i], cb = b[i];
        if (ca >= 'A' && ca <= 'Z') ca = (char)(ca - 'A' + 'a');
        if (cb >= 'A' && cb <= 'Z') cb = (char)(cb - 'A' + 'a');
        if (ca != cb) {
            return false;
        }
        if (ca == '\0') {
            return true;
        }
        if (i > CS_MAX_STORY_ID_LEN) {
            return false;   // unterminated / absurd input
        }
    }
}

/// Offline games — true when a held clip is the one named by this
/// (gameKey, clipId) pair. Everything downstream asks this rather than
/// comparing the two fields separately, so "same clip" has one meaning in
/// one place. Case-insensitive for the same reason cs_story_ids_equal is.
inline bool cs_game_pairs_equal(const CsGame *g,
                                const char *game_key, const char *clip_id) {
    return g != NULL
        && cs_story_ids_equal(g->game_key, game_key)
        && cs_story_ids_equal(g->clip_id, clip_id);
}

// ---- path construction --------------------------------------------

/// "/stories/<storyId>-v<version>.mp3".
///
/// The directory is deliberately the EXISTING /stories convention that
/// content_sync has always written to, not a new /content/stories tree:
/// moving it would orphan the anban-huri-v1.mp3 already cached on the
/// bench card and break the hardware-verified SD-first playback path.
///
/// Returns false (and leaves `out` untouched) when the id is invalid or
/// the result would not fit, so a caller can never silently truncate a
/// path into a different file.
inline bool cs_build_cache_path(char *out, size_t out_len,
                                const char *story_id, int version) {
    if (out == NULL || out_len == 0 || !cs_is_valid_story_id(story_id)) {
        return false;
    }
    const int v = cs_normalize_version(version);
    char scratch[CS_MAX_PATH_LEN * 2];
    const int written = snprintf(scratch, sizeof(scratch),
                                 "/stories/%s-v%d.mp3", story_id, v);
    if (written <= 0 || (size_t)written >= out_len || (size_t)written >= sizeof(scratch)) {
        return false;
    }
    memcpy(out, scratch, (size_t)written + 1);
    return true;
}

/// Bounded clip-kind allowlist: exactly "intro", "question",
/// "question1", "question2", "summary", "offer", "reoffer" or
/// "serialnext" ("question" = dialogue question index 0; question1/2 are
/// the reflection-dialogue follow-ups; offer/reoffer are the welcome
/// flow's spoken story offers — "Ուզո՞ւմ ես լսել X" and "Մենք արդեն լսել
/// ենք X…" — which are what let the toy say a story's TITLE aloud with no
/// runtime TTS; serialnext is the "Շարունակությունը՝ վաղը" line a serial
/// EPISODE closes on). The kind reaches an SD filename, so like story ids
/// it is allowlisted — traversal characters are unrepresentable, not
/// filtered.
inline bool cs_is_valid_clip_kind(const char *kind) {
    if (kind == NULL) {
        return false;
    }
    return strcmp(kind, "intro") == 0
        || strcmp(kind, "question") == 0
        || strcmp(kind, "question1") == 0
        || strcmp(kind, "question2") == 0
        || strcmp(kind, "summary") == 0
        || strcmp(kind, "offer") == 0
        || strcmp(kind, "reoffer") == 0
        || strcmp(kind, "serialnext") == 0;
}

/// Welcome-flow clip kinds, as constants so a caller never open-codes the
/// string (a typo would silently resolve to "no clip" and the toy would
/// simply skip the offer, which is the hardest kind of bug to see).
#define CS_CLIP_KIND_OFFER   "offer"
#define CS_CLIP_KIND_REOFFER "reoffer"

/// Serial support — the closing line an EPISODE ends on
/// («Շարունակությունը՝ վաղը»). Exactly CS_CLIP_KIND_LEN characters, so a
/// longer name here would need the bound raised in lockstep on both sides.
#define CS_CLIP_KIND_SERIALNEXT "serialnext"

/// Clip kind for dialogue question `index` (0 → "question",
/// 1 → "question1", 2 → "question2"); NULL for any other index.
inline const char *cs_question_clip_kind(int index) {
    switch (index) {
        case 0:  return "question";
        case 1:  return "question1";
        case 2:  return "question2";
        default: return NULL;
    }
}

/// "/stories/<storyId>-v<version>-<kind>.mp3". Same refuse-on-truncation
/// contract as cs_build_cache_path.
inline bool cs_build_clip_cache_path(char *out, size_t out_len,
                                     const char *story_id, int version,
                                     const char *kind) {
    if (out == NULL || out_len == 0
        || !cs_is_valid_story_id(story_id) || !cs_is_valid_clip_kind(kind)) {
        return false;
    }
    const int v = cs_normalize_version(version);
    char scratch[CS_MAX_PATH_LEN * 2];
    const int written = snprintf(scratch, sizeof(scratch),
                                 "/stories/%s-v%d-%s.mp3", story_id, v, kind);
    if (written <= 0 || (size_t)written >= out_len || (size_t)written >= sizeof(scratch)) {
        return false;
    }
    memcpy(out, scratch, (size_t)written + 1);
    return true;
}

/// "/tmp/<storyId>-v<version>-<kind>.mp3.part".
inline bool cs_build_clip_temp_path(char *out, size_t out_len,
                                    const char *story_id, int version,
                                    const char *kind) {
    if (out == NULL || out_len == 0
        || !cs_is_valid_story_id(story_id) || !cs_is_valid_clip_kind(kind)) {
        return false;
    }
    const int v = cs_normalize_version(version);
    char scratch[CS_MAX_PATH_LEN * 2];
    const int written = snprintf(scratch, sizeof(scratch),
                                 "/tmp/%s-v%d-%s.mp3.part", story_id, v, kind);
    if (written <= 0 || (size_t)written >= out_len || (size_t)written >= sizeof(scratch)) {
        return false;
    }
    memcpy(out, scratch, (size_t)written + 1);
    return true;
}

/// "/tmp/<storyId>-v<version>.mp3.part" — unique PER STORY AND VERSION
/// so two stories syncing in the same run (or a version bump) can never
/// share a partial file.
inline bool cs_build_temp_path(char *out, size_t out_len,
                               const char *story_id, int version) {
    if (out == NULL || out_len == 0 || !cs_is_valid_story_id(story_id)) {
        return false;
    }
    const int v = cs_normalize_version(version);
    char scratch[CS_MAX_PATH_LEN * 2];
    const int written = snprintf(scratch, sizeof(scratch),
                                 "/tmp/%s-v%d.mp3.part", story_id, v);
    if (written <= 0 || (size_t)written >= out_len || (size_t)written >= sizeof(scratch)) {
        return false;
    }
    memcpy(out, scratch, (size_t)written + 1);
    return true;
}

/// "/music/<trackId>-v<version>.mp3" — the music namespace's analogue of
/// cs_build_cache_path, same refuse-on-truncation contract.
inline bool cs_build_music_cache_path(char *out, size_t out_len,
                                      const char *track_id, int version) {
    if (out == NULL || out_len == 0 || !cs_is_valid_story_id(track_id)) {
        return false;
    }
    const int v = cs_normalize_version(version);
    char scratch[CS_MAX_PATH_LEN * 2];
    const int written = snprintf(scratch, sizeof(scratch),
                                 "/music/%s-v%d.mp3", track_id, v);
    if (written <= 0 || (size_t)written >= out_len || (size_t)written >= sizeof(scratch)) {
        return false;
    }
    memcpy(out, scratch, (size_t)written + 1);
    return true;
}

/// "/voice/<voiceId>-v<version>.mp3" — the welcome-flow namespace's
/// analogue of cs_build_cache_path, same refuse-on-truncation contract.
inline bool cs_build_voice_cache_path(char *out, size_t out_len,
                                      const char *voice_id, int version) {
    if (out == NULL || out_len == 0 || !cs_is_valid_story_id(voice_id)) {
        return false;
    }
    const int v = cs_normalize_version(version);
    char scratch[CS_MAX_PATH_LEN * 2];
    const int written = snprintf(scratch, sizeof(scratch),
                                 "/voice/%s-v%d.mp3", voice_id, v);
    if (written <= 0 || (size_t)written >= out_len || (size_t)written >= sizeof(scratch)) {
        return false;
    }
    memcpy(out, scratch, (size_t)written + 1);
    return true;
}

/// "/tmp/v-<voiceId>-v<version>.mp3.part" ("v-" keeps a voice part file
/// from colliding with a story's or a music track's).
inline bool cs_build_voice_temp_path(char *out, size_t out_len,
                                     const char *voice_id, int version) {
    if (out == NULL || out_len == 0 || !cs_is_valid_story_id(voice_id)) {
        return false;
    }
    const int v = cs_normalize_version(version);
    char scratch[CS_MAX_PATH_LEN * 2];
    const int written = snprintf(scratch, sizeof(scratch),
                                 "/tmp/v-%s-v%d.mp3.part", voice_id, v);
    if (written <= 0 || (size_t)written >= out_len || (size_t)written >= sizeof(scratch)) {
        return false;
    }
    memcpy(out, scratch, (size_t)written + 1);
    return true;
}

/// "/games/<gameKey>" — the per-game subdirectory, which must exist before
/// a clip can be renamed into it.
inline bool cs_build_game_dir_path(char *out, size_t out_len,
                                   const char *game_key) {
    if (out == NULL || out_len == 0 || !cs_is_valid_story_id(game_key)) {
        return false;
    }
    char scratch[CS_MAX_PATH_LEN * 2];
    const int written = snprintf(scratch, sizeof(scratch),
                                 CS_GAMES_DIR "/%s", game_key);
    if (written <= 0 || (size_t)written >= out_len || (size_t)written >= sizeof(scratch)) {
        return false;
    }
    memcpy(out, scratch, (size_t)written + 1);
    return true;
}

/// "/games/<gameKey>/<clipId>.mp3" — the EXACT path offline_games.cpp
/// already builds (AREG_GAMES_CLIP_DIR "/%s/%s.mp3"). Same
/// refuse-on-truncation contract as cs_build_cache_path.
///
/// NOTE the deliberate absence of "-v<version>". Stories, music and voice
/// all embed their version in the filename; game clips must NOT, because
/// the games module resolves a clip from its key and id alone and a
/// versioned name would simply never be found. The version lives on the
/// wire and in the index instead, which is what still makes a re-render
/// re-download: the index entry stops matching, so the file is fetched and
/// overwritten in place.
inline bool cs_build_game_cache_path(char *out, size_t out_len,
                                     const char *game_key, const char *clip_id) {
    if (out == NULL || out_len == 0
        || !cs_is_valid_story_id(game_key) || !cs_is_valid_story_id(clip_id)) {
        return false;
    }
    char scratch[CS_MAX_PATH_LEN * 2];
    const int written = snprintf(scratch, sizeof(scratch),
                                 CS_GAMES_DIR "/%s/%s.mp3", game_key, clip_id);
    if (written <= 0 || (size_t)written >= out_len || (size_t)written >= sizeof(scratch)) {
        return false;
    }
    memcpy(out, scratch, (size_t)written + 1);
    return true;
}

/// "/tmp/g-<gameKey>-<clipId>.mp3.part" ("g-" keeps a game part file from
/// colliding with a story's, a music track's or a voice clip's).
inline bool cs_build_game_temp_path(char *out, size_t out_len,
                                    const char *game_key, const char *clip_id) {
    if (out == NULL || out_len == 0
        || !cs_is_valid_story_id(game_key) || !cs_is_valid_story_id(clip_id)) {
        return false;
    }
    char scratch[CS_MAX_PATH_LEN * 2];
    const int written = snprintf(scratch, sizeof(scratch),
                                 "/tmp/g-%s-%s.mp3.part", game_key, clip_id);
    if (written <= 0 || (size_t)written >= out_len || (size_t)written >= sizeof(scratch)) {
        return false;
    }
    memcpy(out, scratch, (size_t)written + 1);
    return true;
}

/// "/tmp/m-<trackId>-v<version>.mp3.part" ("m-" keeps a music part file
/// from ever colliding with a story's).
inline bool cs_build_music_temp_path(char *out, size_t out_len,
                                     const char *track_id, int version) {
    if (out == NULL || out_len == 0 || !cs_is_valid_story_id(track_id)) {
        return false;
    }
    const int v = cs_normalize_version(version);
    char scratch[CS_MAX_PATH_LEN * 2];
    const int written = snprintf(scratch, sizeof(scratch),
                                 "/tmp/m-%s-v%d.mp3.part", track_id, v);
    if (written <= 0 || (size_t)written >= out_len || (size_t)written >= sizeof(scratch)) {
        return false;
    }
    memcpy(out, scratch, (size_t)written + 1);
    return true;
}

// ---- sync decisions ------------------------------------------------

/// How many manifest items will actually be processed, and whether the
/// backend offered more than we accept. Truncation is reported, never
/// fatal.
inline int cs_accepted_count(int manifest_count) {
    if (manifest_count < 0) {
        return 0;
    }
    return manifest_count > CS_MAX_STORIES ? CS_MAX_STORIES : manifest_count;
}

inline int cs_truncated_count(int manifest_count) {
    return manifest_count > CS_MAX_STORIES ? manifest_count - CS_MAX_STORIES : 0;
}

/// Metadata half of the "already current" decision: does this index
/// entry describe exactly the story the manifest is offering, and was it
/// verified when it was written?
///
/// The IO half (file exists, actual on-card size matches) is the
/// caller's job — index metadata alone must never be enough to skip a
/// download, because the file can vanish independently of the index.
inline bool cs_entry_matches_manifest(const CsStory *entry,
                                      const char *story_id, int version,
                                      const char *sha256, long size_bytes) {
    if (entry == NULL || !entry->verified) {
        return false;
    }
    if (!cs_story_ids_equal(entry->story_id, story_id)) {
        return false;
    }
    if (entry->version != cs_normalize_version(version)) {
        return false;
    }
    if (entry->size_bytes != size_bytes) {
        return false;
    }
    // Hash comparison is case-insensitive: the backend lowercases on the
    // wire, but an index written by an older build may not have.
    if (sha256 == NULL) {
        return false;
    }
    size_t i = 0;
    for (;; ++i) {
        char ca = entry->sha256[i], cb = sha256[i];
        if (ca >= 'A' && ca <= 'Z') ca = (char)(ca - 'A' + 'a');
        if (cb >= 'A' && cb <= 'Z') cb = (char)(cb - 'A' + 'a');
        if (ca != cb) {
            return false;
        }
        if (ca == '\0') {
            return true;
        }
        if (i >= 64) {
            return false;
        }
    }
}

/// Bounded copy that always NUL-terminates. Returns false when the
/// source had to be truncated, so callers can reject rather than store a
/// silently shortened id/hash (truncation is only tolerated for titles).
inline bool cs_copy_bounded(char *dst, size_t dst_len, const char *src) {
    if (dst == NULL || dst_len == 0) {
        return false;
    }
    if (src == NULL) {
        dst[0] = '\0';
        return false;
    }
    const size_t n = strlen(src);
    if (n >= dst_len) {
        memcpy(dst, src, dst_len - 1);
        dst[dst_len - 1] = '\0';
        return false;
    }
    memcpy(dst, src, n + 1);
    return true;
}
