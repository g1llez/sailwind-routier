using System.Collections.Generic;
using UnityEngine;

namespace Routier
{
  /// <summary>
  /// Nautical miles using the Sailwind interactive map formula
  /// (flat hypot on map lon/lat × 140). Matches direct port-to-port readings on
  /// moffkalast.github.io/Sailwind-Map. Sailing routes with coastal waypoints may be longer.
  /// </summary>
  internal static class PortMapDistance
  {
    private const float NmPerDegree = 140f;

    internal static float NmBetween(Port origin, Port destination)
    {
      if (origin == null || destination == null)
        return 0f;
      if (!PortMapCoords.TryGet(origin.GetPortName(), out var lon1, out var lat1)
          || !PortMapCoords.TryGet(destination.GetPortName(), out var lon2, out var lat2))
        return FallbackNm(origin, destination);
      return NmFromCoords(lon1, lat1, lon2, lat2);
    }

    internal static float RouteNm(IReadOnlyList<int> route)
    {
      if (Port.ports == null || route == null || route.Count < 2)
        return 0f;
      var total = 0f;
      for (var i = 0; i < route.Count - 1; i++)
      {
        if (route[i] < 0 || route[i] >= Port.ports.Length)
          continue;
        if (route[i + 1] < 0 || route[i + 1] >= Port.ports.Length)
          continue;
        var a = Port.ports[route[i]];
        var b = Port.ports[route[i + 1]];
        if (a == null || b == null)
          continue;
        total += NmBetween(a, b);
      }
      return total;
    }

    internal static float LegNm(RoutePlan plan, int stopIndex)
    {
      if (Port.ports == null || stopIndex >= plan.Route.Count - 1)
        return 0f;
      var a = Port.ports[plan.Route[stopIndex]];
      var b = Port.ports[plan.Route[stopIndex + 1]];
      return NmBetween(a, b);
    }

    private static float NmFromCoords(float lon1, float lat1, float lon2, float lat2)
    {
      var dLon = lon2 - lon1;
      var dLat = lat2 - lat1;
      return Mathf.Sqrt(dLon * dLon + dLat * dLat) * NmPerDegree;
    }

    /// <summary>Vanilla crow-flight distance when map coords are missing for a port.</summary>
    private static float FallbackNm(Port origin, Port destination)
    {
      var raw = Mission.GetDistance(origin, destination);
      if (Sun.sun == null)
        return raw;
      var units = raw * 100f;
      var scale = 0.514444f / Sun.sun.initialTimescale;
      return units / scale;
    }
  }
}
