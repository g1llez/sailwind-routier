using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Routier
{
    internal struct GenerationConfig
    {
        public int LocalCount;
        public int RegionalCount;
        public float RoiFloor;
        public int BudgetMin;
        public int BudgetMax;
        public int HopsMin;
        public int HopsMax;
        public int SamplesPerList;
    }

    internal sealed class GeneratedRouteRow
    {
        public long SnapshotId;
        public int GameDay;
        public int HubPortIndex;
        public string HubPortName;
        public int HubRegion;
        public string Kind;
        public string RouteCsv;
        public string RouteNames;
        public int Hops;
        public int Budget;
        public int CapitalInitial;
        public int GrossPurchases;
        public int Profit;
        public float Roi;
        public int DisplayProfit;
        public int Price;
        public int RepLevel;
        public string Tier;
        public string Summary;
        public RouteOffer Offer;
    }

    /// <summary>
    /// Daily route generator: builds live market views, samples local + regional
    /// routes per hub, runs the optimizer, prices the parchment (% of profit scaled
    /// by reputation), logs everything and persists for the parity harness.
    /// </summary>
    internal static class RouteGenerator
    {
        private const string RunSeedKey = "Routier.RunSeed";
        private static int? _runSeed;

        /// <summary>
        /// Per-save random seed, minted once from real entropy and persisted in
        /// GameState.modData. Without this, the route RNG below was seeded purely from
        /// GameState.day + portIndex, which is identical on day 1 of every new game —
        /// so every fresh playthrough rolled the exact same "random" routes. Mixing in
        /// this per-save seed keeps a given save reproducible day-to-day while making
        /// different saves/playthroughs actually differ.
        /// </summary>
        private static int GetRunSeed()
        {
            if (_runSeed.HasValue)
                return _runSeed.Value;

            if (GameState.modData != null &&
                GameState.modData.TryGetValue(RunSeedKey, out var raw) &&
                int.TryParse(raw, out var stored))
            {
                _runSeed = stored;
                return stored;
            }

            var minted = unchecked((int)DateTime.Now.Ticks) ^ Guid.NewGuid().GetHashCode();
            _runSeed = minted;
            if (GameState.modData != null)
                GameState.modData[RunSeedKey] = minted.ToString();
            return minted;
        }

        internal static void ReloadRunSeed()
        {
            _runSeed = null;
        }

        internal static void Generate(long snapshotId, GenerationConfig cfg)
        {
            if (PrefabsDirectory.instance == null || Port.ports == null)
                return;

            var portsByIndex = new Dictionary<int, Port>();
            IslandMarket pricer = null;
            foreach (var port in Port.ports)
            {
                if (port == null)
                    continue;
                var market = port.GetComponent<IslandMarket>();
                if (market == null || market.production == null)
                    continue;
                portsByIndex[port.portIndex] = port;
                if (pricer == null)
                    pricer = market;
            }
            if (pricer == null)
                return;

            RouteSim.Pricer = pricer;
            RouteSim.ResetCache();

            var goods = BuildGoods();
            var views = BuildPortViews(portsByIndex, goods);

            var rows = new List<GeneratedRouteRow>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            DailyRouteCatalog.Reset();

            foreach (var kv in portsByIndex)
            {
                var hub = kv.Value;
                if (!hub.hubPort)
                    continue;
                if (!views.TryGetValue(hub.portIndex, out var hubView))
                    continue;

                var playerRep = PlayerReputation.GetRepLevel(hubView.Region);
                var rng = new System.Random(GetRunSeed() + GameState.day * 1000 + hub.portIndex);

                var local = GenerateForHub(
                    hub, hubView, views, portsByIndex, goods, cfg, rng, playerRep,
                    "local", false, snapshotId);
                var regional = GenerateForHub(
                    hub, hubView, views, portsByIndex, goods, cfg, rng, playerRep,
                    "regional", true, snapshotId);
                rows.AddRange(local);
                rows.AddRange(regional);
            }

            sw.Stop();

            if (Plugin.Database != null && rows.Count > 0)
            {
                try
                {
                    Plugin.Database.InsertGeneratedRoutes(rows);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Routier failed to save generated routes: {ex}");
                }
            }

            LogRoutes(rows, snapshotId, sw.ElapsedMilliseconds);
        }

        private static List<GeneratedRouteRow> GenerateForHub(
            Port hub,
            PortView hubView,
            Dictionary<int, PortView> views,
            Dictionary<int, Port> portsByIndex,
            List<GoodDef> goods,
            GenerationConfig cfg,
            System.Random rng,
            int playerRepLevel,
            string kind,
            bool crossRegion,
            long snapshotId)
        {
            var pool = BuildPool(hub, portsByIndex, crossRegion, playerRepLevel);
            var slotCount = crossRegion ? cfg.RegionalCount : cfg.LocalCount;
            var result = new List<GeneratedRouteRow>();
            if (pool.Count < 2)
                return result;

            var cut = Mathf.Clamp(0.35f - 0.05f * playerRepLevel, 0.10f, 0.35f);
            var seen = new HashSet<string>();

            for (var slot = 0; slot < slotCount; slot++)
            {
                var routeTier = crossRegion
                    ? RouteTierTable.RollRegionalTier(rng)
                    : RouteTierTable.RollLocalTier(rng);
                var limits = RouteTierTable.ForRouteTier(routeTier, cfg);
                if (pool.Count < limits.HopsMin - 1)
                    continue;

                GeneratedRouteRow best = null;

                for (var attempt = 0; attempt < cfg.SamplesPerList; attempt++)
                {
                    var hops = limits.HopsMin == limits.HopsMax
                        ? limits.HopsMin
                        : rng.Next(limits.HopsMin, limits.HopsMax + 1);
                    var routeViews = SampleRoute(hub, hubView, pool, views, hops, rng, crossRegion);
                    if (routeViews == null)
                        continue;

                    var sig = RouteSignature(routeViews);
                    if (!seen.Add(sig))
                        continue;

                    var budget = limits.SampleBudget(rng, cfg);
                    if (budget <= 0)
                        continue;

                    RoutePlan plan;
                    try
                    {
                        plan = RouteOptimizer.SequentialRoutePlan(
                            routeViews, goods, budget, limits.MaxWeightLb, limits.MaxVolumeCuft);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"Routier route plan failed: {ex.Message}");
                        continue;
                    }

                    if (plan.BudgetSpent <= 0 || plan.TotalProfit <= 0)
                        continue;
                    if (limits.MaxWeightLb.HasValue && plan.WeightUsed > limits.MaxWeightLb.Value + 0.01f)
                        continue;
                    if (limits.MaxVolumeCuft.HasValue && plan.VolumeUsed > limits.MaxVolumeCuft.Value + 0.01f)
                        continue;

                    var roi = (float)plan.TotalProfit / budget;
                    if (roi < cfg.RoiFloor)
                        continue;

                    var row = BuildRow(
                        plan, hub, hubView, kind, budget, roi, cut, playerRepLevel, routeTier, snapshotId);
                    if (best == null || row.Profit > best.Profit)
                        best = row;
                }

                if (best == null)
                    continue;

                result.Add(best);
                DailyRouteCatalog.Add(best.Offer);
            }

            return result;
        }

        private static GeneratedRouteRow BuildRow(
            RoutePlan plan,
            Port hub,
            PortView hubView,
            string kind,
            int budget,
            float roi,
            float cut,
            int playerRepLevel,
            int routeTier,
            long snapshotId)
        {
            var displayProfit = 0;
            foreach (var d in plan.Deals)
                displayProfit += RouteDisplay.DealProfit(d, hubView.Region);
            var displayPrice = Mathf.RoundToInt(displayProfit * cut);

            var offer = new RouteOffer
            {
                SnapshotId = snapshotId,
                GameDay = GameState.day,
                HubPortIndex = hub.portIndex,
                HubPortName = hubView.Name,
                HubRegion = hubView.Region,
                Kind = kind,
                Plan = plan,
                CapitalInitial = budget,
                GrossPurchases = plan.BudgetSpent,
                Profit = plan.TotalProfit,
                DisplayProfit = displayProfit,
                Roi = roi,
                Price = displayPrice,
                RepLevel = playerRepLevel,
                RouteTier = routeTier,
                Tier = Tier(roi),
                TotalDistanceNm = PortMapDistance.RouteNm(plan.Route),
            };
            offer.Pages = RouteParchmentBuilder.BuildPageModels(offer);

            return new GeneratedRouteRow
            {
                SnapshotId = snapshotId,
                GameDay = GameState.day,
                HubPortIndex = hub.portIndex,
                HubPortName = hubView.Name,
                HubRegion = hubView.Region,
                Kind = kind,
                RouteCsv = string.Join(",", plan.Route.ConvertAll(x => x.ToString()).ToArray()),
                RouteNames = string.Join(" -> ", plan.RouteNames.ToArray()),
                Hops = plan.Route.Count,
                Budget = budget,
                CapitalInitial = budget,
                GrossPurchases = plan.BudgetSpent,
                Profit = plan.TotalProfit,
                DisplayProfit = displayProfit,
                Roi = roi,
                Price = displayPrice,
                RepLevel = playerRepLevel,
                Tier = Tier(roi),
                Summary = BuildSummary(plan, hubView.Region),
                Offer = offer,
            };
        }

        private static string Tier(float roi)
        {
            if (roi >= 1.5f) return "****";
            if (roi >= 1.0f) return "***";
            if (roi >= 0.6f) return "**";
            return "*";
        }

        private static string BuildSummary(RoutePlan plan, int hubRegion)
        {
            var sb = new StringBuilder();
            foreach (var deal in plan.Deals)
            {
                if (sb.Length > 0)
                    sb.Append(" | ");
                var legs = new List<string>();
                foreach (var leg in deal.SellLegs)
                    legs.Add($"{leg.Quantity}@{leg.PortName}");
                var buyD = RouteDisplay.DealBuyTotal(deal, hubRegion);
                var sellD = RouteDisplay.DealSellTotal(deal, hubRegion);
                var profitD = sellD - buyD;
                sb.Append(
                    $"{deal.Quantity} {deal.GoodName} @{deal.BuyPortName} -> {string.Join(", ", legs.ToArray())} " +
                    $"| buy {buyD} sell {sellD} (+{profitD})");
            }
            return sb.ToString();
        }

        private static string BuildDealDetail(RouteDeal deal, int hubRegion)
        {
            var buyD = RouteDisplay.DealBuyTotal(deal, hubRegion);
            var sellD = RouteDisplay.DealSellTotal(deal, hubRegion);
            var profitD = sellD - buyD;
            var legs = new List<string>();
            foreach (var leg in deal.SellLegs)
            {
                var sellRegion = RouteDisplay.PortRegion(leg.PortIndex);
                var legDisplay = 0;
                if (leg.UnitPrices != null)
                {
                    foreach (var raw in leg.UnitPrices)
                        legDisplay += RouteDisplay.Sell(raw, sellRegion, hubRegion);
                }
                else
                {
                    legDisplay = RouteDisplay.Sell(leg.TotalRevenue, sellRegion, hubRegion);
                }
                legs.Add($"{leg.Quantity}@{leg.PortName} sell {legDisplay}");
            }
            var buyRegion = RouteDisplay.PortRegion(deal.BuyPortIndex);
            var unitBuys = new List<string>();
            if (deal.BuyUnitPrices != null)
            {
                foreach (var raw in deal.BuyUnitPrices)
                    unitBuys.Add(RouteDisplay.Buy(raw, buyRegion, hubRegion).ToString());
            }
            var unitPart = unitBuys.Count > 0 ? $" units [{string.Join(", ", unitBuys.ToArray())}]" : "";
            return
                $"        {deal.Quantity} {deal.GoodName} @{deal.BuyPortName} buy {buyD}{unitPart} -> " +
                $"{string.Join(", ", legs.ToArray())} | profit {profitD}";
        }

        private static List<int> BuildPool(
            Port hub,
            Dictionary<int, Port> portsByIndex,
            bool crossRegion,
            int playerRepLevel)
        {
            var pool = new List<int>();
            if (crossRegion)
            {
                var maxDist = PlayerReputation.GetMaxDistance(hub.region);
                foreach (var kv in portsByIndex)
                {
                    if (kv.Key == hub.portIndex)
                        continue;
                    if (!RouteIslandAccess.IsPortAllowed(kv.Value.GetPortName(), playerRepLevel))
                        continue;
                    if (Mission.GetDistance(hub, kv.Value) <= maxDist)
                        pool.Add(kv.Key);
                }
            }
            else
            {
                foreach (var kv in portsByIndex)
                {
                    if (kv.Key == hub.portIndex)
                        continue;
                    if (!RouteIslandAccess.IsPortAllowed(kv.Value.GetPortName(), playerRepLevel))
                        continue;
                    if ((int)kv.Value.region == (int)hub.region)
                        pool.Add(kv.Key);
                }
            }
            return pool;
        }

        private static List<PortView> SampleRoute(
            Port hub,
            PortView hubView,
            List<int> pool,
            Dictionary<int, PortView> views,
            int hops,
            System.Random rng,
            bool crossRegion)
        {
            var need = hops - 1;
            if (pool.Count < need)
                return null;

            var shuffled = new List<int>(pool);
            for (var i = shuffled.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                var tmp = shuffled[i];
                shuffled[i] = shuffled[j];
                shuffled[j] = tmp;
            }

            var route = new List<PortView> { hubView };
            var regions = new HashSet<int> { hubView.Region };
            for (var i = 0; i < need; i++)
            {
                var view = views[shuffled[i]];
                route.Add(view);
                regions.Add(view.Region);
            }

            if (crossRegion && regions.Count < 2)
                return null;

            return route;
        }

        private static string RouteSignature(List<PortView> route)
        {
            var idx = new List<int>();
            foreach (var v in route)
                idx.Add(v.Index);
            idx.Sort();
            return string.Join(",", idx.ConvertAll(x => x.ToString()).ToArray());
        }

        private static List<GoodDef> BuildGoods()
        {
            var maxGoods = 0;
            foreach (var port in Port.ports)
            {
                var market = port == null ? null : port.GetComponent<IslandMarket>();
                if (market?.production != null)
                    maxGoods = Math.Max(maxGoods, market.production.Length);
            }
            if (maxGoods <= 0)
                maxGoods = 65;

            var goods = new List<GoodDef>();
            for (var g = 1; g < maxGoods; g++)
            {
                var shipItem = PrefabsDirectory.instance.GetGood(g);
                if (shipItem == null)
                    continue;
                var good = shipItem.GetComponent<Good>();
                if (good == null)
                    continue;
                goods.Add(new GoodDef
                {
                    Index = g,
                    Name = shipItem.name,
                    WeightLb = good.GetCargoWeight(),
                    VolumeCuft = CargoDims.ParseVolumeCuft(good.sizeDescription),
                });
            }
            return goods;
        }

        private static Dictionary<int, PortView> BuildPortViews(
            Dictionary<int, Port> portsByIndex,
            List<GoodDef> goods)
        {
            var views = new Dictionary<int, PortView>();
            foreach (var kv in portsByIndex)
            {
                var port = kv.Value;
                var market = port.GetComponent<IslandMarket>();
                if (market == null)
                    continue;

                var view = new PortView
                {
                    Index = port.portIndex,
                    Name = port.GetPortName(),
                    Region = (int)port.region,
                    SupplyPurchaseLimit = market.supplyPurchaseLimit,
                };

                foreach (var good in goods)
                {
                    if (good.Index >= market.currentSupply.Length)
                        continue;
                    var supply = market.currentSupply[good.Index];
                    var available = market.HasGood(good.Index);
                    view.Supply[good.Index] = supply;
                    view.Available[good.Index] = available;
                    view.BuyQty[good.Index] = available
                        ? Mathf.FloorToInt(Mathf.Max(0f, supply - market.supplyPurchaseLimit + 1f))
                        : 0;
                }

                views[port.portIndex] = view;
            }
            return views;
        }

        private static void LogRoutes(List<GeneratedRouteRow> rows, long snapshotId, long elapsedMs)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Routier routes — day {GameState.day}, snapshot #{snapshotId}, {rows.Count} routes in {elapsedMs} ms");

            var lastHub = int.MinValue;
            foreach (var row in rows)
            {
                if (row.HubPortIndex != lastHub)
                {
                    lastHub = row.HubPortIndex;
                    var cutPct = Mathf.RoundToInt(Mathf.Clamp(0.35f - 0.05f * row.RepLevel, 0.10f, 0.35f) * 100f);
                    var cur = PlayerGold.GetCurrencyName(row.HubRegion);
                    sb.AppendLine($"[{row.HubPortName}] region {row.HubRegion}, board tier L{row.Offer?.RouteTier ?? 0}, agent cut {cutPct}% ({cur})");
                }
                var capitalD = RouteDisplay.RawToDisplay(row.CapitalInitial, row.HubRegion);
                sb.AppendLine(
                    $"  {row.Tier,-4} {row.Kind,-8} {row.RouteNames}  capital {capitalD} " +
                    $"profit {row.DisplayProfit} ({Mathf.RoundToInt(row.Roi * 100f)}% ROI) price {row.Price}");
                if (row.Offer?.Plan?.Deals != null)
                {
                    foreach (var deal in row.Offer.Plan.Deals)
                        sb.AppendLine(BuildDealDetail(deal, row.HubRegion));
                }
                else
                {
                    sb.AppendLine($"        {row.Summary}");
                }
            }

            Plugin.Log.LogInfo(sb.ToString());
        }
    }
}
