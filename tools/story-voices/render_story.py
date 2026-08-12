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
TAIL_WINDOW = 0.03             # how much of the end to inspect for a chop
# A loud ending is not proof of a chop: «- Եկե՜լ եմ,» genuinely ends on an
# emphatic syllable and came back at 81%, 52%, 65% across three renders. So
# truncation needs TWO signals — loud at the very end AND shorter than the
# text implies. Either alone is normal speech.
TAIL_MAX_RATIO = 0.35
TAIL_MIN_LENGTH_RATIO = 0.70   # got/expected below this, with a loud tail, is a cut
TAIL_RETRIES = 2               # rendering is non-deterministic; ask again
FADE_IN = 0.008
FADE_OUT = 0.030               # longer out: gives a loud ending a natural decay
                               # instead of a wall, which is what a splice hears

# The story's own punctuation is the timing sheet. A full stop earns more air
# than a comma, and a change of speaker earns a real breath.
PAUSE_SENTENCE = 0.34
PAUSE_CLAUSE   = 0.16
PAUSE_SPEAKER  = 0.40

LATIN_RUN = re.compile(r"[A-Za-z]{3,}")

def guard(text):
    """Refuse anything that is not the tale. This is the whole lesson."""
    if "[" in text or "]" in text:
        raise SystemExit(f"REFUSED: bracket in text sent to TTS: {text[:60]!r}")
    if LATIN_RUN.search(text):
        raise SystemExit(f"REFUSED: Latin letters in Armenian text: {text[:60]!r}")
    return text

def tail_ratio(path):
    """Peak of the final TAIL_WINDOW against the file's own peak.

    ElevenLabs sometimes returns a short request with the last word chopped —
    «- Եկե՜լ եմ,» came back ending at 92% of its own peak, mid-syllable. A file
    that ends loud has been cut; a file that ends on a decay has not.
    """
    import struct
    raw = subprocess.run(["ffmpeg","-v","error","-i",path,"-f","s16le",
                          "-ac","1","-ar","44100","-"], capture_output=True).stdout
    n = len(raw)//2
    if n < 2000: return 1.0
    s = struct.unpack(f"<{n}h", raw[:n*2])
    peak = max(abs(x) for x in s) or 1
    edge = int(TAIL_WINDOW*44100)
    return (max(abs(x) for x in s[-edge:]) or 0) / peak

def pause_after(text):
    """How much air this span has earned, from its final punctuation."""
    t = text.rstrip()
    if t.endswith(("։", "՞", "՜", "...", "․․․", ".", "!", "?")): return PAUSE_SENTENCE
    if t.endswith((",", "՝", ":", ";", "—", "-")):               return PAUSE_CLAUSE
    return PAUSE_CLAUSE

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

        # Render, and re-ask if the model returns it with the tail cut off.
        for attempt in range(TAIL_RETRIES + 1):
            tts(text, raw, token, voice, spk.get("voiceSettings"))
            ratio = tail_ratio(raw)
            expect = len(text) / CHARS_PER_SECOND
            short = duration(raw) < expect * TAIL_MIN_LENGTH_RATIO
            if ratio <= TAIL_MAX_RATIO or not short:
                break
            if attempt == TAIL_RETRIES:
                raise SystemExit(
                    f"CHOPPED: span {i} ({who}) {text[:40]!r} ends at {ratio:.0%} "
                    f"of its peak AND is {duration(raw):.1f}s against ~{expect:.1f}s "
                    f"expected, after {TAIL_RETRIES} retries — the last word is cut")
            print(f"    {i:02d} {who:14} tail {ratio:.0%}, short — re-asking", flush=True)

        got, want = duration(raw), len(text) / CHARS_PER_SECOND
        if want > 0.3 and got > want * MAX_OVERRUN:
            problems.append(f"  span {i} ({who}): {len(text)} chars implies "
                            f"~{want:.1f}s, got {got:.1f}s — something was spoken "
                            f"that is not in the story")

        pitch = spk.get("pitch", 1.0)
        af = ("loudnorm=I=-17:TP=-1.5" if abs(pitch - 1.0) < 0.005 else
              f"asetrate=44100*{pitch},aresample=44100,atempo=1/{pitch},"
              f"loudnorm=I=-17:TP=-1.5")
        stage = raw[:-4] + ".stage.wav"
        subprocess.run(["ffmpeg","-v","error","-y","-i",raw,"-af",af,
                        "-ac","1","-ar","44100",stage], capture_output=True)

        # Fades go in a SECOND pass, timed against the file that actually
        # exists. Computing the fade-out from the pre-loudnorm duration missed
        # by enough to do nothing at all — a 67% ending stayed 66%.
        wav = raw[:-4] + ".wav"
        real = duration(stage)
        subprocess.run(["ffmpeg","-v","error","-y","-i",stage,"-af",
                        f"afade=t=in:st=0:d={FADE_IN},"
                        f"afade=t=out:st={max(0, real-FADE_OUT):.3f}:d={FADE_OUT}",
                        "-ac","1","-ar","44100",wav], capture_output=True)
        os.remove(stage)
        parts.append((who, wav, pause_after(text)))
        print(f"    {i:02d} {who:14} {len(text):>4}ch {got:>5.1f}s "
              f"tail {tail_ratio(raw):.0%}"
              f"{'  pitch '+str(pitch) if pitch!=1.0 else ''}", flush=True)
    if problems:
        raise SystemExit("SPAN LENGTH CHECK FAILED\n" + "\n".join(problems))
    return parts

def stitch(parts, outdir, name):
    """Join the spans, giving each seam the air its punctuation asks for."""
    made = {}
    def silence(sec):
        key = round(sec, 3)
        if key not in made:
            p = os.path.join(outdir, f"_sil{int(key*1000)}.wav")
            subprocess.run(["ffmpeg","-v","error","-y","-f","lavfi","-t",str(key),
                            "-i","anullsrc=r=44100:cl=mono",p], capture_output=True)
            made[key] = p
        return made[key]

    lst = os.path.join(outdir, f"_{name}.txt")
    with open(lst, "w") as f:
        prev, prev_pause = None, None
        for who, wav, pause in parts:
            if prev is not None:
                # the gap belongs to the span that just ENDED — its punctuation
                # is what earned the air, not the one about to start
                gap = PAUSE_SPEAKER if who != prev else prev_pause
                f.write(f"file '{silence(gap)}'\n")
            f.write(f"file '{wav}'\n")
            prev, prev_pause = who, pause
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
