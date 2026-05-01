# Evaluation — Story Plan Generator review (30 plans, seed=123, 2026-05-01)

**Plans file:** [`story-plan-generator-review-20260501.plans.json`](./story-plan-generator-review-20260501.plans.json)

> **Reviewer caveat (load-bearing).** Claude Code (agent) drafted
> this review. I am not a native Eastern Armenian speaker; the
> *Armenian naturalness* dimension below is marked **"needs
> Hayk native review"** wherever it could not be assessed
> structurally. I still flag obviously mechanical or weird
> phrases.

---

## A. Metadata

| Field | Value |
|---|---|
| Tool | `tools/StoryModelBakeoff/generate-story-plan.js` |
| Tool commit at run time | 6c5021e (origin/main HEAD) |
| Seed | 123 |
| Count | 30 |
| Captured (UTC) | 2026-05-01 |
| Source | tool-only; no model call, no network, no live API |
| Seed bank | `tools/StoryModelBakeoff/story-seed-bank.v1.json` (v1, 11 palette keys) |
| Validator | `node tools/StoryModelBakeoff/validate-seed-bank.js` → **PASS** |
| Reviewer | Claude Code (agent) — Armenian naturalness needs Hayk native review |

## B. Review rubric

Per-plan labels — **strong / acceptable / weak / reject**.

A plan is **strong** when:
- hero + friend feel like a natural Armenian fairy-tale pairing (animals from `palettes.animals`, both child-safe, no scale mismatch that breaks the scene);
- place feels concrete and Armenian-friendly;
- magicalObject feels concrete, child-safe, and pairs with the place / problem;
- smallProblem is low-stakes and story-useful (no injury, no death, no abandonment);
- sensoryDetails are concrete and complementary;
- choiceA and choiceB are concrete grounded actions that meaningfully differ;
- no obvious mechanical / template-shaped phrasing;
- no scary / too-adult / too-abstract content.

**Acceptable** = mostly works, with one minor stretch (e.g. a slight scale mismatch, a slightly mechanical template line, or a borderline animal). **Weak** = noticeable mechanical phrasing or off-tone combination, but still rescuable. **Reject** = scary, unsafe, broken Armenian, or fundamentally non-fairy-tale. No plan in this batch is weak or reject.

## C. Summary table (30 plans)

| # | hero | friendOrGuide | place | magicalObject | smallProblem (short) | choice quality | overall | notes |
|---|---|---|---|---|---|---|---|---|
| 1 | մրջյուն | գառնուկ | անտառի արահետ | աստղիկով կոճակ | քամին դադարել է | acceptable | acceptable | ant-scale vs button is fine; "տանել կոճակը ընկերոջ մոտ" is concrete. |
| 2 | ճպուռ | արծիվ | ծիրանենու տակ | խոսող հայելի | ընկերը մոլորվել է | acceptable | acceptable | Eagle is high-register but not broken; "վերցնել խոսող հայելի" reads OK. |
| 3 | հավիկ | լորիկ | լեռան լանջին թառած ամպ | քնած բանալի | երգող խեցին լռում է | strong | strong | Poetic place + sleeping-key-to-light is atmospheric. |
| 4 | արագիլ | արջուկ | լեռան լանջին թառած ամպ | խոսող հայելի | առվակը սառել է ու լռել | strong | strong | Stork + mountainside-cloud reads as warm fairy-tale. |
| 5 | սարյակ | աքլոր | ծառի փչակ | ոսկե թել | քամին դադարել է | strong | strong | High-place template (`բարձրանալ դեպի ծառի փչակ`) fired correctly. |
| 6 | խլուրդ | հազարան բլբուլ | փոքրիկ ջրաղաց | փայտե սրինգ | ոզնին չի գտնում իր պահած տերևը | strong | strong | Sound-template (`լսել՝ արդյոք … ձայն ունի`) fired correctly. Hazaran bulbul is very Armenian. |
| 7 | իշուկ | սագ | փոքրիկ ջրաղաց | երազների կլորիկ | ձյունը չի սկսում հալվել | strong | strong | Springtime feel; donkey + goose pairing is grounded. |
| 8 | լորիկ | բու | քարայրի մուտք | լուսավոր քար | ընկերը մոլորվել է | strong | strong | Glowing-stone-in-palm + cave entrance is the best atmospheric pairing in the batch. |
| 9 | ճպուռ | բու | քարավանատուն | կավե փոքրիկ կուժ | արևը թաքնվել է մեծ ամպի հետևում | strong | strong | Caravanserai + clay jug is uniquely Armenian. |
| 10 | թութակ | ճնճղուկ | ծիածանի կամար | քնքուշ բարձիկ | արևը թաքնվել է մեծ ամպի հետևում | acceptable | acceptable | "Bring pillow close to light" is mildly mechanical but interpretable. |
| 11 | շնիկ | արջ | հին քարե աղբյուր | դաշտային ծաղկեպսակ | փոքրիկ թռչունը չի գտնում իր բույնը | strong | strong | Old stone spring + flower wreath; warm. |
| 12 | ծիտիկ | թիթեռ | լուսնի արահետ | անմահական խնձոր | քայլող քարը կորցրել է իր ուղին | strong | strong | "Անմահական խնձոր" (immortality apple) is classical Sasna-Tsrer flavor. |
| 13 | մրջյուն | ոզնի | մամռոտ քար | մոռացված ժապավեն | առվակը սառել է ու լռել | strong | strong | Small-creature pairing + frozen brook reads as gentle. |
| 14 | իշուկ | այծիկ | քամու երգող սարը | քնքուշ բարձիկ | տերևները մոռացել են իրենց ծառի տեղը | acceptable | acceptable | "Pillow in palm" with donkey-scale hero is mildly off but not broken. |
| 15 | կաքավ | սագ | մշուշապատ առավոտ | արծաթե մարգարիտ | տերևները մոռացել են իրենց ծառի տեղը | acceptable | acceptable | "Walking toward a misty morning" is more atmospheric than literal place; OK. |
| 16 | բու | իմաստուն ձուկ | անտառի եզր | անկշռելի բմբուլ | ծաղիկը չի բացվում | acceptable | acceptable | Owl + fish pairing is unusual but the "wise fish" framing rescues it. |
| 17 | ուղտիկ | շնիկ | անտառային թաքստոց | հնչող փետուր | ծիածանը կորցրել է մեկ գույնը | acceptable | acceptable | Camel in a forest hideout is a slight geographic mismatch; otherwise warm. |
| 18 | գայլուկ | ծիտիկ | հին դարբնոց | ոսկե տերև | լապտերը մարել է | strong | strong | Old smithy + extinguished lantern + golden leaf to light = self-coherent. |
| 19 | ծիտիկ | ճնճղուկ | հին դարբնոց | դաշտային ծաղկեպսակ | մեղվաբույնի դուռը կպել է | acceptable | acceptable | Two small birds as hero+friend (very similar species). "Bring wreath to light" is the most mechanical choice in the batch. |
| 20 | մողես | գառնուկ | գաղտնի պարտեզ | նախշուն թաշկինակ | ծաղիկը չի բացվում | acceptable | acceptable | Lizard as hero is borderline for ages 4–7 (cold-feel) but not unsafe; secret garden is warm. |
| 21 | ծղրիդ | արջ | ցորենի ոսկե արտ | ցողի կաթիլներով տերև | մեղուն կորցրել է ծաղկի ճանապարհը | strong | strong | Cricket + bear is an Armenian "unlikely friends" classic. Wheat field is evocative. |
| 22 | սարյակ | բու | տատիկի բակ | արագավազ տրեխներ | աղբյուրը դադարել է խոսել | acceptable | acceptable | Grandma's yard is very warm; but "պահել տրեխները ափի մեջ" is scale-odd (shoes don't fit in a palm). Choice-template polish opportunity. |
| 23 | լորիկ | այծիկ | կապույտ լճակ | կարկաչուն կաթիլ | ոզնին չի գտնում իր պահած տերևը | strong | strong | "Կարկաչուն կաթիլ" (gurgling drop) + blue pond is musically grounded. |
| 24 | արծիվ | արջ | հին քարե աղբյուր | խոսող կաղին | արագիլը չի գտնում հանգստանալու տեղը | acceptable | acceptable | Eagle + bear is a heavyweight pairing for ages 4–7; not broken, just imposing. |
| 25 | ծիծեռնակ | ոզնի | ուռենու տակ | ոսկե թել | կամուրջը քնած է | strong | strong | Swallow under willow + sleeping bridge — gentle and visual. |
| 26 | գառնուկ | փասիան | ձյունոտ բլուր | գունավոր ապակու կտոր | քարը փակել է ջրի ճամփան | acceptable | acceptable | "Piece of colored glass" carries a faint sharp-glass concern at age 4–7; treat as sea-glass-shaped if used in story prose. Otherwise warm. |
| 27 | փասիան | սարյակ | լուսնի արահետ | կախարդական սփռոց | զանգակը կորցրել է ձայնը | strong | strong | "Կախարդական սփռոց" (magic tablecloth) is classical Armenian fairy-tale staple. |
| 28 | շուն | բադիկ | անտառի արահետ | քնած բանալի | ոզնին չի գտնում իր պահած տերևը | strong | strong | Dog + duckling on a forest path is a clean opener. |
| 29 | կատու | թութակ | ձյունոտ բլուր | անմահական խնձոր | ճանապարհը անհետացել է | strong | strong | Cat + parrot, snowy hill, immortality apple — coherent fantasy register. |
| 30 | գառնուկ | ճնճղուկ | ծիրանենու տակ | լուսավոր քար | գորտուկը մոռացել է ցատկելու երգը | strong | strong | "Frog forgot its jumping song" is the most charming small-problem in the batch. |

**Tally:** strong=18, acceptable=12, weak=0, reject=0.

## D. Findings

### Top 5 strongest plans

1. **#8** (լորիկ + բու / քարայրի մուտք / լուսավոր քար / "ընկերը մոլորվել է").
   Why: glowing-stone-in-palm + cave entrance creates a tight atmospheric scene. Both choices are concrete and complementary.
2. **#9** (ճպուռ + բու / քարավանատուն / կավե փոքրիկ կուժ / "արևը թաքնվել է").
   Why: caravanserai + clay jug is uniquely Armenian-medieval; uncommon enough to feel surprising, common enough to read as native.
3. **#12** (ծիտիկ + թիթեռ / լուսնի արահետ / անմահական խնձոր / "քայլող քարը").
   Why: "moon path" + "immortality apple" land directly in the Sasna Tsrer / classical Armenian fairy-tale register.
4. **#21** (ծղրիդ + արջ / ցորենի ոսկե արտ / ցողի կաթիլներով տերև / "մեղուն կորցրել է").
   Why: cricket + bear "unlikely friends" pairing in a golden wheat field reads warm and concrete; the dewdrops-leaf-to-friend choice is grounded.
5. **#30** (գառնուկ + ճնճղուկ / ծիրանենու տակ / լուսավոր քար / "գորտուկը մոռացել է ցատկելու երգը").
   Why: the "frog forgot its jumping song" small-problem is the most charming in the batch and the Aragats-leaning hero pairing fits the apricot-tree opening cleanly.

### Weakest plans

There are no weak / reject plans in this batch. The 12 "acceptable" plans share a common pattern (one of the following stretches):

- A choice template that doesn't quite fit the object's scale (#14, #22) — see "Choice template issues" below.
- A hero+friend pairing that's borderline (#16 owl+fish, #19 two small birds, #20 lizard, #24 eagle+bear).
- A magicalObject that's slightly safety-adjacent for age 4–7 (#26 colored-glass piece — a one-line edit at story-render time would say "sea-glass" or "smoothed glass" to neutralise).

### Repeated weak patterns

The two patterns that most consistently produce "acceptable" rather than "strong":

1. **`պահել X-ը ափի մեջ` (keep X in palm)** template is scale-blind. It reads great for small handheld items (#8 glowing stone, #15 silver pearl, #21 dewdrop leaf, #25 golden thread) but mildly off for objects that are clearly larger than a palm: #14 pillow, #22 fast-running shoes, #26 glass piece. Polish opportunity below.
2. **`մոտեցնել X-ը լույսին` (bring X close to the light)** template is "inspection-shaped"; it lands well on objects with internal mystery (#3 sleeping key, #5 golden thread, #18 golden leaf, #29 immortality apple). It feels mechanical when the object has no inspection-natural reading: #10 pillow, #19 wreath.

The frequency of these is bounded — each fired roughly 1/4 of the time the object-grounded family was selected — so they don't dominate output. But they are the prevailing reason a plan landed at "acceptable" instead of "strong".

### Seed bank entries that seem problematic (mild)

- `քնքուշ բարձիկ` (gentle pillow): too large for the "in palm" frame; consistently lands at "acceptable" when used as the magicalObject (#10, #14).
- `արագավազ տրեխներ` (fast-running shoes): same — pluralisable, scale-large, doesn't fit "in palm".
- `գունավոր ապակու կտոր` (piece of colored glass): the literal reading "shard of glass" is age-marginal. Could be reworded to `ծովապակու կտոր` (sea-glass piece) or `ապակու հղկված կտոր` (smoothed-glass piece) at the seed-bank level.
- Animal-list edge cases (`ուղտիկ`, `մողես`, `փասիան`) are all individually fine; they show up at "acceptable" because their scene context didn't quite reinforce them. Not a seed-bank fix.

These are polish nudges, not blockers.

### Choice template issues

The two patterns above are the only systemic ones. Two small generator-side polish ideas, neither required to proceed:

1. **Tag magical objects with size class.** Add a `palmSized` whitelist (e.g. by keyword: `տերև`, `քար`, `մարգարիտ`, `բանալի`, `փետուր`, `կաթիլ`, `թել`, `կոճակ`, `կաղին`, `սրվակ`, `կուժ`, `բմբուլ`, `ապակու կտոր`, `ժապավեն`, `թաշկինակ`). Restrict the `պահել X-ը ափի մեջ` template to those.
2. **Tag inspection-natural objects.** Restrict `մոտեցնել X-ը լույսին` to objects with an "inspection" feel — overlap with `isShiny` plus `բանալի`, `տուփ`, `կուժ`, `կլորիկ`, `ապակու կտոր`, `հայելի`, `ապակի`. Skip for soft / cloth / plant items.

Both are 5–10-line additions to `objectActions()` in `generate-story-plan.js`. Out of scope for this slice (review only); proposed for a follow-up.

### Armenian naturalness

I am not native. **Needs Hayk native review.** I did not spot obviously mechanical or non-Armenian phrases in the 30 plans, but case-marking on multi-word noun phrases (e.g. `«քնքուշ բարձիկը» / «ոսկե թելը»`) deserves a native-ear pass. The generator's conservative inflection helpers (`-ը` / `-ն` definite suffix; `-ի` genitive only on consonant-final phrases) avoid the most common breakage but cannot guarantee idiomatic feel.

## E. Recommendation

**Proceed to plan-to-story experiment.**

Rationale:

- **Distribution is healthy.** 18/30 strong, 12/30 acceptable, 0 weak, 0 reject. No plan would embarrass Areg if it became the spine of an actual story turn.
- **The two recurring polish nudges are downstream-fixable.** Either at the generator (size-class tag on magicalObjects) or, more cheaply, at the writer's prompt layer when the plan is rendered into Armenian prose ("if the magicalObject is too large to fit in a palm, paraphrase the choice").
- **The seed bank is providing usable raw material.** The places, magicalObjects, and smallProblems sections in particular are generating grounded, concrete combinations a model can write naturally over.
- **The native-review caveat is a layer above this signal.** Hayk's pass on the 30 plans here is the next step regardless of which downstream slice we run first.

The honest next step is **Phase 2.5 — render a few of these plans into actual Armenian story prose using a model** (Claude API via F1.2, or OpenAI API via the production stack with Areg system prompt). That will tell us whether Plan-Gate-grade plans actually produce better Areg output than free-form generation — which is the whole F1 hypothesis.

**Do not** revise the seed bank or generator templates as a precondition for Phase 2.5; the polish ideas above are 1–2 hour follow-ups that can land while plan-to-story experiments are running.
