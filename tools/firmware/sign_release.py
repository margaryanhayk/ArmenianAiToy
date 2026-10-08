#!/usr/bin/env python3
"""Sign an Areg release on the OFFLINE signing machine (Secure Boot V2).

The private keys never leave this machine (docs/firmware-security.md s7):
the build machine hands over UNSIGNED images, this script signs them and
hands back signed images plus bundle.json. Nothing here touches a toy.

    # every release -- the app, primary key only:
    python3 tools/firmware/sign_release.py app \\
        --keyfile /media/vault/sb_primary.pem \\
        --input app-unsigned.bin --expect-version 1.4.0 --out-dir bundle-1.4.0

    # once per hardware generation -- the bootloader, BOTH keys. Only a build
    # recorded in esp32/bootloader-release/RELEASED.md as APPROVED (or, with
    # --pilot, PILOT -- for the two sacrificial pilot boards):
    python3 tools/firmware/sign_release.py bootloader \\
        --keyfile /media/vault/sb_primary.pem --backup-keyfile /media/vault2/sb_backup.pem \\
        --input bootloader-unsigned.bin --out-dir bundle-1.4.0 [--pilot]

    # the unsigned factory pieces, and -- once the hardware pilot passed with
    # THIS signed bootloader -- the pilot evidence (no key needed):
    python3 tools/firmware/sign_release.py add --out-dir bundle-1.4.0 \\
        --partition-table partition-table.bin --boot-app0 boot_app0.bin \\
        --pilot-evidence tools/quality-evidence/chip-security-pilot-YYYYMMDD.md

Refuses (exit 1) unless:
  * the UNSIGNED app passes tools/firmware/check_release_image.py
    --profile release (marker AREGFWV1:<version>:<board>-sb:release, no
    bench/fallback strings, ESP32-S3 dio/80m/8MB header);
  * every key's public digest is one of the trusted digests
    (default esp32/security/sb_trusted_digests.txt -- the PUBLIC half that
    the factory burns into the eFuses), so a typo'd or stray key cannot sign;
  * the key is not a TEST key (a digest listed in a "TEST ONLY" file), unless
    --allow-test-key is given -- then every output and bundle.json say TEST;
  * after signing, the gate verifies the result with --require-sbv2 (app) or
    --bootloader (exactly two blocks, both keys);
  * bootloader: sha256(input) is the "bootloader-unsigned.bin" of a row of
    esp32/bootloader-release/RELEASED.md whose Status is APPROVED (--pilot:
    PILOT or APPROVED; a TEST key: any recorded row). A signature says who
    signed, not WHAT gets frozen into a toy for life -- the app-config
    bootloader build_idf.sh also produces (no CONFIG_SECURE_BOOT, so it never
    verifies the app) is the same size and would sign just as well. The owner
    sets APPROVED only after the hardware pilot and the anti-rollback decision
    (docs/firmware-security.md s11), which makes that rule a code gate;
  * add: partition-table.bin is the one recorded in the SAME RELEASED.md row
    as the bundle's bootloader (sign the bootloader first), and boot_app0.bin
    is arduino-esp32 3.3.8's (pinned sha256);
  * `espsecure` is esptool 5.2.0's (tools/factory/requirements.txt; the
    dash-style sign-data this script runs does not exist in 4.x); set
    ESPSECURE to override the command;
  * --out-dir is not a git-tracked place inside this repository: signed
    images carry the manifest HMAC key and never go into git (the repository
    was public -- docs/firmware-security.md s11; serve them from private
    storage).
--pilot-evidence (add): the factory station refuses to `provision` a real
toy with a bundle whose bundle.json has no "pilot" entry naming an evidence
file in tools/quality-evidence/ that contains the bundle's SIGNED bootloader
sha256 and the line "OTA release+1 on locked board: PASS"
(docs/firmware-security.md s12). This script checks the same before writing.

bundle.json (in --out-dir) records sha256 + size of every artifact, the
version, the pinned ESP-IDF commit and Arduino core, and whether anything is
TEST-signed. The factory station verifies every sha before using a bundle.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shlex
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import check_release_image as gate_lib  # noqa: E402  (RELEASED.md pins -- one rule, every tool)

REPO = Path(__file__).resolve().parents[2]
GATE = REPO / "tools" / "firmware" / "check_release_image.py"
DEFAULT_TRUSTED = REPO / "esp32" / "security" / "sb_trusted_digests.txt"
TEST_TRUSTED = REPO / "tools" / "firmware" / "testdata" / "TEST_sb_trusted_digests.txt"
IDF_COMMIT = "735507283d5b2f9fb363a1901172dbd9e847945d"
ARDUINO_CORE = "3.3.8"
ESPSECURE_BANNER = "espsecure v5.2.0"
EVIDENCE_DIR = REPO / "tools" / "quality-evidence"
PILOT_PASS_LINE = "OTA release+1 on locked board: PASS"


class SignError(RuntimeError):
    pass


def _default_runner(argv: list[str]) -> subprocess.CompletedProcess:
    return subprocess.run(argv, capture_output=True, text=True)


class Signer:
    def __init__(self, runner=_default_runner, espsecure: str | None = None):
        self.run = runner
        self.espsecure = shlex.split(espsecure or os.environ.get("ESPSECURE", "espsecure"))

    def _ok(self, argv: list[str], what: str) -> str:
        r = self.run(argv)
        if r.returncode != 0:
            raise SignError(f"{what} failed (exit {r.returncode}):\n{r.stdout}{r.stderr}")
        return r.stdout

    def key_digest(self, keyfile: Path) -> str:
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp) / "digest.bin"
            self._ok([*self.espsecure, "digest-sbv2-public-key", "--keyfile", str(keyfile), "--output", str(out)],
                     "espsecure digest-sbv2-public-key")
            data = out.read_bytes()
        if len(data) != 32:
            raise SignError("espsecure produced a digest that is not 32 bytes")
        return data.hex()

    def sign(self, keyfile: Path, src: Path, dst: Path, append: bool = False) -> None:
        argv = [*self.espsecure, "sign-data", "--version", "2", "--keyfile", str(keyfile)]
        if append:
            argv.append("--append-signatures")
        argv += ["--output", str(dst), str(src)]
        self._ok(argv, "espsecure sign-data")

    def check_tool(self) -> None:
        r = self.run([*self.espsecure, "--help"])
        if ESPSECURE_BANNER not in (r.stdout or ""):
            raise SignError(f"ESPSECURE ({' '.join(self.espsecure)}) is not esptool 5.2.0's espsecure -- "
                            "pip install -r tools/factory/requirements.txt, or set ESPSECURE")

    def gate(self, image: Path, *extra: str) -> None:
        r = self.run([sys.executable, str(GATE), str(image), *extra])
        if r.returncode != 0:
            raise SignError(f"release gate refused {image.name}:\n{r.stdout}{r.stderr}")


def read_digests(path: Path) -> tuple[list[str], bool]:
    if not path.is_file():
        raise SignError(f"no trusted digests file {path} -- create esp32/security/ from the PUBLIC keys first "
                        f"(esp32/security/README.md)")
    text = path.read_text()
    digests = [ln.split("#", 1)[0].strip().lower() for ln in text.splitlines()]
    return [d for d in digests if d], "TEST ONLY" in text


def test_digests() -> set[str]:
    try:
        return set(read_digests(TEST_TRUSTED)[0])
    except SignError:
        return set()


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def check_key(signer: Signer, keyfile: Path, trusted: list[str], allow_test: bool, label: str) -> tuple[str, bool]:
    if not keyfile.is_file():
        raise SignError(f"{label} key {keyfile} not found")
    d = signer.key_digest(keyfile)
    is_test = d in test_digests()
    if is_test and not allow_test:
        raise SignError(f"{label} key is a TEST key ({d[:16]}...) -- refusing to sign a release with it")
    if d not in trusted:
        raise SignError(f"{label} key digest {d[:16]}... is not in the trusted digests -- wrong key, or "
                        f"esp32/security/sb_trusted_digests.txt is out of date")
    return d, is_test


def pilot_evidence_problems(evidence_rel: str, bootloader_sha256: str, repo: Path | None = None) -> list[str]:
    """Shared with tools/factory/secure_provision.py (same rule, both ends)."""
    repo = repo or REPO
    rel = Path(evidence_rel)
    if rel.is_absolute() or ".." in rel.parts:
        return [f"pilot evidence path {evidence_rel!r} must be relative to the repository"]
    path = (repo / rel).resolve()
    if (repo / "tools" / "quality-evidence").resolve() not in path.parents:
        return [f"pilot evidence {evidence_rel!r} is not under tools/quality-evidence/"]
    if not path.is_file():
        return [f"pilot evidence {evidence_rel} does not exist in this repository"]
    text = path.read_text(errors="replace")
    bad = []
    if not bootloader_sha256 or bootloader_sha256.lower() not in text.lower():
        bad.append(f"{evidence_rel} does not name this bundle's signed bootloader sha256 "
                   f"({(bootloader_sha256 or '?')[:16]}...) -- the pilot proved a different bootloader")
    if not any(ln.strip() == PILOT_PASS_LINE for ln in text.splitlines()):
        bad.append(f"{evidence_rel} has no line '{PILOT_PASS_LINE}'")
    return bad


def refuse_tracked_out_dir(out_dir: Path, repo: Path | None = None) -> None:
    repo = repo or REPO
    try:
        resolved = out_dir.resolve()
    except OSError:
        return
    if resolved != repo.resolve() and repo.resolve() not in resolved.parents:
        return  # outside the repository: fine
    probe = resolved / "app-signed.bin"
    r = subprocess.run(["git", "-C", str(repo), "check-ignore", "-q", str(probe)], capture_output=True)
    if r.returncode != 0:
        raise SignError(f"--out-dir {out_dir} is inside the repository and not gitignored -- signed images "
                        "carry the manifest HMAC key and must never be committed; use a directory outside "
                        "the repository (private storage)")


def update_bundle(out_dir: Path, updates: dict) -> Path:
    path = out_dir / "bundle.json"
    bundle = json.loads(path.read_text()) if path.is_file() else {}
    bundle.setdefault("idf_commit", IDF_COMMIT)
    bundle.setdefault("arduino_core", ARDUINO_CORE)
    bundle.setdefault("artifacts", {})
    for k, v in updates.items():
        if k == "artifacts":
            bundle["artifacts"].update(v)
        elif k == "test_signed":
            bundle["test_signed"] = bool(bundle.get("test_signed")) or v
        else:
            bundle[k] = v
    path.write_text(json.dumps(bundle, indent=2, sort_keys=True) + "\n")
    return path


def sign_app(a, signer: Signer) -> int:
    trusted, trusted_is_test = read_digests(a.trusted_digests)
    if trusted_is_test and not a.allow_test_key:
        raise SignError(f"{a.trusted_digests} is a TEST ONLY digests file -- pass --allow-test-key for a TEST run")
    signer.gate(a.input, "--expect-version", a.expect_version, "--profile", "release",
                *(["--forbid-version", a.forbid_version] if a.forbid_version else []))
    _, is_test = check_key(signer, a.keyfile, trusted, a.allow_test_key, "primary")
    a.out_dir.mkdir(parents=True, exist_ok=True)
    out = a.out_dir / ("app-signed-TEST.bin" if is_test else "app-signed.bin")
    if out.exists():
        out.unlink()
    signer.sign(a.keyfile, a.input, out)
    signer.gate(out, "--expect-version", a.expect_version, "--profile", "release",
                "--require-sbv2", "--trusted-digests", str(a.trusted_digests))
    bundle = update_bundle(a.out_dir, {
        "version": a.expect_version, "test_signed": is_test,
        "artifacts": {"app": {"file": out.name, "sha256": sha256(out), "size": out.stat().st_size,
                              "unsigned_sha256": sha256(a.input), "flash_offset": "0x10000"}},
    })
    print(f"[sign] {out} sha256 {sha256(out)} ({out.stat().st_size} B)")
    print(f"[sign] {bundle}")
    if is_test:
        print("*** TEST-SIGNED: never stage it, never give it to the factory. ***")
    return 0


def sign_bootloader(a, signer: Signer) -> int:
    trusted, trusted_is_test = read_digests(a.trusted_digests)
    if trusted_is_test and not a.allow_test_key:
        raise SignError(f"{a.trusted_digests} is a TEST ONLY digests file -- pass --allow-test-key for a TEST run")
    if a.backup_keyfile is None:
        raise SignError("the bootloader must be signed with BOTH keys: pass --backup-keyfile")
    size = a.input.stat().st_size
    if size > 0x7000:
        raise SignError(f"unsigned bootloader is {size:#x} bytes; it must be <= 0x7000 to fit below 0x8000 once signed")
    if size % 0x1000:
        raise SignError(f"unsigned bootloader is {size:#x} bytes, not a 4 KB multiple -- not a Secure Boot V2 "
                        "build of esp32/bootloader-release (signing would pad it and no RELEASED.md row could match)")
    dp, t1 = check_key(signer, a.keyfile, trusted, a.allow_test_key, "primary")
    db, t2 = check_key(signer, a.backup_keyfile, trusted, a.allow_test_key, "backup")
    if dp == db:
        raise SignError("primary and backup are the same key")
    is_test = t1 or t2
    # WHICH bootloader: a recorded esp32/bootloader-release build, approved
    # for this use, before any key touches it.
    min_status = "CANDIDATE" if is_test else ("PILOT" if a.pilot else "APPROVED")
    unsigned_sha = sha256(a.input)
    bad = gate_lib.approved_bootloader_problems(unsigned_sha, min_status, a.released_md)
    if bad:
        raise SignError("bootloader refused:\n  " + "\n  ".join(bad))
    row = gate_lib.release_row(unsigned_sha, a.released_md)
    a.out_dir.mkdir(parents=True, exist_ok=True)
    out = a.out_dir / ("bootloader-signed-TEST.bin" if is_test else "bootloader-signed.bin")
    with tempfile.TemporaryDirectory() as tmp:
        one = Path(tmp) / "bl-1.bin"
        signer.sign(a.keyfile, a.input, one)
        if out.exists():
            out.unlink()
        signer.sign(a.backup_keyfile, one, out, append=True)
    signer.gate(out, "--bootloader", "--trusted-digests", str(a.trusted_digests),
                "--released-md", str(a.released_md), "--min-status", min_status)
    update_bundle(a.out_dir, {
        "test_signed": is_test,
        "artifacts": {"bootloader": {"file": out.name, "sha256": sha256(out), "size": out.stat().st_size,
                                     "unsigned_sha256": unsigned_sha, "flash_offset": "0x0",
                                     "released_status": row["status"],
                                     "partition_table_sha256": row["partition_table_sha256"]}},
    })
    print(f"[sign] {out} sha256 {sha256(out)} ({out.stat().st_size} B, primary + backup; "
          f"RELEASED.md status {row['status']})")
    return 0


def add_plain(a, signer: Signer) -> int:
    """Records the unsigned factory pieces (partition table, boot_app0) in the
    bundle so the station can verify every byte it flashes, and the hardware
    pilot's evidence once it exists."""
    a.out_dir.mkdir(parents=True, exist_ok=True)
    if a.pilot_evidence:
        path = a.out_dir / "bundle.json"
        bundle = json.loads(path.read_text()) if path.is_file() else {}
        bl = bundle.get("artifacts", {}).get("bootloader", {}).get("sha256", "")
        if not bl:
            raise SignError("sign the bootloader into this bundle before recording the pilot evidence")
        bad = pilot_evidence_problems(a.pilot_evidence, bl)
        if bad:
            raise SignError("pilot evidence refused:\n  " + "\n  ".join(bad))
        update_bundle(a.out_dir, {"pilot": {"evidence": Path(a.pilot_evidence).as_posix(), "bootloader_sha256": bl}})
        print(f"[bundle] pilot evidence {a.pilot_evidence} (bootloader {bl[:16]}...)")
    arts = {}
    bundle_path = a.out_dir / "bundle.json"
    bl_art = (json.loads(bundle_path.read_text()) if bundle_path.is_file() else {}).get(
        "artifacts", {}).get("bootloader", {})
    for name, src, off in (("partition_table", a.partition_table, "0x8000"), ("boot_app0", a.boot_app0, "0xe000")):
        if src is None:
            continue
        if not src.is_file():
            raise SignError(f"no such {name.replace('_', ' ')} file: {src}")
        got = sha256(src)
        if name == "partition_table":
            # The table the bootloader was recorded with -- the SAME RELEASED.md row.
            if not bl_art.get("unsigned_sha256"):
                raise SignError("sign the bootloader into this bundle first: the partition table must be the one "
                                "RELEASED.md records with that bootloader")
            row = gate_lib.release_row(bl_art["unsigned_sha256"], a.released_md)
            if row is None or row["partition_table_sha256"] != got:
                raise SignError(f"partition-table.bin sha256 {got[:16]}... is not the table RELEASED.md records with "
                                f"this bundle's bootloader ({(row or {}).get('partition_table_sha256', '?')[:16]}...) -- "
                                "a wrong table is frozen into every toy for life")
        elif got != gate_lib.BOOT_APP0_SHA256:
            raise SignError(f"boot_app0.bin sha256 {got[:16]}... is not arduino-esp32 3.3.8's "
                            f"({gate_lib.BOOT_APP0_SHA256[:16]}...)")
        dst = a.out_dir / src.name
        if src.resolve() != dst.resolve():
            dst.write_bytes(src.read_bytes())
        arts[name] = {"file": dst.name, "sha256": sha256(dst), "size": dst.stat().st_size, "flash_offset": off}
    update_bundle(a.out_dir, {"artifacts": arts})
    for k, v in arts.items():
        print(f"[bundle] {k}: {v['file']} sha256 {v['sha256']}")
    return 0


def main(argv=None, signer: Signer | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name in ("app", "bootloader"):
        p = sub.add_parser(name)
        p.add_argument("--keyfile", type=Path, required=True, help="PRIMARY Secure Boot V2 private key (PEM)")
        p.add_argument("--input", type=Path, required=True)
        p.add_argument("--out-dir", type=Path, required=True)
        p.add_argument("--trusted-digests", type=Path, default=DEFAULT_TRUSTED)
        p.add_argument("--allow-test-key", action="store_true",
                       help="permit a TEST key (outputs are named *-TEST.bin, bundle says test_signed)")
        if name == "app":
            p.add_argument("--expect-version", required=True)
            p.add_argument("--forbid-version")
        else:
            p.add_argument("--backup-keyfile", type=Path, help="BACKUP key (required)")
            p.add_argument("--pilot", action="store_true",
                           help="sign a RELEASED.md row whose Status is PILOT (for the 2 sacrificial pilot boards)")
            p.add_argument("--released-md", type=Path, default=gate_lib.RELEASED_MD)
    p = sub.add_parser("add")
    p.add_argument("--out-dir", type=Path, required=True)
    p.add_argument("--released-md", type=Path, default=gate_lib.RELEASED_MD)
    p.add_argument("--partition-table", type=Path)
    p.add_argument("--boot-app0", type=Path)
    p.add_argument("--pilot-evidence", help="repo-relative tools/quality-evidence/... file from the hardware pilot")
    a = ap.parse_args(argv)
    signer = signer or Signer()
    try:
        refuse_tracked_out_dir(a.out_dir)
        if a.cmd != "add":
            if not a.input.is_file():
                raise SignError(f"no such input: {a.input}")
            signer.check_tool()
        return {"app": sign_app, "bootloader": sign_bootloader, "add": add_plain}[a.cmd](a, signer)
    except SignError as e:
        print(f"FAIL - {e}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
