"""Parity harness: C# in-game engine vs Python reference optimizer.

The mod writes each generated route (ordered ports + random budget + resulting
profit/spent) to `generated_routes`, linked to the snapshot it was computed from.
This script re-runs the SAME route+budget through the Python `plan_route` on the
SAME snapshot and compares totals. Small deviations are expected because Python
derives base_value from the snapshot quote while C# uses the exact item.value;
large deviations mean the port drifted from the reference and must be investigated.

Run after playing in-game so `generated_routes` is populated:
    python check_route_parity.py [path/to/routier.db]
"""

import sqlite3
import sys
from pathlib import Path

SIM_DIR = Path(__file__).resolve().parents[1]
WEB_DIR = SIM_DIR.parent / "web"
for path in (str(SIM_DIR), str(WEB_DIR)):
    if path not in sys.path:
        sys.path.insert(0, path)

from route_planner import plan_route, plan_to_dict  # noqa: E402

DEFAULT_DB = Path(
    r"C:/Program Files (x86)/Steam/steamapps/common/Sailwind"
    r"/BepInEx/plugins/Routier/data/routier.db"
)


def main(db_path: Path) -> int:
    if not db_path.exists():
        print(f"database not found: {db_path}")
        return 2

    conn = sqlite3.connect(str(db_path))
    conn.row_factory = sqlite3.Row

    has_table = conn.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name='generated_routes'"
    ).fetchone()
    if not has_table:
        print("generated_routes table not found — run the updated mod in-game first.")
        return 0

    rows = conn.execute(
        "SELECT * FROM generated_routes ORDER BY id DESC LIMIT 1000"
    ).fetchall()
    if not rows:
        print("no rows in generated_routes yet — run the mod in-game first.")
        return 0

    checked = 0
    mismatched_deals = 0
    worst_profit_pct = 0.0
    worst_row = None
    within = {0.005: 0, 0.01: 0, 0.02: 0}
    failures = []

    for r in rows:
        route = [int(x) for x in r["route_csv"].split(",") if x != ""]
        if len(route) < 2:
            continue
        try:
            plan = plan_route(
                conn, route, budget_raw=int(r["budget"]), snapshot_id=int(r["snapshot_id"])
            )
        except Exception as exc:  # snapshot may lack a port/good
            failures.append(f"route {r['id']} ({r['route_names']}): {exc}")
            continue

        pd = plan_to_dict(plan, {})
        py_profit = pd["total_profit_raw"]
        py_capital = pd["budget_raw"]
        cs_profit = int(r["profit"])
        cs_capital = int(r.get("capital_initial") or r["budget"])

        checked += 1
        denom = max(abs(cs_profit), 1)
        profit_pct = abs(py_profit - cs_profit) / denom
        for thr in within:
            if profit_pct <= thr:
                within[thr] += 1
        if profit_pct > worst_profit_pct:
            worst_profit_pct = profit_pct
            worst_row = (r, py_profit, py_spent)

        if len(pd["deals"]) != _count_summary_deals(r["summary"]):
            mismatched_deals += 1

    print(f"parity checked: {checked} routes")
    if checked:
        print(
            f"  profit within 0.5%: {within[0.005]}  "
            f"1%: {within[0.01]}  2%: {within[0.02]}"
        )
        print(f"  worst profit deviation: {worst_profit_pct * 100:.2f}%")
        if worst_row is not None:
            r, py_profit, py_spent = worst_row
            print(
                f"    route #{r['id']} {r['route_names']} capital {r.get('capital_initial', r['budget'])}: "
                f"C# profit {r['profit']} / Py {py_profit}, "
                f"C# ROI {int(r['profit'])/max(cs_capital,1)*100:.0f}% / Py {py_profit/max(py_capital,1)*100:.0f}%"
            )
        print(f"  deal-count mismatches: {mismatched_deals}")
    if failures:
        print(f"  {len(failures)} routes failed to replan:")
        for line in failures[:10]:
            print(f"    {line}")

    ok = checked > 0 and worst_profit_pct <= 0.02 and not failures
    print("PARITY OK" if ok else "PARITY: review deviations above")
    return 0 if ok else 1


def _count_summary_deals(summary) -> int:
    if not summary:
        return 0
    return len([s for s in str(summary).split("|") if s.strip()])


if __name__ == "__main__":
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_DB
    raise SystemExit(main(path))
