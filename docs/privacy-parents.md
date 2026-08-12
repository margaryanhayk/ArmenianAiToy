# What Areg records, and how long it keeps it

*A page for parents. Written to be read, not to be agreed to.*

Areg is a toy that tells stories in Armenian. To answer a child's question it
has to hear it, and to let you check what your child heard it has to keep a
record. This page says exactly what that means, in the plainest terms we can
manage.

---

## What is recorded

**Only what happens during a conversation with the toy.** There is no
always-on listening. The toy records when the child presses the button, and
stops when they finish speaking.

When your child speaks to Areg, four things are stored:

- **What the child said**, as text.
- **What Areg said back**, as text.
- **The recordings themselves** — your child's voice, and Areg's spoken reply.
- **When it happened**, and which toy and which child profile it belongs to.

Most of what the toy does is not recorded at all, because it never leaves the
toy: the stories play from a card inside it, and the greetings, games and
bedtime lines are pre-recorded audio. Nothing about those reaches us.

## What is never recorded

- No location. The toy has no GPS and we do not derive one from its network
  address. The one thing that comes close is a **time zone** — you set it so
  that "quiet hours" mean the right hours where you live. It is a setting you
  choose, not a position we track, and it is stored on the toy's record where
  you can see it in your export.
- No contacts, photos, or anything else from your phone.
- No advertising identifiers. Nothing about your child is sold, shared with
  advertisers, or used to build a profile.
- Nothing is used to train an AI model.

## How long it is kept

**Conversations and recordings are deleted automatically after 90 days.**

This is the shipped default (`Retention:Messages:MaxAgeDays`). If the person
running your Areg service has changed it, the real number is shown in your own
data export, in the `dataRetention` section — that section reads the same
setting the deletion job uses, so it cannot quietly disagree with what actually
happens.

Deletion is permanent. It removes the text and the audio together.

## What you can do about it

From the parent dashboard, at any time:

- **See everything.** Every conversation, in full, and every message Areg
  flagged as needing a look.
- **Listen to what Areg said.** Any of Areg's spoken replies can be played back.
- **Delete a single conversation.** It goes immediately — the messages and the
  audio recordings with it — not in 90 days.
- **Download everything.** One file with your account, your toys, your
  children's profiles, every conversation, and the log of every action you have
  taken on the account.
- **Delete your account.** Everything belonging to you goes with it.
- **Pause the toy**, or set quiet hours, or switch off any of the play modes.

### One honest limitation

Your child's own recordings are kept for the 90 days and are **not currently
downloadable one by one** — the export tells you they exist and when, but does
not hand you the audio files. Areg's replies *are* playable. This is a gap in
what we have built, not a policy: the recordings are yours, and a download for
them is on the list.

## Who can see it

**Only the parents linked to that toy.** At most two accounts can be linked to
one toy. If a second parent joins, the parents already linked are emailed to
tell them it happened.

Every sensitive action on your account — a password change, a deletion, an
export, a toy unlinked — is written to a log you can read yourself in the
dashboard, under "Your activity".

Our own operators can reach conversation records for support and safety
investigation. Every such access writes a record naming the operator, what they
opened, and when.

## Safety checking

Everything a child says and everything Areg says is checked by an automated
safety filter, in both directions, before it is spoken. If the check cannot be
performed, Areg says something safe and pre-written instead of guessing. That
checking is why messages carry a flag you can see in the dashboard.

## If something here is wrong

Tell us. A privacy page that does not match what the software does is worse
than none, and we would rather fix either the page or the software.

---

*Last updated 2026-08-12. The retention period on this page is read from the
same configuration the automatic deletion uses; if you find it disagrees with
your export, that is a bug and we want to hear about it.*
