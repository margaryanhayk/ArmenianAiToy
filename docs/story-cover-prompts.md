# Story cover prompts — paste these into an image AI

**Written 2026-08-10**, owner decision: *"You can give me prompts for images, I
can generate with other AIs."* So there is no illustrator to brief — these are
the briefs, written to be pasted directly.

Ten covers, one per story in `Stories/Content/`, plus one mark for the Ծիվիկ
series. Every subject below is taken from the **actual Armenian text**, not
from the English title, because several of them differ (the third piglet builds
a **stone** house, not brick; the princess is tested with a **chickpea**).

---

## Before you generate anything

**Do one story first, look at it, then batch.** Same rule the audio tool has:
one bad style setting costs one image if you check, or ten if you don't. Start
with **Ուլիկը** — it has a figure, a building and a night sky, so if the style
works there it works everywhere.

**Always end every prompt with:** `no text, no letters, no writing, no words`.
Image models cannot render Armenian script and will produce convincing-looking
gibberish that a parent will read as broken.

**Output:** 4:5 portrait, at least 800 × 1000. Save each one as
`backend/src/ArmenianAiToy.Api/wwwroot/covers/<storyId>.webp` — the file name
must be exactly the story id (the left-hand column below), lowercase, `.webp`.
The dashboard finds them by that name; nothing else needs configuring.

**Keep them calm.** These sit in a parent's app beside their child's evening.
No bared teeth, no fear, no dark menace — the wolf in Ուլիկը is a shape at a
door, not a threat. The stories carry their own tension in the telling.

---

## The style block

Paste this **before** each subject line, unchanged, every time. It is what
makes the ten look like one library instead of ten unrelated pictures.

```
Flat Armenian medieval manuscript illumination, in the style of a Toros Roslin
miniature. Heavy dark brown-black outlines, completely flat saturated colour
with no shading and no gradients, no perspective and no depth — everything on
one plane. Palette strictly: deep lapis blue #2A4A8F, cinnabar red #C2432B,
gold leaf #C6952F, deep pomegranate #7E2547, foliage green #46664B, warm
vellum #EFE4CE. The whole scene sits inside a tall rounded arch frame with a
thin dark border, like a page in an illuminated gospel. Simple, calm,
child-friendly, decorative rather than realistic. Vertical 4:5 composition.
```

Then add one blank line and the subject.

---

## The ten subjects

### `ulik` — «Ուլիկը»
```
A small black kid goat standing alone inside a simple wooden house at night,
facing a shut plank door. Through the gap under the door, a large grey shadow
waits outside. A gold crescent moon in the deep blue sky above the roof. The
kid is calm and alert, not frightened.
```
*Why this moment:* the whole story is the kid deciding not to open the door.

### `khosogh-dzuk` — «Խոսող ձուկը»
```
A poor fisherman in a plain brown tunic kneeling at the edge of a blue river,
releasing a large gold fish from his hands back into the water. The fish is
mid-air above the surface, its mouth open as if speaking. Small white ripple
rings on the water. Reeds along the bank.
```
*Why:* the act of mercy the whole story turns on — not the Monster.

### `pochat-aghves` — «Պոչատ աղվեսը»
```
A red fox sitting upright in profile, with a visibly missing tail — a short
clean stump where the tail should be. He holds a small clay milk jug in his
front paws and looks up hopefully. Behind him a low stone wall and a simple
village doorway.
```
*Why:* the tailless fox begging his way around the village is the image
everyone remembers. Do not draw the cutting.

### `anban-huri` — «Անբան Հուռին»
```
A young woman in a long red dress standing on a riverbank, throwing white
bundles of cotton into the blue water. Three round green frogs sit on the bank
looking up at her, mouths open as if croaking. A wooden spindle with white
thread wound on it lies unused on the grass beside her.
```
*Why:* selling the cotton to the frogs is the joke the story is built on.

### `sutasan` — «Սուտասանը»
```
A crowned king on a simple gold throne, leaning forward with one hand raised,
listening to a barefoot peasant in a plain tunic who holds up a large empty
wooden measuring basket. The king looks amused and caught out. Flat decorative
palace arches behind them.
```
*Why:* the peasant with his `կոտ` wins the story with the emptiest object in it.

### `sutlik-orskan` — «Սուտլիկ որսկանը»
```
Six comic hunters in a row, each smaller than the last, marching in profile
with long old-fashioned rifles over their shoulders, all wearing tall
sheepskin hats. Ahead of them three round lakes, two drawn dry and cracked and
one full of blue water with a single duck on it. Bright, funny, exaggerated.
```
*Why:* it is a tall tale, and the cover should look like one.

### `three-piglets` — «Երեք խոզուկները»
```
Three small houses in a row on a green hill: a golden straw hut, a brown
wooden house, and a solid grey STONE house with a chimney. Three little pink
pigs stand together in front of the stone house. A grey wolf shape walks away
into dark trees in the far corner, small and already leaving.
```
*Note:* stone, not brick — the Armenian text says `քարե տուն`.

### `princess-and-pea` — «Արքայադուստրը և սիսեռահատիկը»
```
A very tall stack of about twelve colourful mattresses and quilts, stripes of
lapis blue, cinnabar red and gold, with a small girl in a white nightdress
lying awake on the very top. At the very bottom of the stack, one tiny green
chickpea, drawn large enough to see. A narrow window with rain outside.
```

### `hedgehog-apple` — «Ոզնիկն ու խնձորը»
```
A small brown hedgehog and a grey hare together rolling one very large red
apple along a forest path. Both are pushing with their front paws, leaning
into it. Simple round trees on either side. Warm and friendly.
```

### `little-cloud` — «Փոքրիկ ամպիկը»
```
One small round white cloud low in a lapis blue sky, letting a few straight
lines of rain fall onto a single tall flower with open red petals below it.
Green ground. Empty, calm, mostly sky. A gold sun in the corner.
```

---

## The series mark — Ծիվիկ

Not a story cover; the badge that groups the six episodes.

```
[style block]

A single small red bird with a gold beak standing on a pale winding road that
climbs over green hills into the distance. The bird is in profile, facing up
the road, one small bundle on its back. Wide empty sky.
```

Save as `covers/tsivik.webp`. Individual episodes reuse it with the part
number added by the dashboard — do not generate six near-identical birds.

---

## If the results come out wrong

| What you see | Add to the prompt |
|---|---|
| Shading, glow, 3D look | `absolutely flat colour, no shading, no highlights, no 3D` |
| Photo-realistic animals | `simple decorative shapes, not realistic` |
| Wrong colours creeping in | repeat the six hex codes at the end |
| Frame missing | `the whole image inside a tall rounded arch with a dark border` |
| Gibberish writing | `no text, no letters, no writing, no words` — and regenerate |
| Ten pictures that don't match | you changed the style block between them; paste it identically |

## What happens after you generate them

Put the files in `wwwroot/covers/`, tell me, and I will wire the library to
show them. **The dashboard falls back to today's text card for any story whose
cover is missing**, so you can add them one at a time and nothing breaks while
the set is incomplete. No backend change, no config, no `Version` bump — these
are not content the toy downloads. The toy has no screen; only the parent sees
them.
