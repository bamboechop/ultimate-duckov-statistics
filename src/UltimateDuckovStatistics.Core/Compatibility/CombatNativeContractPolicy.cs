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
        GrenadeExplosion = true,
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
            DamageDealt = Coverage(support.HealthHurt, completeOwnership, "Observed health loss with proven player ownership.", "Some source paths are unavailable; independently proven player damage continues."),
            DamageReceived = Availability(support.HealthHurt, "Health.Hurt pre/post CurrentHealth delta for Health.IsMainCharacterHealth.", "Exact Health.Hurt is unavailable."),
            RangedHits = Availability(ranged, "One unique player projectile that causes positive actual enemy HP loss counts once.", "Complete ranged-hit attribution requires Health.Hurt and Projectile.Init/Update/Release."),
            Accuracy = Availability(ranged, "Unique player projectiles causing positive enemy HP loss divided by completed player projectiles.", "Compatible accuracy requires Health.Hurt and Projectile.Init/Update/Release."),
            MeleeSwings = Availability(support.PublicMeleeSwing, "CharacterMainControl.attackAction.OnAttack proves an accepted main-duck melee attack action.", "The public melee action callback is unavailable."),
            MeleeHits = Availability(meleeHit, "One accepted melee damage scope causing positive enemy HP loss counts once.", "Melee hits require Health.Hurt and the melee collision scope."),
            PlayerDeaths = Availability(support.PublicPlayerDeath, "LevelManager.OnMainCharacterDead proves one main-duck death per run.", "The public main-character death callback is unavailable."),
            Ownership = Coverage(support.HealthHurt, completeOwnership, "Agreed event-time actor evidence; ambiguous actors remain Unknown.", "Some actor paths are unavailable; independently proven ownership continues."),
            EnemyIdentity = Availability(deathOrHealth, "CharacterRandomPreset.nameKey with stable preset/name fallback.", "Enemy/killer identity requires Health.Hurt or the public player-death callback."),
            EnemyFamily = Availability(support.HealthHurt, "Health.isZombie exposes the Zombie family; other families remain explicitly unknown.", "Enemy family requires exact Health.Hurt."),
            Cause = Availability(deathOrHealth, "Effect, explosion, real-damage, environmental, or direct context attached to proven health/death evidence.", "Damage cause requires Health.Hurt or the public player-death callback."),
            WeaponIdentity = Coverage(support.HealthHurt, completeOwnership, "Event-time native weapon or proven source snapshot.", "Some source paths are unavailable; independently proven weapon identity continues."),
            AmmunitionIdentity = Availability(projectileDamage || (support.PublicPlayerDeath && support.ProjectileInit && support.ProjectileUpdate), "ProjectileContext ammunition snapshot retained on damage, completion, and death outcomes.", "Ammunition identity requires Projectile.Init/Update plus proven health or death evidence."),
            DamageOverTime = Coverage(effectDamage, effectDamage && support.BuffApplication, "Observed effect scope with proven actor ownership.", "Buff provenance is unavailable; independently proven effect observations continue."),
            Headshots = Availability(projectileDamage && support.HeadshotEvidence, "Native launch-time head aim or the verified active First Person Camera target-specific head-hit result; generic critical damage is never headshot evidence.", "Headshots require Health.Hurt, Projectile.Init/Update and trusted native or First Person Camera headshot evidence."),
            HeadshotFinalBlows = Availability(projectileDamage && support.HeadshotEvidence, "A proven player-owned headshot on the same target's fatal Health.Hurt transition.", "Headshot final blows require Health.Hurt, Projectile.Init/Update and trusted native or First Person Camera headshot evidence."),
            ThrowableKills = Coverage(support.HealthHurt && support.GrenadeExplosion, completeOwnership && support.GrenadeExplosion, "Proven player final blows during item-sourced grenade damage.", "Some source paths are unavailable; independently proven grenade kills continue."),
            KillsByYou = Coverage(support.HealthHurt, completeOwnership, "Fatal enemy health transitions with proven player ownership.", "Some source paths are unavailable; independently proven player kills continue."),
            ObservedWorldDeaths = Availability(
                support.HealthHurt,
                "Observed fatal enemy transitions remain separate from player kills; ambiguous actor evidence stays Unknown.",
                "Observed-world deaths require the native health transition observer.")
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
        ThrowableKills = Unavailable(detail),
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
            Record(CombatCapabilityIds.ThrowableKills, capabilities.ThrowableKills, adapterVersion),
            Record(CombatCapabilityIds.KillsByYou, capabilities.KillsByYou, adapterVersion),
            Record(CombatCapabilityIds.ObservedWorldDeaths, capabilities.ObservedWorldDeaths, adapterVersion)
        };

    private static MetricAvailability Supported(string provenance) => new()
    { State = AdapterCapabilityState.Supported, Provenance = provenance };

    private static MetricAvailability Coverage(bool observable, bool complete, string full, string partial) =>
        !observable ? Unavailable("The required native observation is unavailable.")
            : new() { State = complete ? AdapterCapabilityState.Supported : AdapterCapabilityState.Experimental,
                Provenance = complete ? full : partial };

    // Coverage describes the adapter's available paths. A particular event can
    // still prove these facts; this never upgrades historical aggregate coverage.
    public static void MarkProvenHealthOutcome(CombatRecorded value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (value.Ownership != CombatOwnership.Unknown)
            value.Capabilities.Ownership = Supported("Agreed event-time source evidence.");
        if (value.Ownership == CombatOwnership.Player && value.ActualDamageDealt > 0)
            value.Capabilities.DamageDealt = Supported("Observed health loss attributed to the proven player source.");
        if (value.Ownership == CombatOwnership.Player && value.KillsByYou > 0)
            value.Capabilities.KillsByYou = Supported("Observed fatal health transition attributed to the proven player source.");
    }

    private static MetricAvailability Unavailable(string provenance) => new()
    { State = AdapterCapabilityState.DisabledIncompatible, Provenance = provenance };

    private static MetricAvailability Availability(bool supported, string supportedDetail, string unavailableDetail) =>
        supported ? Supported(supportedDetail) : Unavailable(unavailableDetail);

    private static CapabilityRecord Record(string id, MetricAvailability value, string version) => new()
    { AdapterId = id, State = value.State, Version = version, Detail = value.Provenance };
}

public sealed record class CombatHookSupport
{
    public bool BarrelExplosion { get; set; }
    public bool HeadshotEvidence { get; set; } = true;
    public bool GrenadeLaunch { get; set; }
    public bool GrenadeObjectCreation { get; set; }
    public bool GrenadeExplosion { get; set; }
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
