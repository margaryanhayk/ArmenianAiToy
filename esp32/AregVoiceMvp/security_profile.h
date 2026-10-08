#pragma once
// -------------------------------------------------------------
// AregVoiceMvp / security_profile.h -- which security profile this image is,
// and the compile-time refusals a RELEASE image must pass.
//
// Two profiles (docs/firmware-security.md):
//   DEV     (default) -- bench boards, no eFuse changes, stock bootloader.
//                         Bench flags allowed. nvs_sec is plaintext.
//   RELEASE           -- the only image a locked (Secure Boot V2 + flash
//                         encryption) toy runs. Pass
//                         -DAREG_SECURITY_PROFILE_RELEASE on the build line
//                         (tools/firmware/build_idf.sh release does) -- not in
//                         config.h, because config.h.example reads the
//                         profile to pick defaults BEFORE a later line could
//                         set it.
//
// One header, not two: this is also the release guard the fix plan called
// release_guard.h (fw-135-build-gate). Every #error / static_assert below
// exists because the thing it refuses has shipped, or nearly shipped, once:
// a real device key in an OTA image (2026-08-13), a home Wi-Fi password in
// the served image (2026-08-14), a bench flag eating the button for two
// evenings, the shared "areg-pair" PoP on every toy (C150).
//
// Include it from any translation unit that needs AREG_PROFILE_NAME or the
// profile macros. The checks are header-level, so the first TU that includes
// it (AregVoiceMvp.ino does, unconditionally) fails the whole build.
// -------------------------------------------------------------
#include "config.h"
#include "tls_trust_anchors.h"  // AREG_CA_ANCHOR_COUNT (release needs >= 3 roots)
#include <esp_arduino_version.h>

#if defined(AREG_SECURITY_PROFILE_RELEASE) && defined(AREG_SECURITY_PROFILE_DEV)
#error "Define exactly one of AREG_SECURITY_PROFILE_RELEASE / AREG_SECURITY_PROFILE_DEV"
#endif
#if !defined(AREG_SECURITY_PROFILE_RELEASE) && !defined(AREG_SECURITY_PROFILE_DEV)
#define AREG_SECURITY_PROFILE_DEV 1
#endif

#ifdef AREG_SECURITY_PROFILE_RELEASE
#define AREG_PROFILE_NAME "release"
#define AREG_IS_RELEASE 1
#else
#define AREG_PROFILE_NAME "dev"
#define AREG_IS_RELEASE 0
#endif

#ifndef AREG_FW_VERSION
#define AREG_FW_VERSION "1.0.0"
#endif
#ifndef AREG_BOARD_MODEL
#if AREG_IS_RELEASE
#define AREG_BOARD_MODEL "areg-s3-n8-sb"
#else
#define AREG_BOARD_MODEL "areg-s3-n8"
#endif
#endif
#ifndef AREG_MANIFEST_HMAC_KEY
#define AREG_MANIFEST_HMAC_KEY ""
#endif

// ---- the pinned Arduino core ----------------------------------------------
// The release bootloader is built from the exact ESP-IDF commit the pinned
// core's precompiled libs were built from (esp32s3-libs versions.txt), and a
// frozen bootloader must keep booting every future app. Pin = 3.3.8
// (ESP-IDF v5.5.4 @ 735507283d). Changing it is an owner decision that also
// means re-proving the release bootloader on a locked bench toy.
#define AREG_PINNED_CORE_MAJOR 3
#define AREG_PINNED_CORE_MINOR 3
#define AREG_PINNED_CORE_PATCH 8
#if ESP_ARDUINO_VERSION != ESP_ARDUINO_VERSION_VAL(AREG_PINNED_CORE_MAJOR, AREG_PINNED_CORE_MINOR, AREG_PINNED_CORE_PATCH)
#if AREG_IS_RELEASE
#error "RELEASE images must be built with arduino-esp32 3.3.8 (the pinned core; see docs/firmware-security.md)"
#else
#warning "arduino-esp32 core is not the pinned 3.3.8 -- fine for a bench build, never for a release"
#endif
#endif

#if AREG_IS_RELEASE

// ---- bench / one-shot flags: never in a release image ----------------------
#ifdef AREG_TLS_INSECURE
#error "RELEASE: AREG_TLS_INSECURE turns off server verification"
#endif
#ifdef AREG_PROVISION_IDENTITY_ONCE
#error "RELEASE: AREG_PROVISION_IDENTITY_ONCE burns ONE toy's identity; an OTA image reaches every toy"
#endif
#ifdef AREG_PROVISION_WIFI_ONCE
#error "RELEASE: AREG_PROVISION_WIFI_ONCE carries one household's Wi-Fi password"
#endif
#ifdef AREG_PROVISION_WIFI_FORCE
#error "RELEASE: AREG_PROVISION_WIFI_FORCE carries one household's Wi-Fi password"
#endif
#ifdef AREG_BLE_POP
#error "RELEASE: AREG_BLE_POP is a bench burn; the factory station writes the PoP to nvs_sec"
#endif
#ifdef AREG_PROV_POP
#error "RELEASE: AREG_PROV_POP must not be defined -- the shared fallback PoP exists only in DEV builds (C150). Update your config.h from config.h.example."
#endif
#ifdef AREG_SD_BENCH_TEST
#error "RELEASE: AREG_SD_BENCH_TEST is a bench build"
#endif
#ifdef AREG_SD_DIAG_BENCH
#error "RELEASE: AREG_SD_DIAG_BENCH is a bench build"
#endif
#ifdef AREG_SD_PLAYBACK_BENCH
#error "RELEASE: AREG_SD_PLAYBACK_BENCH is a bench build"
#endif
#ifdef AREG_OFFLINE_GAMES_BENCH
#error "RELEASE: AREG_OFFLINE_GAMES_BENCH is a bench build"
#endif
#ifdef AREG_OFFLINE_QUIZ_BENCH
#error "RELEASE: AREG_OFFLINE_QUIZ_BENCH is a bench build"
#endif
#ifdef AREG_STORY_SD_FALLBACK_TEST_BENCH
#error "RELEASE: AREG_STORY_SD_FALLBACK_TEST_BENCH is a bench build"
#endif
#ifdef AREG_STORY_SELECT_TEST_BENCH
#error "RELEASE: AREG_STORY_SELECT_TEST_BENCH is a bench build"
#endif
#ifdef AREG_CONTENT_SYNC_TEST_BENCH
#error "RELEASE: AREG_CONTENT_SYNC_TEST_BENCH is a bench build"
#endif

// ---- TLS trust: a locked toy's only update path ----------------------------
// ROM download mode is burned off at the factory, so a toy that cannot
// verify the backend's certificate can never be updated again. One pinned
// root would strand the whole locked fleet on a CA move or root rotation
// (review 2026-10-08); tls_trust_anchors.h explains the set.
#if !defined(AREG_CA_ANCHOR_COUNT) || AREG_CA_ANCHOR_COUNT < 3
#error "RELEASE: at least 3 independent TLS trust anchors required (tls_trust_anchors.h) -- a locked toy cannot be re-flashed"
#endif

// AREG_CONTENT_SYNC_BENCH is the one *_BENCH name that is LOAD-BEARING
// (config.h.example: without it the toy downloads nothing, silently,
// forever). A release REQUIRES it rather than refusing it.
#ifndef AREG_CONTENT_SYNC_BENCH
#error "RELEASE: AREG_CONTENT_SYNC_BENCH must be defined (despite its name it enables cloud->SD content sync)"
#endif
// A release image has no compiled-in Wi-Fi; BLE provisioning is the only way
// a family's network ever reaches it.
#ifndef AREG_USE_BLE_PROVISIONING
#error "RELEASE: AREG_USE_BLE_PROVISIONING must be defined (no compiled Wi-Fi exists in a release image)"
#endif

namespace areg_sec_profile {
constexpr bool starts_with(const char *s, const char *p) {
    return *p == '\0' ? true : (*s == *p && starts_with(s + 1, p + 1));
}
constexpr bool str_eq(const char *a, const char *b) {
    return *a == *b && (*a == '\0' || str_eq(a + 1, b + 1));
}
constexpr unsigned long str_len(const char *s) { return *s == '\0' ? 0 : 1 + str_len(s + 1); }
constexpr bool ends_with(const char *s, const char *suffix) {
    return str_len(s) >= str_len(suffix) && str_eq(s + (str_len(s) - str_len(suffix)), suffix);
}
}  // namespace areg_sec_profile

static_assert(areg_sec_profile::starts_with(AREG_BACKEND_BASE_URL, "https://"),
              "RELEASE: AREG_BACKEND_BASE_URL must start with https://");
#ifdef AREG_WIFI_SSID
static_assert(sizeof(AREG_WIFI_SSID) == 1, "RELEASE: AREG_WIFI_SSID must be \"\" (no compiled-in Wi-Fi)");
#endif
#ifdef AREG_WIFI_PASSWORD
static_assert(sizeof(AREG_WIFI_PASSWORD) == 1, "RELEASE: AREG_WIFI_PASSWORD must be \"\" (no compiled-in Wi-Fi)");
#endif
#ifdef AREG_DEVICE_ID
static_assert(areg_sec_profile::str_eq(AREG_DEVICE_ID, "YOUR_DEVICE_GUID"),
              "RELEASE: AREG_DEVICE_ID must stay the config.h.example placeholder");
#endif
#ifdef AREG_DEVICE_API_KEY
static_assert(areg_sec_profile::str_eq(AREG_DEVICE_API_KEY, "YOUR_DEVICE_API_KEY"),
              "RELEASE: AREG_DEVICE_API_KEY must stay the config.h.example placeholder");
#endif
static_assert(sizeof(AREG_MANIFEST_HMAC_KEY) > 1,
              "RELEASE: AREG_MANIFEST_HMAC_KEY must be set (rotate it with the first secured release)");
static_assert(areg_sec_profile::ends_with(AREG_BOARD_MODEL, "-sb"),
              "RELEASE: AREG_BOARD_MODEL must end in -sb so the backend never offers it to an unsecured toy");

#endif  // AREG_IS_RELEASE
