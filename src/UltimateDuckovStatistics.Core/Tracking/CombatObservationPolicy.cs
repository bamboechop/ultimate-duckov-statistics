using System.Globalization;
using System.Text;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Tracking;

public readonly struct CombatActorEvidence
{
    public CombatActorEvidence(CombatActorEvidenceKind kind, int identity)
    {
        Kind = kind;
        Identity = identity;
    }

    public CombatActorEvidenceKind Kind { get; }
    public int Identity { get; }
    public bool IsPresent => Kind != CombatActorEvidenceKind.Missing;
    public static CombatActorEvidence Missing => default;
}

public readonly struct CombatDeathClassification
{
    public CombatDeathClassification(long killsByYou, long observedWorldDeaths)
    {
        KillsByYou = killsByYou;
        ObservedWorldDeaths = observedWorldDeaths;
    }

    public long KillsByYou { get; }
    public long ObservedWorldDeaths { get; }
}

public readonly struct CombatProjectileTransition
{
    public CombatProjectileTransition(bool rangedHit, bool headshot, bool headshotFinalBlow)
    {
        RangedHit = rangedHit;
        Headshot = headshot;
        HeadshotFinalBlow = headshotFinalBlow;
    }

    public bool RangedHit { get; }
    public bool Headshot { get; }
    public bool HeadshotFinalBlow { get; }
}

public static class CombatObservationPolicy
{
    public static CombatOwnership ResolveOwnership(
        CombatActorEvidence physicalActor,
        CombatActorEvidence creditedActor,
        CombatActorEvidence damageActor,
        bool nativePlayerOwnerChain,
        bool explicitActorlessWorldDamage,
        bool conflictingActorEvidence = false)
    {
        if (conflictingActorEvidence) return CombatOwnership.Unknown;
        if (explicitActorlessWorldDamage)
        {
            return physicalActor.IsPresent || creditedActor.IsPresent || damageActor.IsPresent
                ? CombatOwnership.Unknown
                : CombatOwnership.Environmental;
        }
        if (!physicalActor.IsPresent && !creditedActor.IsPresent && !damageActor.IsPresent)
            return CombatOwnership.Unknown;

        if (nativePlayerOwnerChain
            && creditedActor.Kind == CombatActorEvidenceKind.Player
            && damageActor.Kind is CombatActorEvidenceKind.Missing or CombatActorEvidenceKind.Player)
        {
            return CombatOwnership.Player;
        }

        var first = physicalActor.IsPresent
            ? physicalActor
            : creditedActor.IsPresent ? creditedActor : damageActor;
        if ((physicalActor.IsPresent && !SameActor(first, physicalActor))
            || (creditedActor.IsPresent && !SameActor(first, creditedActor))
            || (damageActor.IsPresent && !SameActor(first, damageActor))) return CombatOwnership.Unknown;

        return first.Kind switch
        {
            CombatActorEvidenceKind.Player => CombatOwnership.Player,
            CombatActorEvidenceKind.Companion => CombatOwnership.PetCompanion,
            CombatActorEvidenceKind.OtherNpc => CombatOwnership.OtherNpc,
            _ => CombatOwnership.Unknown
        };
    }

    public static CombatOwnership ResolveHealthTransitionOwnership(
        CombatActorEvidence physicalActor,
        CombatActorEvidence creditedActor,
        CombatActorEvidence damageActor,
        bool nativePlayerOwnerChain,
        bool explicitActorlessWorldDamage,
        bool conflictingActorEvidence,
        bool ownershipScopePresent,
        bool effectScopeObservationTrusted)
    {
        if (!ownershipScopePresent && !effectScopeObservationTrusted)
        {
            return CombatOwnership.Unknown;
        }

        return ResolveOwnership(
            physicalActor,
            creditedActor,
            damageActor,
            nativePlayerOwnerChain,
            explicitActorlessWorldDamage,
            conflictingActorEvidence);
    }

    public static int ResolveHealthTransitionWeaponTypeId(
        int scopedWeaponTypeId,
        int nativeWeaponTypeId,
        bool ownershipScopePresent,
        bool effectScopeObservationTrusted)
    {
        if (!ownershipScopePresent && !effectScopeObservationTrusted)
        {
            return -1;
        }

        return scopedWeaponTypeId >= 0 ? scopedWeaponTypeId : nativeWeaponTypeId;
    }

    public static CombatDeathClassification ClassifyEnemyDeath(
        bool enemyTarget,
        bool fatalTransition,
        CombatOwnership ownership)
    {
        if (!enemyTarget || !fatalTransition) return default;
        return ownership == CombatOwnership.Player
            ? new CombatDeathClassification(killsByYou: 1, observedWorldDeaths: 0)
            : new CombatDeathClassification(killsByYou: 0, observedWorldDeaths: 1);
    }

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
        bool headshotOnCurrentTransition,
        bool exactPlayerOwnership,
        bool enemyTarget,
        bool fatalTransition,
        bool alreadyCounted) =>
        headshotOnCurrentTransition && exactPlayerOwnership && enemyTarget && fatalTransition && !alreadyCounted;

    public static CombatProjectileTransition ClassifyProjectileTransition(
        bool headTargetedProjectile,
        bool nativeCritical,
        bool exactPlayerOwnership,
        bool enemyTarget,
        bool rangedScope,
        bool fatalTransition,
        bool hitAlreadyCounted,
        bool headshotAlreadyCounted,
        bool headshotFinalBlowAlreadyCounted)
    {
        var rangedHit = CountRangedHit(
            enemyTarget, exactPlayerOwnership, rangedScope, hitAlreadyCounted);
        var headshot = CountHeadshot(
            headTargetedProjectile, nativeCritical, rangedHit, headshotAlreadyCounted);
        var headshotFinalBlow = CountHeadshotFinalBlow(
            headshot, exactPlayerOwnership, enemyTarget, fatalTransition, headshotFinalBlowAlreadyCounted);
        return new CombatProjectileTransition(rangedHit, headshot, headshotFinalBlow);
    }

    public static bool ShouldRecordHealthTransition(
        bool targetIsMain, bool targetIsEnemy, CombatOwnership ownership)
    {
        _ = ownership;
        return targetIsMain || targetIsEnemy;
    }

    public static string OwnershipDisplayName(CombatOwnership ownership) => ownership switch
    {
        CombatOwnership.Player => "Player",
        CombatOwnership.PetCompanion => "Companion",
        CombatOwnership.OtherNpc => "Other NPC",
        CombatOwnership.Environmental => "Environmental",
        _ => "Unknown"
    };

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
    private static bool SameActor(CombatActorEvidence left, CombatActorEvidence right) =>
        left.Kind == right.Kind && left.Identity == right.Identity;
}
