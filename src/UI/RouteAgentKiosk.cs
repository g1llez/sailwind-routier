using UnityEngine;

namespace Routier
{
  /// <summary>Hub interactable: opens the route-offers UI to browse and buy a daily route guide.</summary>
  public sealed class RouteAgentKiosk : GoPointerButton
  {
    public int hubPortIndex;

    public override void Start()
    {
      base.Start();
      lookText = "Route agent — click to see today's routes";
    }

    public override void OnActivate()
    {
      var hub = Port.ports != null && hubPortIndex >= 0 && hubPortIndex < Port.ports.Length
        ? Port.ports[hubPortIndex]
        : null;
      if (hub == null)
        return;

      var playerRep = PlayerReputation.GetRepLevel(hub.region);
      if (playerRep < RouteTierTable.MinPlayerRepToAccess)
      {
        NotificationUi.instance.ShowNotification(RouteNotifications.AgentNeedsRep());
        return;
      }

      var cfg = Plugin.Instance != null ? Plugin.Instance.GenerationConfig : default;
      var offers = DailyRouteCatalog.GetAllHubOffers(hubPortIndex);
      if (offers.Count == 0)
      {
        NotificationUi.instance.ShowNotification(RouteBoardReport.EmptyBoardMessage());
        return;
      }

      var hubName = offers[0].HubPortName;
      RouteOffersUI.EnsureInstance();
      RouteOffersUI.Instance.Open(hubPortIndex, hubName, offers, playerRep, cfg);
      if (UISoundPlayer.instance != null)
        UISoundPlayer.instance.PlayUIClickSound();
      ForceUnlook();
    }
  }
}
