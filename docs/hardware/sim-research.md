# Online circuit-simulator research — shareable, phone-openable simulation links

Researched 2026-08-08 for the owner request "find an online simulator of circuit
designs and simulate if the circuit is working". Goal: a way to GENERATE
simulation links programmatically (from circuit text) that open on a phone with
zero install and zero account.

**Verdict up front:**

| Need | Tool | Why |
|---|---|---|
| Shareable schematic-level sims (dividers, RC, protection, switches) | **Falstad CircuitJS** (falstad.com/circuit) | Link IS the circuit (`?ctz=`), no account, loads on any phone browser, link generation fully reverse-engineered below and verified working |
| ESP32-S3 firmware/GPIO-level sim | **Wokwi** | Simulates ESP32-S3 + buttons/SD/WS2812, BUT **no I2S audio** (no INMP441, no MAX98357A) — see § Wokwi |
| Buck-boost power-stage behavior | **Falstad again** (idealized topology) | Every real alternative fails the phone-link test: Multisim Live shuts down 2026-09-15, EveryCircuit needs account+payment, WEBENCH needs myTI login and isn't an interactive sim — see § Power sims |

---

## 1. Falstad CircuitJS — link generation (SOLVED, verified)

### 1.1 The two URL parameters

Both are handled in `CirSim.java` of the official source
(github.com/pfalstad/circuitjs1, the code running on falstad.com):

- **`?ctz=<compressed>`** — the "Export as URL" mechanism. Decoded with
  `LZString.decompressFromEncodedURIComponent(ctz)` (CirSim.java:138-192).
  Encoded by the app's export dialog with
  `LZString.compressToEncodedURIComponent(dump)` (ExportAsUrlDialog.java).
  **The compression is the standard `lz-string` library by pieroxy** — an
  LZ78/LZW-family algorithm whose `compressToEncodedURIComponent` variant
  emits 6-bit codes mapped onto the URI-safe 65-char alphabet
  `A-Za-z0-9 + - $` (no `=` padding). Ports exist for every language we use:
  - JS/Node: `npm i lz-string` → `compressToEncodedURIComponent(text)`
  - Python: `pip install lzstring` → `LZString().compressToEncodedURIComponent(text)`
  - C#/.NET: `LZStringCSharp` NuGet → `LZString.CompressToEncodedURIComponent(text)`

  **Do NOT percent-encode the result** — the app splits the query string raw
  and runs GWT `URL.decode` (JS `decodeURI`), which leaves `+ - $` untouched;
  that is exactly why lz-string's URI alphabet was chosen.

- **`?cct=<url-encoded plain text>`** — the simpler no-compression alternative.
  CirSim.java:187: `startCircuitText = cct.replace("%24", "$")` after
  `decodeURI`. So: take the circuit text, `encodeURIComponent` it (newline →
  `%0A`, space → `%20`, `$` → `%24`), done. Note `decodeURI` does NOT decode
  `%24` — the app special-cases it, which is why `$` must be `%24` and why
  other reserved characters should be avoided in element text fields. Fine for
  short circuits; `ctz` compresses ~3-4x and is what the site itself exports.

The export dialog warns above 2000 URL chars (old-browser limit); modern
mobile browsers handle far longer, but keep demo circuits small anyway.

### 1.2 Recipe (3 lines, verified working)

```js
import lz from 'lz-string';
const url = "https://www.falstad.com/circuit/circuitjs.html?ctz="
          + lz.compressToEncodedURIComponent(circuitText);
```

### 1.3 Verified example

Circuit text (5 V battery → 1 kΩ resistor → ground):

```
$ 1 0.000005 10.20027730826997 50 5 43 5e-11
v 176 240 176 80 0 0 40 5 0 0 0.5
r 176 80 384 80 0 1000
w 384 80 384 240 0
w 176 240 384 240 0
g 288 240 288 272 0
```

Generated link (roundtrip `compress → decompress` verified byte-identical
locally with lz-string 1.5; URL fetched 2026-08-08 → HTTP 200 returning the
CircuitJS GWT app HTML — the circuit itself renders client-side, so a fetch
can only prove the app page loads, which it does):

```
https://www.falstad.com/circuit/circuitjs.html?ctz=CQAgjCAMB0l3BWcMBMcUHYMGZIA4UA2ATmIxAUgpABZsKBTAWjDACgA3cDQkFGqmB4g8VMbSpJxMBGwBO3XqJDY8NEeLDw2AdxVqN+9fzG7FfAUYumA5nzx5r9x5hRQ2QA
```

The same circuit as a plain `cct` link (also usable, no library needed):

```
https://www.falstad.com/circuit/circuitjs.html?cct=%24%201%200.000005%2010.20027730826997%2050%205%2043%205e-11%0Av%20176%20240%20176%2080%200%200%2040%205%200%200%200.5%0Ar%20176%2080%20384%2080%200%201000%0Aw%20384%2080%20384%20240%200%0Aw%20176%20240%20384%20240%200%0Ag%20288%20240%20288%20272%200%0A
```

Do not use `circuitjs.com` — `circuitjs.com/circuitjs.html` returned 404;
`falstad.com/circuit/circuitjs.html` is the canonical host.

### 1.4 Circuit text format (from the element sources, master branch)

First line = simulator options (`CirSim.dumpOptions()`):

```
$ <flags> <maxTimeStep> <iterCount> <currentBarValue> <voltageRange> <powerBarValue> [minTimeStep]
```

A known-good default: `$ 1 0.000005 10.20027730826997 50 5 43 5e-11`
(flags bit 1 = show current dots; voltageRange 5 V).

Every element line: `<type> <x1> <y1> <x2> <y2> <flags> <params...>`.
Coordinates are pixels on a 16-px grid (use multiples of 16). Elements
connect by exact endpoint coordinates — no explicit nets. All element
constructors parse params with try/catch, so **trailing params are optional**
(older/shorter dumps load fine).

| Element | Type | Params after flags | Notes |
|---|---|---|---|
| Resistor | `r` | `resistance` | `r 176 80 384 80 0 1000` = 1 kΩ |
| Capacitor | `c` | `capacitance voltdiff [initialVoltage seriesResistance]` | `c 320 160 320 240 0 1e-6 0` = 1 µF |
| Inductor | `l` | `inductance current [initialCurrent saturationCurrent]` | `l 176 80 320 80 0 0.000047 0` = 47 µH |
| Wire | `w` | — | `w 384 80 384 240 0` |
| Ground | `g` | `[symbolType]` | `g 288 240 288 272 0` |
| Voltage source (2-terminal battery) | `v` | `waveform frequency maxVoltage bias phaseShift dutyCycle` | waveform 0=DC, 1=AC, 2=square, 3=triangle, 4=saw, 5=pulse. DC 5 V: `v 176 240 176 80 0 0 40 5 0 0 0.5`. **Second point (x2,y2) is the + terminal** |
| Voltage rail (1-terminal supply) | `R` | same as `v` | `R 176 80 176 32 0 0 40 5 0 0 0.5` = 5 V rail |
| Current source | `i` | `currentValue [maxVoltage]` | |
| Switch (SPST) | `s` | `position momentary` | position 0=closed 1=open: `s 176 80 288 80 0 1 false` |
| Diode | `d` | flags=2: `modelName`; flags=0/1: `[fwdrop]` | simplest: `d 176 80 288 80 2 default` |
| Zener (≈TVS clamp) | `z` | flags=0: `zvoltage`; flags=1: `fwdrop zvoltage`; flags=2: `modelName` | `z 176 80 288 80 1 0.805904783 5.6` = 5.6 V zener. **Custom breakdown WITHOUT a model line via the legacy flags=0/1 form** — verified in `ZenerElm` constructor |
| Diode model definition (only for custom models) | `34` | `name flags satCurrent seriesResistance emissionCoeff breakdownVoltage forwardCurrent` | not needed when using `default` / `default-zener` / legacy zener form |
| Voltmeter/probe | `p` | `meter scale [resistance]` | meter 0=voltage, 1=RMS, 6=frequency…; `p 384 160 384 240 0 0 0` |
| MOSFET | `f` | `vt [beta ...]` | flags bit 1 = p-channel. Newest builds may append a model name — for MOSFETs, draw once in the editor and copy the dump rather than hand-writing |
| Text label | `x` | `size escapedText` | |

To place a scope on an element, `o` lines at the end reference the element's
zero-based line index — brittle when generating; prefer `p` probe elements,
or let the viewer right-click → View in Scope on the phone.

Ground rule: every circuit needs at least one `g` ground (or the sim reports
"no ground" but usually still solves with a floating reference — include one).

Sanity path for any new element type: draw it once at falstad.com → File →
Export as Text, and read the dump. That is the authoritative format for the
deployed build.

---

## 2. Wokwi — ESP32-S3 verdict

- **ESP32-S3: YES.** Wokwi simulates ESP32, ESP32-S2, **ESP32-S3**, ESP32-C3,
  C5/C6/H2/P4 and runs real compiled firmware (Arduino / ESP-IDF /
  MicroPython).
- **I2S audio: NO.** The official peripheral matrix lists I2S as only
  partially implemented and audio explicitly unsupported. There is **no
  INMP441 microphone part and no MAX98357A amplifier part** in the built-in
  parts library (user projects titled "INMP441" rely on the Custom Chips API
  with community-written chip models — not a faithful audio path; no sound
  out). A buzzer part exists (tone only).
- **What it CAN simulate for Areg:** the button (pushbutton part — including
  the GPIO0-strapping question at boot), WS2812 LED, **microSD card over
  SPI**, Wi-Fi (the sim has a virtual internet gateway — the toy's HTTP
  calls to a backend can actually run), UART logs. So Wokwi is useful for
  firmware-logic bring-up (story_select, content_sync against a mock server,
  button/LED state machine), NOT for the audio path.
- **Project mechanics:** a project = `diagram.json` (parts + wiring) +
  sketch/source + optional `libraries.txt`. Created in the browser editor;
  saving mints a unique `wokwi.com/projects/<id>` URL. **Anonymous save works
  once per project** (no account needed to create a link; an account is
  needed to edit it later); **viewers need no account** — anyone opening the
  link can run the sim and "save a copy". There is no public HTTP API to
  create projects programmatically and no way to embed `diagram.json` in a
  URL; programmatic use = the Wokwi VS Code extension / `wokwi-cli` running
  from local files (licensed, local — not a shareable cloud link).

---

## 3. Power-electronics sims for the buck-boost (one paragraph each)

- **Falstad CircuitJS (recommended for this purpose too).** It has switches,
  MOSFETs, comparators, inductors and scopes, and the built-in examples
  include buck and boost converters. An idealized buck-boost (switch +
  diode + L + C, or synchronous with two MOSFETs and a square-wave gate
  drive) demonstrates topology-level "is this circuit working" — inductor
  current, output ripple, duty-cycle math — in the same zero-friction
  phone link as everything else. What it can NOT tell you: real-part
  efficiency, thermal, transient response of a specific controller IC
  (e.g. the TPS63xxx class). For that, the honest tool is the vendor's own
  design tool, offline.
- **NI/Digilent Multisim Live — DISQUALIFIED.** Real SPICE in the browser
  and shareable circuit links, but a Digilent account has been mandatory
  since 2023, and NI has announced the service **shuts down 2026-09-15** —
  five weeks from now. Do not build anything on it.
- **EveryCircuit — impractical.** Beautiful interactive sims and share
  links, and it does run in a browser (Chrome/WebGL) — but it requires
  signing in with an account and the full version is paid; a cold phone
  link hits a login wall. Fails the "owner taps a link" test.
- **TI WEBENCH Power Designer — different animal.** Free with a myTI login;
  it *generates* a reviewed buck-boost design (schematic, BOM, efficiency
  and thermal curves) around a real TI part and can run electrical/startup
  sims on it. Genuinely valuable when we pick the production regulator —
  but it is a desktop-oriented design generator, not an interactive circuit
  you can author and share as a link. Use it at part-selection time, not
  for "show the owner the circuit works on his phone".

---

## 4. Sources

- `github.com/pfalstad/circuitjs1` — `ExportAsUrlDialog.java` (compress),
  `CirSim.java:138-192` (ctz/cct decode), `QueryParameters.java` (raw query
  split + `URL.decode`), element sources for every dump format in the table
  (`ResistorElm`, `CapacitorElm`, `InductorElm`, `VoltageElm`, `RailElm`,
  `SwitchElm`, `DiodeElm`, `ZenerElm`, `DiodeModel`, `ProbeElm`,
  `MosfetElm`, `TextElm`, `GroundElm`, `WireElm`, `CurrentElm`).
- lz-string: pieroxy.net/blog/pages/lz-string/index.html (npm `lz-string`,
  PyPI `lzstring`, NuGet `LZStringCSharp`).
- docs.wokwi.com — `/guides/esp32` (chip + peripheral matrix),
  `/getting-started/supported-hardware` (parts list),
  wokwi/wokwi-features#794 (anonymous save-once behavior).
- multisim.com (account requirement; 2026-09-15 shutdown notice),
  everycircuit.com (browser app, account sign-in).
- Verification runs 2026-08-08: lz-string roundtrip byte-identical; ctz and
  cct URLs → HTTP 200 serving the CircuitJS app HTML (client-side render —
  fetch cannot prove more; visually confirm once from a phone).
