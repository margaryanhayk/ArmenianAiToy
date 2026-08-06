# Serial hero — «Ծիվիկի մեծ ճանապարհը» (drafts)

Armenian TEXT source for the six-episode day-story serial (owner batch
item 7). **Nothing at runtime reads this folder** — same convention as
`../voice-clips/`, `../quiz-questions/`, and `../variant-endings/`: the
Armenian lives here so the owner reviews words, not renders.

**Status: DRAFTS.** armenian-story-master authored + self-reviewed
2026-08-06. Pending the owner's text review, then the sample-first listen
test (see `_renderNote` in `tsivik-series.json`). No firmware, no
manifest entries, no render yet.

## The hero and his want

**Ծիվիկ** — a young swallow with wings still growing. His want is stated
in the first minute of episode one and never wavers: **to see the sea
with his own eyes**, the sea his grandmother describes every evening
(«Ծովը երգում է՝ շը-շը, շը-շը»). Original character: not folklore, not
Katrin/Vardan, not a talking toy. Every helper on the road is likewise
original and unnamed-or-invented (the stork, Պուտիկ the ladybug, the old
crow, Ալիկ the gull — «Ալիկ» winks at «ալիք»).

## The arc — six self-contained legs of one road

Each episode is a complete mini-adventure with its own emotional
resolution; the open door at the end is **curiosity, never anxiety** —
Tsivik goes to sleep content and *decides* what he will try tomorrow.
A child who hears only one episode gets a whole story.

| # | Title | Self-contained arc | Open door |
|---|---|---|---|
| 1 | Մեծ երազանքը | Three tries to reach the walnut tree — he makes it; grandmother blesses the dream («ամեն մեծ ճանապարհ սկսվում է մի փոքրիկ թռիչքից») | Tomorrow: the river |
| 2 | Գետը և քամին | Fear of the wide river; the stork teaches gliding — befriend the wind, don't fight it; he crosses | What is the golden field whispering? |
| 3 | Ոսկե արտի երգը | Detour to carry lost ladybug Պուտիկ home; helping ≠ losing time («սիրտս այնպես լիքն է, կարծես մի ամբողջ ծով է մեջը») | The mountain where clouds sleep; a silver wonder beyond it |
| 4 | Ամպերի սարը | Cold climb; the old crow shows the warm pass, Tsivik shares his raspberry — kindness exchanged both ways; they reach the shoulder | Is the silver ribbon the sea? |
| 5 | Արծաթե ժապավենը | Disappointment handled gently: it's a lake, not the sea; Ալիկ the gull gives the salt-wind secret; sadness resolves into knowing the way («կանգառը վերջ չէ») | Tomorrow at dawn: the last road |
| 6 | Ծովը | Arrival, joy, and the quiet turn: he got here because of kind hearts along the road; the new want is to fly home and SHARE it | Soft series door: «աշխարհում դեռ շա՜տ մեծ ճանապարհներ կան» (a second season fits without a cliffhanger) |

Emotional themes, one per episode, all age 4–7 concrete: persistence,
trusting help, kindness over hurry, giving back, handling
disappointment, gratitude + sharing joy. Day-Story tone throughout —
warm, unhurried, quiet magic; NOT bedtime (episode 6's arrival is
joyful, not sleepy).

## Fixed clips and play order

- **`series-intro`** — once, before a child's first episode. Teaches the
  chant. Ends on a statement, not a question.
- **`series-refrain`** — «Ծիվի՛կ, Ծիվի՛կ, փոքրիկ թևեր, մեծ ճանապարհ։» —
  six words, chantable, **byte-identical before every episode**. Render
  once, splice.
- **`series-closing`** — «Այսօրվա հեքիաթը այսքանն էր։ Շարունակությունը՝
  վաղը։» — after every episode. Render once, splice.

Play order: (first time only: intro) → refrain → episode → closing.

## Companion boundary

The HERO carries all attachment — Tsivik misses home, his friends wait
for *him*. Areg is only the narrator: no clip or line implies Areg's
feelings during the child's absence or promises availability
(«Ես կսպասեմ քեզ» and kin are banned). The closing clip talks about the
*story* continuing, not about Areg waiting.

## TTS / product rules these texts obey

1. **Full «-ը» article before vowel-initial next words** (2026-08-06
   owner listen-test ruling — the voice swallows the euphonic «ն»):
   «Ծիվիկը ուշադիր», «հեքիաթը այսքանն էր» are deliberate. Junctions
   where grammar forces «ն» (pre-«է/եմ/էր», ի-stem definites like
   «Ընկուզենին») are kept and listed in `_watchWords` — listen for the ն
   at sample time.
2. **Onomatopoeia = bare hyphenated pairs**, no stress marks: «շը-շը»
   (sea/field song), «վու-վու» (wind), «ծիվ-ծիվ» (chirp), «կռա-կռա»
   (crow). The sea's «շը-շը» recurs across episodes as the series'
   sound signature — always the same bare pair.
3. **Repeated lines are word-for-word identical** — the refrain and the
   closing clip are the only cross-episode repeats; both are single
   renders, spliced.
4. **No digits, no Latin script** anywhere in spoken text; episode
   numbers live in JSON metadata only.
5. **Length**: each episode targets 2.5–3 minutes spoken (~380–450
   words); the refrain and closing add a fixed ~8 seconds.

## What is deliberately NOT here

- No folklore characters, formulas, or datasets (postponed by owner
  decree; «Երկնքից երեք խնձոր ընկավ» was deliberately not used).
- No render, no `ContentSync` entries, no firmware flow — text first,
  owner review first.
- No bedtime variant — this is a Day-Story serial; a bedtime-safe
  edit would be a separate reviewed slice.
