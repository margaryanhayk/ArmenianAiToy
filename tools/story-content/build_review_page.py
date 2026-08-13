#!/usr/bin/env python3
"""Turn the reviewable Armenian in backend/content/ into one page for a phone.

WHY
---
106 pieces of Armenian sit in backend/content/ waiting for the owner to read
them — 90 game lines, 10 alternate story endings, 6 episodes of the Ծիվիկ
serial — and the game lines block the expressive re-render of every game the
toy plays. They were unreadable on a phone: JSON, viewed through GitHub, with
each Armenian line buried among English notes written for an engineer (splice
instructions, honesty rules, TTS watch-words). He asked whether he could read
them on his phone; the honest answer was no.

So this strips everything that is a note to me, keeps every Armenian character
byte-identical, and emits ONE self-contained HTML file: one card per line, two
taps (լավ է / փոխել), a box for the replacement, state kept in the phone's own
storage so a ten-minute pass survives closing the tab, and a button that copies
only what he marked as text he can paste back into the chat.

It reads the repo and writes an HTML file. It NEVER writes to backend/content —
corrections come back as text and are applied by hand, through a commit, so
nothing changes without passing through him twice.

RE-RUN IT after any edit to the three source files, then republish the artifact
at the SAME url so his marks keep their meaning (the keys are storyId/clipId,
not positions, so inserting a line does not shift anyone's answers).

USAGE
    python3 tools/story-content/build_review_page.py
"""
import json, html, re
from pathlib import Path

REPO = Path("/home/user/ArmenianAiToy")
import os, sys
OUT = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("areg-texts.html")

games = json.loads((REPO/"backend/content/offline-games/game-clips.json").read_text(encoding="utf-8"))
ends  = json.loads((REPO/"backend/content/variant-endings/variant-endings.json").read_text(encoding="utf-8"))
serial= json.loads((REPO/"backend/content/serial-hero/tsivik-series.json").read_text(encoding="utf-8"))
titles= {}
for f in (REPO/"backend/src/ArmenianAiToy.Application/Stories/Content").glob("*.story.json"):
    s=json.loads(f.read_text(encoding="utf-8")); titles[s["id"]]=s.get("title", s["id"])

GAME_HEAD = {
 "mind-reader":     ("Ես գուշակում եմ", "Երեխան մտքում մի կենդանի է ընտրում։ Արեգը հարցեր է տալիս ու գուշակում։"),
 "who-first":       ("Ո՞վ առաջինը", "Երկու խաղացող՝ կանաչ և կարմիր։ Ով շուտ սեղմի կոճակը, նա է հաղթում։"),
 "sound-detective": ("Գուշակի՛ր ձայնը", "Կատրինը կամ Վարդանը կենդանու ձայն է անում։ Երեխան կոճակով ընտրում է, թե ով էր։"),
 "button-simon":    ("Կրկնի՛ր իմ հետևից", "Արեգը ձայների շարք է նվագում։ Երեխան նույնը կրկնում է կոճակներով։"),
 "story-pauses":    ("Հեքիաթի ընդմիջում", "Հեքիաթը կանգ է առնում, Արեգը հրավիրում է բղավելու, հետո շարունակվում է։ Խոսափողը ՉԻ լսում։"),
}
# When each line is heard. One caption per family of ids — the id itself is an
# engineering handle and says nothing to a reader.
def when(game, cid):
    if cid == "intro":   return "Խաղի սկզբում"
    if game == "mind-reader":
        if cid.startswith("q-"): return "Հարց՝ գուշակելու ճանապարհին"
        if cid.startswith("g-"): return "Արեգի գուշակը"
        return {"win":"Երբ Արեգը ճիշտ է գուշակել","lose":"Երբ Արեգը չի գուշակել",
                "replay":"Խաղի վերջում՝ նորից խաղալու հրավեր"}.get(cid,"")
    if game == "who-first":
        if cid.startswith("go"):        return "Ռաունդի մեկնարկ"
        if cid.startswith("win-green"): return "Երբ կանաչն է շուտ սեղմել"
        if cid.startswith("win-red"):   return "Երբ կարմիրն է շուտ սեղմել"
        if cid.startswith("between"):   return "Ռաունդների արանքում"
        return {"end-both":"Խաղի ավարտին","close":"Հրաժեշտ"}.get(cid,"")
    if game == "sound-detective":
        if cid.endswith("-sound"): return "Կենդանու ձայնը"
        if cid.endswith("-ask"):   return "Արեգի հարցը՝ ո՞վ էր"
    if game == "button-simon":
        if cid.startswith("level-up"): return "Երբ երեխան ճիշտ է կրկնել"
        return {"your-turn":"Երբ հերթը երեխայինն է","miss":"Երբ սխալվել է",
                "best":"Նոր անձնական ռեկորդ","done":"Խաղի ավարտին"}.get(cid,"")
    if game == "story-pauses":
        if cid.startswith("shout"):  return "Հեքիաթը կանգ է առնում"
        if cid.startswith("resume"): return "Հեքիաթը շարունակվում է"
    return ""

SPEAKER = {"katrin":"Կատրին","vardan":"Վարդան"}

items = []                       # (key, section, group, when, speaker, text)
for gid, head in GAME_HEAD.items():
    for c in games[gid]["clips"]:
        items.append({"key": f"{gid}/{c['id']}", "section": "games", "group": gid,
                      "when": when(gid, c["id"]),
                      "who": SPEAKER.get(c.get("speaker","areg"), ""),
                      "text": c["text"]})
for e in ends["endings"]:
    items.append({"key": f"ending/{e['storyId']}", "section": "endings",
                  "group": e["storyId"], "when": "Փոխարինող ավարտ՝ երկրորդ լսելիս",
                  "who": "", "text": e["endingText"], "after": e.get("afterLine","")})
for ep in serial["episodes"]:
    items.append({"key": f"tsivik/{ep['episode']}", "section": "serial",
                  "group": f"episode-{ep['episode']}", "when": "",
                  "who": "", "text": ep["text"], "eptitle": ep.get("title","")})

# ---- verify nothing was dropped or altered -------------------------------
assert sum(1 for i in items if i["section"]=="games") == 90, "game clips"
assert sum(1 for i in items if i["section"]=="endings") == 10, "endings"
assert sum(1 for i in items if i["section"]=="serial") == 6, "episodes"
raw = (REPO/"backend/content/offline-games/game-clips.json").read_text(encoding="utf-8")
for i in items:
    if i["section"] == "games":
        assert json.dumps(i["text"], ensure_ascii=False)[1:-1] in raw, i["key"]

def esc(t): return html.escape(t, quote=False)

cards = []
seen_group = None
n = 0
SECTION_HEAD = {
 "games":   ("Խաղերի տեքստերը", "90 տող։ Սրանք են, որ սպասում են՝ ձայնագրելուց առաջ։"),
 "endings": ("Փոխարինող ավարտներ", "10 հեքիաթ։ Երեխան երկրորդ անգամ լսելիս այլ ավարտ է լսում։"),
 "serial":  ("Ծիվիկի շարքը", "6 մաս։ Օրական մեկ նոր մաս։"),
}
last_section = None
for it in items:
    n += 1
    if it["section"] != last_section:
        t, sub = SECTION_HEAD[it["section"]]
        cards.append(f'<h2 class="sec">{esc(t)}<span>{esc(sub)}</span></h2>')
        last_section = it["section"]; seen_group = None
    if it["group"] != seen_group:
        if it["section"] == "games":
            t, sub = GAME_HEAD[it["group"]]
            cards.append(f'<h3 class="grp">«{esc(t)}»<span>{esc(sub)}</span></h3>')
        elif it["section"] == "endings":
            cards.append(f'<h3 class="grp">{esc(titles.get(it["group"], it["group"]))}</h3>')
        else:
            cards.append(f'<h3 class="grp">{esc(it.get("eptitle",""))}'
                         f'<span>{esc(it["group"].replace("episode-","Մաս "))}</span></h3>')
        seen_group = it["group"]
    meta = []
    if it.get("when"): meta.append(esc(it["when"]))
    if it.get("who"):  meta.append(esc(it["who"]) + "՝ ձայնը")
    if it.get("after"): meta.append("շարունակվում է՝ «" + esc(it["after"]) + "»")
    cards.append(
      f'<article class="card" data-k="{esc(it["key"])}" data-n="{n}">'
      f'<div class="meta"><b>{n}</b>{" · " + " · ".join(meta) if meta else ""}</div>'
      f'<p class="hy">{esc(it["text"])}</p>'
      f'<div class="btns"><button class="ok" type="button">լավ է</button>'
      f'<button class="no" type="button">փոխել</button></div>'
      f'<textarea class="fix" rows="3" placeholder="Ի՞նչ փոխել։ Կարող ես նոր տարբերակը գրել։"></textarea>'
      f'</article>')

CSS = """
:root{
  /* The parent dashboard's Armenian manuscript palette, reused rather than
     reinvented — this page shows the same product's words. */
  --ground:#F7F1E3; --surface:#FFFDF8; --card:#FBF6EC;
  --ink:#241E12; --ink-2:#3E3420; --muted:#6A5B3E; --hint:#7A6230;
  --line:#E2D6BC; --line-strong:#DCC48A;
  --brand:#25417D; --brand-ink:#FFFDF8;
  --ok-bg:#E4EFE1; --ok-line:#A9C4A2; --ok-ink:#2C5233;
  --no-bg:#F8E9CE; --no-line:#DCC48A; --no-ink:#7A5A1E;
  --shadow:0 1px 2px rgba(36,30,18,.06), 0 8px 24px rgba(36,30,18,.05);
}
@media (prefers-color-scheme: dark){
  :root:not([data-theme="light"]){
    --ground:#14120C; --surface:#1B1810; --card:#201C13;
    --ink:#F2EAD6; --ink-2:#DCD1B7; --muted:#A99A7C; --hint:#C0AE86;
    --line:#332C1E; --line-strong:#4A3F2A;
    --brand:#9DB6E4; --brand-ink:#14120C;
    --ok-bg:#1E2A1D; --ok-line:#3F5C3B; --ok-ink:#A8CBA0;
    --no-bg:#2C2313; --no-line:#5A4726; --no-ink:#E0C287;
    --shadow:0 1px 2px rgba(0,0,0,.4), 0 8px 24px rgba(0,0,0,.3);
  }
}
:root[data-theme="dark"]{
  --ground:#14120C; --surface:#1B1810; --card:#201C13;
  --ink:#F2EAD6; --ink-2:#DCD1B7; --muted:#A99A7C; --hint:#C0AE86;
  --line:#332C1E; --line-strong:#4A3F2A;
  --brand:#9DB6E4; --brand-ink:#14120C;
  --ok-bg:#1E2A1D; --ok-line:#3F5C3B; --ok-ink:#A8CBA0;
  --no-bg:#2C2313; --no-line:#5A4726; --no-ink:#E0C287;
  --shadow:0 1px 2px rgba(0,0,0,.4), 0 8px 24px rgba(0,0,0,.3);
}
*{box-sizing:border-box}
body{
  margin:0; background:var(--ground); color:var(--ink);
  font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Noto Sans Armenian',sans-serif;
  font-size:17px; line-height:1.5;
  padding-bottom:calc(84px + env(safe-area-inset-bottom));
  -webkit-text-size-adjust:100%;
}
.wrap{max-width:34rem;margin:0 auto;padding:0 16px}
header.top{
  position:sticky; top:0; z-index:20; background:var(--surface);
  border-bottom:1px solid var(--line); box-shadow:var(--shadow);
}
.top .wrap{display:flex;align-items:center;gap:12px;padding-top:12px;padding-bottom:12px}
.top h1{font-size:17px;line-height:1.2;margin:0;font-weight:650;letter-spacing:-.01em}
.top h1 small{display:block;font-weight:450;color:var(--muted);font-size:13px;letter-spacing:0}
.count{margin-left:auto;font-variant-numeric:tabular-nums;font-size:13px;color:var(--muted);white-space:nowrap;text-align:right}
.count b{display:block;font-size:19px;color:var(--ink);font-weight:650}
.bar{height:3px;background:var(--line)}
.bar i{display:block;height:100%;width:0;background:var(--brand);transition:width .25s ease}
@media (prefers-reduced-motion: reduce){.bar i{transition:none}}
h2.sec{
  margin:40px 0 4px; font-size:24px; line-height:1.25; font-weight:700;
  letter-spacing:-.02em; text-wrap:balance;
}
h2.sec span{display:block;font-size:14px;font-weight:450;color:var(--muted);letter-spacing:0;margin-top:6px}
h3.grp{
  margin:28px 0 10px; font-size:17px; font-weight:650; color:var(--ink-2);
  padding-bottom:8px; border-bottom:1px solid var(--line-strong); text-wrap:balance;
}
h3.grp span{display:block;font-size:14px;font-weight:450;color:var(--muted);margin-top:5px;line-height:1.45}
.card{
  background:var(--card); border:1px solid var(--line); border-left:4px solid var(--line);
  border-radius:10px; padding:14px 15px; margin:10px 0; box-shadow:var(--shadow);
}
.card[data-s="ok"]{border-left-color:var(--ok-line);background:var(--ok-bg)}
.card[data-s="no"]{border-left-color:var(--no-line);background:var(--no-bg)}
.meta{font-size:12.5px;color:var(--hint);margin-bottom:8px;font-variant-numeric:tabular-nums}
.meta b{color:var(--muted);font-weight:650}
p.hy{margin:0 0 12px;font-size:21px;line-height:1.62;color:var(--ink);text-wrap:pretty}
.btns{display:flex;gap:8px}
.btns button{
  flex:1; font:inherit; font-size:16px; font-weight:600; padding:11px 8px;
  border-radius:8px; border:1px solid var(--line-strong); background:var(--surface);
  color:var(--ink-2); cursor:pointer; -webkit-tap-highlight-color:transparent;
}
.btns button:focus-visible{outline:3px solid var(--brand);outline-offset:2px}
.card[data-s="ok"] .ok{background:var(--ok-ink);border-color:var(--ok-ink);color:var(--card)}
.card[data-s="no"] .no{background:var(--no-ink);border-color:var(--no-ink);color:var(--card)}
textarea.fix{
  display:none; width:100%; margin-top:10px; font:inherit; font-size:17px;
  padding:10px; border-radius:8px; border:1px solid var(--line-strong);
  background:var(--surface); color:var(--ink); resize:vertical;
}
.card[data-s="no"] textarea.fix{display:block}
textarea.fix:focus-visible{outline:3px solid var(--brand);outline-offset:1px}
footer.dock{
  position:fixed; left:0; right:0; bottom:0; z-index:20; background:var(--surface);
  border-top:1px solid var(--line); box-shadow:0 -4px 20px rgba(36,30,18,.07);
  padding-bottom:env(safe-area-inset-bottom);
}
.dock .wrap{display:flex;gap:10px;padding-top:12px;padding-bottom:12px}
.dock button{
  font:inherit;font-size:16px;font-weight:650;padding:13px 12px;border-radius:9px;
  border:1px solid var(--brand);background:var(--brand);color:var(--brand-ink);cursor:pointer;flex:2;
}
.dock button.ghost{background:transparent;color:var(--brand);flex:1}
.dock button:focus-visible{outline:3px solid var(--brand);outline-offset:2px}
.said{padding:10px 0 0;font-size:13.5px;color:var(--muted);min-height:1.2em}
.intro{margin:22px 0 0;color:var(--muted);font-size:15px;line-height:1.6}
"""

JS = """
var KEY='areg-text-review-v1';
var store={};
try{store=JSON.parse(localStorage.getItem(KEY)||'{}')}catch(e){store={}}
var cards=[].slice.call(document.querySelectorAll('.card'));
function save(){try{localStorage.setItem(KEY,JSON.stringify(store))}catch(e){}}
function tally(){
  var done=0,changed=0;
  cards.forEach(function(c){var s=store[c.dataset.k];if(s&&s.s){done++;if(s.s==='no')changed++}});
  document.getElementById('done').textContent=done;
  document.getElementById('changed').textContent=changed?changed+' փոխելու':'';
  document.getElementById('fill').style.width=(done/cards.length*100)+'%';
}
function paint(c){
  var s=store[c.dataset.k]||{};
  if(s.s){c.dataset.s=s.s}else{c.removeAttribute('data-s')}
  var t=c.querySelector('.fix'); if(s.fix!=null)t.value=s.fix;
}
cards.forEach(function(c){
  paint(c);
  c.querySelector('.ok').addEventListener('click',function(){
    var s=store[c.dataset.k]||{}; s.s=(s.s==='ok')?null:'ok'; store[c.dataset.k]=s; paint(c);save();tally();});
  c.querySelector('.no').addEventListener('click',function(){
    var s=store[c.dataset.k]||{}; s.s=(s.s==='no')?null:'no'; store[c.dataset.k]=s; paint(c);save();tally();
    if(s.s==='no'){var t=c.querySelector('.fix');t.focus({preventScroll:true})}});
  c.querySelector('.fix').addEventListener('input',function(e){
    var s=store[c.dataset.k]||{}; s.fix=e.target.value; store[c.dataset.k]=s; save();});
});
tally();
document.getElementById('jump').addEventListener('click',function(){
  var next=cards.filter(function(c){var s=store[c.dataset.k];return !(s&&s.s)})[0];
  if(next){next.scrollIntoView({block:'center',behavior:'smooth'});say('')}
  else say('Ամեն տող նայված է։');
});
function say(m){document.getElementById('said').textContent=m}
document.getElementById('copy').addEventListener('click',function(){
  var out=[];
  cards.forEach(function(c){
    var s=store[c.dataset.k]; if(!s||s.s!=='no')return;
    out.push('#'+c.dataset.n+'  ['+c.dataset.k+']');
    out.push('  հին՝ '+c.querySelector('.hy').textContent.trim());
    out.push('  նոր՝ '+((s.fix||'').trim()||'(չգրված)'));
    out.push('');
  });
  if(!out.length){say('Ոչ մի տող փոխելու նշված չէ։');return}
  var text='Areg — փոխելու տողերը ('+((out.length/4)|0)+'):\n\n'+out.join('\n');
  var done=function(){say('Պատճենվեց։ Հիմա փակցրու չաթում։')};
  if(navigator.clipboard&&navigator.clipboard.writeText){
    navigator.clipboard.writeText(text).then(done,function(){fallback(text,done)});
  }else{fallback(text,done)}
});
function fallback(text,done){
  var t=document.createElement('textarea');t.value=text;
  t.style.cssText='position:fixed;top:0;left:0;opacity:0';
  document.body.appendChild(t);t.select();
  try{document.execCommand('copy');done()}catch(e){say('Չստացվեց պատճենել։')}
  document.body.removeChild(t);
}
"""

HTML = ('<title>Արեգի տեքստերը</title>\n<style>' + CSS + '</style>\n'
  '<header class="top"><div class="wrap">'
  '<h1>Արեգի տեքստերը<small>Կարդա՛ և նշի՛ր</small></h1>'
  '<div class="count"><b><span id="done">0</span>/' + str(n) + '</b>'
  '<span id="changed"></span></div></div>'
  '<div class="bar"><i id="fill"></i></div></header>\n'
  '<main class="wrap">'
  '<p class="intro">Ամեն տողի տակ երկու կոճակ կա։ Նշածդ պահվում է հեռախոսում, '
  'կարող ես փակել ու հետո շարունակել։ Վերջում սեղմի՛ր «Պատճենել փոփոխությունները» '
  'և փակցրու չաթում։</p>\n'
  + "\n".join(cards) +
  '</main>\n'
  '<footer class="dock"><div class="wrap">'
  '<button id="copy" type="button">Պատճենել փոփոխությունները</button>'
  '<button id="jump" class="ghost" type="button">Հաջորդը</button>'
  '</div><div class="wrap"><div class="said" id="said" role="status" aria-live="polite"></div></div></footer>\n'
  '<script>' + JS + '</script>\n')

OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text(HTML, encoding="utf-8")
print("items:", n, "->", OUT, OUT.stat().st_size, "bytes")

