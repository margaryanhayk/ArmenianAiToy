#!/usr/bin/env python3
"""Refuse a firmware image that must not be released.

WHY THIS EXISTS. On 2026-08-13 a 1.2.1 image was built on the release machine
and pushed to the repo carrying the owner's REAL device id and API key
(`dtk_<32 hex>`, the exact format DeviceService mints). The rule against that
was already written down — the 1.1.x work established that OTA images ship
with PLACEHOLDER credentials, "because an OTA image reaches every toy, and one
toy's secret must never ride inside it" — and it was written down in prose, in
a runbook, as a step a human performs. It was skipped on the first release
after it was written.

Had it shipped: `device_creds` reads NVS first and falls back to the compiled
values, so every factory-fresh or re-flashed toy that installed the image would
have authenticated to the backend as that one toy.

So the check is a program now, not a paragraph. Dependency-free — no ffmpeg, no
dotnet, no Arduino toolchain — because a check that needs a toolchain is the
check that gets skipped on the day it matters. Same reasoning, and the same
shape, as tools/story-audio/check_story_audio.py.

It never prints a secret it finds. It reports the KIND and the COUNT, because
the whole point is that the value must not travel any further.

USAGE
    python3 tools/firmware/check_release_image.py <image.bin> [--expect-version 1.2.1]
                                                 [--forbid-version 1.2.0]

Exit code 0 = safe to stage. Non-zero = do not release.
"""
import argparse
import re
import sys
from pathlib import Path

# Printable ASCII runs, the same thing `strings` extracts.
STRING_RE = re.compile(rb"[\x20-\x7e]{4,}")

# A device API key. DeviceService.cs mints `dtk_{Guid:N}` — 32 hex chars.
DEVICE_KEY_RE = re.compile(r"^dtk_[0-9a-fA-F]{32}$")

# A device id. Also matches BLE service UUIDs, which is why a hit is reported
# rather than silently ignored: an operator looks once and decides, instead of
# the tool guessing and being wrong in the unsafe direction.
GUID_RE = re.compile(
    r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")

# What a correctly-built OTA image carries instead of real credentials.
PLACEHOLDERS = ("YOUR_DEVICE_GUID", "YOUR_DEVICE_API_KEY")

# The 8 MB whole-flash artifact. Serving it over OTA writes a bootloader and a
# partition table into a 3 MB app slot; the runbook says it would produce an
# unbootable toy. Size alone catches it long before anything else does.
OTA_SLOT_BYTES = 3 * 1024 * 1024


def extract_strings(data: bytes) -> list[str]:
    return [m.group().decode("ascii") for m in STRING_RE.finditer(data)]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("image", type=Path)
    ap.add_argument("--expect-version",
                    help="version string that MUST be present (e.g. 1.2.1)")
    ap.add_argument("--forbid-version",
                    help="version string that must be ABSENT (e.g. the one "
                         "being replaced) — catches the config.h override trap "
                         "where a -D flag is silently ignored and the old "
                         "version ships under a new name")
    args = ap.parse_args()

    if not args.image.is_file():
        print(f"FAIL - no such file: {args.image}")
        return 2

    data = args.image.read_bytes()
    strings = extract_strings(data)
    size = len(data)
    failures: list[str] = []
    notes: list[str] = []

    print(f"image   {args.image}")
    print(f"size    {size:,} B ({size / OTA_SLOT_BYTES * 100:.1f}% of the "
          f"{OTA_SLOT_BYTES:,} B OTA slot)")

    if size > OTA_SLOT_BYTES:
        failures.append(
            f"larger than the OTA slot ({size:,} > {OTA_SLOT_BYTES:,}). This is "
            f"probably AregVoiceMvp.ino.merged.bin — the app-only image is "
            f"AregVoiceMvp.ino.bin.")

    keys = [s for s in strings if DEVICE_KEY_RE.match(s)]
    if keys:
        failures.append(
            f"{len(keys)} device API key(s) compiled in (dtk_...). An OTA image "
            f"reaches every toy; one toy's credential must never ride inside "
            f"it. Set AREG_DEVICE_API_KEY back to the config.h.example "
            f"placeholder and rebuild. Treat the key as COMPROMISED and revoke "
            f"it — the value is not printed here, look in your own config.h.")

    # Well-known LIBRARY constants that are GUID-shaped by nature. These are
    # identical in every firmware in the world that uses the library, carry no
    # secret, and would otherwise fail every BLE-enabled release forever.
    # Keep this list EXACT-VALUE only -- never a pattern, never a flag an
    # operator could wave a real device id through. First entry added for
    # release 1.3.4 (2026-09-02), the first gated release with BLE
    # provisioning compiled in.
    KNOWN_LIBRARY_GUIDS = {
        # Espressif WiFiProv BLE provisioning service UUID (esp_prov).
        "258eafa5-e914-47da-95ca-c5ab0dc85b11",
    }
    guids = [s for s in strings
             if GUID_RE.match(s) and s.lower() not in KNOWN_LIBRARY_GUIDS]
    if guids:
        failures.append(
            f"{len(guids)} GUID-shaped string(s) compiled in. If any is the "
            f"toy's device id, the same rule applies as for the key. BLE "
            f"service UUIDs are also GUID-shaped — check which this is before "
            f"overriding, and if it is legitimate say so in the release notes.")

    # Wi-Fi credentials. Same rule as the device key and it was missed for the
    # same reason: config.h compiles AREG_WIFI_SSID / AREG_WIFI_PASSWORD in as
    # a fallback, so every image built on a bench that has a working toy
    # carries a HOME network's name and password in plaintext -- and those
    # images are committed to a git repo and served to every toy that polls.
    # Found 2026-08-14: the staged image AND the shipped field image both had
    # one. A factory-fresh toy installing that image would also try to join
    # someone else's house.
    #
    # The gate cannot know the password, so it checks the shape instead: an
    # image built from config.h.example has EMPTY Wi-Fi strings, and the SSID
    # of a real home router is recognisable. This catches the common vendor
    # prefixes; it is a net, not a proof, which is why the release still ends
    # with a human reading the diff.
    wifi_markers = [s for s in strings
                    if re.match(r"^(OVIO|TP-Link|Ucom|Rostelecom|Beeline|MTS|"
                                r"KTV|Team|VivaCell|Telecom)[-_ ]?[A-Za-z0-9_-]{2,}$", s)]
    if wifi_markers:
        failures.append(
            f"{len(wifi_markers)} probable Wi-Fi SSID(s) compiled in. An OTA "
            f"image reaches every toy, so a home network's name and password "
            f"must never ride inside it — a factory-fresh toy would try to "
            f"join that house. Set AREG_WIFI_SSID / AREG_WIFI_PASSWORD back to "
            f"the config.h.example placeholders and provision Wi-Fi onto the "
            f"toy instead. Values are not printed here.")

    present_placeholders = [p for p in PLACEHOLDERS if p in strings]
    if present_placeholders:
        notes.append(f"placeholders present: {', '.join(present_placeholders)}")

    if args.expect_version:
        if args.expect_version in strings:
            notes.append(f"version {args.expect_version} present")
        else:
            failures.append(
                f"expected version {args.expect_version} is NOT in the image. "
                f"config.h uses a plain #define and is included first, so a -D "
                f"build flag is silently overridden — edit config.h.")

    if args.forbid_version:
        if args.forbid_version in strings:
            failures.append(
                f"the OLD version {args.forbid_version} is still in the image. "
                f"The build did not pick up the new version; staging this would "
                f"serve the old firmware under the new manifest.")
        else:
            notes.append(f"old version {args.forbid_version} absent")

    for n in notes:
        print(f"  ok    {n}")
    for f in failures:
        print(f"  FAIL  {f}")

    print()
    if failures:
        print(f"FAIL - {len(failures)} reason(s). DO NOT release this image.")
        return 1
    print("PASS - no credentials found; safe to stage.")
    print("This checks the image only. The human OTA gates still apply: build "
          "on the release machine, roll out to one toy first, watch the "
          "check-in.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
