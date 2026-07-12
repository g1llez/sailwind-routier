"""Unit tests for supply curve behaviour (no in-game data required)."""

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from market_model import (  # noqa: E402
    MarketParams,
    buy_price_at_supply,
    calibrate_base_value,
    sell_price_at_supply,
    simulate_buy,
    simulate_sell,
)


class MarketModelTests(unittest.TestCase):
    def test_buy_prices_increase_as_supply_drops(self):
        params = MarketParams(
            goods_soft_cap=30,
            base_value=2000,
            supply_purchase_limit=0,
        )
        p1 = buy_price_at_supply(20.0, params)
        p2 = buy_price_at_supply(19.0, params)
        self.assertGreater(p2, p1)

    def test_sell_prices_decrease_as_supply_rises(self):
        params = MarketParams(
            goods_soft_cap=30,
            base_value=2000,
            supply_purchase_limit=0,
        )
        p1 = sell_price_at_supply(10.0, params)
        p2 = sell_price_at_supply(11.0, params)
        self.assertLess(p2, p1)

    def test_simulate_buy_respects_purchase_limit(self):
        params = MarketParams(
            goods_soft_cap=30,
            base_value=2000,
            supply_purchase_limit=5.0,
        )
        sim = simulate_buy(6.0, 10, params)
        self.assertEqual(sim.quantity_bought, 2)

    def test_calibrate_round_trip(self):
        params = MarketParams(goods_soft_cap=30, supply_purchase_limit=0)
        true_base = 1800.0
        params.base_value = true_base
        observed = buy_price_at_supply(12.0, params)
        calibrated = calibrate_base_value(12.0, observed, params)
        trial = MarketParams(
            goods_soft_cap=params.goods_soft_cap,
            base_value=calibrated,
            supply_purchase_limit=params.supply_purchase_limit,
        )
        self.assertEqual(buy_price_at_supply(12.0, trial), observed)

    def test_bulk_buy_total_exceeds_marginal_times_qty(self):
        params = MarketParams(goods_soft_cap=30, base_value=1500, supply_purchase_limit=0)
        supply = 25.0
        marginal = buy_price_at_supply(supply, params)
        sim = simulate_buy(supply, 10, params)
        self.assertGreater(sim.total_cost, marginal * sim.quantity_bought)


class RoundTripTests(unittest.TestCase):
    def test_sell_after_buy_lower_profit_than_marginal_spread(self):
        buy_params = MarketParams(goods_soft_cap=30, base_value=1500, supply_purchase_limit=0)
        sell_params = MarketParams(goods_soft_cap=30, base_value=1500, supply_purchase_limit=0)
        buy_supply = 8.0
        sell_supply = 20.0
        qty = 5
        buy_sim = simulate_buy(buy_supply, qty, buy_params)
        sell_sim = simulate_sell(sell_supply, qty, sell_params)
        marginal_profit = sell_price_at_supply(sell_supply, sell_params) - buy_price_at_supply(
            buy_supply, buy_params
        )
        actual_profit = sell_sim.total_revenue - buy_sim.total_cost
        self.assertLess(actual_profit, marginal_profit * qty)


if __name__ == "__main__":
    unittest.main()
