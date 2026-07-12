using UnityEngine;

namespace Routier
{
    /// <summary>Base price value used by IslandMarket.GetGoodPriceAtSupply (item.value + crate rules).</summary>
    internal static class GoodBaseValue
    {
        internal static float Get(int goodIndex)
        {
            if (PrefabsDirectory.instance == null)
                return 0f;
            return Get(PrefabsDirectory.instance.GetGood(goodIndex));
        }

        internal static float Get(ShipItem item)
        {
            if (item == null)
                return 0f;

            var value = (float)item.value;
            if (item.GetType() == typeof(ShipItemCrate))
            {
                var crate = (ShipItemCrate)item;
                var contained = crate.GetContainedPrefab()?.GetComponent<ShipItem>();
                if (contained != null && contained.GetType() == typeof(ShipItemFood))
                {
                    value = (float)contained.value * crate.amount + 20f;
                    value *= 2f;
                }
            }

            return value;
        }
    }
}
