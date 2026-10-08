// Host test for fw_marker_rules.h (signed in-image version marker,
// 2026-10-08). Plain g++:
//
//     g++ -std=c++17 -Wall -Wextra -I.. -o /tmp/fwm_test fw_marker_rules_test.cpp && /tmp/fwm_test
//
// Compiles the REAL header. Covers the stream scanner (magic and marker split
// across chunks at every possible offset, duplicates, malformed copies,
// overlapping partial matches) and judge() (version / downgrade / board /
// profile). NOT covered: ota_apply.cpp's wiring (needs a toy).
#include "../fw_marker_rules.h"
#include <cstdio>
#include <cstring>
#include <string>

static int failures = 0;
static void check(bool ok, const char *what) {
    printf("  %-4s %s\n", ok ? "ok" : "FAIL", what);
    if (!ok) failures++;
}

static const char kMagic[] = "AREGFWV1:";  // spelled out here only; never in firmware headers

static std::string image_with(const std::string &marker_body, const std::string &before = std::string(5000, '\x01'),
                              const std::string &after = std::string(3000, '\x02')) {
    std::string s = before;
    s += kMagic;
    s += marker_body;
    s.push_back('\0');
    s += after;
    return s;
}

static fw_marker::Scanner scan(const std::string &img, size_t chunk) {
    fw_marker::Scanner sc(kMagic, fw_marker::kMagicLen);
    for (size_t i = 0; i < img.size(); i += chunk) {
        const size_t n = std::min(chunk, img.size() - i);
        sc.feed(reinterpret_cast<const uint8_t *>(img.data() + i), n);
    }
    sc.finish();
    return sc;
}

int main() {
    using namespace fw_marker;
    check(strlen(kMagic) == kMagicLen, "magic length constant matches the spelled-out magic");

    const std::string img = image_with("1.4.0:areg-s3-n8-sb:release");
    // Every chunk size from 1 to 40 puts the chunk boundary at every offset
    // inside the magic and the body at least once.
    bool all_ok = true;
    for (size_t chunk = 1; chunk <= 40; chunk++) {
        Scanner sc = scan(img, chunk);
        Marker m;
        all_ok &= judge(sc, "1.4.0", "1.3.4", "areg-s3-n8-sb", true, &m) == Verdict::Ok;
        all_ok &= strcmp(m.board, "areg-s3-n8-sb") == 0 && strcmp(m.profile, "release") == 0;
    }
    check(all_ok, "marker found and parsed for every chunk size 1..40 (split magic/body)");
    {
        Scanner sc = scan(img, 4096);
        check(sc.found() == 1 && sc.stored() == 1 && sc.malformed() == 0, "exactly one marker in a 4096-chunk scan");
    }

    // Overlapping partial match: "AREGAREGFWV1:" must still be found.
    {
        std::string s = std::string("xxAREG") + image_with("2.0.0:b:dev", "", "");
        Scanner sc = scan(s, 3);
        check(sc.found() == 1 && sc.stored() == 1, "a partial magic right before the real one does not hide it (KMP)");
    }

    // Duplicates
    {
        std::string two = image_with("1.4.0:areg-s3-n8-sb:release") + image_with("1.4.0:areg-s3-n8-sb:release");
        check(judge(scan(two, 777), "1.4.0", "1.3.4", "areg-s3-n8-sb", true) == Verdict::Ok,
              "two IDENTICAL copies are accepted");
        std::string diff = image_with("1.4.0:areg-s3-n8-sb:release") + image_with("1.5.0:areg-s3-n8-sb:release");
        check(judge(scan(diff, 777), "1.4.0", "1.3.4", "areg-s3-n8-sb", true) == Verdict::Inconsistent,
              "two DIFFERENT copies are refused");
        std::string bare = image_with("1.4.0:areg-s3-n8-sb:release");
        bare += kMagic;  // a stray magic followed by garbage
        bare += "\x01\x02";
        check(judge(scan(bare, 64), "1.4.0", "1.3.4", "areg-s3-n8-sb", true) == Verdict::Inconsistent,
              "a stray, malformed magic makes the image inconsistent (fail closed)");
    }

    // Missing / malformed
    check(judge(scan(std::string(9000, '\x03'), 512), "1.4.0", "1.3.4", "areg-s3-n8-sb", true) == Verdict::Missing,
          "no marker at all -> missing (a pre-marker image)");
    check(judge(scan(image_with("1.4.0:areg-s3-n8-sb"), 100), "1.4.0", "1.3.4", "areg-s3-n8-sb", false) == Verdict::Inconsistent,
          "two fields only -> malformed");
    check(judge(scan(image_with("1.4.0:a:b:c"), 100), "1.4.0", "1.3.4", "a", false) == Verdict::Inconsistent,
          "four fields -> malformed");
    check(judge(scan(image_with("1.4.x:areg-s3-n8-sb:release"), 100), "1.4.x", "1.3.4", "areg-s3-n8-sb", true) == Verdict::Inconsistent,
          "non-numeric version -> malformed");
    check(judge(scan(image_with(std::string(120, 'A')), 100), "1.4.0", "1.3.4", "x", false) == Verdict::Inconsistent,
          "an over-long body -> malformed (bounded capture)");
    {
        std::string unterminated = std::string(10, '\x01') + kMagic + "1.4.0:areg-s3-n8-sb:release";
        check(judge(scan(unterminated, 7), "1.4.0", "1.3.4", "areg-s3-n8-sb", true) == Verdict::Inconsistent,
              "a marker cut off by the end of the stream -> malformed");
    }

    // judge(): version, downgrade, board, profile
    const std::string rel = image_with("1.4.0:areg-s3-n8-sb:release");
    check(judge(scan(rel, 256), "1.4.1", "1.3.4", "areg-s3-n8-sb", true) == Verdict::VersionMismatch,
          "marker version != manifest version -> refused (forged manifest relabelling an image)");
    check(judge(scan(rel, 256), "1.4.0", "1.4.0", "areg-s3-n8-sb", true) == Verdict::NotNewer,
          "same version as running -> refused");
    check(judge(scan(rel, 256), "1.4.0", "1.10.0", "areg-s3-n8-sb", true) == Verdict::NotNewer,
          "older than running (numeric compare, 1.4.0 < 1.10.0) -> refused");
    check(judge(scan(rel, 256), "1.4.0", "1.3.4", "areg-s3-n8", true) == Verdict::BoardMismatch,
          "release image offered to an unsecured board model -> refused");
    const std::string dev = image_with("1.4.0:areg-s3-n8-sb:dev");
    check(judge(scan(dev, 256), "1.4.0", "1.3.4", "areg-s3-n8-sb", true) == Verdict::ProfileMismatch,
          "a RELEASE toy refuses a dev-profile image");
    check(judge(scan(dev, 256), "1.4.0", "1.3.4", "areg-s3-n8-sb", false) == Verdict::Ok,
          "a DEV toy accepts a dev-profile image");

    check(compare_semver("1.10.0", "1.9.9") > 0 && compare_semver("v1.2.3", "1.2.3") == 0 &&
              compare_semver("1.2", "1.2.0") == 0 && compare_semver("1.2.3", "1.2.4") < 0,
          "compare_semver: numeric, v-prefix, missing parts");
    check(strcmp(verdict_name(Verdict::ProfileMismatch), "profile_mismatch") == 0, "verdict names");

    printf(failures == 0 ? "PASS\n" : "FAIL (%d)\n", failures);
    return failures == 0 ? 0 : 1;
}
