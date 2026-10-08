#!/usr/bin/env python3
"""Source-level rules the chip-security design depends on (stdlib, no toolchain).

    python3 tools/firmware/test_firmware_source_rules.py -v

Each rule is something a compiler cannot catch and a reviewer can miss:
  * app NVS is opened ONLY through areg_prefs_begin(): a Preferences
    object's .begin( appears only in secure_store.cpp (a direct begin() before
    secure_store_begin() would PLAIN-init -- and on a locked toy destroy --
    the encrypted nvs_sec partition);
  * the NVS partition calls (init / secure init / erase / deinit) live only in
    secure_store.cpp, and nothing ever asks NVS to GENERATE keys (that would
    try to burn an eFuse from the app);
  * the firmware never reads its console (Serial.read / available /
    readString...): USB-Serial-JTAG stays enabled on a locked toy as an
    OUTPUT-only console, so input handling would be an attack surface;
  * the release guard is actually included, and the version marker exists
    exactly once in the sources;
  * (review 2026-10-08) a RELEASE image talks TLS only -- areg_http_begin
    refuses a non-https URL and the plain-TCP audio stream class
    (ESP8266Audio's AudioFileSourceHTTPStream: no TLS even for an https://
    URL) is not even included in a release build -- trusts >= 3 root CAs,
    stays offline and refuses BLE setup while ROM download mode is still open
    (a half-secured toy), never purges the default nvs in legacy (old-table)
    mode, and retires the older image in the inactive OTA slot only after
    the check-in confirmed the running one;
  * (review round 3) a fresh OTA image stays PENDING_VERIFY until the
    check-in (verifyRollbackLater() == true), and a failed / abandoned BLE
    setup reboots so the plaintext Wi-Fi copy is purged at once.
"""
import re
import unittest
from pathlib import Path

SKETCH = Path(__file__).resolve().parents[2] / "esp32" / "AregVoiceMvp"
SOURCES = sorted(p for p in SKETCH.iterdir() if p.suffix in (".cpp", ".h", ".ino", ".c"))


def code_of(path: Path) -> str:
    """Source without // and /* */ comments; string and character literals are
    kept intact (a "https://" inside a literal is not a comment)."""
    text = path.read_text(encoding="utf-8", errors="replace")
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        if c in "\"'":
            j = i + 1
            while j < n and text[j] != c and text[j] != "\n":
                j += 2 if text[j] == "\\" else 1
            out.append(text[i:j + 1])
            i = j + 1
        elif text.startswith("//", i):
            j = text.find("\n", i)
            i = n if j < 0 else j
            out.append(" ")
        elif text.startswith("/*", i):
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            out.append(" ")
        else:
            out.append(c)
            i += 1
    return "".join(out)


class FirmwareSourceRules(unittest.TestCase):
    def test_sources_found(self):
        self.assertGreater(len(SOURCES), 40)

    def test_preferences_begin_only_in_secure_store(self):
        offenders = []
        for p in SOURCES:
            code = code_of(p)
            names = set(re.findall(r"\bPreferences\s+(\w+)\s*;", code)) | {"prefs", "p"}
            for n in names:
                if re.search(rf"\b{n}\.begin\s*\(", code) and p.name != "secure_store.cpp":
                    offenders.append(f"{p.name}: {n}.begin(")
        self.assertEqual(offenders, [], "open app NVS with areg_prefs_begin(), never Preferences::begin()")

    def test_nvs_partition_calls_only_in_secure_store(self):
        pat = re.compile(r"\bnvs_flash_(init_partition|secure_init_partition|erase_partition|deinit_partition|"
                         r"init|erase|deinit)\s*\(")
        offenders = [f"{p.name}: {m.group(0)}" for p in SOURCES if p.name != "secure_store.cpp"
                     for m in pat.finditer(code_of(p))]
        self.assertEqual(offenders, [])

    def test_never_generate_nvs_keys(self):
        offenders = [p.name for p in SOURCES if re.search(r"nvs_flash_generate_keys", code_of(p))]
        self.assertEqual(offenders, [], "the factory burns the HMAC key; the app must never generate one")

    def test_no_console_input(self):
        pat = re.compile(r"\bSerial\.(read|available|readString|readStringUntil|readBytes|readBytesUntil|"
                         r"parseInt|parseFloat|find|findUntil|peek)\s*\(")
        offenders = [f"{p.name}: {m.group(0)}" for p in SOURCES for m in pat.finditer(code_of(p))]
        self.assertEqual(offenders, [], "the console is output-only on a locked toy")

    def test_release_guard_is_included_by_the_sketch(self):
        ino = (SKETCH / "AregVoiceMvp.ino").read_text(encoding="utf-8", errors="replace")
        self.assertIn('#include "security_profile.h"', ino)
        self.assertLess(ino.index("secure_store_begin();"), ino.index("#ifdef AREG_PROVISION_IDENTITY_ONCE"),
                        "the store must open before any AREG_PROVISION_* burn")
        self.assertLess(ino.index("WiFi.persistent(false);"), ino.index("WiFi.onEvent("))

    def test_marker_magic_spelled_once(self):
        hits = [p.name for p in SOURCES if "AREGFWV1:" in code_of(p)]
        self.assertEqual(hits, ["fw_version_marker.cpp"], "any second literal is a second marker in the image")

    def test_shared_pop_only_in_dev_builds(self):
        ble = (SKETCH / "ble_provisioning.cpp").read_text(encoding="utf-8", errors="replace")
        self.assertIn("#if !AREG_IS_RELEASE && !defined(AREG_PROV_POP)", ble)
        self.assertIn("[prov] no per-device PoP - BLE setup refused", ble)


class ReviewFixSourceRules(unittest.TestCase):
    """Rules from the 2026-10-08 chip-security review."""

    def code(self, name: str) -> str:
        return code_of(SKETCH / name)

    def test_release_http_is_https_only(self):
        net = self.code("net_transport.cpp")
        allowed = net[net.index("bool areg_url_allowed("):net.index("bool areg_http_begin(")]
        rel = allowed[allowed.index("#if AREG_IS_RELEASE"):allowed.index("#else")]
        self.assertIn('strncmp(url, "https://", 8) == 0', rel)
        begin = net[net.index("bool areg_http_begin("):]
        self.assertLess(begin.index("areg_url_allowed(url.c_str())"), begin.index("http.begin(url)"),
                        "the URL policy runs before any plain-http begin")
        self.assertLess(begin.index("areg_backend_allowed()"), begin.index("http.begin("))

    @staticmethod
    def release_view(code: str) -> str:
        """`code` as the preprocessor sees it with AREG_IS_RELEASE == 1 (and no
        bench flags): keeps the #if/#elif/#else branches a release build
        compiles. Handles the conditions this file uses: AREG_IS_RELEASE,
        !AREG_IS_RELEASE, #ifdef/#ifndef X (X undefined)."""
        def truth(cond: str) -> bool:
            cond = cond.strip()
            if cond in ("AREG_IS_RELEASE", "AREG_IS_RELEASE != 0"):
                return True
            if cond in ("!AREG_IS_RELEASE", "AREG_IS_RELEASE == 0"):
                return False
            return False  # any other macro: undefined in a release build
        out, stack = [], []  # stack of (this_branch_live, any_branch_taken, parent_live)
        live = True
        for line in code.splitlines():
            s = line.strip()
            m = re.match(r"#\s*(if|ifdef|ifndef|elif|else|endif)\b(.*)", s)
            if not m:
                if live:
                    out.append(line)
                continue
            kw, rest = m.group(1), m.group(2)
            if kw in ("if", "ifdef", "ifndef"):
                val = truth(rest) if kw == "if" else (False if kw == "ifdef" else True)
                stack.append([live and val, val, live])
                live = live and val
            elif kw == "elif":
                top = stack[-1]
                val = (not top[1]) and truth(rest)
                top[0], top[1] = top[2] and val, top[1] or val
                live = top[0]
            elif kw == "else":
                top = stack[-1]
                val = not top[1]
                top[0], top[1] = top[2] and val, True
                live = top[0]
            else:
                top = stack.pop()
                live = top[2]
        return "\n".join(out)

    def test_release_build_never_reaches_the_plain_tcp_audio_stream(self):
        # Review round 3: AudioFileSourceHTTPStream holds a plain NetworkClient;
        # HTTPClient::begin(client, "https://...") only sets port 443 -- no TLS.
        # An https-only URL check therefore protected nothing. In a RELEASE
        # build the class must be neither included nor constructed.
        audio = self.code("audio_io.cpp")
        rel = self.release_view(audio)
        self.assertNotIn("AudioFileSourceHTTPStream", rel)
        self.assertNotIn("AudioFileSourceICYStream", rel)
        for name in ("audio_play_story_stream", "audio_play_qa_stream"):
            fn = rel[rel.index(f"bool {name}("):]
            fn = fn[:fn.index("\n}\n")]
            self.assertIn("return false;", fn, f"{name} must refuse in a release build")
        story = rel[rel.index("bool audio_play_story_stream("):]
        story = story[:story.index("\n}\n")]
        self.assertIn("*out_open_failed = true;", story, "the caller ends the session (open failure)")
        # The DEV build still has both streams (bench / Wi-Fi fallback test).
        dev_starts = [i for i in range(len(audio)) if audio.startswith("AudioFileSourceHTTPStream http(", i)]
        self.assertEqual(len(dev_starts), 2, "story stream + Q&A stream (DEV only)")
        # No other source file may construct an HTTP audio stream.
        others = [p.name for p in SOURCES if p.name != "audio_io.cpp"
                  and re.search(r"AudioFileSource(HTTPStream|ICYStream)", code_of(p))]
        self.assertEqual(others, [])
        # A release image fetches no story token for a stream it cannot play.
        ino = self.code("AregVoiceMvp.ino")
        self.assertIn("bool have_token = (use_sd || AREG_IS_RELEASE)", ino)
        # And the gate refuses an image that links the class anyway.
        gate = (Path(__file__).resolve().parent / "check_release_image.py").read_text()
        self.assertTrue("Can't open HTTP request" in gate, "check_release_image.py RELEASE_FORBIDDEN lacks the stream string")

    def test_release_view_helper(self):
        src = "a\n#if AREG_IS_RELEASE\nrel\n#elif X\nx\n#else\ndev\n#endif\n#ifdef Y\ny\n#endif\n#if !AREG_IS_RELEASE\nd2\n#endif\nz"
        self.assertEqual(self.release_view(src).split("\n"), ["a", "rel", "z"])

    def test_release_trusts_at_least_three_root_cas(self):
        src = (SKETCH / "tls_trust_anchors.cpp").read_text(encoding="utf-8")
        hdr = (SKETCH / "tls_trust_anchors.h").read_text(encoding="utf-8")
        n = int(re.search(r"#define AREG_CA_ANCHOR_COUNT (\d+)", hdr).group(1))
        pem_lines = re.findall(r'^\s*"-----BEGIN CERTIFICATE-----\\n"\s*$', src, re.M)
        self.assertEqual(len(pem_lines), n)
        self.assertGreaterEqual(n, 3)
        prof = (SKETCH / "security_profile.h").read_text(encoding="utf-8")
        self.assertIn("AREG_CA_ANCHOR_COUNT < 3", prof)
        self.assertIn('#include "tls_trust_anchors.h"', prof)
        net = self.code("net_transport.cpp")
        self.assertEqual(re.findall(r"setCACert\w*\(([^)]*)\)", net), ["kAregTlsTrustAnchorsPem"])

    def test_half_secured_release_toy_stays_offline(self):
        net = self.code("net_transport.cpp")
        fn = net[net.index("bool areg_backend_allowed()"):net.index("bool areg_url_allowed(")]
        self.assertLess(fn.index("security_network_allowed()"), fn.index("voice_device_identity_ready()"))
        rules = self.code("security_posture_rules.h")
        self.assertIn("return !release_profile || dl == DlMode::Disabled;", rules)
        ble = self.code("ble_provisioning.cpp")
        rel = ble[ble.index("#if AREG_IS_RELEASE", ble.index("void ble_provisioning_begin()")):]
        self.assertLess(rel.index("security_network_allowed()"), rel.index("device_creds_pop_load("),
                        "a half-secured toy never opens BLE setup (no family Wi-Fi ever reaches it)")
        posture = self.code("security_posture.cpp")
        self.assertIn("factory not finished (download mode open) - offline, BLE setup disabled",
                      (SKETCH / "security_posture.cpp").read_text(encoding="utf-8"),
                      "the exact line factory step 8 requires (secure_provision.FACTORY_WINDOW_LINE)")
        self.assertIn("ESP_EFUSE_DIS_DOWNLOAD_MODE", posture)

    def test_legacy_store_mode_never_purges_and_uses_the_default_nvs(self):
        ss = self.code("secure_store.cpp")
        prefs = ss[ss.index("bool areg_prefs_begin("):ss.index("bool secure_store_request_default_nvs_purge(")]
        self.assertIn("store_is_default_nvs(s_mode)", prefs)
        self.assertIn("p.begin(ns, read_only)", prefs)
        req = ss[ss.index("bool secure_store_request_default_nvs_purge("):ss.index("bool secure_store_wipe(")]
        self.assertIn("purge_allowed(s_mode)", req)
        purge = ss[ss.index("void run_purge_check()"):ss.index("bool secure_store_begin()")]
        self.assertIn("purge_allowed(s_mode)", purge)
        self.assertIn('ESP_PARTITION_SUBTYPE_DATA_NVS', ss)

    def test_ota_image_stays_pending_verify_until_the_check_in(self):
        # Review round 3: Arduino 3.3.8's initArduino() marks a PENDING_VERIFY
        # image VALID before setup() unless this weak hook returns true.
        ota = self.code("ota_foundation.cpp")
        self.assertRegex(ota, r'extern\s+"C"\s+bool\s+verifyRollbackLater\s*\(\s*(void)?\s*\)\s*\{\s*return\s+true;\s*\}')
        hits = [p.name for p in SOURCES if re.search(r"\bverifyRollbackLater\b", code_of(p))]
        self.assertEqual(hits, ["ota_foundation.cpp"], "exactly one strong definition")
        self.assertIn("esp_ota_mark_app_valid_cancel_rollback()", ota)
        # An image PENDING_VERIFY with no OTA record cannot prove itself (e.g. a
        # locked-fleet image whose store an unsecured chip refuses): roll back,
        # never confirm it blindly.
        boot = ota[ota.index("static void ota_boot_init()"):ota.index("bool ota_outcome_pending()")]
        orphan = boot[boot.index("img_state == ESP_OTA_IMG_PENDING_VERIFY && s_ota.state != OTA_STATE_REBOOTING"):]
        orphan = orphan[:orphan.index("ota_retire_inactive_slot(")]
        self.assertLess(orphan.index("esp_ota_mark_app_invalid_rollback_and_reboot()"),
                        orphan.index("esp_ota_mark_app_valid_cancel_rollback()"),
                        "roll back first; keep the image only when there is nothing to roll back to")

    def test_failed_ble_setup_reboots_to_purge_the_plaintext_wifi_copy(self):
        ble = self.code("ble_provisioning.cpp")
        recv = ble[ble.index("case ARDUINO_EVENT_PROV_CRED_RECV:"):ble.index("case ARDUINO_EVENT_PROV_CRED_FAIL:")]
        self.assertIn("secure_store_request_default_nvs_purge()", recv)
        self.assertIn("s_purge_pending = true;", recv)
        end = ble[ble.index("case ARDUINO_EVENT_PROV_END:"):]
        self.assertIn("s_ended = true;", end[:end.index("break;")])
        due = ble[ble.index("bool ble_provisioning_purge_reboot_due()"):]
        self.assertIn("s_succeeded", due[:due.index("\n}\n")])
        ino = self.code("AregVoiceMvp.ino")
        i = ino.index("if (ble_provisioning_purge_reboot_due())")
        block = ino[i:ino.index("}", i)]
        self.assertLess(block.index("esp_wifi_restore()"), block.index("esp_restart()"))

    def test_inactive_ota_slot_retired_only_after_confirm(self):
        ota = self.code("ota_foundation.cpp")
        fn = ota[ota.index("static void ota_retire_inactive_slot("):ota.index("static void ota_boot_init()")]
        self.assertIn("esp_partition_read_raw(", fn, "a decrypting read would see garbage, not 0xFF")
        self.assertNotIn("esp_partition_read(", fn)
        self.assertIn("s_ota.state == OTA_STATE_REBOOTING", fn, "never while the old image is the rollback target")
        confirmed = ota.index("s_ota.state = OTA_STATE_CONFIRMED;")
        retire = ota.index('ota_retire_inactive_slot("check-in confirmed")')
        failed = ota.index('"[ota] check-in ack failed')
        self.assertLess(ota.index("esp_ota_mark_app_valid_cancel_rollback()"), confirmed)
        self.assertTrue(confirmed < retire < failed, "retired only on the confirmed (2xx ack) branch")
        boot = ota[ota.index("static void ota_boot_init()"):ota.index("bool ota_outcome_pending()")]
        self.assertIn('ota_retire_inactive_slot("boot")', boot)


if __name__ == "__main__":
    unittest.main()
