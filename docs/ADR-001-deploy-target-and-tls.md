# ADR-001: Production deploy target and TLS termination

**Status:** Proposed
**Date:** 2026-07-26
**Deciders:** Hayk (owner — sole decider)
**Supersedes:** the open question in `docs/deploy.md` ("TLS, metrics scraping
policy, load balancing … live with whatever reverse proxy / scheduler hosts the
image") and readiness-evaluation task #3 ("decide deploy target").

---

## Context

Areg's backend is a .NET 10 container listening on plaintext `:8080`, built by the
root `Dockerfile` and documented in `docs/deploy.md`. It has never been deployed
anywhere. Today it runs on a laptop over HTTP on a dynamic LAN IP.

The forces at play, verified against the repo:

**All persistent state is one volume.** The Dockerfile declares a single
`VOLUME ["/data"]` holding both `armenian_ai_toy.db` (SQLite) and
`/data/audio-blobs` (C1 story audio). `Database__ConnectionString` and
`Audio__BlobStoreRoot` both point inside it. This is the single most constraining
fact in this decision: **SQLite requires real block storage.** Running a SQLite
file over a network filesystem (SMB/NFS) risks lock corruption, and
`docs/deploy.md` already notes SQLite is "not validated against PostgreSQL", so
switching engines to dodge the problem is not a small change either.

**One instance, by construction.** `LoginAttemptThrottle`, `ExportCooldown`,
`OperatorSessionStore`, and `ChatService.ActiveModes` are all process-local. A
second replica would silently break throttling and mode state. Horizontal scaling
is not available to us and will not be for a while, so any platform feature built
around multi-instance rollout is irrelevant — and any platform that *requires*
multi-instance for zero-downtime deploys is offering us nothing.

**The client that constrains TLS is the toy, not the browser.** `config.h.example`
hardcodes five plaintext endpoints:

```
AREG_BACKEND_URL           "http://YOUR_LAN_IP:5000/api/chat/audio"
AREG_STORY_AUDIO_URL       "http://YOUR_LAN_IP:5000/api/story-audio/anban-huri"
AREG_STORY_QA_URL          "http://YOUR_LAN_IP:5000/api/chat/story-qa"
AREG_STORY_REFLECTION_URL  "http://YOUR_LAN_IP:5000/api/chat/story-qa/reflection-answer"
```

`voice_client.h` uses Arduino `HTTPClient` over a plain `WiFiClient`. Moving to
TLS means `WiFiClientSecure`, a trust anchor in flash, working wall-clock time,
and roughly 40–50 KB of additional heap during handshake — while the same device
is streaming audio. The toy is outbound-only (`ota_foundation.h`: "there is no
inbound server on the toy"), so no inbound firewall story is needed.

**The app bakes the URL in at build time.** `eas.json` carries
`EXPO_PUBLIC_API_BASE_URL`, currently `http://192.168.1.4:5000`. Every URL change
is a rebuild, so the hostname chosen here should be the permanent one.

**Solo operator, pre-revenue.** The scarce resource is attention, not money — but
money is not unlimited either. Any hour spent patching an OS is an hour not spent
on the red-team corpus or the enclosure.

**Backups do not exist.** Whatever we choose has to answer "what happens when the
volume dies", because right now the answer is "every account and transcript is
gone".

---

## Decision

**Deploy the existing container to Fly.io, on a single machine with one Fly
volume mounted at `/data`, with a custom domain and Fly-managed Let's Encrypt
certificates. Terminate TLS at Fly's proxy and forward plaintext to `:8080`.**

**On the device, trust the full Mozilla root store via ESP-IDF's certificate
bundle (`setCACertBundle`) rather than pinning a single root CA.**

Keep a build-time `AREG_ALLOW_PLAINTEXT` escape hatch so the bench workflow keeps
working without a domain.

---

## Options considered

### Option A: Fly.io — single machine + Fly volume *(chosen)*

| Dimension | Assessment |
|---|---|
| Complexity | **Low.** Uses the existing Dockerfile unchanged; `fly deploy` is the whole pipeline |
| Cost | Low — single-digit dollars/month at this scale, but verify against current pricing; volume snapshots are billed as of January 2026 |
| Storage fit | **Good.** Fly volumes are block storage on local NVMe — correct for SQLite |
| TLS | Managed. Certificates issued and renewed automatically for a custom domain |
| Ops burden | **Low.** No OS to patch, no Docker daemon to babysit |
| Backups | Volume snapshots available (now a billed line item), plus you still want an off-platform copy |

**Pros:** Deploys the artifact we already have, with no new build step. Block
storage means SQLite behaves. TLS is somebody else's problem, permanently. Single
machine is the honest shape of this app, and Fly does not punish us for it.
Snapshots give us a real answer to the backup gap on day one.

**Cons:** Volumes are pinned to one machine in one region, so this is not a
high-availability story — a host problem is downtime. Vendor-specific concepts
(`fly.toml`, volumes, regions) creep into the deploy story. A machine restart on
each deploy means a few seconds of downtime, and a toy mid-story sees a failed
request.

### Option B: Single VPS (Hetzner / DigitalOcean) + Caddy

| Dimension | Assessment |
|---|---|
| Complexity | Medium — you assemble the whole stack yourself |
| Cost | **Lowest.** A small VPS is the cheapest option available |
| Storage fit | **Best.** A real disk on a real filesystem; nothing between SQLite and the block device |
| TLS | Caddy obtains and renews Let's Encrypt certificates with near-zero config |
| Ops burden | **Highest.** OS patching, Docker upgrades, disk monitoring, reboot recovery, backups all yours |
| Backups | Entirely yours to build |

**Pros:** Cheapest, most portable, zero lock-in — a plain Docker host you can
recreate anywhere. Caddy's automatic TLS is genuinely two lines of config.
`docs/deploy.md` already anticipates exactly this shape ("Put a reverse proxy in
front of it"), and the two validated Windows runbooks prove you can operate a
box.

**Cons:** You become the on-call for an operating system. The readiness
evaluation already lists "no incident playbook, no on-call" as a P0, and this
option makes that list longer rather than shorter. Unattended-reboot recovery,
log rotation, and backup cron are all new work items that Fly simply absorbs.

### Option C: Render — web service + persistent disk

| Dimension | Assessment |
|---|---|
| Complexity | Low |
| Cost | Higher than A or B; a persistent disk requires a paid instance |
| Storage fit | Good — block-storage disk |
| TLS | Managed, automatic |
| Ops burden | Low |
| Backups | Disk snapshots available |

**Pros:** Genuinely close to Option A on every axis that matters. Deploys a
Dockerfile, gives a managed certificate, offers a real disk.

**Cons:** Attaching a persistent disk pins the service to a single instance and
forfeits zero-downtime deploys — the same constraint as Fly, but at a higher
price point. Nothing here beats Option A; it is the fallback if Fly proves
unpleasant.

### Option D: Azure App Service for Containers

| Dimension | Assessment |
|---|---|
| Complexity | Medium |
| Cost | Highest of the four |
| Storage fit | **Bad — this rules it out** |
| TLS | Managed certificates, custom domains |
| Ops burden | Low |

**Pros:** Managed certificates, a mature platform, and a plausible path if this
ever became a business with an enterprise buyer.

**Cons:** Persistent storage is Azure Files, an SMB share. **SQLite over SMB is
a data-corruption risk, not a performance footnote** — file locking over a
network filesystem is precisely the scenario SQLite's own documentation warns
against. Adopting this option means migrating to PostgreSQL first, which
`docs/deploy.md` flags as unvalidated. That converts a deployment task into a
database migration project. Rejected on those grounds alone.

---

## Trade-off analysis

The real decision is **Fly vs. VPS**, and it reduces to a single question: is
your next scarce hour better spent on Areg's product risks or on a Linux box?

The readiness evaluation answers this. The open P0s are *no legal review*, *no
red-team corpus*, *no monitoring*, *no on-call*. A VPS adds a fifth item — an
operating system to own — while saving a few dollars a month. At pre-revenue
scale that trade is bad. Fly costs marginally more and deletes a category of
work; it converts "keep a server alive" into "run `fly deploy`".

The counter-argument deserves stating: Option B is genuinely the best *technical*
fit. SQLite on a bare disk with Caddy in front is simpler, more portable, and has
no vendor concepts in it. If you enjoy running a box — and the two validated
Windows deploy runbooks suggest you might — the cost difference compounds over
years, and there is nothing wrong with choosing it. The recommendation is about
your attention budget, not about technical merit.

Azure is out on storage. Render is Fly with a higher bill.

---

## The ESP32 certificate decision

This is the part of the ADR that outlives the hosting choice, because it is baked
into flash on every unit you ship.

**Rejected: `setInsecure()`.** Encrypts the transport but authenticates nothing.
A device that skips validation can be trivially MITM'd on the shared home Wi-Fi
this is specifically meant to protect against, and it would ship a child's voice
to whoever answered. Not acceptable in a product for four-year-olds.

**Rejected: pin the leaf certificate.** Renews every ~60–90 days. Every renewal
would brick the fleet.

**Rejected: pin a single root CA.** Baking only ISRG Root X1 works — until Let's
Encrypt issues from a different root (X2 is already in service), or you move
hosting to a provider using a different CA. Then every device fails
simultaneously, and the only fix is an OTA that requires the very TLS connection
that just broke. This is a bricking mode with no recovery path.

**Chosen: the ESP-IDF certificate bundle** (`client.setCACertBundle(...)`), which
embeds the Mozilla root store in roughly 65 KB of flash. On an 8 MB N8 part with
dual OTA slots that is affordable. It survives CA rotation, survives changing
hosting providers, and removes an entire class of fleet-wide outage.

Two consequences that will bite during implementation:

**Time must be correct before the first HTTPS call.** Certificate validity
checking needs a real wall clock. The firmware has to complete an SNTP sync after
Wi-Fi comes up and before any TLS handshake, and it needs a sane failure path when
NTP is blocked on a restrictive home network.

**Heap during handshake, concurrent with audio.** An mbedTLS handshake wants
roughly 40–50 KB, arriving while the device is buffering PCM. This has never been
measured on this firmware. Treat "TLS handshake succeeds mid-audio-turn under
memory pressure" as an explicit bench test with a real result, not an assumption.

---

## Consequences

**Easier:** TLS renewal stops being anyone's job. `fly deploy` replaces a manual
deploy. Volume snapshots give a backup story on day one. The app's baked-in URL
becomes permanent, so `eas.json` stops being a per-environment edit. Items 2, 9,
and 10 in the roadmap unblock together.

**Harder:** A single machine in a single region means a host failure is user-visible
downtime — acceptable for a supervised beta, not for a shipped toy. Deploys cause
a brief restart, so the firmware needs sane retry behaviour around a dropped
connection mid-story. Firmware now carries a 65 KB cert bundle and a hard
dependency on NTP.

**To revisit:** If the toy leaves one country, single-region latency becomes a
real product issue — revisit region strategy then, not now. If a second backend
instance ever becomes necessary, the four process-local components have to move
to shared storage first; that is a prerequisite, not a migration detail. And if
concurrent write load ever makes SQLite the bottleneck, the PostgreSQL path
reopens — at which point Option D stops being disqualified.

---

## Action items

**Platform (do these first, in order)**

1. [ ] Register the domain; decide the hostname now, because it ships inside every app build and every firmware image.
2. [ ] `docker build` and `docker run` locally — per `docs/deploy.md` this has **never been executed**; do not discover a broken image while also learning Fly.
3. [ ] `fly launch --no-deploy` from the repo root, pointing at the existing Dockerfile; create a volume and mount it at `/data`.
4. [ ] Set secrets as Fly secrets, never in `fly.toml`: `OpenAI__ApiKey`, `Jwt__Keys__0` (**must not be the legacy default — startup rejects a poisoned key set**), `Devices__ProvisioningSecret`, `Metrics__ScrapeToken`.
5. [ ] Add the custom domain and issue certificates; verify HSTS and the HTTP→HTTPS redirect from a browser.
6. [ ] Confirm the `/data` volume survives a deploy and a machine restart with the SQLite file intact.

**Backend config flips (one commit, after the domain resolves)**

7. [ ] `Security:RequireHttps = true` — currently `false`; the comment in `appsettings.json` already describes this exact switch.
8. [ ] `ForwardedHeaders:Enabled = true` plus `KnownProxies` — **required**, or the app sees Fly's proxy as the client and every request looks plaintext.
9. [ ] Pin `AllowedHosts` to the real hostname (currently `""`).
10. [ ] Set `Cors:AllowedOrigins` for the dashboard origin (currently `[]`).
11. [ ] Switch `Notifications:Transport` to `smtp` and fill the `Smtp__*` block; set `PasswordResetLinkBase` to the HTTPS origin.
12. [ ] Verify `/metrics` returns 404 or 401 to an unauthenticated caller from the public internet.

**Clients**

13. [ ] `eas.json` → `EXPO_PUBLIC_API_BASE_URL = https://<host>`; rebuild the preview profile and confirm login end to end.
14. [ ] Firmware: `WiFiClientSecure` + `setCACertBundle`, all five `AREG_*_URL` constants to `https://`, SNTP sync before the first handshake, and an `AREG_ALLOW_PLAINTEXT` build flag so the bench keeps working.
15. [ ] Bench-measure free heap through a TLS handshake during an audio turn. Record the number in `docs/`. If it is tight, this becomes its own slice.
16. [ ] Re-verify the OTA manifest fetch and SD content sync over HTTPS — both are currently verified over plaintext only.

**Then**

17. [ ] Add a deploy stage to `.github/workflows/ci.yml` (build and test already run on every push and PR).
18. [ ] Schedule an off-platform copy of `/data` — snapshots on the same provider are not a backup strategy on their own.

---

## Sources

- [Fly.io Resource Pricing](https://fly.io/docs/about/pricing/)
- [Cost Management on Fly.io](https://fly.io/docs/about/cost-management/)
- [Fly.io: charging for volume snapshots from January 2026](https://community.fly.io/t/we-are-going-to-start-charging-for-volume-snapshots-from-january-2026/26202)
