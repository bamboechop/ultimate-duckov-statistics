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
    public void ProvenActiveTotemSetBecomesRecurringOnlyAfterTwoCompletedRunMerges()
    {
        var lifetime = new EquipmentStatisticsAggregate();
        var run = Aggregate();
        var snapshot = Snapshot("totem-run", string.Empty, "totems:a");
        snapshot.Totems.Add(new TotemSnapshot
        {
            ItemId = "totem:a",
            DisplayName = "Totem A",
            CarryKind = TotemCarryKind.DirectSlot,
            ContainerId = "character",
            ActivationState = TotemActivationState.ProvenActive
        });
        EquipmentStatisticsReducer.Observe(run, snapshot, 0);
        EquipmentStatisticsReducer.Advance(run, 10);

        EquipmentStatisticsReducer.Merge(lifetime, run);
        Assert.Equal(1, lifetime.TotemSets["totems:a"].RunOccurrences);
        EquipmentStatisticsReducer.Merge(lifetime, run);
        Assert.Equal(2, lifetime.TotemSets["totems:a"].RunOccurrences);
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
        equipment.TotemStates["tote|totem|unknown|copy:1"] = new EquipmentDurationAggregate
        { Id = "tote|totem|unknown|copy:1", DisplayName = "Totem [Unknown]", ActiveDurationSeconds = 7 };
        equipment.Loadouts["single"] = new EquipmentDurationAggregate { Id = "single", ActiveDurationSeconds = 5, RunOccurrences = 1 };
        equipment.Loadouts["recurring"] = new EquipmentDurationAggregate { Id = "recurring", ActiveDurationSeconds = 15, RunOccurrences = 2 };
        equipment.CombatAssociations["association"] = new EquipmentCombatAssociationAggregate
        { LoadoutId = "recurring", SelectedWeaponSlotId = "slot:primary", SelectedWeaponId = "weapon:a", TotemSetId = "totems:a", DamageDealt = 9 };

        var bundle = StatisticsExporter.Create(profile, Now);
        using var json = JsonDocument.Parse(bundle.Json);
        var jsonEquipment = json.RootElement.GetProperty("RunTotals").GetProperty("EquipmentStatistics");

        Assert.Equal(12, jsonEquipment.GetProperty("Items").GetProperty("slot|item").GetProperty("ActiveDurationSeconds").GetDouble());
        Assert.Contains("lifetime,generation,item,slot|item,Vest,12,0", bundle.EquipmentTotalsCsv);
        Assert.Contains("lifetime,generation,totem_state,tote|totem|unknown|copy:1,Totem [Unknown],7,0", bundle.EquipmentTotalsCsv);
        Assert.Contains("recurring", bundle.RecurringLoadoutsCsv);
        Assert.DoesNotContain("single", bundle.RecurringLoadoutsCsv);
        Assert.StartsWith("scope,scope_id,loadout_id,selected_weapon_slot_id", bundle.EquipmentCombatCsv);
        Assert.Contains("lifetime,generation,recurring,slot:primary,weapon:a,totems:a,0,0,0,9", bundle.EquipmentCombatCsv);
    }

    [Fact]
    public void InvalidMainObservationClosesTheOpenIntervalUntilAValidSnapshotReturns()
    {
        var tracker = new RunLifecycleTracker(() => "run-equipment-stale-main");
        tracker.Apply(Lifecycle(RunLifecycleEventKind.RaidInitialized, 0));
        tracker.Apply(Lifecycle(RunLifecycleEventKind.ControlReady, 0, new RunStartContext
        {
            SaveGenerationId = "generation",
            Map = new MapIdentity { MapId = "map", DisplayName = "Map", IsKnown = true },
            IntegrityTags = IntegrityTags.Normal,
            LifecycleCapability = AdapterCapabilityState.Supported,
            EquipmentCapabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities()
        }));
        tracker.ObserveEquipment(Snapshot("before-loss", "weapon:a", "totems:none"));

        Assert.True(tracker.SuspendEquipment(Now.AddSeconds(3), 3));
        Assert.False(tracker.SuspendEquipment(Now.AddSeconds(8), 8));
        tracker.ObserveEquipment(Snapshot("after-return", "weapon:a", "totems:none"), Now.AddSeconds(10), 10);
        var summary = tracker.Apply(Lifecycle(RunLifecycleEventKind.Extracted, 12)).Completed!;

        Assert.Equal(3, summary.EquipmentStatistics.Loadouts["loadout:before-loss"].ActiveDurationSeconds);
        Assert.Equal(2, summary.EquipmentStatistics.Loadouts["loadout:after-return"].ActiveDurationSeconds);
        Assert.Equal(4, summary.EquipmentStatistics.TransitionCount);
        Assert.Null(summary.EquipmentStatistics.CurrentSnapshot);
    }

    [Fact]
    public void UnknownDuplicateToteContentsPreserveMultiplicityAsPresenceTimeOnly()
    {
        var aggregate = Aggregate();
        var snapshot = Snapshot("duplicate-tote", string.Empty, "totems:none");
        snapshot.Totems = new List<TotemSnapshot>
        {
            Tote("totem:a"),
            Tote("totem:a")
        };

        EquipmentStatisticsReducer.Observe(aggregate, snapshot, 0);
        EquipmentStatisticsReducer.Advance(aggregate, 5);

        Assert.Equal(2, aggregate.TotemStates.Count);
        Assert.All(aggregate.TotemStates.Values, row => Assert.Equal(5, row.ActiveDurationSeconds));
        Assert.Empty(aggregate.TotemSets);
        Assert.Contains(aggregate.TotemStates.Keys, key => key.EndsWith("|copy:1", StringComparison.Ordinal));
        Assert.Contains(aggregate.TotemStates.Keys, key => key.EndsWith("|copy:2", StringComparison.Ordinal));
    }

    [Fact]
    public void DurationCombatAndCounterOverflowSaturateWithoutBecomingNonFiniteOrDecreasing()
    {
        var aggregate = Aggregate();
        var snapshot = Snapshot("overflow", "weapon:a", "totems:none");
        EquipmentStatisticsReducer.Observe(aggregate, snapshot, 0);
        aggregate.Loadouts[snapshot.LoadoutId] = new EquipmentDurationAggregate
        {
            Id = snapshot.LoadoutId,
            ActiveDurationSeconds = double.MaxValue
        };
        EquipmentStatisticsReducer.Advance(aggregate, 1);

        var association = new EquipmentEventAssociation { LoadoutId = "loadout:a", TotemSetId = "totems:none" };
        EquipmentStatisticsReducer.RecordCombat(aggregate, new CombatRecorded
        {
            EquipmentAssociation = association,
            ActualDamageDealt = double.MaxValue
        });
        EquipmentStatisticsReducer.RecordCombat(aggregate, new CombatRecorded
        {
            EquipmentAssociation = association,
            ActualDamageDealt = double.MaxValue
        });
        EquipmentStatisticsReducer.RecordShot(aggregate, new ShotRecorded
        {
            EquipmentAssociation = association,
            FiringActionCount = long.MaxValue
        });
        EquipmentStatisticsReducer.RecordShot(aggregate, new ShotRecorded
        {
            EquipmentAssociation = association,
            FiringActionCount = 1
        });
        EquipmentStatisticsReducer.RecordShot(aggregate, new ShotRecorded
        {
            EquipmentAssociation = association,
            FiringActionCount = -5
        });

        var row = Assert.Single(aggregate.CombatAssociations).Value;
        Assert.Equal(double.MaxValue, aggregate.Loadouts[snapshot.LoadoutId].ActiveDurationSeconds);
        Assert.Equal(double.MaxValue, row.DamageDealt);
        Assert.Equal(long.MaxValue, row.FiringActions);
    }

    [Fact]
    public void AttachmentAndSelectedSlotChangesCreateDistinctLoadoutsAndSelectionDurations()
    {
        var aggregate = Aggregate();
        var first = Snapshot("first", "weapon:a", "totems:none");
        first.Items.Add(new EquippedItemSnapshot
        {
            SlotId = "slot:secondary",
            SlotDisplayName = "Secondary",
            ItemId = "weapon:a",
            ItemDisplayName = "Rifle",
            Kind = EquipmentItemKind.Weapon,
            AttachmentSignature = "attachments:a"
        });
        var second = Snapshot("second", "weapon:a", "totems:none");
        second.Items.Add(new EquippedItemSnapshot
        {
            SlotId = "slot:secondary",
            SlotDisplayName = "Secondary",
            ItemId = "weapon:a",
            ItemDisplayName = "Rifle",
            Kind = EquipmentItemKind.Weapon,
            AttachmentSignature = "attachments:a"
        });
        second.SelectedWeaponSlotId = "slot:secondary";
        second.Items[0].AttachmentSignature = "attachments:b";

        EquipmentStatisticsReducer.Observe(aggregate, first, 0);
        EquipmentStatisticsReducer.Observe(aggregate, second, 4);
        EquipmentStatisticsReducer.Advance(aggregate, 10);

        Assert.Equal(4, aggregate.SelectedWeapons["slot:primary|weapon:a"].ActiveDurationSeconds);
        Assert.Equal(6, aggregate.SelectedWeapons["slot:secondary|weapon:a"].ActiveDurationSeconds);
        Assert.Contains("attachments=attachments:a", aggregate.Loadouts["loadout:first"].DisplayName);
        Assert.Contains("attachments=attachments:b", aggregate.Loadouts["loadout:second"].DisplayName);
    }

    [Fact]
    public void NonWeaponHeldItemCannotBecomeSelectedWeapon()
    {
        var aggregate = Aggregate();
        var snapshot = Snapshot("held-nonweapon", "weapon:a", "totems:none");
        snapshot.Items[0].Kind = EquipmentItemKind.Backpack;

        Assert.Throws<ArgumentException>(() => EquipmentStatisticsReducer.Observe(aggregate, snapshot, 0));
    }

    [Fact]
    public void NestedNormalizationMergesCanonicalDuplicatesAndIsIdempotent()
    {
        var aggregate = Aggregate();
        aggregate.TotemStates = null!;
        aggregate.Loadouts[" loadout:a "] = new EquipmentDurationAggregate
        {
            Id = "loadout:a",
            DisplayName = " First ",
            ActiveDurationSeconds = 2,
            RunOccurrences = 1
        };
        aggregate.Loadouts["loadout:a"] = new EquipmentDurationAggregate
        {
            Id = " loadout:a ",
            DisplayName = "Second",
            ActiveDurationSeconds = 3,
            RunOccurrences = 2
        };
        aggregate.Capabilities.EquipmentSlots.State = (AdapterCapabilityState)int.MaxValue;
        aggregate.TransitionCount = -4;

        Assert.True(EquipmentStatisticsReducer.NormalizePersisted(aggregate));
        var row = Assert.Single(aggregate.Loadouts).Value;
        Assert.Equal("loadout:a", row.Id);
        Assert.Equal(5, row.ActiveDurationSeconds);
        Assert.Equal(3, row.RunOccurrences);
        Assert.NotNull(aggregate.TotemStates);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, aggregate.Capabilities.EquipmentSlots.State);
        Assert.Equal(0, aggregate.TransitionCount);
        Assert.True(aggregate.WasRepairedFromInvalidState);
        Assert.False(EquipmentStatisticsReducer.NormalizePersisted(aggregate));
    }

    [Fact]
    public void EquipReplaceMoveUnequipAndClearPreserveSlotAndStableItemIdentity()
    {
        var aggregate = Aggregate();
        var initial = MatrixSnapshot("initial",
            Item("slot:primary", "weapon:gun", EquipmentItemKind.Weapon),
            Item("slot:melee", "weapon:knife", EquipmentItemKind.Weapon),
            Item("slot:backpack", "item:pack-a", EquipmentItemKind.Backpack));
        initial.SelectedWeaponId = "weapon:gun";
        initial.SelectedWeaponSlotId = "slot:primary";
        var equippedFace = MatrixSnapshot("face",
            Item("slot:primary", "weapon:gun", EquipmentItemKind.Weapon),
            Item("slot:melee", "weapon:knife", EquipmentItemKind.Weapon),
            Item("slot:backpack", "item:pack-a", EquipmentItemKind.Backpack),
            Item("slot:face", "modded:face", EquipmentItemKind.Other));
        equippedFace.SelectedWeaponId = "weapon:gun";
        equippedFace.SelectedWeaponSlotId = "slot:primary";
        var replaced = MatrixSnapshot("replace",
            Item("slot:primary", "weapon:gun", EquipmentItemKind.Weapon),
            Item("slot:melee", "weapon:knife", EquipmentItemKind.Weapon),
            Item("slot:backpack", "item:pack-b", EquipmentItemKind.Backpack),
            Item("slot:face", "modded:face", EquipmentItemKind.Other));
        replaced.SelectedWeaponId = "weapon:gun";
        replaced.SelectedWeaponSlotId = "slot:primary";
        var moved = MatrixSnapshot("move",
            Item("slot:secondary", "weapon:gun", EquipmentItemKind.Weapon),
            Item("slot:melee", "weapon:knife", EquipmentItemKind.Weapon),
            Item("slot:backpack", "item:pack-b", EquipmentItemKind.Backpack),
            Item("slot:face", "modded:face", EquipmentItemKind.Other));
        moved.SelectedWeaponId = "weapon:gun";
        moved.SelectedWeaponSlotId = "slot:secondary";
        var cleared = MatrixSnapshot("clear");

        EquipmentStatisticsReducer.Observe(aggregate, initial, 0);
        EquipmentStatisticsReducer.Observe(aggregate, equippedFace, 1);
        EquipmentStatisticsReducer.Observe(aggregate, replaced, 2);
        EquipmentStatisticsReducer.Observe(aggregate, moved, 3);
        EquipmentStatisticsReducer.Observe(aggregate, cleared, 4);
        EquipmentStatisticsReducer.Advance(aggregate, 5);

        Assert.Equal(5, aggregate.TransitionCount);
        Assert.Equal(3, Duration("slot:primary", "weapon:gun"));
        Assert.Equal(1, Duration("slot:secondary", "weapon:gun"));
        Assert.Equal(4, Duration("slot:melee", "weapon:knife"));
        Assert.Equal(2, Duration("slot:backpack", "item:pack-a"));
        Assert.Equal(2, Duration("slot:backpack", "item:pack-b"));
        Assert.Equal(3, Duration("slot:face", "modded:face"));
        Assert.Equal(3, aggregate.SelectedWeapons["slot:primary|weapon:gun"].ActiveDurationSeconds);
        Assert.Equal(1, aggregate.SelectedWeapons["slot:secondary|weapon:gun"].ActiveDurationSeconds);

        double Duration(string slot, string item) => aggregate.Items.Single(pair =>
            pair.Key.StartsWith(slot + "|" + item + "|", StringComparison.Ordinal)).Value.ActiveDurationSeconds;
    }

    [Fact]
    public void MutableDisplayNameDoesNotCreateASecondStableIdentity()
    {
        var aggregate = Aggregate();
        var first = MatrixSnapshot("stable", Item("slot:armor", "item:stable", EquipmentItemKind.Armor));
        first.Items[0].ItemDisplayName = "Localized name A";
        var renamed = MatrixSnapshot("stable", Item("slot:armor", "item:stable", EquipmentItemKind.Armor));
        renamed.Items[0].ItemDisplayName = "Localized name B";

        Assert.True(EquipmentStatisticsReducer.Observe(aggregate, first, 0));
        Assert.False(EquipmentStatisticsReducer.Observe(aggregate, renamed, 2));
        EquipmentStatisticsReducer.Advance(aggregate, 5);

        var row = Assert.Single(aggregate.Items).Value;
        Assert.Equal(5, row.ActiveDurationSeconds);
        Assert.Equal("Localized name B", row.DisplayName);
        Assert.Equal(1, aggregate.TransitionCount);
    }

    [Fact]
    public void CompletedRunEquipmentMatchesRunMapAndLifetimeWhileInterruptedSetsDoNotBecomeRecurring()
    {
        var profile = new ProfileStatistics
        {
            SchemaVersion = 6,
            SaveGenerationId = "generation",
            CreatedUtc = Now,
            UpdatedUtc = Now
        };
        var completedEquipment = Aggregate();
        EquipmentStatisticsReducer.Observe(completedEquipment, Snapshot("shared", string.Empty, "totems:none"), 0);
        EquipmentStatisticsReducer.Advance(completedEquipment, 5);
        var interruptedEquipment = EquipmentStatisticsReducer.Clone(completedEquipment);

        Assert.True(RunReducer.Apply(profile, Run("completed", RunOutcome.Extracted, completedEquipment)));
        Assert.True(RunReducer.Apply(profile, Run("interrupted", RunOutcome.Interrupted, interruptedEquipment)));

        Assert.Equal(10, profile.RunTotals.EquipmentStatistics.Loadouts["loadout:shared"].ActiveDurationSeconds);
        Assert.Equal(10, profile.RunTotals.Maps["map"].EquipmentStatistics.Loadouts["loadout:shared"].ActiveDurationSeconds);
        Assert.Equal(1, profile.RunTotals.EquipmentStatistics.Loadouts["loadout:shared"].RunOccurrences);
        Assert.Equal(1, profile.RunTotals.Maps["map"].EquipmentStatistics.Loadouts["loadout:shared"].RunOccurrences);
        Assert.Equal(5, profile.Runs.Single(value => value.RunId == "completed").EquipmentStatistics.Loadouts["loadout:shared"].ActiveDurationSeconds);
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
            new() { SlotId = "slot:primary", SlotDisplayName = "Primary", ItemId = "weapon:a", ItemDisplayName = "Rifle", Kind = EquipmentItemKind.Weapon, AttachmentSignature = "attachments:a" }
        }
    };

    private static TotemSnapshot Tote(string itemId) => new()
    {
        ItemId = itemId,
        DisplayName = "Totem",
        CarryKind = TotemCarryKind.ToteInventory,
        ContainerId = "tote:a",
        ActivationState = TotemActivationState.Unknown
    };

    private static EquippedItemSnapshot Item(string slotId, string itemId, EquipmentItemKind kind) => new()
    {
        SlotId = slotId,
        SlotDisplayName = slotId,
        ItemId = itemId,
        ItemDisplayName = itemId,
        Kind = kind,
        AttachmentSignature = "attachments:none"
    };

    private static EquipmentSnapshot MatrixSnapshot(string id, params EquippedItemSnapshot[] items) => new()
    {
        SnapshotId = "snapshot:" + id,
        LoadoutId = "loadout:" + id,
        TotemSetId = "totems:none",
        Items = items.ToList()
    };

    private static RunSummary Run(string id, RunOutcome outcome, EquipmentStatisticsAggregate equipment) => new()
    {
        RunId = id,
        SaveGenerationId = "generation",
        NativeRaidId = "raid:" + id,
        MapId = "map",
        MapDisplayName = "Map",
        MapKnown = true,
        StartedUtc = Now,
        EndedUtc = Now.AddSeconds(5),
        ActiveDurationSeconds = 5,
        WallClockDurationSeconds = 5,
        Outcome = outcome,
        LifecycleCapability = AdapterCapabilityState.Supported,
        MovementCapability = AdapterCapabilityState.Supported,
        MapCapability = AdapterCapabilityState.Supported,
        EquipmentStatistics = equipment
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
