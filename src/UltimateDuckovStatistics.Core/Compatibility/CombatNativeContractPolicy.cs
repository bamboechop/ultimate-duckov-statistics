using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Compatibility;

public static class CombatNativeContractPolicy
{
    public static CombatMetricCapabilities CreateSupportedCapabilities() => new()
    {
        DamageDealt = Supported("Health.Hurt pre/post CurrentHealth delta for exact non-player targets owned by the main duck."),
        DamageReceived = Supported("Health.Hurt pre/post CurrentHealth delta for Health.IsMainCharacterHealth."),
        RangedHits = Supported("One unique player projectile that causes positive actual enemy HP loss counts once."),
        Accuracy = Supported("Unique player projectiles causing positive enemy HP loss divided by completed player projectiles."),
        MeleeSwings = Supported("CharacterMainControl.attackAction.OnAttack proves an accepted main-duck melee attack action."),
        MeleeHits = Supported("One accepted melee damage scope causing positive enemy HP loss counts once."),
        EnemiesKilled = Supported("The Health.Hurt transition from positive HP to dead proves the final blow."),
        PlayerDeaths = Supported("The main Health.Hurt transition to dead, reconciled before run finalization."),
        Ownership = Supported("Exact main character, built-in pet/master chain, null environmental source, otherwise unknown."),
        EnemyIdentity = Supported("CharacterRandomPreset.nameKey with stable preset/name fallback."),
        EnemyFamily = Supported("Health.isZombie exposes the Zombie family; other families remain explicitly unknown."),
        Cause = Supported("Tick/update effect scope, effect marker, explosion marker, real-damage type, environmental ownership, or direct fallback."),
        WeaponIdentity = Supported("DamageInfo.fromWeaponItemID or projectile initialization snapshot at event time."),
        AmmunitionIdentity = Supported("ProjectileContext.fromGunItemSetting.TargetBulletID snapshot at projectile initialization; unavailable on uncorrelated damage."),
        DamageOverTime = Supported("ItemStatsSystem TickTrigger/UpdateTrigger scope independently proves repeated effect damage."),
        Headshots = Supported("InputManager.AimingEnemyHead sampled for an exact player projectile; DamageInfo.crit alone is never used."),
        HeadshotFinalBlows = Supported("A proven head-targeted projectile that performs the fatal Health.Hurt transition.")
    };

    public static CombatMetricCapabilities CreateUnavailableCapabilities(string detail) => new()
    {
        DamageDealt = Unavailable(detail),
        DamageReceived = Unavailable(detail),
        RangedHits = Unavailable(detail),
        Accuracy = Unavailable(detail),
        MeleeSwings = Unavailable(detail),
        MeleeHits = Unavailable(detail),
        EnemiesKilled = Unavailable(detail),
        PlayerDeaths = Unavailable(detail),
        Ownership = Unavailable(detail),
        EnemyIdentity = Unavailable(detail),
        EnemyFamily = Unavailable(detail),
        Cause = Unavailable(detail),
        WeaponIdentity = Unavailable(detail),
        AmmunitionIdentity = Unavailable(detail),
        DamageOverTime = Unavailable(detail),
        Headshots = Unavailable(detail),
        HeadshotFinalBlows = Unavailable(detail)
    };

    public static IReadOnlyList<CapabilityRecord> ToRecords(
        CombatMetricCapabilities capabilities,
        string adapterVersion) => new[]
        {
            Record(CombatCapabilityIds.DamageDealt, capabilities.DamageDealt, adapterVersion),
            Record(CombatCapabilityIds.DamageReceived, capabilities.DamageReceived, adapterVersion),
            Record(CombatCapabilityIds.RangedHits, capabilities.RangedHits, adapterVersion),
            Record(CombatCapabilityIds.Accuracy, capabilities.Accuracy, adapterVersion),
            Record(CombatCapabilityIds.MeleeSwings, capabilities.MeleeSwings, adapterVersion),
            Record(CombatCapabilityIds.MeleeHits, capabilities.MeleeHits, adapterVersion),
            Record(CombatCapabilityIds.EnemiesKilled, capabilities.EnemiesKilled, adapterVersion),
            Record(CombatCapabilityIds.PlayerDeaths, capabilities.PlayerDeaths, adapterVersion),
            Record(CombatCapabilityIds.Ownership, capabilities.Ownership, adapterVersion),
            Record(CombatCapabilityIds.EnemyIdentity, capabilities.EnemyIdentity, adapterVersion),
            Record(CombatCapabilityIds.EnemyFamily, capabilities.EnemyFamily, adapterVersion),
            Record(CombatCapabilityIds.Cause, capabilities.Cause, adapterVersion),
            Record(CombatCapabilityIds.WeaponIdentity, capabilities.WeaponIdentity, adapterVersion),
            Record(CombatCapabilityIds.AmmunitionIdentity, capabilities.AmmunitionIdentity, adapterVersion),
            Record(CombatCapabilityIds.DamageOverTime, capabilities.DamageOverTime, adapterVersion),
            Record(CombatCapabilityIds.Headshots, capabilities.Headshots, adapterVersion),
            Record(CombatCapabilityIds.HeadshotFinalBlows, capabilities.HeadshotFinalBlows, adapterVersion)
        };

    private static MetricAvailability Supported(string provenance) => new()
    { State = AdapterCapabilityState.Supported, Provenance = provenance };

    private static MetricAvailability Unavailable(string provenance) => new()
    { State = AdapterCapabilityState.DisabledIncompatible, Provenance = provenance };

    private static CapabilityRecord Record(string id, MetricAvailability value, string version) => new()
    { AdapterId = id, State = value.State, Version = version, Detail = value.Provenance };
}
