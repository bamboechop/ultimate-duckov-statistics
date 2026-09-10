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

    public static double? Overall(CombatMetricTotals totals, CombatMetricCapabilities capabilities, bool repaired,
        WeaponMetricTotals firing, WeaponMetricCapabilities firingCapabilities, bool firingRepaired)
    {
        // An exact zero attempt count proves an unused family's contribution even if its hit hook is unavailable.
        // A missing/disabled attempt counter does not prove zero. Firing actions are used only for this zero proof,
        // never as a replacement for the completed-projectile denominator.
        var rangedComplete = Supported(capabilities.Accuracy)
            || !firingRepaired && Supported(firingCapabilities.FiringActions) && firing.FiringActions == 0
                && totals.CompletedPlayerProjectiles == 0 && totals.RangedHits == 0;
        var meleeComplete = Supported(capabilities.MeleeSwings)
            && (Supported(capabilities.MeleeHits) || totals.MeleeSwings == 0 && totals.MeleeHits == 0);
        return Ratio((decimal)totals.RangedHits + totals.MeleeHits,
            (decimal)totals.CompletedPlayerProjectiles + totals.MeleeSwings,
            !repaired && rangedComplete && meleeComplete);
    }

    /// <summary>Complete matching-scope counts only. Ratios may exceed one for multi-projectile firing actions.</summary>
    public static double? Ratio(decimal hits, decimal attempts, bool complete) =>
        complete && hits >= 0 && attempts > 0 ? (double)hits / (double)attempts : null;

    private static bool Supported(MetricAvailability value) => value.State == AdapterCapabilityState.Supported;
}
