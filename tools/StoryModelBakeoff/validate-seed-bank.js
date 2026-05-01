#!/usr/bin/env node
//
// StoryModelBakeoff — seed-bank validator (Node.js, no dependencies).
//
// Run from repo root, from this folder, or from anywhere — the script
// resolves the seed-bank path relative to its own location:
//
//   node tools/StoryModelBakeoff/validate-seed-bank.js
//
// Pure validation tool. Does NOT modify the seed bank. Does NOT
// touch production runtime. Exits 0 on PASS, non-zero on FAIL.
//
// Companion docs:
//   - STORY_DIRECTOR_ARCHITECTURE.md (design)
//   - story-seed-bank.v1.json        (the file under validation)

'use strict';

const fs = require('fs');
const path = require('path');

// ---- file location ----
const SEED_BANK_PATH = path.resolve(__dirname, 'story-seed-bank.v1.json');
const REL_PATH = path.relative(process.cwd(), SEED_BANK_PATH).replace(/\\/g, '/');

// ---- collected output ----
const errors = [];
const warnings = [];
function err(msg) { errors.push(msg); }
function warn(msg) { warnings.push(msg); }

// ---- header ----
console.log('StoryModelBakeoff seed-bank validator');
console.log('=====================================');
console.log('File: ' + REL_PATH);

// ---- existence + parse ----
if (!fs.existsSync(SEED_BANK_PATH)) {
  console.error('FAIL: seed bank file not found at ' + SEED_BANK_PATH);
  process.exit(1);
}
const raw = fs.readFileSync(SEED_BANK_PATH);
console.log('Bytes: ' + raw.length);

let data;
try {
  data = JSON.parse(raw.toString('utf8'));
  console.log('PARSE_OK');
} catch (e) {
  console.error('FAIL: JSON parse failed: ' + e.message);
  process.exit(1);
}

// ---- top-level fields ----
const requiredTop = ['version', 'language', 'purpose', 'palettes'];
for (const k of requiredTop) {
  if (!(k in data)) err(`top-level field missing: \`${k}\``);
}
console.log();
console.log('Top-level fields:');
console.log('  version          = ' + JSON.stringify(data.version));
console.log('  language         = ' + JSON.stringify(data.language));
console.log('  purpose present  = ' + ('purpose' in data ? 'yes' : 'no'));
console.log('  palettes present = ' + ('palettes' in data ? 'yes' : 'no'));

const palettes = data.palettes;
if (!palettes || typeof palettes !== 'object' || Array.isArray(palettes)) {
  console.error('FAIL: `palettes` is missing or not an object.');
  process.exit(1);
}

// ---- required palette arrays + minimum counts ----
const REQUIRED = {
  animals: 30,
  places: 30,
  magicalObjects: 30,
  smallProblems: 30,
  sensoryDetails: 30,
  gentleActions: 30,
  choiceVerbs: 20,
  rareOrRequestedOnlyAnimals: 5,
  hardAvoidCreatures: 3,
  avoidPatterns: 10,
};

console.log();
console.log('Palette counts:');
for (const [name, min] of Object.entries(REQUIRED)) {
  const arr = palettes[name];
  if (!Array.isArray(arr)) {
    console.log('  ' + name.padEnd(28) + ' MISSING or not an array');
    err(`palettes.${name} is missing or not an array`);
    continue;
  }
  const status =
    arr.length >= min ? `(≥${min} OK)` : `(MIN ${min} REQUIRED — short)`;
  console.log('  ' + name.padEnd(28) + ' ' + String(arr.length).padStart(3) + '   ' + status);
  if (arr.length < min) {
    err(`palettes.${name} has ${arr.length} entries; minimum is ${min}`);
  }

  // String + non-empty after trim.
  for (let i = 0; i < arr.length; i++) {
    const v = arr[i];
    if (typeof v !== 'string') {
      err(`palettes.${name}[${i}] is not a string (got ${typeof v})`);
    } else if (v.trim().length === 0) {
      err(`palettes.${name}[${i}] is empty after trim`);
    }
  }

  // Duplicate detection.
  const seen = new Map(); // value -> first index
  for (let i = 0; i < arr.length; i++) {
    const v = arr[i];
    if (typeof v !== 'string') continue;
    const key = v;
    if (seen.has(key)) {
      err(`duplicate in palettes.${name}: "${v}" (first at index ${seen.get(key)}, again at index ${i})`);
    } else {
      seen.set(key, i);
    }
  }
}

// ---- optional traditionalFormulas ----
console.log();
if ('traditionalFormulas' in palettes) {
  const tf = palettes.traditionalFormulas;
  if (!tf || typeof tf !== 'object' || Array.isArray(tf)) {
    err('palettes.traditionalFormulas exists but is not an object');
    console.log('Optional traditionalFormulas: PRESENT but malformed');
  } else {
    const subKeys = ['openings', 'transitions', 'closings'];
    const summary = [];
    for (const k of subKeys) {
      const sub = tf[k];
      if (!Array.isArray(sub)) {
        err(`palettes.traditionalFormulas.${k} is missing or not an array`);
        summary.push(`${k}=MISSING`);
        continue;
      }
      summary.push(`${k}=${sub.length}`);
      for (let i = 0; i < sub.length; i++) {
        const v = sub[i];
        if (typeof v !== 'string') {
          err(`palettes.traditionalFormulas.${k}[${i}] is not a string`);
        } else if (v.trim().length === 0) {
          err(`palettes.traditionalFormulas.${k}[${i}] is empty after trim`);
        }
      }
      // Duplicate detection inside the sub-array.
      const seen = new Map();
      for (let i = 0; i < sub.length; i++) {
        const v = sub[i];
        if (typeof v !== 'string') continue;
        if (seen.has(v)) {
          err(`duplicate in palettes.traditionalFormulas.${k}: "${v}"`);
        } else {
          seen.set(v, i);
        }
      }
    }
    console.log('Optional traditionalFormulas: present (' + summary.join(', ') + ')');
  }
} else {
  console.log('Optional traditionalFormulas: not present (allowed)');
}

// ---- deprecated key check ----
if ('avoidAnimals' in palettes) {
  err(
    'deprecated key `palettes.avoidAnimals` present. Split it into ' +
    '`rareOrRequestedOnlyAnimals` (soft guard) and `hardAvoidCreatures` ' +
    '(hard guard); see STORY_DIRECTOR_ARCHITECTURE.md.'
  );
}

// ---- known bad values (anywhere in any palette array) ----
const KNOWN_BAD = [
  'նապիկ-մարդակեր',
  'վիշապ (եթե վախենալու է)',
  'մոթի մեջ փայլող քար',
];

function* walkAllStrings(obj, pathPrefix) {
  if (obj == null) return;
  if (typeof obj === 'string') {
    yield { path: pathPrefix, value: obj };
    return;
  }
  if (Array.isArray(obj)) {
    for (let i = 0; i < obj.length; i++) {
      yield* walkAllStrings(obj[i], pathPrefix + '[' + i + ']');
    }
    return;
  }
  if (typeof obj === 'object') {
    for (const [k, v] of Object.entries(obj)) {
      yield* walkAllStrings(v, pathPrefix ? pathPrefix + '.' + k : k);
    }
  }
}

for (const { path: p, value: v } of walkAllStrings(palettes, 'palettes')) {
  for (const bad of KNOWN_BAD) {
    if (v === bad) {
      err(`known-bad value "${bad}" found at ${p}`);
    }
  }
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
