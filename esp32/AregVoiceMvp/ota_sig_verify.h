#pragma once
// -------------------------------------------------------------
// AregVoiceMvp / ota_sig_verify.h -- app-side Secure Boot V2 check of a
// downloaded OTA image, BEFORE it can become the boot partition.
//
// The bootloader is the real enforcement point (it verifies the app on every
// boot and falls back to the old slot if the signature is bad -- QEMU case E
// in docs/firmware-security.md). This pre-check means a bad image never even
// becomes the selected boot partition. Same algorithm IDF's own
// secure_boot_rsa_signature.c uses: SHA-256 over [0, len-4096), then at least
// one signature block whose key digest is one of the chip's NON-revoked
// eFuse digests and whose RSA-3072-PSS (MGF1-SHA256, salt 32) verifies.
//
// Arduino's Update "signing" feature (UPDATE_SIGN, 512-byte trailer) is a
// different, incompatible format -- not used.
//
// Streaming API, fed from ota_apply's download loop:
//   sbv2_begin(total_len)  -- total = signed file size (manifest sizeBytes)
//   sbv2_update(buf, n)    -- every downloaded chunk, in order
//   sbv2_finish(err, cap)  -- true only if >=1 block verified
// -------------------------------------------------------------
#include <stddef.h>
#include <stdint.h>

// True when this toy must verify OTA signatures: always on a RELEASE image,
// and on any chip whose Secure Boot is enabled (a DEV image there would be
// refused by the bootloader anyway).
bool sbv2_required();

void sbv2_begin(size_t total_len);
void sbv2_update(const uint8_t *buf, size_t n);
bool sbv2_finish(char *err, size_t err_cap);
