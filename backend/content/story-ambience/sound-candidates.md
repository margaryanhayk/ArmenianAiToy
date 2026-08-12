# Ambience sound candidates — free sources

**Status: 18 of 18 found and auditioned. The owner has heard them all; he has
not yet chosen.** Nothing is mixed into a story, and every cue in
`ambience-cues.json` still reads `licence: TBD`. He chooses by ear.

Two sources, 2026-08-12. Both were picked because every file traces to a public
page naming its author and licence — the provenance a product that will be sold
actually needs.

| cue sound | licence | source | file | author |
|---|---|---|---|---|
| `birds-dawn` | CC BY 4.0 | Commons | [Dawn chorus at Glencairnie big bun](https://commons.wikimedia.org/wiki/File:Dawn_chorus_at_Glencairnie_big_bungalow_Craigmore_DM.ogg) | DivyaCM |
| `birds-spring` | Public domain | Commons | [Birdsong mild sunny day.ogg](https://commons.wikimedia.org/wiki/File:Birdsong_mild_sunny_day.ogg) | stephan |
| `door-knock` | Public domain | Commons | [Knocking on wood or door.ogg](https://commons.wikimedia.org/wiki/File:Knocking_on_wood_or_door.ogg) | stephan |
| `fire-cooking` | CC BY 3.0 | Commons | [Campfire sound ambience.ogg](https://commons.wikimedia.org/wiki/File:Campfire_sound_ambience.ogg) | Glaneur de sons |
| `forest-day` | CC0 | Freesound | [#679753 Strong Wind Through Spruce With Bi](https://freesound.org/people/Sotiris_Laskaris/sounds/679753/) | Sotiris_Laskaris |
| `forest-evening` | CC BY 3.0 | Commons | [Keoka070923Crickets+1.ogg](https://commons.wikimedia.org/wiki/File:Keoka070923Crickets%2B1.ogg) | Keoka |
| `hall-murmur` | CC0 | Commons | [Cafe ambiance.ogg](https://commons.wikimedia.org/wiki/File:Cafe_ambiance.ogg) | Marble Toast |
| `open-country-wind` | CC0 | Freesound | [#847257 Wind Over Holehead Hill](https://freesound.org/people/hidetora/sounds/847257/) | hidetora |
| `rain-light` | CC0 | Commons | [Light Rain Distant Thunder July 5t](https://commons.wikimedia.org/wiki/File:Light_Rain_Distant_Thunder_July_5th_2016.wav) | https://freesound.org/pe |
| `rain-storm` | CC0 | Freesound | [#768870 heavy_rain_outside](https://freesound.org/people/Gustavo_C/sounds/768870/) | Gustavo_C |
| `river-shallow` | CC0 | Commons | [433589 jackthemurray stream-river-](https://commons.wikimedia.org/wiki/File:433589_jackthemurray_stream-river-water-up-close.wav) | jackthemurray |
| `spring-trickle` | Public domain | Commons | [Water flowing pouring trickling.og](https://commons.wikimedia.org/wiki/File:Water_flowing_pouring_trickling.ogg) | stephan |
| `thunder-distant` | CC BY 4.0 | Commons | [Nosferatu thunderclap - Richard Hu](https://commons.wikimedia.org/wiki/File:Nosferatu_thunderclap_-_Richard_Humphries.wav) | Richard Humphries |
| `village-morning` | Public domain | Commons | [Medium rooster crowing.ogg](https://commons.wikimedia.org/wiki/File:Medium_rooster_crowing.ogg) | alys |
| `village-yard` | Public domain | Commons | [Corner of a sheep field in summer.](https://commons.wikimedia.org/wiki/File:Corner_of_a_sheep_field_in_summer.ogg) | earthcalling |
| `water-splash` | CC0 | Freesound | [#425140 Small Splash (field recording walk](https://freesound.org/people/gis_sweden/sounds/425140/) | gis_sweden |
| `wind-gust` | CC0 | Commons | [Killiney Hill Storm Floris 2025080](https://commons.wikimedia.org/wiki/File:Killiney_Hill_Storm_Floris_20250804_1537.ogg) | Karlunun |
| `wind-leaves` | CC0 | Freesound | [#378725 Leaves Rustling in Wind - Zoom H6.](https://freesound.org/people/lunchmoney/sounds/378725/) | lunchmoney |

Auditioned as clips levelled to -18 LUFS so none was judged loud or quiet
rather than good. Long beds sampled ~25% in (a field recording often opens
with setup noise); short one-shots kept whole.

## Licence rule

**CC0, public domain and plain CC-BY only.** CC BY-SA is excluded
deliberately: share-alike audio mixed into a narration can make the finished
story a derivative that must itself be share-alike — not a licence this product
can carry. Both heavy-rain candidates on Commons were dropped on that alone,
which is why that sound came from Freesound in the end.

The CC-BY files (`birds-dawn`, `thunder-distant`, `fire-cooking`,
`forest-evening`) require crediting the author on distribution. A credits line
in the parent app or in `docs/` satisfies it; the child never sees it.

## What the search actually taught

**Wikimedia Commons is an encyclopedia, not a sound library.** Searching it for
sounds returns articles about them: *Perseverance rover's SuperCam records wind
on Mars* for open-country wind, *Major fire at Istanbul airport* for a cooking
fire, and music tracks whose titles contained the word — *Daniel Birch - 03 -
Trees In The Wind*, and *Magnetic Myths - Red Sleaves*, which matched "leaves"
inside "Sleaves". Walking its category tree instead (Sound effects, Ambient
sounds, Soundscape, Audio files of animal sounds, the Dougherty Natural Sounds
Collection, weather and water) gave ~4,900 real audio files to match against,
and that is what found 14 of them.

**Two of my own filters were wrong** and had silently discarded good results: a
150 kB size floor rejected every SHORT sound — a rooster crow and a door knock
are two-second files, both public domain — and requiring an `audio/*` MIME type
rejected files Commons serves as `application/ogg`, which is most of them.

**Freesound closed the last four**, plus replaced a bad Commons pick. Its API
filters by licence server-side (`license:"Creative Commons 0"`), so licence
guessing disappears entirely. But **sorting by rating is not enough**: the first
pass returned a passing truck for "wind in leaves", a bird recording for "open
country wind", and a night thunderstorm for "forest day". Requiring the name and
tags to contain the thing — and to exclude the obvious intruders (`truck`,
`engine`, `wing`, `night`, `thunder`) — is what made it land.

## One file found and deliberately rejected

The Commons `forest-day` was *toucans singing in the Amazonian rainforest* —
CC0, good quality, wrong continent. A toucan in an Armenian forest is the same
category of error as a narrator who does not speak Armenian. Replaced with
*Strong Wind Through Spruce With Birds*.

## A quality caveat, stated plainly

The Freesound files are the API's **preview MP3s**; the original WAV needs an
OAuth2 browser flow. For an ambience bed this is acceptable — the cue sheet's
design is 3-5 seconds at low level under narration, and the finished story is
re-encoded to 192 kbps anyway. It would NOT be acceptable for narration. If a
chosen sound ends up sitting high in a mix, fetch that one properly first.

## Before anything is mixed

Adding ambience means **re-rendering every story and a fresh listen test**. The
owner's approval of the current ten narrations is pinned to their exact sha256
in `tools/quality-evidence/story-audio-listen-test-20260812.md`; new bytes are a
new file and need a new listen.
