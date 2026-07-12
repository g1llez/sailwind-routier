using HarmonyLib;

namespace Routier
{
  [HarmonyPatch(typeof(ShipItemScroll), "OnLoad")]
  internal static class ShipItemScrollOnLoadPatch
  {
    private static bool Prefix(ShipItemScroll __instance)
    {
      var overlay = __instance.GetComponent<RouteParchmentOverlay>();
      if (overlay == null)
        return true;
      overlay.SetupOnLoad();
      return false;
    }

    private static void Postfix(ShipItemScroll __instance)
    {
      if (__instance.GetComponent<RouteParchmentOverlay>() != null)
        return;

      var save = __instance.GetComponent<SaveablePrefab>();
      if (save == null || !RouteParchmentRegistry.TryGet(save.instanceId, out var rawPages))
        return;

      var pages = ParchmentPageCodec.DeserializeAll(rawPages);
      var overlay = __instance.gameObject.AddComponent<RouteParchmentOverlay>();
      overlay.Bind(pages, __instance);
      overlay.SetupOnLoad();
    }
  }

  [HarmonyPatch(typeof(ShipItemScroll), "OnScroll")]
  internal static class ShipItemScrollOnScrollPatch
  {
    private static bool Prefix(ShipItemScroll __instance, float input)
    {
      var overlay = __instance.GetComponent<RouteParchmentOverlay>();
      if (overlay == null)
        return true;
      overlay.HandleScroll(input);
      return false;
    }
  }

  [HarmonyPatch(typeof(ShipItemScroll), "OnPickup")]
  internal static class ShipItemScrollOnPickupPatch
  {
    private static void Postfix(ShipItemScroll __instance)
    {
      var overlay = __instance.GetComponent<RouteParchmentOverlay>();
      if (overlay == null)
        return;
      overlay.Show();
    }
  }

  [HarmonyPatch(typeof(ShipItemScroll), "OnDrop")]
  internal static class ShipItemScrollOnDropPatch
  {
    private static void Postfix(ShipItemScroll __instance)
    {
      var overlay = __instance.GetComponent<RouteParchmentOverlay>();
      if (overlay == null)
        return;
      overlay.Hide();
    }
  }
}
