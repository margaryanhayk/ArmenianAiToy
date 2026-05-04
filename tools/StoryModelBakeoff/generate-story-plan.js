#!/usr/bin/env node
//
// StoryModelBakeoff — Story Plan generator (Node.js, no dependencies).
//
// Tool-only experiment per STORY_DIRECTOR_ARCHITECTURE.md, Phase 2.
// Reads tools/StoryModelBakeoff/story-seed-bank.v1.json and prints
// pretty JSON story plans to stdout. Does NOT touch production
// runtime, ChatService, prompts, or any provider.
//
// Usage:
//   node tools/StoryModelBakeoff/generate-story-plan.js
//   node tools/StoryModelBakeoff/generate-story-plan.js --count 3
//   node tools/StoryModelBakeoff/generate-story-plan.js --count 5 --seed 123
//   node tools/StoryModelBakeoff/generate-story-plan.js \
//     --count 3 --seed 123 --age-profile age-5-balanced
//   node tools/StoryModelBakeoff/generate-story-plan.js --with-names
//   node tools/StoryModelBakeoff/generate-story-plan.js \
//     --count 3 --seed 123 --with-names
//
// Plan shape (now consumes the seed bank's story-control attributes
// in addition to the original content palettes; see
// `palettes.characterTraits`, `palettes.relationshipTypes`,
// `palettes.storyMoods`, `palettes.conflictTypes`,
// `palettes.characterGoals`, `palettes.resolutionStyles`,
// `palettes.choiceTypes`, `palettes.ageToneProfiles`):
//
//   {
//     "hero": "...",                       (palettes.animals, safe pool)
//     "heroTrait": "...",                  (palettes.characterTraits)
//     "friendOrGuide": "...",              (palettes.animals, safe pool, ≠ hero)
//     "relationship": "...",               (palettes.relationshipTypes)
//     "place": "...",                      (palettes.places)
//     "mood": "...",                       (palettes.storyMoods)
//     "magicalObject": "...",              (palettes.magicalObjects)
//     "smallProblem": "...",               (palettes.smallProblems)
//     "conflictType": "...",               (palettes.conflictTypes)
//     "goal": "...",                       (palettes.characterGoals)
//     "resolutionStyle": "...",            (palettes.resolutionStyles)
//     "sensoryDetails": ["...", "..."],    (palettes.sensoryDetails, 2 distinct)
//     "ageToneProfile": { ... },           (palettes.ageToneProfiles object,
//                                           pinned by --age-profile if set)
//     "choiceAType": "...",                (palettes.choiceTypes — derived
//     "choiceBType": "...",                 from the selected templates)
//     "choiceA": "...",                    (concrete grounded action)
//     "choiceB": "..."                     (concrete grounded action)
//   }
//
// Choices are still one place-grounded + one object-grounded per plan;
// each template carries its `choiceType` tag (drawn from the seed
// bank's `choiceTypes`) so the plan records WHICH choice family was
// drawn, not just the rendered phrase. Order between A and B is
// randomised per plan.

'use strict';

const fs = require('fs');
const path = require('path');

const SEED_BANK_PATH = path.resolve(__dirname, 'story-seed-bank.v1.json');
const NAMES_BANK_PATH = path.resolve(__dirname, 'story-character-names.v1.json');

// ---- args ----
const args = process.argv.slice(2);
function flag(name) {
  for (let i = 0; i < args.length - 1; i++) {
    if (args[i] === name) return args[i + 1];
  }
  return null;
}
const countArg = flag('--count');
const seedArg = flag('--seed');
const ageProfileArg = flag('--age-profile');
const withNames = args.includes('--with-names');

const count = countArg === null
  ? 1
  : (() => {
      const n = parseInt(countArg, 10);
      if (!Number.isFinite(n) || n < 1) {
        console.error('Invalid --count: ' + countArg);
        process.exit(1);
      }
      return n;
    })();

const seed = seedArg === null
  ? null
  : (() => {
      const n = parseInt(seedArg, 10);
      if (!Number.isFinite(n)) {
        console.error('Invalid --seed: ' + seedArg);
        process.exit(1);
      }
      return n >>> 0;
    })();

// ---- RNG ----
function makeRng(s) {
  if (s === null) return Math.random;
  let state = (s >>> 0) || 1;
  return function () {
    state = (Math.imul(state, 1664525) + 1013904223) >>> 0;
    return state / 0x100000000;
  };
}
const rng = makeRng(seed);

function pick(arr) {
  return arr[Math.floor(rng() * arr.length)];
}

function pickDistinct(arr, n) {
  if (arr.length < n) {
    throw new Error('not enough entries to pick ' + n + ' distinct from array of ' + arr.length);
  }
  const chosen = [];
  const usedIdx = new Set();
  while (chosen.length < n) {
    const i = Math.floor(rng() * arr.length);
    if (usedIdx.has(i)) continue;
    usedIdx.add(i);
    chosen.push(arr[i]);
  }
  return chosen;
}

// ---- load + light validation ----
if (!fs.existsSync(SEED_BANK_PATH)) {
  console.error('Seed bank not found: ' + SEED_BANK_PATH);
  process.exit(1);
}
let data;
try {
  data = JSON.parse(fs.readFileSync(SEED_BANK_PATH, 'utf8'));
} catch (e) {
  console.error('Seed bank parse error: ' + e.message);
  process.exit(1);
}
const palettes = data && data.palettes;

// Required string-array palettes for plan generation. forbidden-
// TonePatterns is intentionally NOT in this list — it is a
// guardrail for the writer prompt layer, not a generation source.
const REQUIRED_ARRAYS = [
  // content palettes
  'animals', 'places', 'magicalObjects', 'smallProblems',
  'sensoryDetails',
  // guardrails (we filter against, not draw from)
  'rareOrRequestedOnlyAnimals', 'hardAvoidCreatures',
  // story-control attributes
  'characterTraits', 'characterGoals', 'storyMoods',
  'relationshipTypes', 'conflictTypes', 'resolutionStyles',
  'choiceTypes',
];
for (const k of REQUIRED_ARRAYS) {
  const arr = palettes && palettes[k];
  if (!Array.isArray(arr) || arr.length === 0) {
    console.error('Seed bank missing or empty palettes.' + k);
    console.error('Run: node tools/StoryModelBakeoff/validate-seed-bank.js');
    process.exit(1);
  }
}
// ageToneProfiles is an object array — separate shape check.
if (!Array.isArray(palettes.ageToneProfiles) || palettes.ageToneProfiles.length === 0) {
  console.error('Seed bank missing or empty palettes.ageToneProfiles');
  console.error('Run: node tools/StoryModelBakeoff/validate-seed-bank.js');
  process.exit(1);
}

// ---- optional character name bank (loaded only when --with-names set) ----
let namesBank = null;
if (withNames) {
  if (!fs.existsSync(NAMES_BANK_PATH)) {
    console.error('Character name bank not found: ' + NAMES_BANK_PATH);
    console.error('Run: node tools/StoryModelBakeoff/validate-character-names.js');
    process.exit(1);
  }
  try {
    namesBank = JSON.parse(fs.readFileSync(NAMES_BANK_PATH, 'utf8'));
  } catch (e) {
    console.error('Character name bank parse error: ' + e.message);
    process.exit(1);
  }
  if (!namesBank
      || typeof namesBank.animalNames !== 'object'
      || Array.isArray(namesBank.animalNames)) {
    console.error('Character name bank missing or malformed animalNames');
    console.error('Run: node tools/StoryModelBakeoff/validate-character-names.js');
    process.exit(1);
  }
}

// ---- defensive animal pool ----
const block = new Set([
  ...palettes.rareOrRequestedOnlyAnimals,
  ...palettes.hardAvoidCreatures,
]);
const safeAnimals = palettes.animals.filter((x) => !block.has(x));
if (safeAnimals.length < 2) {
  console.error('Not enough safe animals to pick a hero + distinct friend.');
  process.exit(1);
}

// ---- --age-profile resolution ----
// Pinned per-run profile when the operator requests one; otherwise
// each plan draws independently from palettes.ageToneProfiles.
let agePinnedProfile = null;
if (ageProfileArg !== null) {
  agePinnedProfile = palettes.ageToneProfiles.find(
    (p) => p && p.label === ageProfileArg
  );
  if (!agePinnedProfile) {
    const labels = palettes.ageToneProfiles
      .map((p) => p && p.label)
      .filter(Boolean)
      .join(', ');
    console.error('Unknown --age-profile: ' + ageProfileArg);
    console.error('Available labels: ' + labels);
    process.exit(1);
  }
}

// ---- Armenian morphology helpers ----
const VOWELS = new Set(['ա', 'ե', 'է', 'ի', 'ո', 'օ', 'ը', 'ու']);

function endsInVowel(phrase) {
  const s = String(phrase).trim();
  if (s.length === 0) return false;
  const last2 = s.slice(-2);
  if (VOWELS.has(last2)) return true;
  return VOWELS.has(s.slice(-1));
}

function appendDefiniteSuffix(phrase) {
  const s = String(phrase).trim();
  return s + (endsInVowel(s) ? 'ն' : 'ը');
}

function appendGenitiveSafe(phrase) {
  if (endsInVowel(phrase)) return null;
  return String(phrase).trim() + 'ի';
}

// ---- shape classifiers ----
function isOpenable(obj) {
  return /(տուփ|սրվակ|կուժ|տոպրակ|սփռոց)/.test(obj);
}
function isShiny(obj) {
  return /(ոսկ|արծաթ|լուսավոր|փայլող|մարգարիտ|աստղ|լույս|շող)/.test(obj);
}
function isSoundCapable(obj) {
  return /(զանգակ|սանր|սրինգ|սուլիչ|խեցի|փետուր|կաթիլ|երգող|խոսող|հնչող|հայելի)/.test(obj);
}

function isWaterOrLow(place) {
  return /(լճակ|աղբյուր|առվակ|ափ|ձոր|ներքև|ճահ)/.test(place);
}
function isHighOrUp(place) {
  return /(բլուր|սար|ժայռ|կատար|լեռ|կաղնի|ծառ|ընկուզենի|աշտարակ|ճյուղ|բարձ)/.test(place);
}

// ---- choice templates (now type-tagged) ----
//
// Each template returns { phrase, choiceType }. The `choiceType`
// strings are exact members of `palettes.choiceTypes` so the
// generated plan's choiceAType / choiceBType slot into the seed-
// bank vocabulary without translation.
//
// Place templates → choiceType "գնալ դեպի վայր" (uniform — all
// place templates use `դեպի` + nominative).
// Object templates carry per-template choiceTypes that match the
// action family in the seed bank.

const CT_PLACE        = 'գնալ դեպի վայր';
const CT_USE          = 'օգտագործել կախարդական առարկան';
const CT_HELP         = 'օգնել կերպարին';
const CT_KEEP_NEAR    = 'պահել առարկան իր մոտ';
const CT_GENTLE_TRY   = 'փորձել մեղմ գործողություն';
const CT_OPEN         = 'բացել փոքրիկ առարկան';
const CT_FOLLOW_TRACE = 'հետևել հետքին';
const CT_LISTEN       = 'լսել ձայնը';

function objectActions(obj) {
  const def = appendDefiniteSuffix(obj);
  const out = [
    { phrase: 'վերցնել ' + obj,                       choiceType: CT_USE },
    { phrase: 'տանել ' + def + ' ընկերոջ մոտ',        choiceType: CT_HELP },
    { phrase: 'պահել ' + def + ' ափի մեջ',            choiceType: CT_KEEP_NEAR },
    { phrase: 'մոտեցնել ' + def + ' լույսին',         choiceType: CT_GENTLE_TRY },
  ];
  if (isOpenable(obj)) {
    out.push({ phrase: 'բացել ' + def, choiceType: CT_OPEN });
  }
  const gen = appendGenitiveSafe(obj);
  if (isShiny(obj) && gen) {
    out.push({ phrase: 'հետևել ' + gen + ' փայլին', choiceType: CT_FOLLOW_TRACE });
  }
  if (isSoundCapable(obj)) {
    out.push({
      phrase: 'լսել՝ արդյոք ' + def + ' ձայն ունի',
      choiceType: CT_LISTEN,
    });
  }
  return out;
}

// Sub-location / scene-exploration templates.
//
// The story always opens IN `plan.place`, so the earlier
// "գնալ դեպի <place>" / "քայլել դեպի <place>" templates were
// spatially vacuous on Turn 1 — "go to the place we're already
// in" is not a child choice. See
// `evaluations/story-brain-finalization-20260504.md` § 4 for
// the discovery context.
//
// The new templates point to sub-regions of `<place>` or to
// actions performed *within* the scene. Each template still
// contains `<place>` verbatim as a substring so the Plan Gate's
// choice-grounding check (`choice.includes(place)`) keeps
// passing without further changes.
//
// Armenian morphology note: the templates emit `<place>-ի` /
// `<place>-ը` etc. with a literal hyphen rather than trying to
// inflect each `<place>` correctly (which would require per-
// place declension data the seed bank does not carry). The
// writer prompt is responsible for polishing the morphology
// at render time — e.g. "խնձորենու այգի-ի" (research-tool
// emission) → "խնձորենու այգու" (proper genitive) in the
// rendered Armenian prose.
function placeActions(place) {
  const out = [
    { phrase: 'գնալ ' + place + '-ի հեռավոր եզրը',           choiceType: CT_PLACE },
    { phrase: 'քայլել ' + place + '-ի միջով',                 choiceType: CT_PLACE },
    { phrase: 'կանգնել ու լսել ' + place + '-ի ձայները',     choiceType: CT_PLACE },
    { phrase: 'փնտրել մի փոքրիկ նշան ' + place + '-ի մոտ',   choiceType: CT_PLACE },
  ];
  if (isWaterOrLow(place)) {
    out.push({ phrase: 'իջնել ' + place + '-ի խորքը', choiceType: CT_PLACE });
  }
  if (isHighOrUp(place)) {
    out.push({ phrase: 'բարձրանալ ' + place + '-ի գագաթը', choiceType: CT_PLACE });
  }
  return out;
}

// ---- character-name draw helpers (only used when --with-names) ----
//
// Source / fallback rules per
// `evaluations/character-name-wiring-plan-20260503.md` § 3.1:
//   primary  = animalNames[animal] (when non-empty)
//   fallback = sharedNames (only when primary is missing or empty)
//
// Collision rules per § 3.2:
//   redraw friend once from friend's primary pool;
//   if still equal, draw friend from sharedNames excluding heroName;
//   if exhausted, throw a clear error (no numeric suffixes).
function namePoolFor(animal) {
  const arr = namesBank.animalNames[animal];
  if (Array.isArray(arr)) {
    const filtered = arr.filter(
      (v) => typeof v === 'string' && v.trim().length > 0
    );
    if (filtered.length > 0) return filtered;
  }
  if (Array.isArray(namesBank.sharedNames)) {
    const shared = namesBank.sharedNames.filter(
      (v) => typeof v === 'string' && v.trim().length > 0
    );
    if (shared.length > 0) return shared;
  }
  return [];
}

function pickNamesPair(hero, friend) {
  const heroPool = namePoolFor(hero);
  if (heroPool.length === 0) {
    throw new Error(
      'No names available for hero "' + hero +
      '" (per-animal list empty and sharedNames empty)'
    );
  }
  const heroName = pick(heroPool);

  const friendPool = namePoolFor(friend);
  if (friendPool.length === 0) {
    throw new Error(
      'No names available for friend "' + friend +
      '" (per-animal list empty and sharedNames empty)'
    );
  }
  let friendName = pick(friendPool);
  if (friendName === heroName) {
    friendName = pick(friendPool); // redraw once
  }
  if (friendName === heroName) {
    const sharedMinusHero = (
      Array.isArray(namesBank.sharedNames) ? namesBank.sharedNames : []
    ).filter(
      (v) => typeof v === 'string' && v.trim().length > 0 && v !== heroName
    );
    if (sharedMinusHero.length === 0) {
      throw new Error(
        'name-collision: heroName "' + heroName +
        '" + friendOrGuideName pool exhausted'
      );
    }
    friendName = pick(sharedMinusHero);
  }
  return { heroName, friendName };
}

// ---- plan builder ----
function buildPlan() {
  const hero = pick(safeAnimals);
  let friend = pick(safeAnimals);
  while (friend === hero) friend = pick(safeAnimals);

  const heroTrait      = pick(palettes.characterTraits);
  const relationship   = pick(palettes.relationshipTypes);
  const place          = pick(palettes.places);
  const mood           = pick(palettes.storyMoods);
  const magicalObject  = pick(palettes.magicalObjects);
  const smallProblem   = pick(palettes.smallProblems);
  const conflictType   = pick(palettes.conflictTypes);
  const goal           = pick(palettes.characterGoals);
  const resolutionStyle = pick(palettes.resolutionStyles);
  const sensoryDetails = pickDistinct(palettes.sensoryDetails, 2);
  const ageToneProfile = agePinnedProfile || pick(palettes.ageToneProfiles);

  const objCandidates = objectActions(magicalObject);
  const plcCandidates = placeActions(place);
  const objChoice = pick(objCandidates);
  const plcChoice = pick(plcCandidates);
  const aIsPlace = rng() < 0.5;
  const choiceAObj = aIsPlace ? plcChoice : objChoice;
  const choiceBObj = aIsPlace ? objChoice : plcChoice;

  // Names are drawn LAST per character-name-wiring-plan-20260503.md
  // § 3.3 so the first plan in a run has identical non-name fields
  // whether or not --with-names is set; the per-plan name draws
  // shift downstream RNG state across plans 2…N (documented).
  let heroName = null;
  let friendName = null;
  if (withNames) {
    const pair = pickNamesPair(hero, friend);
    heroName = pair.heroName;
    friendName = pair.friendName;
  }

  // Field order is contractual — this is the shape consumers / the
  // experiment markdown reference. Do not reorder without a slice.
  // Optional name fields, when present, are positioned right after
  // the animal field they name (per design § 2).
  return {
    hero,
    ...(withNames ? { heroName } : {}),
    heroTrait,
    friendOrGuide: friend,
    ...(withNames ? { friendOrGuideName: friendName } : {}),
    relationship,
    place,
    mood,
    magicalObject,
    smallProblem,
    conflictType,
    goal,
    resolutionStyle,
    sensoryDetails,
    ageToneProfile,
    choiceAType: choiceAObj.choiceType,
    choiceBType: choiceBObj.choiceType,
    choiceA: choiceAObj.phrase,
    choiceB: choiceBObj.phrase,
  };
}

// ---- emit ----
const plans = [];
for (let i = 0; i < count; i++) plans.push(buildPlan());

if (count === 1) {
  process.stdout.write(JSON.stringify(plans[0], null, 2) + '\n');
} else {
  process.stdout.write(JSON.stringify(plans, null, 2) + '\n');
}
