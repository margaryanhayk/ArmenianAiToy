// One-off icon generator for the Areg parent app. "Areg" (Արեգ) = sun, so the
// mark is a simple warm sun. Renders SVG -> PNG with sharp. Run: node scripts/gen-icons.js
// Requires sharp (dev-only). Not part of the app bundle.
const fs = require('fs');
const path = require('path');
let sharp;
try {
  sharp = require('sharp');
} catch {
  console.error('sharp not installed; skipping icon generation.');
  process.exit(0);
}

const SUN = '#F5A623';
const SUN_CORE = '#FDB94E';
const BG = '#FFF1D6';

// Build a sun SVG. size = canvas, r = core radius, transparent = no bg fill.
function sunSvg(size, r, transparent) {
  const c = size / 2;
  const rayInner = r * 1.25;
  const rayOuter = r * 1.7;
  const rayW = Math.max(8, r * 0.16);
  let rays = '';
  const n = 12;
  for (let i = 0; i < n; i++) {
    const a = (Math.PI * 2 * i) / n;
    const x1 = c + Math.cos(a) * rayInner;
    const y1 = c + Math.sin(a) * rayInner;
    const x2 = c + Math.cos(a) * rayOuter;
    const y2 = c + Math.sin(a) * rayOuter;
    rays += `<line x1="${x1.toFixed(1)}" y1="${y1.toFixed(1)}" x2="${x2.toFixed(1)}" y2="${y2.toFixed(1)}" stroke="${SUN}" stroke-width="${rayW.toFixed(1)}" stroke-linecap="round"/>`;
  }
  const bg = transparent ? '' : `<rect width="${size}" height="${size}" fill="${BG}"/>`;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${size} ${size}">${bg}<g>${rays}</g><circle cx="${c}" cy="${c}" r="${r}" fill="${SUN}"/><circle cx="${c}" cy="${c}" r="${(r * 0.66).toFixed(1)}" fill="${SUN_CORE}"/></svg>`;
}

const assets = path.join(__dirname, '..', 'assets');

async function render(svg, size, out) {
  await sharp(Buffer.from(svg)).resize(size, size).png().toFile(path.join(assets, out));
  console.log('wrote', out);
}

(async () => {
  // Main app icon: full bleed sun on warm bg.
  await render(sunSvg(1024, 260, false), 1024, 'icon.png');
  // Android adaptive foreground: sun centered, smaller (safe zone), transparent.
  await render(sunSvg(1024, 200, true), 1024, 'android-icon-foreground.png');
  // Splash icon: transparent sun.
  await render(sunSvg(1024, 240, true), 1024, 'splash-icon.png');
  // Favicon.
  await render(sunSvg(256, 70, false), 48, 'favicon.png');
  console.log('done');
})();
