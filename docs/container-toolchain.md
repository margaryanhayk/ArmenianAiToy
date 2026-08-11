# Getting a real toolchain in the agent container

**Written 2026-08-11.** Several commit messages and notes in this repo say some
variant of *"cannot be compiled or tested here (no dotnet in this container)"*.
That was true of the container as it starts, and it is **no longer a limit worth
accepting** — both missing tools install from Ubuntu's own archive in about a
minute. This is how, and what each one buys.

Anyone reading an older commit message that claims verification was impossible
should read it as *"was not done"*, not *"could not be done"*.

## .NET 10 SDK — run the 2,542 backend tests locally

```sh
dpkg --configure -a          # the image ships with dpkg mid-interrupt
apt-get update -qq
apt-get install -y dotnet-sdk-10.0
```

`dotnet-sdk-10.0` is in the **Ubuntu 24.04 archive** — no Microsoft feed needed.
That matters, because the usual route does not work here:

- `https://dot.net/v1/dotnet-install.sh` → **403 from the egress proxy**.
- `builds.dotnet.microsoft.com`, `dotnetcli.azureedge.net` → unreachable.
- `packages.microsoft.com` answers, but is not needed.

Then:

```sh
cd backend
dotnet restore ArmenianAiToy.slnx
dotnet test ArmenianAiToy.slnx -c Release      # ~35s for the whole suite
```

**Worth the minute it costs.** CI takes ~90 seconds per push and it is the only
thing standing between an editor and a broken build; locally it is 35 seconds
and it happens before the commit.

### What still does not work: `dotnet ef`

```
Could not load file or assembly 'Microsoft.EntityFrameworkCore.Design'
```

This is **by design and not a container problem**: the Api startup project
deliberately does not reference `Microsoft.EntityFrameworkCore.Design`, and
adding a NuGet dependency is an owner decision (see the note in
`Migrations/20260803120000_AddStoryPlays.cs`). So migrations here stay
hand-written.

**Verify a hand-written migration by APPLYING it instead** — which tests the
thing that actually runs in production, rather than the scaffolder's opinion of
it. Boot the API against a throwaway database; `Program.cs` calls `Migrate()`:

```sh
cd backend/src/ArmenianAiToy.Api
ASPNETCORE_ENVIRONMENT=Development \
Database__ConnectionString="Data Source=/tmp/mig.db" \
Jwt__Key="a-local-key-long-enough-to-pass-the-startup-guard-123456" \
OpenAI__ApiKey="sk-not-used" \
Urls="http://127.0.0.1:5310" \
timeout 90 dotnet run --no-launch-profile
```

Then read the schema it produced, and check the columns, indexes and foreign
keys are what the migration claimed:

```sh
python3 -c "
import sqlite3; c = sqlite3.connect('/tmp/mig.db')
print([r[1] for r in c.execute('PRAGMA table_info(YourTable)')])
print([(r[1], 'UNIQUE' if r[2] else '') for r in c.execute('PRAGMA index_list(YourTable)')])
print([(r[2], r[3], r[6]) for r in c.execute('PRAGMA foreign_key_list(YourTable)')])
"
```

The run exits 124 (the `timeout`), which is success — you only need it to reach
`Now listening on:`.

## ffmpeg — the story-audio tools

```sh
apt-get install -y ffmpeg
```

`tools/story-audio/Ship-StoryAudio.ps1` (loudness, repair, install) and
`tools/story-audio/mix_ambience.py` (the ambience mixer) both need it, and both
were written here without ever being run.

It also gives an independent check on the truncation finding, which was measured
with a dependency-free MP3 frame parser:

```sh
ffprobe -v error -show_entries format=duration -of csv=p=0 <file>.mp3
```

The two agree — `khosogh-dzuk` 81.7 s where `check_story_audio.py` said 1:21,
and 128 kbps confirming those files never went through the 192 kbps shipper.

## What is still genuinely unavailable

- **`dot.net` and the Microsoft CDNs** — proxy-denied. Use the Ubuntu package.
- **A phone simulator.** The mobile app is verified by building it for web and
  driving it in Chromium — see `tools/dashboard-audit/mobile-preview.sh`.
- **Two apt PPAs** (`deadsnakes`, `ondrej/php`) 403 through the proxy. They are
  unrelated to this project; `apt-get update` warns and carries on.

## The general lesson

Check `apt-cache search` before concluding a tool is unavailable. The proxy
blocks vendor CDNs, which makes the *documented* install path fail and makes it
look as though the tool cannot be had at all — while the distribution's own
archive, which is allowed, has it.
