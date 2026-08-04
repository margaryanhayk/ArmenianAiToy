# Device-global voice clips (the welcome flow)

Every line Areg speaks at power-on lives here as **text**, is rendered once to
MP3, and is synced to the toy's SD card under `/voice`. Nothing at runtime reads
this folder — it exists so the Armenian is reviewable in one place and a diff
shows honestly what changed.

## Why pre-rendered and not runtime TTS

The greeting has to work with no network, cost nothing, add no delay, and be in
the voice we chose. Runtime synthesis fails all four. It also means a **paused**
toy stays silent for free: there is no request to suppress.

## The id carries the role

| id | what it says | count |
|---|---|---|
| `greet-01` … `greet-NN` | a power-on hello, rotated, never the same one twice running | 39 at launch |
| `ask-sgrc`, `ask-s`, … | «what shall we do?», naming exactly the modes the parent left on. Letters in fixed order **s,g,r,c** | 2 at launch (see below) |
| `ask-any` | the generic «what shall we do?», used when the exact combination has no recording | 1 |
| `say-again` | «I didn't catch that» — spoken once, before the second and last try | 1 |
| `just-story` | the graceful default: «then let me tell you a story» | 1 |

`greet-` is the only **prefix** the firmware matches; everything else is looked
up by exact id. So adding greeting #25 is a config edit with **no firmware
change**.

## Two counts that are deliberate, not lazy

**39 greetings.** These are the owner's own set (2026-08-05), reviewed and
ranked. ~70 were submitted; the rest were cut for asking a question (the ask
clip plays right after — two questions in a row loses a small child), for
crossing the companion boundary, for being calques, or for being near-duplicates
of a line already in.

The pool is **not** padded to fill the remaining slots, on the reviewer's
advice: *a child notices two greetings that say the same thing sooner than a
missing one*, so near-duplicates make the rotation feel smaller, not bigger.

Sizing was measured, not guessed. `CS_MAX_VOICE` is 48 slots; every slot costs
~384 bytes across three firmware tables:

| slots | free RAM on the toy |
|---|---|
| 32 | 157,680 B |
| **48** | **149,232 B** |
| 64 | 140,784 B |

An earlier draft landed at 110,512 B free — too little on a board that also
wants 40–50 KB for a TLS handshake while audio is playing — but the cause was
duplicated scratch tables, not the bound. With those shared, 48 is comfortable.

**2 ask variants, not 15.** All 15 combinations of the four mode flags are
*supported*; only `ask-sgrc` and `ask-any` are *shipped*. A parent disabling
Story on a storytelling toy is rare, and Game / Riddle / Curiosity have no
offline content yet, so any other combination falls back to `ask-any`. Add the
missing ones when a real parent config needs them.

## Two things that are NOT here, and why

**Bedtime greetings.** The owner submitted a "calm & bedtime" group. The toy is
deliberately **silent inside the parent's bedtime window** — a cheerful hello at
21:30 is exactly what loses a parent's trust — so those lines would never play.
The four that were not bedtime-specific were salvaged into the daytime pool; the
rest are unused. Changing this means changing the silence rule, which is a
product decision, not a content one.

**Greetings that say the child's name.** «Բարև՛, Անի՛։ …» is lovely and does not
work with pre-rendered clips: the manifest is static config shared by every toy,
so a name-specific clip would have to be rendered per child and delivered only
to that child's device. That needs per-device entitlement (deliberately deferred
on the ContentSync contract) plus a render job triggered when a parent creates a
child profile. Real work, real recurring API cost, and a new failure mode — a
toy that greets the wrong child by name is worse than one that greets nobody by
name. It is a good v2 feature, not a content edit.

## Per-story offer lines

The two clip kinds `offer` and `reoffer` are **per story**, not here — they live
in `ContentSync:Stories[].Clips` beside `intro` / `question` / `summary`, and
are rendered with the story. They are what let the toy say a story's title out
loud with no runtime TTS:

- `offer` — «Ուզո՞ւմ ես լսել «X»-ը։» for a story the child has not heard
- `reoffer` — «Մենք արդեն լսել ենք «X»-ը, բայց եթե ուզում ես, նորից կպատմեմ։»
  once the whole shelf has been heard

A story with no `offer` clip is simply played rather than offered — a missing
recording must never be the reason a child hears nothing.

## Rendering

```powershell
# 1. draft/review the text in voice-clips.json
# 2. dry run (default; costs nothing)
dotnet run --project tools/ElevenLabsRender -- --voice-clips

# 3. real render (paid, two-man rule)
dotnet run --project tools/ElevenLabsRender -- --voice-clips --render --confirm-paid-api

# 4. check + level + install (NON-NEGOTIABLE)
pwsh tools/story-audio/Ship-StoryAudio.ps1 -In <render folder> -Fix
pwsh tools/story-audio/Ship-StoryAudio.ps1 -In <render folder> -Apply
```

The loudness check matters more here than anywhere else: 28 short clips at the
level the API returns, interleaved with narration at **−16.4 LUFS**, would make
the toy sound broken in a way no single file reveals.

**Re-rendering an existing id MUST bump its `Version`**, or every toy keeps its
cached copy and nothing changes.

## Then listen

To all of them, end to end, in one sitting. Twenty-four greetings is exactly the
batch size at which "they all sounded fine" stops being a real statement.
