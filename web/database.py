from pathlib import Path
from typing import Optional

SCHEMA = """
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;

CREATE TABLE IF NOT EXISTS snapshots (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    captured_at TEXT NOT NULL,
    game_day INTEGER NOT NULL,
    game_time REAL
);

CREATE TABLE IF NOT EXISTS port_prices (
    snapshot_id INTEGER NOT NULL,
    port_index INTEGER NOT NULL,
    port_name TEXT NOT NULL,
    region INTEGER NOT NULL,
    good_index INTEGER NOT NULL,
    good_name TEXT NOT NULL,
    buy_raw INTEGER NOT NULL,
    sell_raw INTEGER NOT NULL,
    supply REAL NOT NULL,
    available INTEGER NOT NULL,
    FOREIGN KEY (snapshot_id) REFERENCES snapshots(id)
);

CREATE INDEX IF NOT EXISTS idx_port_prices_snapshot ON port_prices(snapshot_id);
CREATE INDEX IF NOT EXISTS idx_port_prices_lookup ON port_prices(port_index, good_index, snapshot_id);

CREATE TABLE IF NOT EXISTS currency_prices (
    snapshot_id INTEGER NOT NULL,
    currency_index INTEGER NOT NULL,
    currency_name TEXT NOT NULL,
    price_index REAL NOT NULL,
    PRIMARY KEY (snapshot_id, currency_index),
    FOREIGN KEY (snapshot_id) REFERENCES snapshots(id)
);

CREATE TABLE IF NOT EXISTS currency_rates (
    snapshot_id INTEGER NOT NULL,
    sell_currency INTEGER NOT NULL,
    buy_currency INTEGER NOT NULL,
    rate REAL NOT NULL,
    rate_with_fee REAL NOT NULL,
    PRIMARY KEY (snapshot_id, sell_currency, buy_currency),
    FOREIGN KEY (snapshot_id) REFERENCES snapshots(id)
);

CREATE TABLE IF NOT EXISTS reputation (
    snapshot_id INTEGER NOT NULL,
    region INTEGER NOT NULL,
    region_name TEXT NOT NULL,
    reputation INTEGER NOT NULL,
    rep_level INTEGER NOT NULL,
    retail_discount REAL NOT NULL,
    bulk_discount REAL NOT NULL,
    PRIMARY KEY (snapshot_id, region),
    FOREIGN KEY (snapshot_id) REFERENCES snapshots(id)
);
"""


def migrate(conn) -> None:
    columns = {
        row[1]
        for row in conn.execute("PRAGMA table_info(snapshots)").fetchall()
    }
    if "game_time" not in columns:
        conn.execute("ALTER TABLE snapshots ADD COLUMN game_time REAL")

    port_columns = {
        row[1]
        for row in conn.execute("PRAGMA table_info(port_prices)").fetchall()
    }
    if "buy_qty" not in port_columns:
        conn.execute("ALTER TABLE port_prices ADD COLUMN buy_qty INTEGER")

    conn.executescript("""
CREATE TABLE IF NOT EXISTS goods_catalog (
    good_index INTEGER PRIMARY KEY,
    good_name TEXT NOT NULL,
    size_description TEXT,
    weight_lb REAL,
    base_value REAL
);

CREATE TABLE IF NOT EXISTS market_globals (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    goods_soft_cap REAL NOT NULL,
    positive_price_mult REAL NOT NULL,
    negative_price_mult REAL NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ports_catalog (
    port_index INTEGER PRIMARY KEY,
    port_name TEXT NOT NULL,
    region INTEGER NOT NULL,
    supply_purchase_limit REAL NOT NULL,
    goods_soft_cap_override REAL NOT NULL
);
""")

    goods_columns = {
        row[1]
        for row in conn.execute("PRAGMA table_info(goods_catalog)").fetchall()
    }
    if goods_columns and "base_value" not in goods_columns:
        conn.execute("ALTER TABLE goods_catalog ADD COLUMN base_value REAL")


def ensure_database(db_path: Path) -> Path:
    db_path = Path(db_path)
    db_path.parent.mkdir(parents=True, exist_ok=True)
    import sqlite3

    conn = sqlite3.connect(db_path)
    try:
        conn.executescript(SCHEMA)
        migrate(conn)
        conn.commit()
    finally:
        conn.close()
    return db_path


def latest_snapshot_id(conn) -> Optional[int]:
    row = conn.execute(
        "SELECT id FROM snapshots ORDER BY id DESC LIMIT 1"
    ).fetchone()
    return None if row is None else int(row[0])
