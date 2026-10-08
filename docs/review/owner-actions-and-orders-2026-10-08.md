# Areg: your actions and what to order (8 Oct 2026)

## What you need to do

### This week (by 16 Oct)
1. Top up OpenAI today: about $50, with auto-recharge on. The balance ran out on 4 Oct during the bake-off. If production uses the same OpenAI account, voice chat on the toy fails until you top up.
2. Top up Gemini (about $20). Set budget alerts at 50/90/100 % on OpenAI and Google. Write down the ElevenLabs renewal date.
3. Decide all of these in one reply (15 min): repo private yes/no; the domain name and the registrar; one mailbox for security@ and support@; the second person who will hold the keys; Railway Pro (about $20/mo) and its region (move only after a backup); a fleet cap of $5/day.
4. Tell Claude "open the prep PR", then merge it (30 min).
5. Railway: check the volume is mounted at `/data` and the environment is not Development. Write down the region and the variable names. Never write down the values.
6. Do one Railway deploy with all of these: two operators plus TOTP, `RequireSession=true`, `ForwardedHeaders__Enabled=true`, a new provisioning secret and the $5 global cap. Then test it: a phone on mobile data gets locked out after 10 wrong logins, and the laptop can still log in.
7. Set up Telegram alerts and an UptimeRobot keyword monitor. Send one test alert.
8. Make the repo private. Redeploy and check that it still works.
9. Make a new JWT key, delete the old ones, and log in again.
10. Make a new OTA key and store it in the password manager only. Do not put it on Railway yet.
11. Change your home Wi-Fi password. Re-key the bench toy over USB (`provision_toy.py --rotate-existing`) and give it the new Wi-Fi. Check that the old key gets a 401. Revoke every other device row.
12. Buy the domain and add `api.<domain>` on Railway (DNS only). In the next deploy, set AllowedHosts.
13. Set up Resend on the domain (SPF, DKIM, DMARC). Send a password reset and a verification email to someone else's inbox and check both arrive.
14. Forward security@ and support@ to yourself. Tell Claude the address.
15. Turn on Railway volume backups. Set up the daily off-site backup pull on your Windows PC with healthchecks.io.
16. Set up the password manager with emergency access for your second key holder. Make an offline encrypted export and print the continuity page.
17. Order everything in "What to order" below (1 h).
18. Bake-off: open `tools/quality-evidence/model-bakeoff-20261004/blind-rating.html`, rate every reply, press Export and send Claude `blind-ratings.json` (40 min). Also export the OpenAI and Google bills for 4 Oct (15 min).
19. Send Claude the checklist: names, dates and ticket numbers. Never send values.

### Lock the chip (so nobody can read your codes or change the toy)
20. When Claude's chip-lock plan arrives, reply "go".
21. Make the firmware signing key on your own PC with Claude's command. Keep three copies: the password manager, a USB stick at home and a second USB stick with your second key holder. Never send the key to anyone, Claude included. If you lose it, locked toys can never be updated again. If it leaks, anyone can make firmware your toys will accept.
22. Spare board 1: run Claude's lock script and its checks. The board must boot and play a story, refuse an unsigned image, and show no keys or Wi-Fi password when read over USB. Send Claude the log.
23. Spare board 2: repeat step 22, then also install one update over Wi-Fi. Keep spare board 3 in reserve in case of a mistake.
24. Only after boards 1 and 2 both pass, lock unit #1. No toy leaves your house unlocked.

### Before your child uses it
25. Approve each HIGH plan when it arrives (about 30 min each): safety-input-pipeline, stop-danger-routing, distress-calm-topics, safety-output-guards, fw-135-build-gate, fw-135-child.
26. Open the System tab: all three speech-to-text rows must read `gpt-transcribe`. Delete any old override variable.
27. When Claude's decision sheet arrives:
    - switch Railway chat to OpenAI, the bake-off's safest and cheapest option;
    - set `OpenAI__DailyCostCap__Default=0.50` and `OpenAI__DailyCostCap__Global=5`;
    - do a 30-min listen test (10 typed and 5 spoken questions);
    - say "go" for the regression re-run;
    - two weeks later, delete the AI Studio key.
28. Answer Claude's decision sheets, one line each:
    - what Areg says to "are you alive / a robot / where do you live", and deleting "Do not say you are an AI";
    - your backup reviewer's name;
    - safety-input-pipeline (about 1 h);
    - stop-danger-routing (5 yes/no questions and 3 Armenian lines);
    - distress-calm-topics part A (about 20 lines and a 15-min ear check);
    - safety-output-guards (about 8 lines);
    - fw-135-child (error lines, bedtime defaults, a render under $1);
    - pilot decisions D1–D8.
29. Approve about $60–90 for the regression run, and put the keys in Claude's session environment.
30. fw-135-build-gate:
    - sign off "no per-toy Wi-Fi code means no Bluetooth setup";
    - set the base URL to `https://api.<domain>`;
    - paste `arduino-cli version`, `arduino-cli core list`, `arduino-cli lib list` and your config diff with the secrets removed.

    Until Claude's fix lands, build anything that has Bluetooth setup on core 3.3.6. Cores 3.3.7 and 3.3.8 crash when setup starts.
31. Build unit #1 (2 evenings), wired exactly like the bench toy: main button 18, YES 21, NO 47, LED 48, mic 4/5/6, amp 15/16/7, SD 10/11/12/13 (SD board on 5 V), knob 8. Never connect anything to GPIO0.
32. Fill in unit #1's check sheet and send the photos. All of these must pass:
    - the lid is closed with the security screws;
    - a 3 mm rod cannot get in anywhere (grille holes 3 mm or smaller, mic hole about 1 mm);
    - the SD card is inside and taped down;
    - no lead is loose (glue or solder every one);
    - the cable, the knob and the buttons each survive a 5 kg pull for 10 s (a 5 L water bottle);
    - after 1 h of stories with the lid shut, the outside is 40 °C or cooler;
    - after 3 drops from 75 cm, it is still closed and still boots;
    - the LED is visible in daylight;
    - you can tell YES from NO with your eyes closed.
33. Paste Claude's SHIP.md block (30 min).
34. Do the Railway staging restore rehearsal from Claude's sheet (one evening, 2–3 h).
35. Build 1.3.5 with Claude's release script. It must print PASS. Then run `ht-bench-135` (2 evenings).
36. Run `ht-listen-loudness` on unit #1 (4 sessions, about 7–8 h), with the meter on the tripod 50 cm from the speaker. Get an adult native Eastern Armenian listener (not you) to listen for 10 min and sign a note.
37. Run `ht-first-child` (about 3 h, plus 1.5 h that evening). On that day set `OpenAI__DailyCostCap__PerDeviceOverride__<toy id>=2.00`. Remove it the next day.

### Before other families
38. Book a child-protection adviser for a 2-h session on the disclosure protocol and the topics policy, and sign both. Then run a 30-min tabletop drill with your backup reviewer.
39. Approve and answer these plans:
    - flagged-reason-review: who reads the queue; the Armenian helpline name, number and hours; the support address;
    - auth-second-seat: its decisions, plus a staging check with a Google account;
    - toy-handover: its 8 decisions.
40. After privacy-data-lifecycle merges, set `Backup__PullPassphrase` and `StoryRequests__PhotoRoot=/data/story-requests`. Switch the off-site pull to `.db.enc`.
41. Approve regression PR-B. Then check the moderation row in the System tab.
42. Turn on the dormancy variables only after toy-handover PR2 and the real email are both live.
43. Approve the Stage-2 plans and answer their short sheets: chatservice-pilot-fixes, voice-turn-backend, armenian-text-clips, fw-pilot-connectivity (give it a factory Wi-Fi network, never your home one), fw-pilot-loop-controls, settings-tz-child, mobile-pilot, parent-web-pilot-pass.
44. mobile-pilot: generate and back up the Android upload key on your laptop. Set up the Android toolchain on your PC (2–4 h).
45. Do a 45-min rehearsal in production of "stop all AI".
46. ci-gates: confirm that every old secret the scan finds has been rotated. Turn on Railway "Wait for CI", branch protection and push protection.
47. After `ht-first-child` passes, build and lock units #2–#6 (one evening each, each with its own check sheet). Do not label them until Claude's new label design (D3) is done.
48. Run `ht-pilot-image` (2 days, about 8 h).
49. pilot-go-live: make the dated decision and say "go".

## What to order

Unit #1 parts come to about $60–80. The 3 practice boards add about $45. The meter is €100–300 (or borrow one). The tools add about $40–60 (skip any you already have).

| # | What | How many | ≈ Price | Link | Link type |
|---|---|---|---|---|---|
| 1 | ESP32-S3-DevKitC-1 **N8R8**, the same chip as the toy (ESP32-S3-WROOM-1-N8R8). Choose the N8R8 option in the listing | **4** (1 for unit #1 + 3 spares to practise the lock) | $12–22 each | https://www.aliexpress.com/item/1005003819366900.html · https://www.aliexpress.com/w/wholesale-esp32-s3-devkitc-1-n8r8.html | from repo doc · search link |
| 2 | INMP441 microphone board | 2 | $1.61 | https://www.aliexpress.com/item/32962426410.html | from repo doc |
| 3 | MAX98357A amplifier board | 2 | $1.85 | https://www.aliexpress.com/item/1005004840960248.html | from repo doc |
| 4 | microSD SPI board, the same type as in your bench toy (the one with its own regulator, on 5 V) | 2 | $1–2 | https://www.aliexpress.com/w/wholesale-micro-sd-card-module-spi.html | search link |
| 5 | microSD card, 8 GB industrial (SanDisk SDSDQAF3-008G-I) | 1 | ~$35 | https://www.mouser.com/ProductDetail/SanDisk/SDSDQAF3-008G-I?qs=1mbolxNpo8dZV83dHCEirA%3D%3D | from repo doc |
| 5b | (if Mouser is a problem) SanDisk Ultra 16 GB, only from SanDisk's official store | 1 | $4–8 | https://www.aliexpress.com/store/1102960672 | from repo doc |
| 6 | Speaker, 50–57 mm, 8 Ω, 2–5 W, with a mounting flange (buy different ones and compare) | 2–3 | $1–3 each | https://www.aliexpress.com/w/wholesale-50mm-full-range-speaker.html | from repo doc |
| 6b | Named option: Visaton K 57 C, 8 Ω (87 dB published) | 1 | not recorded | https://www.visaton.de/en/products/drivers/fullrange-systems/k-57-c-8-ohm | from repo doc |
| 7 | Foam gasket rings (speaker and mic) | 1 pack | cents | https://www.aliexpress.com/w/wholesale-speaker-foam-gasket-ring-50mm.html | from repo doc |
| 8 | ABS project box, about 150×100×60 mm, with a screw-down lid | 2 (1 spare for drilling mistakes) | $4–8 | https://www.aliexpress.com/w/wholesale-abs-project-box-150x100x60.html | search link |
| 9 | 30 mm round arcade button, one-piece (MAIN) | 2 | $1–2 | https://www.aliexpress.com/w/wholesale-30mm-arcade-button.html | search link |
| 10 | 16 mm **round green momentary** panel button (YES), not latching | 2 | $1–2 | https://www.aliexpress.com/w/wholesale-16mm-momentary-push-button-round-green.html | search link |
| 11 | 16 mm **square red momentary** panel button (NO), not latching | 2 | $1–2 | https://www.aliexpress.com/w/wholesale-16mm-momentary-push-button-square-red.html | search link |
| 12 | B10K potentiometer, 6 mm shaft (volume) | 1 pack of 5 | $2–3 | https://www.aliexpress.us/item/3256802840715108.html | from repo doc |
| 13 | Knob with a **set screw**, for a 6 mm shaft | 2 | $1–2 | https://www.aliexpress.com/w/wholesale-6mm-knob-set-screw.html | search link |
| 14 | 5 mm LED light pipe (over the GPIO48 LED) | 1 pack | $1–3 | https://www.aliexpress.com/w/wholesale-5mm-led-light-pipe.html | search link |
| 15 | Rubber cable grommet kit | 1 | $2–3 | https://www.aliexpress.com/w/wholesale-rubber-grommet-assortment.html | search link |
| 16 | Nylon P-clip cable clamps, screwed inside the box so the clamp takes the 5 kg pull | 1 pack | ~$2 | https://www.aliexpress.com/w/wholesale-nylon-p-clip-cable-clamp.html | search link |
| 17 | M3 security Torx screws (with a pin in the middle) | 1 pack | $3–6 | https://www.aliexpress.com/w/wholesale-m3-security-torx-screw.html | search link |
| 18 | Security Torx bit set (to drive the screws) | 1 | $3–6 | https://www.aliexpress.com/w/wholesale-security-torx-bit-set.html | search link |
| 19 | M3 nylon standoff kit | 1 | $4–6 | https://www.aliexpress.com/w/wholesale-m3-nylon-standoff-kit.html | search link |
| 20 | USB wall adapter, 5 V 2 A, a known brand with the **CE and EAC** marks. Buy it in a real shop; the link is only for comparing prices | 1 per toy | $8–15 | https://www.amazon.com/s?k=5V+2A+USB+wall+charger+CE | search link |
| 21 | USB **data** cable, 1.5 m, that fits your board's port (most of these boards are USB-C). The board has its own USB-serial chip, so you do not need a separate USB-UART adapter | 3 | $2–4 | https://www.aliexpress.com/w/wholesale-usb-c-data-cable-1.5m.html | search link |
| 22 | **Class-2 sound level meter**: IEC 61672-1 Class 2, A and C weighting, LAeq and LCpeak, with a tripod thread. Borrowing one is fine; a phone app does not count | 1 | €100–300 | https://www.amazon.com/s?k=IEC+61672-1+class+2+sound+level+meter+LAeq | search link |
| 23 | 94 dB sound calibrator (optional) | 0–1 | €60–120 | https://www.amazon.com/s?k=94dB+sound+level+calibrator | search link |
| 24 | Small tripod with a 1/4" screw (for the meter at 50 cm, and as a phone stand for recordings) | 1 | $8–15 | https://www.aliexpress.com/w/wholesale-mini-tripod-1-4-screw.html | search link |
| 25 | Infrared thermometer (for the 1-hour 40 °C check) | 1 | $10–20 | https://www.aliexpress.com/w/wholesale-infrared-thermometer-gun.html | search link |
| 26 | Through-hole resistor kit with 100 kΩ (for the amp GAIN step), if you don't have one | 1 | $3–5 | https://www.aliexpress.com/w/wholesale-through-hole-resistor-kit-1-4w.html | search link |
| 27 | Hot-glue gun and sticks, if you don't have one | 1 | $5–10 | https://www.aliexpress.com/w/wholesale-mini-hot-glue-gun.html | search link |
| 28 | Step drill bit 4–32 mm, plus a 3 mm drill bit, if you don't have them | 1 each | $5–10 | https://www.aliexpress.com/w/wholesale-step-drill-bit-4-32mm.html | search link |
| 29 | 2 small USB sticks for the offline copies of the signing key. Buy SanDisk from its official store only: a fake stick can lose the key | 2 | $5 each | https://www.aliexpress.com/store/1102960672 | from repo doc |

- No link is marked "checked 200": the shop sites (AliExpress, Amazon, Mouser, Visaton) are blocked from Claude's side, so none could be opened. Item links often stop working; the search link in the same row finds the same part.
- For units #2–#6 (later, after `ht-first-child` passes), order rows 1–21 again: 5 more sets, one per toy.
