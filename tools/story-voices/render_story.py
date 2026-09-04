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
# Between two segments the narrator turns the page. This gap is also load-
# bearing arithmetic: a segment's start in the map is the running sum of the
# audio AND the gaps before it, so changing it here changes the byte map.
PAUSE_SEGMENT  = 0.60

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

DEFAULT_MODEL = "eleven_v3"

def tts(text, path, token, voice, settings, model=DEFAULT_MODEL):
    body = {"text": text, "model_id": model}
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

STT_MODEL = "scribe_v2"
STT_MAX_EXTRA_WORDS = 0        # any word the story does not have is a fault
STT_MAX_WER = 0.35             # above this the take is garbage, not an accent

def _norm_words(t):
    t = re.sub(r"[՞՛՜]", "", t.lower())
    return re.sub(r"[^\w\s]", " ", t).split()

def _wer(ref, hyp):
    r, h = _norm_words(ref), _norm_words(hyp)
    d = list(range(len(h) + 1))
    for i in range(1, len(r) + 1):
        prev, d[0] = d[0], i
        for j in range(1, len(h) + 1):
            cur = d[j]
            d[j] = min(d[j] + 1, d[j - 1] + 1, prev + (r[i - 1] != h[j - 1]))
            prev = cur
    return d[len(h)] / max(1, len(r))

def spoken_matches(path, text, token):
    """Transcribe the span and refuse a take that says something the story
    does not. The owner caught «Արածում է shshsh իրիկունը» and a stray «hmm»
    after «դուռը բաց անում» by ear (2026-09-04); both transcribe as extra
    words. Returns (ok, reason). A key without speech_to_text permission
    skips the check with a warning rather than failing the render."""
    r = subprocess.run(
        ["curl","-sS","--max-time","120","-X","POST","-H",f"xi-api-key: {token}",
         "-F",f"model_id={STT_MODEL}","-F","language_code=hy","-F",f"file=@{path}",
         "https://api.elevenlabs.io/v1/speech-to-text"], capture_output=True)
    try:
        j = json.loads(r.stdout.decode("utf-8", "replace"))
    except ValueError:
        return True, "stt unreadable — skipped"
    if "text" not in j:
        return True, f"stt skipped ({str(j)[:80]})"
    hyp = j["text"]
    extra = len(_norm_words(hyp)) - len(_norm_words(text))
    w = _wer(text, hyp)
    if extra > STT_MAX_EXTRA_WORDS or w > STT_MAX_WER:
        return False, f"heard {hyp[:90]!r} (+{extra} words, wer {w:.2f})"
    return True, f"wer {w:.2f}"

def render_segment(smap, seg, outdir, token, voice, sid):
    parts, problems = [], []
    for i, span in enumerate(seg["spans"]):
        who = span["speaker"]
        spk = smap["speakers"][who]
        text = guard(span["text"].strip())
        # The story id is in the NAME. Without it, rendering ten stories into
        # one directory silently overwrites every span of the first nine — the
        # finished audio survived (each story is stitched before the next
        # begins) but the pieces did not, and that is why the ambience mixer
        # later had to INFER where a speaker changed instead of measuring it.
        raw = os.path.join(outdir, f"{sid}-{seg['index']:02d}-{i:02d}-{who}.mp3")

        # A CAST, not one voice (owner decision 2026-09-03): a speaker may name
        # its own ElevenLabs voice and model. Without them it is the narrator's
        # voice in the default model, so every speaker map written before this
        # renders exactly as it did. The narrator stays Areg by rule; clones
        # for anyone a child should love; library voices only for villains
        # and animals, because a library voice speaks Armenian with an accent.
        spk_voice = spk.get("voiceId") or voice
        spk_model = spk.get("modelId") or DEFAULT_MODEL
        # Render, and re-ask if the model returns it with the tail cut off.
        for attempt in range(TAIL_RETRIES + 1):
            tts(text, raw, token, spk_voice, spk.get("voiceSettings"), spk_model)
            ratio = tail_ratio(raw)
            expect = len(text) / CHARS_PER_SECOND
            short = duration(raw) < expect * TAIL_MIN_LENGTH_RATIO
            said_ok, why = spoken_matches(raw, text, token)
            if not said_ok:
                if attempt == TAIL_RETRIES:
                    raise SystemExit(f"NOT THE STORY: span {i} ({who}) {text[:40]!r} — {why}, "
                                     f"after {TAIL_RETRIES} retries")
                print(f"    {i:02d} {who:14} {why} — re-asking", flush=True)
                continue
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
        parts.append((who, wav, pause_after(text), len(text), duration(wav)))
        print(f"    {i:02d} {who:14} {len(text):>4}ch {got:>5.1f}s "
              f"tail {tail_ratio(raw):.0%}"
              f"{'  pitch '+str(pitch) if pitch!=1.0 else ''}"
              f"{'  voice '+spk_voice[:8]+'… '+spk_model if spk.get('voiceId') else ''}", flush=True)
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
        for who, wav, pause, _chars, _dur in parts:
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

def span_timings(parts):
    """Where each span starts INSIDE its segment, and how long it runs.

    The same arithmetic stitch() uses, kept next to it deliberately: these two
    must agree exactly or the map describes audio that was never made.
    """
    out, t, prev, prev_pause = [], 0.0, None, None
    for who, _wav, pause, chars, dur in parts:
        if prev is not None:
            t += PAUSE_SPEAKER if who != prev else prev_pause
        out.append({"speaker": who, "chars": chars,
                    "start": round(t, 3), "duration": round(dur, 3)})
        t += dur
        prev, prev_pause = who, pause
    return out


def write_span_map(sid, smap, span_map, outdir):
    """Times are RELATIVE to each segment's start, on purpose.

    The segment map is regenerated against the finished MP3 after the shipper
    re-encodes; expressing spans absolutely would make them disagree with it by
    whatever that encode shifted. Relative, they stay true and a consumer adds
    the segment start it already has.

    This is what lets an ambience cue land on a LINE. Without it the mixer has
    to estimate a speaker change from character counts and then hunt for a
    nearby pause, which works but is a guess dressed as a measurement.
    """
    # A resumed run skips segments it already has, so their spans were never
    # measured. Writing what we do have would produce a map that is the right
    # SHAPE and wrong — the exact failure mode this repo keeps paying for.
    want = [seg["index"] for seg in smap["segments"]]
    missing = [i for i in want if i not in span_map]
    if missing:
        print(f"  no span map: segments {missing} were resumed from disk, so "
              f"their spans were never timed. Re-render the story to get one.")
        return
    doc = {"storyId": sid, "unit": "seconds", "relativeTo": "segment start",
           "segments": [span_map[i] for i in want]}
    p = os.path.join(outdir, f"{sid}.spans.json")
    with open(p, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
    n = sum(len(v) for v in span_map.values())
    print(f"  -> {p}  {n} spans")


def assemble(sid, seg_files, outdir):
    """Join the segments into the story, and write the map of where they are.

    The map is in SECONDS because byte offsets cannot be known yet — they
    depend on the re-encode Ship-StoryAudio.ps1 does afterwards. That is what
    tools/story-audio/segments_to_bytes.py converts, and it must run last.
    """
    sil = os.path.join(outdir, f"_segsil.wav")
    subprocess.run(["ffmpeg","-v","error","-y","-f","lavfi","-t",str(PAUSE_SEGMENT),
                    "-i","anullsrc=r=44100:cl=mono",sil], capture_output=True)
    lst = os.path.join(outdir, f"_{sid}.txt")
    starts, t = [], 0.0
    with open(lst, "w") as f:
        for i, seg in enumerate(seg_files):
            if i:
                f.write(f"file '{sil}'\n")
                t += PAUSE_SEGMENT
            starts.append(round(t, 3))
            f.write(f"file '{seg}'\n")
            t += duration(seg)
    out = os.path.join(outdir, f"{sid}.mp3")
    subprocess.run(["ffmpeg","-v","error","-y","-f","concat","-safe","0","-i",lst,
                    "-ac","1","-ar","44100","-b:a","192k",out], capture_output=True)
    mp = os.path.join(outdir, f"{sid}.segments.json")
    json.dump({"storyId": sid, "unit": "seconds", "starts": starts},
              open(mp, "w", encoding="utf-8"))
    print(f"  -> {out}  {duration(out):.1f}s, {len(starts)} segments -> {mp}")
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
    seg_files, span_map = [], {}
    for seg in smap["segments"]:
        if only is not None and seg["index"] != only:
            continue
        f = os.path.join(outdir, f"{sid}-seg{seg['index']}.mp3")
        # Resume. 211 paid requests where one chopped span at number 200 throws
        # away the other 199 is the wrong shape: a finished segment is money
        # already spent, so it is never re-requested.
        if os.path.exists(f) and os.path.getsize(f) > 1000:
            print(f"  segment {seg['index']} already rendered — keeping")
            seg_files.append(f); continue
        print(f"  segment {seg['index']} ({len(seg['spans'])} spans)")
        parts = render_segment(smap, seg, outdir, token, voice, sid)
        print("  ->", stitch(parts, outdir, f"{sid}-seg{seg['index']}.mp3"))
        seg_files.append(f)
        span_map[seg["index"]] = span_timings(parts)
    # A single-segment run is a spot check, not a story: assembling one segment
    # into <sid>.mp3 would look exactly like a finished render and ship.
    if only is None:
        assemble(sid, seg_files, outdir)
        write_span_map(sid, smap, span_map, outdir)
    return 0

if __name__ == "__main__":
    sys.exit(main())
