#!/usr/bin/env python3
"""Build a single self-contained HTML page for the 70 per-story clip listen test.

Why this exists: the clips shipped 2026-08-16 and this repo's standing rule is
that no audio reaches a child until a human has heard it. The owner reviews
from a phone, and the clips live only on an SD card and inside the backend
image -- there was no way to hear them without a toy in hand. This embeds all
70 as data URIs (~7.2 MB) beside the exact text each one should say, so the
review is: tap, listen, compare, flag.

Output goes to a file; nothing Armenian is ever printed to stdout -- the
Windows console codepage mangles it, a repo-known trap.

    python tools/story-audio/build_clip_listen_page.py [out.html]
"""
import base64
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CLIPS = os.path.join(ROOT, 'backend/src/ArmenianAiToy.Api/story-audio/clips')
STORIES = os.path.join(ROOT, 'backend/src/ArmenianAiToy.Application/Stories/Content')
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    ROOT, 'tools/story-audio/clip-listen-test.html')

# Review order follows the child's journey, not the filename alphabet.
KINDS = [
    ('offer', 'Offer', 'Menu: would you like to hear this one?'),
    ('reoffer', 'Re-offer', 'Menu: we heard this before -- again?'),
    ('intro', 'Intro', 'Title and author, before the story starts'),
    ('summary', 'Summary', 'The takeaway, after the story ends'),
    ('question', 'Question 1', 'First reflection question'),
    ('question1', 'Question 2', 'Second reflection question'),
    ('question2', 'Question 3', 'Third reflection question'),
]

# The reference text MUST come from the same sources the renderer used, never
# from a hand-written reconstruction. The first version of this file rebuilt
# the intro/offer/reoffer strings by hand and got all three subtly wrong
# («Հեքիաթը՝» for «Հեքիաթ՝», a different offer sentence entirely), which sent
# the owner chasing two clips that were in fact correct. A review instrument
# that shows the wrong reference is worse than no instrument.
#   intro         -> tools/ElevenLabsRender/Program.cs (composed there)
#   offer/reoffer -> backend/content/voice-clips/voice-clips.json
#                    _perStoryTemplates, {Title}-substituted
#   the rest      -> the story's own JSON fields
TEMPLATES = os.path.join(ROOT, 'backend/content/voice-clips/voice-clips.json')


def load_templates():
    with open(TEMPLATES, encoding='utf-8') as fh:
        blob = json.load(fh)
    t = blob.get('_perStoryTemplates') or {}
    return t.get('offer') or '', t.get('reoffer') or ''


OFFER_T, REOFFER_T = load_templates()

# Exactly as ElevenLabsRender/Program.cs composes it -- classifier without the
# definite article, title already carrying its own.
INTRO_STORY = 'Հեքիաթ՝'      # Հեքիաթ՝
INTRO_AUTHOR = 'Հեղինակ՝'  # Հեղինակ՝
LQ, RQ, STOP = '«', '»', '։'


def load_story(sid):
    path = os.path.join(STORIES, sid + '.story.json')
    if not os.path.exists(path):
        return {}
    with open(path, encoding='utf-8') as fh:
        return json.load(fh)


def expected_text(kind, s):
    """What this clip is supposed to say -- shown beside the audio so the
    reviewer checks words against sound, not just 'did a sound happen'."""
    questions = s.get('reflectionQuestions') or []
    title = s.get('title', '')
    if kind == 'intro':
        text = INTRO_STORY + ' ' + LQ + title + RQ + STOP
        if s.get('author'):
            text += ' ' + INTRO_AUTHOR + ' ' + s['author'] + STOP
        return text
    if kind == 'summary':
        return s.get('lesson') or s.get('reflectionText') or ''
    if kind == 'question':
        return questions[0] if len(questions) > 0 else ''
    if kind == 'question1':
        return questions[1] if len(questions) > 1 else ''
    if kind == 'question2':
        return questions[2] if len(questions) > 2 else ''
    if kind == 'offer':
        return OFFER_T.replace('{Title}', title)
    if kind == 'reoffer':
        return REOFFER_T.replace('{Title}', title)
    return ''


def collect():
    stories, total = [], 0
    for sid in sorted(os.listdir(CLIPS)):
        folder = os.path.join(CLIPS, sid)
        if not os.path.isdir(folder):
            continue
        story = load_story(sid)
        items = []
        for kind, label, role in KINDS:
            path = os.path.join(folder, kind + '.mp3')
            if not os.path.exists(path):
                continue
            with open(path, 'rb') as fh:
                b64 = base64.b64encode(fh.read()).decode('ascii')
            items.append({
                'kind': kind,
                'label': label,
                'role': role,
                'text': expected_text(kind, story),
                'note': ('This story has no verified author, so the intro names'
                         ' none by design -- an attribution spoken to a child is'
                         ' never guessed.')
                        if kind == 'intro' and not story.get('author') else '',
                'src': 'data:audio/mpeg;base64,' + b64,
            })
            total += 1
        stories.append({
            'id': sid,
            'title': story.get('title') or sid,
            'author': story.get('author'),
            'clips': items,
        })
    return stories, total


CSS = """
:root{
  --ground:#f2f3f7; --card:#ffffff; --edge:#d9dced;
  --text:#171b2c; --dim:#5c6076; --faint:#8b90a4;
  --lapis:#3550a0; --gold:#b8801d; --flag:#b23b32; --ok:#2f7d5c;
  --shadow:0 1px 2px rgba(18,23,43,.06),0 8px 24px -12px rgba(18,23,43,.18);
}
@media (prefers-color-scheme:dark){
  :root{
    --ground:#101423; --card:#181d33; --edge:#2a3050;
    --text:#e8eaf2; --dim:#a2a8c0; --faint:#767d99;
    --lapis:#7d9aec; --gold:#e0a73c; --flag:#e2736a; --ok:#5cc094;
    --shadow:0 1px 2px rgba(0,0,0,.4),0 10px 30px -14px rgba(0,0,0,.7);
  }
}
:root[data-theme="dark"]{
  --ground:#101423; --card:#181d33; --edge:#2a3050;
  --text:#e8eaf2; --dim:#a2a8c0; --faint:#767d99;
  --lapis:#7d9aec; --gold:#e0a73c; --flag:#e2736a; --ok:#5cc094;
  --shadow:0 1px 2px rgba(0,0,0,.4),0 10px 30px -14px rgba(0,0,0,.7);
}
:root[data-theme="light"]{
  --ground:#f2f3f7; --card:#ffffff; --edge:#d9dced;
  --text:#171b2c; --dim:#5c6076; --faint:#8b90a4;
  --lapis:#3550a0; --gold:#b8801d; --flag:#b23b32; --ok:#2f7d5c;
  --shadow:0 1px 2px rgba(18,23,43,.06),0 8px 24px -12px rgba(18,23,43,.18);
}
*{box-sizing:border-box}
body{
  margin:0; background:var(--ground); color:var(--text);
  font-family:system-ui,-apple-system,"Segoe UI","Noto Sans Armenian",sans-serif;
  font-size:16px; line-height:1.5;
  padding-bottom:calc(2rem + env(safe-area-inset-bottom));
}
.wrap{max-width:38rem;margin:0 auto;padding:0 1rem}
header.top{
  position:sticky; top:0; z-index:10; background:var(--ground);
  border-bottom:1px solid var(--edge); padding-top:1.25rem;
}
h1{
  font-family:Sylfaen,"Noto Serif Armenian",Georgia,serif;
  font-size:1.5rem; line-height:1.2; margin:0 0 .15rem; text-wrap:balance;
}
.sub{color:var(--dim); font-size:.85rem; margin:0 0 .9rem}
.bar{height:6px;border-radius:3px;background:var(--edge);overflow:hidden;margin-bottom:.5rem}
.bar>i{display:block;height:100%;background:var(--gold);width:0;transition:width .3s}
.stats{
  display:flex;gap:1rem;font-size:.8rem;color:var(--dim);
  font-variant-numeric:tabular-nums;padding-bottom:.9rem;flex-wrap:wrap
}
.stats b{color:var(--text);font-weight:600}
.stats .flagged b{color:var(--flag)}
.story{
  background:var(--card);border:1px solid var(--edge);border-radius:10px;
  margin:1rem 0;box-shadow:var(--shadow);overflow:hidden
}
.story>summary{
  cursor:pointer;padding:.85rem 1rem;display:flex;gap:.6rem;align-items:baseline;
  list-style:none
}
.story>summary::-webkit-details-marker{display:none}
.story>summary::after{
  content:attr(data-count);margin-left:auto;font-size:.75rem;color:var(--faint);
  font-variant-numeric:tabular-nums;white-space:nowrap
}
.story h2{
  font-family:Sylfaen,"Noto Serif Armenian",Georgia,serif;
  font-size:1.05rem;margin:0;font-weight:600
}
.story .who{font-size:.75rem;color:var(--faint);display:block;margin-top:.1rem}
.clip{border-top:1px solid var(--edge);padding:.75rem 1rem}
.clip.heard{background:color-mix(in srgb,var(--ok) 6%,transparent)}
.clip.flag{background:color-mix(in srgb,var(--flag) 8%,transparent)}
.clip .row{display:flex;gap:.6rem;align-items:center}
.play{
  flex:0 0 auto;width:2.6rem;height:2.6rem;border-radius:50%;border:1px solid var(--edge);
  background:var(--ground);color:var(--text);font-size:1rem;cursor:pointer;
  display:grid;place-items:center
}
.play:hover{border-color:var(--lapis)}
.play[aria-pressed="true"]{background:var(--lapis);border-color:var(--lapis);color:#fff}
.play:focus-visible,.tog:focus-visible,.btn:focus-visible{outline:2px solid var(--lapis);outline-offset:2px}
.meta{flex:1 1 auto;min-width:0}
.kind{
  font-family:ui-monospace,"SF Mono",Menlo,monospace;font-size:.68rem;
  letter-spacing:.08em;text-transform:uppercase;color:var(--lapis)
}
.role{font-size:.75rem;color:var(--faint);display:block}
.say{
  margin:.5rem 0 0;padding:.5rem .65rem;border-left:2px solid var(--gold);
  background:color-mix(in srgb,var(--gold) 7%,transparent);
  font-family:Sylfaen,"Noto Serif Armenian",Georgia,serif;font-size:.95rem
}
.why{margin:.4rem 0 0;font-size:.76rem;color:var(--faint);font-style:italic}
.acts{display:flex;gap:.4rem;margin-top:.55rem}
.tog{
  flex:1;padding:.4rem;border-radius:6px;border:1px solid var(--edge);
  background:transparent;color:var(--dim);font-size:.78rem;cursor:pointer;
  font-family:inherit
}
.tog[aria-pressed="true"]{font-weight:600}
.tog.h[aria-pressed="true"]{border-color:var(--ok);color:var(--ok)}
.tog.f[aria-pressed="true"]{border-color:var(--flag);color:var(--flag)}
.note{
  width:100%;margin-top:.45rem;padding:.45rem .55rem;border-radius:6px;
  border:1px solid var(--edge);background:var(--ground);color:var(--text);
  font-family:inherit;font-size:.85rem;display:none
}
.clip.flag .note{display:block}
footer{margin:1.5rem 0;display:flex;gap:.5rem;flex-wrap:wrap}
.btn{
  padding:.6rem .9rem;border-radius:8px;border:1px solid var(--edge);
  background:var(--card);color:var(--text);font-size:.85rem;cursor:pointer;
  font-family:inherit
}
.btn.primary{background:var(--lapis);border-color:var(--lapis);color:#fff;font-weight:600}
#report{
  width:100%;margin-top:.75rem;min-height:9rem;padding:.6rem;border-radius:8px;
  border:1px solid var(--edge);background:var(--card);color:var(--text);
  font-family:ui-monospace,"SF Mono",Menlo,monospace;font-size:.78rem;display:none
}
.hint{font-size:.78rem;color:var(--faint);margin:.4rem 0 0}
@media (prefers-reduced-motion:reduce){*{transition:none!important}}
"""

JS = r"""
const DATA = __PAYLOAD__;
const KEY = 'areg-clip-listen-v1';
let state = {};
try { state = JSON.parse(localStorage.getItem(KEY) || '{}'); } catch (e) { state = {}; }
const save = () => { try { localStorage.setItem(KEY, JSON.stringify(state)); } catch (e) {} };
const idOf = (s, c) => s + '/' + c;
let current = null;

function stats() {
  let heard = 0, flagged = 0, total = 0;
  DATA.forEach(s => s.clips.forEach(c => {
    total++;
    const st = state[idOf(s.id, c.kind)] || {};
    if (st.heard) heard++;
    if (st.flag) flagged++;
  }));
  return { heard, flagged, total };
}

function paintStats() {
  const r = stats();
  document.querySelector('.bar > i').style.width = (r.total ? r.heard / r.total * 100 : 0) + '%';
  document.getElementById('nHeard').textContent = r.heard;
  document.getElementById('nTotal').textContent = r.total;
  document.getElementById('nFlag').textContent = r.flagged;
  DATA.forEach(s => {
    const n = s.clips.filter(c => (state[idOf(s.id, c.kind)] || {}).heard).length;
    const el = document.querySelector('summary[data-story="' + s.id + '"]');
    if (el) el.dataset.count = n + '/' + s.clips.length;
  });
}

function stopCurrent() {
  if (!current) return;
  current.audio.pause();
  current.btn.setAttribute('aria-pressed', 'false');
  current.btn.textContent = '▶';
  current = null;
}

function play(btn, story, clip) {
  if (current && current.btn === btn) { stopCurrent(); return; }
  stopCurrent();
  const audio = new Audio(clip.src);
  btn.setAttribute('aria-pressed', 'true');
  btn.textContent = '■';
  current = { audio: audio, btn: btn };
  audio.addEventListener('ended', function () {
    const id = idOf(story.id, clip.kind);
    state[id] = Object.assign({}, state[id], { heard: true });
    save();
    const row = document.getElementById('c-' + id.replace('/', '-'));
    if (row) {
      row.classList.add('heard');
      const h = row.querySelector('.tog.h');
      if (h) h.setAttribute('aria-pressed', 'true');
    }
    paintStats();
    stopCurrent();
  });
  audio.play().catch(function () { stopCurrent(); });
}

function render() {
  const root = document.getElementById('list');
  DATA.forEach(function (s, si) {
    const d = document.createElement('details');
    d.className = 'story';
    if (si === 0) d.open = true;
    const sum = document.createElement('summary');
    sum.dataset.story = s.id;
    const box = document.createElement('div');
    const h2 = document.createElement('h2');
    h2.textContent = s.title;
    const who = document.createElement('span');
    who.className = 'who';
    who.textContent = s.author ? s.author : s.id;
    box.appendChild(h2); box.appendChild(who);
    sum.appendChild(box);
    d.appendChild(sum);

    s.clips.forEach(function (c) {
      const id = idOf(s.id, c.kind);
      const st = state[id] || {};
      const row = document.createElement('div');
      row.className = 'clip' + (st.heard ? ' heard' : '') + (st.flag ? ' flag' : '');
      row.id = 'c-' + id.replace('/', '-');

      const btn = document.createElement('button');
      btn.className = 'play';
      btn.textContent = '▶';
      btn.setAttribute('aria-pressed', 'false');
      btn.setAttribute('aria-label', 'Play ' + c.label + ' of ' + s.title);
      btn.addEventListener('click', function () { play(btn, s, c); });

      const meta = document.createElement('div');
      meta.className = 'meta';
      const kind = document.createElement('span');
      kind.className = 'kind';
      kind.textContent = c.label;
      const role = document.createElement('span');
      role.className = 'role';
      role.textContent = c.role;
      meta.appendChild(kind); meta.appendChild(role);

      const rowEl = document.createElement('div');
      rowEl.className = 'row';
      rowEl.appendChild(btn); rowEl.appendChild(meta);
      row.appendChild(rowEl);

      if (c.text) {
        const p = document.createElement('p');
        p.className = 'say';
        p.textContent = c.text;
        row.appendChild(p);
      }
      if (c.note) {
        const n = document.createElement('p');
        n.className = 'why';
        n.textContent = c.note;
        row.appendChild(n);
      }

      const acts = document.createElement('div');
      acts.className = 'acts';
      const hb = document.createElement('button');
      hb.className = 'tog h';
      hb.textContent = 'Sounds right';
      hb.setAttribute('aria-pressed', st.heard ? 'true' : 'false');
      const fb = document.createElement('button');
      fb.className = 'tog f';
      fb.textContent = 'Something is wrong';
      fb.setAttribute('aria-pressed', st.flag ? 'true' : 'false');
      const note = document.createElement('textarea');
      note.className = 'note';
      note.rows = 2;
      note.placeholder = 'What is wrong with it?';
      note.value = st.note || '';

      hb.addEventListener('click', function () {
        const on = hb.getAttribute('aria-pressed') !== 'true';
        hb.setAttribute('aria-pressed', on ? 'true' : 'false');
        state[id] = Object.assign({}, state[id], { heard: on });
        row.classList.toggle('heard', on);
        save(); paintStats();
      });
      fb.addEventListener('click', function () {
        const on = fb.getAttribute('aria-pressed') !== 'true';
        fb.setAttribute('aria-pressed', on ? 'true' : 'false');
        state[id] = Object.assign({}, state[id], { flag: on });
        row.classList.toggle('flag', on);
        save(); paintStats();
        if (on) note.focus();
      });
      note.addEventListener('input', function () {
        state[id] = Object.assign({}, state[id], { note: note.value });
        save();
      });

      acts.appendChild(hb); acts.appendChild(fb);
      row.appendChild(acts); row.appendChild(note);
      d.appendChild(row);
    });
    root.appendChild(d);
  });
  paintStats();
}

function report() {
  const r = stats();
  const lines = ['Clip listen test - ' + r.heard + '/' + r.total + ' heard, ' + r.flagged + ' flagged', ''];
  DATA.forEach(s => s.clips.forEach(c => {
    const st = state[idOf(s.id, c.kind)] || {};
    if (st.flag) lines.push('FLAG  ' + s.id + ' / ' + c.kind + (st.note ? '  -- ' + st.note : ''));
  }));
  if (r.flagged === 0) lines.push('No clips flagged.');
  const notHeard = [];
  DATA.forEach(s => s.clips.forEach(c => {
    if (!(state[idOf(s.id, c.kind)] || {}).heard) notHeard.push(s.id + '/' + c.kind);
  }));
  if (notHeard.length) lines.push('', 'Not yet heard (' + notHeard.length + '): ' + notHeard.join(', '));
  const out = lines.join('\n');
  const ta = document.getElementById('report');
  ta.style.display = 'block';
  ta.value = out;
  ta.select();
  if (navigator.clipboard) navigator.clipboard.writeText(out).catch(function () {});
}

document.getElementById('mkReport').addEventListener('click', report);
document.getElementById('reset').addEventListener('click', function () {
  if (!confirm('Clear every mark and note?')) return;
  state = {}; save();
  document.getElementById('list').innerHTML = '';
  render();
});
render();
"""


def main():
    stories, total = collect()
    payload = json.dumps(stories, ensure_ascii=False)
    page = (
        '<title>Areg — clip listen test</title>\n'
        '<meta name="viewport" content="width=device-width,initial-scale=1">\n'
        '<style>' + CSS + '</style>\n'
        '<header class="top"><div class="wrap">\n'
        '  <h1>The clips a child hears</h1>\n'
        '  <p class="sub">' + str(total) + ' clips across ' + str(len(stories)) +
        ' stories. Tap play, compare against the words underneath, mark it.</p>\n'
        '  <p class="sub" style="color:var(--flag)">Corrected 2026-09-03: the reference'
        ' text under <b>intro</b>, <b>offer</b> and <b>re-offer</b> was mine and was'
        ' wrong — it now comes from the same files the renderer used. Your marks'
        ' are kept; those three rows per story are worth a second look.</p>\n'
        '  <div class="bar"><i></i></div>\n'
        '  <div class="stats">\n'
        '    <span><b id="nHeard">0</b> of <b id="nTotal">0</b> heard</span>\n'
        '    <span class="flagged"><b id="nFlag">0</b> flagged</span>\n'
        '    <span>progress saves itself</span>\n'
        '  </div>\n'
        '</div></header>\n'
        '<main class="wrap"><div id="list"></div>\n'
        '  <footer>\n'
        '    <button class="btn primary" id="mkReport">Copy report</button>\n'
        '    <button class="btn" id="reset">Start over</button>\n'
        '  </footer>\n'
        '  <textarea id="report" readonly aria-label="Report to paste back"></textarea>\n'
        '  <p class="hint">A clip marks itself heard when it plays to the end. The report'
        ' lists everything flagged plus anything still unheard — paste it back into'
        ' the session.</p>\n'
        '</main>\n'
        '<script>' + JS.replace('__PAYLOAD__', payload) + '</script>\n'
    )
    with open(OUT, 'w', encoding='utf-8') as fh:
        fh.write(page)
    print('wrote ' + OUT)
    print('%d clips, %d stories, %.2f MB'
          % (total, len(stories), os.path.getsize(OUT) / 1e6))


if __name__ == '__main__':
    main()
