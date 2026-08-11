# Driving the parent dashboard without a backend

`parent.html` is 6,620 lines and 17 screens. Checking a change to one of them
has meant running the whole .NET stack, registering a parent, pairing a toy and
producing conversations — which is enough friction that in practice nobody
looks at the other sixteen.

This is the short way: a mock server that serves the **real** `wwwroot` and
answers every parent endpoint with realistic Armenian data, plus a browser
walkthrough that photographs each screen and counts what is on it.

It does **not** replace the backend tests. It proves the screens render,
navigate and fit; it proves nothing about what the server actually does. The
mock always succeeds, so an error state you want to see has to be mocked
deliberately.

## Run it

```bash
node tools/dashboard-audit/mock-server.js &     # http://127.0.0.1:5099
node tools/dashboard-audit/walk.js              # writes shots/ and walk.log
```

**Writes persist for the life of the server process.** That is deliberate —
the controls save themselves and the page reloads after each save, so a mock
that forgot every write could not test them at all. The cost is that a check
which CREATES something (adding a child, renaming a toy) has already changed
the fixture by the time it finishes: run it twice against the same server and
the second run starts from a state the first run made. **Restart the server
between runs of anything that writes.**

`walk.js` needs Playwright and a Chromium. If they are not where node looks by
default, point at them:

```bash
PLAYWRIGHT_PATH=/path/to/playwright CHROME_PATH=/path/to/chrome \
  node tools/dashboard-audit/walk.js
```

Then just open <http://127.0.0.1:5099/parent.html> and log in with anything —
the mock accepts any credentials and returns a token.

## What the walkthrough reports

Per screen: which view is visible, the full page height, how many buttons and
fields are on it, every button's label, and whether the page overflows
horizontally. That last one is the check that is easy to lose on a phone-first
page and expensive to notice late.

It also fails loudly on a JavaScript error, which is the thing a screenshot
alone will not tell you.

## Keeping the mock honest

The response shapes are copied from the DTO records in
`backend/src/ArmenianAiToy.Application/DTOs/`, not invented. When a DTO gains a
field the dashboard reads, add it here too — a mock that silently lags the wire
shape will show you a screen no parent will ever see.

## What it found on 2026-08-10

Zero JavaScript errors, zero horizontal overflow at 390 px, zero dead controls,
and all 45 parent-facing endpoints reachable from the UI. The findings that
mattered were about shape, not function — chiefly that the single-toy screen is
**2,859 px tall with 21 buttons, 15 fields and nine separate Save buttons**.
Full write-up in `docs/parent-dashboard-audit-20260810.md`.
