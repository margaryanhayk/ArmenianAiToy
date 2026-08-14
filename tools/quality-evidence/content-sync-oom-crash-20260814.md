# The toy is not failing to sync — it is crashing and rebooting (2026-08-14)

Stage 0 of the fleet-content plan: find out why the owner's toy would not pull the
re-rendered stories. Answer captured on real hardware over USB serial (COM7), full
transcript beside this file in `content-sync-oom-crash-20260814.log`.

**It is an out-of-memory panic in the content sync, on every attempt, forever.**

## What happens, every ~184 seconds

```
  0.3  [boot] AregVoiceMvp starting        reset_reason=11/UNKNOWN
  0.3  [sd] mounted; type=3 size=7680MB
  0.6  [content-report] schema=7 stories=[...8 old versions...] games=92 voice=43
 22.2  [alive] heap=123528 psram=7861564 wifi=3 ip=192.168.1.3
180.1  [content-sync] starting
180.1  [content-sync] heap before=123528
183.5  [content-sync] manifest status=200 stories=10 voice=42 games=104 ...
183.7  ESP_ERROR_CHECK failed: esp_err_t 0x101 (ESP_ERR_NO_MEM) at 0x42037771
183.7  file: ".../esp_phy/src/phy_common.c" line 118   func: phy_track_pll_init
184.0  abort() was called at PC 0x4037e07b on core 0
184.0  Rebooting...
184.5  [boot] reset_reason=4/PANIC
```

Then it arms again 180 s later and does exactly the same thing. The toy has been in
this loop since it took 1.2.1.

## Ruled out by the capture

- **Not a full card.** `[sd] mounted; type=3 size=7680MB`, and the panic is nowhere
  near a download. `no_space` is not involved.
- **Not the backend.** `manifest status=200` with the correct new content —
  `stories=10 voice=42 games=104`. The server did its job.
- **Not a parse failure.** The crash is *after* the fetch and inside allocation, not
  a `DeserializationError`.
- **Not the missing build flag.** `[content-sync] bench build enabled` — the 1.2.1
  image carries `AREG_CONTENT_SYNC_BENCH` correctly.

## Root cause

`esp32/AregVoiceMvp/content_sync.cpp:1027-1028`:

```cpp
JsonDocument doc;
const DeserializationError jerr = deserializeJson(doc, http.getString());
```

`http.getString()` materialises the **whole manifest** as a heap `String`, and the
elastic `JsonDocument` then allocates the parsed tree **beside it** — both alive
simultaneously. The manifest is now 156 items (10 stories + 42 voice clips + 104 game
clips), each carrying a 64-character sha256, a URL and a size. Against **123,528 bytes**
of free heap, the pair does not fit.

The panic surfaces inside `phy_track_pll_init` creating a Wi-Fi PHY timer. That is a
**victim, not the culprit** — the heap is already exhausted, so the next allocation
anywhere in the system fails an `ESP_ERROR_CHECK` and aborts. Any other subsystem
could have been the one to die.

## Why it started now — two things moved the wrong way at once

1. **The manifest grew.** Game clips 92 → 104 (the "twelve kid lines" commit),
   stories 8 → 10, on top of 42 voice clips.
2. **Free heap fell ~86 KB.** The 1.2.0 OTA boot diagnostic reported
   `heap=210020`; 1.2.1 reports `heap=123300`, and steady-state `[alive]` lines here
   confirm `heap=123528`. CLAUDE.md recorded this drop as "unexplained" after the
   1.2.1 rollout. **It is not cosmetic — it is the difference between a sync that
   fits and one that panics.** The content report itself accounts for only 672 B, so
   the remaining ~86 KB is still unaccounted for and now has a known cost.

## What this invalidates in the plan

- **Stage 1 as written would not have caught this.** The device aborts mid-run; there
  is no path to set a status field, and the heartbeat never gets to report it. The
  only surviving signal is `reset_reason=4/PANIC` at the *next* boot — which the
  firmware already computes (`ota_foundation.cpp:101-112`) but sends **only** on an
  OTA command ack, never on the heartbeat. **Stage 1 must add the reset reason and a
  boot counter to the heartbeat**, or a crash-looping toy stays invisible.
- **A crash loop is indistinguishable from health today.** The toy heartbeats
  normally during the first 180 s of every cycle, so `lastSeenAt` is always fresh and
  the console shows it online and content-`stale`. Nothing anywhere says "this device
  has panicked 400 times."
- **The Stage 2 retry design needs a crash guard.** A scheduler alone would re-arm and
  re-panic. The persisted `s_fail_streak` must also be incremented *before* the risky
  work and cleared after it, so a panic is counted as a failure on the next boot and
  backs off — otherwise retry makes the loop tighter, not safer.

## The fix

`deserializeJson(doc, http.getStream())` instead of `http.getString()` — parse
straight from the socket and never hold the payload twice. Roughly halves peak usage.
Add an ArduinoJson **filter** so the device allocates only the fields it uses.
Together these are a handful of lines and they scale with the library rather than
against it.

The ~86 KB heap regression between 1.2.0 and 1.2.1 should be measured separately; the
streaming fix buys headroom but does not explain where that memory went.

## Immediate mitigation available, not applied

Setting `ContentSync__Enabled=false` in Railway returns an empty manifest, so the sync
does nothing and the loop stops within one cycle. It is one variable and instantly
reversible. **Not applied — it is a fleet-wide content change and the owner was
asleep.** Note the side effect if it is used: `AdvertisedStoryVersions()` goes empty,
so `DeviceContentHealth` reports `up_to_date` ("cannot be behind what was never
offered") and both dashboards would show the toy as ready while it is not.

## Method

`python` + `pyserial` on COM7 at 115200, DTR/RTS pulse to force a clean boot, 260 s
capture. `arduino-cli monitor` was tried first and exits immediately without a TTY —
worth knowing, since it is the command the runbook and README both recommend.
