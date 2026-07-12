"""
Route planner: ordered ports, bulk buy/sell simulation.

Deals are constrained to the route: buy at an earlier stop, sell at later stop(s).
Sell quantity is split optimally across downstream ports (diminishing prices).
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, List, Optional, Sequence, Tuple

from market_model import (
    MarketParams,
    sell_price_at_supply,
    simulate_buy,
    simulate_sell,
)


def _params_at(params, port_index: int) -> MarketParams:
    """Resolve params for a port. Accepts a single MarketParams, a dict, or a callable."""
    if callable(params):
        return params(port_index)
    if isinstance(params, dict):
        return params[port_index]
    return params


@dataclass
class SellLeg:
    port_index: int
    port_name: str
    quantity: int
    unit_prices: List[int]
    total_revenue: int


@dataclass
class RouteDeal:
    good_index: int
    good_name: str
    buy_port_index: int
    buy_port_name: str
    sell_legs: List[SellLeg]
    quantity: int
    buy_unit_prices: List[int]
    buy_total: int
    sell_total: int
    profit: int
    weight_total: float
    volume_total: float


@dataclass
class RoutePlan:
    route: List[int]
    route_names: List[str]
    budget: int
    budget_spent: int
    budget_left: int
    max_weight: Optional[float]
    weight_used: float
    max_volume: Optional[float]
    volume_used: float
    deals: List[RouteDeal] = field(default_factory=list)
    total_profit: int = 0


def _qty_cap_for_capacity(
    max_qty: int,
    weight_lb: float,
    max_weight: Optional[float],
    volume_cuft: float,
    max_volume: Optional[float],
) -> int:
    qty_cap = max_qty
    if max_weight is not None and weight_lb > 0:
        qty_cap = min(qty_cap, int(max_weight // weight_lb))
    if max_volume is not None and volume_cuft > 0:
        qty_cap = min(qty_cap, int(max_volume // volume_cuft))
    return qty_cap


def optimal_sell_split(
    sell_ports: Sequence[Tuple[int, str]],
    qty: int,
    supply_by_port: Dict[int, float],
    params,
) -> Tuple[List[SellLeg], int]:
    """
    Best revenue selling `qty` units across ports, each with its own supply.

    `params` may be a single MarketParams, a {port_index: MarketParams} dict, or
    a callable(port_index) -> MarketParams.

    Greedy by marginal price: sell each unit at whichever port currently offers
    the highest sell price. Because each port's marginal sell price only
    decreases as supply rises, this greedy allocation is revenue-optimal and runs
    in O(qty * ports) instead of enumerating every split.
    """
    if qty <= 0 or not sell_ports:
        return [], 0

    order = [p[0] for p in sell_ports]
    names = {p[0]: p[1] for p in sell_ports}
    start_supply = {p[0]: supply_by_port.get(p[0], 0.0) for p in sell_ports}

    live_supply = dict(start_supply)
    allocation = {idx: 0 for idx in order}

    for _ in range(qty):
        best_port = None
        best_price = 0
        for idx in order:
            price = sell_price_at_supply(live_supply[idx], _params_at(params, idx))
            if best_port is None or price > best_price:
                best_port, best_price = idx, price
        if best_port is None or best_price <= 0:
            break
        allocation[best_port] += 1
        live_supply[best_port] += 1.0

    legs: List[SellLeg] = []
    revenue = 0
    for idx in order:
        k = allocation[idx]
        if k <= 0:
            continue
        sim = simulate_sell(start_supply[idx], k, _params_at(params, idx))
        legs.append(
            SellLeg(
                port_index=idx,
                port_name=names[idx],
                quantity=sim.quantity_sold,
                unit_prices=sim.unit_prices,
                total_revenue=sim.total_revenue,
            )
        )
        revenue += sim.total_revenue

    return legs, revenue


def best_deal_for_pair(
    buy_port: Tuple[int, str],
    sell_ports: Sequence[Tuple[int, str]],
    good_index: int,
    good_name: str,
    supply_buy: float,
    supplies_sell: Dict[int, float],
    params: MarketParams,
    budget: int,
    max_qty: int,
    weight_lb: float,
    max_weight: Optional[float],
    volume_cuft: float,
    max_volume: Optional[float],
    sell_params=None,
) -> Optional[RouteDeal]:
    """Max-profit qty for one (buy, downstream sells) pair within budget/weight/volume.

    `params` prices the buy. Sells are priced with `sell_params` (single
    MarketParams / dict / callable); when None the buy `params` are reused. Using
    each sell port's own params keeps evaluation consistent with execution.
    """
    best: Optional[RouteDeal] = None
    sell_params = params if sell_params is None else sell_params

    qty_cap = _qty_cap_for_capacity(
        max_qty, weight_lb, max_weight, volume_cuft, max_volume
    )

    for qty in range(1, qty_cap + 1):
        buy_sim = simulate_buy(supply_buy, qty, params)
        if buy_sim.quantity_bought < qty:
            continue
        if buy_sim.total_cost > budget:
            break
        if max_weight is not None and weight_lb > 0 and weight_lb * qty > max_weight:
            break
        if max_volume is not None and volume_cuft > 0 and volume_cuft * qty > max_volume:
            break

        bought = buy_sim.quantity_bought
        sell_legs, sell_total = optimal_sell_split(
            sell_ports, bought, supplies_sell, sell_params
        )
        if sum(leg.quantity for leg in sell_legs) < bought:
            continue

        profit = sell_total - buy_sim.total_cost
        if profit <= 0:
            continue

        deal = RouteDeal(
            good_index=good_index,
            good_name=good_name,
            buy_port_index=buy_port[0],
            buy_port_name=buy_port[1],
            sell_legs=sell_legs,
            quantity=bought,
            buy_unit_prices=buy_sim.unit_prices,
            buy_total=buy_sim.total_cost,
            sell_total=sell_total,
            profit=profit,
            weight_total=weight_lb * bought,
            volume_total=volume_cuft * bought,
        )
        if best is None or deal.profit > best.profit:
            best = deal

    return best


@dataclass
class OnBoardBatch:
    deal: RouteDeal
    qty_remaining: int
    weight_lb: float
    volume_cuft: float


def _sell_at_port(
    port_index: int,
    batches: List[OnBoardBatch],
    working_supplies: Dict[Tuple[int, int], float],
    params_for: callable,
    cash: int,
    on_board_weight: float,
    on_board_volume: float,
) -> Tuple[int, float, float, int]:
    """Unload cargo scheduled for this port."""
    revenue = 0
    for batch in batches:
        if batch.qty_remaining <= 0:
            continue
        deal = batch.deal
        g = deal.good_index
        for leg in deal.sell_legs:
            if leg.port_index != port_index or leg.quantity <= 0:
                continue
            supply = working_supplies.get((port_index, g), 0.0)
            sell_sim = simulate_sell(supply, leg.quantity, params_for(port_index, g))
            if sell_sim.quantity_sold <= 0:
                continue
            revenue += sell_sim.total_revenue
            sold_qty = sell_sim.quantity_sold
            on_board_weight = max(0.0, on_board_weight - batch.weight_lb * sold_qty)
            on_board_volume = max(0.0, on_board_volume - batch.volume_cuft * sold_qty)
            batch.qty_remaining -= sold_qty
            leg.quantity = sold_qty
            leg.unit_prices = sell_sim.unit_prices
            leg.total_revenue = sell_sim.total_revenue
            working_supplies[(port_index, g)] = sell_sim.supply_end
    return cash + revenue, on_board_weight, on_board_volume, revenue


def _best_deal_at_port(
    buy_i: int,
    route_list: Sequence[Tuple[int, str]],
    goods: Sequence[dict],
    working_supplies: Dict[Tuple[int, int], float],
    params_for: callable,
    cash: int,
    on_board_weight: float,
    max_weight: Optional[float],
    on_board_volume: float,
    max_volume: Optional[float],
    on_board_goods: set,
) -> Optional[RouteDeal]:
    buy_port = route_list[buy_i]
    sell_ports = route_list[buy_i + 1 :]
    best: Optional[RouteDeal] = None

    for good in goods:
        g_idx = good["good_index"]
        if g_idx in on_board_goods:
            continue
        if not good.get("available_by_port", {}).get(buy_port[0]):
            continue
        max_qty = int(good.get("buy_qty_by_port", {}).get(buy_port[0], 0))
        if max_qty <= 0:
            continue

        weight_lb = float(good.get("weight_lb") or 0.0)
        volume_cuft = float(good.get("volume_cuft") or 0.0)
        sell_supplies = {
            p[0]: working_supplies.get((p[0], g_idx), 0.0) for p in sell_ports
        }
        sell_params = {p[0]: params_for(p[0], g_idx) for p in sell_ports}
        free_weight = (max_weight - on_board_weight) if max_weight is not None else None
        free_volume = (max_volume - on_board_volume) if max_volume is not None else None
        deal = best_deal_for_pair(
            buy_port,
            sell_ports,
            g_idx,
            good["good_name"],
            working_supplies.get((buy_port[0], g_idx), 0.0),
            sell_supplies,
            params_for(buy_port[0], g_idx),
            cash,
            max_qty,
            weight_lb,
            free_weight,
            volume_cuft,
            free_volume,
            sell_params=sell_params,
        )
        if deal is not None and (best is None or deal.profit > best.profit):
            best = deal

    return best


def sequential_route_plan(
    route: Sequence[Tuple[int, str]],
    goods: Sequence[dict],
    supplies: Dict[Tuple[int, int], float],
    params_for: callable,
    budget: int,
    max_weight: Optional[float] = None,
    max_volume: Optional[float] = None,
) -> RoutePlan:
    """
    Walk the route port by port: sell on arrival, then buy for downstream ports.

    Cash and hold capacity freed at intermediate stops become available for
    the next leg (e.g. Oasis → Academy sell → Academy → GRC buy).
    """
    route_list = list(route)
    port_indices = [p[0] for p in route_list]
    working_supplies = dict(supplies)
    cash = budget
    on_board_weight = 0.0
    on_board_volume = 0.0
    peak_weight = 0.0
    peak_volume = 0.0
    batches: List[OnBoardBatch] = []
    plan_deals: List[RouteDeal] = []

    for stop_i, port in enumerate(route_list):
        port_index = port[0]
        cash, on_board_weight, on_board_volume, _ = _sell_at_port(
            port_index,
            batches,
            working_supplies,
            params_for,
            cash,
            on_board_weight,
            on_board_volume,
        )

        if stop_i >= len(route_list) - 1:
            continue

        on_board_goods = {b.deal.good_index for b in batches if b.qty_remaining > 0}

        while True:
            deal = _best_deal_at_port(
                stop_i,
                route_list,
                goods,
                working_supplies,
                params_for,
                cash,
                on_board_weight,
                max_weight,
                on_board_volume,
                max_volume,
                on_board_goods,
            )
            if deal is None:
                break

            buy_sim = simulate_buy(
                working_supplies[(deal.buy_port_index, deal.good_index)],
                deal.quantity,
                params_for(deal.buy_port_index, deal.good_index),
            )
            working_supplies[(deal.buy_port_index, deal.good_index)] = buy_sim.supply_end

            cash -= deal.buy_total
            weight_lb = deal.weight_total / deal.quantity if deal.quantity else 0.0
            volume_cuft = deal.volume_total / deal.quantity if deal.quantity else 0.0
            on_board_weight += deal.weight_total
            on_board_volume += deal.volume_total
            peak_weight = max(peak_weight, on_board_weight)
            peak_volume = max(peak_volume, on_board_volume)

            batches.append(
                OnBoardBatch(
                    deal=deal,
                    qty_remaining=deal.quantity,
                    weight_lb=weight_lb,
                    volume_cuft=volume_cuft,
                )
            )
            plan_deals.append(deal)
            on_board_goods.add(deal.good_index)

    # Legs may have been re-simulated during the walk (supplies shifted by other
    # deals on the same port/good). Recompute each deal's totals from the actual
    # executed legs so displayed profit matches what really happened.
    for deal in plan_deals:
        deal.sell_total = sum(leg.total_revenue for leg in deal.sell_legs)
        deal.profit = deal.sell_total - deal.buy_total

    total_profit = cash - budget
    total_spent = sum(d.buy_total for d in plan_deals)

    return RoutePlan(
        route=port_indices,
        route_names=[p[1] for p in route_list],
        budget=budget,
        budget_spent=total_spent,
        budget_left=cash,
        max_weight=max_weight,
        weight_used=peak_weight,
        max_volume=max_volume,
        volume_used=peak_volume,
        deals=plan_deals,
        total_profit=total_profit,
    )


def greedy_route_plan(
    route: Sequence[Tuple[int, str]],
    goods: Sequence[dict],
    supplies: Dict[Tuple[int, int], float],
    params_for: callable,
    budget: int,
    max_weight: Optional[float] = None,
    max_volume: Optional[float] = None,
) -> RoutePlan:
    """
    Enumerate route-constrained deals (buy before sell), rank by profit,
    greedily pack until budget/weight exhausted.

    `goods` items: good_index, good_name, weight_lb, buy_qty per port via supplies.
    `params_for(port_index, good_index) -> MarketParams`
    """
    route_list = list(route)
    port_indices = [p[0] for p in route_list]
    candidates: List[RouteDeal] = []

    for good in goods:
        g_idx = good["good_index"]
        g_name = good["good_name"]
        weight_lb = float(good.get("weight_lb") or 0.0)
        volume_cuft = float(good.get("volume_cuft") or 0.0)

        for buy_i in range(len(route_list) - 1):
            buy_port = route_list[buy_i]
            if not good.get("available_by_port", {}).get(buy_port[0]):
                continue
            max_qty = int(good.get("buy_qty_by_port", {}).get(buy_port[0], 0))
            if max_qty <= 0:
                continue

            for sell_j in range(buy_i + 1, len(route_list)):
                sell_ports = route_list[buy_i + 1 : sell_j + 1]
                sell_supplies = {
                    p[0]: supplies.get((p[0], g_idx), 0.0) for p in sell_ports
                }
                params = params_for(buy_port[0], g_idx)
                supply_buy = supplies.get((buy_port[0], g_idx), 0.0)

                deal = best_deal_for_pair(
                    buy_port,
                    sell_ports,
                    g_idx,
                    g_name,
                    supply_buy,
                    sell_supplies,
                    params,
                    budget,
                    max_qty,
                    weight_lb,
                    max_weight,
                    volume_cuft,
                    max_volume,
                    sell_params={p[0]: params_for(p[0], g_idx) for p in sell_ports},
                )
                if deal is not None:
                    candidates.append(deal)

    candidates.sort(key=lambda d: d.profit, reverse=True)

    plan = RoutePlan(
        route=port_indices,
        route_names=[p[1] for p in route_list],
        budget=budget,
        budget_spent=0,
        budget_left=budget,
        max_weight=max_weight,
        weight_used=0.0,
        max_volume=max_volume,
        volume_used=0.0,
    )

    used_good: set = set()
    working_supplies = dict(supplies)
    goods_by_index = {g["good_index"]: g for g in goods}

    for deal in candidates:
        if deal.good_index in used_good:
            continue
        if deal.buy_total > plan.budget_left:
            continue
        if max_weight is not None and deal.weight_total + plan.weight_used > max_weight:
            continue
        if max_volume is not None and deal.volume_total + plan.volume_used > max_volume:
            continue

        # Re-verify with current supplies (earlier deals may have shifted same port/good)
        buy_port = (deal.buy_port_index, deal.buy_port_name)
        # Rebuild sell_ports sequence preserving order along route
        sell_ports_ordered = [
            p for p in route_list if p[0] in {leg.port_index for leg in deal.sell_legs}
        ]
        sell_j = max(port_indices.index(p[0]) for p in sell_ports_ordered)
        buy_i = port_indices.index(deal.buy_port_index)
        sell_ports_ordered = route_list[buy_i + 1 : sell_j + 1]
        good_row = goods_by_index[deal.good_index]
        max_qty = int(good_row.get("buy_qty_by_port", {}).get(deal.buy_port_index, 0))

        refreshed = best_deal_for_pair(
            buy_port,
            sell_ports_ordered,
            deal.good_index,
            deal.good_name,
            working_supplies.get((deal.buy_port_index, deal.good_index), 0.0),
            {p[0]: working_supplies.get((p[0], deal.good_index), 0.0) for p in sell_ports_ordered},
            params_for(deal.buy_port_index, deal.good_index),
            plan.budget_left,
            max_qty,
            deal.weight_total / deal.quantity if deal.quantity else 0.0,
            max_weight - plan.weight_used if max_weight is not None else None,
            deal.volume_total / deal.quantity if deal.quantity else 0.0,
            max_volume - plan.volume_used if max_volume is not None else None,
            sell_params={
                p[0]: params_for(p[0], deal.good_index) for p in sell_ports_ordered
            },
        )
        if refreshed is None or refreshed.profit <= 0:
            continue

        plan.deals.append(refreshed)
        plan.budget_spent += refreshed.buy_total
        plan.budget_left -= refreshed.buy_total
        plan.weight_used += refreshed.weight_total
        plan.volume_used += refreshed.volume_total
        plan.total_profit += refreshed.profit
        used_good.add(deal.good_index)

        # Update supplies
        g = deal.good_index
        buy_sim = simulate_buy(
            working_supplies[(refreshed.buy_port_index, g)],
            refreshed.quantity,
            params_for(refreshed.buy_port_index, g),
        )
        working_supplies[(refreshed.buy_port_index, g)] = buy_sim.supply_end
        for leg in refreshed.sell_legs:
            sell_sim = simulate_sell(
                working_supplies.get((leg.port_index, g), 0.0),
                leg.quantity,
                params_for(leg.port_index, g),
            )
            working_supplies[(leg.port_index, g)] = sell_sim.supply_end

    return plan
