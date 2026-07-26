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
#ifndef CS_MAX_STORIES
#define CS_MAX_STORIES 8
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
// array (plus a legacy mirror, see content_sync.cpp).
#define CS_INDEX_SCHEMA_VERSION 2

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
