using System;
using System.Collections.Generic;

namespace Routier
{
  /// <summary>
  /// Reputation gates for including ports on generated routes.
  /// Rep is the player's level in the hub's region (<see cref="PlayerReputation.GetRepLevel"/>).
  /// </summary>
  internal static class RouteIslandAccess
  {
    private static readonly Dictionary<string, int> MinRepByPortName =
      new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
      {
        // Al'Ankh
        { "Oasis", 3 },
        { "Mirage Mountain", 3 },
        { "Saffron Island", 5 },
      };

    internal static bool IsPortAllowed(string portName, int playerRepLevel)
    {
      if (string.IsNullOrEmpty(portName))
        return true;
      if (!MinRepByPortName.TryGetValue(portName, out var minRep))
        return true;
      return playerRepLevel >= minRep;
    }

    internal static int RequiredRep(string portName)
    {
      if (string.IsNullOrEmpty(portName))
        return 0;
      return MinRepByPortName.TryGetValue(portName, out var minRep) ? minRep : 0;
    }
  }
}
