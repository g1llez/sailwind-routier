using System.Collections;
using UnityEngine;

namespace Routier
{
  /// <summary>Spawns a route-agent kiosk at each hub port once the world is ready.</summary>
  internal sealed class HubKioskInstaller : MonoBehaviour
  {
    private bool _installed;

    private IEnumerator Start()
    {
      yield return new WaitForSeconds(5f);
      TryInstall();
    }

    private void TryInstall()
    {
      if (_installed || Port.ports == null)
        return;

      foreach (var port in Port.ports)
      {
        if (port == null || !port.hubPort)
          continue;
        if (port.transform.Find("RoutierRouteKiosk") != null)
          continue;

        var kioskGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        kioskGo.name = "RoutierRouteKiosk";
        kioskGo.transform.SetParent(port.transform, false);
        kioskGo.transform.localPosition = new Vector3(1.4f, 1.0f, 0.5f);
        kioskGo.transform.localScale = new Vector3(0.35f, 0.5f, 0.2f);

        var kiosk = kioskGo.AddComponent<RouteAgentKiosk>();
        kiosk.hubPortIndex = port.portIndex;
        kiosk.lookText = "Route agent";
      }

      _installed = true;
      Plugin.Log.LogInfo("Routier route-agent kiosks installed at hub ports.");
    }
  }
}
