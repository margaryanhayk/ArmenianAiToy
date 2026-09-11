#!/usr/bin/env python3
"""Verify every speaker map reconstructs its story's text EXACTLY.

The maps say who speaks each stretch of a story so a render can give the wolf
a different voice from the mother. They are only safe if annotating cannot
change the tale: the story texts are adapted Tumanyan and are approved, and a
map that silently dropped a comma would put an unreviewed text in a child's
ear.

So the one hard rule: joining a segment's spans, in order, must reproduce that
segment byte for byte. Anything else fails. No ffmpeg, no dotnet, no network —
this must be runnable on the day it matters.
"""
import json, glob, os, sys, unicodedata

CONTENT = "backend/src/ArmenianAiToy.Application/Stories/Content"
DRAFTS  = "backend/content/story-drafts"
MAPS    = "backend/content/story-voices"
ENDINGS = "backend/content/variant-endings/variant-endings.json"

def seg_texts(story):
    return [s if isinstance(s, str) else s.get("text", "") for s in story["segments"]]

def _load_ending_text(base_id):
    """Returns the endingText for base_id from variant-endings.json, or
    None if no ending is drafted for it."""
    if not os.path.exists(ENDINGS):
        return None
    doc = json.load(open(ENDINGS, encoding="utf-8"))
    for e in doc.get("endings", []):
        if e.get("storyId") == base_id:
            return e.get("endingText")
    return None

def check(path):
    m = json.load(open(path, encoding="utf-8"))
    sid = m["storyId"]
    alt_of = m.get("altOf")

    if alt_of:
        # Variant-ending map: no <sid>.story.json exists (the alt is not a
        # curated story — story_select always tracks the BASE id for
        # rotation/reflection, per ContentSyncStoryOptions.AltOf). Validate
        # instead against [base story segments] + [the approved ending
        # text], both owner-approved sources.
        base_path = os.path.join(CONTENT, f"{alt_of}.story.json")
        if not os.path.exists(base_path):
            return [f"{sid}: altOf='{alt_of}' but no such base story {base_path}"]
        ending_text = _load_ending_text(alt_of)
        if ending_text is None:
            return [f"{sid}: altOf='{alt_of}' but no ending found in {ENDINGS}"]
        texts = seg_texts(json.load(open(base_path, encoding="utf-8"))) + [ending_text]
    else:
        sp = os.path.join(CONTENT, f"{sid}.story.json")
        if not os.path.exists(sp):
            # Fall back to the drafts folder — a serial episode or any other
            # draft-status story is never embedded (StoryDraftFolderTests
            # enforces that), but its speaker map still needs checking
            # before promotion.
            sp = os.path.join(DRAFTS, f"{sid}.story.json")
        if not os.path.exists(sp):
            return [f"{sid}: no such story {sp}"]
        texts = seg_texts(json.load(open(sp, encoding="utf-8")))

    errs = []

    if len(m["segments"]) != len(texts):
        errs.append(f"{sid}: map has {len(m['segments'])} segments, story has {len(texts)}")

    known = set(m["speakers"])
    for entry in m["segments"]:
        i = entry["index"]
        if i >= len(texts):
            errs.append(f"{sid}[{i}]: segment does not exist"); continue
        joined = "".join(s["text"] for s in entry["spans"])
        if joined != texts[i]:
            # locate the first divergence so the fix is obvious
            a, b = joined, texts[i]
            k = next((j for j in range(min(len(a), len(b))) if a[j] != b[j]), min(len(a), len(b)))
            errs.append(
                f"{sid}[{i}]: spans do not reconstruct the text (diverge at char {k})\n"
                f"      map:   ...{a[max(0,k-25):k+25]!r}\n"
                f"      story: ...{b[max(0,k-25):k+25]!r}")
        for s in entry["spans"]:
            if s["speaker"] not in known:
                errs.append(f"{sid}[{i}]: unknown speaker {s['speaker']!r}")
            if not s["text"]:
                errs.append(f"{sid}[{i}]: empty span")
    return errs

def main():
    maps = sorted(glob.glob(os.path.join(MAPS, "*.voices.json")))
    if not maps:
        print("no speaker maps found"); return 1
    stories = {os.path.basename(p).split(".")[0]
               for p in glob.glob(os.path.join(CONTENT, "*.story.json"))}
    stories |= {os.path.basename(p).split(".")[0]
                for p in glob.glob(os.path.join(DRAFTS, "*.story.json"))}
    mapped, bad = set(), []
    print(f"{'story':20} {'segments':>9} {'spans':>6} {'speakers':>9}  verdict")
    print("-" * 66)
    for p in maps:
        m = json.load(open(p, encoding="utf-8")); mapped.add(m["storyId"])
        errs = check(p); bad += errs
        spans = sum(len(e["spans"]) for e in m["segments"])
        print(f"{m['storyId']:20} {len(m['segments']):>9} {spans:>6} {len(m['speakers']):>9}  "
              f"{'ok' if not errs else 'FAIL'}")
    missing = stories - mapped
    if missing:
        print(f"\nno speaker map yet: {', '.join(sorted(missing))}")
    if bad:
        print("\n" + "\n".join(bad)); print(f"\nFAIL — {len(bad)} problem(s)"); return 1
    print(f"\nPASS — every span set reconstructs its story text exactly"
          f"{'' if not missing else f'; {len(missing)} story/stories still unmapped'}")
    return 0

if __name__ == "__main__":
    sys.exit(main())
