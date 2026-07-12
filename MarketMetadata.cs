using System.Collections.Generic;
using UnityEngine;

namespace Routier
{
    internal static class MarketMetadata
    {
        private static bool _globalsSynced;
        private static bool _portsSynced;

        internal static void EnsureSynced()
        {
            if (Plugin.Database == null)
                return;

            SyncGlobals();
            SyncPorts();
        }

        private static void SyncGlobals()
        {
            if (_globalsSynced)
                return;

            var tracker = DebugMarketTracker.instance;
            if (tracker == null)
                return;

            var row = new MarketGlobalsRow(
                DebugMarketTracker.goodsAmountSoftCap,
                tracker.positivePriceMult,
                tracker.negativePriceMult);

            Plugin.Database.UpsertMarketGlobals(row);
            _globalsSynced = true;
            Plugin.Log.LogInfo(
                $"Routier market globals: soft_cap={row.GoodsSoftCap}, pos_mult={row.PositivePriceMult}, neg_mult={row.NegativePriceMult}");
        }

        private static void SyncPorts()
        {
            if (_portsSynced || Port.ports == null)
                return;

            var rows = new List<PortCatalogRow>();
            foreach (var port in Port.ports)
            {
                if (port == null)
                    continue;
                var market = port.GetComponent<IslandMarket>();
                if (market == null)
                    continue;

                rows.Add(new PortCatalogRow(
                    port.portIndex,
                    port.GetPortName(),
                    (int)port.region,
                    market.supplyPurchaseLimit,
                    market.goodsSoftCapOverride));
            }

            if (rows.Count == 0)
                return;

            Plugin.Database.UpsertPortsCatalog(rows);
            _portsSynced = true;
            Plugin.Log.LogInfo($"Routier ports catalog synced ({rows.Count} ports)");
        }
    }

    internal readonly struct MarketGlobalsRow
    {
        public readonly float GoodsSoftCap;
        public readonly float PositivePriceMult;
        public readonly float NegativePriceMult;

        public MarketGlobalsRow(float goodsSoftCap, float positivePriceMult, float negativePriceMult)
        {
            GoodsSoftCap = goodsSoftCap;
            PositivePriceMult = positivePriceMult;
            NegativePriceMult = negativePriceMult;
        }
    }

    internal readonly struct PortCatalogRow
    {
        public readonly int PortIndex;
        public readonly string PortName;
        public readonly int Region;
        public readonly float SupplyPurchaseLimit;
        public readonly float GoodsSoftCapOverride;

        public PortCatalogRow(
            int portIndex,
            string portName,
            int region,
            float supplyPurchaseLimit,
            float goodsSoftCapOverride)
        {
            PortIndex = portIndex;
            PortName = portName;
            Region = region;
            SupplyPurchaseLimit = supplyPurchaseLimit;
            GoodsSoftCapOverride = goodsSoftCapOverride;
        }
    }
}
