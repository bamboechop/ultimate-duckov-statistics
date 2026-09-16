using ItemStatsSystem;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Encounters;

namespace UltimateDuckovStatistics.Tests;

public sealed partial class NativeCombatDegradationTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public void EncounterFallbackAfterEffectHookLossKeepsHealthEvidenceWithoutClaimingOwnership(
        bool incoming, bool nativeEffectFlag, bool lostBeforeObserver)
    {
        if (lostBeforeObserver) AddForeignPatchAndInspect(NativeMethod(typeof(Effect), "Trigger"));
        var sink = new CombatSink();
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        if (!lostBeforeObserver) AddForeignPatchAndInspect(NativeMethod(typeof(Effect), "Trigger"));
        var npc = new CharacterMainControl { characterPreset = new() { nameKey = "Scavenger" } };
        var health = new Health { CurrentHealth = 10, Character = incoming ? player : npc,
            IsMainCharacterHealth = incoming, team = incoming ? Teams.player : Teams.enemy };
        // Installed ExplosionAction can copy a retained buff owner without setting
        // isFromBuffOrEffect. No effect callback runs when its hook is unavailable.
        FatalFallbackHit(health, new DamageInfo { fromCharacter = incoming ? npc : player,
            fromWeaponItemID = 67, damageValue = 10, isExplosion = true, isFromBuffOrEffect = nativeEffectFlag });

        var aggregate = Assert.Single(events, value => value.ActualDamageToTarget > 0 || value.ActualDamageReceived > 0);
        Assert.Equal(CombatOwnership.Unknown, aggregate.Ownership);
        Assert.Equal(0, aggregate.KillsByYou); Assert.Equal(0, aggregate.ActualDamageDealt);
        Assert.Equal("duckov:weapon:unknown", aggregate.WeaponId);
        var fatal = Assert.Single(sink.Rows, row => row.Kind == "combat_fatal").Data;
        Assert.False((bool)fatal["Source"]!["ActorCreditResolved"]!);
        Assert.False((bool)fatal["Candidate"]!["SourcePosition"]!["Available"]!);

        var codec = new ProfileRecordCodec(NativeProfileJsonWriter.WriteRecord);
        var records = sink.Flush().Select(row => ProfileRecordCodec.Decode<EncounterRecord>(codec.Encode(row))).ToArray();
        EncounterRecordValidation.ValidateHistory(records);
        var encounter = Assert.Single(records, row => row.Encounter != null).Encounter!;
        Assert.Equal(incoming ? EncounterOutcome.PlayerDeath : EncounterOutcome.OtherDeath, encounter.Outcome);
        Assert.Equal(EncounterCredit.Unknown, encounter.FinalSource!.Credit);
        Assert.Null(encounter.FinalSource.CreditedActorId); Assert.Null(encounter.FinalSource.WeaponTypeId);
        Assert.Null(encounter.SourcePosition);
        if (incoming)
        {
            Assert.Null(encounter.EnemyPosition);
            var damage = Assert.Single(records, row => row.Damage != null).Damage!;
            Assert.True(damage.Incoming); Assert.Equal(10, damage.Amount);
            Assert.Equal(EncounterCredit.Unknown, damage.Source.Credit);
            Assert.Null(damage.Source.CreditedActorId); Assert.Null(damage.Source.WeaponTypeId);
        }
        else Assert.DoesNotContain(records, row => row.Damage != null);
        Assert.Equal("ui.encounters_capture_incomplete", StoredEncounterRun.Build(records).CoverageNoticeKey);
        Assert.Equal(EncounterCaptureIssue.CombatIncomplete, observer.FailureIssue);
    }

    [Fact]
    public void HealthyUnscopedExplosionRetainsPlayerAndWeaponEvidence()
    {
        var sink = new CombatSink();
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        var health = new Health { CurrentHealth = 10, Character = new(), team = Teams.enemy };
        FatalFallbackHit(health, new DamageInfo { fromCharacter = player, fromWeaponItemID = 67,
            damageValue = 10, isExplosion = true });
        var records = sink.Flush();
        var encounter = Assert.Single(records, row => row.Encounter != null).Encounter!;
        Assert.Equal(EncounterOutcome.PlayerKill, encounter.Outcome);
        Assert.Equal(EncounterCredit.Player, encounter.FinalSource!.Credit);
        Assert.Equal(67, encounter.FinalSource.WeaponTypeId);
        Assert.NotNull(encounter.SourcePosition);
        Assert.Equal(10, Assert.Single(records, row => row.Damage != null).Damage!.Amount);
        Assert.Null(observer.FailureIssue);
    }

    [Fact]
    public void EffectHookLossPreservesTrustedProjectileAndMeleeEncounterAttribution()
    {
        var sink = new CombatSink();
        using var observer = new NativeEncounterCombatObserver(sink, diagnostics.Add);
        AddForeignPatchAndInspect(NativeMethod(typeof(Effect), "Trigger"));
        EncounterHit(LaunchEncounterProjectile(), sink);
        EncounterMelee(sink);
        var records = sink.Flush();
        var kills = records.Where(row => row.Encounter != null).ToArray();
        Assert.Equal(2, kills.Length);
        Assert.All(kills, row =>
        {
            Assert.Equal(EncounterOutcome.PlayerKill, row.Encounter!.Outcome);
            Assert.Equal(EncounterCredit.Player, row.Encounter.FinalSource!.Credit);
            Assert.NotNull(row.Encounter.SourcePosition);
        });
        Assert.Contains(kills, row => row.Encounter!.FinalSource!.WeaponTypeId == 42);
        Assert.Contains(kills, row => row.Encounter!.FinalSource!.WeaponTypeId == 99);
        Assert.Equal(2, records.Count(row => row.Damage?.Amount == 10));
        Assert.Equal("ui.encounters_capture_incomplete", StoredEncounterRun.Build(records).CoverageNoticeKey);
    }

    private static void FatalFallbackHit(Health health, DamageInfo info)
    {
        object?[] hurt = [health, info, null];
        CombatHarmonyCallbacks.HealthPrefixMethod.Invoke(null, hurt);
        try
        {
            NativeEncounterCombatObserver.AssignHealth(health, 0);
            health.IsDead = true;
            CombatHarmonyCallbacks.HealthPostfixMethod.Invoke(null, [health, hurt[2]]);
        }
        finally { CombatHarmonyCallbacks.HealthFinalizerMethod.Invoke(null, [health, null]); }
    }
}
