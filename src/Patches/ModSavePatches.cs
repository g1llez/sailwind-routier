using HarmonyLib;

namespace Routier
{
  [HarmonyPatch(typeof(SaveLoadManager), "SaveModData")]
  internal static class SaveModDataPatch
  {
    private static void Prefix()
    {
      RouteParchmentRegistry.Flush();
    }
  }

  [HarmonyPatch(typeof(SaveLoadManager), "LoadModData")]
  internal static class LoadModDataPatch
  {
    private static void Prefix()
    {
      RouteParchmentRegistry.LoadFromGameState();
    }
  }
}
