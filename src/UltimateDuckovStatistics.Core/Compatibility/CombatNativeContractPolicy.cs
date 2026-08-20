using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Compatibility;

public static class CombatNativeContractPolicy
{
    public static CombatMetricCapabilities CreateSupportedCapabilities() => CreateCapabilities(new CombatHookSupport
    {
        HealthHurt = true,
        ProjectileInit = true,
        ProjectileUpdate = true,
        ProjectileRelease = true,
        MeleeCheck = true,
        EffectTrigger = true,
        EffectApplication = true,
        BuffApplication = true,
        EnvironmentalDamage = true,
        PublicMeleeSwing = true,
        PublicPlayerDeath = true
    });

    public static CombatMetricCapabilities CreateCapabilities(CombatHookSupport support)
    {
        if (support == null) throw new ArgumentNullException(nameof(support));
        var ranged = support.HealthHurt && support.ProjectileInit && support.ProjectileUpdate && support.ProjectileRelease;
        var meleeHit = support.HealthHurt && support.MeleeCheck;
        var deathOrHealth = support.HealthHurt || support.PublicPlayerDeath;
        var projectileDamage = support.HealthHurt && support.ProjectileInit && support.ProjectileUpdate;
        var effectDamage = deathOrHealth && support.EffectTrigger;
        var completeOwnership = support.HealthHurt && support.BuffApplication && support.EffectTrigger;
        return new CombatMetricCapabilities
        {
            DamageDealt = Availability(completeOwnership, "Health.Hurt pre/post CurrentHealth delta for exact enemy targets owned by the main duck, with trusted buff actor and effect-scope observation.", "Damage dealt requires exact Health.Hurt plus trusted buff actor and effect-scope observation."),
            DamageReceived = Availability(support.HealthHurt, "Health.Hurt pre/post CurrentHealth delta for Health.IsMainCharacterHealth.", "Exact Health.Hurt is unavailable."),
            RangedHits = Availability(ranged, "One unique player projectile that causes positive actual enemy HP loss counts once.", "Complete ranged-hit attribution requires Health.Hurt and Projectile.Init/Update/Release."),
            Accuracy = Availability(ranged, "Unique player projectiles causing positive enemy HP loss divided by completed player projectiles.", "Compatible accuracy requires Health.Hurt and Projectile.Init/Update/Release."),
            MeleeSwings = Availability(support.PublicMeleeSwing, "CharacterMainControl.attackAction.OnAttack proves an accepted main-duck melee attack action.", "The public melee action callback is unavailable."),
            MeleeHits = Availability(meleeHit, "One accepted melee damage scope causing positive enemy HP loss counts once.", "Melee hits require Health.Hurt and the melee collision scope."),
            EnemiesKilled = Unavailable("Schema-11 replaced the ambiguous enemies-killed metric."),
            PlayerDeaths = Availability(support.PublicPlayerDeath, "LevelManager.OnMainCharacterDead proves one main-duck death per run.", "The public main-character death callback is unavailable."),
            Ownership = Availability(completeOwnership, "Exact main character, credited/physical projectile actor, native pet/master/leader chain, proven actorless world scope, or explicit OtherNpc/Unknown, with trusted buff actor and effect-scope observation.", "Ownership requires exact Health.Hurt plus trusted buff actor and effect-scope observation."),
            EnemyIdentity = Availability(deathOrHealth, "CharacterRandomPreset.nameKey with stable preset/name fallback.", "Enemy/killer identity requires Health.Hurt or the public player-death callback."),
            EnemyFamily = Availability(support.HealthHurt, "Health.isZombie exposes the Zombie family; other families remain explicitly unknown.", "Enemy family requires exact Health.Hurt."),
            Cause = Availability(deathOrHealth, "Effect, explosion, real-damage, environmental, or direct context attached to proven health/death evidence.", "Damage cause requires Health.Hurt or the public player-death callback."),
            WeaponIdentity = Availability(completeOwnership, "DamageInfo.fromWeaponItemID or projectile initialization snapshot at event time, with trusted buff reapplication and effect-scope observation.", "Complete weapon identity requires exact Health.Hurt plus trusted buff actor and effect-scope observation."),
            AmmunitionIdentity = Availability(projectileDamage || (support.PublicPlayerDeath && support.ProjectileInit && support.ProjectileUpdate), "ProjectileContext ammunition snapshot retained on damage, completion, and death outcomes.", "Ammunition identity requires Projectile.Init/Update plus proven health or death evidence."),
            DamageOverTime = Availability(effectDamage && support.BuffApplication, "ItemStatsSystem TickTrigger/UpdateTrigger scope proves repeated effect damage; trusted buff application preserves actor and originating-equipment evidence.", "Damage over time requires an effect scope, proven health/death evidence, and trusted buff actor observation."),
            Headshots = Availability(projectileDamage, "InputManager.AimingEnemyHead sampled for an exact player projectile; DamageInfo.crit alone is never used.", "Headshots require Health.Hurt and Projectile.Init/Update."),
            HeadshotFinalBlows = Availability(projectileDamage, "A proven player-owned head-targeted projectile that performs the fatal Health.Hurt transition.", "Headshot final blows require Health.Hurt and Projectile.Init/Update."),
            KillsByYou = Availability(completeOwnership, "Only a fatal enemy Health.Hurt transition with proven player ownership and trusted buff actor and effect-scope observation counts.", "Kills by you require exact Health.Hurt plus trusted buff actor and effect-scope observation."),
            ObservedWorldDeaths = Availability(
                completeOwnership,
                "Every non-player or ambiguous fatal enemy transition remains separate from player kills; trusted buff actor/effect-scope observation and the exact ZoneDamage scope distinguish Unknown from proven actorless Environmental events.",
                "Observed-world deaths require exact Health.Hurt plus trusted buff actor and effect-scope observation.")
        };
    }

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
        HeadshotFinalBlows = Unavailable(detail),
        KillsByYou = Unavailable(detail),
        ObservedWorldDeaths = Unavailable(detail)
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
            Record(CombatCapabilityIds.HeadshotFinalBlows, capabilities.HeadshotFinalBlows, adapterVersion),
            Record(CombatCapabilityIds.KillsByYou, capabilities.KillsByYou, adapterVersion),
            Record(CombatCapabilityIds.ObservedWorldDeaths, capabilities.ObservedWorldDeaths, adapterVersion)
        };

    private static MetricAvailability Supported(string provenance) => new()
    { State = AdapterCapabilityState.Supported, Provenance = provenance };

    private static MetricAvailability Unavailable(string provenance) => new()
    { State = AdapterCapabilityState.DisabledIncompatible, Provenance = provenance };

    private static MetricAvailability Availability(bool supported, string supportedDetail, string unavailableDetail) =>
        supported ? Supported(supportedDetail) : Unavailable(unavailableDetail);

    private static CapabilityRecord Record(string id, MetricAvailability value, string version) => new()
    { AdapterId = id, State = value.State, Version = version, Detail = value.Provenance };
}

public sealed record class CombatHookSupport
{
    public bool HealthHurt { get; set; }
    public bool ProjectileInit { get; set; }
    public bool ProjectileUpdate { get; set; }
    public bool ProjectileRelease { get; set; }
    public bool MeleeCheck { get; set; }
    public bool EffectTrigger { get; set; }
    public bool EffectApplication { get; set; }
    public bool BuffApplication { get; set; }
    public bool EnvironmentalDamage { get; set; }
    public bool PublicMeleeSwing { get; set; }
    public bool PublicPlayerDeath { get; set; }
}
