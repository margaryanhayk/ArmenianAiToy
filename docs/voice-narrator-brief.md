# Hiring the storyteller — what to ask, what to record, what to sign

**Written 2026-08-10**, after the owner decided: a real, famous, living Armenian
storyteller, paid, with a **licensed AI clone** of his voice, used for the
stories **and** for Areg's live answers.

This is the practical companion to `docs/voice-decision-brief.md` (which weighed
the options) and to the plan that chose this one. It is ordered deliberately:
**do not spend money on a step until the step above it has an answer.**

---

## 0. Why this is a clone and not just a recording

The owner wants to add new stories himself later, without booking the narrator
again. A human recording cannot do that — every new story and every text
correction means a new studio day. The clone is therefore the requirement, not
a luxury, and it is what most of the paperwork below exists to make safe.

The library today is small: 8 stories + 43 welcome clips + the per-story clips
is about **27 minutes of finished audio** — one session. (Two further stories,
`hedgehog-apple` and `little-cloud`, exist as approved text with no audio at
all; see §3.) It is the *future* stories that justify the clone.

---

## 1. The first question — ask it before anything else

> **"Would you allow an AI voice to be made from your voice?"**

Many performers refuse this outright, at any fee. Finding that out after a fee
negotiation wastes everyone's time and can sour a relationship you want.

Agree the fallback in the same conversation: **if he says no to the clone, will
he still record the 8 stories?** That is a good outcome too — it just means new
stories need him back.

Two more things worth knowing before you meet:

- A famous voice ties the whole product to one person. If he later withdraws,
  the toy loses its voice. §4 is how you protect against that.
- Do not record anything, even "just to test", before §4 is signed. Audio
  recorded without a written AI clause is audio you cannot legally clone.

---

## 2. What to send him

Keep it short, respectful, and honest that an AI voice is involved. Hiding that
and revealing it later is the one thing that reliably ends these conversations.

> **DRAFT — pending `armenian-linguistic-reviewer` before it is sent.**
> Same rule as every other Armenian text in this repo: nothing goes out on the
> first draft.

```
Հարգելի՛ [Անուն],

Ես Հայկն եմ։ Ստեղծում եմ հայերեն խոսող մանկական խաղալիք՝ «Արեգ» անունով,
4–7 տարեկան երեխաների համար։ Այն երեխային հեքիաթ է պատմում և պատասխանում
է նրա հարցերին։

Ձեր ձայնն է այն ձայնը, որով ես ուզում եմ՝ երեխաները լսեն այդ հեքիաթները։

Կցանկանայի Ձեզ առաջարկել երկու բան՝

1. մեկ ստուդիական ձայնագրություն — մոտ 30 րոպե պատրաստի ձայն
   (8 հեքիաթ և կարճ արտահայտություններ),
2. և իրավունք՝ Ձեր ձայնի հիման վրա թվային (AI) ձայն ստեղծելու, որպեսզի
   ապագայում նոր հեքիաթներ ավելացնենք առանց Ձեզ նորից անհանգստացնելու։

Երկրորդ կետը լուրջ որոշում է, և ես ուզում եմ, որ այն լինի պարզ ու
գրավոր։ Պայմանագրով կսահմանվեն՝ ժամկետը, թե ինչ կարող է և ինչ չի կարող
ասել այդ ձայնը, և որ այն երբեք չի վաճառվի կամ փոխանցվի երրորդ անձի։

Կարո՞ղ ենք հանդիպել կամ խոսել։

Հարգանքով՝
Հայկ Մարգարյան
```

---

## 3. The recording session — get it right once

The same tapes serve two purposes: they are the **finished story audio** *and*
the **reference corpus the clone is trained from**. A session that is good for
one and sloppy for the other has to be repeated.

**Technical**

| | |
|---|---|
| Format | WAV, **48 kHz, 24-bit, mono** (44.1/16 acceptable floor). Never MP3 at source. |
| Files | **One file per story SEGMENT, not one per story.** See below — this is the single most valuable thing to get right in the session. |
| Room | Quiet, no echo. A proper booth if at all possible. |
| Processing | **None.** No noise reduction, no EQ, no compression, no de-esser. Cloning wants raw. Levelling to -16.4 LUFS happens later in our own pipeline. |
| Consistency | Same mic, same distance, same day. A clone trained on varying distance sounds unstable. |
| Takes | Slate each one. Keep every raw file forever — that corpus is an asset. |

**Ask for one file per segment — this cannot be added afterwards**

The stories are already written as 4–9 numbered segments, so this costs the
studio nothing (they slate takes anyway) and it buys four things at once:

1. **An exact segment map, which this project has never had.** There are zero
   `.segments.json` files in the repo today. The backend wants one
   (`StoryQaController.OffsetToSegment`) and, not finding it, guesses with
   `offset × segmentCount ÷ fileSize`.
   Note the map takes **two** steps, not one: `mix_ambience.py` emits it in
   seconds, and `segments_to_bytes.py` converts that to the byte offsets the
   backend actually deserializes, after the MP3 exists. Skip the second and the
   backend ignores the file without complaining and carries on guessing.
2. **Correct scene context when a child interrupts to ask a question.** That
   guess is only as good as the file matching the text — and on the three
   truncated stories it does not, so today a child near the end of the *file* is
   scored as near the end of the *story* and gets an answer about a scene he has
   not heard.
3. **Exact ambience placement.** The 29 cues in
   `backend/content/story-ambience/ambience-cues.json` are anchored to a segment
   index plus a quoted line precisely *because* no timings exist. Per-segment
   files turn those anchors into exact times with no guesswork.
4. **Cheap corrections.** A fluffed line means re-recording one segment, not a
   whole story.

WAV per segment also removes the glued-header defect by construction: everything
is concatenated once and encoded to MP3 exactly once, so there is never a second
length header for a player to believe. That defect once made a four-minute story
stop at 34 seconds.

Name them `<storyId>-01.wav`, `<storyId>-02.wav`, … in reading order.

**Content to record, in this order**

1. **All 10 stories**, full length, from
   `backend/src/ArmenianAiToy.Application/Stories/Content/*.story.json`.
   Note: only **8** have audio today. `hedgehog-apple` and `little-cloud` have
   never been recorded at all and are ~20 seconds each — while he is in the room
   they cost a minute and take the library from 8 stories to 10.
2. The **43 welcome clips** from `backend/content/voice-clips/voice-clips.json`.
3. The per-story clips (intro / question / summary / offer / reoffer).
4. **20–30 minutes of extra neutral reading** beyond the script. This is purely
   clone-training material and it is the cheapest quality you will ever buy —
   the marginal cost is studio minutes, and no commercially-licensed
   single-speaker Eastern Armenian narration corpus exists anywhere to buy
   instead.

**Direction** — he is telling a story to one small child in the room, not
performing to an audience. Warm, unhurried, a little magic. Not a newsreader,
not a teacher, not a cartoon. The tone rules in `.claude/MODES.md` apply to him
exactly as they apply to the model.

---

## 4. The contract — the clauses that matter

From the NAVA synthetic-voice checklist (summarised in
`docs/voice-decision-brief.md`), plus two this product specifically needs.

**Must be in it**

- **Separate, explicit consent for AI use** — its own signed section, not a line
  buried in the session agreement.
- **Fixed start and end dates. Never perpetuity.** Renewable is fine.
- **No revocation during the term.** Without this, one disagreement silences the
  toy in every home that has one.
- **Flat fee, not per-character royalties.** The whole library is ~24,000
  characters; at market royalty rates that is under a dollar, which is an
  insulting offer. Pay a real fee for a defined term.
- **Limits on model training** — what the audio may and may not be used to train.
- **Secure storage** of the voice model and everything derived from it.
- **No resale, no sublicence.**
- **Content prohibitions** — this voice must never be made to produce political,
  sexual, medical-advice or endorsement content. It speaks unscripted to
  children; this clause is what stops the worst headline.

**Also settle**

- Who owns the trained model — you, him, or the vendor. Get it in writing.
- What happens at the end of the term: is the model deleted, or does it freeze?
- Exclusivity: may he license his voice to a competing product?
- **EU AI Act Article 50** has been in force since 2 August 2026. A talking toy
  for 4–7-year-olds cannot rely on "obvious to a reasonably informed person",
  and synthetic content carries a marking obligation. **This is a lawyer
  question, and it is live now, not later.**

---

## 5. The vendor — the real technical risk

ElevenLabs **cannot do this job**: it forbids cloning another person even with
consent (enforced by a live voice-captcha that money cannot bypass), and its
cloning has no Armenian at all. So a different vendor is required, and the one
we need must hold **all three** of these at once — no vendor is yet confirmed to:

1. third-party cloning with documented consent,
2. **Armenian** in the *cloned* voice,
3. fast enough for a live answer.

**Contact in this order**

- **VS.AM (Yerevan)** — sells custom Armenian voice models for brands,
  "legally compliant", with an API. An Armenian company for an Armenian voice,
  and you can call them in Armenian. *Their site could not be reached from the
  build container, so their terms must be confirmed directly.*
- **Camb.ai** — consent-based cloning with permission management, 150+
  languages, short reference audio. No published Armenian confirmation and no
  published character rate; needs a sales conversation.

**Ask each one exactly this**

1. Can you clone a **specific other person** with their written consent? What
   proof of consent do you require?
2. Does the **cloned** voice speak Eastern Armenian — not just your stock voices?
3. **Latency:** time to first audio for a 15-word Armenian sentence over the API?
4. Do you support streaming output?
5. What does the licence permit — a commercial product spoken to children?
6. If we stop paying, what happens to the model? Can we export it?
7. Who owns the trained model?
8. Price shape: per character, per month, or one-time for the clone?
9. Is there an API with documentation?
10. Where is the audio and the model stored? (EU/GDPR — this is a children's
    product.)

---

## 6. The gate before you commit to anybody

**Buy one small paid test first.** Clone a throwaway voice — the owner's own is
ideal, since he already consents — and measure exactly two numbers:

- **Quality:** a real story paragraph in Armenian, in the cloned voice, judged
  by ear.
- **Speed:** time-to-first-audio for a short sentence, measured against the
  toy's existing `X-Qa-*-Ms` instrumentation.

That one test decides the vendor *and* whether the live-answers half is possible
at all.

**If nothing is fast enough, split rather than stall:** use the clone for the
stories (pre-rendered, latency irrelevant) and keep today's live voice for
answers. The toy already ships that way and it is documented as deliberate.
Swapping it later is cheap — `IAudioSynthesisService` is provider-neutral and
`AI:TtsProvider` selects the implementation by config, so a new vendor is one
adapter class plus one DI line.

Today's numbers to beat: TTS ~1.3 s, whole reply ~9–10 s. **A better voice that
answers slower is a worse toy.**

---

## 7. Order of operations, one line each

1. Ask if he will allow an AI clone at all. → §1
2. Agree fee and terms; sign the AI clause **before** any recording. → §4
3. Contact VS.AM and Camb.ai with the ten questions. → §5
4. Run the one paid clone test; get the two numbers. → §6
5. Book the studio; record generously in one session. → §3
6. Render the 8 stories full length, check every one with
   `python3 tools/story-audio/check_story_audio.py`, mix in the ambience cues,
   ship with `Ship-StoryAudio.ps1 -Fix -Apply`.
7. Listen to all of it, end to end, before a single toy gets it.
