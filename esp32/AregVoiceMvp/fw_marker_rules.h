#pragma once
// -------------------------------------------------------------
// AregVoiceMvp / fw_marker_rules.h -- PURE logic for the signed in-image
// version marker. No Arduino: host-tested by host_tests/fw_marker_rules_test.cpp.
//
// Every image carries exactly one
//     "AREGFWV1:<version>:<board>:<profile>\0"
// (fw_version_marker.cpp). It sits inside the Secure-Boot-signed body, so it
// binds the version to the signature: a leaked manifest HMAC key alone can
// no longer pair a forged "version 9.9.9" manifest with an older signed
// image (downgrade), because the toy reads the version from the IMAGE.
//
// ota_apply feeds every downloaded chunk to Scanner (the magic or the whole
// marker may straddle a chunk boundary -- the matcher state and the capture
// buffer carry over), then judge() decides:
//   * at least one marker, every copy identical, none malformed;
//   * marker version == manifest version AND newer than the running one;
//   * marker board == this toy's AREG_BOARD_MODEL;
//   * on a RELEASE toy, marker profile == "release".
// Anything else -> "image_marker_mismatch".
// -------------------------------------------------------------
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

namespace fw_marker {

// The magic is deliberately NOT spelled out as a literal in this header: any
// second copy of it in .rodata would be a second "marker" in the image. The
// firmware takes the first kMagicLen bytes of kAregFwMarker itself
// (fw_version_marker.cpp); host tests and tools spell it out on their own.
constexpr size_t kMagicLen = 9;
constexpr size_t kMaxMagic = 16;
constexpr size_t kMaxBody = 95;  // "<ver>:<board>:<profile>" without the magic
constexpr size_t kMaxCopies = 4;

struct Marker {
    char version[24];
    char board[48];
    char profile[16];
};

// Splits "<ver>:<board>:<profile>" into `out`. Fields must be non-empty, the
// version digits/dots only, no further ':'.
inline bool parse_body(const char *body, Marker *out) {
    const char *c1 = strchr(body, ':');
    if (c1 == nullptr) return false;
    const char *c2 = strchr(c1 + 1, ':');
    if (c2 == nullptr || strchr(c2 + 1, ':') != nullptr) return false;
    const size_t lv = (size_t)(c1 - body), lb = (size_t)(c2 - c1 - 1), lp = strlen(c2 + 1);
    if (lv == 0 || lb == 0 || lp == 0) return false;
    if (lv >= sizeof(out->version) || lb >= sizeof(out->board) || lp >= sizeof(out->profile)) return false;
    for (size_t i = 0; i < lv; i++) {
        if (!((body[i] >= '0' && body[i] <= '9') || body[i] == '.')) return false;
    }
    memcpy(out->version, body, lv);
    out->version[lv] = '\0';
    memcpy(out->board, c1 + 1, lb);
    out->board[lb] = '\0';
    memcpy(out->profile, c2 + 1, lp);
    out->profile[lp] = '\0';
    return true;
}

inline bool marker_eq(const Marker &a, const Marker &b) {
    return strcmp(a.version, b.version) == 0 && strcmp(a.board, b.board) == 0 &&
           strcmp(a.profile, b.profile) == 0;
}

class Scanner {
public:
    // `magic` need not be NUL-terminated; only magic_len bytes are used.
    Scanner(const char *magic, size_t magic_len) { reset(magic, magic_len); }

    void reset(const char *magic, size_t magic_len) {
        magic_len_ = magic_len > kMaxMagic ? kMaxMagic : magic_len;
        memcpy(magic_, magic, magic_len_);
        // KMP failure table so an overlapping partial match is not lost.
        fail_[0] = 0;
        for (size_t i = 1, k = 0; i < magic_len_; i++) {
            while (k > 0 && magic_[i] != magic_[k]) k = fail_[k - 1];
            if (magic_[i] == magic_[k]) k++;
            fail_[i] = k;
        }
        matched_ = 0;
        capturing_ = false;
        cap_len_ = 0;
        found_ = 0;
        malformed_ = 0;
        stored_ = 0;
    }

    void feed(const uint8_t *buf, size_t n) {
        for (size_t i = 0; i < n; i++) step(buf[i]);
    }

    // End of stream: a capture still open had no terminator -> malformed.
    void finish() {
        if (capturing_) {
            capturing_ = false;
            malformed_++;
        }
    }

    size_t found() const { return found_; }          // well-formed + malformed
    size_t malformed() const { return malformed_; }
    size_t stored() const { return stored_; }
    const Marker &copy(size_t i) const { return copies_[i]; }

private:
    void step(uint8_t b) {
        if (capturing_) {
            if (b == 0) {
                capturing_ = false;
                cap_[cap_len_] = '\0';
                Marker m;
                if (parse_body(cap_, &m)) {
                    if (stored_ < kMaxCopies) copies_[stored_++] = m;
                    else malformed_++;  // more copies than we track: refuse
                } else {
                    malformed_++;
                }
                return;
            }
            if (b < 0x20 || b > 0x7E || cap_len_ >= kMaxBody) {
                capturing_ = false;
                malformed_++;
                // fall through: this byte may start a new magic
            } else {
                cap_[cap_len_++] = (char)b;
                return;
            }
        }
        while (matched_ > 0 && (char)b != magic_[matched_]) matched_ = fail_[matched_ - 1];
        if ((char)b == magic_[matched_]) matched_++;
        if (matched_ == magic_len_) {
            found_++;
            capturing_ = true;
            cap_len_ = 0;
            matched_ = 0;
        }
    }

    char magic_[kMaxMagic];
    size_t fail_[kMaxMagic];
    size_t magic_len_ = 0;
    size_t matched_ = 0;
    bool capturing_ = false;
    char cap_[kMaxBody + 1];
    size_t cap_len_ = 0;
    size_t found_ = 0;
    size_t malformed_ = 0;
    size_t stored_ = 0;
    Marker copies_[kMaxCopies];
};

// Tolerant MAJOR.MINOR.PATCH compare (mirrors ota_apply.cpp's compare_semver
// and the backend's FirmwareVersionComparer: "v" prefix ok, missing parts 0).
inline int compare_semver(const char *a, const char *b) {
    int pa[3] = {0, 0, 0}, pb[3] = {0, 0, 0};
    if (a != nullptr) {
        if (*a == 'v' || *a == 'V') a++;
        sscanf(a, "%d.%d.%d", &pa[0], &pa[1], &pa[2]);
    }
    if (b != nullptr) {
        if (*b == 'v' || *b == 'V') b++;
        sscanf(b, "%d.%d.%d", &pb[0], &pb[1], &pb[2]);
    }
    for (int i = 0; i < 3; i++) {
        if (pa[i] != pb[i]) return pa[i] < pb[i] ? -1 : 1;
    }
    return 0;
}

enum class Verdict {
    Ok,
    Missing,           // no marker at all (pre-marker image, or not ours)
    Inconsistent,      // malformed copy, or copies that disagree
    VersionMismatch,   // marker version != manifest version
    NotNewer,          // marker version <= running version
    BoardMismatch,
    ProfileMismatch,   // a RELEASE toy offered a non-release image
};

inline const char *verdict_name(Verdict v) {
    switch (v) {
        case Verdict::Ok: return "ok";
        case Verdict::Missing: return "missing";
        case Verdict::Inconsistent: return "inconsistent";
        case Verdict::VersionMismatch: return "version_mismatch";
        case Verdict::NotNewer: return "not_newer";
        case Verdict::BoardMismatch: return "board_mismatch";
        case Verdict::ProfileMismatch: return "profile_mismatch";
    }
    return "unknown";
}

inline Verdict judge(const Scanner &s, const char *manifest_version, const char *running_version,
                     const char *running_board, bool running_is_release, Marker *out = nullptr) {
    if (s.found() == 0) return Verdict::Missing;
    if (s.malformed() != 0 || s.stored() == 0) return Verdict::Inconsistent;
    for (size_t i = 1; i < s.stored(); i++) {
        if (!marker_eq(s.copy(0), s.copy(i))) return Verdict::Inconsistent;
    }
    const Marker &m = s.copy(0);
    if (out != nullptr) *out = m;
    if (strcmp(m.version, manifest_version) != 0) return Verdict::VersionMismatch;
    if (compare_semver(m.version, running_version) <= 0) return Verdict::NotNewer;
    if (strcmp(m.board, running_board) != 0) return Verdict::BoardMismatch;
    if (running_is_release && strcmp(m.profile, "release") != 0) return Verdict::ProfileMismatch;
    return Verdict::Ok;
}

}  // namespace fw_marker
