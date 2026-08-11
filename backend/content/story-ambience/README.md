# Story ambience — where the forest and the lake go

**Nothing at runtime reads this folder.** It is the reviewable source for the
sound that gets mixed into each story's narration during the next render, in the
same way `voice-clips/` and `offline-games/` hold the words before they are
rendered.

Owner request, 2026-08-10: *"During story telling I want to add some sounds. For
example if there is story in forest, some forest sounds, lake sound…"*

---

## The two decisions this folder is built on

**1. The sound is mixed into the story file, not played by the toy.**

The toy *could* mix a background loop under the narration — there is a cheap
route through a PCM shim over the existing decoder, and PSRAM has room. But
every playback function on the device builds its audio output as a local and
tears the I2S peripheral down on return, and every other sound the toy makes
(game clips, story pauses, greetings) runs through that same function as a
*nested* call while a story is in progress. Making a background bed survive all
that means turning a hard invariant into a lifecycle, on a device that already
has four features switched off for want of bench time.

Mixing at render costs no firmware change, no device memory, and no new way for
a story to break — and it buys the one thing live mixing cannot: a person
ducking the sound under a dense sentence, fading it at the scene change, and
putting the splash exactly on the word.

**2. Sparse, not a continuous bed.**

Establish a place in three to five seconds, then get out of the voice's way. A
forest that runs under four minutes of Armenian narration fights the voice for
the same midrange on a small mono speaker, and the listeners who lose that fight
first are the four-year-olds. Ambience sets a scene; one-shots punctuate;
neither competes.

**The narration is always the loudest thing in the file.** If a cue makes a
sentence harder to understand, the cue is wrong.

---

## How to read `ambience-cues.json`

```jsonc
{ "segment": 0,          // index into the story's segments[] array
  "at": "start",         // "start" | "end" of that segment
  "kind": "scene",       // "scene" = establish a place, then fade out
                         // "oneshot" = a single effect on a moment
  "sound": "forest-day", // an id from the "sounds" list in the same file
  "seconds": 5,          // how long it establishes before fading
  "level": -20,          // dB relative to the narration. Always negative.
  "holdUnder": false,    // true = stay very low under the narration afterwards
  "cueLine": "Խոր անտառում մի այծ է լինում։",  // the line it lands on
  "note": "why this cue exists" }
```

`segment` + `cueLine` together, rather than a timestamp, on purpose: the eight
shipped stories were rendered outside this repo and have no `.segments.json`
byte map, so there are no timings to key to — and a segment index and a quoted
line both survive the re-render, when every timestamp would not.

## The tool that reads this file

`tools/story-audio/mix_ambience.py` takes the per-segment WAVs, this cue sheet
and a folder of sounds, and produces one mixed story plus the
`<storyId>.segments.json` map (in seconds — `segments_to_bytes.py` turns that
into the byte offsets the backend reads, once the MP3 exists). **Dry run by default** — it prints every cue's
resolved time and the exact ffmpeg command and writes nothing. It does not
level: -16.4 LUFS stays with `Ship-StoryAudio.ps1`, after the mix.

```
python3 tools/story-audio/mix_ambience.py --self-test
python3 tools/story-audio/mix_ambience.py --story ulik \
    --segments-dir <wavs> --sounds-dir <sounds> --out mixed
```

It warns when two cues land within 2 seconds of each other. That check exists
because the first run of this sheet found one: a cue at the **end** of segment 1
and another at the **start** of segment 2 are the *same instant*, which is not
obvious when reading the JSON. The `forest-evening` cue in «Ուլիկը» was moved to
the start of its segment because of it.

## What a mix session needs

1. The sounds themselves (see `sounds` in the JSON — none are chosen yet).
2. This cue sheet, owner-approved.
3. The narration, **one WAV per segment**, full length, in the chosen voice —
   see `docs/voice-narrator-brief.md` §3 for why that shape and not one file
   per story.
4. Mix, **then** normalise the finished file to **-16.4 LUFS** — the level every
   story in the library sits at. Never level the narration and the ambience
   separately.
5. `tools/story-audio/Ship-StoryAudio.ps1 -Fix -Apply`, which re-stamps sha256,
   size and `Version`. A new file with an old `Version` is a file no toy will
   ever download.
6. The human listen test. Still the last gate, as everywhere else.

## Licensing — an open cost decision

Every sound must be usable in a product that is given away now and sold later.
CC0 (Freesound and similar) is the place to start and costs nothing; a paid
library (Epidemic, Artlist, Soundly) buys consistency and saves search time.
**Nothing here is chosen yet** — the `licence` field on every sound reads `TBD`
deliberately, so that a file cannot quietly reach a child's toy without someone
having answered the question.

## Deliberate omissions

Three sounds a naive pass would have added, and why they are not here:

- **No gunshot in «Սուտլիկ որսկանը»**, at «մին էլ տրա՜ք, որ կրակեց». It is a
  comic tall tale for four-year-olds; a bang is the one sound in this library
  that could genuinely startle a child in a dark room.
- **No wolf howl in «Ուլիկը» or «Երեք խոզուկները».** Both stories already carry
  their menace in the narration. Adding a howl makes them frightening, not
  atmospheric, and the toy's whole posture is calm.
- **No frogs in «Անբան Հուռին»**, even though the story is full of them — the
  narrator *voices* them («Փե՛փել… Կե՛կել…»). A real frog on top would talk over
  the joke.

## Coverage

Eight stories have narration audio and have cues drafted here.
`hedgehog-apple` and `little-cloud` are in the story library but have **no
narration audio shipped**, so they have no cues yet — add them in the same pass
that first renders them.
