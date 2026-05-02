#!/usr/bin/env node
//
// StoryModelBakeoff — character name bank validator (Node.js, no
// dependencies).
//
// Run from repo root, from this folder, or from anywhere — the script
// resolves the input paths relative to its own location:
//
//   node tools/StoryModelBakeoff/validate-character-names.js
//
// Pure validation tool. Does NOT modify the name bank. Does NOT
// touch production runtime. Exits 0 on PASS, non-zero on FAIL.
//
// Companion files:
//   - story-character-names.v1.json (the file under validation)
//   - story-seed-bank.v1.json       (source of truth for the
//                                    palettes.animals list)

'use strict';

const fs = require('fs');
const path = require('path');

const NAMES_PATH = path.resolve(__dirname, 'story-character-names.v1.json');
const SEED_BANK_PATH = path.resolve(__dirname, 'story-seed-bank.v1.json');
const NAMES_REL = path.relative(process.cwd(), NAMES_PATH).replace(/\\/g, '/');
const SEED_REL = path.relative(process.cwd(), SEED_BANK_PATH).replace(/\\/g, '/');

const errors = [];
const warnings = [];
function err(msg) { errors.push(msg); }
function warn(msg) { warnings.push(msg); }

console.log('StoryModelBakeoff character-names validator');
console.log('===========================================');
console.log('Names: ' + NAMES_REL);
console.log('Seed bank: ' + SEED_REL);

// ---- existence + parse: name bank ----
if (!fs.existsSync(NAMES_PATH)) {
  console.error('FAIL: name bank file not found at ' + NAMES_PATH);
  process.exit(1);
}
const namesRaw = fs.readFileSync(NAMES_PATH);
console.log('Bytes: ' + namesRaw.length);

let names;
try {
  names = JSON.parse(namesRaw.toString('utf8'));
  console.log('PARSE_OK');
} catch (e) {
  console.error('FAIL: JSON parse failed: ' + e.message);
  process.exit(1);
}

// ---- existence + parse: seed bank (read-only — source of truth) ----
if (!fs.existsSync(SEED_BANK_PATH)) {
  console.error('FAIL: seed bank file not found at ' + SEED_BANK_PATH);
  process.exit(1);
}
let seedBank;
try {
  seedBank = JSON.parse(fs.readFileSync(SEED_BANK_PATH).toString('utf8'));
} catch (e) {
  console.error('FAIL: seed bank JSON parse failed: ' + e.message);
  process.exit(1);
}
const seedAnimals = seedBank && seedBank.palettes && seedBank.palettes.animals;
if (!Array.isArray(seedAnimals) || seedAnimals.length === 0) {
  console.error('FAIL: seed bank palettes.animals is missing or empty');
  process.exit(1);
}

// ---- top-level fields ----
const requiredTop = ['version', 'language', 'purpose', 'animalNames'];
for (const k of requiredTop) {
  if (!(k in names)) err(`top-level field missing: \`${k}\``);
}
console.log();
console.log('Top-level fields:');
console.log('  version             = ' + JSON.stringify(names.version));
console.log('  language            = ' + JSON.stringify(names.language));
console.log('  purpose present     = ' + ('purpose' in names ? 'yes' : 'no'));
console.log('  animalNames present = ' + ('animalNames' in names ? 'yes' : 'no'));
console.log('  sharedNames present = ' + ('sharedNames' in names ? 'yes' : 'no'));

const animalNames = names.animalNames;
if (!animalNames || typeof animalNames !== 'object' || Array.isArray(animalNames)) {
  console.error('FAIL: `animalNames` is missing or not an object.');
  process.exit(1);
}

// ---- coverage check ----
const MIN_NAMES_PER_ANIMAL = 3;
console.log();
console.log('Coverage:');
console.log('  seed-bank animals: ' + seedAnimals.length);
console.log('  animalNames keys:  ' + Object.keys(animalNames).length);

const seedSet = new Set(seedAnimals);
const nameKeys = new Set(Object.keys(animalNames));

for (const animal of seedAnimals) {
  if (!nameKeys.has(animal)) {
    err(`animalNames missing entry for seed-bank animal: "${animal}"`);
  }
}
for (const key of nameKeys) {
  if (!seedSet.has(key)) {
    warn(`animalNames key "${key}" is not in seed bank palettes.animals (extra/orphaned entry)`);
  }
}

// ---- per-animal entry checks ----
console.log();
console.log('Per-animal name lists:');
for (const animal of seedAnimals) {
  const arr = animalNames[animal];
  if (arr === undefined) {
    // already reported above
    continue;
  }
  if (!Array.isArray(arr)) {
    err(`animalNames["${animal}"] is not an array`);
    console.log('  ' + animal.padEnd(22) + ' NOT AN ARRAY');
    continue;
  }
  const status =
    arr.length >= MIN_NAMES_PER_ANIMAL
      ? `(≥${MIN_NAMES_PER_ANIMAL} OK)`
      : `(MIN ${MIN_NAMES_PER_ANIMAL} REQUIRED — short)`;
  console.log('  ' + animal.padEnd(22) + ' ' + String(arr.length).padStart(2) + '   ' + status);
  if (arr.length < MIN_NAMES_PER_ANIMAL) {
    err(`animalNames["${animal}"] has ${arr.length} names; minimum is ${MIN_NAMES_PER_ANIMAL}`);
  }

  for (let i = 0; i < arr.length; i++) {
    const v = arr[i];
    if (typeof v !== 'string') {
      err(`animalNames["${animal}"][${i}] is not a string (got ${typeof v})`);
    } else if (v.trim().length === 0) {
      err(`animalNames["${animal}"][${i}] is empty after trim`);
    }
  }

  // Duplicate detection inside this animal's list.
  const seen = new Map();
  for (let i = 0; i < arr.length; i++) {
    const v = arr[i];
    if (typeof v !== 'string') continue;
    if (seen.has(v)) {
      err(`duplicate in animalNames["${animal}"]: "${v}" (first at index ${seen.get(v)}, again at index ${i})`);
    } else {
      seen.set(v, i);
    }
  }
}

// ---- optional sharedNames ----
console.log();
if ('sharedNames' in names) {
  const shared = names.sharedNames;
  if (!Array.isArray(shared)) {
    err('sharedNames exists but is not an array');
    console.log('Optional sharedNames: PRESENT but malformed');
  } else {
    console.log('Optional sharedNames: ' + shared.length + ' entries');
    for (let i = 0; i < shared.length; i++) {
      const v = shared[i];
      if (typeof v !== 'string') {
        err(`sharedNames[${i}] is not a string (got ${typeof v})`);
      } else if (v.trim().length === 0) {
        err(`sharedNames[${i}] is empty after trim`);
      }
    }
    const seen = new Map();
    for (let i = 0; i < shared.length; i++) {
      const v = shared[i];
      if (typeof v !== 'string') continue;
      if (seen.has(v)) {
        err(`duplicate in sharedNames: "${v}"`);
      } else {
        seen.set(v, i);
      }
    }
  }
} else {
  console.log('Optional sharedNames: not present (allowed)');
}

// ---- final report ----
console.log();
if (warnings.length > 0) {
  console.log('Warnings:');
  for (const w of warnings) console.log('  - ' + w);
  console.log();
}

console.log('Errors: ' + errors.length);
if (errors.length > 0) {
  for (const e of errors) console.log('  - ' + e);
  console.log();
  console.log('RESULT: FAIL');
  process.exit(1);
}

console.log('RESULT: PASS');
process.exit(0);
