using System.Reflection;
using Duckov.Buffs;
using ItemStatsSystem;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal static class CombatHarmonyBridge
{
    [ThreadStatic] private static ReferenceScopeStack<CombatNativeScope>? scopes;
    private static NativeCombatAttributionAdapter? adapter;

    public static void Attach(NativeCombatAttributionAdapter value)
    {
        adapter = value ?? throw new ArgumentNullException(nameof(value));
        scopes?.Clear();
    }

    public static void Detach(NativeCombatAttributionAdapter value)
    {
        if (ReferenceEquals(adapter, value)) adapter = null;
        scopes?.Clear();
    }

    public static CombatNativeScope? CurrentScope => scopes?.Current;

    public static void CaptureProjectile(Projectile projectile, ProjectileContext context) =>
        adapter?.CaptureProjectile(projectile, context);

    public static CombatNativeScope? PushProjectile(Projectile projectile) => Push(adapter?.CreateProjectileScope(projectile));

    public static void CompleteProjectile(Projectile projectile) => adapter?.CompleteProjectile(projectile);

    public static CombatNativeScope? PushMelee(ItemAgent_MeleeWeapon weapon, bool dealDamage) =>
        dealDamage ? Push(adapter?.CreateMeleeScope(weapon)) : null;

    public static CombatNativeScope? PushEffect(EffectTriggerEventContext context) =>
        Push(adapter?.CreateEffectScope(context));

    public static CombatNativeScope? PushEnvironmentalDamage() =>
        Push(adapter?.CreateEnvironmentalScope());

    public static void CaptureBuffApplication(CharacterBuffManager manager, Buff buffPrefab) =>
        adapter?.CaptureBuffApplication(manager, buffPrefab);

    public static void CaptureEffectApplication(Effect effect) =>
        adapter?.CaptureEffectApplication(effect);

    public static CombatHealthPatchState BeginHealth(Health health, DamageInfo damageInfo)
    {
        var current = adapter;
        if (current == null || !current.CanObserveHealth || health == null)
        {
            return CombatHealthPatchState.Empty;
        }

        try
        {
            NativeHotPathDiagnostics.CountHealthPrefix();
            var scope = CurrentScope;
            return new CombatHealthPatchState(
                health.CurrentHealth,
                health.IsDead,
                damageInfo,
                scope,
                scope?.EquipmentAssociation ?? current.CaptureEquipmentAssociation());
        }
        catch
        {
            return CombatHealthPatchState.Empty;
        }
    }

    public static void CompleteHealth(Health health, CombatHealthPatchState? state)
    {
        var current = adapter;
        if (current == null || state == null || !state.ShouldMeasure || !current.CanObserveHealth)
        {
            return;
        }

        try
        {
            var actual = CombatObservationPolicy.CalculateActualHealthLoss(state.HealthBefore, health.CurrentHealth);
            if (actual > 0 || (!state.WasDead && health.IsDead))
            {
                current.RecordHealthTransition(health, state, actual, !state.WasDead && health.IsDead);
            }
        }
        catch
        {
            // Contract failures must disable attribution at the adapter boundary,
            // never invent damage from requested values.
        }
    }

    public static void Pop(CombatNativeScope? scope)
    {
        scopes?.Pop(scope);
    }

    public static void ClearScopes() => scopes?.Clear();

    private static CombatNativeScope? Push(CombatNativeScope? scope)
    {
        if (scope == null) return null;
        scopes ??= new ReferenceScopeStack<CombatNativeScope>();
        return scopes.Push(scope);
    }
}

internal sealed class CombatNativeScope
{
    public string? ProjectileId { get; set; }
    public string SourceMapId { get; set; } = MapIdentity.UnknownId;
    public string SourceSegmentId { get; set; } = string.Empty;
    public CharacterMainControl? PhysicalSource { get; set; }
    public CharacterMainControl? CreditedSource { get; set; }
    public bool NativePlayerOwnerChain { get; set; }
    public bool ExplicitActorlessWorldDamage { get; set; }
    public bool IsRanged { get; set; }
    public bool IsMelee { get; set; }
    public bool IsDamageOverTime { get; set; }
    public bool IsEffect { get; set; }
    public bool HeadTargeted { get; set; }
    public int WeaponTypeId { get; set; } = -1;
    public string WeaponDisplayName { get; set; } = string.Empty;
    public int AmmunitionTypeId { get; set; } = -1;
    public string AmmunitionDisplayName { get; set; } = string.Empty;
    public bool HitCounted { get; set; }
    public bool HeadshotCounted { get; set; }
    public bool HeadshotFinalBlowCounted { get; set; }
    public EquipmentEventAssociation EquipmentAssociation { get; set; } = new();
}

internal sealed class CombatHealthPatchState
{
    public static readonly CombatHealthPatchState Empty = new(0, true, default, null, new EquipmentEventAssociation(), false);

    public CombatHealthPatchState(
        double healthBefore,
        bool wasDead,
        DamageInfo damageInfo,
        CombatNativeScope? scope,
        EquipmentEventAssociation equipmentAssociation,
        bool shouldMeasure = true)
    {
        HealthBefore = healthBefore;
        WasDead = wasDead;
        DamageInfo = damageInfo;
        Scope = scope;
        EquipmentAssociation = equipmentAssociation ?? new EquipmentEventAssociation();
        ShouldMeasure = shouldMeasure;
    }

    public double HealthBefore { get; }
    public bool WasDead { get; }
    public DamageInfo DamageInfo { get; }
    public CombatNativeScope? Scope { get; }
    public EquipmentEventAssociation EquipmentAssociation { get; }
    public bool ShouldMeasure { get; }
}

internal static class CombatHarmonyCallbacks
{
    private static void HealthPrefix(Health __instance, DamageInfo damageInfo, out CombatHealthPatchState __state) =>
        __state = CombatHarmonyBridge.BeginHealth(__instance, damageInfo);

    private static void HealthPostfix(Health __instance, CombatHealthPatchState __state) =>
        CombatHarmonyBridge.CompleteHealth(__instance, __state);

    private static void ProjectileInitPostfix(Projectile __instance, ProjectileContext _context) =>
        CombatHarmonyBridge.CaptureProjectile(__instance, _context);

    private static void ProjectileUpdatePrefix(Projectile __instance, out CombatNativeScope? __state) =>
        __state = CombatHarmonyBridge.PushProjectile(__instance);

    private static Exception? ProjectileUpdateFinalizer(Exception? __exception, CombatNativeScope? __state)
    {
        CombatHarmonyBridge.Pop(__state);
        return __exception;
    }

    private static void ProjectileReleasePrefix(Projectile __instance) =>
        CombatHarmonyBridge.CompleteProjectile(__instance);

    private static void MeleePrefix(ItemAgent_MeleeWeapon __instance, bool dealDamage, out CombatNativeScope? __state) =>
        __state = CombatHarmonyBridge.PushMelee(__instance, dealDamage);

    private static Exception? MeleeFinalizer(Exception? __exception, CombatNativeScope? __state)
    {
        CombatHarmonyBridge.Pop(__state);
        return __exception;
    }

    private static void EffectPrefix(EffectTriggerEventContext context, out CombatNativeScope? __state) =>
        __state = CombatHarmonyBridge.PushEffect(context);

    private static Exception? EffectFinalizer(Exception? __exception, CombatNativeScope? __state)
    {
        CombatHarmonyBridge.Pop(__state);
        return __exception;
    }

    private static void EffectApplicationPostfix(Effect __instance) =>
        CombatHarmonyBridge.CaptureEffectApplication(__instance);

    private static void EnvironmentalDamagePrefix(out CombatNativeScope? __state) =>
        __state = CombatHarmonyBridge.PushEnvironmentalDamage();

    private static Exception? EnvironmentalDamageFinalizer(Exception? __exception, CombatNativeScope? __state)
    {
        CombatHarmonyBridge.Pop(__state);
        return __exception;
    }

    public static MethodInfo HealthPrefixMethod => Get(nameof(HealthPrefix));
    public static MethodInfo HealthPostfixMethod => Get(nameof(HealthPostfix));
    public static MethodInfo ProjectileInitPostfixMethod => Get(nameof(ProjectileInitPostfix));
    public static MethodInfo ProjectileUpdatePrefixMethod => Get(nameof(ProjectileUpdatePrefix));
    public static MethodInfo ProjectileUpdateFinalizerMethod => Get(nameof(ProjectileUpdateFinalizer));
    public static MethodInfo ProjectileReleasePrefixMethod => Get(nameof(ProjectileReleasePrefix));
    public static MethodInfo MeleePrefixMethod => Get(nameof(MeleePrefix));
    public static MethodInfo MeleeFinalizerMethod => Get(nameof(MeleeFinalizer));
    public static MethodInfo EffectPrefixMethod => Get(nameof(EffectPrefix));
    public static MethodInfo EffectFinalizerMethod => Get(nameof(EffectFinalizer));
    public static MethodInfo EffectApplicationPostfixMethod => Get(nameof(EffectApplicationPostfix));
    public static MethodInfo EnvironmentalDamagePrefixMethod => Get(nameof(EnvironmentalDamagePrefix));
    public static MethodInfo EnvironmentalDamageFinalizerMethod => Get(nameof(EnvironmentalDamageFinalizer));

    private static MethodInfo Get(string name) => typeof(CombatHarmonyCallbacks).GetMethod(
        name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(CombatHarmonyCallbacks).FullName, name);
}
