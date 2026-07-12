"""Route plan from Routier DB + sim/route_optimizer."""

from __future__ import annotations

import sys
from dataclasses import replace
from pathlib import Path
from statistics import median
from typing import Dict, List, Optional

SIM_DIR = Path(__file__).resolve().parent.parent / "sim"
if str(SIM_DIR) not in sys.path:
    sys.path.insert(0, str(SIM_DIR))

from cargo_dims import parse_volume_cuft  # noqa: E402
from market_model import MarketParams  # noqa: E402
from market_sim import build_market_params, derive_base_value  # noqa: E402
from route_optimizer import RouteDeal, RoutePlan, sequential_route_plan  # noqa: E402


def load_route_ports(conn, port_indices: List[int], snapshot_id: int):
    placeholders = ",".join("?" for _ in port_indices)
    rows = conn.execute(
        f"""
        SELECT DISTINCT port_index, port_name, region
        FROM port_prices
        WHERE snapshot_id = ? AND port_index IN ({placeholders})
        """,
        (snapshot_id, *port_indices),
    ).fetchall()
    by_index = {int(r["port_index"]): dict(r) for r in rows}
    ordered = []
    for idx in port_indices:
        if idx not in by_index:
            raise ValueError(f"port_index {idx} not in snapshot")
        row = by_index[idx]
        ordered.append((idx, row["port_name"], int(row["region"])))
    return ordered


def load_route_market(conn, port_indices: List[int], snapshot_id: int):
    placeholders = ",".join("?" for _ in port_indices)
    rows = conn.execute(
        f"""
        SELECT p.port_index, p.good_index, p.good_name, p.supply, p.available, p.buy_qty,
               p.buy_raw, p.sell_raw,
               c.weight_lb, c.size_description
        FROM port_prices p
        LEFT JOIN goods_catalog c ON c.good_index = p.good_index
        WHERE p.snapshot_id = ? AND p.port_index IN ({placeholders})
        ORDER BY p.good_name, p.port_index
        """,
        (snapshot_id, *port_indices),
    ).fetchall()

    # Group snapshot rows per good so we can fit ONE base_value per good.
    rows_by_good: Dict[int, list] = {}
    for row in rows:
        rows_by_good.setdefault(int(row["good_index"]), []).append(dict(row))

    base_params_cache: Dict[tuple, MarketParams] = {}

    def base_params(port_index: int, good_index: int) -> MarketParams:
        key = (port_index, good_index)
        if key not in base_params_cache:
            base_params_cache[key], _ = build_market_params(
                conn, port_index, good_index
            )
        return base_params_cache[key]

    supplies: Dict[tuple, float] = {}
    goods: List[dict] = []
    params_cache: Dict[tuple, MarketParams] = {}

    for g_idx, grows in rows_by_good.items():
        # base_value is a property of the good (item.value), identical at every
        # port; only supply differs. Fit it once from the ports' quotes and use
        # the median so a single noisy port cannot skew the model.
        derived = []
        for r in grows:
            value, _ = derive_base_value(base_params(r["port_index"], g_idx), r)
            if value is not None:
                derived.append(value)
        good_base_value = (
            median(derived) if derived else base_params(grows[0]["port_index"], g_idx).base_value
        )

        for r in grows:
            p_idx = int(r["port_index"])
            params_cache[(p_idx, g_idx)] = replace(
                base_params(p_idx, g_idx), base_value=good_base_value
            )
            supplies[(p_idx, g_idx)] = float(r["supply"])

        first = grows[0]
        goods.append(
            {
                "good_index": g_idx,
                "good_name": first["good_name"],
                "weight_lb": float(first["weight_lb"] or 0.0),
                "volume_cuft": parse_volume_cuft(first.get("size_description")),
                "available_by_port": {
                    int(r["port_index"]): bool(r["available"]) for r in grows
                },
                "buy_qty_by_port": {
                    int(r["port_index"]): int(r["buy_qty"] or 0) for r in grows
                },
            }
        )

    return supplies, goods, params_cache


def plan_route(
    conn,
    port_indices: List[int],
    budget_raw: int,
    *,
    snapshot_id: Optional[int] = None,
    max_weight: Optional[float] = None,
    max_volume: Optional[float] = None,
) -> RoutePlan:
    if len(port_indices) < 2:
        raise ValueError("route needs at least 2 ports")

    if snapshot_id is None:
        snapshot_id = conn.execute(
            "SELECT id FROM snapshots ORDER BY id DESC LIMIT 1"
        ).fetchone()[0]

    ordered = load_route_ports(conn, port_indices, snapshot_id)
    route = [(p[0], p[1]) for p in ordered]
    supplies, goods, params_cache = load_route_market(conn, port_indices, snapshot_id)

    if not goods:
        raise ValueError("no goods on route ports")

    def params_for(port_index: int, good_index: int) -> MarketParams:
        return params_cache[(port_index, good_index)]

    return sequential_route_plan(
        route,
        goods,
        supplies,
        params_for,
        budget_raw,
        max_weight=max_weight,
        max_volume=max_volume,
    )


def deal_to_dict(deal: RouteDeal, regions: Dict[int, int]) -> dict:
    return {
        "good_index": deal.good_index,
        "good_name": deal.good_name,
        "buy_port_index": deal.buy_port_index,
        "buy_port_name": deal.buy_port_name,
        "quantity": deal.quantity,
        "buy_total_raw": deal.buy_total,
        "sell_total_raw": deal.sell_total,
        "profit_raw": deal.profit,
        "weight_lb": deal.weight_total,
        "volume_cuft": deal.volume_total,
        "buy_unit_prices_raw": deal.buy_unit_prices,
        "sell_legs": [
            {
                "port_index": leg.port_index,
                "port_name": leg.port_name,
                "quantity": leg.quantity,
                "unit_prices_raw": leg.unit_prices,
                "total_revenue_raw": leg.total_revenue,
            }
            for leg in deal.sell_legs
        ],
    }


def plan_to_dict(plan: RoutePlan, port_regions: Dict[int, int]) -> dict:
    return {
        "route": plan.route,
        "route_names": plan.route_names,
        "budget_raw": plan.budget,
        "budget_spent_raw": plan.budget_spent,
        "budget_left_raw": plan.budget_left,
        "max_weight": plan.max_weight,
        "weight_used": plan.weight_used,
        "max_volume": plan.max_volume,
        "volume_used": plan.volume_used,
        "total_profit_raw": plan.total_profit,
        "deals": [deal_to_dict(d, port_regions) for d in plan.deals],
    }
