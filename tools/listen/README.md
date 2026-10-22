# Listen test

CLAUDE.md: "Human listen test gates any shipped audio. No tool can hear a
seam." Nobody ran it because 240 files and a blank page is a chore. This tool
makes it a loop and keeps the books. **A human ear is the gate; the tool only
records what the human typed.** It never writes to an audio file or to
`appsettings.json`.

## Two commands

```bash
python3 tools/listen/listen_test.py run --group stories --device "PC headphones" --listener "Your Name"
python3 tools/listen/listen_test.py status
```

`run` plays the next file you have not answered yet (ffplay by default), then
asks `PASS / FAIL / SKIP / REPLAY (p/f/s/r)`. `q` or Ctrl+C stops; every
verdict is already on disk, and running the same command resumes. A note can
ride along: `f click at 0:42`.

| flag | meaning |
|---|---|
| `--group` | `stories` `alts` `clips` `voice` `games` `music` `all` (default) |
| `--only ask` | only ids containing that text |
| `--player` | `ffplay` (default), `mpv`, or any command containing `{file}` |
| `--device` | what you listen on. The toy's own speaker is the honest test |
| `--again` | also re-ask files already APPROVED (e.g. a second pass on the toy) |
| `--date YYYYMMDD` | evidence date, default today (local time) |

`status` prints every advertised file with its state, then a summary and the
minutes of audio still to hear. Exit code 0 only if **every** file is APPROVED.

| state | meaning |
|---|---|
| APPROVED | latest PASS/FAIL on the file's **current bytes** is PASS |
| FAILED | latest PASS/FAIL on the current bytes is FAIL |
| CHANGED | heard before, but never these bytes: the approval is void, listen again |
| NEVER HEARD | no PASS/FAIL row |
| MISSING | advertised in the catalogue, not on disk |

SKIP rows are kept but never count for or against a file.

## What it listens to

The `ContentSync` catalogue in `backend/src/ArmenianAiToy.Api/appsettings.json`,
resolved under its `AudioRoot` (`backend/src/ArmenianAiToy.Api/story-audio/`),
i.e. exactly what toys download: enabled, not `Retired`, `SizeBytes > 0`,
valid sha256. Placeholders (the six Tsivik rows) are not listened to.

- `stories` / `alts`: `Stories[]` without / with `AltOf`
- `clips`: the per-story clips inside `Stories[].Clips[]` (ids `<story>/<kind>`).
  Decision: they are their own group because they live under the story, not in
  `Games`, and they are short enough to review as a set per story
- `games`: `Games[]`, which also holds the mid-story `story-pauses` clips
- `voice`, `music`: the namespaces of the same name

Not covered: operator-uploaded `ContentItem` rows (they live in the database
on Railway, not in git).

## Where the evidence lands

`tools/quality-evidence/listen-test-<YYYYMMDD>.md`: a header (date, listener,
device, player, git commit) and one table row per verdict:

`| id | namespace | path | sha256 | seconds | verdict | note |`

The sha256 is of the file's bytes at the moment of the verdict, so any later
re-render turns that row into CHANGED. If the device, listener or player
changes during the day, a new header block is appended to the same file.
The tool does not commit anything; commit the evidence file yourself.

## Tests

```bash
python3 -I tools/listen/test_listen_test.py -v
```

stdlib only, synthetic temp dirs, no ffplay, no real audio. Runs in CI under
`python-tool-tests`.
