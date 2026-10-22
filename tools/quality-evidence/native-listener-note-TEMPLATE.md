# Native-listener note — SHIP.md A5

**Status: NOT YET PERFORMED.** This file is a blank template. The listening is
the acceptance gate and it is a human act: an agent must not fill in the name,
the date or the verdict. Rename this file to
`native-listener-note-<YYYYMMDD>.md` when the note is filled in.

SHIP.md A5: *a native Eastern Armenian adult, not you, listens to 10 minutes of
output and says it sounds natural. A written note from them counts as evidence.*

## Who may listen

- A **native Eastern Armenian** speaker, an **adult**, who is **not the owner**.
- They hear the files below as they are. They do not see the story text first and
  they are not told which voice is which.
- They write the note themselves (3 lines, below), or dictate it and sign off
  on the text.

## The 10-minute playlist (current shipped files)

All paths are under `backend/src/ArmenianAiToy.Api/story-audio/`. Each sha256 was
checked on 2026-10-22 against the `ContentSync` row in
`backend/src/ArmenianAiToy.Api/appsettings.json`, so these are the bytes a toy
downloads today. If a file's sha256 differs from the table when the listening
happens, the file changed: re-run `sha256sum`, record the new value in the note
and say so.

| # | File | Duration | sha256 |
|---|---|---|---|
| 1 | `khosogh-dzuk.mp3` | 6:29 (389.5 s) | `e95e1291de4584641ea57330cb5fca7cdbba1cc0c27f289e620dc539f3422146` |
| 2 | `sutlik-orskan.mp3` | 2:34 (153.9 s) | `d7482812c24cea9eebfbcea3eafc627734198d770d85d07b9957c6b1a287e4cb` |
| 3 | `voice/greet-04.mp3` | 0:02 (2.4 s) | `ff1ff46ebe06cc71c4254d89dcf1552fa9d93c4857556ff3f5c99a022ddec79c` |
| 4 | `voice/ask-sgrc.mp3` | 0:06 (6.2 s) | `615b2ffd07e12a65379feb2b7f845dfbd47250d67a37186911be07f1d073411f` |
| 5 | `clips/pochat-aghves/offer.mp3` | 0:03 (2.9 s) | `363c258921316234f30e4ebe5949809da0e564489eb4c14bc89d255c426e75a7` |
| 6 | `princess-and-pea.mp3` | 1:08 (68.3 s) | `28d0ab9161483480616451774fccebf53c8728f9461b5bae0b3840cb9ff74b3f` |

Durations measured with `ffprobe` on 2026-10-22. Items 1 to 5 total 9:15; item 6
is added so the listening reaches 10 minutes (items 1 to 6 total 10:22).

Recompute any row with:

```bash
cd backend/src/ArmenianAiToy.Api/story-audio
sha256sum khosogh-dzuk.mp3 sutlik-orskan.mp3 voice/greet-04.mp3 voice/ask-sgrc.mp3 \
  clips/pochat-aghves/offer.mp3 princess-and-pea.mp3
ffprobe -v error -show_entries format=duration -of csv=p=0 khosogh-dzuk.mp3
```

The mix is deliberate: two long cast-and-ambience stories (items 1 and 2), the
short story (item 6), and the three short live-flow clips a child hears around a
story (greeting, the question prompt, the story offer).

## The note (3 lines)

```
Name:
Date:
Sounds natural (yes / no):
What did not (word, file, minute; "nothing" is a valid answer):
```

## Rules for the note

- `yes` means the listener would be comfortable with a child hearing it as is.
  Any `no`, or any word in "What did not", is a finding: record it, do not edit
  the story text to chase it (story text is byte-frozen except by owner edit;
  a text edit costs a re-render and a fresh listen).
- A note about a different set of files, or about files whose sha256 no longer
  matches the table, is evidence for those files only.
- Keep the filled note in this directory as
  `native-listener-note-<YYYYMMDD>.md` and point SHIP.md A5 at it (the owner
  edits SHIP.md, not an agent).
