using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

public sealed partial class NativeCombatDegradationTests
{
    [Fact]
    public void BarrelHookDriftFailsClosedWithoutDisablingDirectProjectileEvidence()
    {
        RemoveOwnedCallbackAndInspect(typeof(Breakable).GetMethod("OnDead", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!);
        Assert.True(adapter.CanObserveHealth);
        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.RangedHits.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            adapter.Initialize().Single(value => value.AdapterId == "native-barrel-explosion-attribution").State);
        object?[] barrel = [new Breakable { createExplosion = true }, new DamageInfo { fromCharacter = player }, null];
        CombatHarmonyCallbacks.BarrelPrefixMethod.Invoke(null, barrel);
        try
        {
            var death = ExplosiveFatal(new DamageInfo { fromCharacter = player, isExplosion = true, crit = 1, damageValue = 10 });
            Assert.Equal(0, death.KillsByYou);
            Assert.Equal(CombatOwnership.Unknown, death.Ownership);
            Assert.Equal(0, death.Headshots);
        }
        finally { CombatHarmonyCallbacks.BarrelFinalizerMethod.Invoke(null, [null, barrel[2]]); }
    }

    [Fact]
    public void NonExplosiveBreakableDoesNotShadowDirectProjectile()
    {
        object?[] outer = [LaunchEncounterProjectile(), null];
        CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod.Invoke(null, outer);
        var source = CombatHarmonyBridge.CurrentScope;
        object?[] breakable = [new Breakable { createExplosion = false }, new DamageInfo { fromCharacter = player }, null];
        CombatHarmonyCallbacks.BarrelPrefixMethod.Invoke(null, breakable);
        try { Assert.Same(source, CombatHarmonyBridge.CurrentScope); }
        finally
        {
            CombatHarmonyCallbacks.BarrelFinalizerMethod.Invoke(null, [null, breakable[2]]);
            Assert.Same(source, CombatHarmonyBridge.CurrentScope);
            CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod.Invoke(null, [null, outer[1]]);
        }
    }

    private CombatRecorded ExplosiveFatal(DamageInfo damage)
    {
        var health = NewFirstPersonTarget();
        object?[] hurt = [health, damage, null];
        CombatHarmonyCallbacks.HealthPrefixMethod.Invoke(null, hurt);
        try
        {
            UltimateDuckovStatistics.Encounters.NativeEncounterCombatObserver.AssignHealth(health, 0);
            health.IsDead = true;
            CombatHarmonyCallbacks.HealthPostfixMethod.Invoke(null, [health, hurt[2]]);
        }
        finally { CombatHarmonyCallbacks.HealthFinalizerMethod.Invoke(null, [health, null]); }
        return events.Last(value => value.IsFinalBlow);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplosionFallbackDoesNotBorrowGunButNativeGunExplosionRetainsItsOwnWeapon(bool explicitGun)
    {
        var projectile = LaunchEncounterProjectile();
        object?[] outer = [projectile, null];
        CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod.Invoke(null, outer);
        try
        {
            var death = ExplosiveFatal(new DamageInfo { fromCharacter = player, fromWeaponItemID = explicitGun ? 42 : -1,
                isExplosion = true, crit = 1, damageValue = 10 });
            Assert.Equal(explicitGun ? "duckov:weapon:42" : "duckov:weapon:unknown", death.WeaponId);
            Assert.Equal(0, death.Headshots);
            Assert.Equal(0, death.HeadshotFinalBlows);
            Assert.Equal(1, death.KillsByYou);
        }
        finally { CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod.Invoke(null, [null, outer[1]]); }
        CombatHarmonyCallbacks.ProjectileReleasePrefixMethod.Invoke(null, [projectile]);
        Assert.Equal(explicitGun ? 1 : 0, events.Sum(value => value.RangedHits));
    }

    [Fact]
    public void ChainedNpcBarrelShadowsPlayerCreditAndExceptionRestoresEnclosingScope()
    {
        var projectile = LaunchEncounterProjectile();
        object?[] outer = [projectile, null];
        CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod.Invoke(null, outer);
        var projectileScope = CombatHarmonyBridge.CurrentScope;
        object?[] first = [new Breakable { createExplosion = true }, new DamageInfo { fromCharacter = player }, null];
        CombatHarmonyCallbacks.BarrelPrefixMethod.Invoke(null, first);
        var firstScope = CombatHarmonyBridge.CurrentScope;
        var npc = new CharacterMainControl();
        object?[] second = [new Breakable { createExplosion = true }, new DamageInfo { fromCharacter = npc }, null];
        CombatHarmonyCallbacks.BarrelPrefixMethod.Invoke(null, second);
        try
        {
            var death = ExplosiveFatal(new DamageInfo { fromCharacter = npc, isExplosion = true, crit = 1, damageValue = 10 });
            Assert.Equal(0, death.KillsByYou);
            Assert.Equal(1, death.ObservedWorldDeaths);
            Assert.Equal(0, death.Headshots);
            Assert.Equal(CombatSourceIds.ExplosiveBarrel, death.WeaponId);
        }
        finally
        {
            var failure = new InvalidOperationException("native blast failed");
            Assert.Same(failure, CombatHarmonyCallbacks.BarrelFinalizerMethod.Invoke(null, [failure, second[2]]));
            Assert.Same(firstScope, CombatHarmonyBridge.CurrentScope);
            CombatHarmonyCallbacks.BarrelFinalizerMethod.Invoke(null, [null, first[2]]);
            Assert.Same(projectileScope, CombatHarmonyBridge.CurrentScope);
            CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod.Invoke(null, [null, outer[1]]);
            Assert.Null(CombatHarmonyBridge.CurrentScope);
        }
    }

    [Fact]
    public void BarrelEncounterAndMultipleVictimsRetainExplosionSourceWithoutHeadshots()
    {
        var sink = new CombatSink();
        using var observer = new UltimateDuckovStatistics.Encounters.NativeEncounterCombatObserver(sink, diagnostics.Add);
        object?[] outer = [LaunchEncounterProjectile(), null];
        CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod.Invoke(null, outer);
        object?[] barrel = [new Breakable { createExplosion = true }, new DamageInfo { fromCharacter = player }, null];
        CombatHarmonyCallbacks.BarrelPrefixMethod.Invoke(null, barrel);
        try
        {
            for (var i = 0; i < 2; i++)
                ExplosiveFatal(new DamageInfo { fromCharacter = player, isExplosion = true, crit = 1, damageValue = 10 });
        }
        finally
        {
            CombatHarmonyCallbacks.BarrelFinalizerMethod.Invoke(null, [null, barrel[2]]);
            CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod.Invoke(null, [null, outer[1]]);
        }
        var records = sink.Flush();
        var deaths = records.Where(record => record.Encounter?.Outcome == Core.Encounters.EncounterOutcome.PlayerKill).ToArray();
        Assert.Equal(2, deaths.Length);
        Assert.All(deaths, record =>
        {
            Assert.Equal("barrel-explosion", record.Encounter!.FinalSource!.Mechanism);
            Assert.Null(record.Encounter.FinalSource.WeaponTypeId);
            Assert.Null(record.Encounter.FinalSource.AmmunitionTypeId);
        });
        Assert.All(records.Where(record => record.Damage != null), record => Assert.Equal(0, record.Damage!.Headshots));
        var text = UltimateDuckovStatistics.Encounters.EncounterDamageText.Parts(
            records.First(record => record.Damage != null).Damage!, _ => throw new InvalidOperationException("A barrel has no ammunition item"), key => key);
        Assert.Contains(text, part => part.Text == "ui.explosive_barrel");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplosiveBarrelShadowsProjectileAndPreservesPlayerCredit(bool firstPerson)
    {
        using var compatible=StartFirstPersonAdapter(new NativeFirstPersonHeadshotCompatibility(()=>firstPerson));
        var projectile=LaunchEncounterProjectile();
        Time.frameCount++;
        object?[] outer=[projectile,null];
        CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod.Invoke(null,outer);
        object?[] barrel = [new Breakable { createExplosion = true }, new DamageInfo { fromCharacter = player }, null];
        CombatHarmonyCallbacks.BarrelPrefixMethod.Invoke(null, barrel);
        try
        {
            // Installed native flow: Projectile.Update -> DamageReceiver.Hurt ->
            // SimpleHealth.Hurt -> Breakable.OnDead -> CreateExplosion -> Health.Hurt.
            // Breakable copies the triggering actor into a separate explosion DamageInfo.
            // Its weapon is absent; FPC can assign crit=1 to normal player explosion damage.
            var health=NewFirstPersonTarget();
            object?[] hurt=[health,new DamageInfo{fromCharacter=player,fromWeaponItemID=-1,
                damageValue=10,isExplosion=true,crit=1},null];
            CombatHarmonyCallbacks.HealthPrefixMethod.Invoke(null,hurt);
            try
            {
                health.CurrentHealth=0;health.IsDead=true;
                CombatHarmonyCallbacks.HealthPostfixMethod.Invoke(null,[health,hurt[2]]);
            }
            finally{CombatHarmonyCallbacks.HealthFinalizerMethod.Invoke(null,[health,null]);}
        }
        finally
        {
            CombatHarmonyCallbacks.BarrelFinalizerMethod.Invoke(null,[null,barrel[2]]);
            Assert.True(CombatHarmonyBridge.CurrentScope!.IsRanged);
            CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod.Invoke(null,[null,outer[1]]);
        }
        CombatHarmonyCallbacks.ProjectileReleasePrefixMethod.Invoke(null,[projectile]);
        var death=Assert.Single(events,e=>e.KillsByYou>0);
        var stored=new CombatStatisticsAggregate();
        foreach(var value in events)CombatStatisticsReducer.Apply(stored,value);
        var feed=new PlayerKillFeed();Assert.True(feed.Add(death,14.36,1,new KillFeedSettings()));
        var row=Assert.Single(feed.Entries);
        var detail=$"Cause={death.CauseKind}; Attack={death.AttackKind}; Weapon={death.WeaponId}; Ammo={death.AmmunitionId}; "
            +$"Headshots={stored.Totals.Headshots}; HeadshotKills={stored.Totals.HeadshotFinalBlows}; "
            +$"RangedKills={stored.Totals.PlayerKills.Ranged}; AccuracyHits={stored.Totals.RangedHits}; "
            +$"FeedWeapon={row.WeaponId}; FeedHeadshot={row.Headshot}";
        Assert.True(death.AttackKind==CombatAttackKind.Environmental && death.WeaponId==CombatSourceIds.ExplosiveBarrel
            && stored.Totals.Headshots==0 && stored.Totals.RangedHits==0 && stored.Totals.KillsByYou==1 && !row.Headshot,detail);
    }
}
