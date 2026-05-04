#!/usr/bin/env node
//
// StoryModelBakeoff — Story Plan validator (Plan Gate, Phase 3 first
// half per STORY_DIRECTOR_ARCHITECTURE.md).
//
// Tool-only deterministic validator for generated story plans.
// Pure-stdlib, no dependencies, never modifies the input. Reads
// pretty JSON from stdin OR from a file path argument, verifies the
// 17-field shape, the seed-bank membership of each field, the
// guardrail invariants (hardAvoidCreatures / forbiddenTonePatterns
// leaks, banned choice phrases), and a few choice-quality
// consistency rules. Reports per-plan PASS/FAIL with errors and
// lightweight warnings; exits 0 if no errors, 1 otherwise.
//
// Optional fields (per evaluations/character-name-wiring-plan-20260503.md):
// `heroName` and `friendOrGuideName`. Half-state allowed. When
// present, each must be a non-empty string in
// animalNames[<corresponding-animal-field>] ∪ sharedNames, and
// the two names must differ. Older 17-field plans that omit these
// fields continue to PASS.
//
// Usage:
//   node tools/StoryModelBakeoff/generate-story-plan.js --count 10 --seed 123 \
//     | node tools/StoryModelBakeoff/validate-story-plan.js
//   node tools/StoryModelBakeoff/validate-story-plan.js path/to/plans.json

'use strict';

const fs = require('fs');
const path = require('path');

const SEED_BANK_PATH = path.resolve(__dirname, 'story-seed-bank.v1.json');
const NAMES_BANK_PATH = path.resolve(__dirname, 'story-character-names.v1.json');

// Load the seed bank for membership + guardrail checks.
let seedBank;
try {
  seedBank = JSON.parse(fs.readFileSync(SEED_BANK_PATH, 'utf8'));
} catch (e) {
  console.error('FAIL: could not read or parse seed bank: ' + e.message);
  process.exit(1);
}
const palettes = seedBank && seedBank.palettes;
if (!palettes || typeof palettes !== 'object') {
  console.error('FAIL: seed bank has no palettes object.');
  process.exit(1);
}

// Load the character name bank if it exists. Optional — only fails
// per-plan when a plan actually carries heroName / friendOrGuideName
// AND the bank is missing or malformed.
// See evaluations/character-name-wiring-plan-20260503.md § 5.3.
let namesBank = null;
let namesBankError = null;
if (fs.existsSync(NAMES_BANK_PATH)) {
  try {
    namesBank = JSON.parse(fs.readFileSync(NAMES_BANK_PATH, 'utf8'));
  } catch (e) {
    namesBankError = 'parse error: ' + e.message;
  }
  if (namesBank
      && (typeof namesBank.animalNames !== 'object'
          || Array.isArray(namesBank.animalNames))) {
    namesBankError = 'animalNames missing or not an object';
    namesBank = null;
  }
} else {
  namesBankError = 'file not found at ' + NAMES_BANK_PATH;
}

// ---- constants ----
const REQUIRED_FIELDS = [
  'hero', 'heroTrait', 'friendOrGuide', 'relationship',
  'place', 'mood', 'magicalObject', 'smallProblem',
  'conflictType', 'goal', 'resolutionStyle', 'sensoryDetails',
  'ageToneProfile', 'choiceAType', 'choiceBType', 'choiceA',
  'choiceB',
];

// Field → seed-bank palette key for membership checks (skip
// sensoryDetails + ageToneProfile here; both checked specially).
const FIELD_TO_PALETTE = {
  hero: 'animals',
  friendOrGuide: 'animals',
  heroTrait: 'characterTraits',
  relationship: 'relationshipTypes',
  place: 'places',
  mood: 'storyMoods',
  magicalObject: 'magicalObjects',
  smallProblem: 'smallProblems',
  conflictType: 'conflictTypes',
  goal: 'characterGoals',
  resolutionStyle: 'resolutionStyles',
  choiceAType: 'choiceTypes',
  choiceBType: 'choiceTypes',
};

const STRING_FIELDS = [
  'hero', 'heroTrait', 'friendOrGuide', 'relationship',
  'place', 'mood', 'magicalObject', 'smallProblem',
  'conflictType', 'goal', 'resolutionStyle',
  'choiceAType', 'choiceBType', 'choiceA', 'choiceB',
];

const ATP_REQUIRED_FIELDS = [
  'label', 'ageRange', 'sentenceStyle', 'wordChoice', 'targetWords',
];

const PLACE_CHOICE_TYPE = 'գնալ դեպի վայր';

const KNOWN_BAD = [
  'նապիկ-մարդակեր',
  'վիշապ (եթե վախենալու է)',
  'մոթի մեջ փայլող քար',
];

const BANNED_EXACT_CHOICES = [
  'շարունակել', 'գնալ առաջ', 'չգիտեմ', 'այո', 'ոչ',
];

const BANNED_CHOICE_SUBSTRINGS = [
  'բացել ճյուղ',
  'դիտել ',
  'թակել լճակ',
  'շոյել քարայր',
];

// Inspection-template warning: if a choice uses
// "մոտեցնել X-ը լույսին" but the magicalObject doesn't read as
// shiny / openable / sound-capable / inspectable, flag a warning
// (not a hard error — the writer might still find it natural).
function isShiny(obj) {
  return /(ոսկ|արծաթ|լուսավոր|փայլող|մարգարիտ|աստղ|լույս|շող)/.test(obj);
}
function isOpenable(obj) {
  return /(տուփ|սրվակ|կուժ|տոպրակ|սփռոց)/.test(obj);
}
function isSoundCapable(obj) {
  return /(զանգակ|սանր|սրինգ|սուլիչ|խեցի|փետուր|կաթիլ|երգող|խոսող|հնչող|հայելի)/.test(obj);
}
function isInspectionNatural(obj) {
  return isShiny(obj) || isOpenable(obj) || isSoundCapable(obj);
}

// ---- input loading ----
const argPath = process.argv[2];
let inputSource;
let raw;
try {
  if (argPath && !argPath.startsWith('--')) {
    raw = fs.readFileSync(argPath, 'utf8');
    inputSource = argPath;
  } else if (!process.stdin.isTTY) {
    raw = fs.readFileSync(0, 'utf8'); // fd 0 = stdin
    inputSource = '<stdin>';
  } else {
    console.error(
      'FAIL: no input. Pipe JSON to stdin OR pass a file path.\n' +
      'Examples:\n' +
      '  node tools/StoryModelBakeoff/generate-story-plan.js | \\\n' +
      '    node tools/StoryModelBakeoff/validate-story-plan.js\n' +
      '  node tools/StoryModelBakeoff/validate-story-plan.js path/to/plans.json'
    );
    process.exit(1);
  }
} catch (e) {
  console.error('FAIL: could not read input: ' + e.message);
  process.exit(1);
}

if (!raw || raw.trim().length === 0) {
  console.error('FAIL: input is empty.');
  process.exit(1);
}

let parsed;
try {
  parsed = JSON.parse(raw);
} catch (e) {
  console.error('FAIL: input is not valid JSON: ' + e.message);
  process.exit(1);
}

const plans = Array.isArray(parsed) ? parsed : [parsed];

// ---- per-plan validation ----
function* walkStrings(obj) {
  if (obj == null) return;
  if (typeof obj === 'string') { yield obj; return; }
  if (Array.isArray(obj)) {
    for (const v of obj) yield* walkStrings(v);
    return;
  }
  if (typeof obj === 'object') {
    for (const v of Object.values(obj)) yield* walkStrings(v);
  }
}

function isNonEmptyString(v) {
  return typeof v === 'string' && v.trim().length > 0;
}

function inPalette(value, paletteKey) {
  const arr = palettes[paletteKey];
  if (!Array.isArray(arr)) return false;
  return arr.includes(value);
}

function validatePlan(plan, idx) {
  const errors = [];
  const warnings = [];

  // Plan must be an object.
  if (plan == null || typeof plan !== 'object' || Array.isArray(plan)) {
    errors.push('plan is not an object');
    return { idx, errors, warnings, pass: false };
  }

  // Required field presence.
  for (const f of REQUIRED_FIELDS) {
    if (!(f in plan)) errors.push(`missing field: ${f}`);
  }

  // String-field non-empty check.
  for (const f of STRING_FIELDS) {
    if (f in plan && !isNonEmptyString(plan[f])) {
      errors.push(`${f} is missing, not a string, or empty`);
    }
  }

  // sensoryDetails: array of exactly 2 distinct non-empty strings.
  if ('sensoryDetails' in plan) {
    const sd = plan.sensoryDetails;
    if (!Array.isArray(sd)) {
      errors.push('sensoryDetails is not an array');
    } else if (sd.length !== 2) {
      errors.push(`sensoryDetails must have exactly 2 entries, got ${sd.length}`);
    } else {
      for (let i = 0; i < sd.length; i++) {
        if (!isNonEmptyString(sd[i])) {
          errors.push(`sensoryDetails[${i}] is missing, not a string, or empty`);
        }
      }
      if (sd.length === 2 && sd[0] === sd[1]) {
        errors.push('sensoryDetails entries are not distinct');
      }
      // Membership.
      for (let i = 0; i < sd.length; i++) {
        if (isNonEmptyString(sd[i]) && !inPalette(sd[i], 'sensoryDetails')) {
          errors.push(
            `sensoryDetails[${i}] not in palettes.sensoryDetails: "${sd[i]}"`
          );
        }
      }
    }
  }

  // ageToneProfile: object with required string fields, label must
  // be in palettes.ageToneProfiles.
  if ('ageToneProfile' in plan) {
    const atp = plan.ageToneProfile;
    if (atp == null || typeof atp !== 'object' || Array.isArray(atp)) {
      errors.push('ageToneProfile is not an object');
    } else {
      for (const sub of ATP_REQUIRED_FIELDS) {
        if (!isNonEmptyString(atp[sub])) {
          errors.push(`ageToneProfile.${sub} is missing, not a string, or empty`);
        }
      }
      if (isNonEmptyString(atp.label)) {
        const labels = Array.isArray(palettes.ageToneProfiles)
          ? palettes.ageToneProfiles.map((p) => p && p.label).filter(Boolean)
          : [];
        if (!labels.includes(atp.label)) {
          errors.push(
            `ageToneProfile.label not in palettes.ageToneProfiles labels: "${atp.label}"`
          );
        }
        // Lightweight warning: rich tone may be long for younger ages.
        if (atp.label === 'age-7-richer') {
          warnings.push(
            'ageToneProfile is "age-7-richer" — verify the rendered story length suits the intended target age'
          );
        }
      }
    }
  }

  // hero / friendOrGuide: distinct, both not in rare / hard-avoid.
  if (isNonEmptyString(plan.hero) && isNonEmptyString(plan.friendOrGuide)) {
    if (plan.hero === plan.friendOrGuide) {
      errors.push('hero and friendOrGuide are the same animal');
    }
  }
  for (const fld of ['hero', 'friendOrGuide']) {
    const v = plan[fld];
    if (!isNonEmptyString(v)) continue;
    if (Array.isArray(palettes.rareOrRequestedOnlyAnimals)
        && palettes.rareOrRequestedOnlyAnimals.includes(v)) {
      errors.push(`${fld} is in rareOrRequestedOnlyAnimals: "${v}"`);
    }
    if (Array.isArray(palettes.hardAvoidCreatures)
        && palettes.hardAvoidCreatures.includes(v)) {
      errors.push(`${fld} is in hardAvoidCreatures: "${v}"`);
    }
  }

  // Field-level membership checks.
  for (const [field, paletteKey] of Object.entries(FIELD_TO_PALETTE)) {
    const v = plan[field];
    if (!isNonEmptyString(v)) continue;
    if (!inPalette(v, paletteKey)) {
      errors.push(
        `${field} not in palettes.${paletteKey}: "${v}"`
      );
    }
  }

  // Guardrails: hardAvoidCreatures / forbiddenTonePatterns leak,
  // KNOWN_BAD substring leak. Walk every string in the plan.
  const allStrings = [...walkStrings(plan)];
  function leakCheck(needles, label) {
    if (!Array.isArray(needles)) return;
    for (const n of needles) {
      if (typeof n !== 'string' || n.length === 0) continue;
      for (const s of allStrings) {
        if (s.includes(n)) {
          errors.push(`${label} leak: "${n}"`);
          break;
        }
      }
    }
  }
  leakCheck(palettes.hardAvoidCreatures, 'hardAvoidCreatures');
  leakCheck(palettes.forbiddenTonePatterns, 'forbiddenTonePatterns');
  // Static known-bad (legacy / cleanup typos that have shown up before).
  for (const bad of KNOWN_BAD) {
    for (const s of allStrings) {
      if (s.includes(bad)) {
        errors.push(`known-bad value present: "${bad}"`);
        break;
      }
    }
  }

  // Choice-text quality + grounding + type consistency.
  const place = isNonEmptyString(plan.place) ? plan.place : null;
  const obj = isNonEmptyString(plan.magicalObject) ? plan.magicalObject : null;
  function checkChoice(choice, choiceType, label) {
    if (!isNonEmptyString(choice)) return;
    // Banned-exact.
    if (BANNED_EXACT_CHOICES.includes(choice.trim())) {
      errors.push(`${label} is exactly a banned phrase: "${choice}"`);
    }
    // Banned-substring.
    for (const sub of BANNED_CHOICE_SUBSTRINGS) {
      if (choice.includes(sub)) {
        errors.push(`${label} contains banned pattern "${sub}": "${choice}"`);
      }
    }
    // Spatially-vacuous regression check: a bare
    // "գնալ դեպի <plan.place>" / "քայլել դեպի <plan.place>"
    // (and the իջնել / բարձրանալ variants) reads as "go to the
    // place we're already in", because plan.place IS the scene's
    // setting. The 2026-05-04 generator fix replaces these
    // templates with sub-location patterns; this check is a
    // regression guard. See
    // `evaluations/story-brain-finalization-20260504.md` § 4.
    //
    // WARNING (not a hard error) so older plan files generated
    // before the 2026-05-04 generator fix continue to validate
    // for back-compat. Plans emitted by the current generator
    // will not match these patterns.
    if (place != null && place.length > 0) {
      const trimmed = choice.trim();
      const vacuousPrefixes = [
        'գնալ դեպի ', 'քայլել դեպի ',
        'իջնել դեպի ', 'բարձրանալ դեպի ',
      ];
      for (const prefix of vacuousPrefixes) {
        if (trimmed === prefix + place) {
          warnings.push(
            `${label} is spatially vacuous: "${choice}" — story opens in ` +
            `plan.place ("${place}"), so this choice points to the current scene; ` +
            `use a sub-location or scene-element template (see story-brain-finalization-20260504.md § 4)`
          );
          break;
        }
      }
    }
    if (place == null || obj == null) return; // can't check grounding
    // Grounding: choice must reference place OR magicalObject.
    const refsPlace = choice.includes(place);
    const refsObj = choice.includes(obj);
    if (!refsPlace && !refsObj) {
      errors.push(
        `${label} does not reference plan.place or plan.magicalObject: "${choice}"`
      );
      return;
    }
    // Type consistency.
    if (refsPlace && choiceType !== PLACE_CHOICE_TYPE) {
      errors.push(
        `${label}Type is "${choiceType}" but ${label} references the plan's place — ` +
        `expected "${PLACE_CHOICE_TYPE}"`
      );
    }
    if (!refsPlace && refsObj && choiceType === PLACE_CHOICE_TYPE) {
      errors.push(
        `${label}Type is "${PLACE_CHOICE_TYPE}" but ${label} references the plan's ` +
        `magicalObject — expected an object-family choiceType`
      );
    }
    // Inspection-template warning.
    if (choice.includes('մոտեցնել') && choice.includes('լույսին')) {
      if (obj && !isInspectionNatural(obj)) {
        warnings.push(
          `${label} uses inspection template ("մոտեցնել X-ը լույսին") on object ` +
          `that may not be naturally inspectable: "${obj}"`
        );
      }
    }
  }
  checkChoice(plan.choiceA, plan.choiceAType, 'choiceA');
  checkChoice(plan.choiceB, plan.choiceBType, 'choiceB');
  if (isNonEmptyString(plan.choiceA) && isNonEmptyString(plan.choiceB)
      && plan.choiceA === plan.choiceB) {
    errors.push('choiceA and choiceB are identical');
  }

  // Optional character-name fields per
  // evaluations/character-name-wiring-plan-20260503.md § 5.
  // Both fields are optional at the wire shape; half-state allowed.
  function checkOptionalName(field, animalField) {
    if (!(field in plan)) return; // optional — absence is fine
    const v = plan[field];
    if (typeof v !== 'string' || v.trim().length === 0) {
      errors.push(`${field} is present but not a non-empty string`);
      return;
    }
    // Membership check requires the name bank.
    if (!namesBank) {
      errors.push(
        `${field} present but character name bank is unavailable ` +
        `(${namesBankError}); cannot validate membership`
      );
      return;
    }
    const animal = plan[animalField];
    if (!isNonEmptyString(animal)) return; // animal-field check fires elsewhere
    const perAnimal = Array.isArray(namesBank.animalNames[animal])
      ? namesBank.animalNames[animal] : [];
    const shared = Array.isArray(namesBank.sharedNames)
      ? namesBank.sharedNames : [];
    const allowed = new Set([...perAnimal, ...shared]);
    if (!allowed.has(v)) {
      errors.push(
        `${field} "${v}" not in animalNames["${animal}"] ∪ sharedNames`
      );
    }
  }
  checkOptionalName('heroName', 'hero');
  checkOptionalName('friendOrGuideName', 'friendOrGuide');
  if ('heroName' in plan && 'friendOrGuideName' in plan
      && isNonEmptyString(plan.heroName)
      && isNonEmptyString(plan.friendOrGuideName)
      && plan.heroName === plan.friendOrGuideName) {
    errors.push('heroName and friendOrGuideName are identical');
  }

  return { idx, errors, warnings, pass: errors.length === 0 };
}

// ---- run + report ----
const reports = plans.map((p, i) => validatePlan(p, i + 1));
let totalErrors = 0;
let totalWarnings = 0;
for (const r of reports) {
  totalErrors += r.errors.length;
  totalWarnings += r.warnings.length;
}

console.log('STORY_PLAN_VALIDATOR');
console.log('====================');
console.log('Input:    ' + inputSource);
console.log('Plans:    ' + plans.length);
console.log();
for (const r of reports) {
  const flag = r.pass ? 'PASS' : 'FAIL';
  console.log(`Plan ${r.idx}: ${flag}`);
  if (r.errors.length > 0) {
    console.log('  errors:');
    for (const e of r.errors) console.log('    - ' + e);
  }
  if (r.warnings.length > 0) {
    console.log('  warnings:');
    for (const w of r.warnings) console.log('    - ' + w);
  }
}

console.log();
console.log('Summary:');
console.log('  Plans:    ' + plans.length);
console.log('  Errors:   ' + totalErrors);
console.log('  Warnings: ' + totalWarnings);
console.log('RESULT: ' + (totalErrors === 0 ? 'PASS' : 'FAIL'));
process.exit(totalErrors === 0 ? 0 : 1);
