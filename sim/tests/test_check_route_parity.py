"""Regression tests for the C#/Python parity report utility."""

import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


TESTS_DIR = Path(__file__).resolve().parent
if str(TESTS_DIR) not in sys.path:
    sys.path.insert(0, str(TESTS_DIR))

import check_route_parity as parity  # noqa: E402


class CheckRouteParityTests(unittest.TestCase):
    def test_main_handles_sqlite_rows_and_reports_exact_match(self):
        with tempfile.TemporaryDirectory() as tmp:
            db_path = Path(tmp) / "routier_slot0.db"
            conn = sqlite3.connect(str(db_path))
            conn.execute(
                """
                CREATE TABLE generated_routes (
                    id INTEGER,
                    route_csv TEXT,
                    route_names TEXT,
                    budget INTEGER,
                    snapshot_id INTEGER,
                    profit INTEGER,
                    capital_initial INTEGER,
                    summary TEXT
                )
                """
            )
            conn.execute(
                "INSERT INTO generated_routes VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                (1, "1,2", "Port A > Port B", 1000, 7, 100, 900, "one deal"),
            )
            conn.commit()
            conn.close()

            plan_dict = {
                "total_profit_raw": 100,
                "budget_raw": 900,
                "deals": [{}],
            }
            with patch.object(parity, "plan_route", return_value=object()), patch.object(
                parity, "plan_to_dict", return_value=plan_dict
            ):
                self.assertEqual(parity.main(db_path), 0)


if __name__ == "__main__":
    unittest.main()
