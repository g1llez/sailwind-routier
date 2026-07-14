using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Routier
{
  /// <summary>Clones an island market_stall mesh (no sign/flag) for the route kiosk.</summary>
  internal static class MarketStallVisual
  {
    /// <summary>Locate an exact-named stall in an additive island scene (by build index).</summary>
    internal static GameObject FindSourceStallInBuildIndex(int buildIndex, string stallName)
    {
      var scene = SceneManager.GetSceneByBuildIndex(buildIndex);
      if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(stallName))
        return null;

      foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
      {
        if (go == null || go.scene != scene)
          continue;
        if (go.hideFlags != HideFlags.None)
          continue;
        if (go.name != stallName)
          continue;
        if (go.name.IndexOf("(Clone)", StringComparison.Ordinal) >= 0)
          continue;

        var path = HierarchyPath(go.transform).ToLowerInvariant();
        if (path.IndexOf("shifting", StringComparison.Ordinal) >= 0
            || path.IndexOf("boat", StringComparison.Ordinal) >= 0
            || path.IndexOf("dhow", StringComparison.Ordinal) >= 0)
          continue;
        if (path.IndexOf("island", StringComparison.Ordinal) < 0
            && path.IndexOf("scenery", StringComparison.Ordinal) < 0
            && path.IndexOf("clutter", StringComparison.Ordinal) < 0)
          continue;

        return go;
      }

      return null;
    }

    /// <summary>
    /// Bake an upright kiosk clone. Meshes are stored in the same local frame as
    /// <paramref name="uprightYawDegrees"/> so root axes match the stall (like a native market_stall).
    /// Must match the yaw later passed to <see cref="PlaceAtFeet"/>.
    /// </summary>
    internal static GameObject CreateFrom(GameObject source, string rootName, float uprightYawDegrees)
    {
      if (source == null)
        return null;
      Plugin.Log.LogInfo("Routier kiosk visual from: " + source.name
                         + " | bakeYaw=" + uprightYawDegrees.ToString("F2"));
      return BakeUpright(source, rootName, uprightYawDegrees);
    }

    internal static Bounds LocalMeshBounds(Transform root)
    {
      var bounds = new Bounds(Vector3.zero, Vector3.zero);
      var has = false;
      foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
      {
        if (mf.sharedMesh == null)
          continue;
        Encapsulate(ref bounds, ref has, mf.sharedMesh.bounds, mf.transform, root);
      }

      return has ? bounds : new Bounds(Vector3.zero, Vector3.zero);
    }

    private static GameObject BakeUpright(GameObject source, string rootName, float uprightYawDegrees)
    {
      var rootRot = Quaternion.Euler(0f, uprightYawDegrees, 0f);

      var root = new GameObject(rootName);
      // Start at source transform so worldPositionStays children keep their world pose;
      // then snap to the stall yaw (pitch/roll stripped) and normalize pivot.
      root.transform.SetPositionAndRotation(source.transform.position, rootRot);

      foreach (var mf in source.GetComponentsInChildren<MeshFilter>(true))
      {
        var mr = mf.GetComponent<MeshRenderer>();
        if (mf.sharedMesh == null || mr == null)
          continue;
        if (IsDecorProp(mf.transform))
          continue;

        var go = new GameObject(mf.name);
        go.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
        go.transform.SetParent(root.transform, true);
        go.transform.localScale = DivideLossy(mf.transform.lossyScale, root.transform.lossyScale);

        go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = mr.sharedMaterials;
        renderer.shadowCastingMode = mr.shadowCastingMode;
        renderer.receiveShadows = mr.receiveShadows;
      }

      // Root origin = mesh feet (bottom-center). Avoids PlaceAtFeet leaving a phantom
      // local offset (~−1 X) between gizmo/items and the visual stall.
      NormalizePivotToFeet(root.transform);
      EnsureTableCollider(root);
      return root;
    }

    /// <summary>
    /// Shift mesh children so BottomCenterLocal == 0 (root pivot = standing feet).
    /// Keeps world geometry unchanged.
    /// </summary>
    private static void NormalizePivotToFeet(Transform root)
    {
      var pivot = BottomCenterLocal(root);
      if (pivot.sqrMagnitude < 1e-8f)
        return;

      var worldFeet = root.TransformPoint(pivot);
      for (var i = 0; i < root.childCount; i++)
        root.GetChild(i).localPosition -= pivot;

      root.position = worldFeet;
    }

    private static Vector3 DivideLossy(Vector3 childWorldScale, Vector3 parentWorldScale)
    {
      return new Vector3(
        SafeDiv(childWorldScale.x, parentWorldScale.x),
        SafeDiv(childWorldScale.y, parentWorldScale.y),
        SafeDiv(childWorldScale.z, parentWorldScale.z));
    }

    private static float SafeDiv(float num, float den) =>
      Mathf.Abs(den) < 1e-5f ? num : num / den;

    /// <summary>
    /// Counter solid from Hack corner captures Pos1 (0.1, 1, 1.3) and Pos2 (−1.1, 0, −1.2).
    /// </summary>
    private static void EnsureTableCollider(GameObject root)
    {
      var box = root.AddComponent<BoxCollider>();
      box.center = new Vector3(-0.5f, 0.5f, 0.05f);
      box.size = new Vector3(1.2f, 1f, 2.5f);
    }

    internal static Vector3 BottomCenterLocal(Transform root)
    {
      var bounds = LocalMeshBounds(root);
      return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
    }

    internal static Vector3 BottomCenterWorld(Transform root) =>
      root.TransformPoint(BottomCenterLocal(root));

    internal static float KioskYaw(Transform kiosk) => kiosk.eulerAngles.y;

    /// <summary>World feet from offset in kiosk-local horizontal space.</summary>
    internal static Vector3 FeetFromKioskOffset(Transform kiosk, Vector3 localOffset) =>
      BottomCenterWorld(kiosk)
      + Quaternion.Euler(0f, KioskYaw(kiosk), 0f) * localOffset;

    internal static void PlaceAtFeet(Transform root, Vector3 feet, float yawDegrees)
    {
      root.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
      var localBottom = BottomCenterLocal(root);
      // After NormalizePivotToFeet, localBottom ≈ 0 → root.position = feet.
      root.position = feet - root.TransformVector(localBottom);
    }

    private static bool IsDecorProp(Transform t)
    {
      for (var cur = t; cur != null; cur = cur.parent)
      {
        var n = cur.name.ToLowerInvariant();
        if (n.IndexOf("sign", StringComparison.Ordinal) >= 0)
          return true;
        if (n.IndexOf("flag", StringComparison.Ordinal) >= 0)
          return true;
      }

      return false;
    }

    private static void Encapsulate(
      ref Bounds bounds,
      ref bool has,
      Bounds meshBounds,
      Transform meshTransform,
      Transform root)
    {
      for (var ix = -1; ix <= 1; ix += 2)
      for (var iy = -1; iy <= 1; iy += 2)
      for (var iz = -1; iz <= 1; iz += 2)
      {
        var world = meshTransform.TransformPoint(
          meshBounds.center + Vector3.Scale(meshBounds.extents, new Vector3(ix, iy, iz)));
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

    private static string HierarchyPath(Transform t)
    {
      var s = t.name;
      for (var cur = t.parent; cur != null; cur = cur.parent)
        s = cur.name + "/" + s;
      return s;
    }
  }
}
