#!/usr/bin/env python3
"""
Run a market simulation case from JSON.

Usage (from Routier mod folder):
  python sim/tests/run_case.py sim/fixtures/example_case.json
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "sim"))

from market_model import (  # noqa: E402
    MarketParams,
    buy_qty_display,
    calibrate_base_value,
    simulate_buy,
    simulate_round_trip_buy_then_sell,
    simulate_sell,
)


def load_params(raw: dict) -> MarketParams:
    return MarketParams(
        goods_soft_cap=float(raw.get("goods_soft_cap", 30.0)),
        positive_price_mult=float(raw.get("positive_price_mult", 1.0)),
        negative_price_mult=float(raw.get("negative_price_mult", 1.0)),
        supply_purchase_limit=float(raw.get("supply_purchase_limit", 0.0)),
        base_value=float(raw.get("base_value", 1000.0)),
    )


def compare_list(label: str, expected, actual) -> bool:
    if expected is None:
        return True
    if len(expected) != len(actual):
        print(f"  FAIL {label}: length {len(expected)} != {len(actual)}")
        return False
    ok = True
    for i, (exp, got) in enumerate(zip(expected, actual)):
        if exp is None:
            continue
        if exp != got:
            print(f"  FAIL {label}[{i}]: expected {exp}, got {got}")
            ok = False
    if ok:
        print(f"  OK   {label}")
    return ok


def run_case(path: Path) -> int:
    data = json.loads(path.read_text(encoding="utf-8"))
    name = data.get("name", path.name)
    print(f"=== {name} ===")

    params = load_params(data.get("params", {}))

    if "calibrate_from" in data:
        cal = data["calibrate_from"]
        params.base_value = calibrate_base_value(
            float(cal["supply"]),
            int(cal["buy_raw"]),
            params,
        )
        print(f"  Calibrated base_value = {params.base_value:.2f}")

    supply_start = float(data["supply_start"])
    quantity = int(data.get("quantity", 1))

    print(f"  supply_start={supply_start}, buy_qty_display={buy_qty_display(supply_start, params)}")

    if data.get("mode", "buy") == "buy":
        sim = simulate_buy(supply_start, quantity, params)
        print(f"  Bought {sim.quantity_bought}/{sim.quantity_requested}")
        print(f"  Unit prices: {sim.unit_prices}")
        print(f"  Total cost:  {sim.total_cost}")
        if sim.average_price is not None:
            print(f"  Avg price:   {sim.average_price:.2f}")
        print(f"  supply_end:  {sim.supply_end}")

        ok = True
        ok &= compare_list("unit_prices", data.get("expected_unit_prices"), sim.unit_prices)
        if data.get("expected_total") is not None:
            if sim.total_cost != int(data["expected_total"]):
                print(f"  FAIL total: expected {data['expected_total']}, got {sim.total_cost}")
                ok = False
            else:
                print("  OK   total")
        return 0 if ok else 1

    if data.get("mode") == "sell":
        sim = simulate_sell(supply_start, quantity, params)
        print(f"  Sold {sim.quantity_sold}/{sim.quantity_requested}")
        print(f"  Unit prices: {sim.unit_prices}")
        print(f"  Total revenue: {sim.total_revenue}")
        ok = compare_list("unit_prices", data.get("expected_unit_prices"), sim.unit_prices)
        return 0 if ok else 1

    if data.get("mode") == "round_trip":
        sell_supply = float(data["sell_supply_start"])
        sell_params = load_params(data.get("sell_params", data.get("params", {})))
        if "sell_calibrate_from" in data:
            cal = data["sell_calibrate_from"]
            sell_params.base_value = calibrate_base_value(
                float(cal["supply"]),
                int(cal["buy_raw"]),
                sell_params,
            )
            print(f"  Sell port base_value = {sell_params.base_value:.2f}")
        result = simulate_round_trip_buy_then_sell(
            supply_start, sell_supply, quantity, params, sell_params
        )
        buy = result["buy"]
        sell = result["sell"]
        print(f"  Buy prices:  {buy.unit_prices} -> {buy.total_cost}")
        print(f"  Sell prices: {sell.unit_prices} -> {sell.total_revenue}")
        print(f"  Profit:      {result['profit']}")
        if data.get("expected_profit") is not None:
            if result["profit"] != int(data["expected_profit"]):
                print(f"  FAIL profit: expected {data['expected_profit']}, got {result['profit']}")
                return 1
            print("  OK   profit")
        return 0

    print(f"  Unknown mode: {data.get('mode')}")
    return 1


def main() -> None:
    if len(sys.argv) < 2:
        print("Usage: python sim/tests/run_case.py <case.json>")
        sys.exit(2)
    path = Path(sys.argv[1])
    if not path.is_file():
        print(f"File not found: {path}")
        sys.exit(2)
    sys.exit(run_case(path))


if __name__ == "__main__":
    main()
