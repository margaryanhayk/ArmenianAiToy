# Welcome-flow voice clips re-rendered in the cast narrator's voice (2026-09-08)

Owner: "Now rerender voice clips (greetings, ask) with cast narrator." All 42 clips in
`backend/content/voice-clips/voice-clips.json` (38 greetings, `ask-sgrc`, `ask-any`,
`say-again`, `just-story`) re-rendered: `areg-storyteller` on `eleven_v3`, stability .55 /
similarity .8 / style .2 — the same voice and settings as the narration and the 70 story
clips. Texts unchanged. Transcript-guarded; `greet-29` was re-taken after the first take
transcribed twice as «ի՞նչ կա թիորը» for «հեքիաթի օր է» (the re-take transcribed exactly),
and the guard's name allowance is now limited to lines of three words or fewer. Levelled
on a padded copy, 192 kbps; `apply_voice_clips.py --apply` re-hashed and bumped every
clip's `Version`.

| clip | sha256 | bytes | length |
|---|---|---|---|
| ask-any | `e20cf84487ec4c63…` | 58349 | 2.40s |
| ask-sgrc | `615b2ffd07e12a65…` | 149882 | 6.21s |
| greet-02 | `711378913584afea…` | 53960 | 2.22s |
| greet-03 | `42ad0d41c5290fb9…` | 66499 | 2.74s |
| greet-04 | `ff1ff46ebe06cc71…` | 57095 | 2.35s |
| greet-05 | `48663536952edac6…` | 44556 | 1.82s |
| greet-06 | `c0eff33b03f965ed…` | 57095 | 2.35s |
| greet-07 | `719f7edcffd791cf…` | 68380 | 2.82s |
| greet-08 | `d7f56587b42e08f9…` | 59603 | 2.45s |
| greet-09 | `c614e56865f446e2…` | 57095 | 2.35s |
| greet-10 | `f4ae7bcf1fbc5b14…` | 38287 | 1.56s |
| greet-11 | `cc517a8bcb00a732…` | 58349 | 2.40s |
| greet-12 | `4b3d5b9d9b294f8b…` | 64618 | 2.66s |
| greet-13 | `758cc44fa2d7f794…` | 53960 | 2.22s |
| greet-14 | `740091265845f58e…` | 69634 | 2.87s |
| greet-15 | `722906fbed62b078…` | 65245 | 2.69s |
| greet-16 | `c8e8c62c7f880375…` | 67126 | 2.76s |
| greet-17 | `d1163d9babe72b41…` | 53960 | 2.22s |
| greet-18 | `7a3e3b89a00f8341…` | 57722 | 2.37s |
| greet-19 | `56e45a84d6cd11ba…` | 67126 | 2.76s |
| greet-20 | `abbf4bb05881a0fe…` | 73395 | 3.03s |
| greet-21 | `e392ccc4179b5d70…` | 64618 | 2.66s |
| greet-22 | `6d2705e2b37a20af…` | 50199 | 2.06s |
| greet-23 | `626f0effd6057da7…` | 53960 | 2.22s |
| greet-24 | `2efbdf7b9b764b47…` | 50199 | 2.06s |
| greet-25 | `b7f728b94d447282…` | 55841 | 2.29s |
| greet-26 | `c00868ffd3437844…` | 50199 | 2.06s |
| greet-27 | `16db713e7b972c24…` | 63364 | 2.61s |
| greet-28 | `b9d94e7ff2486dc9…` | 63364 | 2.61s |
| greet-29 | `44b14e1ef3599ccc…` | 53960 | 2.22s |
| greet-30 | `709cf28203782dd8…` | 63364 | 2.61s |
| greet-31 | `e5dd6cf011395573…` | 57095 | 2.35s |
| greet-32 | `b242687a3bf155df…` | 76530 | 3.16s |
| greet-33 | `25e1d3152aeddc10…` | 77784 | 3.21s |
| greet-34 | `4061637c82bc7f37…` | 59603 | 2.45s |
| greet-35 | `ddc744d06687d270…` | 63364 | 2.61s |
| greet-36 | `1cf16fb1a1a29853…` | 69634 | 2.87s |
| greet-37 | `a6197d3359466f62…` | 57095 | 2.35s |
| greet-38 | `c779eff8837acb2d…` | 64618 | 2.66s |
| greet-39 | `fd9bab1130897d1f…` | 50199 | 2.06s |
| just-story | `c75ebabbb0481725…` | 50199 | 2.06s |
| say-again | `7470b087db72259c…` | 77784 | 3.21s |

Gates: `dotnet test` 2779/2779. Not done: the listen test on the toy; the owner has the 42-clip preview.
