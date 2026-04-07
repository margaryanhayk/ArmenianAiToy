"""
Parent-history single-conversation endpoint test runner.

Tests GET /api/conversations/{conversationId} against the live local API
with 100 real HTTP calls in three categories:
  - 40 owned conversationIds (Parent A)         -> expect 200
  - 30 non-owned conversationIds (Parent B)     -> expect 404
  - 30 fake conversationIds (random Guid)       -> expect 404

Setup uses HTTP for parent registration/login (free, no OpenAI) and writes
the device/conversation/message rows directly into the SQLite DB the API
already uses. All seeded rows carry an "EPTEST-" marker so they are
recognizable and can be cleaned up later.

Outputs:
  tools/TestReports/parent-history-endpoint-100tests.md
  tools/TestReports/parent-history-endpoint-100tests.json
"""

from __future__ import annotations

import json
import os
import sqlite3
import sys
import urllib.error
import urllib.request
import uuid
from datetime import datetime, timezone
from pathlib import Path

# --- Configuration -----------------------------------------------------------

API_BASE = os.environ.get("EPTEST_API_BASE", "http://localhost:5000")
REPO_ROOT = Path(__file__).resolve().parents[2]
DB_PATH = REPO_ROOT / "backend" / "src" / "ArmenianAiToy.Api" / "armenian_ai_toy.db"
REPORT_DIR = REPO_ROOT / "tools" / "TestReports"
MD_OUT = REPORT_DIR / "parent-history-endpoint-100tests.md"
JSON_OUT = REPORT_DIR / "parent-history-endpoint-100tests.json"

OWNED_COUNT = 40
NON_OWNED_COUNT = 30
FAKE_COUNT = 30
TOTAL = OWNED_COUNT + NON_OWNED_COUNT + FAKE_COUNT  # 100

MARKER = "EPTEST"
RUN_ID = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")

# --- HTTP helpers ------------------------------------------------------------


def http(method: str, path: str, body: dict | None = None,
         headers: dict[str, str] | None = None) -> tuple[int, str]:
    url = f"{API_BASE}{path}"
    data = None
    h = {"Accept": "application/json"}
    if headers:
        h.update(headers)
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        h["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, headers=h, method=method)
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            return resp.getcode(), resp.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as e:
        body_text = ""
        try:
            body_text = e.read().decode("utf-8", errors="replace")
        except Exception:
            pass
        return e.code, body_text
    except urllib.error.URLError as e:
        return 0, f"URLError: {e}"


# --- DB helpers --------------------------------------------------------------


def now_sqlite() -> str:
    # Mirrors EF Core's default DateTime serialization for SQLite:
    # 'YYYY-MM-DD HH:MM:SS.fffffff' (7 fractional digits, but 6 is accepted).
    return datetime.utcnow().strftime("%Y-%m-%d %H:%M:%S.%f") + "0"


def upper_guid() -> str:
    return str(uuid.uuid4()).upper()


def seed_for_parent(con: sqlite3.Connection, parent_id: str, count: int,
                    label: str) -> list[str]:
    """Insert `count` devices, ParentDevice links, conversations and messages
    for the given parent. Returns the list of conversation IDs created."""
    conv_ids: list[str] = []
    cur = con.cursor()
    for i in range(count):
        device_id = upper_guid()
        # Run-id suffix keeps MAC unique across reruns (Devices.MacAddress UNIQUE).
        api_key = f"{MARKER}-KEY-{label}-{RUN_ID}-{i:03d}"
        mac = f"{MARKER}-{label}-{RUN_ID}-{i:03d}"
        name = f"{MARKER} {label} #{i:03d}"
        ts = now_sqlite()

        cur.execute(
            'INSERT INTO "Devices" '
            '("Id","MacAddress","Name","ApiKey","FirmwareVersion","RegisteredAt","LastSeenAt") '
            'VALUES (?,?,?,?,?,?,?)',
            (device_id, mac, name, api_key, "0.0.0-eptest", ts, ts),
        )
        cur.execute(
            'INSERT INTO "ParentDevices" '
            '("ParentId","DeviceId","LinkedAt") VALUES (?,?,?)',
            (parent_id, device_id, ts),
        )

        conv_id = upper_guid()
        cur.execute(
            'INSERT INTO "Conversations" '
            '("Id","DeviceId","ChildId","StartedAt","EndedAt") VALUES (?,?,?,?,?)',
            (conv_id, device_id, None, ts, None),
        )

        # Two realistic messages per conversation: a child question and a
        # toy answer in Armenian. Marker stays inside the content so the
        # rows are recognizable later.
        msg_user_id = upper_guid()
        msg_assistant_id = upper_guid()
        cur.execute(
            'INSERT INTO "Messages" '
            '("Id","ConversationId","Role","Content","Timestamp","SafetyFlag","AudioBlobPath") '
            'VALUES (?,?,?,?,?,?,?)',
            (msg_user_id, conv_id, "User",
             f"[{MARKER}] Արի մի հեքիաթ պատմիր #{i:03d}",
             ts, "Clean", None),
        )
        cur.execute(
            'INSERT INTO "Messages" '
            '("Id","ConversationId","Role","Content","Timestamp","SafetyFlag","AudioBlobPath") '
            'VALUES (?,?,?,?,?,?,?)',
            (msg_assistant_id, conv_id, "Assistant",
             f"[{MARKER}] Մի փոքրիկ նապաստակ ապրում էր անտառում։ #{i:03d}",
             ts, "Clean", None),
        )
        conv_ids.append(conv_id)
    con.commit()
    return conv_ids


# --- Main --------------------------------------------------------------------


def main() -> int:
    print(f"== Parent-history endpoint test runner ==")
    print(f"   API base : {API_BASE}")
    print(f"   DB file  : {DB_PATH}")
    print(f"   Run id   : {RUN_ID}")
    print()

    if not DB_PATH.exists():
        print(f"FATAL: DB not found at {DB_PATH}", file=sys.stderr)
        return 2

    # Step 1: register Parent A and B via HTTP. Email is unique per run.
    parent_a_email = f"eptest-a-{RUN_ID}@local.test"
    parent_b_email = f"eptest-b-{RUN_ID}@local.test"
    parent_password = "EpTest!Pass123"

    print("[1/6] Registering Parent A and Parent B via HTTP ...")
    code, body = http("POST", "/api/parents/register",
                      {"email": parent_a_email, "password": parent_password})
    if code != 201:
        print(f"  Parent A register failed: {code} {body}", file=sys.stderr)
        return 3
    # EF Core stores Guids uppercase in SQLite; uppercase here so the
    # seeded ParentDevices.ParentId matches the EF-side query parameter.
    parent_a_id = json.loads(body)["parentId"].upper()
    print(f"  Parent A id : {parent_a_id}")

    code, body = http("POST", "/api/parents/register",
                      {"email": parent_b_email, "password": parent_password})
    if code != 201:
        print(f"  Parent B register failed: {code} {body}", file=sys.stderr)
        return 3
    parent_b_id = json.loads(body)["parentId"].upper()
    print(f"  Parent B id : {parent_b_id}")

    # Step 2: login Parent A to obtain JWT.
    print("[2/6] Logging in Parent A ...")
    code, body = http("POST", "/api/parents/login",
                      {"email": parent_a_email, "password": parent_password})
    if code != 200:
        print(f"  Parent A login failed: {code} {body}", file=sys.stderr)
        return 3
    parent_a_jwt = json.loads(body)["token"]
    print(f"  JWT length  : {len(parent_a_jwt)}")

    # Step 3: seed devices, conversations, messages for both parents.
    print("[3/6] Seeding owned + non-owned data into SQLite ...")
    con = sqlite3.connect(str(DB_PATH))
    try:
        owned_ids = seed_for_parent(con, parent_a_id, OWNED_COUNT, "A")
        non_owned_ids = seed_for_parent(con, parent_b_id, NON_OWNED_COUNT, "B")
    finally:
        con.close()
    print(f"  Owned conversations     : {len(owned_ids)}")
    print(f"  Non-owned conversations : {len(non_owned_ids)}")

    # Step 4: build the test plan.
    print("[4/6] Building 100-test plan ...")
    fake_ids = [upper_guid() for _ in range(FAKE_COUNT)]
    plan: list[dict] = []
    counter = 1
    for cid in owned_ids:
        plan.append({"testId": f"T{counter:03d}", "category": "owned",
                     "conversationId": cid, "expectedStatus": 200})
        counter += 1
    for cid in non_owned_ids:
        plan.append({"testId": f"T{counter:03d}", "category": "non_owned",
                     "conversationId": cid, "expectedStatus": 404})
        counter += 1
    for cid in fake_ids:
        plan.append({"testId": f"T{counter:03d}", "category": "fake",
                     "conversationId": cid, "expectedStatus": 404})
        counter += 1
    assert len(plan) == TOTAL, f"plan has {len(plan)} entries, want {TOTAL}"

    # Step 5: execute the plan against the live endpoint.
    print(f"[5/6] Executing {TOTAL} HTTP calls against /api/conversations/{{id}} ...")
    headers = {"Authorization": f"Bearer {parent_a_jwt}"}
    results: list[dict] = []
    for idx, t in enumerate(plan, start=1):
        code, body = http("GET", f"/api/conversations/{t['conversationId']}",
                          headers=headers)
        passed = (code == t["expectedStatus"])
        snippet = body[:160].replace("\n", " ")
        notes = ""
        if t["category"] == "owned" and code == 200:
            try:
                parsed = json.loads(body)
                conv = parsed.get("conversation") or {}
                msgs = conv.get("messages") or []
                notes = (f"messages={len(msgs)} "
                         f"deviceIdMatches="
                         f"{str(conv.get('deviceId','')).lower() != ''}")
            except Exception as e:
                notes = f"json_parse_error: {e}"
        results.append({
            "testId": t["testId"],
            "category": t["category"],
            "conversationId": t["conversationId"],
            "expectedStatus": t["expectedStatus"],
            "actualStatus": code,
            "passed": passed,
            "responseSnippet": snippet,
            "notes": notes,
        })
        if idx % 10 == 0 or idx == TOTAL:
            print(f"  {idx:3d}/{TOTAL}")

    # Step 6: write reports.
    print("[6/6] Writing reports ...")
    REPORT_DIR.mkdir(parents=True, exist_ok=True)

    JSON_OUT.write_text(json.dumps({
        "runId": RUN_ID,
        "apiBase": API_BASE,
        "totals": {
            "total": len(results),
            "passed": sum(1 for r in results if r["passed"]),
            "failed": sum(1 for r in results if not r["passed"]),
        },
        "distribution": {
            "owned": OWNED_COUNT,
            "non_owned": NON_OWNED_COUNT,
            "fake": FAKE_COUNT,
        },
        "parents": {
            "parentAId": parent_a_id,
            "parentAEmail": parent_a_email,
            "parentBId": parent_b_id,
            "parentBEmail": parent_b_email,
        },
        "results": results,
    }, indent=2, ensure_ascii=False), encoding="utf-8")

    passed_total = sum(1 for r in results if r["passed"])
    failed_total = TOTAL - passed_total
    by_cat: dict[str, dict[str, int]] = {}
    for r in results:
        c = by_cat.setdefault(r["category"], {"passed": 0, "failed": 0})
        c["passed" if r["passed"] else "failed"] += 1
    failures_by_cat: dict[str, list[dict]] = {}
    for r in results:
        if not r["passed"]:
            failures_by_cat.setdefault(r["category"], []).append(r)

    md = []
    md.append("# Parent-history endpoint — 100-test report")
    md.append("")
    md.append(f"**Run id:** {RUN_ID}  ")
    md.append(f"**API base:** {API_BASE}  ")
    md.append(f"**Endpoint under test:** `GET /api/conversations/{{conversationId}}`  ")
    md.append(f"**Total tests:** {TOTAL}")
    md.append("")
    md.append("## 1. Summary")
    md.append("")
    md.append(f"Ran {TOTAL} real HTTP calls against the live local API on {API_BASE}. "
              f"Owned conversations were created for Parent A, non-owned conversations "
              f"for Parent B, and fake ids were random GUIDs. Owned ids must return "
              f"`200`; both non-owned and fake ids must return `404` (uniform 404 = "
              f"no existence disclosure).")
    md.append("")
    md.append(f"**Result:** {passed_total}/{TOTAL} passed, {failed_total} failed.")
    md.append("")
    md.append("## 2. Test distribution")
    md.append("")
    md.append("| Category | Count | Expected status |")
    md.append("|---|---|---|")
    md.append(f"| owned (Parent A) | {OWNED_COUNT} | 200 |")
    md.append(f"| non_owned (Parent B) | {NON_OWNED_COUNT} | 404 |")
    md.append(f"| fake (random GUID) | {FAKE_COUNT} | 404 |")
    md.append(f"| **Total** | **{TOTAL}** | — |")
    md.append("")
    md.append("## 3. Pass / fail counts")
    md.append("")
    md.append("| Category | Passed | Failed |")
    md.append("|---|---|---|")
    for cat in ["owned", "non_owned", "fake"]:
        c = by_cat.get(cat, {"passed": 0, "failed": 0})
        md.append(f"| {cat} | {c['passed']} | {c['failed']} |")
    md.append(f"| **Total** | **{passed_total}** | **{failed_total}** |")
    md.append("")
    md.append("## 4. Failures (grouped)")
    md.append("")
    if failed_total == 0:
        md.append("_No failures._")
    else:
        for cat, fails in failures_by_cat.items():
            md.append(f"### {cat} ({len(fails)} failed)")
            md.append("")
            md.append("| testId | conversationId | expected | actual | snippet |")
            md.append("|---|---|---|---|---|")
            for r in fails:
                md.append(f"| {r['testId']} | `{r['conversationId']}` | "
                          f"{r['expectedStatus']} | {r['actualStatus']} | "
                          f"`{r['responseSnippet'][:80]}` |")
            md.append("")
    md.append("## 5. Sample responses")
    md.append("")
    samples = []
    for cat in ["owned", "non_owned", "fake"]:
        for r in results:
            if r["category"] == cat:
                samples.append(r)
                break
    for r in samples:
        md.append(f"### {r['category']} — {r['testId']}")
        md.append("")
        md.append(f"- conversationId: `{r['conversationId']}`")
        md.append(f"- expected status: {r['expectedStatus']}")
        md.append(f"- actual status: {r['actualStatus']}")
        md.append(f"- passed: {r['passed']}")
        md.append(f"- notes: {r['notes'] or '(none)'}")
        md.append("")
        md.append("```")
        md.append(r["responseSnippet"])
        md.append("```")
        md.append("")
    md.append("## 6. Setup assumptions")
    md.append("")
    md.append("- The local API is running on `" + API_BASE + "` with the SQLite DB at "
              "`backend/src/ArmenianAiToy.Api/armenian_ai_toy.db`.")
    md.append("- Parent A and Parent B were registered via real HTTP "
              "`POST /api/parents/register`. Parent A was logged in via real HTTP "
              "`POST /api/parents/login` to obtain a real JWT.")
    md.append("- Devices, ParentDevices, Conversations, and Messages were inserted "
              "directly into SQLite (no production code paths bypassed for the "
              "endpoint under test). All seeded rows are tagged with the prefix "
              f"`{MARKER}-` (devices: MAC + ApiKey, messages: leading `[{MARKER}]` "
              "tag) so they are easy to spot and clean later.")
    md.append("- Tables touched by seeding: `Parents` (via HTTP), `Devices`, "
              "`ParentDevices`, `Conversations`, `Messages`. Schema unchanged.")
    md.append(f"- Per parent: {OWNED_COUNT} (A) / {NON_OWNED_COUNT} (B) device + "
              "ParentDevice + conversation rows, plus 2 messages per conversation "
              f"({OWNED_COUNT*2 + NON_OWNED_COUNT*2} message rows total).")
    md.append("- Fake conversationIds are random GUIDs that do not exist in the DB.")
    md.append("- The endpoint under test is hit via real HTTP "
              "`GET /api/conversations/{conversationId}` with "
              "`Authorization: Bearer <parent A JWT>`.")
    md.append("")
    md.append("## 7. Final verdict")
    md.append("")
    if failed_total == 0:
        md.append(f"**PASS — {passed_total}/{TOTAL}.** Owned conversations return `200`, "
                  "non-owned and fake conversation ids return a uniform `404`. "
                  "Authorization, ownership check, and existence-leak prevention all "
                  "behave as specified.")
    else:
        md.append(f"**FAIL — {failed_total} of {TOTAL} tests failed.** See section 4.")
    md.append("")

    MD_OUT.write_text("\n".join(md), encoding="utf-8")

    print()
    print(f"  JSON : {JSON_OUT}")
    print(f"  MD   : {MD_OUT}")
    print()
    print(f"== {passed_total}/{TOTAL} passed, {failed_total} failed ==")
    return 0 if failed_total == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
