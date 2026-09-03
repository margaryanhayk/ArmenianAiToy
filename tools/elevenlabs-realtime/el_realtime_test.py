#!/usr/bin/env python3
"""Step 3 of docs/latency-plan.md Part 4: does ElevenLabs' 2026 realtime pair
speak Armenian, and how fast?

  TTS  eleven_v3 (today's model, baseline)  vs  eleven_v3_conversational
       -> first-byte and total time, MP3s saved for the owner to listen to
  STT  scribe_v2 (batch, baseline)  vs  scribe_v2_realtime (websocket)
       -> transcript vs known text (WER), time from end-of-audio to committed

Reads ELEVENLABS_API_KEY from the environment. Never prints it.
Writes everything under ./out/.
"""
import asyncio, base64, json, os, re, subprocess, sys, time, urllib.parse
from pathlib import Path

import httpx
import websockets

KEY = os.environ.get("ELEVENLABS_API_KEY")
if not KEY:
    sys.exit("ELEVENLABS_API_KEY is not set")
VOICE = os.environ.get("ELEVENLABS_VOICE_ID", "NxAsEwnikgCJa5tyBwEf")  # areg-storyteller
BASE = "https://api.elevenlabs.io"
WS = "wss://api.elevenlabs.io"
HERE = Path(__file__).resolve().parent
OUT = HERE / "out"
OUT.mkdir(exist_ok=True)
H = {"xi-api-key": KEY}

# ---------------------------------------------------------------- TTS samples
TTS_SAMPLES = {
    "short": "Փոքրիկ ամպիկը երկնքում ապրող մի ամպ է։",
    "answer": "Փոքրիկ ամպիկը մի փոքր, սպիտակ ամպ է, որն ապրում է երկնքում։ Նա շատ է սիրում խաղալ արևի հետ։",
    "story": "Լինում է, չի լինում՝ մի աղքատ մարդ։ Էս աղքատ մարդը գնում է դառնում մի ձկնորսի շալակատար։ Օրական մի քանի ձուկն է աշխատում, տուն բերում, նրանով ապրում են ինքն ու կնիկը։",
}
TTS_MODELS = ["eleven_v3", "eleven_v3_conversational"]


def tts_stream(model, text, route):
    """route 'tts'  -> POST /v1/text-to-speech/{voice}/stream
       route 'dlg'  -> POST /v1/text-to-dialogue/stream (the endpoint the
                       conversational docs name; exactly one voice)."""
    if route == "tts":
        url = f"{BASE}/v1/text-to-speech/{VOICE}/stream?output_format=mp3_44100_128"
        body = {"text": text, "model_id": model}
    else:
        url = f"{BASE}/v1/text-to-dialogue/stream?output_format=mp3_44100_128"
        body = {"inputs": [{"text": text, "voice_id": VOICE}], "model_id": model}
    t0 = time.perf_counter()
    first = None
    buf = bytearray()
    with httpx.Client(timeout=60) as c:
        with c.stream("POST", url, headers=H, json=body) as r:
            if r.status_code != 200:
                err = r.read()[:300].decode("utf-8", "replace")
                return {"status": r.status_code, "error": err}
            for chunk in r.iter_bytes():
                if first is None and chunk:
                    first = time.perf_counter() - t0
                buf.extend(chunk)
    return {"status": 200, "ttfb_s": round(first or 0, 3),
            "total_s": round(time.perf_counter() - t0, 3), "bytes": len(buf), "audio": bytes(buf)}


def run_tts():
    rows = []
    for model in TTS_MODELS:
        for name, text in TTS_SAMPLES.items():
            for route in ("tts", "dlg"):
                for attempt in (1, 2):  # 1 = cold, 2 = warm
                    r = tts_stream(model, text, route)
                    row = {"model": model, "sample": name, "route": route, "attempt": attempt,
                           "chars": len(text), **{k: v for k, v in r.items() if k != "audio"}}
                    if r["status"] == 200 and attempt == 2:
                        p = OUT / f"tts_{model}_{route}_{name}.mp3"
                        p.write_bytes(r["audio"])
                        row["file"] = p.name
                    rows.append(row)
                    print(json.dumps(row, ensure_ascii=False), flush=True)
                    if r["status"] != 200:
                        break  # no point retrying a rejected route
    return rows


# ---------------------------------------------------------------- STT samples
STT_SAMPLES = [
    {"name": "tts_answer", "path": HERE / "out" / "tts_eleven_v3_tts_answer.mp3", "expected": TTS_SAMPLES["answer"]},
    {"name": "tts_story3", "path": HERE / "out" / "tts_eleven_v3_tts_story.mp3", "expected": TTS_SAMPLES["story"]},
    {"name": "question", "path": HERE / "in" / "question.mp3",
     "expected": "Ո՞վ է փոքրիկ ամպիկը։"},
    {"name": "story_seg0", "path": HERE / "in" / "khosogh-dzuk-seg0.mp3",
     "expected": ("Լինում է, չի լինում՝ մի աղքատ մարդ։ Էս աղքատ մարդը գնում է դառնում մի ձկնորսի շալակատար։ "
                  "Օրական մի քանի ձուկն է աշխատում, տուն բերում, նրանով ապրում են ինքն ու կնիկը։ "
                  "Մի անգամ էլ ձկնորսը մի սիրուն ձուկն է բռնում, տալիս իր շալակատարին, որ պահի, ինքն էլ ետ ջուրն է մտնում։ "
                  "Էս շալակատարը գետափին նստած՝ նայում է, նայում էն սիրուն ձկանն ու միտք է անում։ "
                  "- Տե՛ր աստված,- ասում է,- սա էլ, որ մեզ նման շունչ-կենդանի է, դու ասա՝ սա՞ էլ մեզ նման ծնող ունի, "
                  "ընկեր ունի, աշխարհքից բան է հասկանում, ուրախություն կամ ցավ է զգում, թե՞ չէ․․․ "
                  "Հենց էս մտածելու ժամանակ ձուկը լեզու է առնում։")},
]


def norm_words(s):
    s = s.lower()
    s = re.sub(r"[՞՛՜]", "", s)               # Armenian intra-word marks
    s = re.sub(r"[^\w\s]", " ", s)            # punctuation
    return s.split()


def wer(ref, hyp):
    r, h = norm_words(ref), norm_words(hyp)
    d = list(range(len(h) + 1))
    for i in range(1, len(r) + 1):
        prev, d[0] = d[0], i
        for j in range(1, len(h) + 1):
            cur = d[j]
            d[j] = min(d[j] + 1, d[j - 1] + 1, prev + (r[i - 1] != h[j - 1]))
            prev = cur
    return round(d[len(h)] / max(1, len(r)), 3)


def to_pcm16k(mp3: Path) -> bytes:
    return subprocess.run(["ffmpeg", "-v", "error", "-i", str(mp3), "-f", "s16le", "-ac", "1", "-ar", "16000", "-"],
                          check=True, capture_output=True).stdout


def stt_batch(mp3: Path, model="scribe_v2"):
    t0 = time.perf_counter()
    with httpx.Client(timeout=120) as c:
        r = c.post(f"{BASE}/v1/speech-to-text", headers=H,
                   data={"model_id": model, "language_code": "hy"},
                   files={"file": (mp3.name, mp3.read_bytes(), "audio/mpeg")})
    dt = round(time.perf_counter() - t0, 3)
    if r.status_code != 200:
        return {"status": r.status_code, "error": r.text[:300], "total_s": dt}
    j = r.json()
    return {"status": 200, "total_s": dt, "text": j.get("text", ""), "lang": j.get("language_code")}


async def stt_realtime(pcm: bytes, realtime_pace=True):
    q = {"model_id": "scribe_v2_realtime", "audio_format": "pcm_16000",
         "commit_strategy": "manual", "language_code": "hy", "include_language_detection": "true"}
    url = f"{WS}/v1/speech-to-text/realtime?" + urllib.parse.urlencode(q)
    events, partials = [], []
    chunk = 16000 * 2 // 10  # 100 ms of s16le mono
    audio_secs = len(pcm) / 32000
    t_start = time.perf_counter()
    t_end_audio = t_commit = t_committed = None
    committed = None
    async with websockets.connect(url, additional_headers=H, max_size=None) as ws:
        async def reader():
            nonlocal committed, t_committed
            async for m in ws:
                d = json.loads(m)
                t = d.get("message_type")
                events.append({"t": round(time.perf_counter() - t_start, 3), "type": t,
                               "text": d.get("text") or d.get("transcript")})
                if t == "partial_transcript":
                    partials.append((round(time.perf_counter() - t_start, 3), d.get("text") or d.get("transcript")))
                elif t == "committed_transcript":
                    committed = d.get("text") or d.get("transcript")
                    t_committed = time.perf_counter()
                    return
                elif t in ("error", "auth_error", "quota_exceeded", "unaccepted_terms", "invalid_request",
                           "transcriber_error", "input_error", "resource_exhausted"):
                    committed = f"<{t}: {json.dumps(d, ensure_ascii=False)[:300]}>"
                    t_committed = time.perf_counter()
                    return
        rd = asyncio.create_task(reader())
        for i in range(0, len(pcm), chunk):
            await ws.send(json.dumps({"message_type": "input_audio_chunk", "audio_base_64": base64.b64encode(pcm[i:i + chunk]).decode(),
                                      "commit": False, "sample_rate": 16000}))
            if realtime_pace:
                await asyncio.sleep(0.1)
        t_end_audio = time.perf_counter()
        await ws.send(json.dumps({"message_type": "input_audio_chunk", "audio_base_64": "", "commit": True, "sample_rate": 16000}))
        t_commit = time.perf_counter()
        try:
            await asyncio.wait_for(rd, timeout=20)
        except asyncio.TimeoutError:
            committed = committed or "<timeout waiting for committed_transcript>"
            t_committed = time.perf_counter()
    return {"audio_s": round(audio_secs, 2), "pace": "realtime" if realtime_pace else "burst",
            "commit_to_final_s": round(t_committed - t_commit, 3) if t_committed else None,
            "end_audio_to_final_s": round(t_committed - t_end_audio, 3) if t_committed else None,
            "partials": len(partials), "first_partial_s": partials[0][0] if partials else None,
            "text": committed, "events": events[-6:]}


def run_stt():
    rows = []
    for s in STT_SAMPLES:
        if not s["path"].exists():
            print(f"skip {s['name']}: {s['path']} missing"); continue
        b = stt_batch(s["path"])
        b.update(model="scribe_v2", sample=s["name"])
        if b.get("text") is not None:
            b["wer"] = wer(s["expected"], b["text"])
        rows.append(b); print(json.dumps(b, ensure_ascii=False), flush=True)
        pcm = to_pcm16k(s["path"])
        for pace in (True, False):
            try:
                r = asyncio.run(stt_realtime(pcm, pace))
            except Exception as e:  # handshake rejections surface here
                r = {"error": f"{type(e).__name__}: {str(e)[:300]}"}
            r.update(model="scribe_v2_realtime", sample=s["name"])
            if r.get("text") and not str(r["text"]).startswith("<"):
                r["wer"] = wer(s["expected"], r["text"])
            rows.append(r); print(json.dumps({k: v for k, v in r.items() if k != "events"}, ensure_ascii=False), flush=True)
    return rows


if __name__ == "__main__":
    which = sys.argv[1] if len(sys.argv) > 1 else "all"
    rp = OUT / "results.json"
    res = json.loads(rp.read_text()) if rp.exists() and which != "all" else {}  # a partial run keeps the other half
    if which in ("all", "tts"):
        res["tts"] = run_tts()
    if which in ("all", "stt"):
        res["stt"] = run_stt()
    (OUT / "results.json").write_text(json.dumps(res, ensure_ascii=False, indent=1))
    print("wrote", OUT / "results.json")
