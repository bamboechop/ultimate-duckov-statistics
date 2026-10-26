using System.Reflection;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Encounters;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

public sealed partial class NativeCombatDegradationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EncounterHistoryAgreesWithChangedSourceOutcomeWithoutInventingCoverageGaps(bool actorChanged)
    {
        var sink = new CombatSink();
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        var projectile = LaunchEncounterProjectile();
        object?[] attack = [projectile, null];
        CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod.Invoke(null, attack);
        var health = new Health { CurrentHealth = 10, Character = new CharacterMainControl(), team = Teams.enemy };
        var damage = new DamageInfo { fromCharacter = player, fromWeaponItemID = 42, crit = 1 };
        object?[] hit = [health, damage, null];
        CombatHarmonyCallbacks.HealthPrefixMethod.Invoke(null, hit);
        try
        {
            if (actorChanged) damage.fromCharacter = new CharacterMainControl();
            else damage.fromWeaponItemID = 99;
            NativeEncounterCombatObserver.AssignHealth(health, 0); health.IsDead = true;
            CombatHarmonyCallbacks.ObservedHealthPostfixMethod.Invoke(null, [health, damage, hit[2], true]);
        }
        finally
        {
            CombatHarmonyCallbacks.HealthFinalizerMethod.Invoke(null, [health, null]);
            CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod.Invoke(null, [null, attack[1]]);
        }
        var rows = sink.Flush();
        EncounterRecordValidation.ValidateHistory(rows);
        var encounter = Assert.Single(rows, row => row.Encounter != null).Encounter!;
        Assert.Equal(actorChanged ? EncounterCredit.Unknown : EncounterCredit.Player, encounter.FinalSource!.Credit);
        Assert.Null(encounter.FinalSource.WeaponTypeId);
        Assert.Null(encounter.FinalSource.AmmunitionTypeId);
        Assert.DoesNotContain(rows, row => row.Coverage != null);
        Assert.Null(observer.FailureIssue);
    }

    [Fact]
    public void PublicPlayerDeathKeepsDeathButDoesNotBorrowConflictingWeapon()
    {
        var projectile = Capture();
        object?[] attack = [projectile, null];
        CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod.Invoke(null, attack);
        try
        {
            adapter.RecordPlayerDeath(new DamageInfo { fromCharacter = player, fromWeaponItemID = 99 });
            var outcome = Assert.Single(events, value => value.PlayerDeaths == 1);
            Assert.Equal("duckov:weapon:unknown", outcome.WeaponId);
            Assert.Equal("duckov:ammo:unknown", outcome.AmmunitionId);
        }
        finally { CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod.Invoke(null, [null, attack[1]]); }
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("weapon")]
    [InlineData("replacement")]
    public void ChangedHealthCallSourceRetainsObservedLossWithoutBorrowingGunOrHeadshot(string change)
    {
        LevelManager.Instance!.InputManager.AimingEnemyHead = true;
        var projectile = Capture();
        object?[] attack = [projectile, null];
        CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod.Invoke(null, attack);
        try
        {
            var enemy = new Health { CurrentHealth = 10, Character = new CharacterMainControl(), team = Teams.enemy };
            var info = new DamageInfo { fromCharacter = player, fromWeaponItemID = 42, crit = 1 };
            object?[] hit = [enemy, info, null];
            CombatHarmonyCallbacks.HealthPrefixMethod.Invoke(null, hit);
            if (change == "actor") info.fromCharacter = new CharacterMainControl();
            if (change == "weapon") info.fromWeaponItemID = 99;
            enemy.CurrentHealth = 0; enemy.IsDead = true;
            CombatHarmonyCallbacks.ObservedHealthPostfixMethod.Invoke(null, [enemy, info, hit[2], change != "replacement"]);
            var outcome = Assert.Single(events);
            Assert.Equal(10, outcome.ActualDamageToTarget);
            Assert.Equal(0, outcome.Headshots);
            Assert.Equal("duckov:weapon:unknown", outcome.WeaponId);
            Assert.Equal("duckov:ammo:unknown", outcome.AmmunitionId);
            Assert.Equal(change == "weapon" ? 1 : 0, outcome.KillsByYou);
            Assert.Equal(change == "weapon" ? 0 : 1, outcome.ObservedWorldDeaths);
            Assert.Equal(change == "weapon" ? CombatOwnership.Player : CombatOwnership.Unknown, outcome.Ownership);
        }
        finally { CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod.Invoke(null, [null, attack[1]]); }
    }

    [Fact]
    public void SkippedOriginalWithoutMeasuredStateCannotInventHealthOrKillEvents()
    {
        var enemy = new Health { CurrentHealth = 10, Character = new CharacterMainControl(), team = Teams.enemy };
        CombatHarmonyCallbacks.ObservedHealthPostfixMethod.Invoke(null, [enemy, new DamageInfo { fromCharacter = player }, null, false]);
        Assert.Empty(events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProvenFatalProjectileKeepsDamageKillAndFeedWhileOtherSourcePathsAreMissing(bool effectLost)
    {
        if (effectLost) RemoveOwnedCallbackAndInspect(NativeMethod(typeof(ItemStatsSystem.Effect), "Trigger"));
        else { buffTrust.MarkUntrusted(); adapter.Tick(); }
        Assert.Equal(AdapterCapabilityState.Experimental, adapter.MetricCapabilities.KillsByYou.State);
        var aggregate = new CombatStatisticsAggregate();
        CombatStatisticsReducer.RestrictCapabilities(aggregate, adapter.MetricCapabilities);
        var feed = new PlayerKillFeed();
        killFeedObservation = (value, distance) => feed.Add(value, distance, 1, new KillFeedSettings());
        var projectile = Capture();
        object?[] attack = [projectile, null];
        CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod.Invoke(null, attack);
        var enemy = new Health { CurrentHealth = 10, Character = new CharacterMainControl(), team = Teams.enemy };
        object?[] hit = [enemy, new DamageInfo { fromCharacter = player }, null];
        CombatHarmonyCallbacks.HealthPrefixMethod.Invoke(null, hit);
        enemy.CurrentHealth = 0; enemy.IsDead = true;
        CombatHarmonyCallbacks.HealthPostfixMethod.Invoke(null, [enemy, hit[2]]);
        CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod.Invoke(null, [null, attack[1]]);
        var outcome = Assert.Single(events, value => value.KillsByYou == 1);
        Assert.Equal(10, outcome.ActualDamageDealt);
        Assert.Equal(AdapterCapabilityState.Supported, outcome.Capabilities.KillsByYou.State);
        CombatStatisticsReducer.Apply(aggregate, outcome);
        Assert.Equal(1, aggregate.Totals.KillsByYou);
        Assert.Equal(AdapterCapabilityState.Experimental, aggregate.Capabilities.KillsByYou.State);
        Assert.Single(feed.Entries);
    }

    [Fact]
    public void RestoredHealthCallbacksResumeNewDamageWithoutResurrectingOldProjectiles()
    {
        var stale = Capture();
        RemoveOwnedCallbackAndInspect(NativeMethod(typeof(Health), nameof(Health.Hurt)));
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, adapter.MetricCapabilities.DamageDealt.State);
        new HarmonyLib.Harmony(NativeCombatAttributionAdapter.HarmonyId).Patch(
            NativeMethod(typeof(Health), nameof(Health.Hurt)),
            new HarmonyLib.HarmonyMethod(CombatHarmonyCallbacks.HealthPrefixMethod),
            new HarmonyLib.HarmonyMethod(CombatHarmonyCallbacks.ObservedHealthPostfixMethod), null,
            new HarmonyLib.HarmonyMethod(CombatHarmonyCallbacks.HealthFinalizerMethod));
        InspectAllCombatStamps(DateTime.UtcNow.AddDays(2));
        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.DamageDealt.State);
        CombatHarmonyCallbacks.ProjectileReleasePrefixMethod.Invoke(null, [stale]);
        Assert.DoesNotContain(events, value => value.CompletedPlayerProjectiles > 0);
        Hit(Capture());
        Assert.Equal(10, events.Sum(value => value.ActualDamageDealt));
    }

    private void InspectAllCombatStamps(DateTime now)
    {
        var inspect = typeof(NativeCombatAttributionAdapter).GetMethod("InspectNextPatchStamp", BindingFlags.NonPublic | BindingFlags.Instance)!;
        for (var i = 0; i < Enum.GetValues<NativeCombatAttributionAdapter.CombatHook>().Length; i++)
            inspect.Invoke(adapter, [now.AddSeconds(i)]);
    }

    [Theory]
    [InlineData(typeof(Health), nameof(Health.Hurt))]
    [InlineData(typeof(Projectile), nameof(Projectile.Init))]
    [InlineData(typeof(Projectile), "Update")]
    [InlineData(typeof(Breakable), "OnDead")]
    public void UnfamiliarPatchKeepsCombatRecordingBeforeAndAfterActivation(Type type, string method)
    {
        var target = NativeMethod(type, method);
        new HarmonyLib.Harmony("unfamiliar.combat-mod").Patch(target,
            new HarmonyLib.HarmonyMethod(typeof(NativeCombatDegradationTests).GetMethod(nameof(ForeignPrefix), BindingFlags.NonPublic | BindingFlags.Static)!),
            null, null, null);
        InspectAllCombatStamps(DateTime.UtcNow.AddDays(1));
        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.DamageDealt.State);
        var projectile = Capture();
        Hit(projectile);
        CombatHarmonyCallbacks.ProjectileReleasePrefixMethod.Invoke(null, [projectile]);
        Assert.Equal(10, events.Sum(value => value.ActualDamageDealt));
        Assert.Equal(1, events.Sum(value => value.RangedHits));
        Assert.Contains(diagnostics, value => value.Contains("unfamiliar.combat-mod", StringComparison.Ordinal));
        Assert.True(adapter.TryCleanup());
        using var replacement = new NativeCombatAttributionAdapter(() => "g", () => "r", () => mapId,
            _ => true, _ => { }, diagnostics.Add, buffTrust);
        replacement.Initialize();
        Assert.Equal(AdapterCapabilityState.Supported, replacement.MetricCapabilities.DamageDealt.State);
    }
}
