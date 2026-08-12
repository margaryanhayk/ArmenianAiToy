# Ambience sound candidates — free sources

**Status: 14 of 18 found and auditioned. The owner has heard them; he has
not yet chosen.** Nothing is mixed into a story and every cue in
`ambience-cues.json` still reads `licence: TBD`. He chooses by ear.

Source: **Wikimedia Commons**, 2026-08-12. Chosen over paid libraries because
every file carries a public page naming author and licence — the provenance a
product that will be sold actually needs.

## Found

| cue sound | licence | file |
|---|---|---|
| `birds-dawn` | CC BY 4.0 | [Dawn chorus at Glencairnie big bungalow Craigm](https://commons.wikimedia.org/wiki/File:Dawn_chorus_at_Glencairnie_big_bungalow_Craigmore_DM.ogg) |
| `birds-spring` | Public domain | [Birdsong mild sunny day.ogg](https://commons.wikimedia.org/wiki/File:Birdsong_mild_sunny_day.ogg) |
| `door-knock` | Public domain | [Knocking on wood or door.ogg](https://commons.wikimedia.org/wiki/File:Knocking_on_wood_or_door.ogg) |
| `fire-cooking` | CC BY 3.0 | [Campfire sound ambience.ogg](https://commons.wikimedia.org/wiki/File:Campfire_sound_ambience.ogg) |
| `forest-day` | CC0 | [404114 felix-blume toucans-singing-in-the-amaz](https://commons.wikimedia.org/wiki/File:404114_felix-blume_toucans-singing-in-the-amazonian-rainforest-brazil.ogg) |
| `forest-evening` | CC BY 3.0 | [Keoka070923Crickets+1.ogg](https://commons.wikimedia.org/wiki/File:Keoka070923Crickets%2B1.ogg) |
| `hall-murmur` | CC0 | [Cafe ambiance.ogg](https://commons.wikimedia.org/wiki/File:Cafe_ambiance.ogg) |
| `rain-light` | CC0 | [Light Rain Distant Thunder July 5th 2016.wav](https://commons.wikimedia.org/wiki/File:Light_Rain_Distant_Thunder_July_5th_2016.wav) |
| `river-shallow` | CC0 | [433589 jackthemurray stream-river-water-up-clo](https://commons.wikimedia.org/wiki/File:433589_jackthemurray_stream-river-water-up-close.wav) |
| `spring-trickle` | Public domain | [Water flowing pouring trickling.ogg](https://commons.wikimedia.org/wiki/File:Water_flowing_pouring_trickling.ogg) |
| `thunder-distant` | CC BY 4.0 | [Nosferatu thunderclap - Richard Humphries.wav](https://commons.wikimedia.org/wiki/File:Nosferatu_thunderclap_-_Richard_Humphries.wav) |
| `village-morning` | Public domain | [Medium rooster crowing.ogg](https://commons.wikimedia.org/wiki/File:Medium_rooster_crowing.ogg) |
| `village-yard` | Public domain | [Corner of a sheep field in summer.ogg](https://commons.wikimedia.org/wiki/File:Corner_of_a_sheep_field_in_summer.ogg) |
| `wind-gust` | CC0 | [Killiney Hill Storm Floris 20250804 1537.ogg](https://commons.wikimedia.org/wiki/File:Killiney_Hill_Storm_Floris_20250804_1537.ogg) |

Auditioned as clips levelled to -18 LUFS so none was judged loud or quiet
rather than good. Long beds sampled 20% in (a field recording often opens with
setup noise); short one-shots — the knock, the rooster — kept whole.

## Still missing (4)

`open-country-wind`, `wind-leaves`, `rain-storm`, `water-splash`

Wind and heavy rain are genuinely scarce on Commons under a usable licence:
almost every good one is **CC BY-SA**, which is excluded (see below). Water
splash returned nothing at all.

## One found file that should NOT be used

`forest-day` resolved to *toucans singing in the Amazonian rainforest* — CC0,
good quality, and **the wrong continent**. A toucan in an Armenian forest is
the same category of error as a narrator who does not speak Armenian. Listed
here so it is rejected deliberately rather than rediscovered later.

## How the search actually went, so it need not be repeated

**Keyword search failed.** Commons is an encyclopedia media library, so
searching for sounds returns articles about them. Genuinely returned:
*Perseverance rover's SuperCam records wind on Mars* for open-country wind,
*Major fire at Istanbul airport* for a cooking fire, and — twice — music
tracks whose titles contained the word: *Daniel Birch - 03 - Trees In The
Wind*, and *Magnetic Myths - Red Sleaves*, which matched "leaves" inside
"Sleaves".

**Category walking worked.** Enumerating `Category:Sound effects`,
`Category:Ambient sounds`, `Category:Soundscape`,
`Category:Audio files of animal sounds`, the Dougherty Natural Sounds
Collection and the weather/water trees gave a pool of ~4,900 audio files to
match against, instead of guessing search terms.

**Two of my own filters were wrong and cost real results:**

- Rejecting anything under 150 kB threw away every SHORT sound. A rooster crow
  and a door knock are two-second files; both are public domain and both were
  initially discarded by my own rule.
- Requiring a `audio/*` MIME type rejected files Commons serves as
  `application/ogg`, which is most of them.

**xeno-canto species recordings (`XC12345`) are excluded** — a single bird in
isolation is a specimen, not a place.

## Licence rule applied

CC0, public domain and plain CC-BY only.

**CC BY-SA is excluded deliberately.** Share-alike audio mixed into a
narration can make the finished story a derivative that must itself be
share-alike — not a licence this product can carry. Several good recordings
were dropped on that alone, including both heavy-rain candidates.

The CC-BY files require crediting the author on distribution. A credits line
in the parent app or in `docs/` satisfies that; the child never sees it.

## Where the rest of them are

The CC0 files that worked here are largely **mirrored from Freesound** — one
is literally named `Dobroide - 20060824.forest03 (cc-by) (freesound).wav`, and
the rain recording's author field is a freesound.org profile URL. The material
lives there; Commons holds a thin copy. `freesound.org` and
`cdn.freesound.org` on the environment's allow-list, plus a free API token,
would open it.

## Before anything is mixed

Adding ambience means **re-rendering every story and a fresh listen test**. The
owner's approval of the current ten narrations is pinned to their exact sha256
in `tools/quality-evidence/story-audio-listen-test-20260812.md`; new bytes are
a new file and need a new listen.
