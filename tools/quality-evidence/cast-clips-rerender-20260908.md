# Summary + question clips re-rendered in the cast narrator's voice (2026-09-08)

Owner: "Now rerender question and summary clips with cast narrator." The 36 clips
(`summary`, `question`, `question1`, `question2` × the nine stories shipped in
PR #32) now match «Ուլիկը»'s: `areg-storyteller` on `eleven_v3`, stability .55 /
similarity .8 / style .2 — the same voice and settings as the cast narration they follow.
Text unchanged: summary = `lesson ?? reflectionText`, questions = `reflectionQuestions[0..2]`.
Each take passed the transcript guard; levelled at −16.4 LUFS on a padded copy (a 2 s clip
cannot be measured bare), 192 kbps. `apply_story_clips.py --apply` re-hashed every clip;
no story `Version` moved, so toys fetch ~4 MB of clips, not the narration again.

| clip | sha256 | bytes | length |
|---|---|---|---|
| anban-huri/summary | `c3a354a67c4a5439…` | 173706 | 7.20s |
| anban-huri/question | `d682b3bc8eee85e8…` | 44556 | 1.82s |
| anban-huri/question1 | `3608bd045d00934f…` | 50199 | 2.06s |
| anban-huri/question2 | `13e5a8483d3e8735…` | 48945 | 2.01s |
| princess-and-pea/summary | `f400553fefaff51b…` | 98473 | 4.07s |
| princess-and-pea/question | `695ff0df0fd85892…` | 55214 | 2.27s |
| princess-and-pea/question1 | `be9f9ddcce7b8aae…` | 82800 | 3.42s |
| princess-and-pea/question2 | `6e1ef4202822e909…` | 84053 | 3.47s |
| little-cloud/summary | `24dcc8691d453f6d…` | 79038 | 3.26s |
| little-cloud/question | `b594fac1e644bed0…` | 47691 | 1.95s |
| little-cloud/question1 | `d73ad8bd8499cbca…` | 57095 | 2.35s |
| little-cloud/question2 | `9abd8cfcae4550f9…` | 50199 | 2.06s |
| sutlik-orskan/summary | `c74dae0641f02c80…` | 113520 | 4.70s |
| sutlik-orskan/question | `1136aac70834989e…` | 69634 | 2.87s |
| sutlik-orskan/question1 | `898803893b7106b6…` | 77784 | 3.21s |
| sutlik-orskan/question2 | `43df6c15091898ac…` | 84053 | 3.47s |
| sutasan/summary | `c524741936d2c77e…` | 88442 | 3.65s |
| sutasan/question | `27a06f284ea972a2…` | 58349 | 2.40s |
| sutasan/question1 | `c819e8df06ea1db8…` | 68380 | 2.82s |
| sutasan/question2 | `f8d3ed2925663446…` | 68380 | 2.82s |
| pochat-aghves/summary | `6c556e1f98cf00b6…` | 134835 | 5.59s |
| pochat-aghves/question | `fe54f443580c1136…` | 47691 | 1.95s |
| pochat-aghves/question1 | `13048213b0a49731…` | 59603 | 2.45s |
| pochat-aghves/question2 | `eff3008f31e55ab4…` | 63364 | 2.61s |
| khosogh-dzuk/summary | `f23223d8e1c51e23…` | 116027 | 4.80s |
| khosogh-dzuk/question | `3d1a13abdd4de9d9…` | 58349 | 2.40s |
| khosogh-dzuk/question1 | `2e5de6e48218452c…` | 59603 | 2.45s |
| khosogh-dzuk/question2 | `90dee53e669099da…` | 59603 | 2.45s |
| three-piglets/summary | `34f626127a1e1931…` | 140478 | 5.82s |
| three-piglets/question | `8d9951044ef0b568…` | 58349 | 2.40s |
| three-piglets/question1 | `6cd19bac5b31483c…` | 68380 | 2.82s |
| three-piglets/question2 | `242087ae937b48df…` | 55841 | 2.29s |
| hedgehog-apple/summary | `03157392a2a5c621…` | 131074 | 5.43s |
| hedgehog-apple/question | `37d3cc71605ccda1…` | 59603 | 2.45s |
| hedgehog-apple/question1 | `9d65a42485546034…` | 57095 | 2.35s |
| hedgehog-apple/question2 | `c444083b0426a2c5…` | 66499 | 2.74s |

Gates: `dotnet test` 2779/2779. Unchanged: intro / offer / reoffer clips (the 2026-08-16
render). Not done: the listen test on the toy; the owner has the 36-clip preview file.

## Owner verdict (2026-09-08)

"Good" on the 36-clip preview. Approval is pinned to the sha256 table above (the installed bytes are the preview's bytes).
