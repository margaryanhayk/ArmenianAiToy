const { chromium } = require(process.env.PLAYWRIGHT_PATH || 'playwright');
const fs = require('fs');
const OUT = __dirname + '/shots';
fs.mkdirSync(OUT, { recursive: true });
const BASE = 'http://127.0.0.1:5099';
const log = [];
const note = (s) => { log.push(s); console.log(s); };

(async () => {
  const browser = await chromium.launch({
    executablePath: process.env.CHROME_PATH || undefined });
  const page = await browser.newPage({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2 });
  page.on('pageerror', e => note('  [page error] ' + e.message));

  const visibleView = () => page.evaluate(() =>
    ([...document.querySelectorAll('section[id$="View"]')].find(s => !s.hidden) || {}).id || 'none');

  const shot = async (name) => {
    await page.waitForTimeout(500);
    await page.screenshot({ path: `${OUT}/${name}.png`, fullPage: true });
    const m = await page.evaluate(() => {
      const vis = (e) => e.offsetParent !== null;
      const scope = [...document.querySelectorAll('section[id$="View"]')].find(s => !s.hidden) || document.body;
      return {
        view: scope.id || 'body',
        h: document.body.scrollHeight,
        buttons: [...scope.querySelectorAll('button')].filter(vis)
                   .map(b => b.textContent.trim().replace(/\s+/g, ' ').slice(0, 42)),
        inputs: [...scope.querySelectorAll('input,select,textarea')].filter(vis).length,
        overflow: document.documentElement.scrollWidth > document.documentElement.clientWidth,
      };
    });
    note(`  ${name}  view=${m.view}  page=${m.h}px  controls=${m.buttons.length}btn/${m.inputs}field` +
         (m.overflow ? '  *** HORIZONTAL OVERFLOW' : ''));
    note('     ' + JSON.stringify(m.buttons));
    return m;
  };

  const clickVisible = async (re, label) => {
    const b = page.locator('section:not([hidden]) button', { hasText: re }).first();
    if (await b.count() === 0 || !(await b.isVisible().catch(() => false))) {
      note('  !! could not reach: ' + (label || re)); return false;
    }
    await b.click().catch(() => {}); await page.waitForTimeout(900); return true;
  };

  await page.goto(BASE + '/parent.html', { waitUntil: 'networkidle' });
  await shot('01-login');
  await page.fill('#emailInput', 'anna@example.am');
  await page.fill('#passwordInput', 'password123');
  await page.click('#loginBtn');
  await page.waitForTimeout(1400);
  await shot('02-devices');

  await clickVisible(/Անիի Արեգը/, 'first toy');
  const toy = await shot('03-toy');

  // From the toy view, follow every onward link the parent can see.
  const onward = toy.buttons.filter(b => !/^←/.test(b));
  note('  onward from toy: ' + JSON.stringify(onward));

  for (const label of onward) {
    const back = await visibleView();
    if (!(await clickVisible(new RegExp(label.slice(0, 18).replace(/[.*+?^${}()|[\]\\]/g, '\\$&')), label)))
      continue;
    const v = await visibleView();
    if (v === back) { note('  (no navigation from "' + label + '")'); continue; }
    await shot('04-' + v.replace('View', '') + '-' + label.slice(0, 12).replace(/[^\wա-ֆԱ-Ֆ]+/g, '_'));
    // inside a list view, walk its tabs too
    const tabs = await page.locator('section:not([hidden]) .tabs button').all();
    for (let i = 0; i < tabs.length; i++) {
      if (!(await tabs[i].isVisible().catch(() => false))) continue;
      const t = (await tabs[i].textContent()).trim();
      await tabs[i].click().catch(() => {}); await page.waitForTimeout(800);
      await shot('05-tab-' + t.replace(/\s+/g, '_'));
    }
    await clickVisible(/^←/, 'back');
  }

  // Account view from the header
  await page.click('#accountBtn').catch(() => {});
  await page.waitForTimeout(900);
  await shot('06-account');

  await browser.close();
  fs.writeFileSync(__dirname + '/walk.log', log.join('\n'));
  note('\ndone');
})().catch(e => { console.error('WALK FAILED:', e.message); process.exit(1); });
