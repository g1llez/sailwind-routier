namespace Routier
{
    /// <summary>Caps for route generation or player access (hops, budget, cargo).</summary>
    internal readonly struct RouteRepLimits
    {
        public readonly bool CanAccessRoutes;
        public readonly bool AllowRegional;
        public readonly int HopsMin;
        public readonly int HopsMax;
        public readonly int BudgetMax;
        public readonly float? MaxWeightLb;
        public readonly float? MaxVolumeCuft;

        internal RouteRepLimits(
            bool canAccessRoutes,
            bool allowRegional,
            int hopsMin,
            int hopsMax,
            int budgetMax,
            float? maxWeightLb,
            float? maxVolumeCuft)
        {
            CanAccessRoutes = canAccessRoutes;
            AllowRegional = allowRegional;
            HopsMin = hopsMin;
            HopsMax = hopsMax;
            BudgetMax = budgetMax;
            MaxWeightLb = maxWeightLb;
            MaxVolumeCuft = maxVolumeCuft;
        }

        internal int SampleBudget(System.Random rng, GenerationConfig cfg)
        {
            if (BudgetMax <= 0)
                return 0;
            var min = System.Math.Min(cfg.BudgetMin, BudgetMax);
            if (min < 1)
                min = 1;
            if (min > BudgetMax)
                return BudgetMax;
            return rng.Next(min, BudgetMax + 1);
        }
    }
}
