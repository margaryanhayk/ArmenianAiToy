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
//
// Choice generation uses category-aware templates rather than naive
// "verb + noun" templates, so the outputs read like grounded story
// actions instead of mechanical phrases. See `objectActions()` and
// `placeActions()` below for the conditional rules. Choices are
// always one place-grounded + one object-grounded, randomised per
// plan so the order does not predict the family.

'use strict';

const fs = require('fs');
const path = require('path');

const SEED_BANK_PATH = path.resolve(__dirname, 'story-seed-bank.v1.json');

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
// Linear congruential generator. Deterministic when --seed is passed;
// falls back to Math.random when no seed is given so two invocations
// don't collide visually.
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
const REQUIRED = [
  'animals', 'places', 'magicalObjects', 'smallProblems',
  'sensoryDetails', 'rareOrRequestedOnlyAnimals', 'hardAvoidCreatures',
];
for (const k of REQUIRED) {
  const arr = palettes && palettes[k];
  if (!Array.isArray(arr) || arr.length === 0) {
    console.error('Seed bank missing or empty palettes.' + k);
    console.error('Run: node tools/StoryModelBakeoff/validate-seed-bank.js');
    process.exit(1);
  }
}

// ---- defensive animal pool ----
// `palettes.animals` is supposed to exclude rareOrRequestedOnlyAnimals
// and hardAvoidCreatures by file convention; we double-check at
// runtime so a future hand-edit cannot silently leak an off-tone
// hero into the default generator.
const block = new Set([
  ...palettes.rareOrRequestedOnlyAnimals,
  ...palettes.hardAvoidCreatures,
]);
const safeAnimals = palettes.animals.filter((x) => !block.has(x));
if (safeAnimals.length < 2) {
  console.error('Not enough safe animals to pick a hero + distinct friend.');
  process.exit(1);
}

// ---- Armenian morphology helpers ----
// Conservative inflection support. The seed bank carries un-inflected
// noun phrases; the generator adds `-ը` / `-ն` (definite) where
// templates need an accusative-like object, and `-ի` (genitive) only
// when the phrase ends in a consonant (irregular -ի-ending stems
// are skipped by returning null so the calling template is filtered
// out instead of producing awkward double-vowel forms).

const VOWELS = new Set(['ա', 'ե', 'է', 'ի', 'ո', 'օ', 'ը', 'ու']);

function endsInVowel(phrase) {
  const s = String(phrase).trim();
  if (s.length === 0) return false;
  // Treat `ու` as a digraph vowel; otherwise check the single last
  // char. The Armenian ligature `և` is treated as consonant-final
  // (its phonetic tail is `-v`), which keeps `տերև` -> `տերևը` as
  // expected.
  const last2 = s.slice(-2);
  if (VOWELS.has(last2)) return true;
  return VOWELS.has(s.slice(-1));
}

function appendDefiniteSuffix(phrase) {
  const s = String(phrase).trim();
  return s + (endsInVowel(s) ? 'ն' : 'ը');
}

// Returns `phrase + 'ի'` for a genitive-style form, but ONLY when
// the phrase ends in a consonant. Returns null for vowel-final
// phrases so the caller drops the template (avoids forms like
// `հայելի-ի` / `ընկուզենի-ի`).
function appendGenitiveSafe(phrase) {
  if (endsInVowel(phrase)) return null;
  return String(phrase).trim() + 'ի';
}

// ---- shape classifiers (keyword heuristics) ----
// Cheap regex-based detection. Drives which conditional templates
// are eligible for a given object / place. If no class matches,
// only universal templates are used — never a malformed action.

function isOpenable(obj) {
  return /(տուփ|սրվակ|կուժ|տոպրակ|սփռոց)/.test(obj);
}
function isShiny(obj) {
  // Includes glow, gold, silver, light, pearl, star, ray.
  return /(ոսկ|արծաթ|լուսավոր|փայլող|մարգարիտ|աստղ|լույս|շող)/.test(obj);
}
function isSoundCapable(obj) {
  return /(զանգակ|սանր|սրինգ|սուլիչ|խեցի|փետուր|կաթիլ|երգող|խոսող|հնչող|հայելի)/.test(obj);
}

function isWaterOrLow(place) {
  // Lake / spring / brook / shore / valley / lower / marsh.
  return /(լճակ|աղբյուր|առվակ|ափ|ձոր|ներքև|ճահ)/.test(place);
}
function isHighOrUp(place) {
  // Hill / mountain / rock / peak / oak / tree / walnut / tower /
  // branch / "high".
  return /(բլուր|սար|ժայռ|կատար|լեռ|կաղնի|ծառ|ընկուզենի|աշտարակ|ճյուղ|բարձ)/.test(place);
}

// ---- object-grounded action templates ----
//
// Universal templates (always present):
//   "վերցնել {obj}"                   — pick up. Bare nominative
//                                       reads as accusative-shape
//                                       in Armenian fairy-tale style.
//   "տանել {obj}ը ընկերոջ մոտ"        — take it to the friend.
//   "պահել {obj}ը ափի մեջ"            — keep it in palm.
//   "մոտեցնել {obj}ը լույսին"         — bring close to the light.
//
// Conditional templates:
//   "բացել {obj}ը"                    — only if isOpenable.
//   "հետևել {obj}ի փայլին"            — only if isShiny AND
//                                       genitive form is safe
//                                       (consonant-ending).
//   "լսել՝ արդյոք {obj}ը ձայն ունի"   — only if isSoundCapable.
function objectActions(obj) {
  const def = appendDefiniteSuffix(obj);
  const out = [
    'վերցնել ' + obj,
    'տանել ' + def + ' ընկերոջ մոտ',
    'պահել ' + def + ' ափի մեջ',
    'մոտեցնել ' + def + ' լույսին',
  ];
  if (isOpenable(obj)) out.push('բացել ' + def);
  const gen = appendGenitiveSafe(obj);
  if (isShiny(obj) && gen) out.push('հետևել ' + gen + ' փայլին');
  if (isSoundCapable(obj)) out.push('լսել՝ արդյոք ' + def + ' ձայն ունի');
  return out;
}

// ---- place-grounded action templates ----
//
// All four templates use the preposition `դեպի` (toward), which
// takes the nominative — so we never need to inflect the place
// phrase. That dodges the irregular-stem hazard for words like
// `ընկուզենի` (genitive `ընկուզենու`) entirely.
//
// Universal:
//   "գնալ դեպի {place}"
//   "քայլել դեպի {place}"
// Conditional:
//   "իջնել դեպի {place}"     — only if isWaterOrLow.
//   "բարձրանալ դեպի {place}" — only if isHighOrUp.
function placeActions(place) {
  const out = [
    'գնալ դեպի ' + place,
    'քայլել դեպի ' + place,
  ];
  if (isWaterOrLow(place)) out.push('իջնել դեպի ' + place);
  if (isHighOrUp(place)) out.push('բարձրանալ դեպի ' + place);
  return out;
}

// ---- plan builder ----
function buildPlan() {
  const hero = pick(safeAnimals);
  let friend = pick(safeAnimals);
  while (friend === hero) friend = pick(safeAnimals);

  const place = pick(palettes.places);
  const magicalObject = pick(palettes.magicalObjects);
  const smallProblem = pick(palettes.smallProblems);
  const sensoryDetails = pickDistinct(palettes.sensoryDetails, 2);

  const objCandidates = objectActions(magicalObject);
  const plcCandidates = placeActions(place);

  // One place-grounded + one object-grounded action per plan.
  // Order randomised so the user can't predict which family is A
  // vs B from output position alone.
  const objChoice = pick(objCandidates);
  const plcChoice = pick(plcCandidates);
  const aIsPlace = rng() < 0.5;
  const choiceA = aIsPlace ? plcChoice : objChoice;
  const choiceB = aIsPlace ? objChoice : plcChoice;

  return {
    hero,
    place,
    magicalObject,
    friendOrGuide: friend,
    smallProblem,
    sensoryDetails,
    choiceA,
    choiceB,
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
