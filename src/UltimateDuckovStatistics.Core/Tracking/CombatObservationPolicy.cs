using System.Globalization;
using System.Text;
using UltimateDuckovStatistics.Core.Domain;

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
        bool headTargetedProjectile, bool enemyTarget, bool fatalTransition, bool alreadyCounted) =>
        headTargetedProjectile && enemyTarget && fatalTransition && !alreadyCounted;

    public static bool ShouldRecordHealthTransition(
        bool targetIsMain, bool targetIsEnemy, CombatOwnership ownership) =>
        targetIsMain || (targetIsEnemy && ownership != CombatOwnership.Unknown);

    public static bool MatchesOriginatingContext(
        string capturedGenerationId,
        string capturedRunId,
        string capturedMapId,
        string currentGenerationId,
        string currentRunId,
        string currentMapId) =>
        !string.IsNullOrWhiteSpace(capturedGenerationId)
        && !string.IsNullOrWhiteSpace(capturedRunId)
        && !string.IsNullOrWhiteSpace(capturedMapId)
        && string.Equals(capturedGenerationId, currentGenerationId, StringComparison.Ordinal)
        && string.Equals(capturedRunId, currentRunId, StringComparison.Ordinal)
        && string.Equals(capturedMapId, currentMapId, StringComparison.Ordinal);

    public static void ApplyOutcomeIdentity(
        CombatRecorded value,
        string? projectileId,
        int weaponTypeId,
        string? weaponDisplayName,
        int ammunitionTypeId,
        string? ammunitionDisplayName)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        value.ProjectileId = projectileId;
        if (weaponTypeId > 0)
        {
            value.WeaponId = $"duckov:weapon:{weaponTypeId.ToString(CultureInfo.InvariantCulture)}";
            value.WeaponDisplayName = string.IsNullOrWhiteSpace(weaponDisplayName)
                ? $"Unknown weapon {weaponTypeId.ToString(CultureInfo.InvariantCulture)}"
                : weaponDisplayName;
        }
        if (ammunitionTypeId >= 0)
        {
            value.AmmunitionId = $"duckov:ammo:{ammunitionTypeId.ToString(CultureInfo.InvariantCulture)}";
            value.AmmunitionDisplayName = string.IsNullOrWhiteSpace(ammunitionDisplayName)
                ? $"Unknown ammunition {ammunitionTypeId.ToString(CultureInfo.InvariantCulture)}"
                : ammunitionDisplayName;
        }
    }

    public static string CreateStableIdentityToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        var normalized = value.Trim().ToLowerInvariant();
        var readable = new string(normalized.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (!string.IsNullOrWhiteSpace(readable)) return readable;
        var builder = new StringBuilder("utf8-");
        foreach (var valueByte in Encoding.UTF8.GetBytes(normalized))
            builder.Append(valueByte.ToString("x2", CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
