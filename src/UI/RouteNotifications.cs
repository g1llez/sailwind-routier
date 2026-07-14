namespace Routier
{
  /// <summary>Short notification lines for vanilla NotificationUi (~25–28 chars per line).</summary>
  internal static class RouteNotifications
  {
    internal static string EmptyBoard() =>
      "No routes posted yet.\nNew manifests at " + RouteBoardReport.FormatHour(RouteBoardReport.GenerationHour) + ".";

    internal static string AgentNeedsRep() =>
      "Earn more trust in\nthis region first.";

    internal static string NeedRepLevel(int level) =>
      "Need rep level " + level + "\nin this region.";

    internal static string NotEnoughMoney() =>
      "Not enough money\nfor this guide.";

    internal static string GuidePurchased(string tier, string kind, int profitEst) =>
      "Route guide purchased.\n" + tier + " " + kind + " · ~" + profitEst + " profit";
  }
}
