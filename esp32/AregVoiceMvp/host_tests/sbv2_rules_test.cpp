// Host test for sbv2_rules.h (Secure Boot V2 signature block parsing,
// 2026-10-08). Plain g++, run FROM host_tests/ (reads vectors/ relatively):
//
//     g++ -std=c++17 -Wall -Wextra -I.. -o /tmp/sbv2_test sbv2_rules_test.cpp && /tmp/sbv2_test
//
// Uses the committed TEST vector vectors/sbv2_sector.bin -- the signature
// sector of tools/firmware/testdata/TEST_app_signed.bin, signed by a TEST
// key that never left the machine that generated it
// (tools/firmware/testdata/make_test_vectors.py). Proves the byte offsets
// (image digest, key region, exponent, signature, CRC) against espsecure's
// real output. The RSA-PSS math itself is covered by the release gate's
// Python tests and, on the toy, by mbedTLS -- not here.
#include "../sbv2_rules.h"
#include "vectors/sbv2_sector_expected.h"
#include <cstdio>
#include <cstring>
#include <vector>

static int failures = 0;
static void check(bool ok, const char *what) {
    printf("  %-4s %s\n", ok ? "ok" : "FAIL", what);
    if (!ok) failures++;
}

// Minimal SHA-256 (FIPS 180-4) so the key digest can be recomputed from the
// parsed key region without any library.
namespace {
struct Sha256 {
    uint32_t h[8] = {0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19};
    static uint32_t r(uint32_t x, int n) { return (x >> n) | (x << (32 - n)); }
    void block(const uint8_t *p) {
        static const uint32_t k[64] = {
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2};
        uint32_t w[64];
        for (int i = 0; i < 16; i++) w[i] = (uint32_t)p[4 * i] << 24 | p[4 * i + 1] << 16 | p[4 * i + 2] << 8 | p[4 * i + 3];
        for (int i = 16; i < 64; i++) {
            uint32_t s0 = r(w[i - 15], 7) ^ r(w[i - 15], 18) ^ (w[i - 15] >> 3);
            uint32_t s1 = r(w[i - 2], 17) ^ r(w[i - 2], 19) ^ (w[i - 2] >> 10);
            w[i] = w[i - 16] + s0 + w[i - 7] + s1;
        }
        uint32_t a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
        for (int i = 0; i < 64; i++) {
            uint32_t t1 = hh + (r(e, 6) ^ r(e, 11) ^ r(e, 25)) + ((e & f) ^ (~e & g)) + k[i] + w[i];
            uint32_t t2 = (r(a, 2) ^ r(a, 13) ^ r(a, 22)) + ((a & b) ^ (a & c) ^ (b & c));
            hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
        }
        h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
    }
    void digest(const uint8_t *data, size_t n, uint8_t out[32]) {
        std::vector<uint8_t> m(data, data + n);
        m.push_back(0x80);
        while (m.size() % 64 != 56) m.push_back(0);
        const uint64_t bits = (uint64_t)n * 8;
        for (int i = 7; i >= 0; i--) m.push_back((uint8_t)(bits >> (8 * i)));
        for (size_t i = 0; i < m.size(); i += 64) block(&m[i]);
        for (int i = 0; i < 8; i++) for (int j = 0; j < 4; j++) out[4 * i + j] = (uint8_t)(h[i] >> (24 - 8 * j));
    }
};
}  // namespace

int main() {
    using namespace sbv2;
    const uint8_t v[] = "123456789";
    check(crc32_zlib(v, 9) == 0xCBF43926u, "crc32_zlib(\"123456789\") == 0xCBF43926 (the standard check value)");

    // ---- shape rules ----
    check(signed_image_shape_ok(0x11000), "0x11000 (one 64 KB page + sector) is a signed app shape");
    check(signed_image_shape_ok(0x2F1000), "0x2F1000 (47 pages + sector) is a signed app shape");
    check(!signed_image_shape_ok(0x10000), "an unsigned 64 KB image is not");
    check(!signed_image_shape_ok(0x1000), "a lone sector is not");
    check(!signed_image_shape_ok(0x12000), "body not a whole MMU page is not");
    check(!signed_image_shape_ok(0x11001), "a length that is not a multiple of 4096 is not");
    check(!signed_image_shape_ok(0), "zero is not");
    check(!signed_image_shape_ok(0x18E720), "an Arduino .bin as built (1,632,032 B) is not");

    // ---- the committed TEST sector ----
    std::vector<uint8_t> sec(kSectorSize, 0);
    FILE *f = fopen("vectors/sbv2_sector.bin", "rb");
    const size_t got = f ? fread(sec.data(), 1, sec.size(), f) : 0;
    if (f) fclose(f);
    check(got == kSectorSize, "vectors/sbv2_sector.bin is a full 4096-byte sector (run from host_tests/)");
    check(signed_image_shape_ok(kTestSignedLen), "the TEST image length is a signed shape");

    SigBlock b{};
    check(parse_block(block_at(sec.data(), 0), &b) == BlockStatus::Ok, "block 0 parses (magic 0xE7, version 2, CRC ok)");
    check(memcmp(b.image_digest, kTestImageDigest, 32) == 0, "block 0 image digest == SHA-256 of the TEST body");
    uint8_t kd[32];
    Sha256().digest(b.key_region, kKeyRegionLen, kd);
    check(memcmp(kd, kTestKeyDigest, 32) == 0, "SHA-256(block[36..812)) == the TEST primary key digest");
    check(b.exponent == 65537, "exponent parsed little-endian at offset 420 (65537)");
    check(b.modulus_le == block_at(sec.data(), 0) + 36 && b.signature_le == block_at(sec.data(), 0) + 812,
          "modulus at 36, signature at 812");
    check((b.modulus_le[kModulusLen - 1] & 0x80) != 0, "modulus is little-endian: top byte (last) has the high bit (3072-bit key)");
    check(parse_block(block_at(sec.data(), 1), nullptr) == BlockStatus::Absent, "block 1 absent (app signed by one key)");

    uint8_t be[kSignatureLen];
    reverse_copy(b.signature_le, be, kSignatureLen);
    check(be[0] == b.signature_le[kSignatureLen - 1] && be[kSignatureLen - 1] == b.signature_le[0],
          "reverse_copy turns the LE signature into big-endian");

    // ---- negatives ----
    std::vector<uint8_t> t = sec;
    t[0] = 0xE6;
    check(parse_block(t.data(), nullptr) == BlockStatus::Absent, "bad magic -> absent");
    t = sec; t[1] = 0x01;
    check(parse_block(t.data(), nullptr) == BlockStatus::Absent, "version 1 -> absent (SBv1 block is not ours)");
    t = sec; t[100] ^= 0x01;
    check(parse_block(t.data(), nullptr) == BlockStatus::BadCrc, "a flipped key byte -> bad CRC");
    t = sec; t[kOffCrc] ^= 0xFF;
    check(parse_block(t.data(), nullptr) == BlockStatus::BadCrc, "a flipped CRC byte -> bad CRC");
    t = sec; t[kOffSignature + 5] ^= 0x10;
    check(parse_block(t.data(), nullptr) == BlockStatus::BadCrc, "a flipped signature byte -> bad CRC (the CRC covers it)");
    std::vector<uint8_t> blank(kSectorSize, 0xFF);
    check(parse_block(blank.data(), nullptr) == BlockStatus::Absent, "an erased (0xFF) sector has no block");

    uint8_t a[32] = {0}, c[32] = {0};
    check(digest_eq(a, c), "digest_eq equal");
    c[31] = 1;
    check(!digest_eq(a, c), "digest_eq differs in the last byte");

    printf(failures == 0 ? "PASS\n" : "FAIL (%d)\n", failures);
    return failures == 0 ? 0 : 1;
}
