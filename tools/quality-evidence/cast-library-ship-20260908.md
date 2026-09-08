# Cast library SHIPPED — all nine remaining stories (2026-09-08)

Owner 2026-09-08: "For now these are good (there are minor bugs, but at this point I
don't want to change)" — then "Ship". With «Ուլիկը» (2026-09-07) the whole library
now narrates with the cast. The pilots are in `cast-pilots-v1-20260907.md`.

## What is on disk

| story | Version | sha256 | bytes | length |
|---|---|---|---|---|
| anban-huri | 10 | `c5680e7b10bbc92938d07ff031217d1368ba36b24ad8347754b5e557ba81c555` | 6023044 | 4:10 |
| princess-and-pea | 7 | `28d0ab9161483480616451774fccebf53c8728f9461b5bae0b3840cb9ff74b3f` | 1638862 | 1:08 |
| little-cloud | 4 | `03e27b572b5c6fa80ed0b1cab8e966d0bba4a345121f78c5da02a9d209d317ce` | 525418 | 0:21 |
| sutlik-orskan | 10 | `d7482812c24cea9eebfbcea3eafc627734198d770d85d07b9957c6b1a287e4cb` | 3695221 | 2:33 |
| sutasan | 7 | `a7af8a603fc75bf6fda254694c5bdda4d378c22c405364dba670bb26d1f6947a` | 2166117 | 1:30 |
| pochat-aghves | 10 | `75f61439210cb7e18f8a5b76f93d228b5be614de4f957087a9770a16e8701711` | 6360964 | 4:25 |
| khosogh-dzuk | 11 | `e95e1291de4584641ea57330cb5fca7cdbba1cc0c27f289e620dc539f3422146` | 9347701 | 6:29 |
| three-piglets | 7 | `a0043a770622fea31d7a7216e8ee898c138fadd894364cd2ef910220d201418a` | 2038849 | 1:24 |
| hedgehog-apple | 4 | `424b9a0c70978aae50d926decc98d6eb7cee31f59d2ada174bc26f02725d2823` | 596889 | 0:24 |

Each shipped file IS the narration the owner heard, minus the appended summary clip (the toy
plays the summary clip after the story on its own). The clips are unchanged — the summary he
heard appended was the shipped one. Beside each: the byte map from `segments_to_bytes.py`
against the shipped file and, where cues exist, the mixer's already-mixed marker. The stale
`.words.json` alignments of the old narrations (khosogh-dzuk, princess-and-pea, sutasan,
three-piglets) are removed.

## Gates (run here, no PowerShell)

| check | result |
|---|---|
| `check_story_audio.py` | PASS 10/10, 192 kbps, one ID3 tag, 101–128% of expected length |
| integrated loudness | −16.6..−16.8 LUFS (library contract −16.4) |
| true peak | −1.6..−1.7 dBTP |
| `dotnet test` | 2779 / 2779 |

## Not done

- The listen test on the TOY. Every toy re-downloads ~32 MB of narration.
- The minor bugs the owner heard are not recorded; he chose not to change anything now.
- «Փոքրիկ ամպիկը» and «Ոզնիկն ու խնձորը» ship without ambience (no cues drafted).
- The OpenAI-TTS stream caches (`StoryAudio:CacheRoot`) are separate renders, untouched.
