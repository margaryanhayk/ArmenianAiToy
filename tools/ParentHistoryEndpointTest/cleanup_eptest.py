"""
One-shot cleanup of EPTEST-tagged seed rows created by run.py.

Safety contract:
  - only deletes rows whose markers are unambiguous (EPTEST- on Devices,
    eptest- on Parents, [EPTEST] tag on Messages, or rows reachable via
    those markers)
  - aborts if an EPTEST parent is linked to a non-EPTEST device
  - prints counts before and after, in FK-safe deletion order
"""
from __future__ import annotations

import sqlite3
import sys
from pathlib import Path

DB_PATH = Path(__file__).resolve().parents[2] / "backend" / "src" / "ArmenianAiToy.Api" / "armenian_ai_toy.db"

def main() -> int:
    if not DB_PATH.exists():
        print(f"DB not found at {DB_PATH}", file=sys.stderr)
        return 2

    con = sqlite3.connect(str(DB_PATH))
    con.execute("PRAGMA foreign_keys = ON")
    cur = con.cursor()

    def count(sql: str) -> int:
        return cur.execute(sql).fetchone()[0]

    msg_tag_sql = "SELECT COUNT(*) FROM Messages WHERE Content LIKE '[EPTEST]%'"
    msg_chain_sql = """
        SELECT COUNT(*) FROM Messages WHERE ConversationId IN (
            SELECT Id FROM Conversations WHERE DeviceId IN (
                SELECT Id FROM Devices WHERE MacAddress LIKE 'EPTEST-%'))
    """
    conv_sql = """
        SELECT COUNT(*) FROM Conversations WHERE DeviceId IN (
            SELECT Id FROM Devices WHERE MacAddress LIKE 'EPTEST-%')
    """
    pd_sql = """
        SELECT COUNT(*) FROM ParentDevices WHERE DeviceId IN (
            SELECT Id FROM Devices WHERE MacAddress LIKE 'EPTEST-%')
    """
    dev_sql = "SELECT COUNT(*) FROM Devices WHERE MacAddress LIKE 'EPTEST-%'"
    par_sql = "SELECT COUNT(*) FROM Parents WHERE Email LIKE 'eptest-%'"

    print("=== BEFORE: EPTEST counts ===")
    print(f"  Messages   ([EPTEST] tag)                : {count(msg_tag_sql)}")
    print(f"  Messages   (via EPTEST device chain)     : {count(msg_chain_sql)}")
    print(f"  Conversations (via EPTEST devices)       : {count(conv_sql)}")
    print(f"  ParentDevices (via EPTEST devices)       : {count(pd_sql)}")
    print(f"  Devices    (MacAddress LIKE 'EPTEST-%')  : {count(dev_sql)}")
    print(f"  Parents    (Email LIKE 'eptest-%')       : {count(par_sql)}")

    safety_sql = """
        SELECT COUNT(*) FROM ParentDevices
        WHERE ParentId IN (SELECT Id FROM Parents WHERE Email LIKE 'eptest-%')
          AND DeviceId NOT IN (SELECT Id FROM Devices WHERE MacAddress LIKE 'EPTEST-%')
    """
    orphan = count(safety_sql)
    print(f"  Safety check: EPTEST parents linked to non-EPTEST devices (must be 0): {orphan}")
    if orphan != 0:
        print("ABORT: safety check failed; refusing to delete.", file=sys.stderr)
        return 3

    print()
    print("=== DELETING in FK-safe order ===")
    deleted = {}

    deleted["Messages_by_tag"] = cur.execute(
        "DELETE FROM Messages WHERE Content LIKE '[EPTEST]%'").rowcount
    print(f"  Deleted Messages by [EPTEST] tag         : {deleted['Messages_by_tag']}")

    deleted["Messages_via_chain"] = cur.execute("""
        DELETE FROM Messages WHERE ConversationId IN (
            SELECT Id FROM Conversations WHERE DeviceId IN (
                SELECT Id FROM Devices WHERE MacAddress LIKE 'EPTEST-%'))
    """).rowcount
    print(f"  Deleted Messages via EPTEST device chain : {deleted['Messages_via_chain']}")

    deleted["Conversations"] = cur.execute("""
        DELETE FROM Conversations WHERE DeviceId IN (
            SELECT Id FROM Devices WHERE MacAddress LIKE 'EPTEST-%')
    """).rowcount
    print(f"  Deleted Conversations                    : {deleted['Conversations']}")

    deleted["ParentDevices"] = cur.execute("""
        DELETE FROM ParentDevices WHERE DeviceId IN (
            SELECT Id FROM Devices WHERE MacAddress LIKE 'EPTEST-%')
    """).rowcount
    print(f"  Deleted ParentDevices                    : {deleted['ParentDevices']}")

    deleted["Devices"] = cur.execute(
        "DELETE FROM Devices WHERE MacAddress LIKE 'EPTEST-%'").rowcount
    print(f"  Deleted Devices                          : {deleted['Devices']}")

    deleted["Parents"] = cur.execute(
        "DELETE FROM Parents WHERE Email LIKE 'eptest-%'").rowcount
    print(f"  Deleted Parents                          : {deleted['Parents']}")

    con.commit()

    print()
    print("=== AFTER: EPTEST counts (must all be 0) ===")
    print(f"  Messages   ([EPTEST] tag)                : {count(msg_tag_sql)}")
    print(f"  Messages   (via EPTEST device chain)     : {count(msg_chain_sql)}")
    print(f"  Conversations (via EPTEST devices)       : {count(conv_sql)}")
    print(f"  ParentDevices (via EPTEST devices)       : {count(pd_sql)}")
    print(f"  Devices    (MacAddress LIKE 'EPTEST-%')  : {count(dev_sql)}")
    print(f"  Parents    (Email LIKE 'eptest-%')       : {count(par_sql)}")

    print()
    print("=== Final table totals (unrelated rows must remain intact) ===")
    for t in ("Parents", "Devices", "ParentDevices", "Conversations", "Messages", "Children"):
        print(f"  {t:<14}: {count(f'SELECT COUNT(*) FROM \"{t}\"')}")

    con.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
