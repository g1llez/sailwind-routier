using System.Collections.Generic;
using UnityEngine;

namespace Routier
{
    /// <summary>
    /// Faithful in-game bulk buy/sell simulation.
    ///
    /// Pricing calls the game's own <see cref="IslandMarket.GetGoodPriceAtSupply"/>,
    /// which is port-independent (global soft cap + item.value + global mults), so a
    /// single reference market prices every port. Buying lowers supply (prices rise),
    /// selling raises it (prices fall) — mirrors PurchaseGood/SellGood.
    /// </summary>
    internal static class RouteSim
    {
        /// <summary>Any live market instance; the price curve does not depend on which.</summary>
        internal static IslandMarket Pricer;

        // Price is a pure function of (good, supply) for a generation (global soft cap
        // and mults are constant), and the same supplies recur across candidate routes,
        // so memoizing avoids a main-thread hitch. Reset at the start of each generation.
        private static Dictionary<int, Dictionary<float, int>> _buyCache;
        private static Dictionary<int, Dictionary<float, int>> _sellCache;

        internal static void ResetCache()
        {
            _buyCache = new Dictionary<int, Dictionary<float, int>>();
            _sellCache = new Dictionary<int, Dictionary<float, int>>();
        }

        internal static float Spread(float rawPrice)
        {
            var t = Mathf.InverseLerp(1000f, 30000f, rawPrice);
            return Mathf.Lerp(0.005f, 0.0001f, t);
        }

        internal static int BuyPriceAtSupply(int goodIndex, float supply)
        {
            if (_buyCache == null)
                ResetCache();
            if (!_buyCache.TryGetValue(goodIndex, out var bySupply))
                _buyCache[goodIndex] = bySupply = new Dictionary<float, int>();
            if (bySupply.TryGetValue(supply, out var cached))
                return cached;

            float mid = Pricer.GetGoodPriceAtSupply(goodIndex, supply);
            var price = Mathf.RoundToInt(mid * (1f + Spread(mid)));
            bySupply[supply] = price;
            return price;
        }

        internal static int SellPriceAtSupply(int goodIndex, float supply)
        {
            if (_sellCache == null)
                ResetCache();
            if (!_sellCache.TryGetValue(goodIndex, out var bySupply))
                _sellCache[goodIndex] = bySupply = new Dictionary<float, int>();
            if (bySupply.TryGetValue(supply, out var cached))
                return cached;

            float mid = Pricer.GetGoodPriceAtSupply(goodIndex, supply + 1f);
            var price = Mathf.RoundToInt(mid * (1f - Spread(mid)));
            bySupply[supply] = price;
            return price;
        }

        internal sealed class BuySim
        {
            public List<int> UnitPrices;
            public int TotalCost;
            public int QuantityBought;
            public float SupplyEnd;
        }

        internal sealed class SellSim
        {
            public List<int> UnitPrices;
            public int TotalRevenue;
            public int QuantitySold;
            public float SupplyEnd;
        }

        internal static BuySim SimulateBuy(float supplyStart, float purchaseLimit, int quantity, int goodIndex)
        {
            var prices = new List<int>();
            var cost = 0;
            var supply = supplyStart;
            for (var i = 0; i < quantity; i++)
            {
                if (supply < purchaseLimit) // HasGood gate
                    break;
                var price = BuyPriceAtSupply(goodIndex, supply);
                prices.Add(price);
                cost += price;
                supply -= 1f;
            }

            return new BuySim
            {
                UnitPrices = prices,
                TotalCost = cost,
                QuantityBought = prices.Count,
                SupplyEnd = supply,
            };
        }

        internal static SellSim SimulateSell(float supplyStart, int quantity, int goodIndex)
        {
            var prices = new List<int>();
            var revenue = 0;
            var supply = supplyStart;
            for (var i = 0; i < quantity; i++)
            {
                var price = SellPriceAtSupply(goodIndex, supply);
                prices.Add(price);
                revenue += price;
                supply += 1f;
            }

            return new SellSim
            {
                UnitPrices = prices,
                TotalRevenue = revenue,
                QuantitySold = prices.Count,
                SupplyEnd = supply,
            };
        }
    }
}
