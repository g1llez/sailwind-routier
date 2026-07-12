"""Tests for route sell-split optimization."""

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from market_model import MarketParams, simulate_buy, simulate_sell
from route_optimizer import (
    best_deal_for_pair,
    optimal_sell_split,
    sequential_route_plan,
)


class SellSplitTests(unittest.TestCase):
    def setUp(self):
        self.params = MarketParams(
            goods_soft_cap=57.0,
            base_value=48.0,
            positive_price_mult=0.5,
            negative_price_mult=1.0,
            supply_purchase_limit=-15,
        )

    def test_optimal_sell_prefers_split_over_single_port(self):
        """When marginal is higher at port A but bulk erodes, split can win."""
        oasis = (1, "Oasis")
        grc = (2, "Gold Rock City")
        supplies = {1: -11.25, 2: -11.25}
        legs, total = optimal_sell_split(
            [oasis, grc], 12, supplies, self.params
        )
        qty_oasis = sum(leg.quantity for leg in legs if leg.port_index == 1)
        qty_grc = sum(leg.quantity for leg in legs if leg.port_index == 2)
        all_at_grc = simulate_sell(-11.25, 12, self.params).total_revenue
        self.assertGreater(total, 0)
        self.assertEqual(qty_oasis + qty_grc, 12)
        self.assertGreaterEqual(total, all_at_grc)

    def test_best_deal_pair_positive_profit(self):
        buy = (0, "Port A")
        sells = [(1, "Port B"), (2, "Port C")]
        deal = best_deal_for_pair(
            buy,
            sells,
            good_index=5,
            good_name="beer",
            supply_buy=0.4,
            supplies_sell={1: -11.25, 2: -11.25},
            params=self.params,
            budget=5000,
            max_qty=16,
            weight_lb=10.0,
            max_weight=200.0,
            volume_cuft=1.0,
            max_volume=None,
        )
        self.assertIsNotNone(deal)
        self.assertGreater(deal.profit, 0)
        self.assertEqual(deal.quantity, sum(leg.quantity for leg in deal.sell_legs))


class SequentialRouteTests(unittest.TestCase):
    def setUp(self):
        self.params = MarketParams(
            goods_soft_cap=57.0,
            base_value=48.0,
            positive_price_mult=0.5,
            negative_price_mult=1.0,
            supply_purchase_limit=-15,
        )

    def test_second_leg_after_intermediate_sell(self):
        """Sell at port 1 frees cash/weight for a new buy there."""
        route = [(0, "Oasis"), (1, "Academy"), (2, "Gold Rock City")]
        goods = [
            {
                "good_index": 1,
                "good_name": "beer",
                "weight_lb": 10.0,
                "volume_cuft": 1.0,
                "available_by_port": {0: True, 1: False, 2: False},
                "buy_qty_by_port": {0: 16},
            },
            {
                "good_index": 2,
                "good_name": "rum",
                "weight_lb": 10.0,
                "volume_cuft": 1.0,
                "available_by_port": {0: False, 1: True, 2: False},
                "buy_qty_by_port": {1: 16},
            },
        ]
        supplies = {
            (0, 1): 0.4,
            (1, 1): -11.25,
            (2, 1): -50.0,
            (1, 2): 0.4,
            (2, 2): -11.25,
        }

        def params_for(_port_index, _good_index):
            return self.params

        plan = sequential_route_plan(
            route, goods, supplies, params_for, budget=5000, max_weight=200.0
        )
        buy_ports = [d.buy_port_index for d in plan.deals]
        self.assertIn(0, buy_ports, "expected a buy at first port")
        self.assertIn(1, buy_ports, "expected a buy at second port after sell")
        self.assertEqual(len(plan.deals), 2)
        self.assertGreater(plan.budget_left, plan.budget)


class VolumeCapTests(unittest.TestCase):
    def setUp(self):
        self.params = MarketParams(
            goods_soft_cap=57.0,
            base_value=48.0,
            positive_price_mult=0.5,
            negative_price_mult=1.0,
            supply_purchase_limit=-15,
        )

    def test_volume_caps_quantity(self):
        buy = (0, "Port A")
        sells = [(1, "Port B")]
        deal = best_deal_for_pair(
            buy,
            sells,
            good_index=1,
            good_name="bulky",
            supply_buy=0.4,
            supplies_sell={1: -11.25},
            params=self.params,
            budget=5000,
            max_qty=16,
            weight_lb=1.0,
            max_weight=None,
            volume_cuft=10.0,
            max_volume=25.0,
        )
        self.assertIsNotNone(deal)
        self.assertLessEqual(deal.volume_total, 25.0)
        self.assertEqual(deal.quantity, 2)


class PerPortSellParamsTests(unittest.TestCase):
    """Regression: sells must be priced with each sell port's own params.

    The old bug priced every sell leg with the BUY port's params during
    evaluation, then re-priced with the real per-port params at execution,
    producing phantom (losing) deals.
    """

    def setUp(self):
        from dataclasses import replace

        self.rich = MarketParams(
            goods_soft_cap=57.0,
            base_value=720.0,
            positive_price_mult=0.5,
            negative_price_mult=1.0,
            supply_purchase_limit=-15,
        )
        self.cheap = replace(self.rich, base_value=360.0)

    def test_greedy_prefers_richer_sell_port(self):
        legs, revenue = optimal_sell_split(
            [(1, "Cheap"), (2, "Rich")],
            10,
            {1: 20.0, 2: 20.0},
            {1: self.cheap, 2: self.rich},
        )
        qty_cheap = sum(l.quantity for l in legs if l.port_index == 1)
        qty_rich = sum(l.quantity for l in legs if l.port_index == 2)
        self.assertGreater(qty_rich, qty_cheap)
        self.assertEqual(revenue, sum(l.total_revenue for l in legs))

    def test_deal_totals_match_legs_and_no_phantom_profit(self):
        deal = best_deal_for_pair(
            (0, "Buy"),
            [(1, "Cheap"), (2, "Rich")],
            good_index=1,
            good_name="sunspot",
            supply_buy=40.0,
            supplies_sell={1: -10.0, 2: -10.0},
            params=self.rich,
            budget=100000,
            max_qty=13,
            weight_lb=0.0,
            max_weight=None,
            volume_cuft=0.0,
            max_volume=None,
            sell_params={1: self.cheap, 2: self.rich},
        )
        self.assertIsNotNone(deal)
        self.assertEqual(deal.sell_total, sum(l.total_revenue for l in deal.sell_legs))
        self.assertEqual(deal.profit, deal.sell_total - deal.buy_total)
        self.assertGreater(deal.profit, 0)


if __name__ == "__main__":
    unittest.main()
