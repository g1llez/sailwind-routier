using System.Collections.Generic;

namespace Routier
{
  internal sealed class RouteOffer
  {
    public long SnapshotId;
    public int GameDay;
    public int HubPortIndex;
    public string HubPortName;
    public int HubRegion;
    public string Kind;
    public RoutePlan Plan;
    public int CapitalInitial;
    public int GrossPurchases;
    public int Profit;
    public int DisplayProfit;
    public float Roi;
    public int Price;
    public int RepLevel;
    /// <summary>Difficulty tier of this manifest (1–5). Used for access gating.</summary>
    public int RouteTier;
    public string Tier;
    public float TotalDistanceNm;
    public ParchmentPage[] Pages;
  }

  /// <summary>In-memory daily offers per hub (reset at 8am generation).</summary>
  internal static class DailyRouteCatalog
  {
    private static readonly Dictionary<int, List<RouteOffer>> ByHub =
      new Dictionary<int, List<RouteOffer>>();

    internal static void Reset()
    {
      ByHub.Clear();
    }

    internal static void Add(RouteOffer offer)
    {
      if (!ByHub.TryGetValue(offer.HubPortIndex, out var list))
      {
        list = new List<RouteOffer>();
        ByHub[offer.HubPortIndex] = list;
      }
      list.Add(offer);
    }

    internal static IReadOnlyList<RouteOffer> GetAllHubOffers(int hubPortIndex)
    {
      if (!ByHub.TryGetValue(hubPortIndex, out var list))
        return new List<RouteOffer>();
      return list;
    }

    internal static void RemoveHub(int hubPortIndex)
    {
      ByHub.Remove(hubPortIndex);
    }
  }
}
