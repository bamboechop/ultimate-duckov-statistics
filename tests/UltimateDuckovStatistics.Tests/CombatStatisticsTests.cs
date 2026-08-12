using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class CombatStatisticsTests
{
    private static readonly DateTime Now = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "Combat")]
    public void ActualHealthLossUsesBeforeAfterStateAndNeverRequestedOrOverkillDamage()
    {
        Assert.Equal(7, CombatObservationPolicy.CalculateActualHealthLoss(10, 3));
        Assert.Equal(3, CombatObservationPolicy.CalculateActualHealthLoss(3, 0));
        Assert.Equal(0, CombatObservationPolicy.CalculateActualHealthLoss(3, 3));
        Assert.Equal(0, CombatObservationPolicy.CalculateActualHealthLoss(double.NaN, 0));
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void CriticalDoesNotBecomeHeadshotAndHeadshotDoesNotBecomeFinalBlow()
    {
        Assert.False(CombatObservationPolicy.CountHeadshot(
            headTargetedProjectile: false, nativeCritical: true, rangedHit: true, alreadyCounted: false));
        Assert.True(CombatObservationPolicy.CountHeadshot(
            headTargetedProjectile: true, nativeCritical: false, rangedHit: true, alreadyCounted: false));
        Assert.False(CombatObservationPolicy.CountHeadshotFinalBlow(
            headTargetedProjectile: true, enemyTarget: true, fatalTransition: false, alreadyCounted: false));
        Assert.True(CombatObservationPolicy.CountHeadshotFinalBlow(
            headTargetedProjectile: true, enemyTarget: true, fatalTransition: true, alreadyCounted: false));
        Assert.False(CombatObservationPolicy.CountHeadshotFinalBlow(
            headTargetedProjectile: true, enemyTarget: true, fatalTransition: true, alreadyCounted: true));
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void OneProjectileOrMeleeScopeCountsOneHitAcrossMultipleDamageCallbacks()
    {
        Assert.True(CombatObservationPolicy.CountRangedHit(true, true, true, alreadyCounted: false));
        Assert.False(CombatObservationPolicy.CountRangedHit(true, true, true, alreadyCounted: true));
        Assert.True(CombatObservationPolicy.CountMeleeHit(true, true, true, alreadyCounted: false));
        Assert.False(CombatObservationPolicy.CountMeleeHit(true, true, true, alreadyCounted: true));
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void StableFallbackIdentityTokensRemainReadableOrDeterministicallyNonEmpty()
    {
        Assert.Equal("modded-bandit", CombatObservationPolicy.CreateStableIdentityToken("Modded Bandit"));
        Assert.Equal("utf8-2a2a2a", CombatObservationPolicy.CreateStableIdentityToken("***"));
        Assert.Equal("unknown", CombatObservationPolicy.CreateStableIdentityToken("  "));
    }

    [Fact]
    [Trait("Category", "Combat")]
    [Trait("Category", "Run")]
    public void DeathEvidenceIsAcceptedOncePerRunAndResetsForTheNextRun()
    {
        var gate = new DeathObservationGate();

        Assert.False(gate.TryObserve(runActive: false));
        Assert.True(gate.TryObserve(runActive: true));
        Assert.False(gate.TryObserve(runActive: true));
        gate.Reset();
        Assert.True(gate.TryObserve(runActive: true));
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void ActualPlayerProjectileDamageKillAndHeadshotFinalBlowAggregateWithoutConflatingCrits()
    {
        var aggregate = new CombatStatisticsAggregate();
        CombatStatisticsReducer.Apply(aggregate, Event("damage") with
        {
            Ownership = CombatOwnership.Player,
            AttackKind = CombatAttackKind.Ranged,
            ActualDamageToTarget = 37,
            ActualDamageDealt = 37,
            RangedHits = 0,
            EnemiesKilled = 1,
            Headshots = 1,
            HeadshotFinalBlows = 1,
            IsFinalBlow = true,
            TargetId = "duckov:target:preset:bandit",
            TargetDisplayName = "Bandit",
            TargetFamilyId = "duckov:family:unknown",
            WeaponId = "duckov:weapon:100",
            AmmunitionId = "duckov:ammo:200"
        });
        CombatStatisticsReducer.Apply(aggregate, Event("projectile") with
        {
            Ownership = CombatOwnership.Player,
            CompletedPlayerProjectiles = 1,
            RangedHits = 1,
            ProjectileId = "p-1"
        });

        Assert.Equal(37, aggregate.Totals.DamageCaused);
        Assert.Equal(37, aggregate.Totals.DamageDealt);
        Assert.Equal(1, aggregate.Totals.RangedHits);
        Assert.Equal(1, aggregate.Totals.CompletedPlayerProjectiles);
        Assert.Equal(1, aggregate.Totals.EnemiesKilled);
        Assert.Equal(1, aggregate.Totals.Headshots);
        Assert.Equal(1, aggregate.Totals.HeadshotFinalBlows);
        Assert.Equal(37, aggregate.Enemies["duckov:target:preset:bandit"].Totals.DamageCaused);
        Assert.Equal(37, aggregate.Ownership[nameof(CombatOwnership.Player)].Totals.DamageCaused);
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void MultiDamageProjectileCanRecordItsHeadshotBeforeItsFinalBlow()
    {
        var aggregate = new CombatStatisticsAggregate();
        CombatStatisticsReducer.Apply(aggregate, Event("headshot") with
        {
            Ownership = CombatOwnership.Player,
            ActualDamageToTarget = 10,
            ActualDamageDealt = 10,
            Headshots = 1
        });
        CombatStatisticsReducer.Apply(aggregate, Event("later-final-blow") with
        {
            Ownership = CombatOwnership.Player,
            ActualDamageToTarget = 5,
            ActualDamageDealt = 5,
            EnemiesKilled = 1,
            HeadshotFinalBlows = 1,
            IsFinalBlow = true
        });

        Assert.Equal(1, aggregate.Totals.Headshots);
        Assert.Equal(1, aggregate.Totals.HeadshotFinalBlows);
        CombatStatisticsReducer.ValidateAggregate(aggregate);
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void OwnershipSeparatesPlayerPetEnvironmentalAndUnknownDamage()
    {
        var aggregate = new CombatStatisticsAggregate();
        foreach (var ownership in Enum.GetValues<CombatOwnership>())
        {
            CombatStatisticsReducer.Apply(aggregate, Event($"damage-{ownership}") with
            {
                Ownership = ownership,
                ActualDamageToTarget = 10,
                ActualDamageDealt = ownership == CombatOwnership.Player ? 10 : 0
            });
        }

        Assert.Equal(40, aggregate.Totals.DamageCaused);
        Assert.Equal(10, aggregate.Totals.DamageDealt);
        Assert.All(Enum.GetNames<CombatOwnership>(), name =>
            Assert.Equal(10, aggregate.Ownership[name].Totals.DamageCaused));
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void FriendlyTargetAndUnrelatedNpcDamageAreRejectedBeforeAggregation()
    {
        var aggregate = new CombatStatisticsAggregate();
        Assert.False(CombatObservationPolicy.ShouldRecordHealthTransition(
            targetIsMain: false, targetIsEnemy: false, CombatOwnership.Player));
        Assert.False(CombatObservationPolicy.ShouldRecordHealthTransition(
            targetIsMain: false, targetIsEnemy: true, CombatOwnership.Unknown));
        Assert.True(CombatObservationPolicy.ShouldRecordHealthTransition(
            targetIsMain: false, targetIsEnemy: true, CombatOwnership.Player));
        Assert.True(CombatObservationPolicy.ShouldRecordHealthTransition(
            targetIsMain: false, targetIsEnemy: true, CombatOwnership.PetCompanion));
        Assert.True(CombatObservationPolicy.ShouldRecordHealthTransition(
            targetIsMain: false, targetIsEnemy: true, CombatOwnership.Environmental));
        Assert.True(CombatObservationPolicy.ShouldRecordHealthTransition(
            targetIsMain: true, targetIsEnemy: false, CombatOwnership.Unknown));

        Assert.Equal(0, aggregate.Totals.DamageCaused);
        Assert.Equal(0, aggregate.Totals.DamageDealt);
        Assert.Empty(aggregate.Enemies);
        Assert.Empty(aggregate.Families);
        Assert.Empty(aggregate.Ownership);
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void ProjectileCompletionAndDeathOutcomesRetainCapturedWeaponAndAmmunitionIdentity()
    {
        var completion = Event("completion");
        var death = Event("death");

        foreach (var value in new[] { completion, death })
        {
            CombatObservationPolicy.ApplyOutcomeIdentity(
                value,
                "projectile-1",
                100,
                "TT-33",
                200,
                "Rost-Muni");
            Assert.Equal("projectile-1", value.ProjectileId);
            Assert.Equal("duckov:weapon:100", value.WeaponId);
            Assert.Equal("TT-33", value.WeaponDisplayName);
            Assert.Equal("duckov:ammo:200", value.AmmunitionId);
            Assert.Equal("Rost-Muni", value.AmmunitionDisplayName);
        }
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void ProjectileCorrelationMatchesOnlyItsOriginatingGenerationRunAndMap()
    {
        Assert.True(CombatObservationPolicy.MatchesOriginatingContext(
            "generation-1", "run-1", "map-1", "generation-1", "run-1", "map-1"));
        Assert.False(CombatObservationPolicy.MatchesOriginatingContext(
            "generation-1", "run-1", "map-1", "generation-2", "run-1", "map-1"));
        Assert.False(CombatObservationPolicy.MatchesOriginatingContext(
            "generation-1", "run-1", "map-1", "generation-1", "run-2", "map-1"));
        Assert.False(CombatObservationPolicy.MatchesOriginatingContext(
            "generation-1", "run-1", "map-1", "generation-1", "run-1", "map-2"));
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void MissingHookDisablesOnlyItsDependentCapabilities()
    {
        var withoutEffect = CombatNativeContractPolicy.CreateCapabilities(AllHooks() with { EffectTrigger = false });
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, withoutEffect.DamageOverTime.State);
        Assert.Equal(AdapterCapabilityState.Supported, withoutEffect.DamageDealt.State);
        Assert.Equal(AdapterCapabilityState.Supported, withoutEffect.Accuracy.State);
        Assert.Equal(AdapterCapabilityState.Supported, withoutEffect.MeleeHits.State);
        Assert.Equal(AdapterCapabilityState.Supported, withoutEffect.PlayerDeaths.State);

        var withoutRelease = CombatNativeContractPolicy.CreateCapabilities(AllHooks() with { ProjectileRelease = false });
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, withoutRelease.RangedHits.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, withoutRelease.Accuracy.State);
        Assert.Equal(AdapterCapabilityState.Supported, withoutRelease.AmmunitionIdentity.State);
        Assert.Equal(AdapterCapabilityState.Supported, withoutRelease.Headshots.State);
        Assert.Equal(AdapterCapabilityState.Supported, withoutRelease.DamageDealt.State);
        Assert.Equal(AdapterCapabilityState.Supported, withoutRelease.MeleeSwings.State);
        Assert.Equal(AdapterCapabilityState.Supported, withoutRelease.PlayerDeaths.State);

        var withoutHealth = CombatNativeContractPolicy.CreateCapabilities(AllHooks() with { HealthHurt = false });
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, withoutHealth.DamageDealt.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, withoutHealth.DamageReceived.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, withoutHealth.EnemiesKilled.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, withoutHealth.MeleeHits.State);
        Assert.Equal(AdapterCapabilityState.Supported, withoutHealth.MeleeSwings.State);
        Assert.Equal(AdapterCapabilityState.Supported, withoutHealth.PlayerDeaths.State);
    }

    [Fact]
    [Trait("Category", "Combat")]
    [Trait("Category", "Persistence")]
    public void RecoveryCandidateValidationRejectsNegativeNonFiniteAndImpossibleCombatState()
    {
        var negative = new CombatStatisticsAggregate();
        negative.Totals.DamageDealt = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatStatisticsReducer.ValidateRecoveryCandidate(negative));

        var nonFinite = new CombatStatisticsAggregate();
        nonFinite.Totals.DamageReceived = double.NaN;
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatStatisticsReducer.ValidateRecoveryCandidate(nonFinite));

        var impossible = new CombatStatisticsAggregate();
        impossible.Totals.CompletedPlayerProjectiles = 1;
        impossible.Totals.RangedHits = 2;
        Assert.Throws<ArgumentException>(() => CombatStatisticsReducer.ValidateRecoveryCandidate(impossible));

        var impossibleHeadshotFinalBlow = new CombatStatisticsAggregate
        {
            Totals = new CombatMetricTotals { EnemiesKilled = 1, HeadshotFinalBlows = 1 }
        };
        Assert.Throws<ArgumentException>(() =>
            CombatStatisticsReducer.ValidateRecoveryCandidate(impossibleHeadshotFinalBlow));

        var nestedImpossible = new CombatStatisticsAggregate();
        nestedImpossible.Weapons["duckov:weapon:1"] = new CombatBreakdownAggregate
        {
            Id = "duckov:weapon:1",
            DisplayName = "Test weapon",
            Totals = new CombatMetricTotals
            {
                CompletedPlayerProjectiles = 1,
                RangedHits = 2
            }
        };
        Assert.Throws<ArgumentException>(() =>
            CombatStatisticsReducer.ValidateRecoveryCandidate(nestedImpossible));

        var nestedImpossibleFinalBlow = new CombatStatisticsAggregate
        {
            Totals = new CombatMetricTotals { EnemiesKilled = 1, Headshots = 1, HeadshotFinalBlows = 1 }
        };
        nestedImpossibleFinalBlow.Weapons["duckov:weapon:1"] = new CombatBreakdownAggregate
        {
            Id = "duckov:weapon:1",
            DisplayName = "Test weapon",
            Totals = new CombatMetricTotals { HeadshotFinalBlows = 1 }
        };
        Assert.Throws<ArgumentException>(() =>
            CombatStatisticsReducer.ValidateRecoveryCandidate(nestedImpossibleFinalBlow));
    }

    [Fact]
    [Trait("Category", "Combat")]
    [Trait("Category", "Persistence")]
    public void RecoveryCandidateValidationAllowsIndependentHeadshotAndFinalBlowTargets()
    {
        var statistics = new CombatStatisticsAggregate
        {
            Totals = new CombatMetricTotals
            {
                EnemiesKilled = 1,
                Headshots = 1,
                HeadshotFinalBlows = 1
            }
        };
        statistics.Enemies["duckov:target:a"] = new CombatBreakdownAggregate
        {
            Id = "duckov:target:a",
            DisplayName = "First target",
            Totals = new CombatMetricTotals { Headshots = 1 }
        };
        statistics.Enemies["duckov:target:b"] = new CombatBreakdownAggregate
        {
            Id = "duckov:target:b",
            DisplayName = "Fatal target",
            Totals = new CombatMetricTotals { EnemiesKilled = 1, HeadshotFinalBlows = 1 }
        };

        CombatStatisticsReducer.ValidateRecoveryCandidate(statistics);
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void DamageOverTimeDeathAndReceivedDamageRetainCauseAndKillerIdentity()
    {
        var aggregate = new CombatStatisticsAggregate();
        CombatStatisticsReducer.Apply(aggregate, Event("dot-kill") with
        {
            Ownership = CombatOwnership.Player,
            AttackKind = CombatAttackKind.Effect,
            CauseKind = CombatCauseKind.DamageOverTime,
            CauseId = "duckov:cause:damage-over-time",
            CauseDisplayName = "Damage over time",
            ActualDamageToTarget = 4,
            ActualDamageDealt = 4,
            EnemiesKilled = 1,
            IsDamageOverTime = true,
            IsFinalBlow = true
        });
        CombatStatisticsReducer.Apply(aggregate, Event("death") with
        {
            Ownership = CombatOwnership.Unknown,
            ActualDamageReceived = 12,
            PlayerDeaths = 1,
            AttackerId = "duckov:attacker:preset:modded-killer",
            AttackerDisplayName = "Modded Killer"
        });

        Assert.Equal(1, aggregate.Causes["duckov:cause:damage-over-time"].Totals.EnemiesKilled);
        Assert.Equal(12, aggregate.Killers["duckov:attacker:preset:modded-killer"].Totals.DamageReceived);
        Assert.Equal(1, aggregate.Killers["duckov:attacker:preset:modded-killer"].Totals.PlayerDeaths);
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void TrackerRejectsReplayButKeepsRapidMultiHitAndCheckpointExactlyOnce()
    {
        var tracker = StartedTracker();
        var first = Event("same") with { ActualDamageToTarget = 2, ActualDamageDealt = 2 };
        Assert.True(tracker.RecordCombat(first));
        Assert.False(tracker.RecordCombat(first));
        Assert.True(tracker.RecordCombat(Event("rapid-2") with { ActualDamageToTarget = 3, ActualDamageDealt = 3 }));
        Assert.True(tracker.RecordCombat(Event("rapid-3") with { ActualDamageToTarget = 5, ActualDamageDealt = 5 }));

        var checkpoint = tracker.CreateCheckpoint(Now.AddSeconds(1), 1)!;
        Assert.Equal(10, checkpoint.CombatStatistics.Totals.DamageDealt);
        var recovered = checkpoint.ToInterruptedSummary();
        var profile = new ProfileStatistics { SaveGenerationId = "generation-1" };
        Assert.True(RunReducer.Apply(profile, recovered));
        Assert.False(RunReducer.Apply(profile, recovered));
        Assert.Equal(10, profile.RunTotals.CombatStatistics.Totals.DamageDealt);
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void TrackerRejectsBaseMissingRunWrongMapAndWrongGenerationActivity()
    {
        var tracker = StartedTracker();
        Assert.False(tracker.RecordCombat(Event("base") with { GameplayContext = GameplayContext.Base }));
        Assert.False(tracker.RecordCombat(Event("generation") with { SaveGenerationId = "other" }));
        Assert.False(tracker.RecordCombat(Event("run") with { RunId = "other" }));
        Assert.False(tracker.RecordCombat(Event("map") with { MapId = "other" }));
        Assert.Equal(0, tracker.CreateCheckpoint(Now.AddSeconds(1), 1)!.CombatStatistics.Totals.DamageCaused);
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void RunMapAndLifetimeMergeRemainEqualAndSaturate()
    {
        var summary = StartedTracker();
        Assert.True(summary.RecordCombat(Event("max") with
        {
            ActualDamageToTarget = double.MaxValue,
            ActualDamageDealt = double.MaxValue,
            EnemiesKilled = long.MaxValue
        }));
        Assert.True(summary.RecordCombat(Event("overflow") with
        {
            ActualDamageToTarget = double.MaxValue,
            ActualDamageDealt = double.MaxValue,
            EnemiesKilled = 5
        }));
        var run = summary.Apply(Lifecycle(RunLifecycleEventKind.Extracted, 2)).Completed!;
        var profile = new ProfileStatistics { SaveGenerationId = "generation-1" };
        Assert.True(RunReducer.Apply(profile, run));

        Assert.Equal(double.MaxValue, run.CombatStatistics.Totals.DamageDealt);
        Assert.Equal(long.MaxValue, run.CombatStatistics.Totals.EnemiesKilled);
        Assert.Equal(run.CombatStatistics.Totals.DamageDealt, profile.RunTotals.CombatStatistics.Totals.DamageDealt);
        Assert.Equal(run.CombatStatistics.Totals.DamageDealt,
            profile.RunTotals.Maps["duckov:map:warehouse"].CombatStatistics.Totals.DamageDealt);
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void NestedNormalizationRepairsInvalidValuesIdentitiesAndNullCollectionsIdempotently()
    {
        var aggregate = new CombatStatisticsAggregate
        {
            Totals = new CombatMetricTotals
            {
                DamageCaused = double.NaN,
                DamageDealt = -1,
                EnemiesKilled = 1,
                CompletedPlayerProjectiles = 2,
                RangedHits = 5,
                Headshots = 1,
                HeadshotFinalBlows = 4
            },
            Enemies = new Dictionary<string, CombatBreakdownAggregate>(StringComparer.Ordinal)
            {
                ["legacy-enemy-a"] = new CombatBreakdownAggregate
                {
                    Id = "enemy",
                    DisplayName = "Zulu",
                    Totals = new CombatMetricTotals { DamageCaused = 2 }
                },
                ["legacy-enemy-b"] = new CombatBreakdownAggregate
                {
                    Id = "enemy",
                    DisplayName = "Alpha",
                    Totals = new CombatMetricTotals { DamageCaused = 3 }
                },
                ["negative"] = new CombatBreakdownAggregate
                {
                    Id = "negative",
                    DisplayName = "Negative",
                    Totals = new CombatMetricTotals { DamageCaused = -5 }
                }
            },
            Killers = null!,
            Families = null!,
            Causes = null!,
            Weapons = null!,
            Ammunition = null!,
            Ownership = null!,
            Capabilities = null!
        };

        var first = CombatStatisticsReducer.NormalizePersisted(aggregate);
        var second = CombatStatisticsReducer.NormalizePersisted(aggregate);

        Assert.True(first.Changed);
        Assert.True(first.Repaired);
        Assert.True(aggregate.WasRepairedFromInvalidState);
        Assert.Equal(0, aggregate.Totals.DamageCaused);
        Assert.Equal(0, aggregate.Totals.DamageDealt);
        Assert.Equal(2, aggregate.Totals.RangedHits);
        Assert.Equal(1, aggregate.Totals.HeadshotFinalBlows);
        Assert.Equal(2, aggregate.Enemies.Count);
        Assert.Equal("enemy", aggregate.Enemies["enemy"].Id);
        Assert.Equal("Alpha", aggregate.Enemies["enemy"].DisplayName);
        Assert.Equal(5, aggregate.Enemies["enemy"].Totals.DamageCaused);
        Assert.Equal(0, aggregate.Enemies["negative"].Totals.DamageCaused);
        Assert.False(second.Changed);
        CombatStatisticsReducer.ValidateAggregate(aggregate);
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void CloneNormalizesMalformedNestedStateWithoutMutatingItsSource()
    {
        var source = new CombatStatisticsAggregate
        {
            Totals = new CombatMetricTotals { DamageDealt = -9 },
            Killers = null!
        };

        var clone = CombatStatisticsReducer.Clone(source);

        Assert.Equal(0, clone.Totals.DamageDealt);
        Assert.NotNull(clone.Killers);
        Assert.True(clone.WasRepairedFromInvalidState);
        Assert.Equal(-9, source.Totals.DamageDealt);
        Assert.Null(source.Killers);
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void SchemaFourMigrationLeavesHistoricalCombatExplicitlyUnavailable()
    {
        var profile = Profile();
        profile.SchemaVersion = 4;
        profile.Statistics.SchemaVersion = 4;
        profile.Statistics.RunTotals.CombatStatistics = null!;

        Assert.True(ProfileMigrator.Migrate(profile));

        Assert.Equal(6, profile.SchemaVersion);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            profile.Statistics.RunTotals.CombatStatistics.Capabilities.DamageDealt.State);
        Assert.Contains("predates M5",
            profile.Statistics.RunTotals.CombatStatistics.Capabilities.DamageDealt.Provenance);
        Assert.Equal(0, profile.Statistics.RunTotals.CombatStatistics.Totals.DamageDealt);
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void CurrentRuntimeSupportNeverUpgradesHistoricalUnavailableCombat()
    {
        var profile = Profile();
        profile.SchemaVersion = 4;
        profile.Statistics.SchemaVersion = 4;
        Assert.True(ProfileMigrator.Migrate(profile));
        profile.Capabilities = CombatNativeContractPolicy.ToRecords(
            CombatNativeContractPolicy.CreateSupportedCapabilities(), "current").ToList();

        var model = CombatStatisticsViewModelFactory.Create(profile);
        var export = StatisticsExporter.Create(profile, Now);

        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, model.Capabilities.DamageDealt.State);
        Assert.Contains("DisabledIncompatible", export.CombatAttributionCsv);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            profile.Statistics.RunTotals.CombatStatistics.Capabilities.DamageDealt.State);
    }

    [Fact]
    [Trait("Category", "Combat")]
    [Trait("Category", "Export")]
    public void PristineSchemaFiveProfileUsesCurrentSupportWithoutMutatingStoredState()
    {
        var profile = Profile();
        profile.Capabilities = CombatNativeContractPolicy.ToRecords(
            CombatNativeContractPolicy.CreateSupportedCapabilities(), "current").ToList();

        var model = CombatStatisticsViewModelFactory.Create(profile);
        var export = StatisticsExporter.Create(profile, Now);

        Assert.Equal(AdapterCapabilityState.Supported, model.Capabilities.DamageDealt.State);
        Assert.Contains("Supported", export.CombatAttributionCsv);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            profile.Statistics.RunTotals.CombatStatistics.Capabilities.DamageDealt.State);
        Assert.True(CombatStatisticsReducer.IsEmpty(profile.Statistics.RunTotals.CombatStatistics));
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void RepairedMissingSchemaFiveCombatRootCannotUsePristineCapabilityFallback()
    {
        var profile = Profile();
        profile.Statistics.RunTotals = null!;
        profile.Capabilities = CombatNativeContractPolicy.ToRecords(
            CombatNativeContractPolicy.CreateSupportedCapabilities(), "current").ToList();

        Assert.True(ProfileMigrator.Migrate(profile));
        var model = CombatStatisticsViewModelFactory.Create(profile);

        Assert.True(profile.Statistics.RunTotals.CombatStatistics.WasRepairedFromInvalidState);
        Assert.False(CombatStatisticsReducer.IsEmpty(profile.Statistics.RunTotals.CombatStatistics));
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, model.Capabilities.DamageDealt.State);
    }

    [Fact]
    [Trait("Category", "Combat")]
    public void MidRunCapabilityFailureIsCheckpointedAndCannotBeUpgradedAgain()
    {
        var tracker = StartedTracker();
        var unavailable = CombatNativeContractPolicy.CreateUnavailableCapabilities("patch conflict");
        Assert.True(tracker.UpdateCombatCapabilities(unavailable));
        Assert.True(tracker.UpdateCombatCapabilities(CombatNativeContractPolicy.CreateSupportedCapabilities()));

        var checkpoint = tracker.CreateCheckpoint(Now.AddSeconds(1), 1)!;

        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            checkpoint.CombatStatistics.Capabilities.DamageDealt.State);
        Assert.Contains("patch conflict", checkpoint.CombatStatistics.Capabilities.DamageDealt.Provenance);
    }

    [Fact]
    [Trait("Category", "Combat")]
    [Trait("Category", "Export")]
    public void UiJsonAndCombatCsvAgreeWithoutMutatingHistoricalCapabilities()
    {
        var profile = Profile();
        var tracker = StartedTracker();
        tracker.RecordCombat(Event("damage") with
        {
            Ownership = CombatOwnership.Player,
            ActualDamageToTarget = 8.5,
            ActualDamageDealt = 8.5
        });
        tracker.RecordCombat(Event("projectile") with
        {
            Ownership = CombatOwnership.Player,
            CompletedPlayerProjectiles = 2,
            RangedHits = 1
        });
        var run = tracker.Apply(Lifecycle(RunLifecycleEventKind.Extracted, 2)).Completed!;
        RunReducer.Apply(profile.Statistics, run);
        profile.Capabilities = CombatNativeContractPolicy.ToRecords(
            CombatNativeContractPolicy.CreateSupportedCapabilities(), "test").ToList();
        var recordedState = profile.Statistics.RunTotals.CombatStatistics.Capabilities.DamageDealt.State;

        var view = CombatStatisticsViewModelFactory.Create(profile);
        var export = StatisticsExporter.Create(profile, Now);

        Assert.Equal(8.5, view.Lifetime.Totals.DamageDealt);
        Assert.Equal(0.5, view.Accuracy);
        Assert.Contains(",8.5,8.5,0,2,1,0.5,", export.CombatAttributionCsv);
        Assert.Contains("\"DamageDealt\":8.5", export.Json);
        Assert.Equal(recordedState, profile.Statistics.RunTotals.CombatStatistics.Capabilities.DamageDealt.State);
    }

    private static RunLifecycleTracker StartedTracker()
    {
        var tracker = new RunLifecycleTracker(() => "run-1");
        tracker.Apply(Lifecycle(RunLifecycleEventKind.RaidInitialized, 0));
        tracker.Apply(Lifecycle(RunLifecycleEventKind.ControlReady, 0, new RunStartContext
        {
            SaveGenerationId = "generation-1",
            Map = new MapIdentity { MapId = "duckov:map:warehouse", DisplayName = "Warehouse", IsKnown = true },
            IntegrityTags = IntegrityTags.Normal,
            GameVersion = "2.3.30",
            GameBuild = "24013657",
            LifecycleCapability = AdapterCapabilityState.Supported,
            MovementCapability = AdapterCapabilityState.Supported,
            MapCapability = AdapterCapabilityState.Supported,
            CombatCapabilities = CombatNativeContractPolicy.CreateSupportedCapabilities()
        }));
        return tracker;
    }

    private static RunLifecycleEvent Lifecycle(RunLifecycleEventKind kind, double seconds, RunStartContext? context = null) => new()
    {
        Kind = kind,
        TimestampUtc = Now.AddSeconds(seconds),
        MonotonicSeconds = seconds,
        NativeRaidId = "raid-1",
        StartContext = context
    };

    private static CombatRecorded Event(string id) => new()
    {
        EventId = id,
        TimestampUtc = Now,
        SaveGenerationId = "generation-1",
        RunId = "run-1",
        MapId = "duckov:map:warehouse",
        GameplayContext = GameplayContext.Raid,
        IntegrityTags = IntegrityTags.Normal,
        GameVersion = "2.3.30",
        GameBuild = "24013657",
        AdapterVersion = "test",
        TargetId = "duckov:target:preset:enemy",
        TargetDisplayName = "Enemy",
        TargetIsEnemy = true,
        TargetFamilyId = "duckov:family:unknown",
        TargetFamilyDisplayName = "Unknown family",
        Capabilities = CombatNativeContractPolicy.CreateSupportedCapabilities()
    };

    private static ProfileDocument Profile() => new()
    {
        GenerationId = "generation-1",
        Slot = 1,
        CreatedUtc = Now,
        UpdatedUtc = Now,
        Identity = new SaveIdentitySnapshot { Slot = 1 },
        Statistics = new ProfileStatistics
        {
            SaveGenerationId = "generation-1",
            CreatedUtc = Now,
            UpdatedUtc = Now
        }
    };

    private static CombatHookSupport AllHooks() => new()
    {
        HealthHurt = true,
        ProjectileInit = true,
        ProjectileUpdate = true,
        ProjectileRelease = true,
        MeleeCheck = true,
        EffectTrigger = true,
        PublicMeleeSwing = true,
        PublicPlayerDeath = true
    };
}
