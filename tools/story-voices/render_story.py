#!/usr/bin/env python3
"""Render a story's narration span by span, giving each character its voice.

WHY THIS LOOKS PARANOID
-----------------------
The first version of this put the character direction in the text, as
"[deep thick growling wolf voice] Սևուկ ուլիկ…". eleven_v3 treated that as a
tag on long spans and READ IT ALOUD on short ones: «- Ո՞վ է։», eight Armenian
characters, came back 4.3 seconds long because it had spoken eighty-eight
characters of English first. It works often enough to look fine and fails on
exactly the lines too short to hide it.

So the rule here is absolute: **the only thing ever sent to TTS is the story's
own words.** Expression comes from voice_settings, which are API fields and
cannot be spoken, and from a pitch/formant shift applied afterwards. Two guards
enforce it — one refuses to send text carrying brackets or Latin letters, and
one fails any span that comes back far longer than its Armenian implies.

The `direction` strings stay in the speaker maps. They are notes for a human
narrator, not input for a machine.
"""
import json, os, re, subprocess, sys

VOICE_ENV = "ELEVENLABS_VOICE_ID"
CHARS_PER_SECOND = 15.0        # same rate the library-wide length gate uses
MAX_OVERRUN = 2.5              # a span this much longer than its text is wrong
BREATH_SECONDS = 0.28          # only where the speaker changes

LATIN_RUN = re.compile(r"[A-Za-z]{3,}")

def guard(text):
    """Refuse anything that is not the tale. This is the whole lesson."""
    if "[" in text or "]" in text:
        raise SystemExit(f"REFUSED: bracket in text sent to TTS: {text[:60]!r}")
    if LATIN_RUN.search(text):
        raise SystemExit(f"REFUSED: Latin letters in Armenian text: {text[:60]!r}")
    return text

def duration(path):
    out = subprocess.run(["ffprobe","-v","error","-show_entries","format=duration",
                          "-of","csv=p=0",path], capture_output=True, text=True).stdout.strip()
    return float(out) if out else 0.0

def tts(text, path, token, voice, settings):
    body = {"text": text, "model_id": "eleven_v3"}
    if settings:
        body["voice_settings"] = settings
    r = subprocess.run(
        ["curl","-sS","--max-time","180","-X","POST",
         "-H", f"xi-api-key: {token}", "-H","content-type: application/json",
         "--data-binary","@-","-o",path,
         f"https://api.elevenlabs.io/v1/text-to-speech/{voice}?output_format=mp3_44100_128"],
        input=json.dumps(body, ensure_ascii=False).encode(), capture_output=True)
    if not os.path.exists(path) or os.path.getsize(path) < 1000:
        raise SystemExit(f"render failed for {text[:40]!r}: {r.stderr.decode()[:200]}")

def render_segment(smap, seg, outdir, token, voice):
    parts, problems = [], []
    for i, span in enumerate(seg["spans"]):
        who = span["speaker"]
        spk = smap["speakers"][who]
        text = guard(span["text"].strip())
        raw = os.path.join(outdir, f"{seg['index']:02d}-{i:02d}-{who}.mp3")
        tts(text, raw, token, voice, spk.get("voiceSettings"))

        got, want = duration(raw), len(text) / CHARS_PER_SECOND
        if want > 0.3 and got > want * MAX_OVERRUN:
            problems.append(f"  span {i} ({who}): {len(text)} chars implies "
                            f"~{want:.1f}s, got {got:.1f}s — something was spoken "
                            f"that is not in the story")

        pitch = spk.get("pitch", 1.0)
        af = ("loudnorm=I=-17:TP=-1.5" if abs(pitch - 1.0) < 0.005 else
              f"asetrate=44100*{pitch},aresample=44100,atempo=1/{pitch},loudnorm=I=-17:TP=-1.5")
        wav = raw[:-4] + ".wav"
        subprocess.run(["ffmpeg","-v","error","-y","-i",raw,"-af",af,
                        "-ac","1","-ar","44100",wav], capture_output=True)
        parts.append((who, wav))
        print(f"    {i:02d} {who:14} {len(text):>4}ch {got:>5.1f}s"
              f"{'  pitch '+str(pitch) if pitch!=1.0 else ''}", flush=True)
    if problems:
        raise SystemExit("SPAN LENGTH CHECK FAILED\n" + "\n".join(problems))
    return parts

def stitch(parts, outdir, name):
    sil = os.path.join(outdir, "_breath.wav")
    subprocess.run(["ffmpeg","-v","error","-y","-f","lavfi","-t",str(BREATH_SECONDS),
                    "-i","anullsrc=r=44100:cl=mono",sil], capture_output=True)
    lst = os.path.join(outdir, f"_{name}.txt")
    with open(lst, "w") as f:
        prev = None
        for who, wav in parts:
            if prev is not None and who != prev:
                f.write(f"file '{sil}'\n")
            f.write(f"file '{wav}'\n")
            prev = who
    out = os.path.join(outdir, name)
    subprocess.run(["ffmpeg","-v","error","-y","-f","concat","-safe","0","-i",lst,
                    "-ac","1","-ar","44100","-b:a","128k",out], capture_output=True)
    return out

def main():
    if len(sys.argv) < 3:
        print("usage: render_story.py <storyId> <outdir> [segmentIndex]"); return 2
    sid, outdir = sys.argv[1], sys.argv[2]
    only = int(sys.argv[3]) if len(sys.argv) > 3 else None
    token = os.environ.get("ELEVENLABS_API_KEY") or sys.exit("set ELEVENLABS_API_KEY")
    voice = os.environ.get(VOICE_ENV) or sys.exit(f"set {VOICE_ENV}")
    os.makedirs(outdir, exist_ok=True)
    smap = json.load(open(f"backend/content/story-voices/{sid}.voices.json", encoding="utf-8"))
    for seg in smap["segments"]:
        if only is not None and seg["index"] != only:
            continue
        print(f"  segment {seg['index']} ({len(seg['spans'])} spans)")
        parts = render_segment(smap, seg, outdir, token, voice)
        print("  ->", stitch(parts, outdir, f"{sid}-seg{seg['index']}.mp3"))
    return 0

if __name__ == "__main__":
    sys.exit(main())
