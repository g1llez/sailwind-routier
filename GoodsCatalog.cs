using System;
using System.Collections.Generic;
using UnityEngine;

namespace Routier
{
    internal static class GoodsCatalog
    {
        private static bool _synced;

        internal static void EnsureSynced()
        {
            if (Plugin.Database == null || PrefabsDirectory.instance == null)
                return;

            var maxGoods = 0;
            if (Port.ports != null)
            {
                foreach (var port in Port.ports)
                {
                    if (port == null)
                        continue;
                    var market = port.GetComponent<IslandMarket>();
                    if (market?.production == null)
                        continue;
                    maxGoods = Math.Max(maxGoods, market.production.Length);
                }
            }

            if (maxGoods <= 0)
                maxGoods = 65;

            var rows = new List<GoodCatalogRow>();
            for (var goodIndex = 0; goodIndex < maxGoods; goodIndex++)
            {
                var shipItem = PrefabsDirectory.instance.GetGood(goodIndex);
                if (shipItem == null)
                    continue;
                var good = shipItem.GetComponent<Good>();
                if (good == null)
                    continue;

                rows.Add(new GoodCatalogRow(
                    goodIndex,
                    shipItem.name,
                    good.sizeDescription ?? string.Empty,
                    good.GetCargoWeight(),
                    GoodBaseValue.Get(shipItem)));
            }

            if (rows.Count == 0)
                return;

            Plugin.Database.UpsertGoodsCatalog(rows);
            if (!_synced)
            {
                _synced = true;
                Plugin.Log.LogInfo($"Routier goods catalog synced ({rows.Count} goods)");
            }
        }
    }

    internal readonly struct GoodCatalogRow
    {
        public readonly int GoodIndex;
        public readonly string GoodName;
        public readonly string SizeDescription;
        public readonly float WeightLb;
        public readonly float BaseValue;

        public GoodCatalogRow(int goodIndex, string goodName, string sizeDescription, float weightLb, float baseValue)
        {
            GoodIndex = goodIndex;
            GoodName = goodName;
            SizeDescription = sizeDescription;
            WeightLb = weightLb;
            BaseValue = baseValue;
        }
    }
}
