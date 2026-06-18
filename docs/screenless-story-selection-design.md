# Screenless story selection — design proposal

**Status:** proposal for owner decision. No code yet. Targets the current
bench hardware (ESP32-S3-DevKitC-1: ONE button on GPIO0, one WS2812 RGB LED
on GPIO48, INMP441 mic, MAX98357A speaker) and the offline SD content pack
(`manifest.json` + `/stories/<id>/` produced by `tools/ContentPackBuilder`).

## The problem

Areg is a storyteller for **pre-readers (ages 4–7)** with **no screen**. The
child must be able to **choose what to hear** using only sound, one button,
and an LED. Today the story is hard-wired (`AREG_STORY_ID` in `config.h`) —
there is no selection at all. Any solution must also:

- work **offline** (selection can't depend on the cloud / STT — STT on a young
  child's Armenian is our weakest link, see HARDENING §3);
- **scale**: the SD pack can hold 100+ stories, but a child cannot sit through
  100 spoken titles;
- respect **bedtime** (`manifest.json` already carries `bedtimeSafe`);
- not overload the single button into an unlearnable gesture soup.

## Models considered

| Model | Pre-reader fit | Offline | Scales to 100 | New HW | Notes |
|---|---|---|---|---|---|
| **A. Spoken menu** (toy reads titles, press to pick) | Good for a SMALL shelf | ✅ | ❌ (can't read 100) | none | Classic one-button screenless picker |
| **B. Physical cards / figurines** (NFC) | **Best** | ✅ | ✅ (one card per story) | PN532 (~$3) + cards | The Yoto / Tonies pattern — proven for this exact age |
| **C. "Surprise me" / shuffle** (one press = a story) | OK, low agency | ✅ | ✅ (no naming needed) | none | Great *default*, weak *choice* |
| **D. Say the story name** (voice) | Natural but fragile | ❌ needs STT | ✅ | none | Breaks offline; relies on the weakest link (kid Armenian STT). Reject. |

D is rejected outright (offline-breaking + leans on the mis-hear problem we
spent the most effort mitigating). The real answer is a **phased A → B**.

## Recommendation

### Phase 1 — ship now, no new hardware: spoken "shelf" + surprise-me

A child cannot navigate 100 titles, so do **not** try. Selection is over a
small **shelf** (≤ ~5 entries): the launch/built-in stories plus a few
"favorites/recent". The long tail is reached by shuffle, and properly by
Phase 2.

**Button vocabulary (context-dependent on the LED-signalled state — minimal,
learnable):**

```
IDLE   + tap   → enter CHOOSE mode (toy announces the shelf)
IDLE   + hold  → "Surprise me" → play a random not-recent story immediately
CHOOSE + tap   → play the story currently being announced
CHOOSE silence → auto-advance to the next title; after the last, wrap to
                 "Surprise me", then back to IDLE
PLAYING + press→ barge-in (pause / in-story Q&A)   [unchanged, existing]
```

**CHOOSE flow (all from local SD, zero runtime TTS):** the toy plays a short
pre-rendered **announce clip** per shelf story — «Առաջին հեքիաթը՝ Փոքրիկ
ամպիկը։ Սեղմի՛ր, եթե սա ես ուզում։» ("First story: The Little Cloud. Press if
you want this one.") — with a ~3–4 s LED-pulsed listening window between
titles. Press during/just-after a title → that story plays.

**LED states (WS2812, one pixel — enough for distinct cues):**
- IDLE: slow soft blue breathe (current idle color).
- CHOOSE: amber pulse during each title's listening window (= "press now").
- PLAYING / RECORDING / etc.: unchanged.

**Bedtime-aware:** when inside the device's bedtime window (the existing B4
concept), the shelf is filtered to `bedtimeSafe == true` stories and
"Surprise me" only draws from those — so a calm story is the only thing
reachable near sleep.

**Content-pack tie-in (small `ContentPackBuilder` addition):** pre-render one
**announce clip per story** to `/clips/announce_<id>.mp3` (a single extra TTS
line per story at build time). Then the offline menu speaks titles with **no
runtime TTS and no network** — consistent with the offline-first principle.
The shelf order + "favorites/recent" can be a tiny `/shelf.json` (ordered list
of ids) or just the first N of `manifest.json`.

### Phase 2 — the real product: physical cards / figurines (NFC)

This is what makes a screenless storyteller genuinely usable for a 4-year-old
(Yoto and Tonies both proved it). Each story is a **printed card** (with art
the child recognizes) or a **figurine**; the child taps it on the toy and it
plays. Selection becomes physical, tactile, and infinite-scalable.

- **Hardware:** add a PN532 NFC reader (~$3) on the spare SPI or I²C bus.
  ESP32-S3 has the pins (avoid the strapping pins + the two I²S peripherals
  already in use — see the ESP32-S3 datasheet review in this repo's history).
- **Mapping:** NFC tag UID → story id. Keep the map on SD
  (`/nfc-map.json`: `{ "<uid>": "<storyId>" }`) so new cards are
  added by editing a file, not reflashing.
- **Flow:** tap card → look up id → play `/stories/<id>/narration.mp3`. No
  button, no menu, no reading. Unknown card → a warm «Էս քարտը չեմ ճանաչում»
  ("I don't know this card") clip.
- Phase 1's spoken-shelf stays as the no-card fallback.

## Firmware touchpoints (Phase 1)

- **Manifest read:** parse `manifest.json` (id / title / bedtimeSafe) — already
  on SD; the offline-mode work (HARDENING §6) needs this anyway.
- **A small CHOOSE state** in the loop alongside IDLE/PLAYING/RECORDING: play
  announce clips, run the listening-window timer, watch the button.
- **Gesture parsing:** tap-vs-hold from idle (the debounce + `AREG_MIN_RECORD_MS`
  machinery already distinguishes tap from hold).
- **Shuffle:** pick a random id (avoid the last K played); seed from `millis()`
  / `esp_random()`.
- **Bedtime filter:** reuse the device's bedtime knowledge (or a local time
  window) to restrict the shelf.

## Backend / content touchpoints

- **None required for Phase 1 selection itself** — it's all local over the SD
  manifest. The only additive work is in `ContentPackBuilder`: render the
  per-story announce clip + (optional) emit `/shelf.json` and (Phase 2)
  `/nfc-map.json`. `manifest.json` already carries `title` + `bedtimeSafe`.
- Optionally add an OPTIONAL `category` field to the manifest (e.g.
  `animals` / `bedtime` / `funny`) for a future "category" picker — not needed
  for Phase 1.

## Open decisions for the owner

1. **Phase 1 entry gesture:** `IDLE+tap → CHOOSE` and `IDLE+hold → surprise`,
   or flip them? (Which should be the "lazy default"?)
2. **Shelf size & source:** first N of the manifest, a curated `/shelf.json`,
   or "recent + favorites"? (Favorites need a tiny persisted store — ties to
   the cross-power-cycle-memory gap.)
3. **Commit to Phase 2 NFC?** It's the single biggest UX jump and a ~$3 part,
   but it's a hardware + sourcing (cards) decision, not just firmware.
4. **Announce-clip voice/wording:** the «Առաջին հեքիաթը՝ …» framing above is a
   sketch — the exact Armenian needs an `armenian-story-master` review before
   it's rendered into the pack.

## Why this order

Phase 1 is buildable today on the current board, entirely offline, and removes
the "there is no way to choose" blocker for a small shelf. Phase 2 is the
honest long-term answer for 100 stories and a 4-year-old's hands — but it's a
hardware/product commitment, so it shouldn't gate shipping a working Phase 1.
