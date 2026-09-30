using System;

namespace BlasphemousTrainer
{
    // Pure defense-mode limits shared by the UI, gameplay hooks and executable boundary tests.
    // The reduction layer is the raw incoming Hit.DamageAmount inside
    // PenitentDamageArea.RaiseDamageEvent, i.e. BEFORE elemental resistance
    // (Entity.GetReducedDamage) and flat defense (Stats.Defense.Final) are applied.
    internal static class DefenseRules
    {
        private static readonly int[] Tiers = { 25, 50, 75, 90 };

        // Snaps an arbitrary configured percent to the nearest supported tier.
        internal static int ClampTier(int percent)
        {
            int best = Tiers[0];
            foreach (int tier in Tiers) if (Math.Abs((long)tier - percent) < Math.Abs((long)best - percent)) best = tier;
            return best;
        }

        // Walks the tier list with wrap-around; unknown values fall back to 50%.
        internal static int CycleTier(int current, int step)
        {
            int index = Array.IndexOf(Tiers, current);
            if (index < 0) return Tiers[1];
            return Tiers[((index + step) % Tiers.Length + Tiers.Length) % Tiers.Length];
        }

        // Applies the reduction once at the raw layer. Non-positive and non-finite
        // values pass through untouched so the hook never fabricates damage.
        internal static float Reduce(float damage, float tier)
        {
            if (float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0f) return damage;
            return damage * (1f - tier);
        }
    }
}
