import json
import math
import sqlite3
from pathlib import Path

from flask import Flask, jsonify, render_template, request

from market_sim import (
    load_market_globals,
    simulate_port_buy,
    simulate_port_buy_then_sell,
    simulate_port_sell,
)
from route_planner import plan_route, plan_to_dict

ROOT = Path(__file__).resolve().parent
CONFIG_PATH = ROOT / "config.json"

REGION_NAMES = {0: "Al'Ankh", 1: "Emerald", 2: "Aestrin"}


def convert_buy(raw, price_index, with_fee, exchange_fee):
    base = price_index * raw
    if with_fee:
        return math.ceil(base * (1 + exchange_fee))
    return round(base)


def convert_sell(raw, price_index, with_fee, exchange_fee):
    base = price_index * raw
    if with_fee:
        return math.floor(base * (1 - exchange_fee))
    return round(base)


def needs_conversion_fee(port_region, currency_index):
    return port_region != currency_index


def avg_or_none(values):
    return round(sum(values) / len(values)) if values else None


def compute_port_averages(rows, currency_index, exchange_fee, archipelago_region=None):
    all_buys = []
    all_sells = []
    arch_buys = []
    arch_sells = []

    for row in rows:
        region = row["region"]
        price_index = row["price_index"]
        with_fee = needs_conversion_fee(region, currency_index)
        buy = None
        if row["available"]:
            buy = convert_buy(row["buy_raw"], price_index, with_fee, exchange_fee)
            all_buys.append(buy)
            if archipelago_region is not None and region == archipelago_region:
                arch_buys.append(buy)
        sell = convert_sell(row["sell_raw"], price_index, with_fee, exchange_fee)
        all_sells.append(sell)
        if archipelago_region is not None and region == archipelago_region:
            arch_sells.append(sell)

    return {
        "global_buy_avg": avg_or_none(all_buys),
        "global_sell_avg": avg_or_none(all_sells),
        "arch_buy_avg": avg_or_none(arch_buys),
        "arch_sell_avg": avg_or_none(arch_sells),
        "buy_count": len(all_buys),
        "sell_count": len(all_sells),
    }


def fetch_averages_by_snapshot(
    conn,
    snapshot_ids,
    good_index,
    port_indices,
    currency_index,
    archipelago_region=None,
):
    if not snapshot_ids or not port_indices:
        return {}

    snap_ph = ",".join("?" for _ in snapshot_ids)
    port_ph = ",".join("?" for _ in port_indices)
    rows = conn.execute(
        f"""
        SELECT p.snapshot_id, p.region, p.buy_raw, p.sell_raw, p.available, cp.price_index
        FROM port_prices p
        JOIN currency_prices cp ON cp.snapshot_id = p.snapshot_id AND cp.currency_index = ?
        WHERE p.good_index = ? AND p.snapshot_id IN ({snap_ph}) AND p.port_index IN ({port_ph})
        """,
        (currency_index, good_index, *snapshot_ids, *port_indices),
    ).fetchall()

    rate_rows = conn.execute(
        f"""
        SELECT snapshot_id, sell_currency, buy_currency, rate, rate_with_fee
        FROM currency_rates WHERE snapshot_id IN ({snap_ph})
        """,
        snapshot_ids,
    ).fetchall()

    rates_by_snap = {}
    for row in rate_rows:
        rates_by_snap.setdefault(row["snapshot_id"], []).append(dict(row))

    by_snap = {}
    for row in rows:
        by_snap.setdefault(row["snapshot_id"], []).append(dict(row))

    result = {}
    for snapshot_id in snapshot_ids:
        snap_rows = by_snap.get(snapshot_id, [])
        fee = derive_exchange_fee(rates_by_snap.get(snapshot_id, []))
        result[snapshot_id] = compute_port_averages(
            snap_rows, currency_index, fee, archipelago_region
        )
    return result


def rows_to_ports_out(rows, currency_index, exchange_fee):
    ports_out = []
    for row in rows:
        item = dict(row)
        region = item["region"]
        with_fee = needs_conversion_fee(region, currency_index)
        buy = None
        buy_qty = None
        if item["available"]:
            buy = convert_buy(item["buy_raw"], item["price_index"], with_fee, exchange_fee)
            buy_qty = int(item["buy_qty"] or 0)
        sell = convert_sell(item["sell_raw"], item["price_index"], with_fee, exchange_fee)
        ports_out.append({
            "port_index": item["port_index"],
            "port_name": item["port_name"],
            "region": region,
            "buy": buy,
            "sell": sell,
            "buy_qty": buy_qty,
            "available": bool(item["available"]),
        })
    return ports_out


def best_route_from_ports(ports_out):
    buys = [port for port in ports_out if port["buy"] is not None]
    if not buys:
        return None
    best_buy = min(buys, key=lambda port: port["buy"])
    best_sell = max(ports_out, key=lambda port: port["sell"])
    profit = best_sell["sell"] - best_buy["buy"]
    if profit <= 0:
        return None
    profit_pct = None
    if best_buy["buy"] > 0:
        profit_pct = round(profit / best_buy["buy"] * 100, 1)
    return {
        "buy_port_index": best_buy["port_index"],
        "buy_port_name": best_buy["port_name"],
        "buy_price": best_buy["buy"],
        "buy_qty": best_buy.get("buy_qty"),
        "sell_port_index": best_sell["port_index"],
        "sell_port_name": best_sell["port_name"],
        "sell_price": best_sell["sell"],
        "profit": profit,
        "profit_pct": profit_pct,
    }


def catalog_for_good(conn, good_index):
    row = conn.execute(
        """
        SELECT good_index, good_name, size_description, weight_lb, base_value
        FROM goods_catalog WHERE good_index = ?
        """,
        (good_index,),
    ).fetchone()
    return dict(row) if row else None


def parse_port_indices(ports_raw):
    try:
        return [int(p.strip()) for p in ports_raw.split(",") if p.strip()]
    except ValueError:
        return None


def invert_buy_raw(display, price_index, with_fee, exchange_fee):
    target = int(display)
    for raw in range(1, 500_001):
        if convert_buy(raw, price_index, with_fee, exchange_fee) == target:
            return raw
    return max(1, round(target / price_index))


def load_price_index(conn, snapshot_id, currency_index):
    row = conn.execute(
        """
        SELECT price_index FROM currency_prices
        WHERE snapshot_id = ? AND currency_index = ?
        """,
        (snapshot_id, currency_index),
    ).fetchone()
    if row is None:
        raise ValueError(f"no price_index for currency {currency_index}")
    return float(row["price_index"])


def enrich_route_plan_display(
    plan_dict, conn, snapshot_id, currency_index, exchange_fee, budget_display
):
    port_regions = {
        int(row["port_index"]): int(row["region"])
        for row in conn.execute(
            """
            SELECT DISTINCT port_index, region
            FROM port_prices
            WHERE snapshot_id = ?
            """,
            (snapshot_id,),
        ).fetchall()
    }
    price_index = load_price_index(conn, snapshot_id, currency_index)

    def buy_display(raw, port_index):
        region = port_regions.get(port_index, currency_index)
        with_fee = needs_conversion_fee(region, currency_index)
        return convert_buy(raw, price_index, with_fee, exchange_fee)

    def sell_display(raw, port_index):
        region = port_regions.get(port_index, currency_index)
        with_fee = needs_conversion_fee(region, currency_index)
        return convert_sell(raw, price_index, with_fee, exchange_fee)

    deals_out = []
    for deal in plan_dict["deals"]:
        raw_prices = deal.get("buy_unit_prices_raw") or []
        if raw_prices:
            buy_total = sum(
                buy_display(raw, deal["buy_port_index"]) for raw in raw_prices
            )
        else:
            buy_total = buy_display(deal["buy_total_raw"], deal["buy_port_index"])
        sell_legs = []
        sell_total = 0
        for leg in deal["sell_legs"]:
            leg_total = sum(
                sell_display(raw, leg["port_index"]) for raw in leg["unit_prices_raw"]
            )
            sell_total += leg_total
            sell_legs.append({
                **leg,
                "total_revenue": leg_total,
                "unit_prices": [
                    sell_display(raw, leg["port_index"]) for raw in leg["unit_prices_raw"]
                ],
            })
        deals_out.append({
            **deal,
            "buy_total": buy_total,
            "sell_total": sell_total,
            "profit": sell_total - buy_total,
            "buy_unit_prices": [
                buy_display(raw, deal["buy_port_index"]) for raw in deal["buy_unit_prices_raw"]
            ],
            "sell_legs": sell_legs,
        })

    budget_spent = sum(d["buy_total"] for d in deals_out)
    cash_left_raw = plan_dict.get("budget_left_raw", plan_dict["budget_raw"])
    profit_raw = plan_dict.get("total_profit_raw", cash_left_raw - plan_dict["budget_raw"])
    cash_left = round(price_index * cash_left_raw)
    total_profit = round(price_index * profit_raw)
    return {
        **plan_dict,
        "currency_index": currency_index,
        "budget": budget_display,
        "budget_spent": budget_spent,
        "budget_left": cash_left,
        "total_profit": total_profit,
        "deals": deals_out,
    }


def load_config():
    if CONFIG_PATH.exists():
        with CONFIG_PATH.open(encoding="utf-8") as handle:
            return json.load(handle)
    return {}


CFG = load_config()
DB_PATH = Path(CFG.get("database_path", ROOT.parent / "data" / "routier.db"))

app = Flask(__name__, template_folder="templates", static_folder="static")


def connect():
    if not DB_PATH.exists():
        raise FileNotFoundError(f"Database not found: {DB_PATH}")
    conn = sqlite3.connect(f"file:{DB_PATH.as_posix()}?mode=ro", uri=True)
    conn.row_factory = sqlite3.Row
    return conn


@app.get("/")
def index():
    return render_template("index.html")


@app.get("/api/status")
def status():
    try:
        with connect() as conn:
            row = conn.execute(
                "SELECT id, captured_at, game_day, game_time FROM snapshots ORDER BY id DESC LIMIT 1"
            ).fetchone()
        if row is None:
            return jsonify({"ok": True, "database": str(DB_PATH), "latest": None, "market_globals": None})
        globals_row = conn.execute(
            """
            SELECT goods_soft_cap, positive_price_mult, negative_price_mult, updated_at
            FROM market_globals WHERE id = 1
            """
        ).fetchone()
        return jsonify({
            "ok": True,
            "database": str(DB_PATH),
            "latest": dict(row),
            "market_globals": dict(globals_row) if globals_row else None,
        })
    except FileNotFoundError as exc:
        return jsonify({"ok": False, "error": str(exc)}), 404


@app.get("/api/ports")
def ports():
    snapshot_id = request.args.get("snapshot_id", type=int)
    with connect() as conn:
        if snapshot_id is None:
            snapshot_id = conn.execute(
                "SELECT id FROM snapshots ORDER BY id DESC LIMIT 1"
            ).fetchone()[0]
        rows = conn.execute(
            """
            SELECT DISTINCT port_index, port_name, region
            FROM port_prices
            WHERE snapshot_id = ?
            ORDER BY region, port_name
            """,
            (snapshot_id,),
        ).fetchall()
    region_names = {0: "Al'Ankh", 1: "Emerald", 2: "Aestrin"}
    ports = [dict(r) for r in rows]
    archipelagos = []
    for region_id in sorted({p["region"] for p in ports}):
        archipelagos.append({
            "region": region_id,
            "name": region_names.get(region_id, f"Region {region_id}"),
            "ports": [
                {"port_index": p["port_index"], "port_name": p["port_name"]}
                for p in ports if p["region"] == region_id
            ],
        })
    return jsonify({
        "snapshot_id": snapshot_id,
        "ports": ports,
        "archipelagos": archipelagos,
    })


@app.get("/api/goods")
def goods():
    snapshot_id = request.args.get("snapshot_id", type=int)
    port_index = request.args.get("port_index", type=int)
    if port_index is None:
        return jsonify({"error": "port_index required"}), 400

    with connect() as conn:
        if snapshot_id is None:
            snapshot_id = conn.execute(
                "SELECT id FROM snapshots ORDER BY id DESC LIMIT 1"
            ).fetchone()[0]
        rows = conn.execute(
            """
            SELECT p.good_index, p.good_name, p.buy_raw, p.sell_raw, p.supply, p.available, p.buy_qty,
                   c.size_description, c.weight_lb
            FROM port_prices p
            LEFT JOIN goods_catalog c ON c.good_index = p.good_index
            WHERE p.snapshot_id = ? AND p.port_index = ?
            ORDER BY p.good_name
            """,
            (snapshot_id, port_index),
        ).fetchall()
    return jsonify({"snapshot_id": snapshot_id, "goods": [dict(r) for r in rows]})


def derive_exchange_fee(rates):
    for row in rates:
        rate = row["rate"]
        if rate and rate > 0:
            return max(0.0, min(1.0, 1.0 - row["rate_with_fee"] / rate))
    return 0.01


@app.get("/api/currency")
def currency():
    snapshot_id = request.args.get("snapshot_id", type=int)
    with connect() as conn:
        if snapshot_id is None:
            snapshot_id = conn.execute(
                "SELECT id FROM snapshots ORDER BY id DESC LIMIT 1"
            ).fetchone()[0]
        prices = conn.execute(
            """
            SELECT currency_index, currency_name, price_index
            FROM currency_prices
            WHERE snapshot_id = ?
            ORDER BY currency_index
            """,
            (snapshot_id,),
        ).fetchall()
        rates = conn.execute(
            """
            SELECT sell_currency, buy_currency, rate, rate_with_fee
            FROM currency_rates
            WHERE snapshot_id = ?
            ORDER BY sell_currency, buy_currency
            """,
            (snapshot_id,),
        ).fetchall()
        reputation = conn.execute(
            """
            SELECT region, region_name, reputation, rep_level, retail_discount, bulk_discount
            FROM reputation
            WHERE snapshot_id = ?
            ORDER BY region
            """,
            (snapshot_id,),
        ).fetchall()
    rates_list = [dict(r) for r in rates]
    return jsonify({
        "snapshot_id": snapshot_id,
        "prices": [dict(r) for r in prices],
        "rates": rates_list,
        "reputation": [dict(r) for r in reputation],
        "exchange_fee": derive_exchange_fee(rates_list),
    })


@app.get("/api/history")
def history():
    port_index = request.args.get("port_index", type=int)
    good_index = request.args.get("good_index", type=int)
    currency_index = request.args.get("currency_index", type=int, default=0)
    ports_raw = request.args.get("ports", "")
    current_port = request.args.get("current_port", type=int)
    if port_index is None or good_index is None:
        return jsonify({"error": "port_index and good_index required"}), 400
    if currency_index < 0 or currency_index > 3:
        return jsonify({"error": "currency_index must be 0-3"}), 400

    port_indices = parse_port_indices(ports_raw) if ports_raw else None
    if ports_raw and port_indices is None:
        return jsonify({"error": "invalid ports list"}), 400

    with connect() as conn:
        rows = conn.execute(
            """
            SELECT s.id AS snapshot_id, s.game_day, s.game_time, s.captured_at,
                   p.buy_raw, p.sell_raw, p.supply, p.available,
                   cp.price_index
            FROM port_prices p
            JOIN snapshots s ON s.id = p.snapshot_id
            JOIN currency_prices cp ON cp.snapshot_id = s.id AND cp.currency_index = ?
            WHERE p.port_index = ? AND p.good_index = ?
            ORDER BY s.game_day, s.id
            """,
            (currency_index, port_index, good_index),
        ).fetchall()
        points = [dict(r) for r in rows]

        archipelago_name = None
        if port_indices:
            archipelago_region = None
            lookup_port = current_port if current_port is not None else port_index
            region_row = conn.execute(
                "SELECT region FROM port_prices WHERE port_index = ? LIMIT 1",
                (lookup_port,),
            ).fetchone()
            if region_row is not None:
                archipelago_region = region_row[0]
                archipelago_name = REGION_NAMES.get(archipelago_region, f"Region {archipelago_region}")

            snapshot_ids = [point["snapshot_id"] for point in points]
            averages_by_snapshot = fetch_averages_by_snapshot(
                conn,
                snapshot_ids,
                good_index,
                port_indices,
                currency_index,
                archipelago_region,
            )
            for point in points:
                averages = averages_by_snapshot.get(point["snapshot_id"], {})
                point["avg_buy"] = averages.get("global_buy_avg")
                point["avg_sell"] = averages.get("global_sell_avg")
                point["arch_buy_avg"] = averages.get("arch_buy_avg")
                point["arch_sell_avg"] = averages.get("arch_sell_avg")

    return jsonify({"points": points, "archipelago_name": archipelago_name})


@app.get("/api/compare")
def compare():
    good_index = request.args.get("good_index", type=int)
    currency_index = request.args.get("currency_index", type=int, default=0)
    ports_raw = request.args.get("ports", "")
    current_port = request.args.get("current_port", type=int)
    if good_index is None:
        return jsonify({"error": "good_index required"}), 400
    if currency_index < 0 or currency_index > 3:
        return jsonify({"error": "currency_index must be 0-3"}), 400

    port_indices = parse_port_indices(ports_raw)
    if port_indices is None:
        return jsonify({"error": "invalid ports list"}), 400
    if not port_indices:
        return jsonify({"error": "ports required"}), 400

    placeholders = ",".join("?" for _ in port_indices)
    with connect() as conn:
        snapshot_id = conn.execute(
            "SELECT id FROM snapshots ORDER BY id DESC LIMIT 1"
        ).fetchone()[0]
        rows = conn.execute(
            f"""
            SELECT p.port_index, p.port_name, p.region, p.buy_raw, p.sell_raw, p.available, p.buy_qty,
                   cp.price_index
            FROM port_prices p
            JOIN currency_prices cp ON cp.snapshot_id = p.snapshot_id AND cp.currency_index = ?
            WHERE p.snapshot_id = ? AND p.good_index = ? AND p.port_index IN ({placeholders})
            ORDER BY p.port_name
            """,
            (currency_index, snapshot_id, good_index, *port_indices),
        ).fetchall()
        rates = conn.execute(
            """
            SELECT sell_currency, buy_currency, rate, rate_with_fee
            FROM currency_rates WHERE snapshot_id = ?
            """,
            (snapshot_id,),
        ).fetchall()

        good_name = conn.execute(
            "SELECT good_name FROM port_prices WHERE snapshot_id = ? AND good_index = ? LIMIT 1",
            (snapshot_id, good_index),
        ).fetchone()
        good_name = good_name[0] if good_name else f"good {good_index}"

    exchange_fee = derive_exchange_fee([dict(r) for r in rates])
    current_region = None
    if current_port is not None:
        for row in rows:
            if row["port_index"] == current_port:
                current_region = row["region"]
                break

    ports_out = rows_to_ports_out(rows, currency_index, exchange_fee)
    stats = compute_port_averages(
        [dict(row) for row in rows],
        currency_index,
        exchange_fee,
        current_region,
    )

    by_region = {}
    for port in ports_out:
        bucket = by_region.setdefault(port["region"], {"buys": [], "sells": []})
        if port["buy"] is not None:
            bucket["buys"].append(port["buy"])
        bucket["sells"].append(port["sell"])

    archipelago_stats = []
    for region in sorted(by_region):
        bucket = by_region[region]
        archipelago_stats.append({
            "region": region,
            "name": REGION_NAMES.get(region, f"Region {region}"),
            "buy_avg": avg_or_none(bucket["buys"]),
            "sell_avg": avg_or_none(bucket["sells"]),
            "buy_count": len(bucket["buys"]),
            "sell_count": len(bucket["sells"]),
        })

    global_stats = {
        "buy_avg": stats["global_buy_avg"],
        "sell_avg": stats["global_sell_avg"],
        "buy_count": stats["buy_count"],
        "sell_count": stats["sell_count"],
    }

    top_sell = sorted(ports_out, key=lambda p: p["sell"], reverse=True)[:3]
    top_buy = sorted(
        [p for p in ports_out if p["buy"] is not None],
        key=lambda p: p["buy"],
    )[:3]

    best_route = best_route_from_ports(ports_out)

    current = None
    current_region = None
    if current_port is not None:
        for port in ports_out:
            if port["port_index"] == current_port:
                current = port
                current_region = port["region"]
                break

    archipelago_for_port = None
    if current_region is not None:
        archipelago_for_port = next(
            (a for a in archipelago_stats if a["region"] == current_region),
            None,
        )

    def delta(price, average):
        if price is None or average is None:
            return None
        return price - average

    current_insight = None
    if current:
        current_insight = {
            "port_index": current["port_index"],
            "buy": current["buy"],
            "sell": current["sell"],
            "buy_vs_global": delta(current["buy"], global_stats["buy_avg"]),
            "sell_vs_global": delta(current["sell"], global_stats["sell_avg"]),
            "buy_vs_archipelago": delta(
                current["buy"],
                archipelago_for_port["buy_avg"] if archipelago_for_port else None,
            ),
            "sell_vs_archipelago": delta(
                current["sell"],
                archipelago_for_port["sell_avg"] if archipelago_for_port else None,
            ),
        }

    def mark_current(items):
        out = []
        for item in items:
            row = dict(item)
            row["is_current"] = current_port is not None and item["port_index"] == current_port
            out.append(row)
        return out

    return jsonify({
        "snapshot_id": snapshot_id,
        "good_index": good_index,
        "good_name": good_name,
        "currency_index": currency_index,
        "global": global_stats,
        "archipelagos": archipelago_stats,
        "archipelago_for_port": archipelago_for_port,
        "current": current_insight,
        "top_sell": mark_current(top_sell),
        "top_buy": mark_current(top_buy),
        "best_route": best_route,
    })


@app.get("/api/deals")
def deals():
    currency_index = request.args.get("currency_index", type=int, default=0)
    ports_raw = request.args.get("ports", "")
    limit = request.args.get("limit", type=int, default=10)
    if currency_index < 0 or currency_index > 3:
        return jsonify({"error": "currency_index must be 0-3"}), 400
    if limit < 1 or limit > 50:
        return jsonify({"error": "limit must be 1-50"}), 400

    port_indices = parse_port_indices(ports_raw)
    if port_indices is None:
        return jsonify({"error": "invalid ports list"}), 400
    if not port_indices:
        return jsonify({"error": "ports required"}), 400

    placeholders = ",".join("?" for _ in port_indices)
    with connect() as conn:
        snapshot_id = conn.execute(
            "SELECT id FROM snapshots ORDER BY id DESC LIMIT 1"
        ).fetchone()[0]
        rows = conn.execute(
            f"""
            SELECT p.good_index, p.good_name, p.port_index, p.port_name, p.region,
                   p.buy_raw, p.sell_raw, p.available, p.buy_qty, cp.price_index
            FROM port_prices p
            JOIN currency_prices cp ON cp.snapshot_id = p.snapshot_id AND cp.currency_index = ?
            WHERE p.snapshot_id = ? AND p.port_index IN ({placeholders})
            ORDER BY p.good_name, p.port_name
            """,
            (currency_index, snapshot_id, *port_indices),
        ).fetchall()
        rates = conn.execute(
            """
            SELECT sell_currency, buy_currency, rate, rate_with_fee
            FROM currency_rates WHERE snapshot_id = ?
            """,
            (snapshot_id,),
        ).fetchall()

    exchange_fee = derive_exchange_fee([dict(r) for r in rates])
    by_good = {}
    for row in rows:
        item = dict(row)
        by_good.setdefault(item["good_index"], []).append(item)

    deals_out = []
    for good_index, good_rows in by_good.items():
        ports_out = rows_to_ports_out(good_rows, currency_index, exchange_fee)
        route = best_route_from_ports(ports_out)
        if route is None:
            continue
        route["good_index"] = good_index
        route["good_name"] = good_rows[0]["good_name"]
        deals_out.append(route)

    deals_out.sort(key=lambda deal: deal["profit"], reverse=True)
    return jsonify({
        "snapshot_id": snapshot_id,
        "currency_index": currency_index,
        "deals": deals_out[:limit],
    })


@app.get("/api/market-meta")
def market_meta():
    with connect() as conn:
        globals_row = load_market_globals(conn)
        ports = conn.execute(
            """
            SELECT port_index, port_name, region, supply_purchase_limit, goods_soft_cap_override
            FROM ports_catalog ORDER BY region, port_name
            """
        ).fetchall()
        goods_sample = conn.execute(
            """
            SELECT good_index, good_name, base_value
            FROM goods_catalog
            WHERE base_value IS NOT NULL
            ORDER BY good_name
            LIMIT 20
            """
        ).fetchall()
        goods_count = conn.execute(
            "SELECT COUNT(*) AS n FROM goods_catalog WHERE base_value IS NOT NULL"
        ).fetchone()["n"]
    return jsonify({
        "market_globals": globals_row,
        "ports_count": len(ports),
        "ports": [dict(row) for row in ports],
        "goods_with_base_value": goods_count,
        "goods_sample": [dict(row) for row in goods_sample],
    })


def _sim_query_args():
    return {
        "port_index": request.args.get("port_index", type=int),
        "good_index": request.args.get("good_index", type=int),
        "quantity": request.args.get("quantity", type=int, default=16),
        "snapshot_id": request.args.get("snapshot_id", type=int),
        "supply_start": request.args.get("supply_start", type=float),
        "observed_buy_raw": request.args.get("observed_buy_raw", type=int),
        "observed_sell_raw": request.args.get("observed_sell_raw", type=int),
    }


def _run_sim(handler):
    args = _sim_query_args()
    if args["port_index"] is None or args["good_index"] is None:
        return jsonify({"error": "port_index and good_index required"}), 400
    try:
        with connect() as conn:
            result = handler(conn, **args)
    except (ValueError, TypeError) as exc:
        return jsonify({"error": str(exc)}), 400
    except sqlite3.OperationalError as exc:
        return jsonify({"error": str(exc)}), 500
    return jsonify(result)


@app.get("/api/simulate")
def simulate_api():
    mode = request.args.get("mode", "buy")
    if mode == "sell":
        return _run_sim(
            lambda conn, **kw: simulate_port_sell(
                conn,
                kw["port_index"],
                kw["good_index"],
                kw["quantity"],
                kw["snapshot_id"],
                kw["supply_start"],
                kw["observed_sell_raw"],
            )
        )
    if mode == "round_trip":
        return _run_sim(
            lambda conn, **kw: simulate_port_buy_then_sell(
                conn,
                kw["port_index"],
                kw["good_index"],
                kw["quantity"],
                kw["snapshot_id"],
                kw["observed_buy_raw"],
            )
        )
    return _run_sim(
        lambda conn, **kw: simulate_port_buy(
            conn,
            kw["port_index"],
            kw["good_index"],
            kw["quantity"],
            kw["snapshot_id"],
            kw["supply_start"],
            kw["observed_buy_raw"],
        )
    )


@app.get("/api/simulate-buy")
def simulate_buy_api():
    return _run_sim(
        lambda conn, **kw: simulate_port_buy(
            conn,
            kw["port_index"],
            kw["good_index"],
            kw["quantity"],
            kw["snapshot_id"],
            kw["supply_start"],
            kw["observed_buy_raw"],
        )
    )


@app.get("/api/simulate-sell")
def simulate_sell_api():
    return _run_sim(
        lambda conn, **kw: simulate_port_sell(
            conn,
            kw["port_index"],
            kw["good_index"],
            kw["quantity"],
            kw["snapshot_id"],
            kw["supply_start"],
            kw["observed_sell_raw"],
        )
    )


@app.get("/api/route-plan")
def route_plan_api():
    currency_index = request.args.get("currency_index", type=int, default=0)
    ports_raw = request.args.get("ports", "")
    budget_display = request.args.get("budget", type=int)
    max_weight = request.args.get("max_weight", type=float)
    max_volume = request.args.get("max_volume", type=float)
    snapshot_id = request.args.get("snapshot_id", type=int)

    if currency_index < 0 or currency_index > 3:
        return jsonify({"error": "currency_index must be 0-3"}), 400
    port_indices = parse_port_indices(ports_raw)
    if port_indices is None:
        return jsonify({"error": "invalid ports list"}), 400
    if len(port_indices) < 2:
        return jsonify({"error": "route needs at least 2 ports (ordered)"}), 400
    if budget_display is None or budget_display < 1:
        return jsonify({"error": "budget required (display currency, >= 1)"}), 400

    try:
        with connect() as conn:
            if snapshot_id is None:
                snapshot_id = conn.execute(
                    "SELECT id FROM snapshots ORDER BY id DESC LIMIT 1"
                ).fetchone()[0]
            rates = conn.execute(
                "SELECT rate, rate_with_fee FROM currency_rates WHERE snapshot_id = ?",
                (snapshot_id,),
            ).fetchall()
            exchange_fee = derive_exchange_fee([dict(r) for r in rates])
            price_index = load_price_index(conn, snapshot_id, currency_index)
            regions_on_route = {
                int(r["region"])
                for r in conn.execute(
                    f"""
                    SELECT DISTINCT region FROM port_prices
                    WHERE snapshot_id = ? AND port_index IN ({",".join("?" for _ in port_indices)})
                    """,
                    (snapshot_id, *port_indices),
                ).fetchall()
            }
            with_fee = any(needs_conversion_fee(r, currency_index) for r in regions_on_route)
            budget_raw = invert_buy_raw(budget_display, price_index, with_fee, exchange_fee)
            plan = plan_route(
                conn,
                port_indices,
                budget_raw,
                snapshot_id=snapshot_id,
                max_weight=max_weight,
                max_volume=max_volume,
            )
            port_regions = {
                int(r["port_index"]): int(r["region"])
                for r in conn.execute(
                    """
                    SELECT DISTINCT port_index, region
                    FROM port_prices WHERE snapshot_id = ?
                    """,
                    (snapshot_id,),
                ).fetchall()
            }
            plan_dict = plan_to_dict(plan, port_regions)
            out = enrich_route_plan_display(
                plan_dict, conn, snapshot_id, currency_index, exchange_fee, budget_display
            )
            out["snapshot_id"] = snapshot_id
            out["budget_raw"] = budget_raw
    except (ValueError, TypeError) as exc:
        return jsonify({"error": str(exc)}), 400
    except sqlite3.OperationalError as exc:
        return jsonify({"error": str(exc)}), 500
    return jsonify(out)


@app.get("/api/simulate-round-trip")
def simulate_round_trip_api():
    return _run_sim(
        lambda conn, **kw: simulate_port_buy_then_sell(
            conn,
            kw["port_index"],
            kw["good_index"],
            kw["quantity"],
            kw["snapshot_id"],
            kw["observed_buy_raw"],
        )
    )


if __name__ == "__main__":
    host = CFG.get("host", "127.0.0.1")
    port = int(CFG.get("port", 8765))
    print(f"Routier web UI — {DB_PATH}")
    app.run(host=host, port=port, debug=False)
