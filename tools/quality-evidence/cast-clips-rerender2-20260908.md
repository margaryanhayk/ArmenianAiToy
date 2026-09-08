# Intro / offer / reoffer clips re-rendered in the cast narrator's voice (2026-09-08)

Owner: "Now rerender intro, offer and reoffer clips too." The 30 clips (three kinds × all
ten stories, «Ուլիկը» included — its own were still the 2026-08-16 render) now match the
narration: `areg-storyteller` on `eleven_v3`, stability .55 / similarity .8 / style .2.
Texts unchanged: intro = «Հեքիաթ՝ «Title»։ Հեղինակ՝ Author։» (title-only where no author is
verified), offer / reoffer from the templates in `backend/content/voice-clips/voice-clips.json`.
Transcript-guarded, levelled on a padded copy, 192 kbps; `apply_story_clips.py --apply`
re-hashed every clip; no `Version` moved. With PR #33 every one of the 70 per-story clips
is now in the cast narrator's voice.

| clip | sha256 | bytes | length |
|---|---|---|---|
| anban-huri/intro | `6998b33f179097f2…` | 98473 | 4.07s |
| anban-huri/offer | `a6253f6882545c99…` | 76530 | 3.16s |
| anban-huri/reoffer | `8b298edf181cc19a…` | 76530 | 3.16s |
| princess-and-pea/intro | `3365bbcd672dae11…` | 140478 | 5.82s |
| princess-and-pea/offer | `7001df04e2708d1d…` | 98473 | 4.07s |
| princess-and-pea/reoffer | `b3a79b4249ec4af8…` | 121043 | 5.01s |
| little-cloud/intro | `a949f4f41462498e…` | 50199 | 2.06s |
| little-cloud/offer | `2be1bfc0eb8fb0f8…` | 69634 | 2.87s |
| little-cloud/reoffer | `009d8957ece18740…` | 93457 | 3.86s |
| sutlik-orskan/intro | `5bff0b2c0c41f290…` | 88442 | 3.65s |
| sutlik-orskan/offer | `8a32d999b86373a5…` | 69634 | 2.87s |
| sutlik-orskan/reoffer | `abef50e59caf8365…` | 93457 | 3.86s |
| sutasan/intro | `b92b06b5db735da4…` | 102235 | 4.23s |
| sutasan/offer | `84800fdc09327fc3…` | 68380 | 2.82s |
| sutasan/reoffer | `3e6cc54597161d8c…` | 87188 | 3.60s |
| pochat-aghves/intro | `99b15d81f9d83414…` | 95338 | 3.94s |
| pochat-aghves/offer | `363c258921316234…` | 69634 | 2.87s |
| pochat-aghves/reoffer | `82537cf473c433b8…` | 85934 | 3.55s |
| khosogh-dzuk/intro | `60d50d04145aafdf…` | 95338 | 3.94s |
| khosogh-dzuk/offer | `2d5ddb669fe7f2d0…` | 66499 | 2.74s |
| khosogh-dzuk/reoffer | `ddd0272eb214739c…` | 96592 | 3.99s |
| three-piglets/intro | `2da8e7b9303fbdb6…` | 53960 | 2.22s |
| three-piglets/offer | `00974f6b8afd258f…` | 68380 | 2.82s |
| three-piglets/reoffer | `84861772aad3b3a6…` | 88442 | 3.65s |
| hedgehog-apple/intro | `86f745f8ee48edc8…` | 63364 | 2.61s |
| hedgehog-apple/offer | `82a938fc23f3bda1…` | 73395 | 3.03s |
| hedgehog-apple/reoffer | `711dd206678cacb7…` | 88442 | 3.65s |
| ulik/intro | `2ca0b2eff6055561…` | 45810 | 1.88s |
| ulik/offer | `a6cdc0e318d3676b…` | 64618 | 2.66s |
| ulik/reoffer | `d7f1110b15c7d3d3…` | 75276 | 3.10s |

Gates: `dotnet test` (see commit). Not done: the listen test on the toy; the owner has the 30-clip preview.
