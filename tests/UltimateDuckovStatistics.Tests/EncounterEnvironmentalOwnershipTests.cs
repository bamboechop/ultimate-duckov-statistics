using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Encounters;

namespace UltimateDuckovStatistics.Tests;

public sealed partial class NativeCombatDegradationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void EncounterGrenadeZoneUsesProvenThrowerEvenWhenNativeDamageActorIsCleared(bool incoming, bool alreadyRelated)
    {
        var sink = new CombatSink();
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        var zone = CaptureGrenadeZone();
        var health = new Health { CurrentHealth = 10, Character = incoming ? player : new(),
            IsMainCharacterHealth = incoming, team = incoming ? Teams.player : Teams.enemy };
        if (alreadyRelated)
        {
            object?[] first = [health, new DamageInfo { fromCharacter = player }, null];
            CombatHarmonyCallbacks.HealthPrefixMethod.Invoke(null, first);
            CombatHarmonyCallbacks.HealthPostfixMethod.Invoke(null, [health, first[2]]);
            CombatHarmonyCallbacks.HealthFinalizerMethod.Invoke(null, [health, null]);
        }
        EncounterZoneHit(zone, health);

        var aggregate = Assert.Single(events, value => value.ActualDamageToTarget > 0 || value.ActualDamageReceived > 0);
        Assert.Equal(CombatOwnership.Player, aggregate.Ownership);
        Assert.Equal("duckov:weapon:67", aggregate.WeaponId);
        var codec = new ProfileRecordCodec(NativeProfileJsonWriter.WriteRecord);
        var records = sink.Flush().Select(row => ProfileRecordCodec.Decode<EncounterRecord>(codec.Encode(row))).ToArray();
        EncounterRecordValidation.ValidateHistory(records);
        var encounter = Assert.Single(records, row => row.Encounter?.Outcome != null).Encounter!;
        Assert.Equal(incoming ? EncounterOutcome.PlayerDeath : EncounterOutcome.PlayerKill, encounter.Outcome);
        Assert.Equal(EncounterCredit.Player, encounter.FinalSource!.Credit);
        Assert.Equal(67, encounter.FinalSource.WeaponTypeId);
        Assert.Equal("effect", encounter.FinalSource.Mechanism);
        Assert.NotNull(encounter.SourcePosition);
        var damage = Assert.Single(records, row => row.Damage != null).Damage!;
        Assert.Equal(incoming, damage.Incoming); Assert.Equal(10, damage.Amount);
        Assert.Null(damage.Source.AmmunitionTypeId);
        Assert.DoesNotContain(EncounterDamageText.Parts(damage, _ => "item", key => key),
            part => part.Text.Contains("headshot", StringComparison.Ordinal));
        Assert.Null(observer.FailureIssue);
        Assert.Null(NativeEncounterCombatObserver.ReadActiveSource());
        Assert.Null(CombatHarmonyBridge.CurrentScope);
    }

    [Theory]
    [InlineData(typeof(Grenade), "Launch")]
    [InlineData(typeof(Grenade), "Explode")]
    [InlineData(typeof(UnityEngine.Object), "Instantiate")]
    [InlineData(typeof(ZoneDamage), "Damage")]
    public void EncounterGrenadeZoneTrustLossReportsCoverageWithoutBorrowingThrower(Type type, string method)
    {
        var sink = new CombatSink();
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        var zone = CaptureGrenadeZone();
        AddForeignPatchAndInspect(NativeMethod(type, method));
        EncounterZoneHit(zone, new Health { CurrentHealth = 10, Character = player,
            IsMainCharacterHealth = true, team = Teams.player });
        // The unrelated projectile family still records normally.
        EncounterHit(LaunchEncounterProjectile(), sink);
        var records = sink.Flush();
        var death = Assert.Single(records, row => row.Encounter?.Outcome == EncounterOutcome.PlayerDeath).Encounter!;
        Assert.Equal(EncounterCredit.Unknown, death.FinalSource!.Credit);
        Assert.Null(death.FinalSource.WeaponTypeId); Assert.Null(death.SourcePosition);
        Assert.Single(records, row => row.Encounter?.Outcome == EncounterOutcome.PlayerKill);
        Assert.Single(records, row => row.Damage?.Incoming == true && row.Damage.Amount == 10);
        Assert.Equal(EncounterCaptureIssue.CombatIncomplete, observer.FailureIssue);
        Assert.Equal("ui.encounters_capture_incomplete", StoredEncounterRun.Build(records).CoverageNoticeKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedUnownedZoneShadowsAndRestoresEnclosingProjectile(bool hookLost)
    {
        var sink = new CombatSink();
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        if (hookLost) AddForeignPatchAndInspect(NativeMethod(typeof(ZoneDamage), "Damage"));
        object?[] outer = [LaunchEncounterProjectile(), null];
        CombatHarmonyCallbacks.ProjectileUpdatePrefixMethod.Invoke(null, outer);
        try
        {
            var prior = NativeEncounterCombatObserver.ReadActiveSource();
            Assert.NotNull(prior);
            EncounterZoneHit(new ZoneDamage(), new Health { CurrentHealth = 10, Character = player,
                IsMainCharacterHealth = true, team = Teams.player });
            Assert.Same(prior, NativeEncounterCombatObserver.ReadActiveSource());
        }
        finally { CombatHarmonyCallbacks.ProjectileUpdateFinalizerMethod.Invoke(null, [null, outer[1]]); }
        var death = Assert.Single(sink.Flush(), row => row.Encounter?.Outcome != null).Encounter!;
        Assert.Equal(EncounterCredit.Unknown, death.FinalSource!.Credit);
        Assert.Null(death.FinalSource.WeaponTypeId); Assert.Null(death.SourcePosition);
        Assert.Null(NativeEncounterCombatObserver.ReadActiveSource());
    }

    private static void EncounterZoneHit(ZoneDamage zone, Health health)
    {
        object?[] args = [zone, null];
        CombatHarmonyCallbacks.EnvironmentalDamagePrefixMethod.Invoke(null, args);
        try
        {
            // Installed ZoneDamage.Damage clears fromCharacter before Health.Hurt.
            FatalFallbackHit(health, new DamageInfo { fromCharacter = null, damageValue = 10 });
        }
        finally { CombatHarmonyCallbacks.EnvironmentalDamageFinalizerMethod.Invoke(null, [null, args[1]]); }
    }
}
