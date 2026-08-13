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

WHY THE CSS AND JS ARE SEPARATE FILES
-------------------------------------
They used to be Python strings inside this file, assembled through a nested
`.replace()`. `\\n` had to survive two layers of quoting to reach the page as
the two characters `\\` `n`; it survived one, and landed as a real line break
inside a single-quoted JS string. That is a syntax error, so the ENTIRE script
failed to parse, no button got a listener, and the page was published inert.
The owner tapped it and nothing happened.

So `review_page.css` and `review_page.js` are read verbatim — no escape crosses
a quoting layer, because there are no layers — and the emitted script is handed
to `node --check` before anything is written. A page whose script cannot parse
is not a page with a bug; it is a blank sheet with text on it.

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
    if cid.startswith("kid-"): return NEW_WHEN.get(game, "")
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
NEW_WHEN = {"who-first":"Ռաունդների արանքում","mind-reader":"Գուշակից հետո",
            "button-simon":"Ռաունդից հետո"}

items = []                       # (key, section, group, when, speaker, text)
# Lines the owner has not read yet go FIRST and in their own section. Burying
# twelve new lines among ninety he has already approved is how they get missed.
for gid, head in GAME_HEAD.items():
    for c in games[gid]["clips"]:
        if not c.get("new"):
            continue
        items.append({"key": f"{gid}/{c['id']}", "section": "new", "group": gid,
                      "when": when(gid, c["id"]),
                      "who": SPEAKER.get(c.get("speaker","areg"), ""),
                      "text": c["text"]})
for gid, head in GAME_HEAD.items():
    for c in games[gid]["clips"]:
        if c.get("new"):
            continue
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
# 102 = the 90 originals plus the twelve Katrin/Vardan lines the owner
# approved on 2026-08-13. The "new" section is empty now and the code that
# builds it stays, because the next batch of new text arrives the same way.
assert sum(1 for i in items if i["section"]=="games") == 102, "game clips"
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
 "new":     ("Նոր տողեր՝ Կատրին և Վարդան",
             "12 տող։ Կատրինն ու Վարդանը հիմա խաղում են ևս երեք խաղում, ոչ միայն մեկում։ Սրանք դեռ ձայնագրված չեն։"),
 "games":   ("Խաղերի տեքստերը", "90 տող։ Սրանք արդեն հաստատված են։"),
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
        if it["section"] in ("games", "new"):
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

CSS = (Path(__file__).parent / "review_page.css").read_text(encoding="utf-8")

JS = (Path(__file__).parent / "review_page.js").read_text(encoding="utf-8")

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

# The gate that was missing. Parse the script exactly as the browser will see
# it — extracted from the finished HTML, not from the source file — and refuse
# to write anything if it fails.
import shutil, subprocess, tempfile
script = HTML[HTML.rindex("<script>") + 8: HTML.rindex("</script>")]
if shutil.which("node") is None:
    raise SystemExit("node is not on PATH, so the page's script cannot be "
                     "checked. Refusing to write: the last unchecked page "
                     "shipped with a syntax error and every button was dead.")
with tempfile.NamedTemporaryFile("w", suffix=".js", encoding="utf-8", delete=False) as fh:
    fh.write(script)
    probe = fh.name
r = subprocess.run(["node", "--check", probe], capture_output=True, text=True)
Path(probe).unlink(missing_ok=True)
if r.returncode != 0:
    raise SystemExit("the page's script does not parse, so every button would "
                     "be dead. Nothing written.\n" + r.stderr[:800])

OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text(HTML, encoding="utf-8")
print("items:", n, "->", OUT, OUT.stat().st_size, "bytes")

