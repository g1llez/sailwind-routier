"""Snapshot base_value calibration (supply level + marginal price)."""

import sys
import unittest
from dataclasses import replace
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from market_model import (  # noqa: E402
    MarketParams,
    buy_price_at_supply,
    calibrate_base_value,
    simulate_buy,
)

RUM_ROW = {"supply": 32.085, "buy_raw": 176, "sell_raw": 173, "available": 1}
SUNSPOT_ROW = {"supply": 28.608, "buy_raw": 589, "sell_raw": 580, "available": 1}
BASE_PARAMS = MarketParams(
    goods_soft_cap=57.0,
    positive_price_mult=0.5,
    negative_price_mult=1.0,
    supply_purchase_limit=1.0,
    base_value=360.0,
)


class SnapshotCalibrationTests(unittest.TestCase):
    def test_rum_marginal_at_snapshot_supply(self):
        seed = replace(BASE_PARAMS, base_value=220.0)
        bv = calibrate_base_value(RUM_ROW["supply"], RUM_ROW["buy_raw"], seed)
        params = replace(seed, base_value=bv)
        self.assertEqual(buy_price_at_supply(RUM_ROW["supply"], params), 176)

    def test_sunspot_bulk_buy_not_free(self):
        bv = calibrate_base_value(
            SUNSPOT_ROW["supply"], SUNSPOT_ROW["buy_raw"], BASE_PARAMS
        )
        params = replace(BASE_PARAMS, base_value=bv)
        sim = simulate_buy(SUNSPOT_ROW["supply"], 28, params)
        self.assertEqual(sim.quantity_bought, 28)
        self.assertGreater(sim.total_cost, 0)
        self.assertEqual(len(sim.unit_prices), 28)


if __name__ == "__main__":
    unittest.main()
