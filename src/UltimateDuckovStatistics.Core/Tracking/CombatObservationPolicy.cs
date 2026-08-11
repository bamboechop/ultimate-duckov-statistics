namespace UltimateDuckovStatistics.Core.Tracking;

public static class CombatObservationPolicy
{
    public static double CalculateActualHealthLoss(double before, double after)
    {
        if (!Finite(before) || !Finite(after)) return 0;
        return Math.Max(0, before - after);
    }

    public static bool CountRangedHit(
        bool enemyTarget, bool exactPlayerOwnership, bool rangedScope, bool alreadyCounted) =>
        enemyTarget && exactPlayerOwnership && rangedScope && !alreadyCounted;

    public static bool CountMeleeHit(
        bool enemyTarget, bool exactPlayerOwnership, bool meleeScope, bool alreadyCounted) =>
        enemyTarget && exactPlayerOwnership && meleeScope && !alreadyCounted;

    public static bool CountHeadshot(
        bool headTargetedProjectile,
        bool nativeCritical,
        bool rangedHit,
        bool alreadyCounted)
    {
        _ = nativeCritical; // Critical outcome is deliberately not headshot evidence.
        return headTargetedProjectile && rangedHit && !alreadyCounted;
    }

    public static bool CountHeadshotFinalBlow(
        bool headTargetedProjectile, bool enemyTarget, bool fatalTransition) =>
        headTargetedProjectile && enemyTarget && fatalTransition;

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
