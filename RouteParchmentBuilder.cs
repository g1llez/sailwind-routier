using System.Collections.Generic;
using UnityEngine;

namespace Routier
{
  internal static class RouteParchmentBuilder
  {
    internal static ParchmentPage[] BuildPageModels(RouteOffer offer)
    {
      var pages = new List<ParchmentPage> { BuildSummaryPage(offer) };
      for (var stop = 0; stop < offer.Plan.Route.Count; stop++)
        pages.Add(BuildPortPage(offer, stop));
      return pages.ToArray();
    }

    private static ParchmentSummaryPage BuildSummaryPage(RouteOffer offer)
    {
      var currency = PlayerGold.GetCurrencyName(offer.HubRegion);
      var miles = Mathf.RoundToInt(DistanceMiles(offer.TotalDistanceKm));
      var capitalD = RouteDisplay.RawToDisplay(offer.CapitalInitial, offer.HubRegion);
      var profitD = offer.DisplayProfit > 0
        ? offer.DisplayProfit
        : SumDealDisplayProfit(offer.Plan, offer.HubRegion);

      var totalUnits = 0;
      var totalWeight = 0f;
      var totalVolume = 0f;
      foreach (var deal in offer.Plan.Deals)
      {
        totalUnits += deal.Quantity;
        totalWeight += deal.WeightTotal;
        totalVolume += deal.VolumeTotal;
      }
      var grandTotalLine = "Manifest total: " + totalUnits + " units, " + Mathf.RoundToInt(totalWeight)
        + " lb, " + Mathf.RoundToInt(totalVolume) + " ft³";

      return new ParchmentSummaryPage
      {
        Title = "TRADE ROUTE GUIDE",
        HubName = offer.Plan.RouteNames[0],
        Currency = currency,
        GrandTotalLine = grandTotalLine,
        RouteLines = WrapRoute(offer.Plan.RouteNames, 36).ToArray(),
        Stats = new List<SummaryStatRow>
        {
          new SummaryStatRow { Label = "hops", Value = offer.Plan.Route.Count.ToString() },
          new SummaryStatRow { Label = "distance", Value = "~" + miles + " mi" },
          new SummaryStatRow { Label = "capital", Value = capitalD.ToString() },
          new SummaryStatRow { Label = "est. profit", Value = profitD.ToString() },
          new SummaryStatRow { Label = "ROI", Value = Mathf.RoundToInt(offer.Roi * 100f) + "%" },
          new SummaryStatRow { Label = "guide price", Value = offer.Price.ToString() },
          new SummaryStatRow { Label = "peak weight", Value = Mathf.RoundToInt(offer.Plan.WeightUsed) + " lb" },
          new SummaryStatRow { Label = "peak volume", Value = Mathf.RoundToInt(offer.Plan.VolumeUsed) + " ft³" },
        },
      };
    }

    private static ParchmentReceiptPage BuildPortPage(RouteOffer offer, int stopIndex)
    {
      var route = offer.Plan.Route;
      var names = offer.Plan.RouteNames;
      var portIndex = route[stopIndex];
      var portName = names[stopIndex];
      var nextName = stopIndex < names.Count - 1 ? names[stopIndex + 1] : "(end)";
      var legMiles = Mathf.RoundToInt(DistanceMiles(LegDistanceKm(offer.Plan, stopIndex)));

      var sells = CollectSellRows(offer.Plan, portIndex, offer.HubRegion);
      var buys = CollectBuyRows(offer.Plan, portIndex, offer.HubRegion);
      var net = NetCashFlow(sells) + NetCashFlow(buys);

      string totalLine;
      if (net > 0)
        totalLine = "Total: +" + net;
      else if (net < 0)
        totalLine = "Total: " + net;
      else
        totalLine = "Total: 0";

      var isLastStop = stopIndex >= names.Count - 1;
      var headerLabel = isLastStop ? "PORT OF FINAL DISCHARGE" : "PORT OF LADING";
      var subtitle = isLastStop
        ? "END OF ROUTE"
        : "BOUND FOR: " + nextName;

      var legStats = new List<SummaryStatRow>();
      if (!isLastStop)
      {
        var cargo = ComputeLegCargo(offer.Plan, stopIndex);
        legStats.Add(new SummaryStatRow { Label = "distance", Value = "~" + legMiles + " mi" });
        legStats.Add(new SummaryStatRow { Label = "cargo", Value = cargo.units + " units" });
        legStats.Add(new SummaryStatRow { Label = "weight", Value = Mathf.RoundToInt(cargo.weightLb) + " lb" });
        legStats.Add(new SummaryStatRow { Label = "volume", Value = Mathf.RoundToInt(cargo.volumeCuft) + " ft³" });
      }

      return new ParchmentReceiptPage
      {
        HeaderLabel = headerLabel,
        Title = portName,
        Subtitle = subtitle,
        LegStats = legStats,
        Sells = new ReceiptSection
        {
          Title = "CARGO DISCHARGED",
          EmptyText = "No cargo discharged at this stop.",
          Rows = sells,
        },
        Buys = new ReceiptSection
        {
          Title = "CARGO LOADED",
          EmptyText = "No cargo loaded at this stop.",
          Rows = buys,
        },
        TotalLine = totalLine,
      };
    }

    private static int NetCashFlow(List<ReceiptLine> rows)
    {
      var net = 0;
      foreach (var row in rows)
      {
        if (row.Amount < 0)
          net += row.TotalMagnitude;
        else
          net -= row.TotalMagnitude;
      }
      return net;
    }

    private static List<ReceiptLine> CollectSellRows(RoutePlan plan, int portIndex, int hubRegion)
    {
      var rows = new List<ReceiptLine>();
      foreach (var deal in plan.Deals)
      {
        foreach (var leg in deal.SellLegs)
        {
          if (leg.PortIndex != portIndex || leg.Quantity <= 0)
            continue;
          var sellRegion = RouteDisplay.PortRegion(leg.PortIndex);
          var totalD = 0;
          if (leg.UnitPrices != null && leg.UnitPrices.Count > 0)
          {
            foreach (var raw in leg.UnitPrices)
              totalD += RouteDisplay.Sell(raw, sellRegion, hubRegion);
          }
          else
          {
            totalD = RouteDisplay.Sell(leg.TotalRevenue, sellRegion, hubRegion);
          }
          var qty = leg.Quantity;
          rows.Add(new ReceiptLine
          {
            Name = deal.GoodName,
            Amount = -qty,
            AvgPrice = qty > 0 ? (float)totalD / qty : totalD,
            TotalMagnitude = totalD,
          });
        }
      }
      return rows;
    }

    private static List<ReceiptLine> CollectBuyRows(RoutePlan plan, int portIndex, int hubRegion)
    {
      var rows = new List<ReceiptLine>();
      foreach (var deal in plan.Deals)
      {
        if (deal.BuyPortIndex != portIndex || deal.Quantity <= 0)
          continue;
        var totalD = RouteDisplay.DealBuyTotal(deal, hubRegion);
        var qty = deal.Quantity;
        rows.Add(new ReceiptLine
        {
          Name = deal.GoodName,
          Amount = qty,
          AvgPrice = qty > 0 ? (float)totalD / qty : totalD,
          TotalMagnitude = totalD,
        });
      }
      return rows;
    }

    private static int SumDealDisplayProfit(RoutePlan plan, int hubRegion)
    {
      var sum = 0;
      foreach (var deal in plan.Deals)
        sum += RouteDisplay.DealProfit(deal, hubRegion);
      return sum;
    }

    private static List<string> WrapRoute(List<string> names, int maxLen)
    {
      var lines = new List<string>();
      var current = names[0];
      for (var i = 1; i < names.Count; i++)
      {
        var part = " -> " + names[i];
        if (current.Length + part.Length > maxLen)
        {
          lines.Add(current + " ->");
          current = names[i];
        }
        else
        {
          current += part;
        }
      }
      lines.Add(current);
      return lines;
    }

    /// <summary>
    /// Cargo (units/weight/volume) still aboard during the leg departing <paramref name="legIndex"/>,
    /// i.e. after any sales made on arrival at that stop but before the next one.
    /// Assumes a deal's buy/sell ports each appear once in the route (true for generated routes).
    /// </summary>
    private static (int units, float weightLb, float volumeCuft) ComputeLegCargo(RoutePlan plan, int legIndex)
    {
      var units = 0;
      var weightLb = 0f;
      var volumeCuft = 0f;

      foreach (var deal in plan.Deals)
      {
        var buyStop = FindStopIndex(plan.Route, deal.BuyPortIndex, 0);
        if (buyStop < 0 || buyStop > legIndex || deal.Quantity <= 0)
          continue;

        var soldByLegStart = 0;
        foreach (var leg in deal.SellLegs)
        {
          var sellStop = FindStopIndex(plan.Route, leg.PortIndex, buyStop + 1);
          if (sellStop >= 0 && sellStop <= legIndex)
            soldByLegStart += leg.Quantity;
        }

        var onboard = deal.Quantity - soldByLegStart;
        if (onboard <= 0)
          continue;

        var unitWeight = deal.WeightTotal / deal.Quantity;
        var unitVolume = deal.VolumeTotal / deal.Quantity;
        units += onboard;
        weightLb += onboard * unitWeight;
        volumeCuft += onboard * unitVolume;
      }

      return (units, weightLb, volumeCuft);
    }

    private static int FindStopIndex(List<int> route, int portIndex, int searchFrom)
    {
      for (var i = Mathf.Max(0, searchFrom); i < route.Count; i++)
        if (route[i] == portIndex)
          return i;
      for (var i = 0; i < route.Count; i++)
        if (route[i] == portIndex)
          return i;
      return -1;
    }

    private static float LegDistanceKm(RoutePlan plan, int stopIndex)
    {
      if (Port.ports == null || stopIndex >= plan.Route.Count - 1)
        return 0f;
      var a = Port.ports[plan.Route[stopIndex]];
      var b = Port.ports[plan.Route[stopIndex + 1]];
      if (a == null || b == null)
        return 0f;
      return Mission.GetDistance(a, b);
    }

    internal static float ComputeRouteDistanceKm(RoutePlan plan)
    {
      if (Port.ports == null || plan.Route.Count < 2)
        return 0f;
      var total = 0f;
      for (var i = 0; i < plan.Route.Count - 1; i++)
      {
        var a = Port.ports[plan.Route[i]];
        var b = Port.ports[plan.Route[i + 1]];
        if (a == null || b == null)
          continue;
        total += Mission.GetDistance(a, b);
      }
      return total;
    }

    internal static float DistanceMiles(float km)
    {
      if (Sun.sun == null)
        return km;
      var units = km * 100f;
      var scale = 0.514444f / Sun.sun.initialTimescale;
      return units / scale;
    }
  }
}
