"""Bulk buy/sell simulation using Routier DB + sim/market_model."""

from __future__ import annotations

import sys
from pathlib import Path
from typing import Optional

SIM_DIR = Path(__file__).resolve().parent.parent / "sim"
if str(SIM_DIR) not in sys.path:
    sys.path.insert(0, str(SIM_DIR))

from dataclasses import replace  # noqa: E402

from market_model import (  # noqa: E402
    MarketParams,
    base_value_from_buy,
    base_value_from_sell,
    calibrate_supply_from_buy,
    calibrate_supply_from_sell,
    simulate_buy,
    simulate_sell,
)


def load_market_globals(conn):
    row = conn.execute(
        """
        SELECT goods_soft_cap, positive_price_mult, negative_price_mult, updated_at
        FROM market_globals WHERE id = 1
        """
    ).fetchone()
    return dict(row) if row else None


def load_port_catalog(conn, port_index: int):
    row = conn.execute(
        """
        SELECT port_index, port_name, supply_purchase_limit, goods_soft_cap_override
        FROM ports_catalog WHERE port_index = ?
        """,
        (port_index,),
    ).fetchone()
    return dict(row) if row else None


def load_good_base_value(conn, good_index: int):
    row = conn.execute(
        "SELECT base_value, good_name FROM goods_catalog WHERE good_index = ?",
        (good_index,),
    ).fetchone()
    if row is None:
        return None, None
    return float(row["base_value"]), row["good_name"]


def load_latest_price(conn, port_index: int, good_index: int, snapshot_id=None):
    if snapshot_id is None:
        snapshot_id = conn.execute(
            "SELECT id FROM snapshots ORDER BY id DESC LIMIT 1"
        ).fetchone()[0]
    row = conn.execute(
        """
        SELECT supply, buy_raw, sell_raw, buy_qty, good_name, port_name
        FROM port_prices
        WHERE snapshot_id = ? AND port_index = ? AND good_index = ?
        """,
        (snapshot_id, port_index, good_index),
    ).fetchone()
    return dict(row) if row else None


def _resolve_snapshot_id(conn, snapshot_id=None):
    if snapshot_id is not None:
        return snapshot_id
    return conn.execute(
        "SELECT id FROM snapshots ORDER BY id DESC LIMIT 1"
    ).fetchone()[0]


def build_market_params(conn, port_index: int, good_index: int):
    globals_row = load_market_globals(conn)
    if globals_row is None:
        raise ValueError("market_globals not captured yet — load a save in-game first")

    port_row = load_port_catalog(conn, port_index)
    if port_row is None:
        raise ValueError(f"port_index {port_index} not in ports_catalog")

    base_value, _ = load_good_base_value(conn, good_index)
    if base_value is None:
        raise ValueError(f"good_index {good_index} not in goods_catalog")

    soft_cap = float(globals_row["goods_soft_cap"])
    # IslandMarket.GetGoodPriceAtSupply always uses DebugMarketTracker.goodsAmountSoftCap;
    # goodsSoftCapOverride only affects EconCycle production, not buy/sell quotes.

    params = MarketParams(
        goods_soft_cap=soft_cap,
        positive_price_mult=float(globals_row["positive_price_mult"]),
        negative_price_mult=float(globals_row["negative_price_mult"]),
        supply_purchase_limit=float(port_row["supply_purchase_limit"]),
        base_value=base_value,
    )
    params_dict = {
        "goods_soft_cap": soft_cap,
        "positive_price_mult": params.positive_price_mult,
        "negative_price_mult": params.negative_price_mult,
        "supply_purchase_limit": params.supply_purchase_limit,
        "base_value": base_value,
    }
    return params, params_dict


def derive_base_value(
    params: MarketParams, price_row: dict
) -> tuple[Optional[float], str]:
    """Base value that reproduces the snapshot quote (buy preferred, else sell)."""
    supply = float(price_row["supply"])
    if price_row.get("available") and price_row.get("buy_raw"):
        value = base_value_from_buy(supply, int(price_row["buy_raw"]), params)
        if value is not None:
            return value, "snapshot_buy_raw"
    if price_row.get("sell_raw"):
        value = base_value_from_sell(supply, int(price_row["sell_raw"]), params)
        if value is not None:
            return value, "snapshot_sell_raw"
    return None, "catalog"


def build_market_params_for_snapshot(
    conn,
    port_index: int,
    good_index: int,
    price_row: dict,
) -> tuple[MarketParams, dict]:
    """
    Snapshot supply + base_value fitted to the observed buy/sell quote.

    Keeps the game's supply level (so HasGood / buy_qty stay valid) while the
    base_value is solved from the marginal price (catalog base_value is
    unreliable for crate/food goods).
    """
    params, params_dict = build_market_params(conn, port_index, good_index)
    base_value, calibration = derive_base_value(params, price_row)
    if base_value is None:
        base_value = params.base_value

    params = replace(params, base_value=base_value)
    params_dict = {**params_dict, "base_value": base_value, "calibration": calibration}
    return params, params_dict


def resolve_supply_start(
    mode: str,
    params: MarketParams,
    price_row: dict,
    *,
    supply_start: Optional[float] = None,
    observed_buy_raw: Optional[int] = None,
    observed_sell_raw: Optional[int] = None,
):
    snapshot_supply = float(price_row["supply"])

    if supply_start is not None:
        return supply_start, "explicit", snapshot_supply

    if mode == "buy" and observed_buy_raw is not None:
        return calibrate_supply_from_buy(int(observed_buy_raw), params), "observed_buy", snapshot_supply

    if mode == "sell" and observed_sell_raw is not None:
        return calibrate_supply_from_sell(int(observed_sell_raw), params), "observed_sell", snapshot_supply

    if mode == "buy" and price_row.get("buy_raw") is not None:
        calibrated = calibrate_supply_from_buy(int(price_row["buy_raw"]), params)
        return calibrated, "snapshot_buy_raw", snapshot_supply

    if mode == "sell" and price_row.get("sell_raw") is not None:
        calibrated = calibrate_supply_from_sell(int(price_row["sell_raw"]), params)
        return calibrated, "snapshot_sell_raw", snapshot_supply

    return snapshot_supply, "snapshot_supply", snapshot_supply


def _base_result(
    snapshot_id,
    port_index,
    price_row,
    good_index,
    good_name,
    supply_start,
    supply_source,
    snapshot_supply,
    params_dict,
):
    return {
        "snapshot_id": snapshot_id,
        "port_index": port_index,
        "port_name": price_row["port_name"],
        "good_index": good_index,
        "good_name": good_name or price_row.get("good_name"),
        "supply_start": supply_start,
        "supply_source": supply_source,
        "snapshot_supply": snapshot_supply,
        "buy_raw_snapshot": price_row.get("buy_raw"),
        "sell_raw_snapshot": price_row.get("sell_raw"),
        "buy_qty_snapshot": price_row.get("buy_qty"),
        "params": params_dict,
    }


def simulate_port_buy(
    conn,
    port_index: int,
    good_index: int,
    quantity: int,
    snapshot_id=None,
    supply_start: Optional[float] = None,
    observed_buy_raw: Optional[int] = None,
):
    snapshot_id = _resolve_snapshot_id(conn, snapshot_id)
    price_row = load_latest_price(conn, port_index, good_index, snapshot_id)
    if price_row is None:
        raise ValueError("no price row for port/good in snapshot")

    params, params_dict = build_market_params_for_snapshot(
        conn, port_index, good_index, price_row
    )
    snapshot_supply = float(price_row["supply"])

    if supply_start is not None:
        resolved_supply, supply_source = supply_start, "explicit"
    elif observed_buy_raw is not None:
        resolved_supply = calibrate_supply_from_buy(int(observed_buy_raw), params)
        supply_source = "observed_buy"
    else:
        resolved_supply, supply_source = snapshot_supply, "snapshot_supply"

    sim = simulate_buy(resolved_supply, quantity, params)
    result = _base_result(
        snapshot_id,
        port_index,
        price_row,
        good_index,
        price_row.get("good_name"),
        resolved_supply,
        supply_source,
        snapshot_supply,
        params_dict,
    )
    result.update({
        "mode": "buy",
        "unit_prices": sim.unit_prices,
        "total_cost": sim.total_cost,
        "quantity_bought": sim.quantity_bought,
        "supply_end": sim.supply_end,
        "average_price": sim.average_price,
    })
    return result


def simulate_port_sell(
    conn,
    port_index: int,
    good_index: int,
    quantity: int,
    snapshot_id=None,
    supply_start: Optional[float] = None,
    observed_sell_raw: Optional[int] = None,
):
    snapshot_id = _resolve_snapshot_id(conn, snapshot_id)
    price_row = load_latest_price(conn, port_index, good_index, snapshot_id)
    if price_row is None:
        raise ValueError("no price row for port/good in snapshot")

    params, params_dict = build_market_params_for_snapshot(
        conn, port_index, good_index, price_row
    )
    snapshot_supply = float(price_row["supply"])

    if supply_start is not None:
        resolved_supply, supply_source = supply_start, "explicit"
    elif observed_sell_raw is not None:
        resolved_supply = calibrate_supply_from_sell(int(observed_sell_raw), params)
        supply_source = "observed_sell"
    else:
        resolved_supply, supply_source = snapshot_supply, "snapshot_supply"

    sim = simulate_sell(resolved_supply, quantity, params)
    result = _base_result(
        snapshot_id,
        port_index,
        price_row,
        good_index,
        price_row.get("good_name"),
        resolved_supply,
        supply_source,
        snapshot_supply,
        params_dict,
    )
    result.update({
        "mode": "sell",
        "unit_prices": sim.unit_prices,
        "total_revenue": sim.total_revenue,
        "quantity_sold": sim.quantity_sold,
        "supply_end": sim.supply_end,
        "average_price": sim.total_revenue / sim.quantity_sold if sim.quantity_sold else None,
    })
    return result


def simulate_port_buy_then_sell(
    conn,
    port_index: int,
    good_index: int,
    quantity: int,
    snapshot_id=None,
    observed_buy_raw: Optional[int] = None,
):
    buy = simulate_port_buy(
        conn,
        port_index,
        good_index,
        quantity,
        snapshot_id,
        observed_buy_raw=observed_buy_raw,
    )
    sell = simulate_port_sell(
        conn,
        port_index,
        good_index,
        buy["quantity_bought"],
        snapshot_id,
        supply_start=buy["supply_end"],
    )
    profit = sell["total_revenue"] - buy["total_cost"]
    return {
        "buy": buy,
        "sell": sell,
        "profit": profit,
        "profit_per_unit": profit / buy["quantity_bought"] if buy["quantity_bought"] else None,
    }
