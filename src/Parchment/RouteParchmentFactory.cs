using UnityEngine;

namespace Routier
{
  internal static class RouteParchmentFactory
  {
    internal static ShipItemScroll Spawn(Vector3 position, Quaternion rotation, ParchmentPage[] pages)
    {
      var template = FindScrollTemplate();
      if (template == null)
      {
        Plugin.Log.LogError("Routier: no scroll prefab found — cannot spawn route guide.");
        return null;
      }

      var go = Object.Instantiate(template, position, rotation);
      go.name = "Route Guide";

      var scroll = go.GetComponent<ShipItemScroll>();
      if (scroll == null)
      {
        Plugin.Log.LogError("Routier: scroll prefab has no ShipItemScroll.");
        Object.Destroy(go);
        return null;
      }

      var overlay = go.AddComponent<RouteParchmentOverlay>();
      overlay.Bind(pages, scroll);

      var save = go.GetComponent<SaveablePrefab>();
      if (save != null)
      {
        save.RegisterToSave();
        RouteParchmentRegistry.Register(save.instanceId, ParchmentPageCodec.SerializeAll(pages));
      }
      else
      {
        Plugin.Log.LogError("Routier: scroll prefab has no SaveablePrefab.");
      }

      return scroll;
    }

    private static GameObject FindScrollTemplate()
    {
      if (PrefabsDirectory.instance == null || PrefabsDirectory.instance.directory == null)
        return null;

      GameObject fallback = null;
      foreach (var go in PrefabsDirectory.instance.directory)
      {
        if (go == null || go.GetComponent<ShipItemScroll>() == null)
          continue;
        if (go.name.ToLower().Contains("generic"))
          return go;
        if (fallback == null)
          fallback = go;
      }
      return fallback;
    }
  }
}
