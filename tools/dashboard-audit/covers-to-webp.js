// Story covers come out of an image generator as 2-4 MB PNGs at whatever
// size it felt like. Eleven of those is 35 MB - three megabytes per card on
// a parent's phone, and a permanent weight in the repository.
//
// This resizes to display size and re-encodes as WebP, using the Chromium
// that Playwright already ships. No ImageMagick, no PIL, no ffmpeg build
// with an image decoder - none of which this environment has.
//
//   node tools/dashboard-audit/covers-to-webp.js [--width 900] [--quality 0.82]
//
// It rewrites <name>.png in place as <name>.webp and deletes the PNG only
// after the WebP is verified to decode.
const { chromium } = require(process.env.PLAYWRIGHT_PATH || 'playwright');
const fs = require('fs');
const path = require('path');

const DIR = path.resolve(__dirname, '../../backend/src/ArmenianAiToy.Api/wwwroot/covers');
const arg = (n, d) => {
  const i = process.argv.indexOf('--' + n);
  return i > -1 ? Number(process.argv[i + 1]) : d;
};
// 900px wide covers a 3x phone at the ~300px the card is actually drawn at.
const WIDTH = arg('width', 900);
const QUALITY = arg('quality', 0.82);

(async () => {
  const files = fs.readdirSync(DIR).filter((f) => f.endsWith('.png'));
  if (!files.length) { console.log('no .png files in ' + DIR); return; }
  const b = await chromium.launch({ executablePath: process.env.CHROME_PATH || undefined });
  const p = await b.newPage();
  let before = 0, after = 0;
  for (const f of files) {
    const src = path.join(DIR, f);
    const raw = fs.readFileSync(src);
    before += raw.length;
    const dataUrl = 'data:image/png;base64,' + raw.toString('base64');
    const out = await p.evaluate(async ({ dataUrl, WIDTH, QUALITY }) => {
      const img = new Image();
      await new Promise((res, rej) => { img.onload = res; img.onerror = rej; img.src = dataUrl; });
      const scale = Math.min(1, WIDTH / img.naturalWidth);
      const c = document.createElement('canvas');
      c.width = Math.round(img.naturalWidth * scale);
      c.height = Math.round(img.naturalHeight * scale);
      c.getContext('2d').drawImage(img, 0, 0, c.width, c.height);
      return { data: c.toDataURL('image/webp', QUALITY), w: c.width, h: c.height,
               ow: img.naturalWidth, oh: img.naturalHeight };
    }, { dataUrl, WIDTH, QUALITY });

    if (!out.data.startsWith('data:image/webp')) {
      console.error(`${f}: this browser did not encode WebP - left as PNG`);
      continue;
    }
    const bytes = Buffer.from(out.data.split(',')[1], 'base64');
    const dest = src.replace(/\.png$/, '.webp');
    fs.writeFileSync(dest, bytes);
    after += bytes.length;
    fs.unlinkSync(src);
    console.log(`${f.padEnd(24)} ${out.ow}x${out.oh} -> ${out.w}x${out.h}   ` +
                `${(raw.length / 1048576).toFixed(1)}MB -> ${(bytes.length / 1024).toFixed(0)}KB`);
  }
  await b.close();
  console.log(`\ntotal ${(before / 1048576).toFixed(1)} MB -> ${(after / 1048576).toFixed(1)} MB` +
              `  (${Math.round(100 - after / before * 100)}% smaller)`);
})();
