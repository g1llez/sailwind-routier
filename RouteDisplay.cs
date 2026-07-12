using UnityEngine;

namespace Routier
{
    /// <summary>
    /// Convert raw market prices to the hub's display currency (what the player sees in-game).
    /// The optimizer works in raw units; logs and parchment should show display units.
    /// </summary>
    internal static class RouteDisplay
    {
        internal static int Buy(int raw, int portRegion, int hubRegion)
        {
            if (CurrencyMarket.instance == null)
                return raw;
            var withFee = portRegion != hubRegion;
            return CurrencyMarket.instance.GetBuyPriceInCurrency((Currency)hubRegion, raw, withFee);
        }

        internal static int Sell(int raw, int portRegion, int hubRegion)
        {
            if (CurrencyMarket.instance == null)
                return raw;
            var withFee = portRegion != hubRegion;
            return CurrencyMarket.instance.GetSellPriceInCurrency((Currency)hubRegion, raw, withFee);
        }

        internal static int PortRegion(int portIndex)
        {
            if (Port.ports == null || portIndex < 0 || portIndex >= Port.ports.Length)
                return 0;
            var port = Port.ports[portIndex];
            return port != null ? (int)port.region : 0;
        }

        internal static int DealBuyTotal(RouteDeal deal, int hubRegion)
        {
            var buyRegion = PortRegion(deal.BuyPortIndex);
            var sum = 0;
            if (deal.BuyUnitPrices != null)
            {
                foreach (var raw in deal.BuyUnitPrices)
                    sum += Buy(raw, buyRegion, hubRegion);
            }
            else
            {
                sum = Buy(deal.BuyTotal, buyRegion, hubRegion);
            }
            return sum;
        }

        internal static int DealSellTotal(RouteDeal deal, int hubRegion)
        {
            var sum = 0;
            if (deal.SellLegs == null)
                return Sell(deal.SellTotal, PortRegion(deal.BuyPortIndex), hubRegion);
            foreach (var leg in deal.SellLegs)
            {
                var sellRegion = PortRegion(leg.PortIndex);
                if (leg.UnitPrices != null && leg.UnitPrices.Count > 0)
                {
                    foreach (var raw in leg.UnitPrices)
                        sum += Sell(raw, sellRegion, hubRegion);
                }
                else
                {
                    sum += Sell(leg.TotalRevenue, sellRegion, hubRegion);
                }
            }
            return sum;
        }

        internal static int DealProfit(RouteDeal deal, int hubRegion)
        {
            return DealSellTotal(deal, hubRegion) - DealBuyTotal(deal, hubRegion);
        }

        internal static int RawToDisplay(int raw, int hubRegion)
        {
            return Buy(raw, hubRegion, hubRegion);
        }
    }
}
