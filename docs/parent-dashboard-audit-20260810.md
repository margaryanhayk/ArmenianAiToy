# The parent dashboard has every feature and no shape

**10 August 2026.** Measured on the running page, not read off the source.
Method and harness: `tools/dashboard-audit/`. Visual proposal:
the design review artifact published the same day.

---

## The coverage answer

**45 of 45 parent-facing endpoints are reachable from `parent.html`.** Nothing
is missing. Derived by extracting all 85 routes from the controllers, filtering
to the parent-facing set, and diffing against every `fetch()` and `authedFetch()`
in the page.

| Area | Endpoints | Reachable |
|---|---|---|
| Sign in, Google, password reset, e-mail verification | 10 | all |
| Account: change password, export, delete | 3 | all |
| Toys: claim, rename, unlink, revoke, pause/resume | 7 | all |
| Toy settings: bedtime, modes, intro, pauses, endings, music | 6 | all |
| Children: add, remove, per-child mode overrides | 4 | all |
| Conversations: list, mode filter, today, detail, delete | 6 | all |
| Story plays, reflection answers, flagged messages | 3 | all |
| Story library, music, assistant-audio replay | 5 | all |
| Audit feed, story requests | 3 | all |

The story and music **preview** URLs are server-supplied (`previewUrl` on the
DTO), which is why a naive grep for `/stories/{id}/audio` in the page finds
nothing — the affordance is built from the field, not from a literal.

Runtime health at 390 px, logged in, with two toys, two children, four
conversations and a flagged message: **zero JavaScript errors, zero horizontal
overflow, zero dead controls.**

## Three stale claims in CLAUDE.md, found while checking

- The tab strip is documented as eight tabs (Conversations / Stories / Games /
  Riddles / Questions / Bedtime / Flagged / Story plays). It is **three tabs
  plus a mode-filter select** — already refactored, never written down.
- The test count reads 2509; it is **2522** at HEAD.
- The batch notes stop at 2026-08-07 and 26 commits have landed since. The file
  says so itself, so this is a reminder rather than a correction.

## The findings that matter

**1. One screen is doing four jobs.** The single-toy view is **2,859 px tall**
with **21 buttons and 15 form fields**. It holds the week's summary, tonight's
talking point, five navigation cards, a settings block per child, ten device
settings and two destructive actions. Seeing what a child heard and changing
bedtime are different visits; they should be different screens.

**2. Nine Save buttons, four control idioms, one screen.** Bedtime saves;
modes save; each child's overrides save; the name saves — while pause,
story-intro, story-pauses and variant-endings apply on tap. A parent cannot
infer a rule from that, so they re-check everything.

**3. The library does not look like books.** Each story renders as
`Author:` · `About:` · `What it teaches:` · `Listened: 12 · to the end: 9` — a
record with its field names still attached. For a storytelling product this is
the highest-value visual change available, and it needs no backend work.

**4. Timestamps are machine-formatted.** `10/08/2026, 15:58:10`. Seconds, on a
parent dashboard. The data supports "Today, 15:58" already.

**5. The Today panel duplicates the list beneath it.** The newest three
conversations appear twice, in two different visual styles, one under the other.

**6. "Messages: 22" is the wrong unit.** A parent wants minutes, or "a whole
story, to the end". The mode chips are the one place the current design already
speaks a parent's language.

**7. No visual hierarchy.** Every element is a rounded lavender box of the same
weight — summary, conversation, pager, settings, and the block that unlinks a
toy forever (labelled "CAREFUL ZONE", which undersells it).

## The one functional gap

**Music is built and dark.** A toggle, a tab, a preview player and a whole
ContentSync namespace exist; `ContentSync:Music` is empty, so the screen is
permanently blank and the toggle permanently disabled. Either configure two
rights-cleared tracks or hide the tab. An empty screen behind a live switch
teaches a parent that the product is broken.

## Competitive read

Sources are search summaries — the App Store, Google Play, Medium and Yoto's
own site are blocked from this machine and were not read in full.

- **Tunik** (Armenian) — Tumanyan, Aghayan, Sasuntsi Davit, narrated in
  Armenian with pronunciation checked by native speakers, aimed at diaspora
  children. Self-described **"calm by design": no ads, no animation, no rewards
  or badges.** The same restraint Areg already writes into its own rules, over
  the same literature. Areg's differentiator is that it answers a question
  mid-story; Tunik plays.
- **Nanik** — personalised bedtime stories in 100+ languages including
  Armenian, narrated in a **clone of the parent's own voice from ~10 seconds**
  of recording, with the child's toy photographed into the illustrated hero.
  Two consequences: it sets the expectation that a story has a picture, and it
  shows a competitor already shipping cloned narration — with a consent problem
  that is trivial because the voice is the customer's own.
- **Yoto** — the closest product shape: a screen-free player with a parent
  companion app. Day and night volume limits, OK-to-wake, bedtime cut-offs,
  content preview, remote library management. Framed throughout as the calm
  setup surface for a device the child operates alone. Areg's dashboard
  currently frames the parent as a device administrator instead.
- **Tonies** — Toniebox 2 shipped this year and the marketing is about
  independence, family routine and sleep, not features.

Recurring principles in the design literature: five to seven navigation items
maximum; symbols recognised faster than labels; and a parent dashboard exists
to support a conversation, not to run surveillance. Areg's "Talk about it
tonight" card already understands the last one better than most products do —
it is buried partway down a long screen instead of leading.

## Proposed shape

Four screens, same 45 endpoints, no migration:

| Screen | Its one job |
|---|---|
| **Tonight** | What happened today, and what to say at bedtime |
| **Library** | What the toy can tell — stories with covers, the series, music |
| **Diary** | One timeline: conversations, saved answers, plays, flagged |
| **Settings** | Change one thing and leave |

Build order, so value lands before the irreversible move: human wording and
units → one switch idiom with no Save buttons → split the toy screen →
commission ten covers → bookshelf and merged diary → the four tabs and the
palette last.

**Open decision, not to drift into:** `parent.html` is one 6,620-line file with
17 screens and 155 element ids. That was right while it was small. At four tabs
and cover art, a change to one screen risks all seventeen.
