using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

/// <summary>Presentation ratios; persisted projectile accuracy and counters keep their original meanings.</summary>
public static class CombatAccuracyProjection
{
    public static double? Ranged(CombatMetricTotals totals, CombatMetricCapabilities capabilities, bool repaired) =>
        Ratio(totals.RangedHits, totals.CompletedPlayerProjectiles,
            !repaired && Supported(capabilities.Accuracy));

    public static double? Melee(CombatMetricTotals totals, CombatMetricCapabilities capabilities, bool repaired) =>
        Ratio(totals.MeleeHits, totals.MeleeSwings,
            !repaired && Supported(capabilities.MeleeSwings) && Supported(capabilities.MeleeHits));

    public static double? Overall(CombatMetricTotals totals, CombatMetricCapabilities capabilities, bool repaired)
    {
        // Zero attempts with complete capture contribute zero, without requiring a standalone percentage.
        // Main-duck action counters cannot prove absent player-credited hits: controlled NPCs can contribute
        // hits without those callbacks. Missing hit/projectile evidence must therefore remain unavailable.
        return Ratio((decimal)totals.RangedHits + totals.MeleeHits,
            (decimal)totals.CompletedPlayerProjectiles + totals.MeleeSwings,
            !repaired && Supported(capabilities.Accuracy) && Supported(capabilities.MeleeSwings)
                && Supported(capabilities.MeleeHits));
    }

    /// <summary>Complete matching-scope counts only. Ratios may exceed one for multi-projectile firing actions.</summary>
    public static double? Ratio(decimal hits, decimal attempts, bool complete) =>
        complete && hits >= 0 && attempts > 0 ? (double)hits / (double)attempts : null;

    private static bool Supported(MetricAvailability value) => value.State == AdapterCapabilityState.Supported;
}
