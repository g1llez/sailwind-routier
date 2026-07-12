using HarmonyLib;

namespace Routier
{
  /// <summary>Route guides are sold loot, not shop stock.</summary>
  [HarmonyPatch(typeof(ShipItem), "GetSellPriceString")]
  internal static class ShipItemSellPricePatch
  {
    private static bool Prefix(ShipItem __instance, ref string __result)
    {
      if (__instance.GetComponent<RouteParchmentOverlay>() == null)
        return true;
      __result = "";
      return false;
    }
  }
}
