# AregVoiceMvp — C1 bench firmware

First firmware slice for the Armenian AI Toy voice loop. One
ESP32-S3 dev board on a breadboard presses the backend's
already-shipped `POST /api/chat/audio` endpoint end-to-end:

```
button press-hold   →  INMP441 records PCM @ 16 kHz mono
button release      →  WAV header + PCM POST to /api/chat/audio
backend returns MP3 →  buffered fully in PSRAM
minimp3 decodes     →  I²S out → MAX98357A → speaker
```

Five LED states (IDLE / RECORDING / UPLOADING / PLAYING /
ERROR). One flash-embedded Armenian failure clip on any error.
One serial latency print per successful turn.

This is a **bench prototype**. No wake word, no barge-in, no
retries, no battery, no enclosure, no provisioning UX, no OTA.
Those are later-phase concerns per the toy-mvp scope guard.

## Hardware assumptions

Defaults target an **ESP32-S3-DevKitC-1** (N8R8 or N16R8 —
PSRAM required for the 480 KB capture + 512 KB response
buffers). Other S3 boards work; adjust pin numbers in
`config.h`.

| Component      | Part        | Pin (config.h)          |
|----------------|-------------|-------------------------|
| Mic (I2S RX)   | INMP441     | BCK=4 / WS=5 / SD=6     |
| Mic L/R select | INMP441 L/R | **tie to GND**          |
| Amp (I2S TX)   | MAX98357A   | BCK=15 / LRC=16 / DIN=7 |
| Button to GND  | tactile     | 0 (BOOT)                |
| LED (WS2812)   | onboard     | 48                      |

The firmware reads only the left I2S slot
(`I2S_CHANNEL_FMT_ONLY_LEFT` in `audio_io.cpp`), so the
INMP441's L/R pin must be tied to GND. Floating L/R produces
silence or noise that looks like a working capture.

Wire all three ground returns (board, mic, amp) to the same
ground rail — shared ground is the most common wiring mistake
on this kind of bench.

**Wi-Fi must be 2.4 GHz.** ESP32-S3 has no 5 GHz radio. If your
router exposes one merged SSID across both bands and prefers
5 GHz for new clients, the board can sit in
`[wifi] connecting...` indefinitely on otherwise-correct
credentials. Move the bench AP to a 2.4 GHz-only SSID for
bring-up.

**BOOT button is the press-to-talk button.** GPIO 0 is wired to
the BOOT button on DevKitC-1, so don't hold it while resetting
(you'll land in flash-mode); use the EN/RESET button for a clean
restart instead.

## Arduino IDE setup

- **Board**: "ESP32S3 Dev Module"
- **PSRAM**: "OPI PSRAM" (or "QSPI PSRAM" depending on your
  board variant — required either way)
- **Partition Scheme**: default "Default 4MB with spiffs" is
  fine
- **Flash Size**: set to match your chip — "8MB (64Mb)" for
  N8R8, "16MB (128Mb)" for N16R8. The Arduino IDE does not
  detect this automatically.
- **USB CDC On Boot**: Enabled (so Serial Monitor works over
  the native USB port)
- **Libraries (via Library Manager)**:
  - `Adafruit NeoPixel` by Adafruit
  - `ESP8266Audio` by Earle Philhower — version not pinned yet;
    record the exact installed version in this README after the
    first successful C3.1 bench compile so future bench machines
    can reproduce the build.

No other libraries. `WiFi`, `HTTPClient`, `driver/i2s.h`, and
`esp_heap_caps.h` all ship with the ESP32 Arduino core.

## First-run provisioning (one-time)

1. Start the backend on your dev laptop. Device registration is FAIL-CLOSED by
   default (#009), so a dev/bench host must opt into open registration:
   ```
   cd backend
   # bash / git-bash:
   export Devices__AllowOpenRegistration=true
   dotnet run --project src/ArmenianAiToy.Api
   ```
   In PRODUCTION do NOT enable open registration — instead set
   `Devices:ProvisioningSecret` and send it in the `X-Provisioning-Secret`
   header on the register call below.
2. Find your laptop's LAN IP (the ESP32-S3 must be on the same
   network).
3. Register one device against the backend and save the
   returned `DeviceId` + `ApiKey`:
   ```
   curl -s -X POST http://<laptop-ip>:5000/api/devices/register \
     -H 'Content-Type: application/json' \
     -d '{"macAddress":"bench-01"}'
   ```
   > With a prod provisioning secret set, add
   > `-H 'X-Provisioning-Secret: <secret>'`. Registering an ALREADY-registered
   > MAC is REFUSED with 409 and the device keeps its key (#011); to
   > deliberately rotate a lost/compromised key, re-send the request with
   > `-H 'X-Force-Rotate: true'`.
4. Copy the four values into `config.h`:
   - `AREG_WIFI_SSID` / `AREG_WIFI_PASSWORD`
   - `AREG_BACKEND_URL` (e.g. `http://192.168.1.100:5000/api/chat/audio`
     — pick a free port; pass `--urls http://0.0.0.0:5050` to
     `dotnet run` if `:5000` is already in use, and match the
     port in the URL above)
   - `AREG_DEVICE_ID` / `AREG_DEVICE_API_KEY`

   > **Do not commit `config.h` after pasting real credentials.**
   > The file is tracked (so pin/timing constants ship with the
   > repo) and `.gitignore` does not exclude it. Run
   > `git diff --staged -- esp32/AregVoiceMvp/config.h` before
   > every commit and confirm it prints nothing. Optionally,
   > mark the file skip-worktree for the duration of bench work:
   >
   > ```
   > git update-index --skip-worktree esp32/AregVoiceMvp/config.h
   > # revert later with --no-skip-worktree
   > ```

## Render the failure clip (optional, one-time per voice change)

This step is **optional**. It calls OpenAI's TTS API and **costs
OpenAI credits** every time you run it. Skip it if you already
have a non-stub `canned_clip.h`, or if you accept silent error
paths during bench bring-up — the firmware logs
`[fail] canned clip stub is empty; skipping playback` and
returns to idle cleanly.

The committed `canned_clip.h` is a 1-byte stub — the firmware
plays silence on error paths until you regenerate it.

```
cd esp32/AregVoiceMvp
export OPENAI_API_KEY=sk-...
./tools/render_canned_clip.sh
```

This calls OpenAI TTS (tts-1 / Nova — same voice identity as
the backend's runtime responses) and rewrites `canned_clip.h`
as a PROGMEM byte array. Rebuild + reflash after running it.

## Build + flash

Open `AregVoiceMvp.ino` in the Arduino IDE. Hit Upload. Open
Serial Monitor at 115200 baud.

You should see (matches `setup()` output in `AregVoiceMvp.ino`):
```
[boot] AregVoiceMvp starting
[boot] backend=http://<your-host>:<port>/api/chat/audio
[boot] pins button=0 led=48
[boot] mic_i2s bck=4 ws=5 sd=6
[boot] amp_i2s bck=15 lrc=16 din=7
[wifi] connecting to <your SSID> ...
[wifi] ip=192.168.1.X
[boot] ready — press button to speak
[state] 0 -> 0
```

### arduino-cli — correct FQBN (avoids the false 96–97% flash alarm)

This sketch ships a custom **8 MB dual-OTA `partitions.csv`** with **3 MB
OTA app slots**. Always build/upload with `FlashSize=8M` and
`PartitionScheme=custom` so the flash-size check matches the real
partition table:

```
esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc
```

**⚠️ Do NOT use `PartitionScheme=default` for this project.** The default
scheme reports a **1.25 MB** app slot (0x140000) and makes the toolchain
measure firmware against the wrong ceiling — that is the sole source of
the false "96–97% of program storage" alarm. Built correctly, the
production image is ~**1,264,539 bytes ≈ 40%** of the real **3 MB** slot,
with ~1.88 MB free per slot.

Production compile (flag-off):
```
arduino-cli compile --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" ".\esp32\AregVoiceMvp"
```

Upload:
```
arduino-cli upload -p COM7 --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" ".\esp32\AregVoiceMvp"
```

Content-sync bench (compile + upload, `-DAREG_CONTENT_SYNC_BENCH`):
```
arduino-cli compile --upload -p COM7 --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" --build-property "compiler.cpp.extra_flags=-DAREG_CONTENT_SYNC_BENCH" ".\esp32\AregVoiceMvp"
```

SD diagnostic bench (compile + upload, `-DAREG_SD_DIAG_BENCH`):
```
arduino-cli compile --upload -p COM7 --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" --build-property "compiler.cpp.extra_flags=-DAREG_SD_DIAG_BENCH" ".\esp32\AregVoiceMvp"
```

Content-sync decision-logic tests (`-DAREG_CONTENT_SYNC_TEST_BENCH`) — no
SD card, no Wi-Fi, no backend needed; prints `[cs-test] RESULT PASS/FAIL`
about 20 s after boot:
```
arduino-cli compile --upload -p COM7 --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" --build-property "compiler.cpp.extra_flags=-DAREG_CONTENT_SYNC_TEST_BENCH" ".\esp32\AregVoiceMvp"
```

Story-selection tests (`-DAREG_STORY_SELECT_TEST_BENCH`) — no SD, no NVS,
no Wi-Fi; prints `[sel-test] RESULT PASS/FAIL` about 20 s after boot:
```
arduino-cli compile --upload -p COM7 --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" --build-property "compiler.cpp.extra_flags=-DAREG_STORY_SELECT_TEST_BENCH" ".\esp32\AregVoiceMvp"
```

> Adjust `-p COM7` to your serial port. Production builds must define
> **neither** bench flag — each `-DAREG_*_BENCH` module compiles to zero
> bytes without its flag.
>
> The custom partition table has **3 MB OTA app slots**, so SD MP3
> playback can proceed **without any partition migration**.

## Bench demo

1. Press and hold the BOOT button. LED turns red.
2. Speak one Armenian sentence — e.g. *"Պատմիր հեքիաթ"* —
   then release. LED turns yellow.
3. Within a few seconds, LED turns green and the speaker plays
   Areg's Armenian response.
4. Press the button again and say *"Ա"* — the story continues.
5. Repeat once more. Three-turn success is the C1 exit
   condition.

On any failure (Wi-Fi down, backend unreachable, non-200
response, decoder error), LED turns orange and the canned
Armenian failure clip plays once before the device returns to
idle. The next press works normally.

## What to look at in the serial log

Every turn emits:
- `[state] N -> M` lines for each transition
- `[cap] samples=...` — size of the captured PCM
- `[voice] http 200, body=N bytes (psram)` — upload + response
  read succeeded
- `[latency] release->play_begin_ms=N` — wall-clock from
  button-release to first audio byte out of the speaker

C1 latency target: **≤ 7 s perceptual**. Good: **≤ 4 s**. If
you are consistently above 7 s, stop adding features and
profile the longest stage.

## Cloud→SD content sync — multi-story

`content_sync.cpp` (bench flag `AREG_CONTENT_SYNC_BENCH`) syncs **N**
stories from `GET /api/devices/content-manifest`, not just the first.
Decision logic lives in two dependency-light layers so it can be tested
without hardware: `content_sync_rules.h` (pure) and
`content_sync_model.cpp` (JSON ↔ struct, no SD/HTTP).

| Property | Value |
|---|---|
| Max stories per sync | **8** (`CS_MAX_STORIES`) — 8 × ~4.6 MB ≈ 37 MB on a 7.5 GB card; tables cost ~10 KB `.bss`; the real ceiling is download wall-clock, not storage |
| Max story-id length | 48 (`CS_MAX_STORY_ID_LEN`) |
| Max story size | 32 MB (`CS_MAX_STORY_BYTES`) |
| Max stored audioUrl | 128 (`CS_MAX_URL_LEN`) |
| Cache file | `/stories/<storyId>-v<version>.mp3` |
| Temp file | `/tmp/<storyId>-v<version>.mp3.part` (unique per story **and** version) |
| Index | `/content_index.json`, schema **v2** |

**Story-id allowlist:** `a-z`, `0-9`, `-`, `_` only. Uppercase, `.`, `/`,
`\`, `:`, spaces and control characters are rejected, so `..`, absolute
paths and traversal segments are *unrepresentable* rather than filtered.
Lowercase-only matters because the backend dedupes case-insensitively —
accepting mixed case would let one backend story become two filenames.
Duplicate ids keep the **first**, matching the backend.

**Per-item independence.** A bad item (empty/unsafe id, malformed
sha256, zero or oversized size, empty audioUrl, over-long path) is
dropped and counted; its valid siblings still sync. A failed download
leaves the previously-good file untouched and never enters the index.
Manifests longer than `CS_MAX_STORIES` are truncated with a log line,
never a crash.

**`audioUrl` is used exactly as supplied** — bare
`/api/devices/content-file` (legacy config) or
`?storyId=<id>` (multi-story). Nothing is appended or duplicated.

**Already-current decision.** After a download, a full SHA-256 is always
verified before promotion. On later boots a story is skipped when the
index entry matches (id/version/sha/size + `verified`) **and** the file
exists at exactly the recorded size — index metadata alone is never
enough, because the file can vanish independently of the index. With no
usable index entry the file is streamed through SHA-256 instead, which is
what the single-story build did on *every* boot; keeping that only for
the no-entry case avoids tens of seconds of SPI reads per boot at 8
stories.

**Index schema v2** — plus a *legacy compatibility mirror*:

```json
{
  "schemaVersion": 2,
  "stories": [
    { "storyId": "anban-huri", "version": 1, "title": "Անբան Հուռին",
      "sha256": "4ba096…", "sizeBytes": 4654560,
      "cachePath": "/stories/anban-huri-v1.mp3", "verified": true }
  ],
  "storyId": "anban-huri", "version": 1, "sha256": "4ba096…",
  "file": "/stories/anban-huri-v1.mp3", "sizeBytes": 4654560
}
```

The four flat fields are **not** a second source of truth. Active playback
no longer reads them (see "Story selection" below); they are retained only
for two bench harnesses — `resolve_path()` in `sd_playback.cpp` and the
Test-E fallback harness. The mirror points
at the entry whose id equals `AREG_STORY_ID`, else the first, which
reproduces single-story behavior exactly. `story-select-from-index`
migrates those readers and drops the mirror.

**Legacy (v1) index migration.** A flat single-object index is detected
and migrated in memory; its cached MP3 is preserved. The only field v1
never wrote is `verified`, inferred `true` because v1 only wrote its
index after a full SHA-256 — and existence + size are re-checked anyway,
so a wrong inference costs a re-download, never a bad file. **A card
never has to be erased.**

**Non-destructive by default.** An empty manifest leaves the index and
every cached file untouched (absence is not a retirement instruction). A
story absent from the manifest but still verified on the card is carried
forward into the index. `enabled:false` skips the story without deleting
its file. Retirement deletion and an orphan sweeper are **deferred**.

**Index replacement is atomic**: written to `/content_index.json.new`,
then swapped in. A crash before the swap leaves the previous known-good
index; a crash inside the remove/rename window leaves the `.new` file and
the next boot rebuilds from the manifest. No MP3 is at risk either way.

## Story selection (`story_select.{h,cpp}`)

As of the `story-select-from-index` slice the toy **chooses** which cached
story to play instead of always playing the compile-time `AREG_STORY_ID`.
This is normal playback, not a bench path — it is compiled into every
build.

**Deterministic round-robin, no-repeat by construction.**
`story_select_next()` is pure and allocation-free:

| Situation | Result |
|---|---|
| 0 eligible | no selection → fall through the chain below |
| 1 eligible | that story (a one-story card must keep working, so no-repeat cannot apply) |
| previous id unknown/empty | the **first** eligible entry |
| otherwise | the entry **after** the previous one, wrapping at the end |

So three stories rotate `A → B → C → A → B → C`. With two or more
eligible stories the result is never the previous one, so **no
back-to-back repeat holds by construction**, not by retry. Random
selection was rejected for v1: it makes the bench unreproducible, can
repeat by chance, and leans on boot-time RNG the device does not have.
Previous-id matching is case-insensitive, as the backend and index are.

**Eligible** requires ALL of: valid id, `verified == true`, `version >= 1`,
positive recorded `sizeBytes`, a bounded safe `cachePath` (absolute, under
`/stories/`, no `..`, no `\`), the file present on the card, and its
**actual size equal to** `sizeBytes`. Index metadata alone is never
enough — a file can vanish independently of the index. Duplicate ids keep
the first; index order is preserved.

**Session stability.** The selected id is held in `s_current_story_id` for
the whole session. The new-story boundary is entering
`handle_story_session()` with `s_story_offset == 0`; a resume (offset > 0)
re-resolves the *same* story and never re-selects. So pause/resume, a Q&A
barge-in, and a stream-token retry all stay on one story. A natural end
resets the offset to 0, so the next press advances the rotation.

**Last-played persistence.** NVS namespace `aregstory`, key `last_id`
(Arduino `Preferences`, the same idiom as `wifi_creds` / `device_creds` /
`ota_state`). Only the id is stored — no secrets, no index. It is written
**only when the value changes**, so pause/resume does not burn flash. A
stored id that no longer validates is ignored rather than trusted, and a
persistence failure is logged and swallowed: it can never block playback.

**The cursor advances only after playback GENUINELY STARTED** — never at
resolve time. `audio_play_story_file()` reports this through its
`out_started` flag, set once `mp3.begin()` has succeeded *and* the first
`mp3.loop()` decode iteration completed, i.e. the decoder is initialized
and the first frame reached I2S. Every earlier bail-out (SD not mounted,
open failed, the #064 not-an-MP3 precheck, `mp3.begin()` failure) returns
with it false and makes no sound. A story that resolved but never played
must not become `last_id`, or the next press would skip a story the child
never heard.

**Failed-start exclusion (boot-scoped).** A story that resolved but did
not start is remembered in bounded RAM and skipped by the next *new-story*
selection, so a corrupt-but-right-sized file cannot trap the rotation on
itself. It is deliberately **not** persisted — a reboot retries it, which
is safer than skipping it forever after one bad start. The whole
exclusion set is cleared as soon as another story genuinely starts.

The exclusion is **best-effort**: if applying it would leave nothing to
play it is ignored, so a one-story card whose only story failed still gets
a retry on the next press rather than silence, and there is no loop
because each press is a single attempt. Consequence worth knowing: with
exactly two stories where one is broken, the good one replays
back-to-back — availability beats strict no-repeat when the library is
effectively one playable story.

Worked example (the policy in one line each):

```
A played successfully        -> last_id = A
selector picks B             -> B resolves, decoder start fails, no audio
                             -> last_id STAYS A, B excluded (RAM only)
next new-story request       -> picks C  (not B, not A)
C starts successfully        -> last_id = C, exclusion set cleared
```

Pause, resume, a Q&A barge-in and the stream-token retry never touch the
cursor: the bookkeeping runs at most once per session, guarded by
`selection_settled`.

**Story-aware resolution.** `story_select_resolve_path(story_id, out, len)`
replaces the old `story_resolve_cache_path(out, len)`. It resolves **only**
the requested id and returns false — never another story's path — when the
id is invalid, absent, unverified, unsafe, missing on the card, or
size-mismatched. Callers cannot push an arbitrary filesystem path through
it. `AREG_STORY_ID` is no longer consulted anywhere in resolution.

**Fallback order** (decided once per session):
1. the selected verified story from the schema-v2 index;
2. the content-pack narration `AREG_SD_STORY_NARRATION`;
3. the Wi-Fi story stream.

A selected story that fails to resolve falls through to 2/3 rather than
silently playing a *different* cached story.

**In-story Q&A follows the selected story.** `voice_set_active_story_id()`
grounds `/api/chat/story-qa` and the reflection endpoint in whatever is
playing, so a question asked during story B is not answered about story A.
Empty restores the configured-story default.

### Serial episodes («Ծիվիկ») — play in order, one at a time

A serial is an ordinary set of `ContentSync:Stories` entries
(`tsivik-ep1..ep6`) that carry two extra manifest fields, `seriesId` and
`seriesIndex`. Nothing about download, caching, verification or playback
changes — only **which members of the set are offered**.

- **Index schema v4 → v5.** Per-story `seriesId` / `seriesIndex`, written
  only for real episodes. Superset like every previous bump, so a v4 card
  parses as "every story is standalone" — the exact pre-serial behaviour —
  and **no card ever has to be wiped**. Pinned by
  `test_index_v4_forward_compatible`.
- **Both-or-neither.** An id without a positive index (or the reverse) is
  stored as a standalone story, never as a half-set episode. The backend
  applies the identical rule before the wire; the device re-applies it
  because a card can be hand-edited. Nothing about a bad pairing drops the
  *story* — a metadata typo must not take a working narration off a toy.
- **Eligibility** (`story_series_member_allowed`, pure and bench-tested):
  an episode is offered only when it is the **lowest-index NOT-heard**
  member of its series. Indexes need not be contiguous; only order
  matters. Standalone stories are completely unaffected.
- **One new episode at a time** — a RAM latch, set when an episode is
  marked heard, that makes every sibling of that series ineligible.
- **After a serial episode ends naturally**, the toy plays the
  `serialnext` clip («Շարունակությունը՝ վաղը») if it is synced and
  verified. It plays **before** the reflection flow, not after, because
  `handle_post_story_flow()` returns early on several ordinary paths
  (offline, no answer in the listening window) and a closing line the
  child usually never hears is worse than one arriving a beat early.
- **Best-effort, never silence.** If the series rule would leave nothing
  to play, it is ignored and the unfiltered list is used — the same
  posture the failed-start exclusion takes. A card holding only a
  fully-heard serial still plays.

#### The "one a day" gate is per BOOT, not per day — read this

The toy has **no wall clock and no calendar**. The latch above lives in
RAM and is cleared by a reboot or a battery pull, after which the next
episode becomes available immediately. That is the honest v1: on a toy
that stays powered through the day (the common case) the child gets one
new episode and the series then waits, which is the product behaviour the
owner asked for. On a toy power-cycled repeatedly, it is not a daily gate
at all.

Real calendar gating needs a **server day-signal**, delivered the way
`inBedtimeWindow` already rides the heartbeat response. That is a separate
slice. Nothing in this one invents a date the device does not have, and
nothing persists the latch — persisting it without a clock would mean
never knowing when to expire it.

#### Not covered by the bench tests

`story_select_test.cpp` covers only the **pure** ordering rule. Still
needs real hardware: the NVS heard-set read inside the filter, the boot
latch surviving a real session, a real multi-episode sync, and the
`serialnext` clip actually playing at the end of an episode.

### Story feature toggles — in-story pauses + variant endings

Two parent switches (`PUT /api/parents/devices/{id}/story-pauses` and
`.../variant-endings`, both default ON) ride the content manifest as
`storyPausesEnabled` / `variantEndingsEnabled` and are cached in the index
root as `pausesEnabled` / `variantsEnabled`, so they apply offline exactly
the way `introEnabled` does.

- **Index schema v5 → v6.** The two root flags plus a per-story `altOf`,
  written only for real variants. Superset like every previous bump: a v5
  card parses as "no story is a variant, both features on" — the exact
  pre-variant behaviour — so **no card ever has to be wiped**. Pinned by
  `test_index_v5_forward_compatible`.
- **`story_pauses_enabled()` is PLUMBING ONLY in this slice.** Nothing
  reads it yet. The pause clips it will gate are not authored, rendered or
  synced, so there is no playback to switch off. It exists now so the
  toggle can be verified along its whole path — dashboard → audit →
  manifest → SD index → device — before any audio depends on it. The
  playback wiring lands with the clips.
- **Variant endings ARE live.** Each variant ships as a FULL alternate
  file (the approved base narration cut at the branch point plus the new
  ending, assembled offline in the Ship-StoryAudio pipeline), configured as
  an ordinary `ContentSync:Stories` entry carrying `altOf: <baseStoryId>`.
  The device never splices audio — it opens one file or the other.
- **A variant is NOT a rotation member.** `cs_story_is_variant()` excludes
  it from `story_select_load_eligible`, so it can never be picked by the
  rotation, offered by name in the welcome flow, or marked heard. A variant
  file starts part-way through its story; if one reached
  `story_select_pick` a child would be told half a story as though it were
  the whole thing. That guard is the point of the field.
- **Resolution** is `story_select_resolve_playback_path()`, which every
  production playback path now calls instead of `story_select_resolve_path`.
  It returns the variant only when the toggle is on **and** the story is
  already in the `aregheard` set **and** an alt entry for it is cached,
  verified, and on the card at the recorded size. Anything else falls back
  to the base narration, so a card with no variants behaves exactly as
  before this slice.
- **A first listen always gets the authored ending** — the heard-set is
  written only at a story's natural end, which is also what makes a RESUME
  safe: the decision is deterministic for a given card + heard-set, so the
  byte offset can never be applied to a different file mid-story.
- **A present-but-unusable variant falls back**, it does not silence the
  toy: the child still gets their story, just the ending they already know.

Backend-side, an invalid `altOf` **drops the whole manifest item** rather
than just the field — the opposite of the series pairing's failure mode,
and deliberately so. See `ContentManifestService.TryResolveAltOf`.

#### Not covered by the bench tests

`content_sync_test.cpp` covers the manifest/index parse, the v5→v6
forward-compat contract and the two root flags. Still needs real hardware:
a real sync carrying an `altOf` entry, the heard-set gate across two real
sessions, and a variant actually playing on the second listen.

#### Measured size cost (canonical FQBN)

| Build | Flash | Free RAM |
|---|---|---|
| production, before | 1,294,199 B | 229,144 B |
| production, after | 1,294,983 B | 227,480 B |
| cs-test bench, before | 1,324,763 B | 176,608 B |
| cs-test bench, after | 1,328,859 B | 168,224 B |

Production pays **+784 B flash / −1,664 B RAM** for the `alt_of` field
across the CsStory tables — MEASURED, not estimated, per the rule the
`CS_MAX_STORIES` and `CS_MAX_CLIPS` bumps set. 227 KB free still leaves far
more than the ~40–50 KB a TLS handshake wants during audio.

### Feature flag: `AREG_STORY_SD_CACHE_FIRST` is GONE

It previously gated the whole cache-first block, was **off by default**,
and was listed in `docs/v2-backlog.md` as "promote to default — deferred".
This slice **removes it**: index-backed selection is the normal playback
source. Compatibility is preserved by the fallback chain rather than by
the flag — a card with no v2 index yields zero eligible stories and
behaves exactly like the old flag-off build. `AREG_STORY_SD_FALLBACK_TEST_BENCH`
no longer requires it (its `#error` is removed).

### Legacy index mirror: RETAINED, deliberately

Active playback no longer reads the flat root fields — selection and
resolution use `stories[]` only, so a stale mirror can never override a
valid v2 selection. The mirror is **not** removed, because two readers
still depend on it, both verified by repo search:

- `sd_playback.cpp:41` — `doc["file"]`, the `AREG_SD_PLAYBACK_BENCH`
  cached-MP3 playback harness;
- `AregVoiceMvp.ino` Test-E in `AREG_STORY_SD_FALLBACK_TEST_BENCH`, which
  writes a flat index on purpose.

Both are hardware-verification tools. Removing the mirror for tidiness
would break them, so it stays until those harnesses are migrated.

### What still blocks SHIP A6

Selection exists, but A6 is **not** DONE. Still required, on real
hardware:

- **three approved MP3 stories** (today: 2 approved stories, and only
  `anban-huri` has an SD-wired MP3; `anban-huri` itself is still a
  `draft` pending its TTS listen test);
- a real **three-item sync** onto the card;
- selection **observed across repeated new-story requests**;
- **no back-to-back repeats** observed;
- **reboot persistence** of the rotation;
- **pause/resume staying on the same story**.

None of that has been run. Do not mark A6 DONE without recorded evidence.

## Welcome flow — the toy's opening (`handle_welcome_flow`)

Before this, the toy was **silent at power-on** and one button press always
started or resumed a story. Now, at the end of `setup()`:

```
greeting  → "what shall we do?" → child answers OUT LOUD
          → "Do you want to hear «X»?"   (a story they have not heard)
          → or "We already heard «X» — shall I tell it again?"
          → "yes" → the story plays, exactly as before
```

**Every line the toy speaks here is a pre-rendered MP3 from the SD card**
(`/voice/<id>-v<n>.mp3`, synced by content_sync from the manifest's new
`voice[]` array). Speaking therefore works offline, costs nothing, and adds no
delay. Only **hearing** the child needs the network.

Shape is copied from `handle_post_story_flow` — play a clip, open a listening
window, record, upload, act — so there is **no new state enum, no new LED
vocabulary and no state machine**. It is a blocking call from `setup()`, exactly
like a story session is a blocking call from the IDLE branch.

### Behaviour that is deliberate

| Situation | What happens | Why |
|---|---|---|
| Toy is paused | **silent** — no greeting at all | A paused toy is fully silent; the greeting would be the first thing to break that promise |
| Inside the bedtime window | **silent** | A cheerful hello at 21:30 loses a parent's trust |
| No SD card | silent | Every line lives on the card |
| No greetings synced yet | skips to the ask | A half-synced card degrades quietly, never mid-sentence |
| Offline | one short line, then a story | Hearing the child needs the cloud, and the owner chose voice-only — so the fallback is a graceful DEFAULT, not a second menu |
| Nobody answers | goes quiet | Silence usually means nobody is there. A toy that keeps asking an empty room is the opposite of what a parent wants |
| Mis-heard | «say again» once, then a story | Two tries. A third reads as nagging to a five-year-old |
| Child asks for game / riddle / curiosity | **opens the online chat session** (`handle_online_chat_session`) | The child's own recorded utterance is POSTed to `/api/chat/audio` — the backend's ModeDetector routes it and speaks the opener. Loop: play reply → press-to-talk within `AREG_CHAT_LISTEN_MS` (default 12 s) → upload → play, until silence closes it quietly. Turn cap `AREG_CHAT_SESSION_MAX_TURNS` (default 30) bounds cost. Parent gates re-checked server-side every turn. **NOT yet bench-verified on hardware.** |
| Every mode disabled | greeting only, then stop | Never promise something the parent switched off |
| A story has no `offer` clip | plays it instead of offering | A missing recording must never be why a child hears nothing |
| A press (not power-on) | unchanged — starts/resumes a story | A child who wants the next story should not be interrogated every time |

### What it added on the device

- **Index schema v3 → v4**: root `voice[]` plus the four parent mode flags
  (`storyEnabled` / `gameEnabled` / `riddleEnabled` / `curiosityEnabled`).
  A superset, like every previous bump — a v3 card parses as "no voice clips,
  every mode enabled", so **no card ever has to be wiped**.
- **`CS_MAX_CLIPS` 5 → 7** for the per-story `offer` / `reoffer` lines.
- **`CS_MAX_VOICE` = 48** device-global clips (39 greetings + 4 fixed lines,
  with headroom).
- **NVS `aregvoice`/`last_greet`** — the greeting rotation cursor.
- **NVS `aregheard`/`ids`** — which stories have been heard, as one bounded
  blob. Needed because `story_report` DELETES each play event once the backend
  accepts it, so the only other surviving memory is the single `last_id`.
  Written under the same `started` gate as the rotation cursor: a story that
  resolved but made no sound has not been heard.
- **NVS `aregstate`** — last-known pause / bedtime, written only on change.
  Without it both read `false` at power-on and a toy that had been off for a
  week would greet a child whose parent paused it six days ago.

### RAM cost — measured, not estimated

| Build | Globals | Free for locals |
|---|---|---|
| before the welcome flow | 139,632 B | 188,048 B |
| first draft (`CS_MAX_VOICE` 48, a table per function) | 217,168 B | **110,512 B** |
| shared tables, `CS_MAX_VOICE` 32 | 170,000 B | 157,680 B |
| **shipped** (`CS_MAX_VOICE` 48) | 178,448 B | **149,232 B** |
| (`CS_MAX_VOICE` 64, measured for reference) | 186,896 B | 140,784 B |

The first draft was rejected on these numbers: ~110 KB leaves too little on a
board that also wants 40–50 KB for a TLS handshake while audio is playing. But
the bound was **not** the cause — the duplicated tables were. Recovered by
sharing one voice scratch table between both readers, sharing ONE
eligible-story table across the offer loop and `story_pick_for_session`, and
building only the chosen greeting's path instead of all of them. With those
fixed, 48 slots costs 8 KB against 32 and is comfortable.

`CS_MAX_CLIPS` 5 → 7 also broke the **test bench** build (`dram0_0_seg`
overflowed by 130 KB) because eleven test functions each held their own
`static CsStory[CS_MAX_STORIES]`. They now share one set of scratch buffers.

### Bench verification — NOT yet run

Nothing below has been observed on hardware. Compile-verified only.

```
[content-sync] manifest status=200 stories=... voice=28 modes=1111
[content-sync] voice summary offered=28 already=0 downloaded=28 failed=0 voice_active=28
[state] restored paused=0 bedtime=0
[welcome] greeting greet-07
[welcome] ask ask-sgrc
[welcome] listening (mode)
[welcome] intent=story
[welcome] offering from 6 unheard stories
[welcome] listening (yesno)
[welcome] intent=yes
[welcome] playing chosen story anban-huri
[welcome] heard anban-huri (1 known)
```

By ear, in this order:

1. power on → a greeting plays → the question follows → say «հեքիաթ» → it offers
   an **unheard** story → say «այո» → it plays;
2. power on again → a **different** greeting;
3. unplug the router, power on → greeting, one short line, a story starts;
4. pause the toy in the dashboard, power on → **silence**;
5. hear every story, then power on → the «shall I tell it again?» line;
6. press the button mid-session → the story pauses/resumes on the SAME story,
   and the welcome flow does not re-run.

The pure decision logic (clip kinds, ask-id composition, voice manifest parse,
index v4 round-trip, **v3 forward compatibility**) is covered by
`content_sync_test.cpp` under `-DAREG_CONTENT_SYNC_TEST_BENCH`. The NVS and SD
halves — greeting rotation persistence, the heard set, the flow itself — are
hardware-only, as the music and clip sync were before them.

## Offline games (`offline_games.{h,cpp}`)

Three more fully-offline SD games beside the true/false quiz — no Wi-Fi, no
STT, no model, **no mic** — built on the same primitives as
`offline_quiz.{h,cpp}`: pre-rendered Armenian MP3s, the GREEN/RED answer
buttons, the same answer window, and the same loop discipline (clip → answer
window → feedback → re-ask **once** → quiet exit; never badger).

Gated behind **`-DAREG_OFFLINE_GAMES_BENCH`**, exactly like the quiz, so a
production image compiles **zero bytes** of it and stays byte-identical.
It also needs `AREG_PIN_BUTTON_YES` / `AREG_PIN_BUTTON_NO` in `config.h`;
with the flag but no pins it logs once and does nothing.

| game | id family | flow |
|---|---|---|
| mind-reader | `intro`, `q-root`/`q-<path>`, `g-<4 bits>`, `win`, `lose` | child thinks of one of 16 animals; the toy walks a 4-deep yes/no tree and guesses, then GREEN = right / RED = wrong |
| two-player buzzer | `intro`, `go`, `win-green`, `win-red`, `end-both`, `close` | a question clip from the existing `/quiz` bank plays, then `go`; the **first press** takes the round; 5 rounds, then the both-celebrated close |
| button Simon | `intro`, `your-turn`, `level-up-1..3`, `miss`, `best`, `done` (+ `tone-green` / `tone-red`) | the toy plays a tone sequence, the child echoes it on the buttons; length grows 2 → 6 |

**The Armenian source of truth is `backend/content/offline-games/game-clips.json`**
— that file's `id` scheme is the contract this module resolves against.
Nothing in the firmware invents an id, and a clip that is not on the card is
a logged no-op, so a partly-rendered card degrades instead of crashing.

### SD layout

```
<AREG_GAMES_CLIP_DIR>/<game-key>/<clip-id>.mp3
/games/mind-reader/q-root.mp3
/games/who-first/win-green.mp3
/games/button-simon/level-up-2.mp3
```

The per-game subdirectory is **not cosmetic**: four of the five games in
`game-clips.json` each define a clip called `intro`, so a flat
`/games/<id>.mp3` layout would collide. The game key is the JSON's own
top-level key, so no new naming vocabulary is introduced. Root and subdir
names are all `#define`s (overridable in `config.h`) so the layout can move
without touching the code.

### Design notes that are product rules, not style

- **The mind-reader tree is implicit in the clip ids.** GREEN/yes appends
  `1`, RED/no appends `0`; a node's id is the path so far, a guess leaf's id
  is the full 4-bit path. There is no node table and no animal table in RAM —
  the entire tree state is a **5-byte path string**.
- **The buzzer has no notion of player identity.** It knows only which
  *button* was pressed first and plays that *colour's* celebration. There is
  no loser branch to take, and no clip names a child.
- **The buzzer never reveals the quiz answer.** The question clip is only
  something to listen to while the players wait for «Հիմա՛»; the round is
  about speed, not correctness, so the `-y`/`-n` suffix is read and
  discarded.
- **A round nobody pressed in is passed over in silence.** There is no clip
  for a dead round and inventing one would be the toy commenting on children.
  Two silent rounds in a row ends the game quietly. The `end-both` / `close`
  clips are skipped entirely when zero rounds were played, so the toy never
  says «Ես հաշվեցի ձեր սեղմումները» about zero presses.
- **Simon's ramp is within-session only.** Nothing is persisted; every
  session starts at length 2 again. A wrong press ends the session with the
  warm `miss` clip and the reached length is the result — no score is stored
  and none is announced as a failure.
- Every claim in these games is derived from **measured button presses**. The
  mic is off in all three, so nothing may claim to have heard the child.

### Entry points and how a game starts

```c
void offline_games_tick();              // one game per boot, 30 s after boot, IDLE-only
void offline_games_run_mindreader();
void offline_games_run_buzzer();
void offline_games_run_simon();
```

`offline_games_tick()` is called from the IDLE branch of `loop()`, the same
shape as `offline_quiz_tick` / `sd_playback_tick`. Which game it runs is a
**build-time** pick, `-DAREG_OFFLINE_GAMES_PICK=1|2|3` (1 = mind-reader,
2 = buzzer, 3 = Simon; default 1).

Runtime game **selection UX is deliberately not invented here** — every
option (a long-press cycle, a spoken menu clip, a third button) adds either
an input gesture or an LED meaning the toy does not have today, and the rule
for this slice was no new state machine and no new LED vocabulary. Until that
decision is made at the bench, choosing a game is exactly like choosing which
bench harness is compiled in.

### Build

```
arduino-cli compile --fqbn "esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc" --build-property "compiler.cpp.extra_flags=-DAREG_OFFLINE_GAMES_BENCH -DAREG_OFFLINE_GAMES_PICK=1" ".\esp32\AregVoiceMvp"
```

### Open for bench day

- **Nothing here has run on hardware.** Compile-verified only.
- **The clips do not exist yet.** `game-clips.json` is still owner-review +
  listen-test pending, and no MP3 has been rendered. Every game therefore
  logs `clip missing` and exits quietly on today's card — correct
  degradation, but it also means none of the three can be bench-tested
  before the renders land.
- **Simon needs two tone clips that the JSON does not yet list**
  (`tone-green` / `tone-red`). There is no parameterised tone helper in
  `audio_io` (`audio_play_thinking_earcon` is one fixed 440 Hz earcon and
  `synth_write_tone` is file-static), so the two tones are short clips like
  everything else. They are non-verbal — no Armenian text to review, just two
  renders to add.
- The mind-reader `replay` clip is **not** played: honouring the invitation
  it makes needs the selection gesture above.
- Button-edge handling is inherited from the quiz verbatim: a press that
  happens *during* clip playback is not queued, because the answer buttons
  are only polled inside an answer window. Whether that feels wrong to a
  four-year-old is a listening question for the bench, not a code question.

## Known C1 limitations (deliberate, deferred)

- Buffered response playback — streaming comes later.
- One attempt per turn. No retry, no reconnect. A dropped
  Wi-Fi packet mid-upload trips the error clip.
- Button presses during UPLOADING / PLAYING / ERROR are
  ignored. No barge-in.
- Plain HTTP only. No TLS on the bench LAN.
- Wi-Fi SSID / password / device credentials compiled in.
- One device only. One voice identity.
- Amp output gain hardcoded conservative; raise in
  `audio_io.cpp`'s `SetGain` call if the speaker is too quiet.
