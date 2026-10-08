# Release bootloader -- recorded builds

The release bootloader is built ONCE per hardware generation from this
directory by `tools/firmware/build_release_bootloader.sh` (ESP-IDF v5.5.4 @
`735507283d5b2f9fb363a1901172dbd9e847945d`, the commit the pinned
arduino-esp32 3.3.8 precompiled libs were built from), signed OFFLINE with
BOTH Secure Boot keys (`tools/firmware/sign_release.py bootloader`), and
flashed only by the factory station. It can never change on a locked toy.
The build is byte-reproducible (no build date; IDF maps its own paths), so
anyone can rebuild and compare against the table below before signing.

The unsigned binary is NOT committed (`out/` is gitignored); its sha256 is.
The signed binary never enters the repo.

| Date | Built by | IDF commit | Toolchain | IDF esptool / repo | Size | sha256 (bootloader-unsigned.bin) | sha256 (partition-table.bin) | Status |
|---|---|---|---|---|---|---|---|---|
| 2026-10-08 | Claude (cloud container) | 735507283d | `esp-14.2.0_20260121` | esptool 4.12.0, repo 1e499213cd69 + uncommitted chip-security tree | 0x6000 (24,576 B) | `ebabd3548b88ddb2bc557d477b90c1485a2805f026cb99fcdc792da7b021be6e` | `15be07f6da09dbf781f18041d82afc6ddc69e8535502a92b30d512317c59e375` | SUPERSEDED -- its partition table dropped `coredump`; replaced by the next row (same bootloader bytes). |
| 2026-10-08 | Claude (cloud container), review round 3 | 735507283d | `esp-14.2.0_20260121` | esptool 4.12.0, repo 1e499213cd69 + uncommitted chip-security tree | 0x6000 (24,576 B) | `ebabd3548b88ddb2bc557d477b90c1485a2805f026cb99fcdc792da7b021be6e` | `a3a0b8135076dd393fb436e957b2cbc65a3f517d3adf1a76e8e4292f182ee61b` | CANDIDATE -- anti-rollback OFF; table keeps an `encrypted` coredump (0x7E0000). Bootloader byte-identical to the row above; NOT booted on silicon, NOT signed. The owner rebuilds, compares, sets PILOT, runs the pilot, decides anti-rollback + coredump, then sets APPROVED. |

`tools/firmware/build_release_bootloader.sh` asserts the toolchain and IDF
esptool against `esp32/AregVoiceIdf/deps.lock` (TOOLCHAIN, IDF_ESPTOOL),
refuses a dirty tree (and an ESP-IDF checkout or submodule that is not
pristine), and prints the row to paste here.

**Status is a code gate** (review round 3; `tools/firmware/check_release_image.py`
reads this table): `CANDIDATE` (built) -> `PILOT` (the owner rebuilt and
compared; `sign_release.py bootloader --pilot` signs it for the two
sacrificial pilot boards only) -> `APPROVED` (after the hardware pilot and
the anti-rollback / crash-dump decisions; the ONLY status
`sign_release.py bootloader`, the gate's `--bootloader` and the factory
station accept for a real toy). Any other word (`SUPERSEDED`) is never
signable. The partition table a bundle carries must be the one in the SAME
row as its bootloader. Edit only the Status cell of an existing row; a new
build gets a new row.

Signed size = unsigned + one 4 KB signature sector (two blocks: primary +
backup) = 0x7000, which leaves 4 KB below the partition table at 0x8000.
