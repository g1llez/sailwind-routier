using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;

namespace Routier
{
    internal sealed class RoutierDatabase
    {
        private readonly string _connectionString;
        private bool _legacyBudgetSpentColumn;

        public RoutierDatabase(string dbPath)
        {
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            _connectionString = $"Data Source={dbPath};Version=3;";
        }

        public void Initialize()
        {
            using (var connection = Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
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
    buy_qty INTEGER,
    FOREIGN KEY (snapshot_id) REFERENCES snapshots(id)
);

CREATE INDEX IF NOT EXISTS idx_port_prices_snapshot ON port_prices(snapshot_id);
CREATE INDEX IF NOT EXISTS idx_port_prices_lookup ON port_prices(port_index, good_index, snapshot_id);

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

CREATE TABLE IF NOT EXISTS generated_routes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    snapshot_id INTEGER NOT NULL,
    game_day INTEGER NOT NULL,
    hub_port_index INTEGER NOT NULL,
    hub_port_name TEXT NOT NULL,
    hub_region INTEGER NOT NULL,
    kind TEXT NOT NULL,
    route_csv TEXT NOT NULL,
    route_names TEXT NOT NULL,
    hops INTEGER NOT NULL,
    budget INTEGER NOT NULL,
    capital_initial INTEGER NOT NULL,
    gross_purchases INTEGER NOT NULL,
    profit INTEGER NOT NULL,
    roi REAL NOT NULL,
    price INTEGER NOT NULL,
    rep_level INTEGER NOT NULL,
    tier TEXT NOT NULL,
    summary TEXT,
    FOREIGN KEY (snapshot_id) REFERENCES snapshots(id)
);

CREATE INDEX IF NOT EXISTS idx_generated_routes_snapshot ON generated_routes(snapshot_id);
";
                command.ExecuteNonQuery();
                EnsureColumn(connection, "snapshots", "game_time", "REAL");
                EnsureColumn(connection, "port_prices", "buy_qty", "INTEGER");
                EnsureColumn(connection, "generated_routes", "capital_initial", "INTEGER");
                EnsureColumn(connection, "generated_routes", "gross_purchases", "INTEGER");
                EnsureColumn(connection, "goods_catalog", "base_value", "REAL");
                MigrateGeneratedRoutes(connection);
            }
        }

        private void MigrateGeneratedRoutes(SQLiteConnection connection)
        {
            _legacyBudgetSpentColumn = HasColumn(connection, "generated_routes", "budget_spent");
            using (var command = connection.CreateCommand())
            {
                if (_legacyBudgetSpentColumn)
                {
                    command.CommandText =
                        "UPDATE generated_routes SET gross_purchases = budget_spent WHERE gross_purchases IS NULL";
                    command.ExecuteNonQuery();
                }
                if (HasColumn(connection, "generated_routes", "capital_initial"))
                {
                    command.CommandText =
                        "UPDATE generated_routes SET capital_initial = budget WHERE capital_initial IS NULL";
                    command.ExecuteNonQuery();
                }
            }
        }

        private static bool HasColumn(SQLiteConnection connection, string table, string column)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA table_info({table})";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            return false;
        }

        public void UpsertMarketGlobals(MarketGlobalsRow row)
        {
            using (var connection = Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
INSERT INTO market_globals (id, goods_soft_cap, positive_price_mult, negative_price_mult, updated_at)
VALUES (1, $soft_cap, $pos_mult, $neg_mult, $updated_at)
ON CONFLICT(id) DO UPDATE SET
    goods_soft_cap = excluded.goods_soft_cap,
    positive_price_mult = excluded.positive_price_mult,
    negative_price_mult = excluded.negative_price_mult,
    updated_at = excluded.updated_at;";
                command.Parameters.AddWithValue("$soft_cap", row.GoodsSoftCap);
                command.Parameters.AddWithValue("$pos_mult", row.PositivePriceMult);
                command.Parameters.AddWithValue("$neg_mult", row.NegativePriceMult);
                command.Parameters.AddWithValue("$updated_at", DateTime.UtcNow.ToString("o"));
                command.ExecuteNonQuery();
            }
        }

        public void UpsertPortsCatalog(IReadOnlyList<PortCatalogRow> rows)
        {
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
INSERT INTO ports_catalog (
    port_index, port_name, region, supply_purchase_limit, goods_soft_cap_override
) VALUES (
    $port_index, $port_name, $region, $supply_limit, $soft_cap_override
)
ON CONFLICT(port_index) DO UPDATE SET
    port_name = excluded.port_name,
    region = excluded.region,
    supply_purchase_limit = excluded.supply_purchase_limit,
    goods_soft_cap_override = excluded.goods_soft_cap_override;";

                    command.Parameters.Add("$port_index", DbType.Int32);
                    command.Parameters.Add("$port_name", DbType.String);
                    command.Parameters.Add("$region", DbType.Int32);
                    command.Parameters.Add("$supply_limit", DbType.Double);
                    command.Parameters.Add("$soft_cap_override", DbType.Double);

                    foreach (var row in rows)
                    {
                        command.Parameters["$port_index"].Value = row.PortIndex;
                        command.Parameters["$port_name"].Value = row.PortName;
                        command.Parameters["$region"].Value = row.Region;
                        command.Parameters["$supply_limit"].Value = row.SupplyPurchaseLimit;
                        command.Parameters["$soft_cap_override"].Value = row.GoodsSoftCapOverride;
                        command.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }
        }

        public void UpsertGoodsCatalog(IReadOnlyList<GoodCatalogRow> rows)
        {
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
INSERT INTO goods_catalog (good_index, good_name, size_description, weight_lb, base_value)
VALUES ($good_index, $good_name, $size_description, $weight_lb, $base_value)
ON CONFLICT(good_index) DO UPDATE SET
    good_name = excluded.good_name,
    size_description = excluded.size_description,
    weight_lb = excluded.weight_lb,
    base_value = excluded.base_value;";

                    command.Parameters.Add("$good_index", DbType.Int32);
                    command.Parameters.Add("$good_name", DbType.String);
                    command.Parameters.Add("$size_description", DbType.String);
                    command.Parameters.Add("$weight_lb", DbType.Double);
                    command.Parameters.Add("$base_value", DbType.Double);

                    foreach (var row in rows)
                    {
                        command.Parameters["$good_index"].Value = row.GoodIndex;
                        command.Parameters["$good_name"].Value = row.GoodName;
                        command.Parameters["$size_description"].Value = row.SizeDescription;
                        command.Parameters["$weight_lb"].Value = row.WeightLb;
                        command.Parameters["$base_value"].Value = row.BaseValue;
                        command.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }
        }

        private static void EnsureColumn(SQLiteConnection connection, string table, string column, string type)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA table_info({table})";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                            return;
                    }
                }
            }

            using (var alter = connection.CreateCommand())
            {
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
                alter.ExecuteNonQuery();
            }
        }

        public long InsertSnapshot(
            DateTime capturedAtUtc,
            int gameDay,
            float gameTime,
            IReadOnlyList<PortPriceRow> prices,
            IReadOnlyList<CurrencyPriceRow> currencyPrices,
            IReadOnlyList<CurrencyRateRow> currencyRates,
            IReadOnlyList<ReputationRow> reputations)
        {
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                long snapshotId;
                using (var insertSnapshot = connection.CreateCommand())
                {
                    insertSnapshot.Transaction = transaction;
                    insertSnapshot.CommandText =
                        "INSERT INTO snapshots (captured_at, game_day, game_time) VALUES ($at, $day, $time); SELECT last_insert_rowid();";
                    insertSnapshot.Parameters.AddWithValue("$at", capturedAtUtc.ToString("o"));
                    insertSnapshot.Parameters.AddWithValue("$day", gameDay);
                    insertSnapshot.Parameters.AddWithValue("$time", gameTime);
                    snapshotId = (long)insertSnapshot.ExecuteScalar();
                }

                InsertPortPrices(connection, transaction, snapshotId, prices);
                InsertCurrencyPrices(connection, transaction, snapshotId, currencyPrices);
                InsertCurrencyRates(connection, transaction, snapshotId, currencyRates);
                InsertReputation(connection, transaction, snapshotId, reputations);

                transaction.Commit();
                return snapshotId;
            }
        }

        private static void InsertPortPrices(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long snapshotId,
            IReadOnlyList<PortPriceRow> prices)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO port_prices (
    snapshot_id, port_index, port_name, region, good_index, good_name,
    buy_raw, sell_raw, supply, available, buy_qty
) VALUES (
    $snapshot_id, $port_index, $port_name, $region, $good_index, $good_name,
    $buy_raw, $sell_raw, $supply, $available, $buy_qty
);";

                command.Parameters.Add("$snapshot_id", DbType.Int64);
                command.Parameters.Add("$port_index", DbType.Int32);
                command.Parameters.Add("$port_name", DbType.String);
                command.Parameters.Add("$region", DbType.Int32);
                command.Parameters.Add("$good_index", DbType.Int32);
                command.Parameters.Add("$good_name", DbType.String);
                command.Parameters.Add("$buy_raw", DbType.Int32);
                command.Parameters.Add("$sell_raw", DbType.Int32);
                command.Parameters.Add("$supply", DbType.Double);
                command.Parameters.Add("$available", DbType.Int32);
                command.Parameters.Add("$buy_qty", DbType.Int32);

                command.Parameters["$snapshot_id"].Value = snapshotId;
                foreach (var row in prices)
                {
                    command.Parameters["$port_index"].Value = row.PortIndex;
                    command.Parameters["$port_name"].Value = row.PortName;
                    command.Parameters["$region"].Value = row.Region;
                    command.Parameters["$good_index"].Value = row.GoodIndex;
                    command.Parameters["$good_name"].Value = row.GoodName;
                    command.Parameters["$buy_raw"].Value = row.BuyRaw;
                    command.Parameters["$sell_raw"].Value = row.SellRaw;
                    command.Parameters["$supply"].Value = row.Supply;
                    command.Parameters["$available"].Value = row.Available;
                    command.Parameters["$buy_qty"].Value = row.BuyQty;
                    command.ExecuteNonQuery();
                }
            }
        }

        private static void InsertCurrencyPrices(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long snapshotId,
            IReadOnlyList<CurrencyPriceRow> rows)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO currency_prices (snapshot_id, currency_index, currency_name, price_index)
VALUES ($snapshot_id, $currency_index, $currency_name, $price_index);";

                command.Parameters.Add("$snapshot_id", DbType.Int64);
                command.Parameters.Add("$currency_index", DbType.Int32);
                command.Parameters.Add("$currency_name", DbType.String);
                command.Parameters.Add("$price_index", DbType.Double);
                command.Parameters["$snapshot_id"].Value = snapshotId;

                foreach (var row in rows)
                {
                    command.Parameters["$currency_index"].Value = row.CurrencyIndex;
                    command.Parameters["$currency_name"].Value = row.CurrencyName;
                    command.Parameters["$price_index"].Value = row.PriceIndex;
                    command.ExecuteNonQuery();
                }
            }
        }

        private static void InsertCurrencyRates(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long snapshotId,
            IReadOnlyList<CurrencyRateRow> rows)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO currency_rates (snapshot_id, sell_currency, buy_currency, rate, rate_with_fee)
VALUES ($snapshot_id, $sell_currency, $buy_currency, $rate, $rate_with_fee);";

                command.Parameters.Add("$snapshot_id", DbType.Int64);
                command.Parameters.Add("$sell_currency", DbType.Int32);
                command.Parameters.Add("$buy_currency", DbType.Int32);
                command.Parameters.Add("$rate", DbType.Double);
                command.Parameters.Add("$rate_with_fee", DbType.Double);
                command.Parameters["$snapshot_id"].Value = snapshotId;

                foreach (var row in rows)
                {
                    command.Parameters["$sell_currency"].Value = row.SellCurrency;
                    command.Parameters["$buy_currency"].Value = row.BuyCurrency;
                    command.Parameters["$rate"].Value = row.Rate;
                    command.Parameters["$rate_with_fee"].Value = row.RateWithFee;
                    command.ExecuteNonQuery();
                }
            }
        }

        private static void InsertReputation(
            SQLiteConnection connection,
            SQLiteTransaction transaction,
            long snapshotId,
            IReadOnlyList<ReputationRow> rows)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO reputation (
    snapshot_id, region, region_name, reputation, rep_level, retail_discount, bulk_discount
) VALUES (
    $snapshot_id, $region, $region_name, $reputation, $rep_level, $retail_discount, $bulk_discount
);";

                command.Parameters.Add("$snapshot_id", DbType.Int64);
                command.Parameters.Add("$region", DbType.Int32);
                command.Parameters.Add("$region_name", DbType.String);
                command.Parameters.Add("$reputation", DbType.Int32);
                command.Parameters.Add("$rep_level", DbType.Int32);
                command.Parameters.Add("$retail_discount", DbType.Double);
                command.Parameters.Add("$bulk_discount", DbType.Double);
                command.Parameters["$snapshot_id"].Value = snapshotId;

                foreach (var row in rows)
                {
                    command.Parameters["$region"].Value = row.Region;
                    command.Parameters["$region_name"].Value = row.RegionName;
                    command.Parameters["$reputation"].Value = row.Reputation;
                    command.Parameters["$rep_level"].Value = row.RepLevel;
                    command.Parameters["$retail_discount"].Value = row.RetailDiscount;
                    command.Parameters["$bulk_discount"].Value = row.BulkDiscount;
                    command.ExecuteNonQuery();
                }
            }
        }

        public void InsertGeneratedRoutes(IReadOnlyList<GeneratedRouteRow> rows)
        {
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction())
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    var legacySpent = _legacyBudgetSpentColumn || HasColumn(connection, "generated_routes", "budget_spent");
                    command.CommandText = legacySpent
                        ? @"
INSERT INTO generated_routes (
    snapshot_id, game_day, hub_port_index, hub_port_name, hub_region, kind,
    route_csv, route_names, hops, budget, capital_initial, budget_spent, gross_purchases,
    profit, roi, price, rep_level, tier, summary
) VALUES (
    $snapshot_id, $game_day, $hub_port_index, $hub_port_name, $hub_region, $kind,
    $route_csv, $route_names, $hops, $budget, $capital_initial, $budget_spent, $gross_purchases,
    $profit, $roi, $price, $rep_level, $tier, $summary
);"
                        : @"
INSERT INTO generated_routes (
    snapshot_id, game_day, hub_port_index, hub_port_name, hub_region, kind,
    route_csv, route_names, hops, budget, capital_initial, gross_purchases, profit, roi, price,
    rep_level, tier, summary
) VALUES (
    $snapshot_id, $game_day, $hub_port_index, $hub_port_name, $hub_region, $kind,
    $route_csv, $route_names, $hops, $budget, $capital_initial, $gross_purchases, $profit, $roi, $price,
    $rep_level, $tier, $summary
);";

                    command.Parameters.Add("$snapshot_id", DbType.Int64);
                    command.Parameters.Add("$game_day", DbType.Int32);
                    command.Parameters.Add("$hub_port_index", DbType.Int32);
                    command.Parameters.Add("$hub_port_name", DbType.String);
                    command.Parameters.Add("$hub_region", DbType.Int32);
                    command.Parameters.Add("$kind", DbType.String);
                    command.Parameters.Add("$route_csv", DbType.String);
                    command.Parameters.Add("$route_names", DbType.String);
                    command.Parameters.Add("$hops", DbType.Int32);
                    command.Parameters.Add("$budget", DbType.Int32);
                    command.Parameters.Add("$capital_initial", DbType.Int32);
                    command.Parameters.Add("$gross_purchases", DbType.Int32);
                    if (legacySpent)
                        command.Parameters.Add("$budget_spent", DbType.Int32);
                    command.Parameters.Add("$profit", DbType.Int32);
                    command.Parameters.Add("$roi", DbType.Double);
                    command.Parameters.Add("$price", DbType.Int32);
                    command.Parameters.Add("$rep_level", DbType.Int32);
                    command.Parameters.Add("$tier", DbType.String);
                    command.Parameters.Add("$summary", DbType.String);

                    foreach (var row in rows)
                    {
                        command.Parameters["$snapshot_id"].Value = row.SnapshotId;
                        command.Parameters["$game_day"].Value = row.GameDay;
                        command.Parameters["$hub_port_index"].Value = row.HubPortIndex;
                        command.Parameters["$hub_port_name"].Value = row.HubPortName;
                        command.Parameters["$hub_region"].Value = row.HubRegion;
                        command.Parameters["$kind"].Value = row.Kind;
                        command.Parameters["$route_csv"].Value = row.RouteCsv;
                        command.Parameters["$route_names"].Value = row.RouteNames;
                        command.Parameters["$hops"].Value = row.Hops;
                        command.Parameters["$budget"].Value = row.Budget;
                        command.Parameters["$capital_initial"].Value = row.CapitalInitial;
                        command.Parameters["$gross_purchases"].Value = row.GrossPurchases;
                        if (legacySpent)
                            command.Parameters["$budget_spent"].Value = row.GrossPurchases;
                        command.Parameters["$profit"].Value = row.Profit;
                        command.Parameters["$roi"].Value = row.Roi;
                        command.Parameters["$price"].Value = row.Price;
                        command.Parameters["$rep_level"].Value = row.RepLevel;
                        command.Parameters["$tier"].Value = row.Tier;
                        command.Parameters["$summary"].Value = (object)row.Summary ?? DBNull.Value;
                        command.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }
        }

        private SQLiteConnection Open()
        {
            var connection = new SQLiteConnection(_connectionString);
            connection.Open();
            return connection;
        }
    }
}
