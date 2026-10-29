#!/usr/bin/env python3
"""Tests for preflight_checks.py. Run: python3 tools/release/test_preflight_checks.py -v

Every secret-shaped fixture is assembled at runtime from fragments, so this file
itself contains no contiguous secret-shaped string and the repo-wide scan stays
clean without a path exclusion.
"""
import io
import json
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import preflight_checks as pc  # noqa: E402

RANDOM48 = "Q7mZk2VbX9" "nR4tYc8LpA3" "wE6uJh1GdS5" "oF0iKxMvB2" "yT"  # not fixture-shaped
OPENAI = "sk-" + RANDOM48
PROJ = "sk-" + "proj-" + RANDOM48 + "_" + RANDOM48[:20]
GOOGLE = "AI" + "za" + "Sy" + "D3k9Zq1WmXv7Rt2LpB6nYc8Hd4Ge0JfUa"[:33]
DEVKEY = "dtk" + "_" + "9f8e7d6c5b4a39281706f5e4d3c2b1a0"
ELEVEN = "sk" + "_" + "3fa91c0de57b48e6a2d1c9b7f4e05a83c6d2b19e4f7a0c58"
TELEGRAM = "7203948156" + ":" + "AAH3kLm9QzXv2RtYc8LpBnW6uJh1GdS5oF0"
PEM = "-----BEGIN " + "RSA PRIVATE KEY-----"
BEARER = "Bearer " + "eyAbCdEf90Zq3Lm7WxVu2Rt"

DOTNET_OK = ("Passed!  - Failed:     0, Passed:  3197, Skipped:     0, "
             "Total:  3197, Duration: 38 s - ArmenianAiToy.Application.Tests.dll (net10.0)\n")


def scan_one(rel, content):
    with tempfile.TemporaryDirectory() as d:
        p = Path(d, rel)
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(content, encoding="utf-8")
        return pc.secret_scan(Path(d))


class TestSummary(unittest.TestCase):
    def test_single_assembly(self):
        self.assertEqual(pc.parse_test_summary(DOTNET_OK), (3197, 0, 0))

    def test_sums_assemblies_and_counts_failures_and_skips(self):
        log = (DOTNET_OK + "Failed!  - Failed:     2, Passed:    98, Skipped:     1, "
               "Total:   101, Duration: 3 s - Other.Tests.dll (net10.0)\n")
        self.assertEqual(pc.parse_test_summary(log), (3298, 2, 1))

    def test_ansi_colour_is_ignored(self):
        self.assertEqual(pc.parse_test_summary("\x1b[32m" + DOTNET_OK + "\x1b[0m"), (3197, 0, 0))

    def test_testing_platform_format(self):
        log = "Test summary: total: 12, failed: 1, succeeded: 10, skipped: 1, duration: 2s\n"
        self.assertEqual(pc.parse_test_summary(log), (12, 1, 1))

    def test_no_totals_is_none(self):
        self.assertIsNone(pc.parse_test_summary("Build succeeded.\n"))


class ClaudeMdCount(unittest.TestCase):
    MD = "## Build & test\n\n```bash\ncd backend\ndotnet build\ndotnet test            # 3197 tests, ~35 s in Release\n```\n"

    def test_documented_count_and_line(self):
        self.assertEqual(pc.documented_test_count(self.MD), (3197, 6))

    def test_comma_separated_count(self):
        self.assertEqual(pc.documented_test_count("dotnet test # 3,197 tests"), (3197, 1))

    def test_match(self):
        code, msg = pc.check_claude_md_count(self.MD, DOTNET_OK)
        self.assertEqual(code, 0, msg)

    def test_mismatch_names_the_number_to_write(self):
        code, msg = pc.check_claude_md_count(self.MD, DOTNET_OK.replace("3197", "3200"))
        self.assertEqual(code, 1)
        self.assertIn("line 6", msg)
        self.assertIn("set it to 3200", msg)

    def test_unreadable_inputs_are_code_2(self):
        self.assertEqual(pc.check_claude_md_count("no such line", DOTNET_OK)[0], 2)
        self.assertEqual(pc.check_claude_md_count(self.MD, "Build succeeded.")[0], 2)

    def test_size_within_budget_passes(self):
        code, msg = pc.check_claude_md_size(20_000)
        self.assertEqual(code, 0)
        self.assertIn("19.5 KB", msg)

    def test_size_over_budget_fails_and_says_where_to_move_text(self):
        code, msg = pc.check_claude_md_size(70_958)
        self.assertEqual(code, 1)
        self.assertIn("69.3 KB", msg)
        self.assertIn("CLAUDE-history.md", msg)

    def test_real_claude_md_is_within_budget(self):
        real = Path(__file__).resolve().parents[2] / "CLAUDE.md"
        self.assertEqual(pc.check_claude_md_size(real.stat().st_size)[0], 0)

    def test_real_claude_md_has_a_parsable_line(self):
        md = (Path(__file__).resolve().parents[2] / "CLAUDE.md").read_text(encoding="utf-8")
        self.assertIsNotNone(pc.documented_test_count(md))


class SecretScan(unittest.TestCase):
    def test_clean_tree_passes(self):
        findings, scanned = scan_one("a.txt", "nothing to see here\nBearer some-words-without-digits-in-them\n")
        self.assertEqual(findings, [])
        self.assertEqual(scanned, 1)

    def test_planted_secrets_are_caught_with_file_line_and_rule(self):
        cases = {"openai-key": OPENAI, "openai-key ": PROJ, "google-api-key": GOOGLE,
                 "device-key": DEVKEY, "elevenlabs-key": ELEVEN, "telegram-bot-token": TELEGRAM,
                 "private-key-block": PEM, "bearer-token": BEARER}
        for rule, secret in cases.items():
            with self.subTest(rule=rule):
                findings, _ = scan_one("src/leak.txt", f"line one\nx = '{secret}'\n")
                self.assertEqual([(f[0], f[1], f[2]) for f in findings],
                                 [("src/leak.txt", 2, rule.strip())])

    def test_matched_value_is_never_printed(self):
        with tempfile.TemporaryDirectory() as d:
            Path(d, "leak.txt").write_text(f"key = {OPENAI}\n", encoding="utf-8")
            buf = io.StringIO()
            with redirect_stdout(buf):
                code = pc.main(["secret-scan", "--root", d])
        self.assertEqual(code, 1)
        self.assertIn("openai-key", buf.getvalue())
        self.assertNotIn(OPENAI, buf.getvalue())
        self.assertNotIn(RANDOM48[:12], buf.getvalue())

    def test_fixture_words_and_allow_marker_are_ignored(self):
        findings, _ = scan_one("t.cs", 'const string k = "sk-test-SECRETVALUE-123456";\n'
                                       'var x = "sk-proj-ABCDEFGH12345678";\n'
                                       f"real = {OPENAI}  // preflight:allow-secret\n")
        self.assertEqual(findings, [])

    def test_credential_files_by_name(self):
        for name in (".env", ".env.production", "tls/server.pem", "signing.key", "credentials.json",
                     "gcp/service-account-prod.json"):
            with self.subTest(name=name):
                findings, _ = scan_one(name, "x\n")
                self.assertTrue(any(f[2].startswith("credential-file") for f in findings), findings)
        for ok in (".env.example", "config.h.example", "README.md"):
            with self.subTest(name=ok):
                self.assertEqual(scan_one(ok, "x\n")[0], [])

    def test_appsettings_secret_literals(self):
        bad = json.dumps({"OpenAI": {"ApiKey": "live-value-here"}, "Jwt": {"Key": "Zq7mK2" * 7}})
        findings, _ = scan_one("backend/appsettings.json", bad)
        self.assertEqual(sorted(f[2] for f in findings),
                         ["config-secret-literal (Jwt:Key)", "config-secret-literal (OpenAI:ApiKey)"])
        good = json.dumps({"OpenAI": {"ApiKey": ""}, "//SigningKey": "SECURITY: set via env",
                           "Game": {"GameKey": "button-simon"}})
        self.assertEqual(scan_one("backend/appsettings.json", "﻿" + good)[0], [])

    def test_header_credential_defines(self):
        findings, _ = scan_one("fw/config.h", '#define AREG_WIFI_PASSWORD "hunter2hunter2"\n')
        self.assertEqual([f[2] for f in findings], ["header-credential-literal"])
        clean = ('#define AREG_WIFI_PASSWORD ""\n#define AREG_DEVICE_API_KEY "YOUR_DEVICE_API_KEY"\n'
                 '#define AREG_PROV_POP "areg-pair"\n#define AREG_MANIFEST_HMAC_KEY ""\n')
        self.assertEqual(scan_one("fw/config.h", clean)[0], [])

    def test_google_key_inside_a_base64_run_is_not_a_finding(self):
        # Generated pages embed audio as base64; random bytes once spelled "AIza"
        # plus 35 more characters (2026-10-29). A real key stands alone.
        run = "QQjBKBiNOMB0xV" + GOOGLE + "L9MuABoYFE06x6" + "A" * 200
        findings, _ = scan_one("page.html", 'const DATA = [{"audio": "' + run + '"}];\n')
        self.assertEqual(findings, [])

    def test_google_key_is_still_caught_when_delimited(self):
        for text in (f'key = "{GOOGLE}"\n', f"key={GOOGLE}\n", f"{GOOGLE}\n", f"x: {GOOGLE}, y: 1\n"):
            with self.subTest(text=text[:8]):
                findings, _ = scan_one("src/leak.txt", text)
                self.assertEqual([f[2] for f in findings], ["google-api-key"])

    def test_binary_files_are_skipped(self):
        with tempfile.TemporaryDirectory() as d:
            Path(d, "a.mp3").write_bytes(OPENAI.encode())
            Path(d, "b.dat").write_bytes(b"\0\0" + OPENAI.encode())
            self.assertEqual(pc.secret_scan(Path(d))[0], [])


class HelperOutputLineEndings(unittest.TestCase):
    def test_helper_prints_bare_lf_even_on_windows(self):
        # preflight.sh does `read -r total failed skipped` on this line; a trailing
        # carriage return turned "0" into "0\r" and a green run into a FAIL.
        import subprocess
        with tempfile.TemporaryDirectory() as d:
            log = Path(d, "t.log")
            log.write_text(DOTNET_OK, encoding="utf-8")
            proc = subprocess.run([sys.executable, str(Path(pc.__file__)), "test-summary", "--log", str(log)],
                                  capture_output=True)
        self.assertEqual(proc.returncode, 0)
        self.assertEqual(proc.stdout, b"3197 0 0\n")


if __name__ == "__main__":
    unittest.main()
