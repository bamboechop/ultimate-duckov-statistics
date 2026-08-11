using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class WeaponStatisticsTests
{
    private static readonly DateTime Origin = new(2026, 8, 11, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "Weapon")]
    public void OneFiringEventCountsDistinctActionAmmunitionAndProjectileMetricsExactlyOnce()
    {
        var tracker = StartedTracker();
        var shot = Shot("event-one", "weapon:shotgun", "Shotgun", "ammo:shell", "Shell", 1, 1, 8);

        Assert.True(tracker.RecordShot(shot));
        Assert.False(tracker.RecordShot(shot));
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 10)).Completed!;

        Assert.Equal(1, summary.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(1, summary.WeaponStatistics.Totals.AmmunitionUnitsConsumed);
        Assert.Equal(8, summary.WeaponStatistics.Totals.Projectiles);
        Assert.Equal(1, summary.WeaponStatistics.Weapons["weapon:shotgun"].Totals.FiringActions);
        Assert.Equal(8, summary.WeaponStatistics.AmmunitionTypes["ammo:shell"].Totals.Projectiles);
    }

    [Fact]
    [Trait("Category", "Weapon")]
    public void SemiAutomaticRepeatedAutomaticAndBurstDischargesRemainSeparateFiringActions()
    {
        var tracker = StartedTracker();

        Assert.True(tracker.RecordShot(Shot("semi", "weapon:one", "Rifle", "ammo:a", "A", 1, 1, 1)));
        Assert.True(tracker.RecordShot(Shot("auto-1", "weapon:one", "Rifle", "ammo:a", "A", 1, 1, 1)));
        Assert.True(tracker.RecordShot(Shot("auto-2", "weapon:one", "Rifle", "ammo:a", "A", 1, 1, 1)));
        Assert.True(tracker.RecordShot(Shot("burst-1", "weapon:one", "Rifle", "ammo:a", "A", 1, 1, 1)));
        Assert.True(tracker.RecordShot(Shot("burst-2", "weapon:one", "Rifle", "ammo:a", "A", 1, 1, 1)));
        Assert.True(tracker.RecordShot(Shot("burst-3", "weapon:one", "Rifle", "ammo:a", "A", 1, 1, 1)));

        var summary = tracker.Apply(Event(RunLifecycleEventKind.Died, 10)).Completed!;
        Assert.Equal(6, summary.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(6, summary.WeaponStatistics.Totals.AmmunitionUnitsConsumed);
        Assert.Equal(6, summary.WeaponStatistics.Totals.Projectiles);
    }

    [Fact]
    [Trait("Category", "Weapon")]
    public void SupportedMultiUnitConsumptionAndEventTimeWeaponAndAmmoSwitchesStayAttributed()
    {
        var tracker = StartedTracker();
        tracker.RecordShot(Shot("one", "weapon:a", "A", "ammo:a", "Ammo A", 1, 2, 3));
        tracker.RecordShot(Shot("two", "weapon:b", "B", "ammo:b", "Ammo B", 1, 1, 1));

        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 10)).Completed!;

        Assert.Equal(3, summary.WeaponStatistics.Totals.AmmunitionUnitsConsumed);
        Assert.Equal(2, summary.WeaponStatistics.Weapons.Count);
        Assert.Equal(2, summary.WeaponStatistics.Weapons["weapon:a"].Totals.AmmunitionUnitsConsumed);
        Assert.Equal(1, summary.WeaponStatistics.AmmunitionTypes["ammo:b"].Totals.FiringActions);
    }

    [Fact]
    [Trait("Category", "Weapon")]
    public void UnknownAndModdedStableIdsRemainDistinctAndKeepFallbackNames()
    {
        var tracker = StartedTracker();
        tracker.RecordShot(Shot("one", "duckov:weapon:900001", "Unknown weapon 900001", "duckov:ammo:800001", "Unknown ammo 800001", 1, 1, 1));
        tracker.RecordShot(Shot("two", "duckov:weapon:900002", "Modded blaster", "duckov:ammo:800002", "Modded cell", 1, 1, 4));

        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 10)).Completed!;

        Assert.Equal(2, summary.WeaponStatistics.Weapons.Count);
        Assert.Equal(2, summary.WeaponStatistics.AmmunitionTypes.Count);
        Assert.Equal("Unknown weapon 900001", summary.WeaponStatistics.Weapons["duckov:weapon:900001"].DisplayName);
        Assert.Equal("Modded cell", summary.WeaponStatistics.AmmunitionTypes["duckov:ammo:800002"].DisplayName);
    }

    [Fact]
    [Trait("Category", "Weapon")]
    public void MismatchedGenerationRunMapAndLateTerminalEventsCannotEnterStatistics()
    {
        var tracker = StartedTracker();
        var wrongGeneration = Shot("generation", "weapon:a", "A", "ammo:a", "A", 1, 1, 1);
        wrongGeneration.SaveGenerationId = "other";
        var wrongRun = Shot("run", "weapon:a", "A", "ammo:a", "A", 1, 1, 1);
        wrongRun.RunId = "other";
        var wrongMap = Shot("map", "weapon:a", "A", "ammo:a", "A", 1, 1, 1);
        wrongMap.MapId = "other";

        Assert.False(tracker.RecordShot(wrongGeneration));
        Assert.False(tracker.RecordShot(wrongRun));
        Assert.False(tracker.RecordShot(wrongMap));
        var completed = tracker.Apply(Event(RunLifecycleEventKind.Interrupted, 5)).Completed!;
        Assert.False(tracker.RecordShot(Shot("late", "weapon:a", "A", "ammo:a", "A", 1, 1, 1)));
        Assert.Equal(0, completed.WeaponStatistics.Totals.FiringActions);
    }

    [Theory]
    [Trait("Category", "Weapon")]
    [InlineData(false, GameplayContext.Raid, true, false, false)]
    [InlineData(true, GameplayContext.Base, true, false, false)]
    [InlineData(true, GameplayContext.Unknown, true, true, false)]
    [InlineData(true, GameplayContext.Paused, true, false, true)]
    [InlineData(true, GameplayContext.Raid, false, false, false)]
    public void BaseLoadingPausedNoRunAndNonMainSubjectsAreRejected(
        bool activeRun,
        GameplayContext context,
        bool exactMainDuck,
        bool loading,
        bool paused)
    {
        Assert.False(WeaponFireAcceptancePolicy.ShouldRecord(activeRun, context, exactMainDuck, loading, paused));
    }

    [Fact]
    [Trait("Category", "Weapon")]
    public void ExactMainDuckInActiveUnpausedRaidIsAccepted()
    {
        Assert.True(WeaponFireAcceptancePolicy.ShouldRecord(true, GameplayContext.Raid, true, false, false));
    }

    [Fact]
    [Trait("Category", "Weapon")]
    public void ReloadDryFireInventoryMovementPetsCompanionsNpcsAndUnrelatedProjectilesDoNotCreateEvents()
    {
        var tracker = StartedTracker();

        Assert.False(WeaponFireAcceptancePolicy.ShouldRecord(true, GameplayContext.Raid, false, false, false));
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 10)).Completed!;
        Assert.Equal(0, summary.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(0, summary.WeaponStatistics.Totals.AmmunitionUnitsConsumed);
        Assert.Equal(0, summary.WeaponStatistics.Totals.Projectiles);
    }

    [Fact]
    [Trait("Category", "Weapon")]
    [Trait("Category", "Compatibility")]
    public void PublicNativeFiringContractDoesNotInventAmmunitionOrProjectileOutcomes()
    {
        var capabilities = WeaponNativeContractPolicy.CreateMetricCapabilities();
        var shot = Shot("native", "weapon", "Weapon", "ammo", "Ammo", 1, 1, 5);
        shot.AmmunitionUnitsConsumed = null;
        shot.ProjectileCount = null;
        shot.Capabilities = capabilities;
        var statistics = new WeaponStatisticsAggregate();

        WeaponStatisticsReducer.Apply(statistics, shot);

        Assert.Equal(1, statistics.Totals.FiringActions);
        Assert.Equal(0, statistics.Totals.AmmunitionUnitsConsumed);
        Assert.Equal(0, statistics.Totals.Projectiles);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, statistics.Capabilities.AmmunitionConsumption.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, statistics.Capabilities.Projectiles.State);
    }

    [Fact]
    [Trait("Category", "Weapon")]
    [Trait("Category", "Integrity")]
    public void FiringEventIntegrityIsAccumulatedIntoTheRunSummary()
    {
        var tracker = StartedTracker();
        var shot = Shot("modded", "weapon", "Weapon", "ammo", "Ammo", 1, 1, 1);
        shot.IntegrityTags = IntegrityTags.ModdedContent;

        Assert.True(tracker.RecordShot(shot));
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 10)).Completed!;

        Assert.True(summary.IntegrityTags.HasFlag(IntegrityTags.ModdedContent));
        Assert.False(summary.RecordEligible);
    }

    [Fact]
    [Trait("Category", "Weapon")]
    public void RejectedInvalidEventDoesNotPoisonItsEventIdForALaterValidEvent()
    {
        var tracker = StartedTracker();
        var invalid = Shot("recoverable-id", "weapon", "Weapon", "ammo", "Ammo", 1, 1, 1);
        invalid.ProjectileCount = -1;

        Assert.Throws<ArgumentException>(() => tracker.RecordShot(invalid));
        Assert.True(tracker.RecordShot(Shot("recoverable-id", "weapon", "Weapon", "ammo", "Ammo", 1, 1, 1)));
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 10)).Completed!;
        Assert.Equal(1, summary.WeaponStatistics.Totals.FiringActions);
    }

    [Fact]
    [Trait("Category", "Weapon")]
    [Trait("Category", "Persistence")]
    public void NegativePersistedWeaponCountersAreRejectedBeforeAggregateMutation()
    {
        var summary = StartedTracker().Apply(Event(RunLifecycleEventKind.Extracted, 10)).Completed!;
        summary.WeaponStatistics.Totals.FiringActions = -1;
        var profile = new ProfileStatistics { SaveGenerationId = "generation-1" };

        Assert.Throws<ArgumentOutOfRangeException>(() => RunReducer.Apply(profile, summary));
        Assert.Empty(profile.Runs);
        Assert.Equal(0, profile.RunTotals.WeaponStatistics.Totals.FiringActions);
    }

    [Fact]
    [Trait("Category", "Weapon")]
    [Trait("Category", "Persistence")]
    public void PersistedCounterOverflowSaturatesInsteadOfWrappingNegative()
    {
        var tracker = StartedTracker();
        tracker.RecordShot(Shot("one", "weapon", "Weapon", "ammo", "Ammo", 1, 1, 1));
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 10)).Completed!;
        var profile = new ProfileStatistics { SaveGenerationId = "generation-1" };
        profile.RunTotals.WeaponStatistics.Totals.FiringActions = long.MaxValue;
        profile.RunTotals.WeaponStatistics.Totals.AmmunitionUnitsConsumed = long.MaxValue;
        profile.RunTotals.WeaponStatistics.Totals.Projectiles = long.MaxValue;

        Assert.True(RunReducer.Apply(profile, summary));
        Assert.Equal(long.MaxValue, profile.RunTotals.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(long.MaxValue, profile.RunTotals.WeaponStatistics.Totals.AmmunitionUnitsConsumed);
        Assert.Equal(long.MaxValue, profile.RunTotals.WeaponStatistics.Totals.Projectiles);
    }

    [Fact]
    [Trait("Category", "Weapon")]
    [Trait("Category", "Persistence")]
    public void PersistedNegativeCountersNormalizeToZeroAndAreFlagged()
    {
        var statistics = new WeaponStatisticsAggregate
        {
            Totals = new WeaponMetricTotals
            {
                FiringActions = -1,
                AmmunitionUnitsConsumed = -2,
                Projectiles = -3
            }
        };

        var result = WeaponStatisticsReducer.NormalizePersisted(statistics);

        Assert.True(result.Changed);
        Assert.True(result.InvalidCounters);
        Assert.Equal(0, statistics.Totals.FiringActions);
        Assert.Equal(0, statistics.Totals.AmmunitionUnitsConsumed);
        Assert.Equal(0, statistics.Totals.Projectiles);
        WeaponStatisticsReducer.ValidateAggregate(statistics);
    }

    [Fact]
    [Trait("Category", "Weapon")]
    [Trait("Category", "Compatibility")]
    public void CurrentSupportedCapabilityCannotUpgradePersistedHistoricalUnavailability()
    {
        var historical = new MetricAvailability
        {
            State = AdapterCapabilityState.DisabledIncompatible,
            Provenance = string.Empty
        };

        var visible = WeaponStatisticsReducer.RestrictAvailability(
            historical,
            AdapterCapabilityState.Supported);

        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, visible);
    }

    private static RunLifecycleTracker StartedTracker()
    {
        var tracker = new RunLifecycleTracker(() => "run-1");
        tracker.Apply(Event(RunLifecycleEventKind.RaidInitialized, 0));
        tracker.Apply(Event(RunLifecycleEventKind.ControlReady, 0, Context()));
        return tracker;
    }

    private static RunLifecycleEvent Event(
        RunLifecycleEventKind kind,
        double seconds,
        RunStartContext? context = null) => new()
        {
            Kind = kind,
            TimestampUtc = Origin.AddSeconds(seconds),
            MonotonicSeconds = seconds,
            StartContext = context,
            NativeRaidId = "42"
        };

    private static RunStartContext Context() => new()
    {
        SaveGenerationId = "generation-1",
        Map = new MapIdentity { MapId = "map-1", DisplayName = "Map", IsKnown = true },
        IntegrityTags = IntegrityTags.Normal,
        LifecycleCapability = AdapterCapabilityState.Supported,
        MovementCapability = AdapterCapabilityState.Supported,
        MapCapability = AdapterCapabilityState.Supported,
        WeaponCapabilities = Capabilities()
    };

    internal static ShotRecorded Shot(
        string eventId,
        string weaponId,
        string weaponName,
        string ammunitionId,
        string ammunitionName,
        long firingActions,
        long ammunition,
        long projectiles) => new()
        {
            EventId = eventId,
            TimestampUtc = Origin,
            SaveGenerationId = "generation-1",
            RunId = "run-1",
            MapId = "map-1",
            GameplayContext = GameplayContext.Raid,
            IntegrityTags = IntegrityTags.Normal,
            WeaponId = weaponId,
            WeaponDisplayName = weaponName,
            AmmunitionId = ammunitionId,
            AmmunitionDisplayName = ammunitionName,
            FiringActionCount = firingActions,
            AmmunitionUnitsConsumed = ammunition,
            ProjectileCount = projectiles,
            Capabilities = Capabilities()
        };

    internal static WeaponMetricCapabilities Capabilities() => new()
    {
        FiringActions = Supported("public firing event"),
        AmmunitionConsumption = Supported("native loaded-ammunition consumption"),
        Projectiles = Supported("native ShotCount projectile loop"),
        WeaponIdentity = Supported("weapon TypeID"),
        AmmunitionIdentity = Supported("ammunition TypeID")
    };

    private static MetricAvailability Supported(string provenance) => new()
    {
        State = AdapterCapabilityState.Supported,
        Provenance = provenance
    };
}
