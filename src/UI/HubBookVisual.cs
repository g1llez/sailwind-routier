using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Routier
{
  /// <summary>
  /// Shipyard counter book prop (flat mesh + mission_ui_paint texture) on the route kiosk.
  /// Validated F10 @ GRC shipyard: "shipyard documents" / mission_details_UI_bg_001.
  /// Pose is kiosk-local — moves with RoutierRouteKiosk when GrcFeet/GrcYaw change.
  /// </summary>
  internal static class HubBookVisual
  {
    private const string ValidatedShipyardBookName = "shipyard documents";
    private static readonly Vector3 FallbackTableLocal = new Vector3(-0.5f, 1f, 0f);
    private static Quaternion _tableLocalRotation = Quaternion.Euler(270f, 0f, 0f);

    internal static GameObject CreateInteractable(string rootName, int hubPortIndex, Scene? scene = null)
    {
      var sourceName = ValidatedShipyardBookName;
      var go = CreateFromShipyardBook(rootName, out sourceName, scene);
      if (go == null)
      {
        Plugin.Log.LogWarning("Routier: shipyard documents template not found for route book.");
        return null;
      }

      EnsureCollider(go.transform);

      var kiosk = go.GetComponent<RouteAgentKiosk>();
      if (kiosk == null)
        kiosk = go.AddComponent<RouteAgentKiosk>();
      kiosk.hubPortIndex = hubPortIndex;

      Plugin.Log.LogInfo("Routier route book visual from: " + sourceName);
      return go;
    }

    internal static Vector3 DefaultTableTopLocal(Transform kiosk) => FallbackTableLocal;

    /// <summary>
    /// <paramref name="localPose"/> = SailwindHack probe local on the kiosk (exact transform).
    /// <paramref name="eulerOffsetDegrees"/> = pitch/yaw/roll after the template flat rotation.
    /// </summary>
    internal static void PlaceAtKioskOffset(
      Transform bookRoot,
      Transform kiosk,
      Vector3 localPose,
      Vector3 eulerOffsetDegrees,
      float uniformScale)
    {
      bookRoot.SetParent(kiosk, false);
      // Hack lRot is kiosk-local (roll on the table). Apply that in parent space,
      // then the shipyard "flat on counter" mesh orientation — not flat*roll (hides Z).
      bookRoot.localRotation = Quaternion.Euler(eulerOffsetDegrees) * _tableLocalRotation;
      bookRoot.localScale = Vector3.one * uniformScale;
      bookRoot.localPosition = localPose;
    }

    private static GameObject CreateFromShipyardBook(string rootName, out string sourceName, Scene? scene)
    {
      sourceName = ValidatedShipyardBookName;
      var source = FindShipyardBookTemplate(scene);
      if (source == null)
        return null;

      sourceName = source.name;
      _tableLocalRotation = source.transform.localRotation;

      var srcMf = source.GetComponent<MeshFilter>();
      var srcMr = source.GetComponent<MeshRenderer>();
      if (srcMf == null || srcMr == null || srcMf.sharedMesh == null)
        return null;

      var go = new GameObject(rootName);
      go.AddComponent<MeshFilter>().sharedMesh = srcMf.sharedMesh;
      var mr = go.AddComponent<MeshRenderer>();
      mr.sharedMaterials = srcMr.sharedMaterials;
      mr.shadowCastingMode = srcMr.shadowCastingMode;
      mr.receiveShadows = srcMr.receiveShadows;
      return go;
    }

    private static GameObject FindShipyardBookTemplate(Scene? scene)
    {
      GameObject sceneryMatch = null;
      GameObject nameMatch = null;

      foreach (var doc in Resources.FindObjectsOfTypeAll<ShipyardDocuments>())
      {
        if (doc == null || !IsSceneObject(doc.gameObject))
          continue;
        if (scene.HasValue && doc.gameObject.scene != scene.Value)
          continue;
        if (IsUiLike(doc.gameObject))
          continue;

        var path = HierarchyPath(doc.transform).ToLowerInvariant();
        var isScenery = path.IndexOf("scenery", StringComparison.Ordinal) >= 0
                        && path.IndexOf("shipyard", StringComparison.Ordinal) >= 0;

        if (doc.name.Equals(ValidatedShipyardBookName, StringComparison.OrdinalIgnoreCase))
        {
          if (isScenery)
            return doc.gameObject;
          nameMatch = doc.gameObject;
        }
        else if (isScenery && sceneryMatch == null)
        {
          sceneryMatch = doc.gameObject;
        }
      }

      return nameMatch ?? sceneryMatch;
    }

    private static bool IsSceneObject(GameObject go)
    {
      if (go == null)
        return false;
      if (!go.scene.IsValid() || !go.scene.isLoaded)
        return false;
      if (go.hideFlags != HideFlags.None)
        return false;
      if (go.name.IndexOf("RoutierRoute", StringComparison.Ordinal) >= 0)
        return false;
      return true;
    }

    private static bool IsUiLike(GameObject go)
    {
      for (var cur = go.transform; cur != null; cur = cur.parent)
      {
        var n = cur.name.ToLowerInvariant();
        if (n.IndexOf("missionlistui", StringComparison.Ordinal) >= 0)
          return true;
        if (n.IndexOf("mission ui", StringComparison.Ordinal) >= 0)
          return true;
        if (n.IndexOf("canvas", StringComparison.Ordinal) >= 0)
          return true;
      }

      return false;
    }

    private static string HierarchyPath(Transform t)
    {
      var s = t.name;
      for (var cur = t.parent; cur != null; cur = cur.parent)
        s = cur.name + "/" + s;
      return s;
    }

    private static void EnsureCollider(Transform root)
    {
      foreach (var col in root.GetComponentsInChildren<Collider>(true))
        UnityEngine.Object.Destroy(col);

      var bounds = CombinedLocalBounds(root);
      if (bounds.size.sqrMagnitude < 0.0001f)
        bounds = new Bounds(Vector3.zero, new Vector3(0.35f, 0.02f, 0.45f));

      var box = root.gameObject.AddComponent<BoxCollider>();
      box.center = bounds.center;
      box.size = bounds.size;
    }

    private static Vector3 BottomCenterLocal(Transform root)
    {
      var bounds = CombinedLocalBounds(root);
      return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
    }

    private static Bounds CombinedLocalBounds(Transform root)
    {
      var bounds = new Bounds(Vector3.zero, Vector3.zero);
      var has = false;

      foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
      {
        if (mf.sharedMesh == null)
          continue;
        EncapsulateMeshBounds(ref bounds, ref has, mf.sharedMesh.bounds, mf.transform, root);
      }

      return has ? bounds : new Bounds(Vector3.zero, Vector3.zero);
    }

    private static void EncapsulateMeshBounds(
      ref Bounds bounds,
      ref bool has,
      Bounds meshBounds,
      Transform meshTransform,
      Transform root)
    {
      var ext = meshBounds.extents;
      var center = meshBounds.center;
      for (var ix = -1; ix <= 1; ix += 2)
      for (var iy = -1; iy <= 1; iy += 2)
      for (var iz = -1; iz <= 1; iz += 2)
      {
        var world = meshTransform.TransformPoint(
          center + Vector3.Scale(ext, new Vector3(ix, iy, iz)));
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
  }
}
