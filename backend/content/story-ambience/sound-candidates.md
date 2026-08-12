# Ambience sound candidates — free sources

**Status: 6 of 18 found. The owner has heard these six and has not yet chosen.**
Nothing is mixed into a story; `ambience-cues.json` still says `licence: TBD`
for every cue, and it stays that way until he picks by ear.

Searched 2026-08-12 on **Wikimedia Commons**, which was chosen over paid
libraries because every file there carries a public page naming its author and
licence — the provenance a product that will be sold actually needs.

## What passed

| cue sound | licence | file | author |
|---|---|---|---|
| `wind-gust` | CC0 | [Killiney Hill Storm Floris 20250804 1537.ogg](https://commons.wikimedia.org/wiki/File:Killiney_Hill_Storm_Floris_20250804_1537.ogg) | Karlunun |
| `rain-light` | CC0 | [Light Rain Distant Thunder July 5th 2016.wav](https://commons.wikimedia.org/wiki/File:Light_Rain_Distant_Thunder_July_5th_2016.wav) | https://freesound.org/people/kvgarlic/ |
| `thunder-distant` | CC BY 4.0 | [Nosferatu thunderclap - Richard Humphries.wav](https://commons.wikimedia.org/wiki/File:Nosferatu_thunderclap_-_Richard_Humphries.wav) | Richard Humphries |
| `river-shallow` | CC0 | [433589 jackthemurray stream-river-water-up-close](https://commons.wikimedia.org/wiki/File:433589_jackthemurray_stream-river-water-up-close.wav) | jackthemurray |
| `spring-trickle` | Public domain | [Water flowing pouring trickling.ogg](https://commons.wikimedia.org/wiki/File:Water_flowing_pouring_trickling.ogg) | stephan |
| `birds-dawn` | CC BY 4.0 | [Dawn chorus at Glencairnie big bungalow Craigmor](https://commons.wikimedia.org/wiki/File:Dawn_chorus_at_Glencairnie_big_bungalow_Craigmore_DM.ogg) | DivyaCM |

Each was auditioned as an 8-second clip taken 20% into the recording (the
opening of a field recording is often setup noise), levelled to -18 LUFS so
none was judged loud or quiet rather than good.

## What was rejected, and why it matters

**Licence.** Only CC0, public domain and plain CC-BY were accepted.
**CC BY-SA was excluded deliberately**: share-alike audio mixed into a
narration can make the finished story a derivative that must itself be
share-alike, which is not a licence this product can carry. Several otherwise
good recordings were dropped on that alone.

**Content.** Commons is an encyclopedia media library, not a sound-effects
library, so a keyword search returns music and news. Genuinely returned and
discarded: *Perseverance rover's SuperCam records wind on Mars* for open
country wind, *Daniel Birch - 03 - Trees In The Wind* (a music track) for wind
in leaves, *Major fire at Istanbul airport* for a cooking fire, and *Vintage
Spring Songs* for spring birds. A word match is not a sound match.

## The twelve still missing

`open-country-wind`, `wind-leaves`, `rain-storm`, `water-splash`,
`birds-spring`, `forest-day`, `forest-evening`, `village-morning`,
`village-yard`, `hall-murmur`, `door-knock`, `fire-cooking`.

**Commons is the wrong shape for these.** The everyday sound-effect —
a knock, a crackling fire, chickens in a yard, a room of people murmuring — is
exactly what an encyclopedia does not collect and what a sound library exists
for.

Worth noting: the CC0 files that DID work here are largely **mirrored from
Freesound** (the rain recording's author field is literally a freesound.org
profile URL). So the material is on Freesound; Commons is holding a thin copy
of it. `freesound.org` and `cdn.freesound.org` would need adding to the
environment's allowed domains, plus a free API token, to search it directly.

## If these six are used

CC-BY files (`birds-dawn`, `thunder-distant`) require crediting the author.
A credits line in the parent app or in `docs/` satisfies it — the child never
sees it, and the requirement is on distribution, not on playback.

## Reminder before anything is mixed

Adding ambience means **re-rendering every story and a fresh listen test**, and
the owner's approval of the current ten narrations is pinned to their exact
sha256 in `tools/quality-evidence/story-audio-listen-test-20260812.md`. New
bytes are a new file and need a new listen.
