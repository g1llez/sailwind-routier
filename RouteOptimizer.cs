using System.Collections.Generic;

namespace Routier
{
    internal sealed class PortView
    {
        public int Index;
        public string Name;
        public int Region;
        public float SupplyPurchaseLimit;
        public Dictionary<int, float> Supply = new Dictionary<int, float>();
        public Dictionary<int, bool> Available = new Dictionary<int, bool>();
        public Dictionary<int, int> BuyQty = new Dictionary<int, int>();
    }

    internal sealed class GoodDef
    {
        public int Index;
        public string Name;
        public float WeightLb;
        public float VolumeCuft;
    }

    internal sealed class SellLeg
    {
        public int PortIndex;
        public string PortName;
        public int Quantity;
        public List<int> UnitPrices;
        public int TotalRevenue;
    }

    internal sealed class RouteDeal
    {
        public int GoodIndex;
        public string GoodName;
        public int BuyPortIndex;
        public string BuyPortName;
        public List<SellLeg> SellLegs;
        public int Quantity;
        public List<int> BuyUnitPrices;
        public int BuyTotal;
        public int SellTotal;
        public int Profit;
        public float WeightTotal;
        public float VolumeTotal;
    }

    internal sealed class RoutePlan
    {
        public List<int> Route;
        public List<string> RouteNames;
        public int Budget;
        public int BudgetSpent;
        public int BudgetLeft;
        public float WeightUsed;
        public float VolumeUsed;
        public List<RouteDeal> Deals = new List<RouteDeal>();
        public int TotalProfit;
    }

    /// <summary>
    /// Ordered-route optimizer, ported 1:1 from sim/route_optimizer.py (budget-only).
    /// Deals buy at an earlier stop and sell at later stop(s); sell quantity is split
    /// greedily by marginal price. Selling on arrival frees cash for the next leg.
    /// </summary>
    internal static class RouteOptimizer
    {
        private static long Key(int port, int good) => ((long)port << 32) | (uint)good;

        internal static (List<SellLeg> legs, int revenue) OptimalSellSplit(
            IList<(int Index, string Name)> sellPorts,
            int qty,
            Dictionary<int, float> supplyByPort,
            int goodIndex)
        {
            if (qty <= 0 || sellPorts.Count == 0)
                return (new List<SellLeg>(), 0);

            var startSupply = new Dictionary<int, float>();
            var liveSupply = new Dictionary<int, float>();
            var allocation = new Dictionary<int, int>();
            foreach (var p in sellPorts)
            {
                var s = supplyByPort.TryGetValue(p.Index, out var v) ? v : 0f;
                startSupply[p.Index] = s;
                liveSupply[p.Index] = s;
                allocation[p.Index] = 0;
            }

            for (var n = 0; n < qty; n++)
            {
                var bestPort = -1;
                var bestPrice = 0;
                foreach (var p in sellPorts)
                {
                    var price = RouteSim.SellPriceAtSupply(goodIndex, liveSupply[p.Index]);
                    if (bestPort < 0 || price > bestPrice)
                    {
                        bestPort = p.Index;
                        bestPrice = price;
                    }
                }
                if (bestPort < 0 || bestPrice <= 0)
                    break;
                allocation[bestPort] += 1;
                liveSupply[bestPort] += 1f;
            }

            var legs = new List<SellLeg>();
            var revenue = 0;
            foreach (var p in sellPorts)
            {
                var k = allocation[p.Index];
                if (k <= 0)
                    continue;
                var sim = RouteSim.SimulateSell(startSupply[p.Index], k, goodIndex);
                legs.Add(new SellLeg
                {
                    PortIndex = p.Index,
                    PortName = p.Name,
                    Quantity = sim.QuantitySold,
                    UnitPrices = sim.UnitPrices,
                    TotalRevenue = sim.TotalRevenue,
                });
                revenue += sim.TotalRevenue;
            }

            return (legs, revenue);
        }

        internal static RouteDeal BestDealForPair(
            (int Index, string Name) buyPort,
            IList<(int Index, string Name)> sellPorts,
            GoodDef good,
            float supplyBuy,
            float buyPurchaseLimit,
            Dictionary<int, float> sellSupplies,
            int budget,
            int maxQty)
        {
            RouteDeal best = null;

            for (var qty = 1; qty <= maxQty; qty++)
            {
                var buySim = RouteSim.SimulateBuy(supplyBuy, buyPurchaseLimit, qty, good.Index);
                if (buySim.QuantityBought < qty)
                    continue;
                if (buySim.TotalCost > budget)
                    break;

                var bought = buySim.QuantityBought;
                var (legs, sellTotal) = OptimalSellSplit(sellPorts, bought, sellSupplies, good.Index);
                var soldQty = 0;
                foreach (var leg in legs)
                    soldQty += leg.Quantity;
                if (soldQty < bought)
                    continue;

                var profit = sellTotal - buySim.TotalCost;
                if (profit <= 0)
                    continue;

                var deal = new RouteDeal
                {
                    GoodIndex = good.Index,
                    GoodName = good.Name,
                    BuyPortIndex = buyPort.Index,
                    BuyPortName = buyPort.Name,
                    SellLegs = legs,
                    Quantity = bought,
                    BuyUnitPrices = buySim.UnitPrices,
                    BuyTotal = buySim.TotalCost,
                    SellTotal = sellTotal,
                    Profit = profit,
                    WeightTotal = good.WeightLb * bought,
                    VolumeTotal = good.VolumeCuft * bought,
                };
                if (best == null || deal.Profit > best.Profit)
                    best = deal;
            }

            return best;
        }

        private sealed class OnBoardBatch
        {
            public RouteDeal Deal;
            public int QtyRemaining;
            public float WeightLb;
            public float VolumeCuft;
        }

        private static int SellAtPort(
            int portIndex,
            List<OnBoardBatch> batches,
            Dictionary<long, float> workingSupplies)
        {
            var revenue = 0;
            foreach (var batch in batches)
            {
                if (batch.QtyRemaining <= 0)
                    continue;
                var g = batch.Deal.GoodIndex;
                foreach (var leg in batch.Deal.SellLegs)
                {
                    if (leg.PortIndex != portIndex || leg.Quantity <= 0)
                        continue;
                    var supply = workingSupplies.TryGetValue(Key(portIndex, g), out var v) ? v : 0f;
                    var sim = RouteSim.SimulateSell(supply, leg.Quantity, g);
                    if (sim.QuantitySold <= 0)
                        continue;
                    revenue += sim.TotalRevenue;
                    batch.QtyRemaining -= sim.QuantitySold;
                    leg.Quantity = sim.QuantitySold;
                    leg.UnitPrices = sim.UnitPrices;
                    leg.TotalRevenue = sim.TotalRevenue;
                    workingSupplies[Key(portIndex, g)] = sim.SupplyEnd;
                }
            }
            return revenue;
        }

        private static RouteDeal BestDealAtPort(
            int buyStop,
            IList<PortView> route,
            IList<GoodDef> goods,
            Dictionary<long, float> workingSupplies,
            int cash,
            HashSet<int> onBoardGoods)
        {
            var buyPort = route[buyStop];
            var sellPorts = new List<(int, string)>();
            for (var i = buyStop + 1; i < route.Count; i++)
                sellPorts.Add((route[i].Index, route[i].Name));

            RouteDeal best = null;
            foreach (var good in goods)
            {
                if (onBoardGoods.Contains(good.Index))
                    continue;
                if (!buyPort.Available.TryGetValue(good.Index, out var avail) || !avail)
                    continue;
                var maxQty = buyPort.BuyQty.TryGetValue(good.Index, out var q) ? q : 0;
                if (maxQty <= 0)
                    continue;

                var sellSupplies = new Dictionary<int, float>();
                foreach (var sp in sellPorts)
                    sellSupplies[sp.Item1] = workingSupplies.TryGetValue(Key(sp.Item1, good.Index), out var v) ? v : 0f;

                var supplyBuy = workingSupplies.TryGetValue(Key(buyPort.Index, good.Index), out var sb) ? sb : 0f;
                var deal = BestDealForPair(
                    (buyPort.Index, buyPort.Name),
                    sellPorts,
                    good,
                    supplyBuy,
                    buyPort.SupplyPurchaseLimit,
                    sellSupplies,
                    cash,
                    maxQty);
                if (deal != null && (best == null || deal.Profit > best.Profit))
                    best = deal;
            }

            return best;
        }

        internal static RoutePlan SequentialRoutePlan(
            IList<PortView> route,
            IList<GoodDef> goods,
            int budget)
        {
            var workingSupplies = new Dictionary<long, float>();
            var limits = new Dictionary<int, float>();
            foreach (var port in route)
            {
                limits[port.Index] = port.SupplyPurchaseLimit;
                foreach (var kv in port.Supply)
                    workingSupplies[Key(port.Index, kv.Key)] = kv.Value;
            }

            var cash = budget;
            var batches = new List<OnBoardBatch>();
            var planDeals = new List<RouteDeal>();

            for (var stop = 0; stop < route.Count; stop++)
            {
                var port = route[stop];
                cash += SellAtPort(port.Index, batches, workingSupplies);

                if (stop >= route.Count - 1)
                    continue;

                var onBoardGoods = new HashSet<int>();
                foreach (var b in batches)
                    if (b.QtyRemaining > 0)
                        onBoardGoods.Add(b.Deal.GoodIndex);

                while (true)
                {
                    var deal = BestDealAtPort(stop, route, goods, workingSupplies, cash, onBoardGoods);
                    if (deal == null)
                        break;

                    var buyKey = Key(deal.BuyPortIndex, deal.GoodIndex);
                    var buySim = RouteSim.SimulateBuy(
                        workingSupplies[buyKey],
                        limits[deal.BuyPortIndex],
                        deal.Quantity,
                        deal.GoodIndex);
                    workingSupplies[buyKey] = buySim.SupplyEnd;

                    cash -= deal.BuyTotal;
                    var weightLb = deal.Quantity > 0 ? deal.WeightTotal / deal.Quantity : 0f;
                    var volumeCuft = deal.Quantity > 0 ? deal.VolumeTotal / deal.Quantity : 0f;

                    batches.Add(new OnBoardBatch
                    {
                        Deal = deal,
                        QtyRemaining = deal.Quantity,
                        WeightLb = weightLb,
                        VolumeCuft = volumeCuft,
                    });
                    planDeals.Add(deal);
                    onBoardGoods.Add(deal.GoodIndex);
                }
            }

            // Legs were re-simulated during the walk; recompute totals from actuals
            // so the reported profit matches what really happened.
            var peakWeight = 0f;
            var peakVolume = 0f;
            var runningWeight = 0f;
            var runningVolume = 0f;
            foreach (var deal in planDeals)
            {
                var sellTotal = 0;
                foreach (var leg in deal.SellLegs)
                    sellTotal += leg.TotalRevenue;
                deal.SellTotal = sellTotal;
                deal.Profit = sellTotal - deal.BuyTotal;
                runningWeight += deal.WeightTotal;
                runningVolume += deal.VolumeTotal;
                if (runningWeight > peakWeight) peakWeight = runningWeight;
                if (runningVolume > peakVolume) peakVolume = runningVolume;
            }

            var spent = 0;
            foreach (var d in planDeals)
                spent += d.BuyTotal;

            var names = new List<string>();
            var indices = new List<int>();
            foreach (var p in route)
            {
                names.Add(p.Name);
                indices.Add(p.Index);
            }

            return new RoutePlan
            {
                Route = indices,
                RouteNames = names,
                Budget = budget,
                BudgetSpent = spent,
                BudgetLeft = cash,
                WeightUsed = peakWeight,
                VolumeUsed = peakVolume,
                Deals = planDeals,
                TotalProfit = cash - budget,
            };
        }
    }
}
