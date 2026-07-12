"""
Sailwind island market price model (v0.38 IslandMarket.cs).

Prices are driven by currentSupply. Each buy lowers supply (prices rise);
each sell raises supply (prices fall). Buy/sell spreads are applied on top.
"""

from __future__ import annotations

import math
from dataclasses import dataclass
from typing import List, Optional


def _clamp01(value: float) -> float:
    return max(0.0, min(1.0, value))


def inverse_lerp(a: float, b: float, value: float) -> float:
    """Unity Mathf.InverseLerp."""
    if a == b:
        return 0.0
    return _clamp01((value - a) / (b - a))


def lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


@dataclass
class MarketParams:
    """Tunable globals (DebugMarketTracker) + per-island fields."""

    goods_soft_cap: float = 57.0
    positive_price_mult: float = 0.5
    negative_price_mult: float = 1.0
    supply_purchase_limit: float = 0.0
    base_value: float = 1000.0

    def max_buy_qty(self, supply: float) -> int:
        if supply < self.supply_purchase_limit:
            return 0
        return max(0, int(math.floor(max(0.0, supply - self.supply_purchase_limit + 1.0))))


def good_price_at_supply(supply: float, params: MarketParams) -> int:
    """Mid price before buy/sell spread (GetGoodPriceAtSupply)."""
    base = params.base_value
    soft_cap = params.goods_soft_cap

    if supply >= 0.0:
        t = inverse_lerp(soft_cap, 0.0, supply)
        curve = 1.0 - t * t
        mid = base - base * params.positive_price_mult * curve
    else:
        t = inverse_lerp(-soft_cap, 0.0, supply)
        curve = (1.0 - t * t) * -1.0
        mid = base - base * params.negative_price_mult * curve

    return int(round(mid))


def spread(raw_price: float) -> float:
    """GetSpread — wider % on cheap goods, tighter on expensive."""
    t = inverse_lerp(1000.0, 30000.0, raw_price)
    return lerp(0.005, 0.0001, t)


def _price_curve_factor(supply: float, params: MarketParams) -> float:
    """good_price_at_supply with base_value=1 (float, unrounded).

    Price is linear in base_value, so base_value = observed_mid / factor.
    """
    soft_cap = params.goods_soft_cap
    if supply >= 0.0:
        t = inverse_lerp(soft_cap, 0.0, supply)
        curve = 1.0 - t * t
        return 1.0 - params.positive_price_mult * curve
    t = inverse_lerp(-soft_cap, 0.0, supply)
    curve = (1.0 - t * t) * -1.0
    return 1.0 - params.negative_price_mult * curve


def base_value_from_buy(
    supply: float, buy_raw: int, params: MarketParams
) -> Optional[float]:
    """Solve base_value so the next buy at `supply` matches `buy_raw`.

    Closed-form (linear in base_value) with a few spread iterations. Works for
    any supply sign and never raises. Returns None if not derivable.
    """
    factor = _price_curve_factor(supply, params)
    if factor <= 0.0 or buy_raw <= 0:
        return None
    mid = float(buy_raw)
    for _ in range(6):
        mid = buy_raw / (1.0 + spread(mid))
    value = mid / factor
    return value if value > 0.0 else None


def base_value_from_sell(
    supply: float, sell_raw: int, params: MarketParams
) -> Optional[float]:
    """Solve base_value so the next sell at `supply` matches `sell_raw`."""
    factor = _price_curve_factor(supply + 1.0, params)
    if factor <= 0.0 or sell_raw <= 0:
        return None
    mid = float(sell_raw)
    for _ in range(6):
        mid = sell_raw / (1.0 - spread(mid))
    value = mid / factor
    return value if value > 0.0 else None


def buy_price_at_supply(supply: float, params: MarketParams) -> int:
    """Price for the next buy at this supply (GetBuyPrice before PurchaseGood)."""
    mid = good_price_at_supply(supply, params)
    return int(round(mid * (1.0 + spread(mid))))


def sell_price_at_supply(supply: float, params: MarketParams) -> int:
    """Price for the next sell at this supply (GetSellPrice before SellGood)."""
    mid = good_price_at_supply(supply + 1.0, params)
    return int(round(mid * (1.0 - spread(mid))))


def has_good(supply: float, params: MarketParams) -> bool:
    return supply >= params.supply_purchase_limit


def buy_qty_display(supply: float, params: MarketParams) -> int:
    """EconomyUI displayed buyable count."""
    return params.max_buy_qty(supply)


def calibrate_base_value(
    supply: float,
    observed_buy_raw: int,
    params: MarketParams,
    *,
    low: float = 100.0,
    high: float = 100000.0,
) -> float:
    """
    Find base_value so buy_price_at_supply(supply) matches observed_buy_raw.
    Use when you know one in-game buy quote and the snapshot supply.
    """

    def error(base_value: float) -> int:
        trial = MarketParams(
            goods_soft_cap=params.goods_soft_cap,
            positive_price_mult=params.positive_price_mult,
            negative_price_mult=params.negative_price_mult,
            supply_purchase_limit=params.supply_purchase_limit,
            base_value=base_value,
        )
        return buy_price_at_supply(supply, trial) - observed_buy_raw

    lo, hi = low, high
    if error(lo) > 0 or error(hi) < 0:
        raise ValueError(
            f"Cannot bracket base_value for buy={observed_buy_raw} at supply={supply}. "
            f"Try widening search range or check params."
        )

    for _ in range(80):
        mid = (lo + hi) / 2.0
        if error(mid) <= 0:
            lo = mid
        else:
            hi = mid

    center = int(round(lo))
    for candidate in range(center, center - 50, -1):
        trial = MarketParams(
            goods_soft_cap=params.goods_soft_cap,
            positive_price_mult=params.positive_price_mult,
            negative_price_mult=params.negative_price_mult,
            supply_purchase_limit=params.supply_purchase_limit,
            base_value=float(candidate),
        )
        if buy_price_at_supply(supply, trial) == observed_buy_raw:
            return float(candidate)
    for candidate in range(center + 1, center + 51):
        trial = MarketParams(
            goods_soft_cap=params.goods_soft_cap,
            positive_price_mult=params.positive_price_mult,
            negative_price_mult=params.negative_price_mult,
            supply_purchase_limit=params.supply_purchase_limit,
            base_value=float(candidate),
        )
        if buy_price_at_supply(supply, trial) == observed_buy_raw:
            return float(candidate)

    raise ValueError(
        f"No integer base_value matches buy={observed_buy_raw} at supply={supply} "
        f"(nearest search around {center})."
    )


def calibrate_base_value_from_sell(
    supply: float,
    observed_sell_raw: int,
    params: MarketParams,
    *,
    low: float = 100.0,
    high: float = 100000.0,
) -> float:
    """Find base_value so sell_price_at_supply(supply) matches observed_sell_raw."""

    def error(base_value: float) -> int:
        trial = MarketParams(
            goods_soft_cap=params.goods_soft_cap,
            positive_price_mult=params.positive_price_mult,
            negative_price_mult=params.negative_price_mult,
            supply_purchase_limit=params.supply_purchase_limit,
            base_value=base_value,
        )
        return sell_price_at_supply(supply, trial) - observed_sell_raw

    lo, hi = low, high
    if error(lo) > 0 or error(hi) < 0:
        raise ValueError(
            f"Cannot bracket base_value for sell={observed_sell_raw} at supply={supply}. "
            f"Try widening search range or check params."
        )

    for _ in range(80):
        mid = (lo + hi) / 2.0
        if error(mid) <= 0:
            lo = mid
        else:
            hi = mid

    center = int(round(lo))
    for candidate in range(center, center - 50, -1):
        trial = MarketParams(
            goods_soft_cap=params.goods_soft_cap,
            positive_price_mult=params.positive_price_mult,
            negative_price_mult=params.negative_price_mult,
            supply_purchase_limit=params.supply_purchase_limit,
            base_value=float(candidate),
        )
        if sell_price_at_supply(supply, trial) == observed_sell_raw:
            return float(candidate)
    for candidate in range(center + 1, center + 51):
        trial = MarketParams(
            goods_soft_cap=params.goods_soft_cap,
            positive_price_mult=params.positive_price_mult,
            negative_price_mult=params.negative_price_mult,
            supply_purchase_limit=params.supply_purchase_limit,
            base_value=float(candidate),
        )
        if sell_price_at_supply(supply, trial) == observed_sell_raw:
            return float(candidate)

    raise ValueError(
        f"No integer base_value matches sell={observed_sell_raw} at supply={supply} "
        f"(nearest search around {center})."
    )


def calibrate_supply_from_buy(observed_buy_raw: int, params: MarketParams) -> float:
    """Find supply where the next buy price matches observed_buy_raw."""

    def price_at(supply: float) -> int:
        return buy_price_at_supply(supply, params)

    return _calibrate_supply(observed_buy_raw, price_at)


def calibrate_supply_from_sell(observed_sell_raw: int, params: MarketParams) -> float:
    """Find supply where the next sell price matches observed_sell_raw."""

    def price_at(supply: float) -> int:
        return sell_price_at_supply(supply, params)

    return _calibrate_supply(observed_sell_raw, price_at)


def _calibrate_supply(observed: int, price_at) -> float:
    lo, hi = -80.0, 80.0
    best_supply = lo
    best_error = 10**9
    for _ in range(160):
        mid = (lo + hi) / 2.0
        price = price_at(mid)
        error = abs(price - observed)
        if error < best_error:
            best_error = error
            best_supply = mid
        if price == observed:
            return mid
        if price < observed:
            hi = mid
        else:
            lo = mid
    if best_error > 0:
        # Fine scan around best (rounding can skip exact bracket midpoints).
        center = best_supply
        for step in (0.05, 0.01):
            for delta in [i * step for i in range(-200, 201)]:
                candidate = center + delta
                if price_at(candidate) == observed:
                    return candidate
    return best_supply


@dataclass
class BuySimulation:
    unit_prices: List[int]
    total_cost: int
    supply_start: float
    supply_end: float
    quantity_requested: int
    quantity_bought: int

    @property
    def average_price(self) -> Optional[float]:
        if not self.unit_prices:
            return None
        return self.total_cost / len(self.unit_prices)


def simulate_buy(
    supply_start: float,
    quantity: int,
    params: MarketParams,
) -> BuySimulation:
    """Sequential buys: charge GetBuyPrice at current supply, then supply -= 1."""
    supply = supply_start
    prices: List[int] = []

    for _ in range(quantity):
        if not has_good(supply, params):
            break
        prices.append(buy_price_at_supply(supply, params))
        supply -= 1.0

    return BuySimulation(
        unit_prices=prices,
        total_cost=sum(prices),
        supply_start=supply_start,
        supply_end=supply,
        quantity_requested=quantity,
        quantity_bought=len(prices),
    )


@dataclass
class SellSimulation:
    unit_prices: List[int]
    total_revenue: int
    supply_start: float
    supply_end: float
    quantity_requested: int
    quantity_sold: int


def simulate_sell(
    supply_start: float,
    quantity: int,
    params: MarketParams,
) -> SellSimulation:
    """Sequential sells at one port."""
    supply = supply_start
    prices: List[int] = []

    for _ in range(quantity):
        prices.append(sell_price_at_supply(supply, params))
        supply += 1.0

    return SellSimulation(
        unit_prices=prices,
        total_revenue=sum(prices),
        supply_start=supply_start,
        supply_end=supply,
        quantity_requested=quantity,
        quantity_sold=len(prices),
    )


def simulate_round_trip_buy_then_sell(
    buy_supply: float,
    sell_supply: float,
    quantity: int,
    buy_params: MarketParams,
    sell_params: MarketParams,
) -> dict:
    """Buy Q at port A then sell Q at port B (independent supply pools)."""
    buy = simulate_buy(buy_supply, quantity, buy_params)
    sell = simulate_sell(sell_supply, buy.quantity_bought, sell_params)
    profit = sell.total_revenue - buy.total_cost
    return {
        "buy": buy,
        "sell": sell,
        "profit": profit,
        "profit_per_unit": profit / buy.quantity_bought if buy.quantity_bought else None,
    }
