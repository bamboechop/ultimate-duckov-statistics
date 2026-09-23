using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Encounters;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

public sealed partial class NativeCombatDegradationTests
{
    [Theory]
    [InlineData(true, false, 1, 1)]
    [InlineData(true, true, 0, 0)]
    [InlineData(false, false, 1, 0)]
    [InlineData(false, true, 0, 1)]
    public void VerifiedFirstPersonModeSelectsTargetHitEvidenceWhileOffKeepsNativeLaunchEvidence(
        bool firstPerson, bool launchHeadAim, int verifiedPrefixCrit, int expectedHeadshots)
    {
        using var compatible = StartFirstPersonAdapter(new(() => firstPerson));
        var sink = new CombatSink();
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        LevelManager.Instance!.InputManager.AimingEnemyHead = launchHeadAim;
        var projectile = LaunchEncounterProjectile();
        // Neither a later aim change nor the generic native crit substitutes for
        // launch evidence when FPC is disabled.
        LevelManager.Instance.InputManager.AimingEnemyHead = !launchHeadAim;
        FirstPersonHit(projectile, NewFirstPersonTarget(), verifiedPrefixCrit, fatal: true);
        CombatHarmonyCallbacks.ProjectileReleasePrefixMethod.Invoke(null, [projectile]);

        Assert.Equal(expectedHeadshots, events.Sum(value => value.Headshots));
        Assert.Equal(expectedHeadshots, events.Sum(value => value.HeadshotFinalBlows));
        var row = Assert.Single(sink.Rows, value => value.Kind == "combat_hurt_complete");
        Assert.Equal(expectedHeadshots == 1, (bool)row.Data["Headshot"]!);
        Assert.Equal(1, events.Sum(value => value.RangedHits));
        Assert.Equal(10, events.Sum(value => value.ActualDamageDealt));
        Assert.Equal(AdapterCapabilityState.Supported, compatible.MetricCapabilities.Headshots.State);
    }

    [Fact]
    public void FirstPersonModeIsReadForEachDamageTargetRatherThanFrozenAtProjectileLaunch()
    {
        var firstPerson = false;
        using var compatible = StartFirstPersonAdapter(new(() => firstPerson));
        LevelManager.Instance!.InputManager.AimingEnemyHead = true;
        var first = LaunchEncounterProjectile();
        firstPerson = true;
        FirstPersonHit(first, NewFirstPersonTarget(), 0, fatal: true);
        LevelManager.Instance.InputManager.AimingEnemyHead = false; // FPC neutralizes launch aim.
        var second = LaunchEncounterProjectile();
        firstPerson = false;
        FirstPersonHit(second, NewFirstPersonTarget(), 1, fatal: true);
        LevelManager.Instance.InputManager.AimingEnemyHead = true;
        FirstPersonHit(LaunchEncounterProjectile(), NewFirstPersonTarget(), 0, fatal: true);
        Assert.Equal(0, events[0].Headshots);
        Assert.Equal(0, events[1].Headshots);
        Assert.Equal(1, events[2].Headshots);
    }

    [Fact]
    public void FirstPersonHeadshotEvidenceDoesNotLeakToLaterFatalBodyTargetAndPelletsRemainIndependent()
    {
        using var compatible = StartFirstPersonAdapter(new(() => true));
        var sink = new CombatSink();
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        var projectile = LaunchEncounterProjectile();
        var initialTarget = NewFirstPersonTarget();
        FirstPersonHit(projectile, initialTarget, 1, fatal: false);
        FirstPersonHit(projectile, initialTarget, 1, fatal: false);
        FirstPersonHit(projectile, NewFirstPersonTarget(), 0, fatal: true);
        Assert.Equal(1, events.Sum(value => value.Headshots));
        Assert.Equal(0, events.Sum(value => value.HeadshotFinalBlows));

        var pellet = LaunchEncounterProjectile();
        FirstPersonHit(pellet, NewFirstPersonTarget(), 1, fatal: true);
        foreach (var value in new[] { projectile, pellet, projectile })
            CombatHarmonyCallbacks.ProjectileReleasePrefixMethod.Invoke(null, [value]);
        Assert.Equal(2, events.Sum(value => value.Headshots));
        Assert.Equal(1, events.Sum(value => value.HeadshotFinalBlows));
        Assert.Equal(2, events.Sum(value => value.CompletedPlayerProjectiles));
        Assert.Equal(2, events.Sum(value => value.RangedHits));
        Assert.Equal(2, sink.Rows.Count(row => row.Kind == "combat_hurt_complete" && (bool)row.Data["Headshot"]!));
    }

    [Fact]
    public void FirstPersonHeadshotResultRequiresNormalDamageFromMainDuckToAnotherCharacter()
    {
        var compatibility = new NativeFirstPersonHeadshotCompatibility(() => true);
        var info = new DamageInfo { fromCharacter = player, crit = 1 };
        Assert.True(compatibility.Capture(NewFirstPersonTarget(), info, false));
        info.damageType = DamageTypes.realDamage;
        Assert.False(compatibility.Capture(NewFirstPersonTarget(), info, true));
        info.damageType = DamageTypes.normal;
        info.fromCharacter = new CharacterMainControl();
        Assert.False(compatibility.Capture(NewFirstPersonTarget(), info, true));
        info.fromCharacter = player;
        Assert.False(compatibility.Capture(new() { Character = player }, info, true));
        Assert.False(compatibility.Capture(new(), info, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstPersonContractFailureOnlyDisablesHeadshotMetricsAndReportsPartialEncounterEvidence(bool failsDuringDamage)
    {
        var compatibility = failsDuringDamage
            ? new NativeFirstPersonHeadshotCompatibility(() => throw new InvalidOperationException("mode read failed"))
            : new NativeFirstPersonHeadshotCompatibility(failure: "unverified first person contract");
        using var compatible = StartFirstPersonAdapter(compatibility);
        var sink = new CombatSink();
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        LevelManager.Instance!.InputManager.AimingEnemyHead = true;
        var projectile = LaunchEncounterProjectile();
        FirstPersonHit(projectile, NewFirstPersonTarget(), 1, fatal: true);
        CombatHarmonyCallbacks.ProjectileReleasePrefixMethod.Invoke(null, [projectile]);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, compatible.MetricCapabilities.Headshots.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, compatible.MetricCapabilities.HeadshotFinalBlows.State);
        Assert.Equal(AdapterCapabilityState.Supported, compatible.MetricCapabilities.Accuracy.State);
        Assert.Equal(AdapterCapabilityState.Supported, compatible.MetricCapabilities.DamageDealt.State);
        Assert.Equal(AdapterCapabilityState.Supported, compatible.MetricCapabilities.KillsByYou.State);
        Assert.Equal(1, events.Sum(value => value.RangedHits));
        Assert.Equal(10, events.Sum(value => value.ActualDamageDealt));
        Assert.Equal(0, events.Sum(value => value.Headshots));
        Assert.Equal(EncounterCaptureIssue.CombatIncomplete, observer.FailureIssue);
        Assert.Contains(sink.Rows, value => value.Kind == "combat_coverage"
            && value.Data["UntrustedHooks"]!.ToString().Contains("HeadshotEvidence", StringComparison.Ordinal));
        Assert.Equal("ui.encounters_capture_incomplete", StoredEncounterRun.Build(sink.Flush()).CoverageNoticeKey);
    }

    [Fact]
    public void FirstPersonAbsentUsesVanillaEvidenceWithoutConsultingCrit()
    {
        var compatibility = new NativeFirstPersonHeadshotCompatibility();
        Assert.True(compatibility.Capture(NewFirstPersonTarget(), new() { crit = 0 }, true));
        Assert.False(compatibility.Capture(NewFirstPersonTarget(), new() { crit = 1 }, false));
        Assert.True(compatibility.IsSupported);
    }

    private NativeCombatAttributionAdapter StartFirstPersonAdapter(NativeFirstPersonHeadshotCompatibility compatibility)
    {
        Assert.True(adapter.TryCleanup());
        buffTrust.MarkTrusted();
        var result = new NativeCombatAttributionAdapter(
            () => "g", () => "r", () => "m", value => { events.Add(value); return true; },
            _ => { }, diagnostics.Add, buffTrust, firstPersonCompatibilityFactory: _ => compatibility);
        result.Initialize();
        Assert.True(result.CanObserveHealth);
        return result;
    }

    private static Health NewFirstPersonTarget() => new()
    {
        CurrentHealth = 10,
        Character = new() { characterPreset = new() { nameKey = "Scavenger" } },
        team = Teams.enemy
    };

    private void FirstPersonHit(Projectile projectile, Health health, int verifiedPrefixCrit, bool fatal)
    {
        Time.frameCount++;
        object?[] update = [projectile, null];
        CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod.Invoke(null, update);
        try
        {
            // The exact verified FPC prefix ran earlier and assigned this target's
            // actual head-hit flag to DamageInfo.crit. Invoke our production boundary.
            object?[] hurt = [health, new DamageInfo
            {
                fromCharacter = player, fromWeaponItemID = 42, damageValue = fatal ? health.CurrentHealth : 1,
                crit = verifiedPrefixCrit
            }, null];
            CombatHarmonyCallbacks.HealthPrefixMethod.Invoke(null, hurt);
            try
            {
                NativeEncounterCombatObserver.AssignHealth(health, fatal ? 0 : health.CurrentHealth - 1);
                health.IsDead = fatal;
                CombatHarmonyCallbacks.HealthPostfixMethod.Invoke(null, [health, hurt[2]]);
            }
            finally { CombatHarmonyCallbacks.HealthFinalizerMethod.Invoke(null, [health, null]); }
        }
        finally { CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod.Invoke(null, [null, update[1]]); }
    }
}
