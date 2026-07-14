using UnityEngine;

namespace Routier
{
  /// <summary>Formats the route-agent board date line (manifest day, freshness, next posting).</summary>
  internal static class RouteBoardReport
  {
    private const string StaleColor = "#AA2222";

    internal static int GenerationHour =>
      Plugin.Instance != null ? Plugin.Instance.RoutesGenerationHour : 8;

    internal static int CurrentHour =>
      Sun.sun != null ? Mathf.FloorToInt(Sun.sun.globalTime) : 0;

    /// <summary>True when the board predates the current in-game day (posted before last midnight).</summary>
    internal static bool IsStale(int reportDay) =>
      GameState.day > reportDay;

    internal static int NextPostingDay(int reportDay)
    {
      var day = GameState.day;
      var hour = CurrentHour;
      var genHour = GenerationHour;
      if (hour < genHour || IsStale(reportDay))
        return day;
      return day + 1;
    }

    internal static string FormatHour(int hour) =>
      Mathf.Clamp(hour, 0, 23).ToString("00") + ":00";

    /// <summary>
    /// e.g. Currency: Al'Ankh Lions  ·  Board D45 (today)  ·  Next D46, 08:00
    /// </summary>
    internal static string FormatHeaderLine(string currencyName, int reportDay)
    {
      var stale = IsStale(reportDay);
      var age = stale
        ? "<color=" + StaleColor + ">yesterday</color>"
        : "today";
      var nextDay = NextPostingDay(reportDay);
      var nextHour = FormatHour(GenerationHour);

      return "Currency: " + currencyName
        + "  ·  Board D" + reportDay + " (" + age + ")"
        + "  ·  Next D" + nextDay + ", " + nextHour;
    }

    internal static string EmptyBoardMessage() => RouteNotifications.EmptyBoard();
  }
}
