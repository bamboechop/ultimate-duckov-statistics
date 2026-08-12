using UltimateDuckovStatistics.Core.Compatibility;
using System.Globalization;
using System.Text.Json;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class EquipmentStatisticsTests
{
    private static readonly DateTime Now = new(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RepeatedSnapshotIsIdempotentAndDurationUsesActiveTime()
    {
        var aggregate = Aggregate();
        var snapshot = Snapshot("one", "weapon:a", "totems:a");
        snapshot.Totems.Add(new TotemSnapshot
        {
            ItemId = "totem:a",
            ContainerId = "character",
            CarryKind = TotemCarryKind.DirectSlot,
            ActivationState = TotemActivationState.ProvenActive
        });

        Assert.True(EquipmentStatisticsReducer.Observe(aggregate, snapshot, 0));
        Assert.False(EquipmentStatisticsReducer.Observe(aggregate, snapshot, 3));
        EquipmentStatisticsReducer.Advance(aggregate, 8);

        Assert.Equal(1, aggregate.TransitionCount);
        Assert.Equal(8, aggregate.Loadouts["loadout:one"].ActiveDurationSeconds);
        Assert.Equal(8, aggregate.SelectedWeapons["slot:primary|weapon:a"].ActiveDurationSeconds);
        Assert.Equal(8, aggregate.TotemSets["totems:a"].ActiveDurationSeconds);
    }

    [Fact]
    public void LifecyclePauseDoesNotAccrueEquipmentDuration()
    {
        var tracker = new RunLifecycleTracker(() => "run-equipment");
        tracker.Apply(Lifecycle(RunLifecycleEventKind.RaidInitialized, 0));
        tracker.Apply(Lifecycle(RunLifecycleEventKind.ControlReady, 0, new RunStartContext
        {
            SaveGenerationId = "generation",
            Map = new MapIdentity { MapId = "map", DisplayName = "Map", IsKnown = true },
            IntegrityTags = IntegrityTags.Normal,
            LifecycleCapability = AdapterCapabilityState.Supported,
            EquipmentCapabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities()
        }));
        tracker.ObserveEquipment(Snapshot("pause", string.Empty, "totems:none"));
        tracker.Tick(Now.AddSeconds(2), 2);
        tracker.Apply(Lifecycle(RunLifecycleEventKind.PauseStarted, 2));
        tracker.Tick(Now.AddSeconds(7), 7);
        tracker.Apply(Lifecycle(RunLifecycleEventKind.PauseEnded, 7));
        var summary = tracker.Apply(Lifecycle(RunLifecycleEventKind.Extracted, 10)).Completed!;

        Assert.Equal(5, summary.ActiveDurationSeconds);
        Assert.Equal(5, summary.EquipmentStatistics.Loadouts["loadout:pause"].ActiveDurationSeconds);
    }

    [Fact]
    public void TimedEquipmentChangeClosesPriorLoadoutAtCallbackTime()
    {
        var tracker = new RunLifecycleTracker(() => "run-equipment-boundary");
        tracker.Apply(Lifecycle(RunLifecycleEventKind.RaidInitialized, 0));
        tracker.Apply(Lifecycle(RunLifecycleEventKind.ControlReady, 0, new RunStartContext
        {
            SaveGenerationId = "generation",
            Map = new MapIdentity { MapId = "map", DisplayName = "Map", IsKnown = true },
            IntegrityTags = IntegrityTags.Normal,
            LifecycleCapability = AdapterCapabilityState.Supported,
            EquipmentCapabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities()
        }));
        tracker.ObserveEquipment(Snapshot("before", string.Empty, "totems:none"));
        tracker.ObserveEquipment(Snapshot("after", string.Empty, "totems:none"), Now.AddSeconds(2.5), 2.5);
        var summary = tracker.Apply(Lifecycle(RunLifecycleEventKind.Extracted, 5)).Completed!;

        Assert.Equal(2.5, summary.EquipmentStatistics.Loadouts["loadout:before"].ActiveDurationSeconds);
        Assert.Equal(2.5, summary.EquipmentStatistics.Loadouts["loadout:after"].ActiveDurationSeconds);
    }

    [Fact]
    public void TransitionHistoryIsBoundedWithoutLosingAggregateTime()
    {
        var aggregate = Aggregate();
        for (var index = 0; index < 300; index++)
            EquipmentStatisticsReducer.Observe(aggregate, Snapshot(index.ToString(CultureInfo.InvariantCulture), string.Empty, "totems:none"), index);
        EquipmentStatisticsReducer.Advance(aggregate, 300);

        Assert.Equal(300, aggregate.TransitionCount);
        Assert.Equal(EquipmentStatisticsAggregate.TransitionCapacity, aggregate.Transitions.Count);
        Assert.True(aggregate.TransitionsTruncated);
        Assert.Equal(300, aggregate.ObservedActiveDurationSeconds);
    }

    [Fact]
    public void EventTimeAssociationFeedsCombatBreakdown()
    {
        var aggregate = Aggregate();
        var association = new EquipmentEventAssociation
        { LoadoutId = "loadout:a", SelectedWeaponId = "weapon:a", TotemSetId = "totems:a" };
        EquipmentStatisticsReducer.RecordShot(aggregate, new ShotRecorded
        { EquipmentAssociation = association, FiringActionCount = 1, AmmunitionUnitsConsumed = 2, ProjectileCount = 3 });
        EquipmentStatisticsReducer.RecordCombat(aggregate, new CombatRecorded
        { EquipmentAssociation = association, ActualDamageDealt = 12.5, RangedHits = 1, EnemiesKilled = 1 });

        var row = Assert.Single(aggregate.CombatAssociations).Value;
        Assert.Equal(1, row.FiringActions);
        Assert.Equal(2, row.AmmunitionUnitsConsumed);
        Assert.Equal(3, row.Projectiles);
        Assert.Equal(12.5, row.DamageDealt);
        Assert.Equal(1, row.EnemiesKilled);
    }

    [Fact]
    public void LifetimeLoadoutsBecomeRecurringOnlyAfterTwoRunMerges()
    {
        var lifetime = new EquipmentStatisticsAggregate();
        var run = Aggregate();
        EquipmentStatisticsReducer.Observe(run, Snapshot("same", string.Empty, "totems:none"), 0);
        EquipmentStatisticsReducer.Advance(run, 10);

        EquipmentStatisticsReducer.Merge(lifetime, run);
        Assert.Equal(1, lifetime.Loadouts["loadout:same"].RunOccurrences);
        Assert.Equal(AdapterCapabilityState.Supported, lifetime.Capabilities.EquipmentSlots.State);
        EquipmentStatisticsReducer.Merge(lifetime, run);
        Assert.Equal(2, lifetime.Loadouts["loadout:same"].RunOccurrences);
    }

    [Fact]
    public void SchemaFiveMigrationLeavesHistoricalEquipmentUnavailable()
    {
        var profile = Profile(5);

        Assert.True(ProfileMigrator.Migrate(profile));

        Assert.Equal(6, profile.SchemaVersion);
        var equipment = profile.Statistics.RunTotals.EquipmentStatistics;
        Assert.True(equipment.HistoricalUnavailable);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, equipment.Capabilities.EquipmentSlots.State);
        Assert.Contains("predates M6", equipment.Capabilities.EquipmentSlots.Provenance);
    }

    [Fact]
    public void ToteActivationRemainsDisabledWhilePresenceIsSupported()
    {
        var capabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities();

        Assert.Equal(AdapterCapabilityState.Supported, capabilities.DirectTotems.State);
        Assert.Equal(AdapterCapabilityState.Supported, capabilities.ToteContents.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, capabilities.ToteActivation.State);
    }

    [Fact]
    public void EmptyCurrentGenerationUsesLiveCapabilitiesButHistoricalGenerationStaysUnavailable()
    {
        var current = Profile(6);
        current.Capabilities = EquipmentNativeContractPolicy.ToRecords(
            EquipmentNativeContractPolicy.CreateSupportedCapabilities(), "current").ToList();
        var currentModel = EquipmentStatisticsViewModelFactory.Create(current);
        Assert.Equal(AdapterCapabilityState.Supported, currentModel.Capabilities.EquipmentSlots.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, currentModel.Capabilities.ToteActivation.State);

        var historical = Profile(5);
        ProfileMigrator.Migrate(historical);
        historical.Capabilities = current.Capabilities;
        var historicalModel = EquipmentStatisticsViewModelFactory.Create(historical);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, historicalModel.Capabilities.EquipmentSlots.State);
    }

    [Fact]
    public void UnknownTotePresenceDoesNotAccumulateActiveTotemSetTime()
    {
        var aggregate = Aggregate();
        var snapshot = Snapshot("tote", string.Empty, "totems:empty");
        snapshot.Totems.Add(new TotemSnapshot
        {
            ItemId = "totem:a",
            ContainerId = "tote:a",
            CarryKind = TotemCarryKind.ToteInventory,
            ActivationState = TotemActivationState.Unknown
        });

        EquipmentStatisticsReducer.Observe(aggregate, snapshot, 0);
        EquipmentStatisticsReducer.Advance(aggregate, 10);

        Assert.Empty(aggregate.TotemSets);
        Assert.Single(aggregate.CurrentSnapshot!.Totems);
    }

    [Fact]
    public void SnapshotRejectsSelectionThatIsNotInTheClaimedSlot()
    {
        var aggregate = Aggregate();
        var snapshot = Snapshot("bad-selection", "weapon:a", "totems:none");
        snapshot.SelectedWeaponSlotId = "slot:secondary";

        Assert.Throws<ArgumentException>(() => EquipmentStatisticsReducer.Observe(aggregate, snapshot, 0));
    }

    [Fact]
    public void RecoveryValidationRejectsNegativeEquipmentDuration()
    {
        var aggregate = Aggregate();
        aggregate.Items["bad"] = new EquipmentDurationAggregate { Id = "bad", ActiveDurationSeconds = -1 };

        Assert.Throws<ArgumentException>(() => EquipmentStatisticsReducer.ValidateRecoveryCandidate(aggregate));
    }

    [Fact]
    public void ExportsIncludeEquipmentAndOnlyRecurringLifetimeLoadouts()
    {
        var profile = Profile(6);
        var equipment = profile.Statistics.RunTotals.EquipmentStatistics;
        equipment.Items["slot|item"] = new EquipmentDurationAggregate { Id = "slot|item", DisplayName = "Vest", ActiveDurationSeconds = 12 };
        equipment.Loadouts["single"] = new EquipmentDurationAggregate { Id = "single", ActiveDurationSeconds = 5, RunOccurrences = 1 };
        equipment.Loadouts["recurring"] = new EquipmentDurationAggregate { Id = "recurring", ActiveDurationSeconds = 15, RunOccurrences = 2 };
        equipment.CombatAssociations["association"] = new EquipmentCombatAssociationAggregate
        { LoadoutId = "recurring", SelectedWeaponSlotId = "slot:primary", SelectedWeaponId = "weapon:a", TotemSetId = "totems:a", DamageDealt = 9 };

        var bundle = StatisticsExporter.Create(profile, Now);
        using var json = JsonDocument.Parse(bundle.Json);
        var jsonEquipment = json.RootElement.GetProperty("RunTotals").GetProperty("EquipmentStatistics");

        Assert.Equal(12, jsonEquipment.GetProperty("Items").GetProperty("slot|item").GetProperty("ActiveDurationSeconds").GetDouble());
        Assert.Contains("lifetime,generation,item,slot|item,Vest,12,0", bundle.EquipmentTotalsCsv);
        Assert.Contains("recurring", bundle.RecurringLoadoutsCsv);
        Assert.DoesNotContain("single", bundle.RecurringLoadoutsCsv);
        Assert.StartsWith("scope,scope_id,loadout_id,selected_weapon_slot_id", bundle.EquipmentCombatCsv);
        Assert.Contains("lifetime,generation,recurring,slot:primary,weapon:a,totems:a,0,0,0,9", bundle.EquipmentCombatCsv);
    }

    private static EquipmentStatisticsAggregate Aggregate() => new()
    { Capabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities() };

    private static EquipmentSnapshot Snapshot(string id, string selected, string totems) => new()
    {
        SnapshotId = "snapshot:" + id,
        LoadoutId = "loadout:" + id,
        SelectedWeaponId = selected,
        SelectedWeaponSlotId = string.IsNullOrWhiteSpace(selected) ? string.Empty : "slot:primary",
        TotemSetId = totems,
        Items = new List<EquippedItemSnapshot>
        {
            new() { SlotId = "slot:primary", ItemId = "weapon:a", ItemDisplayName = "Rifle", AttachmentSignature = "attachments:a" }
        }
    };

    private static ProfileDocument Profile(int schema) => new()
    {
        SchemaVersion = schema,
        GenerationId = "generation",
        CreatedUtc = Now,
        UpdatedUtc = Now,
        Identity = new SaveIdentitySnapshot(),
        Statistics = new ProfileStatistics
        {
            SchemaVersion = schema,
            SaveGenerationId = "generation",
            CreatedUtc = Now,
            UpdatedUtc = Now
        }
    };

    private static RunLifecycleEvent Lifecycle(
        RunLifecycleEventKind kind,
        double seconds,
        RunStartContext? context = null) => new()
        {
            Kind = kind,
            TimestampUtc = Now.AddSeconds(seconds),
            MonotonicSeconds = seconds,
            NativeRaidId = "raid",
            StartContext = context
        };
}
