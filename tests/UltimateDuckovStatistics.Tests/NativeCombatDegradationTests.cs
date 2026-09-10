using System.Reflection;
using ItemStatsSystem;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeHotPathDiagnosticsTestGroup.CollectionName)]
public sealed class NativeCombatDegradationTests : IDisposable
{
    private readonly List<CombatRecorded> events = new();
    private readonly NativeCombatAttributionAdapter adapter;
    private readonly CharacterMainControl player = new() { IsMainCharacter = true };
    private readonly List<string> diagnostics = new();

    public NativeCombatDegradationTests()
    {
        HarmonyLib.Harmony.ClearAll();
        Application.version = "2.3.30";
        NativeRaidContext.GameplayContext = GameplayContext.Raid;
        CharacterMainControl.Main = player;
        LevelManager.Instance = new() { MainCharacter = player, ControllingCharacter = player };
        LevelManager.LevelInitializing = false;
        GameManager.Paused = false;
        Duckov.Scenes.SceneLoader.IsSceneLoading = false;
        MultiSceneCore.Instance = null;
        var buffTrust = new NativeBuffApplicationObservationBoundary();
        buffTrust.MarkTrusted();
        adapter = new(() => "g", () => "r", () => "m", value => { events.Add(value); return true; },
            _ => { }, diagnostics.Add, buffTrust, () => new() { LoadoutId = "at-shot" });
        adapter.Initialize();
        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.Accuracy.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EffectApplicationDriftPreservesInFlightProjectileHitAndCompletion(bool hitBeforeDrift)
    {
        var projectile = Capture();
        var effect = new Effect { Item = new Item { Character = player } };
        var trigger = new TickTrigger { Master = new EffectMaster { Item = effect.Item } };
        effect.Triggers.Add(trigger);
        CombatHarmonyCallbacks.EffectApplicationPostfixMethod.Invoke(null, [effect]);
        Assert.Equal("at-shot", adapter.CreateEffectScope(new() { source = trigger })!.EquipmentAssociation.LoadoutId);
        if (hitBeforeDrift) Hit(projectile);

        AddForeignPatchAndInspect(typeof(Effect).GetMethod(nameof(Effect.SetItem))!);

        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.Accuracy.State);
        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.RangedHits.State);
        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.DamageDealt.State);
        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.PlayerDeaths.State);
        Assert.Equal(EquipmentEventAssociation.UnavailableId, adapter.CreateEffectScope(new() { source = trigger })!.EquipmentAssociation.LoadoutId);
        CombatHarmonyCallbacks.EffectApplicationPostfixMethod.Invoke(null, [effect]);
        Assert.Equal(EquipmentEventAssociation.UnavailableId, adapter.CreateEffectScope(new() { source = trigger })!.EquipmentAssociation.LoadoutId);
        if (!hitBeforeDrift) Hit(projectile);
        CombatHarmonyCallbacks.ProjectileReleasePrefixMethod.Invoke(null, [projectile]);
        CombatHarmonyCallbacks.ProjectileReleasePrefixMethod.Invoke(null, [projectile]);

        var completion = Assert.Single(events, value => value.CompletedPlayerProjectiles == 1);
        Assert.Equal(1, completion.RangedHits);
        Assert.Equal("at-shot", completion.EquipmentAssociation.LoadoutId);
        var damage = Assert.Single(events, value => value.ActualDamageDealt > 0);
        Assert.Equal(10, damage.ActualDamageDealt);
        Assert.Equal(completion.ProjectileId, damage.ProjectileId);
        Assert.Equal(CombatAttackKind.Ranged, damage.AttackKind);
        Assert.Equal(1, events.Sum(value => value.CompletedPlayerProjectiles));
    }

    [Theory]
    [InlineData("Init")]
    [InlineData("Update")]
    [InlineData("Release")]
    public void ProjectileHookDriftStillDiscardsUntrustedCorrelation(string method)
    {
        var projectile = Capture();
        Hit(projectile);
        AddForeignPatchAndInspect(typeof(Projectile).GetMethod(method,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, adapter.MetricCapabilities.Accuracy.State);
        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.PlayerDeaths.State);
        CombatHarmonyCallbacks.ProjectileReleasePrefixMethod.Invoke(null, [projectile]);
        Assert.DoesNotContain(events, value => value.CompletedPlayerProjectiles != 0);
        Assert.Null(adapter.CreateProjectileScope(projectile));
    }

    [Theory]
    [InlineData(typeof(Effect), "SetItem")]
    [InlineData(typeof(Projectile), "Init")]
    [InlineData(typeof(Projectile), "Update")]
    [InlineData(typeof(Projectile), "Release")]
    [InlineData(typeof(ItemAgent_MeleeWeapon), "CheckCollidersInRange")]
    public void UnrelatedHookDriftRetainsBuffConflictAndProvenGrenadeHazard(Type nativeType, string method)
    {
        var zone = CaptureGrenadeZone();
        Assert.Same(player, adapter.CreateEnvironmentalScope(zone)!.PhysicalSource);
        var buff = new Duckov.Buffs.Buff { ID = 1, fromWho = player };
        var manager = new CharacterBuffManager();
        manager.Buffs.Add(buff);
        CombatHarmonyBridge.CaptureBuffApplication(manager, buff, player, 42, newlyCreated: true);
        CombatHarmonyBridge.CaptureBuffApplication(manager, buff, new CharacterMainControl(), 43, newlyCreated: false);
        var context = new EffectTriggerEventContext { source = new TickTrigger { Parent = buff } };
        Assert.True(adapter.CreateEffectScope(context)!.ConflictingActorEvidence);

        AddForeignPatchAndInspect(NativeMethod(nativeType, method));

        Assert.True(adapter.CreateEffectScope(context)!.ConflictingActorEvidence);
        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.Ownership.State);
        var hazard = adapter.CreateEnvironmentalScope(zone)!;
        Assert.Same(player, hazard.PhysicalSource);
        Assert.Equal(67, hazard.WeaponTypeId);
        Assert.Equal("at-shot", hazard.EquipmentAssociation.LoadoutId);
        Assert.Equal(AdapterCapabilityState.Supported, adapter.Initialize().Single(value => value.AdapterId == "native-grenade-hazard-attribution").State);
    }

    [Theory]
    [InlineData(typeof(Health), "Hurt")]
    [InlineData(typeof(Grenade), "Launch")]
    [InlineData(typeof(Grenade), "Explode")]
    [InlineData(typeof(UnityEngine.Object), "Instantiate")]
    [InlineData(typeof(Effect), "Trigger")]
    [InlineData(typeof(ZoneDamage), "Damage")]
    public void RequiredHazardHookDriftDisablesGrenadeOriginAttribution(Type nativeType, string method)
    {
        var zone = CaptureGrenadeZone();
        Assert.Same(player, adapter.CreateEnvironmentalScope(zone)!.PhysicalSource);
        AddForeignPatchAndInspect(NativeMethod(nativeType, method));
        Assert.Null(adapter.CreateEnvironmentalScope(zone)?.GrenadeHazardOrigin);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, adapter.Initialize().Single(value => value.AdapterId == "native-grenade-hazard-attribution").State);
    }

    [Fact]
    public void HealthHookDriftDisablesHealthAndAccuracyWhilePublicPlayerDeathRemainsAvailable()
    {
        var projectile = Capture();
        AddForeignPatchAndInspect(typeof(Health).GetMethod(nameof(Health.Hurt))!);
        Assert.False(adapter.CanObserveHealth);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, adapter.MetricCapabilities.Accuracy.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, adapter.MetricCapabilities.DamageDealt.State);
        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.PlayerDeaths.State);
        CombatHarmonyCallbacks.ProjectileReleasePrefixMethod.Invoke(null, [projectile]);
        Assert.Empty(events);
        adapter.RecordPlayerDeath(new DamageInfo());
        Assert.Equal(1, Assert.Single(events).PlayerDeaths);
    }

    [Fact]
    public void ThreeAcceptedSwingsWithOneMultiContactDamageCheckCountOneMeleeHit()
    {
        player.MeleeWeapon = new ItemAgent_MeleeWeapon { Holder = player, Item = new Item { TypeID = 42 } };
        // Installed CA_Attack.OnStart emits OnAttack once; OnUpdateAction's damageDealed guard
        // admits one CheckAndDealDamage per action. The native loop can hurt multiple colliders.
        for (var swing = 0; swing < 3; swing++)
        {
            player.attackAction.RaiseAttack();
            object?[] check = [player.MeleeWeapon, true, null];
            CombatHarmonyCallbacks.MeleePrefixMethod.Invoke(null, check);
            Assert.NotNull(check[2]);
            if (swing == 1)
            {
                var first = new Health { CurrentHealth = 100, Character = new CharacterMainControl(), team = Teams.enemy };
                var second = new Health { CurrentHealth = 100, Character = new CharacterMainControl(), team = Teams.enemy };
                foreach (var target in new[] { first, first, second })
                {
                    object?[] hurt = [target, new DamageInfo { fromCharacter = player }, null];
                    CombatHarmonyCallbacks.HealthPrefixMethod.Invoke(null, hurt);
                    target.CurrentHealth -= 10;
                    CombatHarmonyCallbacks.HealthPostfixMethod.Invoke(null, [target, hurt[2]]);
                }
            }
            CombatHarmonyCallbacks.MeleeFinalizerMethod.Invoke(null, [null, check[2]]);
        }
        Assert.Equal(3, events.Sum(value => value.MeleeSwings));
        Assert.Equal(1, events.Sum(value => value.MeleeHits));
        Assert.Equal(30, events.Sum(value => value.ActualDamageDealt));
        Assert.All(events, value => Assert.Equal("duckov:weapon:42", value.WeaponId));
        var aggregate = new Core.Statistics.CombatStatisticsAggregate();
        foreach (var value in events) Core.Statistics.CombatStatisticsReducer.Apply(aggregate, value);
        Assert.Equal(1d / 3, Core.Statistics.CombatAccuracyProjection.Melee(aggregate.Totals, aggregate.Capabilities, false));
        // Native range queries use dealDamage:false, and must not create a hit scope.
        Assert.Null(CombatHarmonyBridge.PushMelee(player.MeleeWeapon, false));
    }

    [Fact]
    public void ControlledCharacterHitsDoNotProveMainDuckAttackAttempts()
    {
        // Installed gun/melee code credits the main duck when Holder == ControllingCharacter,
        // while OnMainCharacterShootEvent and the subscribed attackAction belong to the main duck only.
        var controlled = new CharacterMainControl();
        LevelManager.Instance!.ControllingCharacter = controlled;
        var weapon = new ItemAgent_MeleeWeapon { Holder = controlled, Item = new Item { TypeID = 42 } };
        controlled.attackAction.RaiseAttack();
        object?[] check = [weapon, true, null];
        CombatHarmonyCallbacks.MeleePrefixMethod.Invoke(null, check);
        var enemy = new Health { CurrentHealth = 100, Character = new CharacterMainControl(), team = Teams.enemy };
        object?[] hurt = [enemy, new DamageInfo { fromCharacter = player }, null];
        CombatHarmonyCallbacks.HealthPrefixMethod.Invoke(null, hurt);
        enemy.CurrentHealth = 90;
        CombatHarmonyCallbacks.HealthPostfixMethod.Invoke(null, [enemy, hurt[2]]);
        CombatHarmonyCallbacks.MeleeFinalizerMethod.Invoke(null, [null, check[2]]);
        var projectile = new Projectile();
        var context = new ProjectileContext { realFromCharacter = controlled, fromCharacter = player, fromWeaponItemID = 42 };
        projectile.Init(context);
        CombatHarmonyCallbacks.ProjectileInitPostfixMethod.Invoke(null, [projectile, context]);
        Hit(projectile);
        CombatHarmonyCallbacks.ProjectileReleasePrefixMethod.Invoke(null, [projectile]);
        Assert.Equal(0, events.Sum(value => value.MeleeSwings));
        Assert.Equal(1, events.Sum(value => value.MeleeHits));
        Assert.Equal(1, events.Sum(value => value.RangedHits));
        Assert.Equal(1, events.Sum(value => value.CompletedPlayerProjectiles));
        var aggregate = new Core.Statistics.CombatStatisticsAggregate();
        foreach (var value in events) Core.Statistics.CombatStatisticsReducer.Apply(aggregate, value);
        Assert.Equal(2, Core.Statistics.CombatAccuracyProjection.Overall(aggregate.Totals, aggregate.Capabilities, false));
        Assert.Null(Core.Statistics.CombatAccuracyProjection.Melee(aggregate.Totals, aggregate.Capabilities, false));
    }

    [Fact]
    public void MultipleProjectilesAndPenetratingContactsCountOncePerCompletedProjectile()
    {
        // One native gun firing action can loop ShotCount times; each Init has its own correlation.
        var first = Capture(); var second = Capture();
        Hit(first); Hit(first); Hit(second);
        Assert.Equal(0, events.Sum(value => value.RangedHits));
        foreach (var projectile in new[] { first, second, first })
            CombatHarmonyCallbacks.ProjectileReleasePrefixMethod.Invoke(null, [projectile]);
        Assert.Equal(2, events.Sum(value => value.CompletedPlayerProjectiles));
        Assert.Equal(2, events.Sum(value => value.RangedHits));
        Assert.Equal(30, events.Sum(value => value.ActualDamageDealt));
    }

    private Projectile Capture()
    {
        var projectile = new Projectile();
        var context = new ProjectileContext { realFromCharacter = player, fromCharacter = player, fromWeaponItemID = 42 };
        projectile.Init(context);
        CombatHarmonyCallbacks.ProjectileInitPostfixMethod.Invoke(null, [projectile, context]);
        Assert.NotNull(adapter.CreateProjectileScope(projectile));
        return projectile;
    }

    private static MethodInfo NativeMethod(Type nativeType, string name) => nativeType.GetMethod(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!;

    private ZoneDamage CaptureGrenadeZone()
    {
        var grenade = new Grenade
        {
            createOnExlode = new GameObject(),
            damageInfo = new DamageInfo { fromCharacter = player, fromWeaponItemID = 67, isExplosion = true }
        };
        CombatHarmonyCallbacks.GrenadeLaunchPostfixMethod.Invoke(null, [grenade, player]);
        var zone = new ZoneDamage();
        var clone = new GameObject();
        clone.Components.Add(zone);
        var explosion = NativeGrenadeAttribution.Begin(grenade);
        try { CombatHarmonyCallbacks.GrenadeClonePostfixMethod.Invoke(null, [grenade.createOnExlode, clone]); }
        finally { NativeGrenadeAttribution.End(explosion); }
        return zone;
    }

    private static void Hit(Projectile projectile)
    {
        Time.frameCount++;
        object?[] update = [projectile, null];
        CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod.Invoke(null, update);
        Assert.NotNull(update[1]);
        var enemy = new Health { CurrentHealth = 100, Character = new CharacterMainControl(), team = Teams.enemy };
        object?[] hurt = [enemy, new DamageInfo { fromCharacter = projectile.context.fromCharacter }, null];
        CombatHarmonyCallbacks.HealthPrefixMethod.Invoke(null, hurt);
        enemy.CurrentHealth = 90;
        CombatHarmonyCallbacks.HealthPostfixMethod.Invoke(null, [enemy, hurt[2]]);
        CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod.Invoke(null, [null, update[1]]);
        Assert.Null(CombatHarmonyBridge.CurrentScope);
    }

    private void AddForeignPatchAndInspect(MethodInfo method)
    {
        new HarmonyLib.Harmony("foreign.combat-test").Patch(method,
            new HarmonyLib.HarmonyMethod(typeof(NativeCombatDegradationTests).GetMethod(nameof(ForeignPrefix), BindingFlags.NonPublic | BindingFlags.Static)!),
            null, null, null);
        // Drive the actual incremental stamp-inspection method with a deterministic
        // clock; no game loop, wall-clock sleep, or private correlation mutation.
        var inspect = typeof(NativeCombatAttributionAdapter).GetMethod("InspectNextPatchStamp", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var now = DateTime.UtcNow.AddSeconds(3);
        for (var index = 0; index < 11; index++) inspect.Invoke(adapter, [now.AddSeconds(index)]);
        Assert.Contains(diagnostics, value => value.Contains("after patch-set drift", StringComparison.Ordinal)
            && value.Contains(method.DeclaringType!.Name + "." + method.Name, StringComparison.Ordinal));
        Assert.Contains(HarmonyLib.Harmony.GetPatchInfo(method)!.Prefixes, value => value.owner == "foreign.combat-test");
    }

    private static void ForeignPrefix() { }
    public void Dispose()
    {
        Assert.True(adapter.TryCleanup());
        HarmonyLib.Harmony.ClearAll();
        CharacterMainControl.ResetNativeState();
        LevelManager.ResetNativeState();
        Time.frameCount = 0;
    }
}
