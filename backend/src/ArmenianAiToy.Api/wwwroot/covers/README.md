# Story covers

Drop a picture here named exactly after the story id and the parent dashboard
shows it at the top of that story's card in the library:

    ulik.webp   khosogh-dzuk.webp   pochat-aghves.webp   anban-huri.webp
    sutasan.webp   sutlik-orskan.webp   three-piglets.webp
    princess-and-pea.webp   hedgehog-apple.webp   little-cloud.webp
    tsivik.webp   (the serial's mark)

`.webp`, `.png` and `.jpg` all work — the page tries them in that order, so
whatever your image tool hands you can go straight in. WebP is about a third
the size if you have the choice.

**4:5 portrait, at least 800 × 1000.** The card reserves that shape before the
image loads, so the page does not jump as covers arrive.

**A missing cover is not a fault.** The card simply reads the way it did before
covers existed, so the set can arrive one story at a time.

Nothing else needs doing: no endpoint, no config entry, no `Version` bump.
These are not content the toy downloads — it has no screen. Only the parent
sees them.

The prompts they were generated from are in `docs/story-cover-prompts.md`.
