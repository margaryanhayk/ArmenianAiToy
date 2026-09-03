# How the toy works — the interaction roadmap

Owner-requested (2026-08-19): one place that says which step follows which,
what Areg asks, what counts as an answer, and what silence does. The rule
set below was decided with the owner the same evening; the "today" column
records what shipped before it, because the differences are the work list.

## The one-button rule

```
IDLE, story paused half-way  → press = CONTINUE that story
IDLE, nothing paused         → press = MENU ("what shall we do?")
hold (2 s)                   → same as press
```

One rule a 4-year-old can learn: press = Areg asks. The single exception is
a story the child stopped in the middle — asking "what shall we do?" there
would throw away their place, so the press resumes it instead. (Decided
2026-08-19; before this, press started a story with no question and only
the hold asked — two rules for one button.)

## The menu

```
greeting          only the FIRST menu of the boot (power-on greets; later
                  menus go straight to the ask — fast beats charming twice)
ask               the clip that names exactly the parent-enabled modes
                  the toy can HONOR here (offline: no riddle/curiosity)
answering         voice (press-to-talk, online only)
                  GREEN = yes / story
                  RED   = no / next suggestion
silence ~10 s     Areg suggests ONE story by name: «Want to hear X?»
refused / silent  next suggestion; stories first, then a game
~60 s total       soft close → IDLE  (the toy never talks to an empty room
                  for minutes; goodbye clip pending render — quiet for now)
```

The 60-second budget applies to CHOOSING only. Once a story or game starts,
it runs to its own end.

At power-on the same flow runs with one asymmetry kept from before: silence
closes quietly and early, because nothing at power-on proves a child is in
the room. After a press, silence means "didn't answer", and the toy suggests
instead of going dead — going dead after asking a question is the defect
firmware 1.3.3 fixed.

## What each mode really does

| Child asks for | Online | Offline |
|---|---|---|
| Story | offer by name → play from SD | same (SD needs no network) |
| Game | offline games engine (mind-reader / buzzer / Simon, rotating) | same — decided 2026-08-19, replaces the online chat route |
| Riddle | online chat session (server-side engine) | not offered — the ask clip must not name it |
| Curiosity | online chat session | not offered |
| Calm / bedtime cue | a story, never a menu | same |

Never offer a mode the toy cannot honor RIGHT NOW: parent-disabled modes
are already dropped from the ask clip; offline drops riddle/curiosity too.

## During a story

```
MAIN press        barge-in → hold+speak = ask a question, tap = sticky pause
GREEN             next story (browse — does not mark anything heard)
RED               previous story
volume knob       live, capped at gain 1.0 (above that is clipping, not louder)
```

## After any activity

```
story end → summary clip → ONE reflection question (rotating per listen)
          → listen → reaction + takeaway → the menu, same rules as above
game end  → the menu
menu-after-activity is capped by the same chain limit so two silent menus
in a row end in IDLE, not a loop
```

## Always-on gates (unchanged, order matters)

```
parent pause > bedtime window > per-mode flags
```

Paused: fully silent. Bedtime: press = music (if opted in) or nothing; the
menu NEVER opens at bedtime. Mode flags: enforced in the ask clip on the
toy and re-checked server-side on every turn.

## Open items from this decision round

- Soft-goodbye clip: not rendered yet; the 60 s close is silent until then.
- A "want to play a game?" offer clip: not rendered; offline, the game is
  offered last, after the story suggestions, by starting its own intro.
- The 70 per-story clips and the whole menu flow still need the standing
  human listen test.
