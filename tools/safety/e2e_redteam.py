#!/usr/bin/env python3
"""Send the Armenian red-team corpus through a RUNNING backend's real
/api/chat pipeline (moderation + chat model + guards) and record replies.

Needs a local API started with Devices__AllowOpenRegistration=true and real
provider keys; it registers one device, claims it for a throwaway parent,
then posts every corpus prompt. Stdlib only. Never point it at production.

    python3 tools/safety/e2e_redteam.py --base http://127.0.0.1:5000 --out /tmp/out.json
"""
import argparse, json, time, uuid, urllib.request, urllib.error
from pathlib import Path

CORPUS = Path(__file__).resolve().parents[2] / "backend/tests/ArmenianAiToy.Application.Tests/TestData/armenian-red-team-safety-corpus.json"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="http://127.0.0.1:5000")
    ap.add_argument("--out", required=True)
    ap.add_argument("--delay", type=float, default=2.5, help="seconds between chat calls (per-device rate limit)")
    args = ap.parse_args()

    def call(method, path, body=None, headers=None):
        req = urllib.request.Request(args.base + path, method=method,
                                     data=json.dumps(body).encode() if body is not None else None,
                                     headers={"Content-Type": "application/json", **(headers or {})})
        try:
            with urllib.request.urlopen(req, timeout=90) as r:
                return r.status, json.loads(r.read() or b"null")
        except urllib.error.HTTPError as e:
            return e.code, e.read().decode()[:300]

    raw = json.loads(CORPUS.read_text(encoding="utf-8"))
    cases = raw["cases"] if isinstance(raw, dict) else raw

    email, pw = f"e2e-{uuid.uuid4().hex[:8]}@example.test", "E2e-test-password-123!"
    call("POST", "/api/parents/register", {"email": email, "password": pw, "acceptedTerms": True})
    _, login = call("POST", "/api/parents/login", {"email": email, "password": pw})
    s, dev = call("POST", "/api/devices/register", {"macAddress": f"E2E-{time.time_ns()}"})
    assert s in (200, 201), (s, dev)
    s, _ = call("POST", "/api/parents/devices/link", {"deviceId": dev["deviceId"], "apiKey": dev["apiKey"]},
                {"Authorization": "Bearer " + login["token"]})
    assert s == 200, s
    h = {"X-Device-Id": dev["deviceId"], "X-Api-Key": dev["apiKey"]}

    out = []
    for c in cases:
        time.sleep(args.delay)
        s, resp = call("POST", "/api/chat", {"message": c["text"]}, h)
        r = resp if isinstance(resp, dict) else {"raw": resp}
        reply = r.get("response") or str(r.get("raw"))
        out.append({"id": c["id"], "lang": c["language"], "cat": c["category"], "expected": c["expected"],
                    "status": s, "safetyFlag": r.get("safetyFlag"), "reply": reply})
        print(c["id"], s, r.get("safetyFlag"), reply[:110].replace("\n", " "), flush=True)
    Path(args.out).write_text(json.dumps(out, ensure_ascii=False, indent=1), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
