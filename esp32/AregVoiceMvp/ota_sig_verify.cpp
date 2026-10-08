// -------------------------------------------------------------
// AregVoiceMvp / ota_sig_verify.cpp -- see ota_sig_verify.h.
//
// Reference implementations that agree with the ROM/bootloader on every
// QEMU vector of the design: tools/firmware/check_release_image.py (stdlib
// port) and host_tests/sbv2_rules_test.cpp (parsing rules). The mbedTLS
// calls are the exact ones the design's QEMU probe app ran.
// NOT yet run on real silicon.
// -------------------------------------------------------------
#include "ota_sig_verify.h"

#include <Arduino.h>
#include <string.h>

#include <esp_efuse.h>
#include <esp_secure_boot.h>
#include <mbedtls/bignum.h>
#include <mbedtls/md.h>
#include <mbedtls/rsa.h>
#include <mbedtls/sha256.h>

#include "sbv2_rules.h"
#include "security_profile.h"

namespace {

mbedtls_sha256_context s_sha;
bool s_active = false;
size_t s_total = 0;
size_t s_seen = 0;
// The last 4096 bytes of the stream (the signature sector). Static: never on
// the loop task's stack.
uint8_t s_sector[sbv2::kSectorSize];

void set_err(char *err, size_t cap, const char *msg) {
    if (err != nullptr && cap > 0) snprintf(err, cap, "%s", msg);
}

// RSA-3072-PSS verify of one parsed block over `digest`. 0 == valid.
int rsa_pss_verify(const sbv2::SigBlock &b, const uint8_t digest[32]) {
    mbedtls_rsa_context rsa;
    mbedtls_mpi N, E;
    mbedtls_rsa_init(&rsa);
    mbedtls_mpi_init(&N);
    mbedtls_mpi_init(&E);
    int r = mbedtls_rsa_set_padding(&rsa, MBEDTLS_RSA_PKCS_V21, MBEDTLS_MD_SHA256);
    if (r == 0) r = mbedtls_mpi_read_binary_le(&N, b.modulus_le, sbv2::kModulusLen);
    if (r == 0) r = mbedtls_mpi_lset(&E, (mbedtls_mpi_sint)b.exponent);
    if (r == 0) r = mbedtls_rsa_import(&rsa, &N, nullptr, nullptr, nullptr, &E);
    if (r == 0) r = mbedtls_rsa_complete(&rsa);
    static uint8_t sig_be[sbv2::kSignatureLen];
    sbv2::reverse_copy(b.signature_le, sig_be, sbv2::kSignatureLen);
    if (r == 0) {
        r = mbedtls_rsa_rsassa_pss_verify_ext(&rsa, MBEDTLS_MD_SHA256, 32, digest,
                                              MBEDTLS_MD_SHA256, 32, sig_be);
    }
    mbedtls_mpi_free(&N);
    mbedtls_mpi_free(&E);
    mbedtls_rsa_free(&rsa);
    return r;
}

}  // namespace

bool sbv2_required() {
    return AREG_IS_RELEASE || esp_secure_boot_enabled();
}

void sbv2_begin(size_t total_len) {
    if (s_active) mbedtls_sha256_free(&s_sha);
    mbedtls_sha256_init(&s_sha);
    mbedtls_sha256_starts(&s_sha, /*is224=*/0);
    s_active = true;
    s_total = total_len;
    s_seen = 0;
    memset(s_sector, 0xFF, sizeof(s_sector));
}

void sbv2_update(const uint8_t *buf, size_t n) {
    if (!s_active || buf == nullptr || n == 0) return;
    const size_t body_end = (s_total >= sbv2::kSectorSize) ? s_total - sbv2::kSectorSize : 0;
    // Part of this chunk that belongs to the body -> hash it.
    if (s_seen < body_end) {
        const size_t take = (body_end - s_seen) < n ? (body_end - s_seen) : n;
        mbedtls_sha256_update(&s_sha, buf, take);
    }
    // Part that falls in the last 4096 bytes -> keep it.
    const size_t chunk_end = s_seen + n;
    if (chunk_end > body_end && s_total >= sbv2::kSectorSize) {
        const size_t from = s_seen > body_end ? s_seen : body_end;      // absolute
        const size_t to = chunk_end < s_total ? chunk_end : s_total;    // absolute
        if (to > from) {
            memcpy(s_sector + (from - body_end), buf + (from - s_seen), to - from);
        }
    }
    s_seen = chunk_end;
}

bool sbv2_finish(char *err, size_t err_cap) {
    if (!s_active) {
        set_err(err, err_cap, "sig_not_started");
        return false;
    }
    uint8_t digest[32];
    mbedtls_sha256_finish(&s_sha, digest);
    mbedtls_sha256_free(&s_sha);
    s_active = false;
    if (s_seen != s_total || !sbv2::signed_image_shape_ok(s_total)) {
        set_err(err, err_cap, "sig_shape_invalid");
        return false;
    }

    // Trusted keys = the chip's non-revoked eFuse digests (NULL = revoked
    // or empty slot). No trusted digest at all => nothing can verify.
    esp_secure_boot_key_digests_t trusted;
    memset(&trusted, 0, sizeof(trusted));
    if (esp_secure_boot_read_key_digests(&trusted) != ESP_OK) {
        set_err(err, err_cap, "sig_no_trusted_keys");
        return false;
    }

    int verified = 0;
    for (size_t i = 0; i < sbv2::kMaxBlocks; i++) {
        sbv2::SigBlock b;
        const sbv2::BlockStatus st = sbv2::parse_block(sbv2::block_at(s_sector, i), &b);
        if (st == sbv2::BlockStatus::Absent) break;
        if (st == sbv2::BlockStatus::BadCrc) {
            Serial.printf("[ota] sig block %u: bad CRC\n", (unsigned)i);
            break;
        }
        if (!sbv2::digest_eq(b.image_digest, digest)) {
            Serial.printf("[ota] sig block %u: image digest mismatch\n", (unsigned)i);
            continue;
        }
        uint8_t key_digest[32];
        mbedtls_sha256(b.key_region, sbv2::kKeyRegionLen, key_digest, /*is224=*/0);
        int slot = -1;
        for (int s = 0; s < (int)(sizeof(trusted.key_digests) / sizeof(trusted.key_digests[0])); s++) {
            if (trusted.key_digests[s] != nullptr &&
                sbv2::digest_eq((const uint8_t *)trusted.key_digests[s], key_digest)) {
                slot = s;
                break;
            }
        }
        if (slot < 0) {
            Serial.printf("[ota] sig block %u: key %02x%02x%02x%02x not trusted by this chip\n",
                          (unsigned)i, key_digest[0], key_digest[1], key_digest[2], key_digest[3]);
            continue;
        }
        const int r = rsa_pss_verify(b, digest);
        Serial.printf("[ota] sig block %u: slot %d rsa-pss %s\n", (unsigned)i, slot,
                      r == 0 ? "OK" : "FAIL");
        if (r == 0) verified++;
    }
    if (verified == 0) {
        set_err(err, err_cap, "image_sig_invalid");
        return false;
    }
    return true;
}
