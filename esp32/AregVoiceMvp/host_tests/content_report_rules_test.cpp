// Host test for the content-report buffer arithmetic.
//
// Runs with plain g++ — no Arduino core, no board, no cable:
//
//     g++ -std=c++17 -Wall -Wextra -o /tmp/cr_test
//         esp32/AregVoiceMvp/host_tests/content_report_rules_test.cpp && /tmp/cr_test
//
//   (one line; split here only so this comment stays a comment)
//
// It exists because the one genuinely error-prone part of the content
// report is buffer arithmetic, and getting it wrong is not a compile error
// — it is a toy telling the backend it owns a story called "anban-hu",
// which the backend then counts as MISSING and the dashboard reports as
// "your toy is out of date" about a toy that is fine.
//
// It compiles the REAL header. A copy of the function would only ever
// prove the copy right, which is the trap the SD-fallback bench harness
// was written to avoid.
//
// What it does NOT cover: reading the index off a real card, JSON parsing,
// the heartbeat body, or the change-detection state. Those need hardware.
#include "../content_report_rules.h"
#include <cstdio>
#include <cstring>
static int failures = 0;
static void check(bool ok, const char *what) {
    printf("  %-4s %s\n", ok ? "ok" : "FAIL", what);
    if (!ok) failures++;
}
int main() {
    { char b[64] = ""; size_t u = 0;
      check(cr_append_story(b, sizeof(b), u, "ulik", 12), "first entry appends");
      check(strcmp(b, "ulik:12") == 0 && u == 7, "no leading comma");
      check(cr_append_story(b, sizeof(b), u, "anban-huri", 9), "second entry appends");
      check(strcmp(b, "ulik:12,anban-huri:9") == 0, "comma separated"); }

    { char b[64] = ""; size_t u = 0;
      check(!cr_append_story(b, sizeof(b), u, nullptr, 3), "null id refused");
      check(!cr_append_story(b, sizeof(b), u, "", 3), "empty id refused");
      check(!cr_append_story(b, sizeof(b), u, "ulik", 0), "version 0 refused");
      check(!cr_append_story(b, sizeof(b), u, "ulik", -1), "negative version refused");
      check(b[0] == '\0' && u == 0, "refusals leave the buffer empty"); }

    // KEYSTONE: an entry that does not fit is dropped WHOLE. A truncated id
    // reaches the backend as an unknown story and is counted as missing —
    // a false "your toy is out of date" on a toy that is fine.
    { char b[16] = ""; size_t u = 0;
      check(cr_append_story(b, sizeof(b), u, "ulik", 12), "fits");
      check(!cr_append_story(b, sizeof(b), u, "anban-huri", 9), "overflow refused");
      check(strcmp(b, "ulik:12") == 0, "buffer unchanged by the refusal");
      check(b[u] == '\0', "still terminated"); }

    // Exact fit: "ulik:12" is 7 chars + NUL = 8.
    { char b[8] = ""; size_t u = 0;
      check(cr_append_story(b, sizeof(b), u, "ulik", 12), "exact fit accepted");
      check(strcmp(b, "ulik:12") == 0, "exact fit correct"); }
    { char b[7] = ""; size_t u = 0;
      check(!cr_append_story(b, sizeof(b), u, "ulik", 12), "one byte short refused"); }

    { char b[8] = ""; size_t u = 0;
      check(!cr_append_story(nullptr, 8, u, "ulik", 12), "null buffer refused");
      check(!cr_append_story(b, 0, u, "ulik", 12), "zero capacity refused"); }

    // The real library must fit the shipped 320-byte buffer with room over.
    { char b[320] = ""; size_t u = 0;
      const char *ids[] = {"anban-huri","hedgehog-apple","khosogh-dzuk","little-cloud",
                           "pochat-aghves","princess-and-pea","sutasan","sutlik-orskan",
                           "three-piglets","ulik"};
      bool all = true;
      for (int i = 0; i < 10; i++) all = cr_append_story(b, sizeof(b), u, ids[i], 12) && all;
      check(all, "the ten shipped stories all fit");
      printf("       (uses %zu of 320 bytes)\n", u); }

    // An id at the allowlist limit is never truncated mid-name.
    { char b[64] = ""; size_t u = 0;
      char longid[49]; memset(longid, 'a', 48); longid[48] = '\0';
      check(cr_append_story(b, sizeof(b), u, longid, 1), "48-char id fits in 64");
      check(strncmp(b, longid, 48) == 0 && b[48] == ':', "id written whole"); }

    printf(failures == 0 ? "PASS\n" : "FAIL (%d)\n", failures);
    return failures == 0 ? 0 : 1;
}
