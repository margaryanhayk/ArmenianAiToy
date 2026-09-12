#!/usr/bin/env python3
"""Proves load_sd_card.py's on-card contract, in the same dependency-free
style as tools/firmware/test_check_release_image.py: stdlib unittest only,
a temp directory standing in for the mounted SD card, and an injected
fetch_json/download pair standing in for the network -- no real HTTP, no
toolchain, so this never gets skipped.

USAGE
    python3 tools/factory/test_load_sd_card.py
"""
from __future__ import annotations

import hashlib
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import load_sd_card as sut  # noqa: E402


def _sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


# Deterministic file contents, one per manifest item below.
STORY_BYTES = b"story bytes" * 100
CLIP_BYTES = b"clip bytes" * 10
MUSIC_BYTES = b"music bytes" * 200
VOICE_BYTES = b"voice bytes" * 5
GAME_BYTES = b"game bytes" * 5

BACKEND = "http://backend.example"


def sample_manifest() -> dict:
    return {
        "storyIntroEnabled": True,
        "bedtimeMusicEnabled": True,
        "storyEnabled": True,
        "gameEnabled": True,
        "riddleEnabled": True,
        "curiosityEnabled": True,
        "storyPausesEnabled": True,
        "variantEndingsEnabled": True,
        "storyQuestionsEnabled": True,
        "stories": [
            {
                "storyId": "little-cloud",
                "version": 3,
                "title": "Cloud",
                "audioUrl": "/api/devices/content-file?storyId=little-cloud",
                "sha256": _sha(STORY_BYTES),
                "sizeBytes": len(STORY_BYTES),
                "enabled": True,
                "retired": False,
                "clips": [
                    {
                        "kind": "intro",
                        "audioUrl": "/api/devices/content-file?storyId=little-cloud&clip=intro",
                        "sha256": _sha(CLIP_BYTES),
                        "sizeBytes": len(CLIP_BYTES),
                    },
                ],
            },
            {
                "storyId": "retired-one",
                "version": 1,
                "title": "Retired",
                "audioUrl": "/api/devices/content-file?storyId=retired-one",
                "sha256": _sha(b"whatever"),
                "sizeBytes": 8,
                "enabled": False,
                "retired": True,
            },
            {
                "storyId": "disabled-one",
                "version": 1,
                "title": "Disabled",
                "audioUrl": "/api/devices/content-file?storyId=disabled-one",
                "sha256": _sha(b"whatever2"),
                "sizeBytes": 9,
                "enabled": False,
                "retired": False,
            },
        ],
        "music": [
            {
                "trackId": "lullaby-melody",
                "version": 1,
                "title": "Lullaby",
                "audioUrl": "/api/devices/content-file?trackId=lullaby-melody",
                "sha256": _sha(MUSIC_BYTES),
                "sizeBytes": len(MUSIC_BYTES),
                "enabled": True,
                "retired": False,
            },
        ],
        "voice": [
            {
                "voiceId": "greeting-1",
                "version": 2,
                "audioUrl": "/api/devices/content-file?voiceId=greeting-1",
                "sha256": _sha(VOICE_BYTES),
                "sizeBytes": len(VOICE_BYTES),
                "enabled": True,
                "retired": False,
            },
        ],
        "games": [
            {
                "gameKey": "mindreader",
                "clipId": "intro",
                "version": 1,
                "audioUrl": "/api/devices/content-file?gameKey=mindreader&clipId=intro",
                "sha256": _sha(GAME_BYTES),
                "sizeBytes": len(GAME_BYTES),
                "enabled": True,
            },
        ],
    }


CONTENT_BY_PATH = {
    "/api/devices/content-file?storyId=little-cloud": STORY_BYTES,
    "/api/devices/content-file?storyId=little-cloud&clip=intro": CLIP_BYTES,
    "/api/devices/content-file?trackId=lullaby-melody": MUSIC_BYTES,
    "/api/devices/content-file?voiceId=greeting-1": VOICE_BYTES,
    "/api/devices/content-file?gameKey=mindreader&clipId=intro": GAME_BYTES,
}


def make_fetch_json(manifest: dict, seen_headers: list):
    def _fetch_json(url, headers, timeout):
        seen_headers.append(dict(headers))
        assert url == BACKEND + "/api/devices/content-manifest", url
        return manifest
    return _fetch_json


def make_download(content_by_url: dict, wrong_sha_for: set = frozenset()):
    def _download(url, headers, dest, timeout):
        # url is backend + relative path; strip the backend prefix to look
        # the fixture content up by its original manifest path.
        path = url[len(BACKEND):]
        content = content_by_url[path]
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(content)
        if path in wrong_sha_for:
            return _sha(b"not-the-right-bytes"), len(content)
        return _sha(content), len(content)
    return _download


class BuildIndexAndTasksTests(unittest.TestCase):
    """The index shape this tool writes must match what content_sync_model.cpp's
    cs_index_build / cs_index_add_music / cs_index_add_voice / cs_index_add_games
    / cs_index_add_modes / cs_index_add_story_flags / cs_index_add_questions_flag
    / cs_index_add_orphan_sweep_flag produce."""

    def test_schema_version_and_root_flags(self):
        index, tasks, stats = sut.build_index_and_tasks(sample_manifest())
        self.assertEqual(index["schemaVersion"], 7)
        self.assertTrue(index["introEnabled"])
        self.assertTrue(index["musicEnabled"])
        for flag in ("storyEnabled", "gameEnabled", "riddleEnabled",
                     "curiosityEnabled", "pausesEnabled", "variantsEnabled",
                     "questionsEnabled"):
            self.assertTrue(index[flag], flag)
        # Never sent by the backend today (content_sync.cpp:1654 default) --
        # always false here, matching the shipped server behavior exactly.
        self.assertFalse(index["orphanSweepEnabled"])

    def test_story_entry_shape_and_cache_path(self):
        index, tasks, stats = sut.build_index_and_tasks(sample_manifest())
        stories = index["stories"]
        self.assertEqual(len(stories), 1, "retired and disabled stories must be dropped")
        entry = stories[0]
        self.assertEqual(entry["storyId"], "little-cloud")
        self.assertEqual(entry["version"], 3)
        self.assertEqual(entry["cachePath"], "/stories/little-cloud-v3.mp3")
        self.assertFalse(entry["verified"], "not verified until actually downloaded")
        self.assertEqual(len(entry["clips"]), 1)
        self.assertEqual(entry["clips"][0]["kind"], "intro")
        self.assertNotIn("cachePath", entry["clips"][0],
                          "clip cache paths are derived, never stored (matches "
                          "cs_index_build, content_sync_model.cpp:372-381)")

    def test_retired_and_disabled_are_both_dropped(self):
        index, tasks, stats = sut.build_index_and_tasks(sample_manifest())
        ids = {s["storyId"] for s in index["stories"]}
        self.assertNotIn("retired-one", ids)
        self.assertNotIn("disabled-one", ids)
        self.assertEqual(stats.get("skipped_retired"), 1)
        self.assertEqual(stats.get("skipped_disabled"), 1)

    def test_music_voice_games_present_only_when_nonempty(self):
        manifest = sample_manifest()
        manifest["music"] = []
        manifest["voice"] = []
        manifest["games"] = []
        index, tasks, stats = sut.build_index_and_tasks(manifest)
        self.assertNotIn("music", index)
        self.assertNotIn("voice", index)
        self.assertNotIn("games", index)
        # musicEnabled is still always written (cs_index_add_music always
        # sets it, content_sync_model.cpp:496), independent of the list.
        self.assertIn("musicEnabled", index)

    def test_game_cache_path_has_no_version(self):
        index, tasks, stats = sut.build_index_and_tasks(sample_manifest())
        game_task = next(t for t in tasks if t.namespace == "games")
        self.assertEqual(game_task.rel_path, "games/mindreader/intro.mp3")

    def test_invalid_story_id_is_skipped_not_fatal(self):
        manifest = sample_manifest()
        manifest["stories"].append({
            "storyId": "BAD ID!", "version": 1, "title": "x",
            "audioUrl": "/api/devices/content-file?storyId=bad",
            "sha256": _sha(b"x"), "sizeBytes": 1, "enabled": True,
        })
        index, tasks, stats = sut.build_index_and_tasks(manifest)
        ids = {s["storyId"] for s in index["stories"]}
        self.assertNotIn("BAD ID!", ids)
        self.assertGreaterEqual(stats.get("skipped_invalid", 0), 1)


class LoadAndVerifyTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.mount = Path(self._tmp.name)

    def tearDown(self):
        self._tmp.cleanup()

    def _run(self, extra_args, fetch_json=None, download=None, manifest=None):
        manifest = manifest if manifest is not None else sample_manifest()
        seen_headers: list = []
        fetch_json = fetch_json or make_fetch_json(manifest, seen_headers)
        download = download or make_download(CONTENT_BY_PATH)
        argv = ["--backend", BACKEND, "--mount", str(self.mount)] + extra_args
        os.environ["AREG_DEVICE_ID"] = "11111111-1111-1111-1111-111111111111"
        os.environ["AREG_DEVICE_KEY"] = "test-key-not-real"
        code = sut.main(argv, fetch_json=fetch_json, download=download)
        return code, seen_headers

    def test_full_load_writes_index_and_files(self):
        code, headers = self._run([])
        self.assertEqual(code, 0)
        self.assertEqual(headers[0]["X-Device-Id"], "11111111-1111-1111-1111-111111111111")
        self.assertEqual(headers[0]["X-Api-Key"], "test-key-not-real")

        story_path = self.mount / "stories" / "little-cloud-v3.mp3"
        clip_path = self.mount / "stories" / "little-cloud-v3-intro.mp3"
        music_path = self.mount / "music" / "lullaby-melody-v1.mp3"
        voice_path = self.mount / "voice" / "greeting-1-v2.mp3"
        game_path = self.mount / "games" / "mindreader" / "intro.mp3"
        for p in (story_path, clip_path, music_path, voice_path, game_path):
            self.assertTrue(p.exists(), p)

        index = json.loads((self.mount / "content_index.json").read_text())
        self.assertEqual(index["schemaVersion"], 7)
        self.assertTrue(index["stories"][0]["verified"],
                         "verified must flip true only after a real sha check")
        self.assertTrue(index["stories"][0]["clips"][0]["verified"])
        self.assertTrue(index["music"][0]["verified"])
        self.assertTrue(index["voice"][0]["verified"])
        self.assertTrue(index["games"][0]["verified"])

        # No stray .part files after a clean run.
        tmp_dir = self.mount / "tmp"
        self.assertFalse(list(tmp_dir.glob("*.part")) if tmp_dir.exists() else [])

    def test_sha_mismatch_is_nonzero_and_leaves_no_part_file(self):
        bad_download = make_download(
            CONTENT_BY_PATH, wrong_sha_for={"/api/devices/content-file?storyId=little-cloud"})
        code, _ = self._run([], download=bad_download)
        self.assertNotEqual(code, 0)
        self.assertFalse((self.mount / "stories" / "little-cloud-v3.mp3").exists())
        self.assertFalse((self.mount / "tmp" / "little-cloud-v3.mp3.part").exists(),
                          "a failed verification must not leave a .part behind")

    def test_idempotent_rerun_skips_files_already_on_card(self):
        code, _ = self._run([])
        self.assertEqual(code, 0)

        calls = []

        def counting_download(url, headers, dest, timeout):
            calls.append(url)
            raise AssertionError("download must not be called when the file "
                                  "already matches the manifest's sha256")

        code2, _ = self._run([], download=counting_download)
        self.assertEqual(code2, 0)
        self.assertEqual(calls, [])

    def test_dry_run_writes_nothing(self):
        code, _ = self._run(["--dry-run"])
        self.assertEqual(code, 0)
        self.assertFalse((self.mount / "content_index.json").exists())
        self.assertFalse((self.mount / "stories").exists())

    def test_verify_mode_reports_missing_card_as_failure(self):
        code, _ = self._run(["--verify"])
        self.assertNotEqual(code, 0, "an empty card must fail --verify")

    def test_verify_mode_passes_after_a_real_load(self):
        code, _ = self._run([])
        self.assertEqual(code, 0)
        code2, _ = self._run(["--verify"])
        self.assertEqual(code2, 0)


class CredentialResolutionTests(unittest.TestCase):
    def setUp(self):
        self._saved = {k: os.environ.get(k) for k in ("AREG_DEVICE_ID", "AREG_DEVICE_KEY")}
        os.environ.pop("AREG_DEVICE_ID", None)
        os.environ.pop("AREG_DEVICE_KEY", None)

    def tearDown(self):
        for k, v in self._saved.items():
            if v is None:
                os.environ.pop(k, None)
            else:
                os.environ[k] = v

    def _args(self, device_info=None):
        return sut.build_arg_parser().parse_args([
            "--backend", BACKEND, "--mount", "/tmp/whatever",
            *(["--device-info", str(device_info)] if device_info else []),
        ])

    def test_missing_credentials_raises(self):
        with self.assertRaises(sut.ToolError):
            sut.resolve_credentials(self._args())

    def test_env_credentials(self):
        os.environ["AREG_DEVICE_ID"] = "dev-1"
        os.environ["AREG_DEVICE_KEY"] = "key-1"
        device_id, api_key = sut.resolve_credentials(self._args())
        self.assertEqual((device_id, api_key), ("dev-1", "key-1"))

    def test_device_info_file_credentials(self):
        with tempfile.TemporaryDirectory() as td:
            info_path = Path(td) / "register_response.json"
            info_path.write_text(json.dumps({
                "deviceId": "dev-2", "apiKey": "key-2",
                "claimCode": "ABC123", "pop": "POPPOPOP",
            }))
            device_id, api_key = sut.resolve_credentials(self._args(info_path))
            self.assertEqual((device_id, api_key), ("dev-2", "key-2"))


class UrlAndPathHelperTests(unittest.TestCase):
    def test_resolve_url_joins_relative_path(self):
        self.assertEqual(
            sut.resolve_url("http://host:5000", "/api/devices/content-file?storyId=x"),
            "http://host:5000/api/devices/content-file?storyId=x")

    def test_resolve_url_passes_through_absolute(self):
        self.assertEqual(
            sut.resolve_url("http://host:5000", "https://other/x"), "https://other/x")

    def test_cache_path_naming_matches_firmware(self):
        self.assertEqual(sut.story_cache_path("foo", 2), "stories/foo-v2.mp3")
        self.assertEqual(sut.story_clip_cache_path("foo", 2, "intro"),
                          "stories/foo-v2-intro.mp3")
        self.assertEqual(sut.music_cache_path("bar", 1), "music/bar-v1.mp3")
        self.assertEqual(sut.voice_cache_path("baz", 3), "voice/baz-v3.mp3")
        self.assertEqual(sut.game_cache_path("simon", "intro"), "games/simon/intro.mp3")

    def test_temp_path_prefixes_match_firmware(self):
        self.assertEqual(sut.story_temp_path("foo", 2), "tmp/foo-v2.mp3.part")
        self.assertEqual(sut.music_temp_path("bar", 1), "tmp/m-bar-v1.mp3.part")
        self.assertEqual(sut.voice_temp_path("baz", 3), "tmp/v-baz-v3.mp3.part")
        self.assertEqual(sut.game_temp_path("simon", "intro"), "tmp/g-simon-intro.mp3.part")

    def test_normalize_version_clamps(self):
        self.assertEqual(sut.normalize_version(0), 1)
        self.assertEqual(sut.normalize_version(-5), 1)
        self.assertEqual(sut.normalize_version(999999), 99999)
        self.assertEqual(sut.normalize_version(42), 42)


if __name__ == "__main__":
    unittest.main()
