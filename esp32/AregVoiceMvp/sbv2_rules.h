#pragma once
// -------------------------------------------------------------
// AregVoiceMvp / sbv2_rules.h -- PURE parsing rules for an ESP32-S3 Secure
// Boot V2 (RSA-3072-PSS) signed app image. No Arduino, no ESP-IDF, no
// crypto: host-tested by host_tests/sbv2_rules_test.cpp against a committed
// TEST-signed vector. The RSA math runs in ota_sig_verify.cpp (mbedTLS) and,
// for the release gate, in tools/firmware/check_release_image.py (stdlib).
//
// Format (ESP-IDF secure-boot-v2.rst, "Signature Block Format"; checked
// against IDF v5.5.4 and espsecure 5.2.0):
//   signed image = body ++ 4096-byte signature sector
//   body         = app image padded (espsecure / elf2image --secure-pad-v2)
//                  so that len(body) is a multiple of the 64 KB MMU page
//   sector       = up to 3 blocks of 1216 bytes, then 0xFF padding
//   block        = [0] magic 0xE7, [1] version 0x02, [2..4) padding,
//                  [4..36) SHA-256(body),
//                  [36..420) RSA modulus n (little-endian),
//                  [420..424) exponent e (LE uint32),
//                  [424..808) R, [808..812) M' (Montgomery helpers),
//                  [812..1196) RSA-PSS signature (little-endian),
//                  [1196..1200) CRC32 (zlib) of [0..1196), LE,
//                  [1200..1216) padding
//   key digest   = SHA-256(block[36..812)) -- what espefuse burns into
//                  SECURE_BOOT_DIGESTn and esp_secure_boot_read_key_digests
//                  returns.
// -------------------------------------------------------------
#include <stddef.h>
#include <stdint.h>
#include <string.h>

namespace sbv2 {

constexpr size_t kSectorSize = 4096;
constexpr size_t kBlockSize = 1216;
constexpr size_t kMaxBlocks = 3;
constexpr size_t kMmuPage = 0x10000;
constexpr uint8_t kMagic = 0xE7;
constexpr uint8_t kVersion = 0x02;

constexpr size_t kOffImageDigest = 4;
constexpr size_t kOffModulus = 36;
constexpr size_t kModulusLen = 384;
constexpr size_t kOffExponent = 420;
constexpr size_t kOffR = 424;
constexpr size_t kOffMPrime = 808;
constexpr size_t kOffSignature = 812;
constexpr size_t kSignatureLen = 384;
constexpr size_t kOffCrc = 1196;
constexpr size_t kKeyRegionLen = kOffSignature - kOffModulus;  // 776 bytes hashed into the key digest

// zlib/IEEE CRC-32 (reflected, poly 0xEDB88320, init/xorout 0xFFFFFFFF).
inline uint32_t crc32_zlib(const uint8_t *p, size_t n) {
    uint32_t c = 0xFFFFFFFFu;
    for (size_t i = 0; i < n; i++) {
        c ^= p[i];
        for (int k = 0; k < 8; k++) {
            c = (c >> 1) ^ (0xEDB88320u & (0u - (c & 1u)));
        }
    }
    return ~c;
}

inline uint32_t read_le32(const uint8_t *p) {
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}

// A signed APP: at least one 64 KB page of body plus the 4 KB sector, the
// body a whole number of MMU pages. (A signed BOOTLOADER is padded only to
// 4 KB; this rule is for OTA app images.)
constexpr bool signed_image_shape_ok(size_t len) {
    return len % kSectorSize == 0 && len >= (kMmuPage + kSectorSize) &&
           (len - kSectorSize) % kMmuPage == 0;
}

enum class BlockStatus {
    Ok,          // magic, version and CRC all good
    Absent,      // magic/version not present: end of the block list
    BadCrc,      // looks like a block but the CRC does not match
};

struct SigBlock {
    const uint8_t *raw;            // the 1216-byte block
    const uint8_t *image_digest;   // 32 bytes
    const uint8_t *key_region;     // 776 bytes (n, e, R, M')
    const uint8_t *modulus_le;     // 384 bytes
    uint32_t exponent;
    const uint8_t *signature_le;   // 384 bytes
};

inline BlockStatus parse_block(const uint8_t *blk, SigBlock *out) {
    if (blk[0] != kMagic || blk[1] != kVersion) {
        return BlockStatus::Absent;
    }
    if (crc32_zlib(blk, kOffCrc) != read_le32(blk + kOffCrc)) {
        return BlockStatus::BadCrc;
    }
    if (out != nullptr) {
        out->raw = blk;
        out->image_digest = blk + kOffImageDigest;
        out->key_region = blk + kOffModulus;
        out->modulus_le = blk + kOffModulus;
        out->exponent = read_le32(blk + kOffExponent);
        out->signature_le = blk + kOffSignature;
    }
    return BlockStatus::Ok;
}

// Block i of a 4096-byte signature sector (i < kMaxBlocks).
inline const uint8_t *block_at(const uint8_t *sector, size_t i) { return sector + i * kBlockSize; }

// Constant-time-ish 32-byte compare (digests are public; no secret here,
// but there is no reason to be clever either).
inline bool digest_eq(const uint8_t *a, const uint8_t *b) {
    uint8_t d = 0;
    for (size_t i = 0; i < 32; i++) d |= (uint8_t)(a[i] ^ b[i]);
    return d == 0;
}

// Little-endian signature -> big-endian buffer for mbedTLS / RFC 8017.
inline void reverse_copy(const uint8_t *in, uint8_t *out, size_t n) {
    for (size_t i = 0; i < n; i++) out[i] = in[n - 1 - i];
}

}  // namespace sbv2
