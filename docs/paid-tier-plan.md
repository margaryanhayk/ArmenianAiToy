# What Areg would charge for, and what it never will

**Status: a decision record, not a build.** Nothing here is implemented and
nothing should be until the toy is in families' hands. Written 2026-08-11 at
the owner's request ("yes, write the plan").

---

## How this decision was reached

The first instinct, during the dashboard redesign, was to lock the four
switched-off features behind a subscription so the app looked fuller. Three
facts changed it, and they are worth keeping written down because the same
temptation will come back:

1. **Those features are broken, not withheld.** Mid-story pauses, variant
   endings, the button games and bedtime music are all gated off in firmware
   or have no content configured. A paywall over them would be selling
   something that does not work — the exact failure this project spent a week
   correcting elsewhere (three stories shipped truncated behind a quality gate
   that was never run).
2. **The decision was already taken.** `CLAUDE.md` line 2179, from the
   2026-08-03 owner batch: *"content is paid, controls stay free."* This
   document is that sentence worked out in full, not a new direction.
3. **There is no billing surface at all.** A grep for `stripe|billing|
   subscri|payment` across `backend/src` returns four hits and not one of them
   is commerce: two are the word "payment" inside the plot summary of «Անբան
   Հուռին» (the frogs), one is a metrics comment about subscribing to a meter,
   and one is an email footer explaining why a transactional mail carries no
   unsubscribe link. There is no payment code, no tier column, no entitlement
   check anywhere in the product.

The owner's revised instruction — *"in production we will not have something
broken, so make it coming soon"* — is now shipped: the four unfinished
features appear on the "What Areg can do" screen under a **Coming soon**
heading, honestly labelled, with nothing charged for them.

---

## The line

**Everything that keeps a child safe or a parent in control is free forever.**

| Free, permanently | Why it can never be paid |
|---|---|
| Bedtime window, pause, per-mode and per-child switches | A parent must never pay to make the toy quieter or safer. This is the whole trust argument of the product. |
| The diary — conversations, flagged messages, what the child said | A parent has a right to know what their child was told. Charging for that is charging for oversight. |
| Data export and deletion | Legal in the EU (GDPR Art. 15/20), and it would be indefensible anyway. |
| The activity log, unlink, device revoke | Security controls. A lost toy must be killable by anyone who owns it. |
| The eight stories on the card, the greetings, the games already rendered | What was in the box stays in the box. See "if a subscription lapses" below. |

**What could carry a price is content the child has not got yet**, and only
that:

| Candidate | Note |
|---|---|
| New story packs | The natural one. Each is real work: text, review, narration, listen test. |
| Seasonal / holiday stories | Same shape, smaller. |
| **A custom story about the child** | The only item with a genuine per-order cost — a person writes and records it. `POST /api/parents/story-requests` and the operator queue in `admin.html` already exist and are human-fulfilled today, for free. |
| Music packs | Zero tracks are configured today, so this is theoretical until the owner's tracks land. |

---

## Why building this now would be the wrong order

**The payment page is not the work. Entitlement is.**

ContentSync — the mechanism that puts stories, greetings, games and music on a
toy — serves **static config shared by every device**. `ContentSync:Stories`
is one array in `appsettings.json`, and every paired toy that asks for the
manifest gets the same answer. There is no per-device, per-parent or per-tier
filter anywhere in it, and CLAUDE.md records that omission deliberately in
three places (§ Cloud→SD content sync: *"per-device / per-tier entitlement is
still a later slice"*).

So a paid story pack needs, in order:

1. **Per-device entitlement on the manifest** — the manifest service resolves
   what *this* toy is allowed, not what is configured globally. This is the
   real slice, and it is the same gap that blocked personalised name-greetings
   (a v2 feature for exactly this reason).
2. A record of what an account has bought, surviving unlink and re-pairing —
   note a toy can be shared by two parents (`MaxParentsPerDevice = 2`), so
   entitlement belongs to the *account*, not the device.
3. A payment provider, its webhooks, refunds, failed renewals, tax.
4. Only then a purchase screen.

Steps 1–2 are weeks and touch the device wire protocol. Step 3 is a
compliance surface a one-person product should take on once, deliberately.
`docs/v2-backlog.md` already says this: *"No billing, no tiers, no entitlement
checks… build features ungated for now."* That remains the right call.

---

## When to revisit

Not on a date — on evidence:

- Real families are using the toy daily and asking for more stories.
- The eight-story library is genuinely exhausted (the story-plays data already
  in the dashboard will show this without asking anyone).
- The narrator is settled, so new stories can actually be produced on demand.
  Today they cannot: every story is in a temporary voice awaiting the
  storyteller decision, so there is no supply to sell.

That last one is decisive. **Selling a story pack requires being able to make
story packs**, and the production pipeline is paused pending a voice.

---

## Open questions for the owner

None of these need answering now; they are what the build would ask.

1. **One-off or subscription?** Story packs suit one-off purchase (buy it, keep
   it). A subscription suits a steady stream of new content — which requires a
   steady producer of it.
2. **Price and currency.** Armenia and the diaspora are different markets with
   different expectations; a single USD price serves neither well.
3. **Family sharing.** Two parents can hold one toy. A purchase by either
   should reach the child — anything else produces a support request the first
   week.
4. **If a subscription lapses, what happens to content already on the card?**
   Recommendation: **it keeps working.** Reaching into a child's toy to remove
   a story they know is not a thing this product should do, and the toy plays
   from SD offline anyway — enforcing it would mean building a revocation path
   that does not exist and should not.
5. **Is the custom story a product or a courtesy?** It is the one item with a
   real marginal cost. It may be better as a paid extra than as a tier, and it
   is the only candidate that could ship *without* the entitlement slice above,
   since fulfilment is already manual.

---

## What this document does not do

It does not add a price, a tier name, a purchase flow, a `IsPremium` column or
a feature flag. Nothing in the code changed. If a future session finds a
paywall in the dashboard, it did not come from here.
