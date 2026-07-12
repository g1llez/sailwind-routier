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
      var offers = DailyRouteCatalog.GetHubOffers(hubPortIndex);
      if (offers.Count == 0)
      {
        NotificationUi.instance.ShowNotification("No routes today — come back after 8am.");
        return;
      }

      RouteOffersUI.EnsureInstance();
      RouteOffersUI.Instance.Open(hubPortIndex, offers[0].HubPortName, offers);
    }
  }
}
