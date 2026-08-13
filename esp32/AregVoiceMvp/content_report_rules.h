#pragma once

// Pure decision logic for the content report — no Arduino, no SD, no JSON,
// no HTTP. Same split content_sync_rules.h uses, and for the same reason:
// the only genuinely error-prone part of the report is the buffer
// arithmetic, and a bug there is not a compile error, it is a toy that
// tells the backend it owns a story called "anban-hu".
//
// Because this header carries no platform dependency, it compiles and runs
// on a host with plain g++, so the arithmetic is tested without hardware.

#include <stddef.h>
#include <stdio.h>
#include <string.h>

/// Longest story id the content-sync allowlist admits. Mirrors
/// CS_MAX_STORY_ID_LEN; redeclared rather than included so this header
/// stays dependency-free and host-compilable.
#ifndef CR_MAX_STORY_ID_LEN
#define CR_MAX_STORY_ID_LEN 48
#endif

/// Appends "<id>:<version>" to `buf`, comma-separated, and updates `used`
/// (the length excluding the terminator). Returns true when the entry was
/// appended.
///
/// Refuses, leaving the buffer untouched and NUL-terminated, when:
///   - the id is null/empty or the version is below 1 (an entry the
///     backend could not match anyway), or
///   - the entry would not fit.
///
/// The refusal-on-overflow is the point. Truncating mid-id would send the
/// backend a story name that matches nothing, and it would be counted as
/// MISSING — a false "your toy is out of date" on a toy that is fine.
/// Dropping the whole entry is also a wrong count, but it is the same
/// wrongness as a story that genuinely has not downloaded yet, which the
/// dashboard already words safely ("keep it on Wi-Fi").
inline bool cr_append_story(char *buf, size_t cap, size_t &used,
                            const char *id, int version) {
    if (buf == nullptr || cap == 0) {
        return false;
    }
    if (id == nullptr || id[0] == '\0' || version < 1) {
        return false;
    }
    const size_t id_len = strnlen(id, CR_MAX_STORY_ID_LEN);
    if (id_len == 0) {
        return false;
    }
    char tail[16];
    const int tail_len = snprintf(tail, sizeof(tail), ":%d", version);
    if (tail_len <= 0) {
        return false;
    }
    const size_t sep = (used > 0) ? 1u : 0u;
    // +1 for the NUL. `used + need` is compared against cap, so the
    // terminator always has a home.
    const size_t need = sep + id_len + (size_t)tail_len + 1u;
    if (used + need > cap) {
        return false;
    }
    if (sep) {
        buf[used++] = ',';
    }
    memcpy(buf + used, id, id_len);
    used += id_len;
    memcpy(buf + used, tail, (size_t)tail_len);
    used += (size_t)tail_len;
    buf[used] = '\0';
    return true;
}
