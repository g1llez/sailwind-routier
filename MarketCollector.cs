using System;
using UnityEngine;

namespace Routier
{
    internal static class MarketCollector
    {
        internal static long Capture()
        {
            if (Plugin.Database == null)
                return 0;
            if (PrefabsDirectory.instance == null)
                return 0;
            if (CurrencyMarket.instance == null)
                return 0;

            GoodsCatalog.EnsureSynced();
            MarketMetadata.EnsureSynced();

            var capturedAt = DateTime.UtcNow;
            var gameDay = GameState.day;
            var gameTime = Sun.sun != null ? Mathf.Floor(Sun.sun.globalTime) : 0f;
            var rows = new System.Collections.Generic.List<PortPriceRow>();
            var goodsSeen = new System.Collections.Generic.HashSet<int>();

            foreach (var port in Port.ports)
            {
                if (port == null)
                    continue;

                var market = port.GetComponent<IslandMarket>();
                if (market == null || market.production == null)
                    continue;

                var portIndex = port.portIndex;
                var portName = port.GetPortName();
                var region = (int)port.region;

                for (var goodIndex = 0; goodIndex < market.production.Length; goodIndex++)
                {
                    var good = PrefabsDirectory.instance.GetGood(goodIndex);
                    if (good == null)
                        continue;

                    goodsSeen.Add(goodIndex);
                    var supply = market.currentSupply[goodIndex];
                    var available = market.HasGood(goodIndex) ? 1 : 0;
                    var buyQty = 0;
                    if (available == 1)
                        buyQty = Mathf.FloorToInt(Mathf.Max(0f, supply - market.supplyPurchaseLimit + 1f));
                    rows.Add(new PortPriceRow(
                        portIndex,
                        portName,
                        region,
                        goodIndex,
                        good.name,
                        market.GetBuyPrice(goodIndex),
                        market.GetSellPrice(goodIndex),
                        supply,
                        available,
                        buyQty));
                }
            }

            var currencyPrices = BuildCurrencyPrices();
            var currencyRates = BuildCurrencyRates();
            var reputations = BuildReputationRows();

            return Plugin.Database.InsertSnapshot(
                capturedAt,
                gameDay,
                gameTime,
                rows,
                currencyPrices,
                currencyRates,
                reputations);
        }

        private static CurrencyPriceRow[] BuildCurrencyPrices()
        {
            var market = CurrencyMarket.instance;
            var prices = market.currentPrices;
            var rows = new CurrencyPriceRow[prices.Length];
            for (var i = 0; i < prices.Length; i++)
                rows[i] = new CurrencyPriceRow(i, PlayerGold.GetCurrencyName(i), prices[i]);
            return rows;
        }

        private static CurrencyRateRow[] BuildCurrencyRates()
        {
            var market = CurrencyMarket.instance;
            var list = new System.Collections.Generic.List<CurrencyRateRow>();
            for (var sell = 0; sell < 4; sell++)
            {
                for (var buy = 0; buy < 4; buy++)
                {
                    if (sell == buy)
                        continue;
                    list.Add(new CurrencyRateRow(
                        sell,
                        buy,
                        market.GetExchangeRate(sell, buy, false),
                        market.GetExchangeRate(sell, buy, true)));
                }
            }
            return list.ToArray();
        }

        private static ReputationRow[] BuildReputationRows()
        {
            var rows = new ReputationRow[4];
            for (var region = 0; region < 4; region++)
            {
                rows[region] = new ReputationRow(
                    region,
                    RegionName(region),
                    PlayerReputation.GetRep(region),
                    PlayerReputation.GetRepLevel(region),
                    PlayerReputation.retailDiscounts[region],
                    PlayerReputation.bulkDiscounts[region]);
            }
            return rows;
        }

        private static string RegionName(int region)
        {
            switch (region)
            {
                case 0: return "Al'Ankh";
                case 1: return "Emerald";
                case 2: return "Aestrin";
                default: return "None";
            }
        }
    }

    internal readonly struct PortPriceRow
    {
        public readonly int PortIndex;
        public readonly string PortName;
        public readonly int Region;
        public readonly int GoodIndex;
        public readonly string GoodName;
        public readonly int BuyRaw;
        public readonly int SellRaw;
        public readonly float Supply;
        public readonly int Available;
        public readonly int BuyQty;

        public PortPriceRow(int portIndex, string portName, int region, int goodIndex, string goodName,
            int buyRaw, int sellRaw, float supply, int available, int buyQty)
        {
            PortIndex = portIndex;
            PortName = portName;
            Region = region;
            GoodIndex = goodIndex;
            GoodName = goodName;
            BuyRaw = buyRaw;
            SellRaw = sellRaw;
            Supply = supply;
            Available = available;
            BuyQty = buyQty;
        }
    }

    internal readonly struct CurrencyPriceRow
    {
        public readonly int CurrencyIndex;
        public readonly string CurrencyName;
        public readonly float PriceIndex;

        public CurrencyPriceRow(int currencyIndex, string currencyName, float priceIndex)
        {
            CurrencyIndex = currencyIndex;
            CurrencyName = currencyName;
            PriceIndex = priceIndex;
        }
    }

    internal readonly struct CurrencyRateRow
    {
        public readonly int SellCurrency;
        public readonly int BuyCurrency;
        public readonly float Rate;
        public readonly float RateWithFee;

        public CurrencyRateRow(int sellCurrency, int buyCurrency, float rate, float rateWithFee)
        {
            SellCurrency = sellCurrency;
            BuyCurrency = buyCurrency;
            Rate = rate;
            RateWithFee = rateWithFee;
        }
    }

    internal readonly struct ReputationRow
    {
        public readonly int Region;
        public readonly string RegionName;
        public readonly int Reputation;
        public readonly int RepLevel;
        public readonly float RetailDiscount;
        public readonly float BulkDiscount;

        public ReputationRow(int region, string regionName, int reputation, int repLevel,
            float retailDiscount, float bulkDiscount)
        {
            Region = region;
            RegionName = regionName;
            Reputation = reputation;
            RepLevel = repLevel;
            RetailDiscount = retailDiscount;
            BulkDiscount = bulkDiscount;
        }
    }
}
