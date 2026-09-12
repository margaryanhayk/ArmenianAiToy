#!/usr/bin/env python3
"""Factory station: pre-load a toy's SD card with its whole story/voice/
game/music library before first boot, so a child hears every story from the
first press instead of waiting on the ~180 s-after-boot Wi-Fi content sync
(esp32/AregVoiceMvp/content_sync.cpp) to fill an empty card.

WHAT THIS REPLACES. Identity provisioning is scripted
(tools/factory/provision_toy.py: register -> NVS burn -> label). Loading the
SD card that carries every story was a manual, undocumented step — this
script is the SD-card half of the same production line.

THE ON-CARD CONTRACT (read from the firmware source, not guessed):

  Directory layout + exact file naming — esp32/AregVoiceMvp/
  content_sync_rules.h, cs_build_cache_path / cs_build_clip_cache_path /
  cs_build_music_cache_path / cs_build_voice_cache_path /
  cs_build_game_cache_path (lines 530-752):
      /stories/<storyId>-v<version>.mp3
      /stories/<storyId>-v<version>-<kind>.mp3        (per-story clip)
      /music/<trackId>-v<version>.mp3
      /voice/<voiceId>-v<version>.mp3
      /games/<gameKey>/<clipId>.mp3                    (deliberately NO
                                                         version in the name
                                                         — see that file's
                                                         comment above
                                                         cs_build_game_cache_path)
  Matching /tmp/*.part temp names — same file, cs_build_temp_path /
  cs_build_clip_temp_path / cs_build_music_temp_path / cs_build_voice_temp_path
  / cs_build_game_temp_path (lines 600-766) — a distinct one-letter prefix
  per namespace (none / none / "m-" / "v-" / "g-") keeps two namespaces'
  temp files from ever colliding.

  id / kind allowlists — content_sync_rules.h cs_is_valid_story_id (396-414,
  lowercase a-z0-9-_, <=48 chars — CS_MAX_STORY_ID_LEN) and
  cs_is_valid_clip_kind (556-568: intro/question/question1/question2/
  summary/offer/reoffer/serialnext). sha256 must be exactly 64 hex chars
  (cs_is_sha256_hex, 443-461); size must be positive and <= 32 MiB
  (cs_is_valid_size / CS_MAX_STORY_BYTES, 72-73, 464-466); version is
  clamped to [1, 99999] (cs_normalize_version, 471-479) — this tool
  re-applies every one of these rules before writing anything, the same
  fail-closed-per-item posture content_sync.cpp itself uses (a bad item is
  skipped, never a hard stop).

  /content_index.json shape — esp32/AregVoiceMvp/content_sync_model.cpp,
  cs_index_build (338-402), cs_index_add_music (494-510),
  cs_index_add_voice (607-620), cs_index_add_modes (622-628),
  cs_index_add_story_flags (641-644), cs_index_add_questions_flag
  (657-659), cs_index_add_orphan_sweep_flag (672-674), cs_index_add_games
  (767-772). schemaVersion is CS_INDEX_SCHEMA_VERSION
  (content_sync_rules.h:109), 7 today. This tool builds byte-for-shape the
  same document those functions write: schemaVersion, introEnabled,
  stories[] (storyId/version/title/sha256/sizeBytes/cachePath/verified,
  +seriesId/seriesIndex for a serial episode, +altOf for a variant ending,
  +clips[] of kind/sha256/sizeBytes/verified), musicEnabled, music[],
  voice[], games[], storyEnabled/gameEnabled/riddleEnabled/curiosityEnabled,
  pausesEnabled/variantsEnabled, questionsEnabled, orphanSweepEnabled.
  Deliberately NOT written: the legacy root-level storyId/version/sha256/
  file "mirror" fields cs_index_build emits for pre-multi-story firmware
  (content_sync_model.cpp:384-402) — schema-2+ readers (story_select.cpp,
  every build this tool targets) never look at them, and the compile-time
  AREG_STORY_ID they exist for is not knowable from a manifest response.

  NO SEPARATE "SYNCED AT LEAST ONCE" GATE. story_select.cpp reads
  /content_index.json directly at selection/playback time (load_raw_index,
  lines 80-126; music at 422-489; voice/welcome at 639-752), gated only on
  the file existing and parsing — there is no NVS flag that must ALSO say
  "a real sync has happened" before the toy trusts it. So a correctly
  shaped, pre-written index IS accepted on first boot.

  "verified" IS THE GATE, and it is namespace-specific:
    - story_select.h:149 story_entry_eligible() requires
      entry.verified == true (and version >= 1, a safe /stories/ path, a
      positive sizeBytes, and the ACTUAL file on disk matching that exact
      size) before a story can ever be selected. story clips, music tracks
      and welcome-flow voice clips are gated the identical way
      (story_select.cpp:402, 482, 704, 746: `!clip->verified ... continue`).
      This tool therefore writes "verified": true ONLY after it has
      downloaded (or found already on the card) a file and confirmed its
      sha256 AND size match the manifest — exactly what a real sync's
      download-then-verify step does before it sets the same flag
      (content_sync.cpp's per-namespace download functions).
    - Offline GAME clips are the one exception: offline_games.cpp checks
      on-card presence directly, by opening
      /games/<gameKey>/intro.mp3 (game_intro_present, lines 536-541,
      541 uses AREG_GAMES_CLIP_DIR "/%s/intro.mp3") — it never reads the
      index at all. This tool still writes "verified": true for a
      downloaded game clip anyway, matching content_sync.cpp's own
      cs_manifest_read_game (content_sync_model.cpp:684-719) — that flag's
      only real effect is stopping the toy's own next real content_sync
      from redundantly re-downloading a file this tool already placed and
      checked. Games also have no "retired" handling of their own in the
      manifest parser (content_sync_model.cpp:684-719 never reads a
      "retired" key) — an operator retires a game clip the same way as any
      other namespace's, by turning `enabled` off, which this tool already
      treats as "do not download" for every namespace (see below).

  Atomicity — content_sync.cpp's write_index() (lines 459-off, ~575-620)
  writes to content_index.json.new and only replaces the live file once the
  write is verified complete; downloads land in /tmp/*.part and are only
  renamed into place after the sha256 check passes (content_sync.cpp
  ~835-976 sync_story_clips / download_story). This tool follows the same
  discipline end to end: every file is downloaded to its own *.part name
  first, verified, then atomically renamed; the index is written to
  content_index.json.new and swapped in with os.replace() only once fully
  written. A run interrupted partway (a card ejected mid-copy) therefore
  looks exactly like a network sync interrupted the same way — the live
  index, if one already existed, is never left in a half-written state, and
  a stray *.part from an earlier interrupted attempt is swept once a run
  completes cleanly (content_sync.cpp's own orphan_sweep_temp, 767-819,
  does the equivalent cleanup on a real sync).

THE BACKEND MANIFEST (also read from source, not guessed):

  GET /api/devices/content-manifest, device-authed via the X-Device-Id /
  X-Api-Key headers (backend/src/ArmenianAiToy.Api/Middleware/
  DeviceAuthMiddleware.cs:54-55; DeviceController.cs:534-585). Response
  shape is ContentManifestResponse (backend/src/ArmenianAiToy.Application/
  DTOs/ContentManifestResponse.cs), camelCase on the wire (ASP.NET Core's
  default MVC JSON policy — storyId, audioUrl, sizeBytes, enabled,
  retired, ...). `enabled:false` (an item simply not offered right now) and
  `retired:true` (a genuine "delete this if you have it" instruction —
  ContentStoryItem.Retired's own doc comment) both mean "do not download"
  here: a retired item is ALSO always emitted with enabled:false alongside
  it, by contract, so checking only `enabled` already excludes retired
  items without duplicating the retirement logic.

  GET /api/devices/content-file (same auth) streams the actual bytes for
  one lookup key: storyId[&clip=kind] | trackId | voiceId |
  gameKey&clipId (DeviceController.cs:613-771). Every `audioUrl` the
  manifest hands back is a path relative to THIS SAME backend (e.g.
  "/api/devices/content-file?storyId=..." — ContentManifestService.cs
  ResolveAudioUrl/BuildClips), so this tool resolves it against --backend
  rather than treating it as a foreign URL.

USAGE
    # Load a freshly formatted, mounted card:
    export AREG_DEVICE_ID=<from provisioning>
    export AREG_DEVICE_KEY=<from provisioning -- NEVER a CLI flag>
    python3 tools/factory/load_sd_card.py \\
        --backend http://192.168.1.50:5000 \\
        --mount /media/factory/SDCARD

    # See the plan without writing anything:
    python3 tools/factory/load_sd_card.py --backend ... --mount ... --dry-run

    # Check an already-loaded card against the current manifest, no writes:
    python3 tools/factory/load_sd_card.py --backend ... --mount ... --verify

    # Credentials can also come from a saved POST /api/devices/register
    # response instead of the environment (provision_toy.py does not write
    # one to disk itself, on purpose -- CLAUDE.md: never write a device key
    # into a file. Only pass --device-info if you saved that response
    # yourself, e.g. for --dry-run testing against a throwaway backend):
    python3 tools/factory/load_sd_card.py --backend ... --mount ... \\
        --device-info /tmp/register_response.json

Exit code 0 = every enabled item is on the card, sha-verified, and the
index is written. Non-zero = read the printed reason; nothing was left
half-written (see Atomicity above).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional, Tuple

CS_INDEX_SCHEMA_VERSION = 7  # esp32/AregVoiceMvp/content_sync_rules.h:109
CS_MAX_STORY_BYTES = 32 * 1024 * 1024  # content_sync_rules.h:72-73
_ID_RE = re.compile(r"^[a-z0-9_-]{1,48}$")  # cs_is_valid_story_id, rules.h:396-414
_SHA256_RE = re.compile(r"^[0-9a-fA-F]{64}$")  # cs_is_sha256_hex, rules.h:443-461
_CLIP_KINDS = {  # cs_is_valid_clip_kind, content_sync_rules.h:556-568
    "intro", "question", "question1", "question2",
    "summary", "offer", "reoffer", "serialnext",
}

CONTENT_MANIFEST_PATH = "api/devices/content-manifest"


class ToolError(Exception):
    """A condition that stops the run before anything is written."""


# ---- pure validation / naming, mirrored from the firmware -----------------

def is_valid_id(value: Optional[str]) -> bool:
    return bool(value) and bool(_ID_RE.match(value))


def is_valid_clip_kind(kind: Optional[str]) -> bool:
    return kind in _CLIP_KINDS


def is_sha256_hex(value: Optional[str]) -> bool:
    return bool(value) and bool(_SHA256_RE.match(value))


def is_valid_size(size_bytes: Any) -> bool:
    return isinstance(size_bytes, int) and 0 < size_bytes <= CS_MAX_STORY_BYTES


def normalize_version(version: Any) -> int:
    try:
        v = int(version)
    except (TypeError, ValueError):
        v = 1
    if v < 1:
        return 1
    if v > 99999:
        return 99999
    return v


def story_cache_path(story_id: str, version: int) -> str:
    return f"stories/{story_id}-v{version}.mp3"


def story_clip_cache_path(story_id: str, version: int, kind: str) -> str:
    return f"stories/{story_id}-v{version}-{kind}.mp3"


def story_temp_path(story_id: str, version: int) -> str:
    return f"tmp/{story_id}-v{version}.mp3.part"


def story_clip_temp_path(story_id: str, version: int, kind: str) -> str:
    return f"tmp/{story_id}-v{version}-{kind}.mp3.part"


def music_cache_path(track_id: str, version: int) -> str:
    return f"music/{track_id}-v{version}.mp3"


def music_temp_path(track_id: str, version: int) -> str:
    return f"tmp/m-{track_id}-v{version}.mp3.part"


def voice_cache_path(voice_id: str, version: int) -> str:
    return f"voice/{voice_id}-v{version}.mp3"


def voice_temp_path(voice_id: str, version: int) -> str:
    return f"tmp/v-{voice_id}-v{version}.mp3.part"


def game_cache_path(game_key: str, clip_id: str) -> str:
    return f"games/{game_key}/{clip_id}.mp3"


def game_temp_path(game_key: str, clip_id: str) -> str:
    return f"tmp/g-{game_key}-{clip_id}.mp3.part"


# ---- the download plan -----------------------------------------------------

@dataclass
class DownloadTask:
    namespace: str          # "stories" | "clips" | "music" | "voice" | "games"
    label: str               # for log lines / error messages
    rel_path: str             # final path, relative to the card's mount root
    temp_rel_path: str        # /tmp/*.part path, relative to the mount root
    audio_url: str            # as given by the manifest (may be relative)
    sha256: str                # lowercase 64-hex, expected
    size_bytes: int
    on_verified: Callable[[], None]  # marks the owning index entry verified


def _mark_verified(entry: dict) -> Callable[[], None]:
    def _set() -> None:
        entry["verified"] = True
    return _set


def build_index_and_tasks(manifest: dict) -> Tuple[dict, List[DownloadTask], Dict[str, int]]:
    """Turns a content-manifest response into (a) the exact
    /content_index.json document this tool will write once every task
    below is verified, and (b) the list of files to fetch. Every manifest
    item is re-validated against the same rules content_sync.cpp applies
    (see this module's docstring) -- a bad item is skipped and counted,
    never a hard failure, matching the firmware's own fail-closed-per-item
    posture.
    """
    index: dict = {
        "schemaVersion": CS_INDEX_SCHEMA_VERSION,
        "introEnabled": bool(manifest.get("storyIntroEnabled", True)),
        "stories": [],
    }
    tasks: List[DownloadTask] = []
    stats: Dict[str, int] = defaultdict(int)

    seen_story_ids = set()
    for item in manifest.get("stories") or []:
        story_id = item.get("storyId", "")
        if item.get("retired"):
            stats["skipped_retired"] += 1
            continue
        if not item.get("enabled", False):
            stats["skipped_disabled"] += 1
            continue
        version = normalize_version(item.get("version", 1))
        sha256 = (item.get("sha256") or "").lower()
        size_bytes = item.get("sizeBytes", 0)
        audio_url = item.get("audioUrl") or ""
        if (not is_valid_id(story_id) or not is_sha256_hex(sha256)
                or not is_valid_size(size_bytes) or not audio_url
                or story_id.lower() in seen_story_ids):
            stats["skipped_invalid"] += 1
            print(f"[skip] story {story_id!r}: invalid or duplicate manifest item",
                  file=sys.stderr)
            continue
        seen_story_ids.add(story_id.lower())

        entry: dict = {
            "storyId": story_id,
            "version": version,
            "title": item.get("title", ""),
            "sha256": sha256,
            "sizeBytes": size_bytes,
            "cachePath": "/" + story_cache_path(story_id, version),
            "verified": False,
        }
        series_id = item.get("seriesId")
        series_index = item.get("seriesIndex")
        if (isinstance(series_index, int) and series_index >= 1
                and is_valid_id(series_id)):
            entry["seriesId"] = series_id
            entry["seriesIndex"] = series_index
        alt_of = item.get("altOf")
        if is_valid_id(alt_of):
            entry["altOf"] = alt_of

        tasks.append(DownloadTask(
            namespace="stories", label=f"story:{story_id}",
            rel_path=story_cache_path(story_id, version),
            temp_rel_path=story_temp_path(story_id, version),
            audio_url=audio_url, sha256=sha256, size_bytes=size_bytes,
            on_verified=_mark_verified(entry)))

        clip_entries = []
        seen_kinds = set()
        for clip in item.get("clips") or []:
            kind = (clip.get("kind") or "").lower()
            clip_sha = (clip.get("sha256") or "").lower()
            clip_size = clip.get("sizeBytes", 0)
            clip_url = clip.get("audioUrl") or ""
            if (not is_valid_clip_kind(kind) or not is_sha256_hex(clip_sha)
                    or not is_valid_size(clip_size) or not clip_url
                    or kind in seen_kinds):
                stats["skipped_invalid"] += 1
                print(f"[skip] story {story_id!r} clip {kind!r}: invalid or duplicate",
                      file=sys.stderr)
                continue
            seen_kinds.add(kind)
            clip_entry = {
                "kind": kind, "sha256": clip_sha, "sizeBytes": clip_size,
                "verified": False,
            }
            clip_entries.append(clip_entry)
            tasks.append(DownloadTask(
                namespace="clips", label=f"story:{story_id}:{kind}",
                rel_path=story_clip_cache_path(story_id, version, kind),
                temp_rel_path=story_clip_temp_path(story_id, version, kind),
                audio_url=clip_url, sha256=clip_sha, size_bytes=clip_size,
                on_verified=_mark_verified(clip_entry)))
        if clip_entries:
            entry["clips"] = clip_entries
        index["stories"].append(entry)

    index["musicEnabled"] = bool(manifest.get("bedtimeMusicEnabled", False))
    music_entries = []
    seen_track_ids = set()
    for item in manifest.get("music") or []:
        if item.get("retired") or not item.get("enabled", False):
            stats["skipped_retired" if item.get("retired") else "skipped_disabled"] += 1
            continue
        track_id = item.get("trackId", "")
        version = normalize_version(item.get("version", 1))
        sha256 = (item.get("sha256") or "").lower()
        size_bytes = item.get("sizeBytes", 0)
        audio_url = item.get("audioUrl") or ""
        if (not is_valid_id(track_id) or not is_sha256_hex(sha256)
                or not is_valid_size(size_bytes) or not audio_url
                or track_id.lower() in seen_track_ids):
            stats["skipped_invalid"] += 1
            print(f"[skip] music {track_id!r}: invalid or duplicate manifest item",
                  file=sys.stderr)
            continue
        seen_track_ids.add(track_id.lower())
        entry = {
            "trackId": track_id, "version": version,
            "title": item.get("title", ""), "sha256": sha256,
            "sizeBytes": size_bytes, "verified": False,
        }
        music_entries.append(entry)
        tasks.append(DownloadTask(
            namespace="music", label=f"music:{track_id}",
            rel_path=music_cache_path(track_id, version),
            temp_rel_path=music_temp_path(track_id, version),
            audio_url=audio_url, sha256=sha256, size_bytes=size_bytes,
            on_verified=_mark_verified(entry)))
    if music_entries:
        index["music"] = music_entries

    voice_entries = []
    seen_voice_ids = set()
    for item in manifest.get("voice") or []:
        if item.get("retired") or not item.get("enabled", False):
            stats["skipped_retired" if item.get("retired") else "skipped_disabled"] += 1
            continue
        voice_id = item.get("voiceId", "")
        version = normalize_version(item.get("version", 1))
        sha256 = (item.get("sha256") or "").lower()
        size_bytes = item.get("sizeBytes", 0)
        audio_url = item.get("audioUrl") or ""
        if (not is_valid_id(voice_id) or not is_sha256_hex(sha256)
                or not is_valid_size(size_bytes) or not audio_url
                or voice_id.lower() in seen_voice_ids):
            stats["skipped_invalid"] += 1
            print(f"[skip] voice {voice_id!r}: invalid or duplicate manifest item",
                  file=sys.stderr)
            continue
        seen_voice_ids.add(voice_id.lower())
        entry = {
            "voiceId": voice_id, "version": version, "sha256": sha256,
            "sizeBytes": size_bytes, "verified": False,
        }
        voice_entries.append(entry)
        tasks.append(DownloadTask(
            namespace="voice", label=f"voice:{voice_id}",
            rel_path=voice_cache_path(voice_id, version),
            temp_rel_path=voice_temp_path(voice_id, version),
            audio_url=audio_url, sha256=sha256, size_bytes=size_bytes,
            on_verified=_mark_verified(entry)))
    if voice_entries:
        index["voice"] = voice_entries

    game_entries = []
    seen_game_pairs = set()
    for item in manifest.get("games") or []:
        # Games carry no "retired" parsing of their own on the firmware side
        # (content_sync_model.cpp cs_manifest_read_game never reads that
        # key) -- an operator retires a game clip via enabled:false alone,
        # same as this tool already checks below.
        if not item.get("enabled", False):
            stats["skipped_disabled"] += 1
            continue
        game_key = item.get("gameKey", "")
        clip_id = item.get("clipId", "")
        version = normalize_version(item.get("version", 1))
        sha256 = (item.get("sha256") or "").lower()
        size_bytes = item.get("sizeBytes", 0)
        audio_url = item.get("audioUrl") or ""
        pair = (game_key.lower(), clip_id.lower())
        if (not is_valid_id(game_key) or not is_valid_id(clip_id)
                or not is_sha256_hex(sha256) or not is_valid_size(size_bytes)
                or not audio_url or pair in seen_game_pairs):
            stats["skipped_invalid"] += 1
            print(f"[skip] game {game_key!r}/{clip_id!r}: invalid or duplicate manifest item",
                  file=sys.stderr)
            continue
        seen_game_pairs.add(pair)
        entry = {
            "gameKey": game_key, "clipId": clip_id, "version": version,
            "sha256": sha256, "sizeBytes": size_bytes, "verified": False,
        }
        game_entries.append(entry)
        tasks.append(DownloadTask(
            namespace="games", label=f"game:{game_key}/{clip_id}",
            rel_path=game_cache_path(game_key, clip_id),
            temp_rel_path=game_temp_path(game_key, clip_id),
            audio_url=audio_url, sha256=sha256, size_bytes=size_bytes,
            on_verified=_mark_verified(entry)))
    if game_entries:
        index["games"] = game_entries

    index["storyEnabled"] = bool(manifest.get("storyEnabled", True))
    index["gameEnabled"] = bool(manifest.get("gameEnabled", True))
    index["riddleEnabled"] = bool(manifest.get("riddleEnabled", True))
    index["curiosityEnabled"] = bool(manifest.get("curiosityEnabled", True))
    index["pausesEnabled"] = bool(manifest.get("storyPausesEnabled", True))
    index["variantsEnabled"] = bool(manifest.get("variantEndingsEnabled", True))
    index["questionsEnabled"] = bool(manifest.get("storyQuestionsEnabled", True))
    # The manifest does not carry this flag today (content_sync.cpp:1654
    # falls back to false when the key is absent, and
    # ContentManifestResponse has no field for it at all) -- always false
    # here for the same reason: matches the shipped server behavior exactly.
    index["orphanSweepEnabled"] = False

    return index, tasks, dict(stats)


# ---- I/O: HTTP (injectable for tests) and disk -----------------------------

def default_fetch_json(url: str, headers: Dict[str, str], timeout: float) -> Any:
    req = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


def default_download(url: str, headers: Dict[str, str], dest: Path, timeout: float) -> Tuple[str, int]:
    dest.parent.mkdir(parents=True, exist_ok=True)
    req = urllib.request.Request(url, headers=headers)
    sha = hashlib.sha256()
    size = 0
    with urllib.request.urlopen(req, timeout=timeout) as resp, open(dest, "wb") as f:
        while True:
            chunk = resp.read(65536)
            if not chunk:
                break
            f.write(chunk)
            sha.update(chunk)
            size += len(chunk)
    return sha.hexdigest(), size


def sha256_of_file(path: Path) -> Tuple[str, int]:
    sha = hashlib.sha256()
    size = 0
    with open(path, "rb") as f:
        while True:
            chunk = f.read(65536)
            if not chunk:
                break
            sha.update(chunk)
            size += len(chunk)
    return sha.hexdigest(), size


def resolve_url(backend: str, path: str) -> str:
    if path.startswith("http://") or path.startswith("https://"):
        return path
    base = backend if backend.endswith("/") else backend + "/"
    return urllib.parse.urljoin(base, path.lstrip("/"))


def clean_stray_parts(mount: Path) -> int:
    """Removes leftover *.part files under <mount>/tmp -- the only ones
    that can still be there once every task above has either been renamed
    into place or reported as an error (an errored task's own .part is
    already removed at the point of failure). Mirrors the cleanup a real
    sync's orphan_sweep_temp performs (content_sync.cpp:767-819)."""
    tmp_dir = mount / "tmp"
    if not tmp_dir.is_dir():
        return 0
    removed = 0
    for part in tmp_dir.glob("*.part"):
        try:
            part.unlink()
            removed += 1
        except OSError:
            pass
    return removed


_NAMESPACE_ORDER = ("stories", "clips", "music", "voice", "games")


def print_summary(ns_counts: Dict[str, int], ns_bytes: Dict[str, int],
                   header: str) -> None:
    print(f"\n[{header} summary] namespace  count  bytes")
    total_count = 0
    total_bytes = 0
    for ns in _NAMESPACE_ORDER:
        count = ns_counts.get(ns, 0)
        size = ns_bytes.get(ns, 0)
        total_count += count
        total_bytes += size
        print(f"  {ns:<8s} {count:6d} {size:12d}")
    print(f"  {'total':<8s} {total_count:6d} {total_bytes:12d}")


# ---- credentials ------------------------------------------------------------

def resolve_credentials(args: argparse.Namespace) -> Tuple[str, str]:
    """Never accepts a device id/key as a bare CLI flag -- that would land
    in shell history. --device-info reads a saved POST /api/devices/register
    response (the same shape provision_toy.py's own --dry-run consumes);
    provision_toy.py does NOT write one to disk on its own (CLAUDE.md: never
    write a device key into a file), so that flag exists for a caller who
    saved the response themselves (e.g. this tool's own end-to-end test).
    The documented path is the environment."""
    if args.device_info:
        try:
            data = json.loads(Path(args.device_info).read_text())
        except OSError as e:
            raise ToolError(f"could not read {args.device_info}: {e}") from e
        device_id = data.get("deviceId")
        api_key = data.get("apiKey")
        if not device_id or not api_key:
            raise ToolError(f"{args.device_info} is missing deviceId/apiKey")
        return device_id, api_key
    device_id = os.environ.get("AREG_DEVICE_ID")
    api_key = os.environ.get("AREG_DEVICE_KEY")
    if not device_id or not api_key:
        raise ToolError(
            "device credentials required: set AREG_DEVICE_ID and AREG_DEVICE_KEY "
            "in the environment, or pass --device-info with a saved "
            "POST /api/devices/register response. Never pass the key as a "
            "CLI flag -- it would land in shell history.")
    return device_id, api_key


# ---- load / verify ----------------------------------------------------------

def do_load(mount: Path, index: dict, tasks: List[DownloadTask],
            dry_run: bool, download: Callable[[str, Dict[str, str], Path, float], Tuple[str, int]],
            headers: Dict[str, str], backend: str, timeout: float) -> int:
    errors = 0
    ns_counts: Dict[str, int] = defaultdict(int)
    ns_bytes: Dict[str, int] = defaultdict(int)

    for task in tasks:
        final_path = mount / task.rel_path
        if final_path.exists():
            existing_sha, existing_size = sha256_of_file(final_path)
            if existing_sha == task.sha256 and existing_size == task.size_bytes:
                task.on_verified()
                ns_counts[task.namespace] += 1
                ns_bytes[task.namespace] += task.size_bytes
                print(f"[skip] {task.label} already on card, sha256 ok")
                continue

        if dry_run:
            print(f"[plan] {task.label} -> {task.rel_path} ({task.size_bytes} B)")
            continue

        temp_path = mount / task.temp_rel_path
        url = resolve_url(backend, task.audio_url)
        try:
            got_sha, got_size = download(url, headers, temp_path, timeout)
        except (urllib.error.URLError, OSError) as e:
            print(f"[error] {task.label} download failed: {e}", file=sys.stderr)
            errors += 1
            continue

        if got_sha.lower() != task.sha256 or got_size != task.size_bytes:
            print(f"[error] {task.label} sha256/size mismatch "
                  f"(expected {task.sha256}/{task.size_bytes}, "
                  f"got {got_sha}/{got_size})", file=sys.stderr)
            try:
                temp_path.unlink()
            except OSError:
                pass
            errors += 1
            continue

        final_path.parent.mkdir(parents=True, exist_ok=True)
        os.replace(temp_path, final_path)
        task.on_verified()
        ns_counts[task.namespace] += 1
        ns_bytes[task.namespace] += task.size_bytes
        print(f"[ok] {task.label} -> {task.rel_path}")

    if dry_run:
        print_summary(ns_counts, ns_bytes, header="plan")
        return 1 if errors else 0

    swept = clean_stray_parts(mount)
    if swept:
        print(f"[clean] removed {swept} stray .part file(s)")

    index_path = mount / "content_index.json"
    tmp_index_path = mount / "content_index.json.new"
    tmp_index_path.write_text(json.dumps(index, ensure_ascii=False), encoding="utf-8")
    os.replace(tmp_index_path, index_path)
    print(f"[index] wrote {index_path} (schemaVersion={index['schemaVersion']})")

    print_summary(ns_counts, ns_bytes, header="load")
    if errors:
        print(f"FAIL - {errors} item(s) failed sha256/size verification", file=sys.stderr)
        return 1
    return 0


def do_verify(mount: Path, tasks: List[DownloadTask]) -> int:
    errors = 0
    ns_counts: Dict[str, int] = defaultdict(int)
    ns_bytes: Dict[str, int] = defaultdict(int)
    for task in tasks:
        final_path = mount / task.rel_path
        if not final_path.exists():
            print(f"[missing] {task.label} -> {task.rel_path}", file=sys.stderr)
            errors += 1
            continue
        got_sha, got_size = sha256_of_file(final_path)
        if got_sha.lower() != task.sha256 or got_size != task.size_bytes:
            print(f"[mismatch] {task.label} -> {task.rel_path}", file=sys.stderr)
            errors += 1
            continue
        ns_counts[task.namespace] += 1
        ns_bytes[task.namespace] += task.size_bytes
        print(f"[ok] {task.label}")
    print_summary(ns_counts, ns_bytes, header="verify")
    if errors:
        print(f"FAIL - {errors} item(s) missing or mismatched", file=sys.stderr)
        return 1
    return 0


# ---- CLI --------------------------------------------------------------------

def build_arg_parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--backend", required=True, help="e.g. http://192.168.1.50:5000")
    ap.add_argument("--mount", required=True, type=Path,
                     help="the mounted SD card's root directory")
    ap.add_argument("--device-info", type=Path, default=None,
                     help="JSON file shaped like a POST /api/devices/register "
                          "response (deviceId + apiKey) -- see USAGE above; "
                          "the documented path is AREG_DEVICE_ID/AREG_DEVICE_KEY")
    ap.add_argument("--dry-run", action="store_true",
                     help="print the plan, write nothing to the card")
    ap.add_argument("--verify", action="store_true",
                     help="check an already-loaded card against the manifest; "
                          "write nothing")
    ap.add_argument("--timeout", type=float, default=60.0,
                     help="per-request timeout in seconds (default 60)")
    return ap


def main(argv: Optional[List[str]] = None,
         fetch_json: Callable[[str, Dict[str, str], float], Any] = default_fetch_json,
         download: Callable[[str, Dict[str, str], Path, float], Tuple[str, int]] = default_download
         ) -> int:
    args = build_arg_parser().parse_args(argv)

    try:
        device_id, api_key = resolve_credentials(args)
    except ToolError as e:
        print(f"FAIL - {e}", file=sys.stderr)
        return 1

    if args.dry_run and args.verify:
        print("FAIL - --dry-run and --verify are mutually exclusive", file=sys.stderr)
        return 1

    headers = {"X-Device-Id": device_id, "X-Api-Key": api_key}
    print(f"[device] id={device_id}")

    manifest_url = resolve_url(args.backend, CONTENT_MANIFEST_PATH)
    try:
        manifest = fetch_json(manifest_url, headers, args.timeout)
    except (urllib.error.URLError, OSError, json.JSONDecodeError) as e:
        print(f"FAIL - could not fetch content-manifest: {e}", file=sys.stderr)
        return 1

    index, tasks, stats = build_index_and_tasks(manifest)
    skipped = sum(stats.values())
    if skipped:
        print(f"[manifest] skipped {skipped} item(s): "
              f"{dict(sorted(stats.items()))}")

    if not tasks:
        print("[manifest] nothing enabled to load -- is ContentSync configured "
              "and this device entitled?", file=sys.stderr)

    if args.verify:
        return do_verify(args.mount, tasks)

    return do_load(args.mount, index, tasks, args.dry_run, download,
                    headers, args.backend, args.timeout)


if __name__ == "__main__":
    sys.exit(main())
