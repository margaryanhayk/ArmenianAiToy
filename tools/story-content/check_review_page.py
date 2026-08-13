#!/usr/bin/env python3
"""Press the buttons. The check that was missing when the page shipped inert.

`node --check` proves the script PARSES. It does not prove a tap does anything,
and the page that was handed to the owner failed at exactly that level: the
script had a syntax error, no listener was ever attached, and every button was
dead. Parsing is not working.

So this loads the real page in headless Chromium and drives it: marks a card,
opens the change box, types into it, RELOADS with the same browser profile to
prove the mark survived, and builds the copy-report to prove it contains what
was typed. Anything that fails exits non-zero.

No Playwright package is installed, so this uses Chromium directly: a test
script is appended to a copy of the page, and the verdict is read back out of
the DOM with --dump-dom. Two runs share one profile directory, which is what
makes the reload a real reload.

USAGE
    python3 tools/story-content/check_review_page.py <page.html>
"""
import re
import subprocess
import sys
import tempfile
from pathlib import Path

CHROME = "/opt/pw-browsers/chromium-1194/chrome-linux/chrome"


def run(page: Path, script: str, profile: Path) -> str:
    html = page.read_text(encoding="utf-8")
    probe = page.with_name("_probe.html")
    probe.write_text(html + "\n<script>\n" + script + "\n</script>\n", encoding="utf-8")
    r = subprocess.run(
        [CHROME, "--headless", "--disable-gpu", "--no-sandbox",
         f"--user-data-dir={profile}", "--virtual-time-budget=4000",
         "--dump-dom", probe.as_uri()],
        capture_output=True, text=True, timeout=120)
    probe.unlink(missing_ok=True)
    m = re.search(r'id="verdict"[^>]*>(.*?)</', r.stdout, re.S)
    if not m:
        raise SystemExit("no verdict in the DOM — the page did not run.\n"
                         + r.stderr[-800:])
    return m.group(1)


FIRST = """
var out = [];
function want(name, got, expect) {
  out.push((got === expect ? 'ok   ' : 'FAIL ') + name +
           (got === expect ? '' : ' (got ' + got + ', wanted ' + expect + ')'));
}
var cards = document.querySelectorAll('.card');
var counter = document.getElementById('done');

want('starts at zero', counter.textContent, '0');
cards[0].querySelector('.ok').click();
want('marking one counts one', counter.textContent, '1');
want('the card shows it', cards[0].getAttribute('data-s'), 'ok');
cards[0].querySelector('.ok').click();
want('tapping again unmarks', counter.textContent, '0');
cards[0].querySelector('.ok').click();

cards[1].querySelector('.no').click();
want('two marked', counter.textContent, '2');
var box = cards[1].querySelector('.fix');
want('the change box is shown', getComputedStyle(box).display, 'block');
box.value = 'ՓՈՐՁ';
box.dispatchEvent(new Event('input'));

var saved = JSON.parse(localStorage.getItem('areg-text-review-v1') || '{}');
want('it was saved', saved[cards[1].dataset.k].fix, 'ՓՈՐՁ');

var v = document.createElement('div');
v.id = 'verdict';
v.textContent = out.join(' | ');
document.body.appendChild(v);
"""

SECOND = """
var out = [];
function want(name, got, expect) {
  out.push((got === expect ? 'ok   ' : 'FAIL ') + name +
           (got === expect ? '' : ' (got ' + got + ', wanted ' + expect + ')'));
}
var cards = document.querySelectorAll('.card');
want('the marks survived a reload', document.getElementById('done').textContent, '2');
want('the typed text survived', cards[1].querySelector('.fix').value, 'ՓՈՐՁ');
want('the card is still painted', cards[1].getAttribute('data-s'), 'no');

var report = buildReport();
want('the report exists', report === null, false);
want('it carries what was typed', report.indexOf('ՓՈՐՁ') > -1, true);
want('it names the line', report.indexOf(cards[1].dataset.k) > -1, true);
want('it leaves the approved line out', report.indexOf(cards[0].dataset.k) > -1, false);

document.getElementById('copy').click();
var v = document.createElement('div');
v.id = 'verdict';
v.textContent = out.join(' | ');
document.body.appendChild(v);
"""


def main() -> int:
    page = Path(sys.argv[1] if len(sys.argv) > 1 else "areg-texts.html")
    if not Path(CHROME).exists():
        print(f"chromium not at {CHROME}", file=sys.stderr)
        return 1
    with tempfile.TemporaryDirectory() as profile:
        lines = run(page, FIRST, Path(profile)).split(" | ")
        lines += run(page, SECOND, Path(profile)).split(" | ")
    for line in lines:
        print("  " + line)
    bad = [x for x in lines if x.startswith("FAIL")]
    print("PASS" if not bad else f"FAIL — {len(bad)} of {len(lines)}")
    return 0 if not bad else 1


if __name__ == "__main__":
    sys.exit(main())
