#pragma once
// -------------------------------------------------------------
// AregVoiceMvp / tls_trust_anchors.h -- the root CAs every backend TLS
// connection is verified against (net_transport.cpp).
//
// WHY MORE THAN ONE (review 2026-10-08, C089): a locked RELEASE toy has ROM
// download mode burned off -- OTA is its ONLY update path, and OTA itself
// needs TLS to the backend. With a single pinned root, moving the backend off
// Let's Encrypt, or an LE root rotation, would make every locked toy
// permanently unreachable. So the toy trusts several independent roots
// (three CA organisations), and a RELEASE build refuses fewer than three:
//   * security_profile.h  -- #error when AREG_CA_ANCHOR_COUNT < 3 in RELEASE;
//   * tls_trust_anchors.cpp -- static_assert that the PEM text really holds
//     AREG_CA_ANCHOR_COUNT certificates (the macro cannot drift);
//   * tools/firmware/check_release_image.py --profile release -- counts the
//     certificates in the IMAGE and refuses fewer than three (so the factory
//     station's bundle check refuses such an image too).
// Every root here is in Mozilla's CA list (ESP-IDF v5.5.4's
// components/mbedtls/esp_crt_bundle/cacrt_all.pem); the SHA-256
// fingerprints are listed beside each one in the .cpp.
//
// Changing this set: add the new root in the SAME release that still trusts
// the old one, prove that release on a locked bench toy, and only then move
// the backend. Removing a root the backend still chains to strands every
// locked toy for good.
// -------------------------------------------------------------

// Number of certificates in kAregTlsTrustAnchorsPem (checked at compile time).
#define AREG_CA_ANCHOR_COUNT 5

// All anchors as one NUL-terminated PEM string (mbedTLS parses every
// certificate in it): pass straight to WiFiClientSecure::setCACert().
extern const char kAregTlsTrustAnchorsPem[];
