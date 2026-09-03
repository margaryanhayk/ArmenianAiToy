# In-story Q&A latency — the firmware half

Written 2026-08-10. Companion to `docs/latency-plan.md`, which is the measured
analysis; this file is what was actually changed on the device, what each change
is worth, and what still has to happen on the backend and on the bench before
the last of it can be switched on.

**Path optimised:** `POST /api/chat/story-qa` — a button press *during* a story,
i.e. a child interrupting to ask something. Not `/api/chat/audio` (that is only
reached from the boot welcome menu). Story flow, welcome flow, moderation and
the OTA machinery are untouched.

**Nothing here was run on hardware.** Every saving below is arithmetic over
constants that are in the source, or a bound taken straight from the measured
analysis. The bench checklist at the end is not optional.

---

## What changed, and what each item is worth

| # | Change | Saving per question | Risk | Default |
|---|---|---:|---|---|
| 1 | Fire the POST **before** the earcon instead of after it | **600 ms** (exact — `AREG_EARCON_DURATION_MS`) | Low | ON |
| 2 | Thinking-bed pulse aborts the moment the answer lands | **0–600 ms, ~300 ms typical** | Low | ON |
| 3 | `DIAG_MARK` serial print + `delay(5)` compiled out | **~70 ms** on the release→answer path (~130 ms per whole turn) | Low | ON |
| 4 | Reuse the TLS connection between questions | **~300–800 ms**, 2nd question onward | Medium | ON |
| 5 | Decode the answer off the live socket | **~500–1500 ms** | High — needs a bench test **and** a backend change | **OFF** (`AREG_QA_STREAM_PLAYBACK`) |

**Default build (items 1–4): ~970 ms off the first question in a story, and
~1.3–1.8 s off every question after it.** Item 5, once both ends are ready, is
another 0.5–1.5 s on top.

---

### 1. The POST now goes out first (`AregVoiceMvp.ino`)

The old order was: play a blocking 600 ms tone → *then* call
`voice_start_question_upload_async()`. The toy sat on a fully-composed WAV,
doing nothing with the network, for 600 ms of every single question.

Now the upload starts first and the earcon is simply the first thinking-bed
pulse. **The child's experience is unchanged** — the tone still begins within a
few milliseconds of them letting go of the button, which is the whole point of
the earcon — the request is just 600 ms further ahead when it does.

The `[latency] qa_release->play_begin_ms` anchor moved with it, to the instant
`record_question()` returns. It used to be taken *after* the earcon, so the
number the bench log printed was ~600 ms kinder than what the child actually
waited. Expect the printed figure to look **worse** on the first run after this
change while the real wait is shorter; that is the metric becoming honest.

### 2. The thinking bed stops when the answer arrives (`audio_io.*`, `.ino`)

`synth_write_tone()` gained an optional `abort` callback, polled every ~256
samples (~16 ms), with an 8 ms linear fade-out so an early stop does not click
on the MAX98357A. `audio_play_thinking_earcon_abortable()` is the public form;
`audio_play_thinking_earcon()` is unchanged and now just calls it with
`nullptr`.

The Q&A bed loop passes `voice_async_upload_done`. Before this, the loop could
only notice the answer had arrived *between* pulses, so a reply that landed
20 ms into a pulse still waited out the other 580 ms. Uniformly distributed,
that is ~300 ms on an average question and 600 ms on a bad one.

### 3. `DIAG_MARK` no longer costs 5 ms a mark (`diag.*`)

Every mark did `Serial.printf` + `Serial.flush()` + `delay(5)`. One Q&A turn
crosses **26 marks** — `audio_mic_begin` (9), `audio_mic_capture` (4),
`audio_mic_end` (6), `audio_play_mp3_buffer` (7) — of which ~14 sit between the
child releasing the button and hearing the answer.

**The RTC breadcrumb is untouched.** `diag_mark()` still writes step/label/line
into RTC slow memory on every call, so `diag_print_previous_boot_context()`
still localises a crash on the next boot exactly as before — that is the part
that survives a reset and the part that has actually been used. Only the *live*
serial trace is now opt-in, behind `AREG_DIAG_MARK_SERIAL`. Turn it on when you
are watching a hang happen in real time.

### 4. One TLS connection instead of one per question (`voice_client.cpp`)

Both Q&A upload paths declared `HTTPClient http;` on the stack. `~HTTPClient()`
calls `disconnect()`, which calls `_client->stop()` on the **shared**
`WiFiClientSecure` in `net_transport.cpp` — so every question tore the TLS
session down and the next one paid a full handshake.

The fix is the one `content_sync.cpp` already landed in the field on 2026-08-07,
after its per-call clients died at ~14 files: one long-lived client with
`setReuse(true)`, plus a retry on a wedged keep-alive that resets the shared TLS
client first. `qa_http()` allocates it on the heap on first use (like
`tls_client()` does) rather than as a static object, because a plain
`static HTTPClient` costs ~230 B of `.bss` on every toy whether or not a child
ever asks a question.

**The retry is deliberately narrower than content_sync's.** A GET is idempotent,
so content_sync retries every negative status. This POST is not — it spends an
STT + moderation + GPT + TTS pipeline — so a blind retry could bill one child's
question twice and speak two answers. `qa_post_with_retry()` retries only on
`CONNECTION_REFUSED` / `SEND_HEADER_FAILED` / `SEND_PAYLOAD_FAILED`: the three
outcomes that *prove* the request never reached the server, and precisely what
an idle-closed reused socket produces. Anything later (connection lost while
reading headers, read timeout) may already have been processed and is reported
to the caller unchanged.

**Honest scope of this win.** Any *other* module's local `HTTPClient` (heartbeat,
OTA poll, content sync) still stops the shared client when it is destroyed. So
the reuse holds **between questions inside one story** — which is the case that
matters, since nothing else touches the network while a story plays — and the
first question after any other backend call still handshakes. Making heartbeat
and the OTA poll share the same pattern is a separate slice; it touches the OTA
machinery, which was out of scope here.

### 5. Streaming playback — `AREG_QA_STREAM_PLAYBACK`, **default OFF**

Today the toy waits for the *whole* answer body to land in PSRAM before the
first sound. At ~30–60 KB that is ~0.5–1.5 s of pure silence after the answer is
already finished on the server.

**`audio_play_qa_stream(url)` — the function the analysis pointed at — cannot be
used, and this is worth being explicit about.** It builds an
`AudioFileSourceHTTPStream`, which issues its own **GET**. `/api/chat/story-qa`
is POST-only, so that GET 404s; and if a GET were ever added there it would
re-run STT + moderation + GPT + TTS and **double-bill one child's question**.
The answer only ever exists as the body of the POST the toy already made.

So the flag wires up a second form, `audio_play_qa_stream_response(body,
content_length)`, which decodes **that** body:

- The async upload task stops at the response **headers** and publishes
  `VoiceTurnResult.streaming = true` with nothing buffered. The `HTTPClient` is
  the long-lived `qa_http()` object from item 4, so it stays valid after the
  task exits and the loop core reads the body from it.
- The loop core decodes straight off the socket, so sound starts on the first
  frames. The thinking bed still covers the server's think time — that is why
  the task publishes at the headers rather than the POST being made
  synchronously.
- **Chunked bodies are de-framed on the device.** This is the part that is easy
  to get wrong: `HTTPClient` only de-chunks inside `writeToStream()`.
  `getStreamPtr()` hands back the **raw socket, with the hex chunk-size lines
  still in it**. Feeding those to the MP3 decoder is garbage-in. The de-framer
  lives in `AudioFileSourceHttpBody` in `audio_io.cpp` and handles both shapes:
  `Content-Length` present → read exactly that many bytes; absent → read chunk
  by chunk until the terminating zero-size chunk.
- **The #048 non-MP3 guard is preserved.** The first bytes are sniffed for an
  ID3 tag or an MPEG frame sync *before* any byte reaches the decoder, exactly
  as `read_response_into()` does, and then replayed from a 3-byte prefix buffer
  because a socket cannot be rewound.
- **Reads are bounded and feed the watchdog.** The decoder calls `read()` from
  inside `mp3.loop()`, which is where the toy legitimately waits on the network;
  an unbounded wait there is a task-watchdog reboot. Stall bound is
  `AREG_HTTP_READ_MS`.
- **`voice_qa_stream_finish()` closes the connection instead of pooling it.**
  The decoder stops at the audio's end, which need not be the body's end (ID3v1
  trailer, a final partial frame), and a chunked body additionally leaves the
  CRLF after the terminating chunk unread. Reusing a socket that still has bytes
  in it makes the *next* request read them as its own response — a silent,
  one-turn-delayed corruption, in a child's room. One extra handshake is far
  cheaper. So item 5 partly gives back item 4; item 4 keeps its full value on
  the shipped default path.
- **There is no buffered copy to fall back on.** If the stream fails the child
  heard nothing, so the caller plays the canned failure clip. That is the
  single biggest reason this ships off.

`turn.streaming` is `false` in every flag-off build, so every existing
`if (turn.ok)` caller keeps its buffered contract unchanged.

---

## What the backend must do for item 5

**DONE on both ends (2026-09-01).** The backend now streams `/api/chat/story-qa`
chunked — answer audio on its first synthesized bytes, then pause + bridge
(+ recap) in the same body — but ONLY when the request carries
`X-Areg-Accept-Stream: 1`. The flag-on async upload sends that header
(`s_qa_accept_stream`, set around its POST in `upload_question_task()`); the
sync fallback path and every flag-off build do not, so they keep receiving the Content-Length body
they require. Nothing else about the contract changed: the shape is negotiated
per request exactly as the "Preferred" order below describes, and the
paragraphs that follow are kept as the record of why. Not yet bench-run on the
toy against the streaming backend (items 10–13 below are the checks).

With the flag on, the device already accepts either wire shape. But the
*saving* depends on the server, and the two ends must be flipped together:

1. **As things stand today (endpoint unchanged):** `/api/chat/story-qa` composes
   the whole MP3, then sends it with a `Content-Length`. With the flag on, the
   toy decodes as the body arrives instead of after it lands — that recovers the
   **download** time (~0.5–1.5 s) and nothing more. This works with **no backend
   change at all**, and it is the version to bench first.

2. **For the larger win, the endpoint must write and flush audio as it is
   produced** rather than composing the whole body first. In ASP.NET Core,
   writing to the response body before a `Content-Length` is set makes the
   response `Transfer-Encoding: chunked`. That is fine for the flag-on firmware
   (the de-framer above), and it is what would let playback start before the
   server has finished the answer.

**The trap that got the previous attempt reverted (`fdc4b66` → `96d6084`):**
`read_response_into()` in `voice_client.cpp` still rejects any response whose
`getSize()` is `<= 0`, and `getSize()` is `-1` for a chunked body. That is the
**buffered** path — i.e. every toy in the field, and every flag-off build. So:

> **A chunked `/api/chat/story-qa` response fails the turn outright on any
> firmware that does not have `AREG_QA_STREAM_PLAYBACK` compiled in.** The child
> hears the canned failure clip.

Which means chunking must not be turned on globally while flag-off firmware
exists in the field. Two workable orders:

- **Preferred:** the backend keys the shape off the request — send chunked only
  when the device asks for it (e.g. a `X-Areg-Accept-Stream: 1` request header
  that the flag-on firmware sends), and keep `Content-Length` for everyone else.
  This is a strictly additive backend change and needs no fleet-wide flag day.
  *(The firmware does not send such a header yet — adding it is a one-line
  change in `qa_post_with_retry()` once the backend agrees on the name.)*
- **Otherwise:** ship flag-on firmware to every toy first, confirm it, and only
  then switch the response shape. Slower, and it strands any toy that misses the
  rollout.

Not this repo's slice to make either way — a separate slice owns the server.

---

## What a bench test must check

Items 1–4 (default build) — one story, several questions:

1. The earcon still begins **immediately** on button release; no perceptible
   gap, no click at its start.
2. The bed pulse that ends early **fades**, it does not cut. Listen for a click
   at the moment the answer starts.
3. `[latency] qa_release->play_begin_ms` — record it for 5+ questions. It is
   now anchored at release, so compare against a *pre-change* run's number
   **plus 600 ms**, not against it directly.
4. Question 2+ in the same story should be visibly faster than question 1 in the
   serial log. If it is not, the TLS reuse is not holding — check whether a
   heartbeat or content-sync ran in between.
5. Force the reuse-retry path: let the toy idle long enough for the server to
   close the keep-alive (or pull the router briefly), then ask a question.
   Expect one `[qa] request not delivered (…) — resetting TLS, retrying once`
   and then a normal answer — **and exactly one answer**, never two.
6. Crash forensics still work: trigger a reset mid-turn and confirm the next
   boot prints a real `previous_step=… label=… line=…`. If it prints
   `(none)`, item 3 broke the breadcrumb and must be reverted.
7. Re-run any bench harness you rely on with `-DAREG_DIAG_MARK_SERIAL` and
   confirm the `[mark]` lines come back.

Item 5 (`-DAREG_QA_STREAM_PLAYBACK`), against a backend still sending
`Content-Length`:

8. The answer plays, complete, with no truncation and no audible gap or
   stutter — the decoder is now racing the network rather than reading PSRAM.
9. A **long** answer (the worst case for the decoder outrunning the socket).
10. Then against a chunked backend, if one exists: same checks, plus confirm the
    de-framer is right — a chunk-header byte reaching the decoder shows up as a
    burst of noise, not as silence.
11. Kill Wi-Fi mid-answer. Expect the stall bound to fire, the canned failure
    clip to play, and **no watchdog reboot**.
12. Ask a second question straight after a streamed one and confirm it is
    answered normally — that is the check on `voice_qa_stream_finish()` closing
    a possibly-undrained socket instead of pooling it.
13. Free heap after 10 streamed questions vs after 10 buffered ones. No growth.

---

## Build sizes

Canonical FQBN
`esp32:esp32:esp32s3:PSRAM=opi,FlashSize=8M,PartitionScheme=custom,CDCOnBoot=cdc`.

| Build | Flash | Static RAM (globals) | Free for locals |
|---|---:|---:|---:|
| Production, before | 1,297,835 B | 100,352 B | 227,328 B |
| **Production, after** | **1,297,743 B** (−92 B) | **100,352 B** (unchanged) | **227,328 B** (unchanged) |
| `-DAREG_QA_STREAM_PLAYBACK` | 1,299,623 B (+1,880 B) | 100,352 B | 227,328 B |
| `-DAREG_DIAG_MARK_SERIAL` | 1,297,847 B (+104 B) | 100,352 B | 227,328 B |

Static RAM is flat in all four — the only new long-lived object (`qa_http()`) is
heap-allocated on first use, and the streaming decoder's state lives on the
stack of the call that is decoding.

---

## Not done here (and why)

- **Compressing or downsampling the upload** (lever 6 in the analysis, ~0.5–1.5 s).
  It changes what the STT model hears, so it needs an Armenian accuracy check on
  real children's speech before it can be considered safe.
- **Making heartbeat / OTA poll / content sync share one connection.** Would
  extend item 4's win to the first question of a session, but it touches the OTA
  machinery, which was out of scope.
- ~~**The `X-Areg-Accept-Stream` request header.**~~ Done 2026-09-01, together
  with the backend half that honours it.
- **Bumping `AREG_FW_VERSION` or building a release image.** The owner owns the
  OTA release.
