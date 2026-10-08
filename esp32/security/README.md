# esp32/security -- PUBLIC Secure Boot material (owner-created)

This directory holds ONLY public information. Nothing in it can sign
firmware. It is empty on purpose until the owner creates the real keys --
every release tool refuses to run without it (no default, no TEST fallback).

Files the owner adds (docs/firmware-security.md s7):

| File | What | Made with |
|---|---|---|
| `sb_primary.pub.pem` | public half of the PRIMARY signing key (signs every release) | `openssl rsa -in sb_primary.pem -pubout -out sb_primary.pub.pem` |
| `sb_backup.pub.pem` | public half of the BACKUP key (bootloader + revocation only) | `openssl rsa -in sb_backup.pem -pubout -out sb_backup.pub.pem` |
| `sb_trusted_digests.txt` | two lines: SHA-256 digest (hex) of primary, then backup -- the 32 bytes burned into SECURE_BOOT_DIGEST0/1 | `espsecure digest-sbv2-public-key --keyfile sb_primary.pub.pem --output primary.digest`, same for backup (`backup.digest`), then `python3 -c "print(open('primary.digest','rb').read().hex()); print(open('backup.digest','rb').read().hex())" > sb_trusted_digests.txt` |

The PRIVATE keys (`sb_primary.pem`, `sb_backup.pem`) are generated on the
OFFLINE signing laptop with `openssl genrsa -out sb_primary.pem 3072` (RSA
3072 is the only Secure Boot V2 scheme the ESP32-S3 has), live on two
separate encrypted USB drives in two buildings, and NEVER enter this
repository, the build machine or the factory station.

**Run every command in the table above on the OFFLINE laptop, in a folder
on the encrypted stick -- never inside this repository** (`openssl rsa
-in sb_primary.pem ...` run here would leave the private key next to its
public half). Then copy ONLY `sb_primary.pub.pem`, `sb_backup.pub.pem` and
`sb_trusted_digests.txt` into `esp32/security/` (a USB stick that carries
nothing else). As a last net the repository's `.gitignore` ignores every
`*.pem` except `*.pub.pem`, so `git add -A` never picks up a private key --
check `git status` shows exactly those three files before committing.

Who reads these files:
- `tools/firmware/check_release_image.py --require-sbv2 / --bootloader
  --trusted-digests esp32/security/sb_trusted_digests.txt` (release gate);
- `tools/firmware/build_idf.sh release` and `tools/firmware/sign_release.py`
  (refuse a key whose digest is not listed);
- `tools/factory/secure_provision.py` (burns the digests from the `.pub.pem`
  files and checks them against `sb_trusted_digests.txt`).

TEST material for the unit tests lives in `tools/firmware/testdata/`
(`TEST_*`), is marked TEST ONLY, and is refused by every real release and
factory path.
