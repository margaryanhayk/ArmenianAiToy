# -*- coding: utf-8 -*-
"""Check that every ambience cue still points at real story text.

Cues are anchored to a segment index AND an exact quoted line rather than a
timestamp, because the shipped stories have no timing map and are about to be
re-recorded anyway. That anchor is only worth anything if it is checked: edit a
story's text and the quote silently stops matching, which is how a forest ends
up three sentences away from the river.

Needs nothing installed - no ffmpeg, no dotnet - so it can run in CI beside
check_story_audio.py. Exits non-zero on the first broken anchor.

    python3 tools/story-audio/check_ambience_anchors.py
"""
import json, glob, os, sys, unicodedata

CUES = 'backend/content/story-ambience/ambience-cues.json'
CONTENT = 'backend/src/ArmenianAiToy.Application/Stories/Content'

def norm(s):
    s = unicodedata.normalize('NFC', s or '')
    for ch in '՚՛՜՝՞’‘':   # Armenian marks
        s = s.replace(ch, '')
    return ' '.join(s.split())

d = json.load(open(CUES, encoding='utf-8'))
sounds = {s['id'] for s in d['sounds']}
bad, checked = [], 0

for story in d['stories']:
    sid = story['storyId']
    path = os.path.join(CONTENT, sid + '.story.json')
    if not os.path.exists(path):
        bad.append((sid, '-', 'no story file at ' + path)); continue
    js = json.load(open(path, encoding='utf-8'))
    segs = [norm(x if isinstance(x, str) else x.get('text', '')) for x in js.get('segments', [])]
    for c in story['cues']:
        checked += 1
        i = int(c['segment'])
        if i >= len(segs):
            bad.append((sid, i, 'segment %d does not exist (story has %d)' % (i, len(segs)))); continue
        if norm(c['cueLine']) not in segs[i]:
            bad.append((sid, i, 'cueLine not found in that segment: ' + c['cueLine'][:40])); continue
        if c['sound'] not in sounds:
            bad.append((sid, i, 'unknown sound id: ' + c['sound']))

print('checked %d cues across %d stories against %d sound ids'
      % (checked, len(d['stories']), len(sounds)))
if bad:
    print('\nBROKEN ANCHORS:')
    for sid, seg, why in bad:
        print('  %-18s seg %-3s %s' % (sid, seg, why))
    sys.exit(1)
print('every cue lands in the segment it names, quoting a line that is really there')
