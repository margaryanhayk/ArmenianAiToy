#!/usr/bin/env python3
"""Proves listen_test.py's contract, in the same dependency-free style as
tools/factory/test_load_sd_card.py: stdlib unittest only, temp directories
standing in for the repo, tiny fake files instead of audio, and the player,
the prompt and ffprobe patched out -- no ffplay, no speakers, no human needed,
so this never gets skipped.

USAGE
    python3 -I tools/listen/test_listen_test.py -v
"""
from __future__ import annotations

import contextlib
import hashlib
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))

import listen_test as lt  # noqa: E402

DATE = "20261022"


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def entry(data: bytes, **extra) -> dict:
    return {"SizeBytes": len(data), "Sha256": sha(data), **extra}


class World:
    """A fake repo: appsettings.json plus real (tiny) files under story-audio/."""

    def __init__(self, tmp: Path):
        self.api = tmp / "api"
        self.audio = self.api / "story-audio"
        self.evidence = tmp / "evidence"
        self.appsettings = self.api / "appsettings.json"
        self.bytes: dict[str, bytes] = {}
        files = {"s1.mp3": b"story-one", "s1-alt.mp3": b"story-one-alt", "clips/s1/intro.mp3": b"intro",
                 "clips/s1/question.mp3": b"question", "voice/hello.mp3": b"hello",
                 "games/g/win.mp3": b"win", "music/m1.mp3": b"music-one"}
        for rel, data in files.items():
            self.write(rel, data)
        e = lambda rel, **x: {"AudioPath": rel, **entry(self.bytes[rel], **x)}  # noqa: E731
        self.config = {"ContentSync": {
            "Enabled": True, "AudioRoot": "story-audio",
            "Stories": [
                {"StoryId": "s1", **e("s1.mp3"), "Clips": [{"Kind": "intro", **e("clips/s1/intro.mp3")},
                                                            {"Kind": "question", **e("clips/s1/question.mp3")}]},
                {"StoryId": "s1-alt", "AltOf": "s1", **e("s1-alt.mp3")},
                {"StoryId": "gone", **e("s1.mp3", Retired=True), "Clips": [{"Kind": "intro", **e("clips/s1/intro.mp3")}]},
                {"StoryId": "placeholder", "AudioPath": "ph.mp3", "SizeBytes": 0, "Sha256": "0" * 64,
                 "Clips": [{"Kind": "intro", **e("clips/s1/intro.mp3")}]},
                {"StoryId": "S1", **e("s1.mp3")},  # duplicate id (case-insensitive): dropped
                {"StoryId": "escape", "AudioPath": "../outside.mp3", "SizeBytes": 3, "Sha256": sha(b"x")},
            ],
            "Voice": [{"VoiceId": "hello", **e("voice/hello.mp3")},
                      {"VoiceId": "badsha", "AudioPath": "voice/hello.mp3", "SizeBytes": 5, "Sha256": "abc"}],
            "Games": [{"GameKey": "g", "ClipId": "win", **e("games/g/win.mp3")},
                      {"GameKey": "g", "ClipId": "", **e("games/g/win.mp3")}],
            "Music": [{"TrackId": "m1", **e("music/m1.mp3")}],
        }}
        self.save()

    def write(self, rel: str, data: bytes) -> Path:
        p = self.audio / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)
        self.bytes[rel] = data
        return p

    def save(self) -> None:
        self.appsettings.write_text(json.dumps(self.config), encoding="utf-8")

    def items(self) -> list[lt.Item]:
        return lt.load_catalogue(self.config, lt.resolve_audio_root("story-audio", self.appsettings))

    def args(self, cmd: str, *extra: str) -> list[str]:
        return [cmd, "--appsettings", str(self.appsettings), "--evidence-dir", str(self.evidence),
                "--date", DATE, *extra]

    def snapshot(self) -> dict[str, str]:
        files = [self.appsettings, *self.audio.rglob("*.mp3")]
        return {str(p): sha(p.read_bytes()) for p in files}


def row(item: lt.Item, verdict: str, data: bytes | None = None, date: str = DATE, device: str = "PC headphones",
        note: str = "") -> lt.Row:
    digest = sha(data) if data is not None else item.sha256
    return lt.Row(item.id, item.group, item.path, digest, 1.5, verdict, note, date, "Tester", device, "ffplay", "abc1234")


class WorldCase(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.w = World(Path(self._tmp.name))


class CatalogueTests(WorldCase):
    def test_groups_and_ids_come_from_the_catalogue(self):
        got = [(i.group, i.id) for i in self.w.items()]
        self.assertEqual(got, [
            ("stories", "s1"), ("alts", "s1-alt"),
            ("clips", "s1/intro"), ("clips", "s1/question"),
            ("voice", "hello"), ("games", "g/win"), ("music", "m1")])

    def test_retired_placeholder_duplicate_badsha_and_traversal_are_not_advertised(self):
        ids = {i.id for i in self.w.items()}
        for dropped in ("gone", "placeholder", "S1", "escape", "badsha", "g/"):
            self.assertNotIn(dropped, ids)
        self.assertEqual(sum(i.id.startswith("s1/") for i in self.w.items()), 2)  # a dropped story's clips go too

    def test_disabled_catalogue_advertises_nothing(self):
        self.w.config["ContentSync"]["Enabled"] = False
        self.assertEqual(self.w.items(), [])

    def test_files_resolve_under_audio_root_next_to_appsettings(self):
        by_id = {i.id: i for i in self.w.items()}
        self.assertEqual(by_id["s1"].file, (self.w.audio / "s1.mp3").resolve())
        self.assertEqual(by_id["s1/intro"].file, (self.w.audio / "clips/s1/intro.mp3").resolve())
        self.assertEqual(by_id["s1"].path, "s1.mp3")  # the evidence key stays the catalogue's spelling
        self.assertTrue(all(i.file.is_file() for i in by_id.values()))

    def test_absolute_audio_root_is_used_as_is(self):
        self.assertEqual(lt.resolve_audio_root(str(self.w.audio), self.w.appsettings), self.w.audio)

    def test_select_group_and_only(self):
        items = self.w.items()
        self.assertEqual([i.id for i in lt.select_group(items, "clips")], ["s1/intro", "s1/question"])
        self.assertEqual(len(lt.select_group(items, "all")), len(items))
        self.assertEqual([i.id for i in lt.select_group(items, "all", "ALT")], ["s1-alt"])
        self.assertEqual(lt.select_group(items, "music", "nope"), [])


class EvidenceTests(WorldCase):
    def test_row_round_trips_and_pipes_cannot_break_the_table(self):
        it = self.w.items()[0]
        text = lt.render_session(DATE, {"listener": "A", "device": "D", "player": "P", "commit": "c"}, True)
        text += lt.render_row(row(it, "FAIL", note="click | at\n0:42")) + "\n"
        rows, ctx = lt.parse_evidence(text, DATE)
        self.assertEqual(len(rows), 1)
        self.assertEqual((rows[0].id, rows[0].verdict, rows[0].note, rows[0].seconds), ("s1", "FAIL", "click / at 0:42", 1.5))
        self.assertEqual((rows[0].listener, rows[0].device, rows[0].commit), ("A", "D", "c"))
        self.assertEqual(ctx["player"], "P")

    def test_unknown_seconds_is_a_question_mark_and_parses_back_to_none(self):
        it = self.w.items()[0]
        r = lt.Row(it.id, it.group, it.path, it.sha256, None, "PASS", "")
        self.assertIn("| ? | PASS |", lt.render_row(r))
        self.assertIsNone(lt.parse_evidence(lt.render_row(r), DATE)[0][0].seconds)

    def test_parse_ignores_header_rule_prose_and_malformed_rows(self):
        good = lt.render_row(row(self.w.items()[0], "PASS"))
        text = "\n".join([
            "# Listen test", "some prose | with a pipe", "| id | namespace | path | sha256 | seconds | verdict | note |",
            "|---|---|---|---|---|---|---|", good,
            "| s | stories | p.mp3 | nothex | 1 | PASS | |",
            f"| s | stories | p.mp3 | {'a' * 64} | 1 | MAYBE | |",
            f"| too | few | cells | {'a' * 64} |"])
        self.assertEqual(len(lt.parse_evidence(text, DATE)[0]), 1)

    def test_rendering_is_deterministic(self):
        r = row(self.w.items()[0], "PASS")
        self.assertEqual(lt.render_row(r), lt.render_row(r))
        self.assertEqual(lt.render_row(r), f"| s1 | stories | s1.mp3 | {r.sha256} | 1.5 | PASS |  |")

    def test_start_session_writes_one_header_and_a_new_block_only_when_context_changes(self):
        path = lt.evidence_path(self.w.evidence, DATE)
        ctx = {"listener": "Hayk", "device": "PC headphones", "player": "ffplay", "commit": "abc1234"}
        items = self.w.items()
        lt.start_session(path, DATE, ctx)
        lt.append_row(path, row(items[0], "PASS"))
        lt.start_session(path, DATE, ctx)  # resumed with the same context: nothing new
        self.assertEqual(path.read_text(encoding="utf-8").count("- **Device:**"), 1)
        lt.start_session(path, DATE, {**ctx, "device": "toy speaker"})
        lt.append_row(path, row(items[1], "PASS", device="toy speaker"))
        text = path.read_text(encoding="utf-8")
        self.assertEqual(text.count("- **Device:**"), 2)
        self.assertIn("## New session", text)
        rows, _ = lt.parse_evidence(text, DATE)
        self.assertEqual([(r.id, r.device) for r in rows], [("s1", "PC headphones"), ("s1-alt", "toy speaker")])
        self.assertTrue(text.endswith("\n"))
        self.assertNotIn("\r", text)

    def test_append_adds_the_missing_newline_of_a_hand_edited_file(self):
        path = lt.evidence_path(self.w.evidence, DATE)
        self.w.evidence.mkdir(parents=True)
        path.write_bytes(b"# hand edited, no trailing newline")
        lt.append_row(path, row(self.w.items()[0], "PASS"))
        self.assertEqual(len(lt.parse_evidence(path.read_text(encoding="utf-8"), DATE)[0]), 1)

    def test_load_evidence_orders_by_file_date_and_ignores_other_files(self):
        it = self.w.items()[0]
        self.w.evidence.mkdir(parents=True)
        for name, verdict in (("listen-test-20261021.md", "FAIL"), ("listen-test-20261020.md", "PASS")):
            (self.w.evidence / name).write_text(lt.render_row(row(it, verdict)) + "\n", encoding="utf-8")
        (self.w.evidence / "anban-huri-listen-test-TEMPLATE.md").write_text(lt.render_row(row(it, "PASS")))
        rows = lt.load_evidence(self.w.evidence)
        self.assertEqual([(r.date, r.verdict) for r in rows], [("20261020", "PASS"), ("20261021", "FAIL")])
        self.assertEqual(lt.load_evidence(self.w.evidence / "missing"), [])


class StatusTests(WorldCase):
    def states(self, rows, mutate=None) -> dict[str, str]:
        items = self.w.items()
        shas = {i.path: sha(self.w.bytes[i.path]) for i in items}
        if mutate:
            shas.update(mutate)
        return {r.item.id: r.state for r in lt.compute_status(items, rows, shas)}

    def test_all_five_states(self):
        by_id = {i.id: i for i in self.w.items()}
        rows = [row(by_id["s1"], "PASS"), row(by_id["s1-alt"], "FAIL"), row(by_id["hello"], "PASS"),
                row(by_id["g/win"], "PASS")]
        got = self.states(rows, {"voice/hello.mp3": sha(b"re-rendered"), "games/g/win.mp3": None})
        self.assertEqual(got["s1"], "APPROVED")
        self.assertEqual(got["s1-alt"], "FAILED")
        self.assertEqual(got["hello"], "CHANGED")
        self.assertEqual(got["g/win"], "MISSING")
        self.assertEqual(got["m1"], "NEVER HEARD")

    def test_latest_verdict_on_the_current_bytes_wins(self):
        s1 = self.w.items()[0]
        self.assertEqual(self.states([row(s1, "PASS"), row(s1, "FAIL")])["s1"], "FAILED")
        self.assertEqual(self.states([row(s1, "FAIL"), row(s1, "PASS")])["s1"], "APPROVED")

    def test_skip_never_erases_or_creates_an_approval(self):
        s1, alt = self.w.items()[:2]
        got = self.states([row(s1, "PASS"), row(s1, "SKIP"), row(alt, "SKIP")])
        self.assertEqual((got["s1"], got["s1-alt"]), ("APPROVED", "NEVER HEARD"))

    def test_approval_is_pinned_to_bytes_and_returns_with_them(self):
        s1 = self.w.items()[0]
        old, new = b"story-one", b"story-one-rerender"
        self.assertEqual(self.states([row(s1, "PASS", old)], {"s1.mp3": sha(new)})["s1"], "CHANGED")
        # a FAIL on the new bytes does not void the old bytes' PASS if the old bytes come back
        rows = [row(s1, "PASS", old), row(s1, "FAIL", new)]
        self.assertEqual(self.states(rows, {"s1.mp3": sha(old)})["s1"], "APPROVED")
        self.assertEqual(self.states(rows, {"s1.mp3": sha(new)})["s1"], "FAILED")
        # heard-and-failed, then re-rendered: it must be heard again
        self.assertEqual(self.states([row(s1, "FAIL", old)], {"s1.mp3": sha(new)})["s1"], "CHANGED")

    def test_file_order_across_days_is_date_then_row_order(self):
        s1 = self.w.items()[0]
        self.w.evidence.mkdir(parents=True)
        for day, verdict in (("20261023", "PASS"), ("20261022", "FAIL")):
            (self.w.evidence / f"listen-test-{day}.md").write_text(lt.render_row(row(s1, verdict, date=day)) + "\n")
        self.assertEqual(self.states(lt.load_evidence(self.w.evidence))["s1"], "APPROVED")

    def test_still_to_hear_sums_unapproved_and_flags_estimates(self):
        items = self.w.items()
        results = lt.compute_status(items, [row(items[0], "PASS")], {i.path: i.sha256 for i in items})
        seconds = {i.path: 60.0 for i in items}
        total, estimated = lt.still_to_hear(results, seconds)
        self.assertEqual((total, estimated), (60.0 * (len(items) - 1), 0))
        _, estimated = lt.still_to_hear(results, {})
        self.assertEqual(estimated, len(items) - 1)

    def test_table_and_summary_text(self):
        items = self.w.items()
        results = lt.compute_status(items, [row(items[0], "PASS")], {i.path: i.sha256 for i in items})
        table = lt.format_table(results, {i.path: 125.0 for i in items})
        self.assertIn("APPROVED", table)
        self.assertIn("2:05", table)
        self.assertIn("PASS 20261022 (PC headphones)", table)
        self.assertNotIn("APPROVED", lt.format_table(results, {}, hide_approved=True))
        summary = lt.format_summary(results, {i.path: 60.0 for i in items})
        self.assertIn("APPROVED 1", summary)
        self.assertIn("NEVER HEARD 6", summary)
        self.assertIn("Still to hear: 6 files, 6.0 minutes", summary)


class PendingTests(WorldCase):
    def test_pending_rules(self):
        items = self.w.items()
        s1, alt, intro = items[0], items[1], items[2]
        shas = {i.path: i.sha256 for i in items}
        shas["games/g/win.mp3"] = None
        rows = [row(s1, "PASS", date="20261001"),                        # approved on an earlier day
                row(alt, "SKIP"),                                        # answered today, this device
                row(intro, "PASS", device="toy speaker")]               # answered today, other device
        results = lt.compute_status(items, rows, shas)
        got = {i.id for i in lt.pending_items(results, rows, DATE, "pc HEADPHONES")}
        self.assertNotIn("s1", got)         # already approved
        self.assertNotIn("s1-alt", got)     # skipped today on this device: not nagged again
        self.assertNotIn("g/win", got)      # missing: cannot be played
        self.assertIn("m1", got)
        self.assertNotIn("s1/intro", got)   # approved on the toy speaker, same bytes: approval is not per device
        again = {i.id for i in lt.pending_items(results, rows, DATE, "pc headphones", again=True)}
        self.assertIn("s1", again)          # --again re-asks the approved...
        self.assertIn("s1/intro", again)
        self.assertNotIn("s1-alt", again)   # ...but not what was already answered today on this device


class PlayerTests(unittest.TestCase):
    def test_defaults(self):
        f = Path("/a b/x.mp3")
        self.assertEqual(lt.build_player_command("ffplay", f),
                         ["ffplay", "-nodisp", "-autoexit", "-loglevel", "error", str(f)])
        self.assertEqual(lt.build_player_command("mpv", f)[-1], str(f))

    def test_custom_command_keeps_paths_with_spaces_and_backslashes_whole(self):
        f = Path("C:/Users/Some One/audio/x.mp3")
        cmd = lt.build_player_command('vlc --intf dummy "{file}" vlc://quit', f, posix=True)
        self.assertEqual(cmd, ["vlc", "--intf", "dummy", str(f), "vlc://quit"])
        win = lt.build_player_command('"C:\\Program Files\\VLC\\vlc.exe" --play-and-exit {file}', f, posix=False)
        self.assertEqual(win, ["C:\\Program Files\\VLC\\vlc.exe", "--play-and-exit", str(f)])

    def test_custom_command_without_placeholder_is_refused(self):
        with self.assertRaises(ValueError):
            lt.build_player_command("vlc", Path("x.mp3"))

    def test_parse_answer(self):
        self.assertEqual(lt.parse_answer("p"), ("PASS", ""))
        self.assertEqual(lt.parse_answer(" F click at 0:42 "), ("FAIL", "click at 0:42"))
        self.assertEqual(lt.parse_answer("pass nice"), ("PASS", "nice"))
        self.assertEqual(lt.parse_answer("s")[0], "SKIP")
        self.assertEqual(lt.parse_answer("r")[0], "REPLAY")
        self.assertEqual(lt.parse_answer("QUIT")[0], "QUIT")
        for junk in ("", "x", "fclick", "maybe"):
            self.assertIsNone(lt.parse_answer(junk))

    def test_hear_replays_on_r_but_not_on_junk(self):
        with mock.patch.object(lt, "play_file") as play, mock.patch("builtins.input", side_effect=["r", "huh", "p nice"]):
            self.assertEqual(lt.hear(["player"]), ("PASS", "nice"))
        self.assertEqual(play.call_count, 2)  # first play + the replay; 'huh' only re-prompted


class CommandTests(WorldCase):
    def run_main(self, *argv, answers=(), **patches):
        out = io.StringIO()
        with contextlib.redirect_stdout(out), \
                mock.patch.object(lt, "play_file") as play, \
                mock.patch.object(lt, "probe_seconds", return_value=2.5), \
                mock.patch.object(lt, "git_commit", return_value="abc1234"), \
                mock.patch("shutil.which", return_value="/usr/bin/ffplay"), \
                mock.patch("builtins.input", side_effect=list(answers)):
            code = lt.main(list(argv))
        return code, out.getvalue(), play

    def hear_everything(self):
        who = ("--listener", "Hayk", "--device", "PC headphones")
        return self.run_main(*self.w.args("run", *who), answers=("p",) * 7)

    def test_status_is_red_until_every_file_is_approved(self):
        before = self.w.snapshot()
        code, out, _ = self.run_main(*self.w.args("status"))
        self.assertEqual(code, 1)
        self.assertIn("NEVER HEARD 7", out)
        self.assertIn("Still to hear: 7 files", out)
        code, out, _ = self.hear_everything()
        self.assertEqual(code, 0)
        code, out, _ = self.run_main(*self.w.args("status"))
        self.assertEqual(code, 0)
        self.assertIn("APPROVED 7", out)
        self.assertIn("Still to hear: 0 files, 0.0 minutes", out)
        self.assertEqual(self.w.snapshot(), before)  # audio and appsettings untouched

    def test_changed_bytes_void_the_approval_and_status_says_so(self):
        self.hear_everything()
        self.w.write("voice/hello.mp3", b"hello")  # same bytes: still approved
        self.assertEqual(self.run_main(*self.w.args("status"))[0], 0)
        self.w.write("voice/hello.mp3", b"hello, re-rendered")
        code, out, _ = self.run_main(*self.w.args("status", "--group", "voice"))
        self.assertEqual(code, 1)
        self.assertIn("CHANGED", out)
        self.assertIn("differ from the catalogue sha256", out)  # toys would reject these bytes too

    def test_status_with_nothing_advertised_is_not_green(self):
        self.w.config["ContentSync"]["Enabled"] = False
        self.w.save()
        self.assertEqual(self.run_main(*self.w.args("status"))[0], 1)

    def test_run_records_each_verdict_immediately_and_resumes(self):
        who = ("--listener", "Hayk", "--device", "PC headphones")
        code, _, _ = self.run_main(*self.w.args("run", "--group", "stories", *who), answers=[EOFError()])
        self.assertEqual(code, 0)  # the listener closed the terminal: a clean exit, nothing recorded
        self.assertEqual(lt.load_evidence(self.w.evidence), [])
        code, out, play = self.run_main(*self.w.args("run", *who), answers=["p", "f click at 0:42", "s", "q"])
        self.assertEqual(code, 0)
        self.assertIn("[1/7] s1", out)
        self.assertEqual(play.call_count, 4)
        text = lt.evidence_path(self.w.evidence, DATE).read_text(encoding="utf-8")
        self.assertIn("- **Listener:** Hayk", text)
        self.assertIn("- **Commit:** abc1234", text)
        rows = lt.parse_evidence(text, DATE)[0]
        self.assertEqual([(r.id, r.verdict, r.note) for r in rows],
                         [("s1", "PASS", ""), ("s1-alt", "FAIL", "click at 0:42"), ("s1/intro", "SKIP", "")])
        self.assertEqual(rows[0].sha256, sha(b"story-one"))
        self.assertEqual(rows[0].seconds, 2.5)
        # second run: only what was not answered today is offered, starting at s1/question
        _, out, _ = self.run_main(*self.w.args("run", *who), answers=["q"])
        self.assertIn("4 to hear", out)
        self.assertIn("[1/4] s1/question", out)

    def test_fail_without_inline_note_asks_what_was_heard(self):
        who = ("--listener", "Hayk", "--device", "PC headphones")
        self.run_main(*self.w.args("run", "--only", "m1", *who), answers=["f", "a click"])
        rows = lt.load_evidence(self.w.evidence)
        self.assertEqual([(r.id, r.verdict, r.note) for r in rows], [("m1", "FAIL", "a click")])

    def test_ctrl_c_keeps_what_was_already_recorded(self):
        who = ("--listener", "Hayk", "--device", "PC headphones")
        self.run_main(*self.w.args("run", *who), answers=["p", KeyboardInterrupt()])
        self.assertEqual([r.id for r in lt.load_evidence(self.w.evidence)], ["s1"])

    def test_only_and_again(self):
        who = ("--listener", "Hayk", "--device", "PC headphones")
        self.run_main(*self.w.args("run", "--only", "m1", *who), answers=["p"])
        _, out, _ = self.run_main(*self.w.args("run", "--group", "music", *who))
        self.assertIn("Nothing left to hear", out)
        _, out, _ = self.run_main(*self.w.args("run", "--group", "music", "--device", "toy speaker",
                                               "--listener", "Hayk", "--again"), answers=["p"])
        self.assertIn("1 to hear", out)
        self.assertEqual(self.run_main(*self.w.args("run", "--only", "zzz", *who))[0], 1)

    def test_missing_files_are_reported_not_played(self):
        (self.w.audio / "music/m1.mp3").unlink()
        who = ("--listener", "Hayk", "--device", "PC headphones")
        _, out, _ = self.run_main(*self.w.args("run", "--group", "music", *who))
        self.assertIn("file not on disk", out)
        self.assertIn("Nothing left to hear", out)

    def test_bad_player_and_missing_player_are_clean_failures(self):
        who = ("--listener", "Hayk", "--device", "PC headphones")
        self.assertEqual(self.run_main(*self.w.args("run", "--player", "vlc", *who))[0], 2)
        out = io.StringIO()
        with contextlib.redirect_stdout(out), mock.patch("shutil.which", return_value=None):
            self.assertEqual(lt.main(self.w.args("run", "--group", "music", *who)), 1)
        self.assertIn("Player not found", out.getvalue())

    def test_bad_date_is_refused(self):
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            lt.main(["status", "--date", "2026-10-22"])


if __name__ == "__main__":
    unittest.main()
