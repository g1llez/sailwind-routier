using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Routier
{
  /// <summary>
  /// Places route-agent kiosks at hub ports.
  ///
  /// GRC waits for additive island scene build-index 1 (PassageDude: islandIndex 1 → GRC)
  /// and clones only market_stall (8) from that scene — never a stall from another island.
  ///
  /// Floating origin: GrcFeet is a world coord at its capture offset. Anchor to the live
  /// stall: snap (GrcFeet - stallWorld) onto the shift grid so placement is offset-independent.
  ///
  /// Book / NPC / parchment / collider locals: SailwindHack on RoutierRouteKiosk.
  /// </summary>
  internal sealed class HubKioskInstaller : MonoBehaviour
  {
    private const int GrcIslandBuildIndex = 1;

    // SailwindHack captures (clean pivot, placeYaw = source+180°).
    private static readonly Vector3 GrcFeet = new Vector3(375.3802f, 2.6259f, 468.2021f);

    private static readonly Vector3 GrcNpcLocalOffset = new Vector3(0.6f, 0f, 0f);
    private const float GrcNpcYawOffset = -90f;

    // Hack probe on kiosk (exact local). Roll matches table slope.
    private static readonly Vector3 GrcBookLocalOffset = new Vector3(-0.5f, 1f, 0f);
    private static readonly Vector3 GrcBookEulerOffset = new Vector3(0f, 0f, 5f);
    private const float GrcBookScale = 0.798f;

    private const string GrcStallName = "market_stall (8)";
    private const string KioskName = "RoutierRouteKiosk";
    private const float DefaultShiftDistance = 512f;
    private const float RetryIntervalSeconds = 1f;

    private IEnumerator Start()
    {
      while (!GameState.playing || GameState.currentlyLoading || Port.ports == null)
        yield return null;

      while (true)
      {
        if (GameState.playing && !GameState.currentlyLoading && Port.ports != null)
          TryInstallAll();
        yield return new WaitForSeconds(RetryIntervalSeconds);
      }
    }

    private void TryInstallAll()
    {
      foreach (var port in Port.ports)
      {
        if (port == null || !port.hubPort)
          continue;
        if (port.transform.Find(KioskName) != null)
          continue;

        var isGrc = port.GetPortName().IndexOf("Gold Rock", StringComparison.OrdinalIgnoreCase) >= 0;
        if (isGrc)
          TryInstallGrc(port);
        else
          TryInstallFallback(port);
      }
    }

    private void TryInstallGrc(Port port)
    {
      var scene = SceneManager.GetSceneByBuildIndex(GrcIslandBuildIndex);
      if (!scene.IsValid() || !scene.isLoaded)
        return;

      var source = MarketStallVisual.FindSourceStallInBuildIndex(GrcIslandBuildIndex, GrcStallName);
      if (source == null)
        return;

      var stallWorld = source.transform.position;
      var kioskWorld = AnchorToStall(GrcFeet, stallWorld);
      // Bake in the source frame, then place flipped toward the dock.
      var bakeYaw = source.transform.eulerAngles.y;
      var placeYaw = NormalizeYaw(bakeYaw + 180f);

      var kiosk = MarketStallVisual.CreateFrom(source, KioskName, bakeYaw);
      if (kiosk == null)
        return;

      kiosk.transform.SetParent(port.transform, true);
      MarketStallVisual.PlaceAtFeet(kiosk.transform, kioskWorld, placeYaw);

      var bottom = MarketStallVisual.BottomCenterLocal(kiosk.transform);
      Plugin.Log.LogInfo("Routier GRC kiosk pivot check | BottomCenterLocal="
                         + bottom.x.ToString("F3") + ","
                         + bottom.y.ToString("F3") + ","
                         + bottom.z.ToString("F3")
                         + " (expect ~0,0,0)");

      var npc = HubNpcVisual.Create("RoutierRouteAgent", scene);
      if (npc != null)
      {
        npc.transform.SetParent(kiosk.transform, false);
        HubNpcVisual.PlaceAtKioskOffset(
          npc.transform, kiosk.transform, GrcNpcLocalOffset, GrcNpcYawOffset);
        npc.transform.SetParent(kiosk.transform, true);
      }

      var book = HubBookVisual.CreateInteractable("RoutierRouteBook", port.portIndex, scene);
      if (book != null)
        HubBookVisual.PlaceAtKioskOffset(
          book.transform, kiosk.transform, GrcBookLocalOffset, GrcBookEulerOffset, GrcBookScale);

      Plugin.Log.LogInfo("Routier GRC kiosk placed"
                         + " | stallWorld=" + stallWorld
                         + " | kioskWorld=" + kioskWorld
                         + " | bakeYaw=" + bakeYaw.ToString("F2")
                         + " | placeYaw=" + placeYaw.ToString("F2")
                         + " | portIndex=" + port.portIndex
                         + " | scene=" + scene.name);
    }

    private void TryInstallFallback(Port port)
    {
      var kiosk = CreateFallbackCube(KioskName);
      kiosk.transform.SetParent(port.transform, false);
      kiosk.transform.localPosition = new Vector3(1.4f, 1.0f, 0.5f);
      var book = HubBookVisual.CreateInteractable("RoutierRouteBook", port.portIndex);
      if (book != null)
        HubBookVisual.PlaceAtKioskOffset(
          book.transform,
          kiosk.transform,
          HubBookVisual.DefaultTableTopLocal(kiosk.transform),
          Vector3.zero,
          GrcBookScale);
      Plugin.Log.LogInfo("Routier fallback kiosk placed at " + port.GetPortName() + ".");
    }

    private static Vector3 AnchorToStall(Vector3 feetWorld, Vector3 stallWorld)
    {
      var frame = DefaultShiftDistance;
      if (FloatingOriginManager.instance != null && FloatingOriginManager.instance.shiftDistance > 1f)
        frame = FloatingOriginManager.instance.shiftDistance;

      var delta = feetWorld - stallWorld;
      var snapped = new Vector3(SnapAxis(delta.x, frame), delta.y, SnapAxis(delta.z, frame));
      return stallWorld + snapped;
    }

    private static float SnapAxis(float value, float frame)
    {
      if (frame <= 1f)
        return value;
      return value - Mathf.Round(value / frame) * frame;
    }

    private static float NormalizeYaw(float yaw)
    {
      yaw %= 360f;
      if (yaw < 0f)
        yaw += 360f;
      return yaw;
    }

    private static GameObject CreateFallbackCube(string name)
    {
      var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
      go.name = name;
      go.transform.localScale = new Vector3(0.35f, 0.5f, 0.2f);
      return go;
    }
  }
}
