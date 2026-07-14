using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Routier
{
  /// <summary>Live rig clone for the route-agent NPC (validated: shopkeeper 5).</summary>
  internal static class HubNpcVisual
  {
    private const string ValidatedGrcNpcName = "shopkeeper (5)";
    private const float MinBodyExtent = 0.12f;

    internal static GameObject Create(string rootName, Scene? scene = null)
    {
      var source = FindIslandNpcTemplate(scene);
      if (source == null)
      {
        Plugin.Log.LogWarning("Routier: NPC template '" + ValidatedGrcNpcName + "' not found.");
        return null;
      }

      var rigRoot = FindRigRoot(source.transform);
      if (rigRoot == null || CountBodySmrs(rigRoot) == 0)
        return null;

      var clone = UnityEngine.Object.Instantiate(rigRoot.gameObject);
      clone.name = rootName;
      PruneStallAndGoods(clone.transform);
      StripGameplay(clone.transform);
      Plugin.Log.LogInfo("Routier NPC visual from: " + source.name);
      return clone;
    }

    internal static void PlaceAtKioskOffset(
      Transform npc,
      Transform kiosk,
      Vector3 localFeetOffset,
      float yawOffset)
    {
      var feet = MarketStallVisual.FeetFromKioskOffset(kiosk, localFeetOffset);
      var yaw = MarketStallVisual.KioskYaw(kiosk) + yawOffset;
      PlaceAtFeet(npc, feet, yaw);
    }

    internal static void PlaceAtFeet(Transform root, Vector3 feet, float yawDegrees)
    {
      root.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
      var localBottom = BottomCenterLocal(root);
      root.position = feet - root.TransformVector(localBottom);
    }

    private static Vector3 BottomCenterLocal(Transform root)
    {
      var bounds = CombinedLocalBounds(root);
      return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
    }

    private static GameObject FindIslandNpcTemplate(Scene? scene)
    {
      foreach (var keeper in Resources.FindObjectsOfTypeAll<Shopkeeper>())
      {
        if (keeper == null)
          continue;
        var go = keeper.gameObject;
        if (go.name != ValidatedGrcNpcName)
          continue;
        if (!go.scene.IsValid() || !go.scene.isLoaded)
          continue;
        if (scene.HasValue && go.scene != scene.Value)
          continue;
        return go;
      }

      return null;
    }

    private static Transform FindRigRoot(Transform source)
    {
      var anims = source.GetComponentInChildren<NPCAnimations>(true);
      if (anims != null)
        return anims.transform;

      var col = source.GetComponentInChildren<NPCPlayerCol>(true);
      return col != null ? col.transform : null;
    }

    private static int CountBodySmrs(Transform root)
    {
      var count = 0;
      foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
      {
        if (smr.sharedMesh == null || !smr.gameObject.activeInHierarchy)
          continue;
        if (IsMarketPropName(smr.name))
          continue;
        if (Mathf.Max(smr.bounds.size.x, smr.bounds.size.y, smr.bounds.size.z) < MinBodyExtent)
          continue;
        count++;
      }

      return count;
    }

    private static void PruneStallAndGoods(Transform root)
    {
      foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        mf.gameObject.SetActive(false);

      foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
      {
        if (IsMarketPropName(smr.name))
          smr.gameObject.SetActive(false);
      }
    }

    private static void StripGameplay(Transform root)
    {
      foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
        UnityEngine.Object.Destroy(mb);

      foreach (var col in root.GetComponentsInChildren<Collider>(true))
        UnityEngine.Object.Destroy(col);
    }

    private static Bounds CombinedLocalBounds(Transform root)
    {
      var bounds = new Bounds(Vector3.zero, Vector3.zero);
      var has = false;

      foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
      {
        if (smr.sharedMesh == null || !smr.gameObject.activeInHierarchy)
          continue;
        EncapsulateRenderer(ref bounds, ref has, smr, root);
      }

      return has ? bounds : new Bounds(Vector3.zero, Vector3.zero);
    }

    private static void EncapsulateRenderer(
      ref Bounds bounds,
      ref bool has,
      Renderer renderer,
      Transform root)
    {
      var meshBounds = renderer.bounds;
      var ext = meshBounds.extents;
      var center = meshBounds.center;
      for (var ix = -1; ix <= 1; ix += 2)
      for (var iy = -1; iy <= 1; iy += 2)
      for (var iz = -1; iz <= 1; iz += 2)
      {
        var world = center + Vector3.Scale(ext, new Vector3(ix, iy, iz));
        var local = root.InverseTransformPoint(world);
        if (!has)
        {
          bounds = new Bounds(local, Vector3.zero);
          has = true;
        }
        else
          bounds.Encapsulate(local);
      }
    }

    private static bool IsMarketPropName(string name)
    {
      var n = name.ToLowerInvariant();
      return n.IndexOf("stall", StringComparison.Ordinal) >= 0
          || n.IndexOf("table", StringComparison.Ordinal) >= 0
          || n.IndexOf("market", StringComparison.Ordinal) >= 0;
    }
  }
}
