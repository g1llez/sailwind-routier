using System;

namespace Routier
{
    /// <summary>
    /// Route difficulty tiers (1–5). Edit <see cref="FixedTiers"/> and tier-5 constants — see TODO.md.
    /// </summary>
    internal readonly struct RouteTierSpec
    {
        public readonly int Level;
        public readonly int HopsMin;
        public readonly int HopsMax;
        public readonly int BudgetMax;
        public readonly float? MaxWeightLb;
        public readonly float? MaxVolumeCuft;
        public readonly bool CanBeRegional;

        public RouteTierSpec(
            int level,
            int hopsMin,
            int hopsMax,
            int budgetMax,
            float? maxWeightLb,
            float? maxVolumeCuft,
            bool canBeRegional)
        {
            Level = level;
            HopsMin = hopsMin;
            HopsMax = hopsMax;
            BudgetMax = budgetMax;
            MaxWeightLb = maxWeightLb;
            MaxVolumeCuft = maxVolumeCuft;
            CanBeRegional = canBeRegional;
        }
    }

    internal static class RouteTierTable
    {
        internal const int MinPlayerRepToAccess = 1;
        internal const int MinPlayerRepForRegional = 4;

        internal const int MinTier = 1;
        internal const int MaxTier = 5;

        // Tier 5+ caps (edit here)
        internal const int Tier5BudgetMax = 50000;
        internal const float Tier5MaxWeightLb = 10000f;
        internal const float Tier5MaxVolumeCuft = 500f;

        private static readonly RouteTierSpec[] FixedTiers =
        {
            new RouteTierSpec(1, 3, 3, 500,   250f,   10f,  false),
            new RouteTierSpec(2, 3, 4, 1000,  500f,   25f,  false),
            new RouteTierSpec(3, 3, 5, 5000,  1000f,  40f,  false),
            new RouteTierSpec(4, 3, 5, 15000, 4000f,  120f, true),
        };

        internal static int RollLocalTier(Random rng)
        {
            return rng.Next(MinTier, MaxTier + 1);
        }

        internal static int RollRegionalTier(Random rng)
        {
            return rng.Next(MinPlayerRepForRegional, MaxTier + 1);
        }

        internal static bool TryGetTier(int tier, GenerationConfig cfg, out RouteTierSpec spec)
        {
            if (tier < MinTier)
            {
                spec = default;
                return false;
            }

            if (tier >= MaxTier)
            {
                spec = new RouteTierSpec(
                    MaxTier,
                    cfg.HopsMin,
                    cfg.HopsMax,
                    Tier5BudgetMax,
                    Tier5MaxWeightLb,
                    Tier5MaxVolumeCuft,
                    canBeRegional: true);
                return true;
            }

            foreach (var row in FixedTiers)
            {
                if (row.Level == tier)
                {
                    spec = row;
                    return true;
                }
            }

            spec = default;
            return false;
        }

        internal static RouteRepLimits ToLimits(RouteTierSpec spec)
        {
            return new RouteRepLimits(
                canAccessRoutes: true,
                allowRegional: spec.CanBeRegional,
                hopsMin: spec.HopsMin,
                hopsMax: spec.HopsMax,
                budgetMax: spec.BudgetMax,
                maxWeightLb: spec.MaxWeightLb,
                maxVolumeCuft: spec.MaxVolumeCuft);
        }

        internal static RouteRepLimits ForRouteTier(int routeTier, GenerationConfig cfg)
        {
            if (!TryGetTier(routeTier, cfg, out var spec))
                return new RouteRepLimits(false, false, 0, 0, 0, null, null);
            return ToLimits(spec);
        }

        /// <summary>Minimum player reputation needed to purchase this manifest.</summary>
        internal static int RequiredPlayerRep(RouteOffer offer)
        {
            if (offer == null)
                return MinPlayerRepToAccess;
            var routeTier = offer.RouteTier > 0 ? offer.RouteTier : MinTier;
            if (offer.Kind == "regional")
                return Math.Max(routeTier, MinPlayerRepForRegional);
            return Math.Max(routeTier, MinPlayerRepToAccess);
        }

        internal static bool PlayerCanAccessOffer(int playerRepLevel, RouteOffer offer, GenerationConfig cfg)
        {
            if (playerRepLevel < MinPlayerRepToAccess || offer == null)
                return false;
            return playerRepLevel >= RequiredPlayerRep(offer);
        }
    }
}
