#!/usr/bin/env python3
"""Pins the frozen chip-security partition table (stdlib only, no toolchain).

    python3 tools/firmware/test_partitions.py -v

WHY. On a locked toy (Secure Boot V2 + flash encryption, download mode
disabled) the partition table, the bootloader and the eFuses can never
change again -- only the app moves, over OTA. A wrong table is a fleet of
toys stuck with it forever, so every rule from docs/firmware-security.md s6
is a test here, not a paragraph:

  * the table ends at exactly 0x800000 (8 MB, nothing wasted or overrun);
  * rows are sorted, contiguous and non-overlapping, starting at 0x9000
    (the table itself lives at 0x8000);
  * both app slots are 64 KB aligned and exactly 0x300000;
  * `nvs` comes BEFORE `nvs_sec` (Arduino's initArduino() erases the FIRST
    nvs-subtype partition on an init error -- it must be the plaintext one);
  * the `coredump` row carries `encrypted` (a locked toy's crash dump is
    then ciphertext under its per-device flash-encryption key -- review
    2026-10-08; an owner decision before the first lock);
  * no `encrypted` flag on any nvs-subtype row (NVS encryption is its own
    scheme; the FE flag would corrupt it);
  * nvs_sec >= 0x3000 (NVS needs at least 3 pages);
  * esp32/bootloader-release/partitions.csv is byte-identical (the release
    bootloader is built against it);
  * docs/hardware/audit-mcu.md documents the same table.
"""
import re
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
TABLE = REPO / "esp32" / "AregVoiceMvp" / "partitions.csv"
BOOTLOADER_TABLE = REPO / "esp32" / "bootloader-release" / "partitions.csv"
AUDIT = REPO / "docs" / "hardware" / "audit-mcu.md"

FLASH_SIZE = 0x800000
TABLE_OFFSET = 0x8000
APP_SLOT = 0x300000
NVS_PAGE = 0x1000


def parse(path: Path):
    rows = []
    for line in path.read_text().splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        cells = [c.strip() for c in line.split(",")]
        while len(cells) < 6:
            cells.append("")
        name, typ, sub, off, size, flags = cells[:6]
        rows.append({
            "name": name, "type": typ, "subtype": sub,
            "offset": int(off, 0), "size": int(size, 0),
            "flags": {f.strip() for f in flags.split(":") if f.strip()},
        })
    return rows


class PartitionTableTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.rows = parse(TABLE)
        cls.by_name = {r["name"]: r for r in cls.rows}

    def test_expected_rows_in_order(self):
        self.assertEqual([r["name"] for r in self.rows],
                         ["nvs", "otadata", "app0", "app1", "spiffs", "coredump", "nvs_sec"])

    def test_ends_at_exactly_8mb(self):
        last = self.rows[-1]
        self.assertEqual(last["offset"] + last["size"], FLASH_SIZE)

    def test_contiguous_sorted_after_the_table(self):
        self.assertGreater(self.rows[0]["offset"], TABLE_OFFSET)
        self.assertEqual(self.rows[0]["offset"], 0x9000)
        for a, b in zip(self.rows, self.rows[1:]):
            self.assertEqual(a["offset"] + a["size"], b["offset"],
                             f"{a['name']} -> {b['name']} gap/overlap")

    def test_app_slots_aligned_and_3mb(self):
        apps = [r for r in self.rows if r["type"] == "app"]
        self.assertEqual(len(apps), 2)
        for r in apps:
            self.assertEqual(r["offset"] % 0x10000, 0, f"{r['name']} not 64 KB aligned")
            self.assertEqual(r["size"], APP_SLOT, f"{r['name']} is not 0x300000")
        self.assertEqual({r["subtype"] for r in apps}, {"ota_0", "ota_1"})

    def test_nvs_before_nvs_sec(self):
        nvs_rows = [r["name"] for r in self.rows if r["type"] == "data" and r["subtype"] == "nvs"]
        self.assertEqual(nvs_rows, ["nvs", "nvs_sec"],
                         "the plaintext nvs must be the FIRST nvs-subtype row")

    def test_coredump_is_encrypted(self):
        dumps = [r for r in self.rows if r["subtype"] == "coredump"]
        self.assertEqual([r["name"] for r in dumps], ["coredump"])
        self.assertIn("encrypted", dumps[0]["flags"],
                      "without `encrypted` a locked toy writes its RAM (device key, Wi-Fi password) in PLAINTEXT")
        self.assertEqual((dumps[0]["offset"], dumps[0]["size"]), (0x7E0000, 0x10000))

    def test_no_encrypted_flag_on_nvs(self):
        for r in self.rows:
            if r["subtype"] in ("nvs", "nvs_keys"):
                self.assertNotIn("encrypted", r["flags"], f"{r['name']} must not carry 'encrypted'")

    def test_spiffs_flagged_encrypted(self):
        self.assertIn("encrypted", self.by_name["spiffs"]["flags"])

    def test_nvs_sec_size_and_alignment(self):
        r = self.by_name["nvs_sec"]
        self.assertGreaterEqual(r["size"], 0x3000)
        self.assertEqual(r["size"] % NVS_PAGE, 0)
        self.assertEqual(r["offset"] % NVS_PAGE, 0)
        self.assertEqual((r["offset"], r["size"]), (0x7F0000, 0x10000),
                         "the factory station writes nvs_sec here (secure_provision.py reads it from this file)")

    def test_otadata_is_where_boot_app0_goes(self):
        self.assertEqual((self.by_name["otadata"]["offset"], self.by_name["otadata"]["size"]), (0xE000, 0x2000))

    def test_bootloader_release_copy_is_byte_identical(self):
        self.assertTrue(BOOTLOADER_TABLE.is_file(), f"missing {BOOTLOADER_TABLE}")
        self.assertEqual(BOOTLOADER_TABLE.read_bytes(), TABLE.read_bytes(),
                         "esp32/bootloader-release/partitions.csv drifted from the sketch's table")

    def test_latest_released_md_row_pins_this_table(self):
        # Review round 3: the factory station only flashes the partition table
        # RELEASED.md records with the bootloader. This CSV must encode
        # (byte-for-byte IDF gen_esp32part.py, MD5 entry on) to the latest
        # row's sha256 -- an edit here without a rebuilt/recorded table fails.
        import hashlib
        import sys
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        import check_release_image as gate
        rows = gate.released_rows()
        encoded = gate.encode_partition_table(gate.partitions_from_csv(TABLE))
        self.assertEqual(len(encoded), 0xC00)
        self.assertEqual(gate.parse_partition_table(encoded), gate.partitions_from_csv(TABLE))
        self.assertEqual(hashlib.sha256(encoded).hexdigest(), rows[-1]["partition_table_sha256"],
                         "partitions.csv changed: rebuild esp32/bootloader-release and record the new row")

    def test_audit_doc_lists_the_same_table(self):
        text = AUDIT.read_text()
        block = re.search(r"\*\*Current partition table\*\*.*?```(.*?)```", text, re.S)
        self.assertIsNotNone(block, "audit-mcu.md lost its partition table block")
        doc_rows = {}
        for line in block.group(1).splitlines():
            m = re.match(r"\s*([a-z_0-9]+)\s+(0x[0-9a-fA-F]+)\s+(0x[0-9a-fA-F]+)", line)
            if m:
                doc_rows[m.group(1)] = (int(m.group(2), 16), int(m.group(3), 16))
        self.assertEqual(doc_rows, {r["name"]: (r["offset"], r["size"]) for r in self.rows})


if __name__ == "__main__":
    unittest.main()
