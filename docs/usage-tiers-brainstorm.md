# Usage tiers — thinking, not a spec

*Written 2026-08-12, after measuring what a child's question actually costs.*

> **DECIDED, INTERIM — shape A is now live, and MUST be revisited before
> production** (owner, 2026-08-12). One limit for everybody: 30 questions per
> child per day, cap `$0.25`, counter fixed so the number is true, no tiers,
> nothing gated. The owner's words were "do as you suggest, but need this to
> change before production" — so this document is not archived. It is the
> open question, and shipping to real families is the deadline for answering
> it. Sections 4, 5 and 7 are what that answer needs.

Companion to `docs/paid-tier-plan.md`, which asks a different question. That
one is about **content** — should new story packs cost money. This one is about
**usage** — how many questions a child may ask. They are different axes and
they want different answers, which is exactly why they are two documents.

---

## 1. The one fact everything else follows from

**The AI question is the only thing in this product with a per-use cost.**

Stories, greetings, offline games, bedtime music and the story-pause lines are
all pre-rendered files on an SD card. A child can replay them ten thousand
times and it costs nothing — no network, no API, no bill. Only three things
touch a paid service: a question asked during a story, an answer to a
reflection question, and the welcome flow listening to a child's choice (which
pays for speech-to-text only, since the intent is matched by keyword rather
than by a model).

So metering questions is not an arbitrary line. It is the only place in the
product where what a child does and what it costs are actually connected.

## 2. The arithmetic

One in-story question costs **$0.0078** — two thirds of it the grounding prompt
sent with the question, which is what keeps the answer inside the story. Full
breakdown and method:
`tools/quality-evidence/cost-per-hour-of-play-20260812.md`.

| questions per day | per day | **per month** |
|---|---|---|
| 5 | $0.04 | **$1.17** |
| 10 | $0.08 | **$2.34** |
| 20 | $0.16 | **$4.68** |
| 30 | $0.23 | **$7.02** |
| 50 | $0.39 | **$11.70** |
| 100 | $0.78 | **$23.40** |
| **~191 — what is allowed today** | **$1.49** | **$44.69** |

Against that, `docs/hardware/bom.md`: electronics are **≈$26.6 at 50 units,
≈$16.7 at 5,000**, and a finished toy is ≈$78 / $36 / $17 at 50 / 500 / 5,000.

**One heavy user, one month, can cost more than the toy is made of.** That is
true today, in production, and it is independent of the counting bug — the
counter under-reports, so the shipped `$0.50` cap actually stops at about
$1.49/day. Fixing the counter without choosing a limit just moves $45 to $15.

Two things make this less alarming than it looks, and neither makes it safe:
the daily counter is process-local and resets on restart (so the real ceiling
is fuzzy, in both directions), and no real child has ever asked 191 questions
in a day. The exposure is a stuck button or a curious eight-year-old, not the
average case.

## 3. What may be metered, and what may never be

Inherited unchanged from `paid-tier-plan.md`, because a product that charges
for safety is not this product:

**Never, at any tier, for any price:**

- Pause, bedtime window, per-mode and per-child switches. A parent must never
  pay to make the toy quieter or safer.
- The diary — conversations, flagged messages, what the child said.
- Data export and deletion. Legal in the EU, and indefensible otherwise.
- The activity log, unlink, device revoke.

**And one this document adds:**

- **Stories must never be metered.** They cost nothing per play, they are
  already on the card, and the child owns them. A story that stops working
  because a month ended is a broken toy, not a business model. This also
  settles what a lapsed subscription does: the card keeps playing.

**What is genuinely meterable:** the question. That is the list.

## 4. Candidate shapes

Each with the number that makes it work and the way it fails.

### A. No tiers. One honest limit.
Set the cap where the economics work — say 20–30 questions a day, $4.70–$7.00
a month — and give it to everybody. Fix the counter so the number is true.

*Why it might be right:* it is the only shape that needs **nothing built**. No
entitlement, no billing, no per-account state. It could ship this week.
*How it fails:* the family whose child genuinely asks forty questions a day is
the family who loves the product most, and they hit a wall with no way past it.

### B. Free tier + paid tier. The owner's instinct.
Free: ~10/day ($2.34/mo). Paid: ~50–100/day ($11.70–$23.40/mo), priced above
that.

*Why it might be right:* the heavy user is exactly the user willing to pay, and
the cost scales with the revenue.
*How it fails:* ten a day is thin. A four-year-old on a rainy afternoon can
spend ten questions in one story, and then the toy is deaf for the rest of the
day. If the free tier feels broken, the paid tier reads as a hostage payment
rather than an upgrade — the worst possible frame for a children's product.
**If this shape is chosen, the free number is the whole decision**, and it
should be set from watching real children, not from a spreadsheet.

### C. Monthly pool instead of a daily limit.
300/month rather than 10/day. Same money, but a child can have one enormous
afternoon and a quiet fortnight.

*Why it might be right:* it matches how children actually behave — bursty, not
uniform — and it removes the "the toy went deaf at 4pm" failure entirely.
*How it fails:* it cannot be built today. The cost meter is in-process and
resets on restart, so a monthly quota needs real persistence. It is also harder
to explain, and a parent who exhausts the month on the 3rd is worse off than
one who hits a daily wall.

### D. Tier by what is enabled, not by how much.
Everyone gets the same generous question limit; tiers differ by content —
story packs, the serial, custom stories.

*Why it might be right:* it is what `paid-tier-plan.md` already argues for, it
never degrades an experience a child is in the middle of, and content is the
thing that costs real money to *produce*.
*How it fails:* it leaves the per-use cost unbounded, so it must be paired with
one honest limit anyway. In practice this is **A + content packs**, and that
may be the real answer.

## 5. The constraint that should decide it

**A child cannot be told about a quota.**

Today, when a device is over its cap, the toy answers questions with the
*paused* line. A four-year-old does not know what a quota is, cannot ask a
parent to buy more, and experiences it as the toy having stopped loving them.
Stories keep playing, which helps a great deal — but the question is what makes
Areg feel alive.

So whatever shape is chosen, running out has to be something a child can
accept. That probably means:

- The toy never says a number, a price, or the word "limit".
- The parent is told, in the app, before the child hits it — not after.
- What remains when it runs out has to be enough to still be a toy. It is:
  ten stories, five games, the serial, music, the whole welcome flow.

That constraint alone may rule out shape B at ten a day, and it costs nothing
to test on a real child before committing.

## 6. What would have to be built first

In order, honestly:

1. **Fix the counter** so the number means what it says. Small, and every
   shape needs it.
2. **Per-account entitlement.** The same missing slice `paid-tier-plan.md`
   identified: ContentSync serves static config identical for every device, and
   there is nowhere to record what an account is entitled to. Entitlement must
   attach to the *account*, not the toy — two parents can share one toy.
3. **Durable usage counting.** The meter is in-memory and per-instance. A daily
   cap survives that badly; a monthly pool not at all.
4. **The parent-facing surface.** Where usage is shown, how a limit is
   explained, what warns before it hits.
5. **Only then** payment, webhooks, refunds, failed renewals, tax.

Steps 1 and 3 are worth doing whatever is decided, because they are honesty
about spending. Steps 2 and 5 are weeks and should wait for evidence that
anyone wants to pay.

## 7. What only the owner can answer

1. **Is the toy sold once, or subscribed?** A one-off sale with unlimited
   questions is a business that loses money on its best customers. This is the
   question everything else hangs on.
2. **What should a family pay per month, if anything?** The tier numbers fall
   out of that, not the other way round.
3. **Is the free tier a trial or a permanent floor?** A toy someone bought
   should probably still be a toy forever, which argues for a floor.
4. **Armenia or the diaspora?** `paid-tier-plan.md` already flags that one USD
   price serves neither market well.

## 8. Recommendation — taken, as an interim

**Shape A now, shape D later.** Adopted 2026-08-12.

One honest limit around 30 questions a day (~$7/month worst case), the counter
fixed so it is true, no tiers, no billing, nothing gated. It ships without
building anything, it cannot make a child feel punished for being curious, and
it caps the exposure at something a hardware margin can carry. Then, when real
families have used it for a season, sell *content* — which is what the other
document already argues, and which never takes anything away from a child
mid-story.

Tiers on usage stay available as shape B, but the number that makes them
humane can only be learned from watching children, and no child has used this
toy yet.
