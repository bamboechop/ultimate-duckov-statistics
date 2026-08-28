using ItemStatsSystem;
using ItemStatsSystem.Items;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeHotPathDiagnosticsTestGroup.CollectionName)]
public sealed class NativeEquipmentAdapterPerformanceTests : IDisposable
{
    public NativeEquipmentAdapterPerformanceTests()
    {
        CharacterMainControl.ResetNativeState();
        UnityEngine.Application.version = "2.3.30";
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void AssociationsUseImmediateEventCacheAndWatchdogBuildsOnlyOncePerSecond()
    {
        var now = 0d;
        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        var weapon = new Item { TypeID = 700, DisplayName = "Weapon" };
        var primary = new Slot { Key = "PrimaryWeapon", DisplayName = "Primary", Content = weapon };
        characterItem.Slots.Add(primary);
        var main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem,
            CurrentHoldItemAgent = new DuckovItemAgent { Item = weapon }
        };
        CharacterMainControl.Main = main;
        var published = new List<EquipmentSnapshot>();
        using var adapter = new NativeEquipmentAdapter(
            () => true,
            snapshot =>
            {
                published.Add(snapshot);
                return true;
            },
            () => true,
            _ => { },
            _ => { },
            () => now);

        adapter.Initialize();
        var initial = Assert.Single(published);
        NativeHotPathDiagnostics.Reset();

        for (var index = 0; index < 60; index++)
        {
            var association = adapter.CaptureAssociation();
            Assert.Equal(initial.LoadoutId, association.LoadoutId);
            Assert.Equal(initial.SelectedWeaponId, association.SelectedWeaponId);
            Assert.Equal(initial.SelectedWeaponSlotId, association.SelectedWeaponSlotId);
            Assert.Equal(initial.TotemSetId, association.TotemSetId);
        }

        Assert.Equal(60, NativeHotPathDiagnostics.Snapshot().EquipmentAssociationRequests);
        Assert.Equal(0, NativeHotPathDiagnostics.Snapshot().EquipmentSnapshotBuilds);
        now = 0.999;
        adapter.Tick();
        Assert.Equal(0, NativeHotPathDiagnostics.Snapshot().EquipmentSnapshotBuilds);
        now = 1;
        adapter.Tick();
        Assert.Equal(1, NativeHotPathDiagnostics.Snapshot().EquipmentSnapshotBuilds);
        Assert.Single(published);

        weapon.Slots.Add(new Slot
        {
            Key = "Muzzle",
            Content = new Item { TypeID = 701, DisplayName = "Muzzle" }
        });
        characterItem.RaiseItemTreeChanged();

        Assert.Equal(2, published.Count);
        Assert.NotEqual(initial.LoadoutId, adapter.CaptureAssociation().LoadoutId);
    }

    [Fact]
    public void UnchangedLoadoutIsRepublishedWhenTheRunSegmentContextChanges()
    {
        var now = 0d;
        var observationContext = "run-one\nsegment-one";
        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        var weapon = new Item { TypeID = 700, DisplayName = "Weapon" };
        characterItem.Slots.Add(new Slot { Key = "PrimaryWeapon", DisplayName = "Primary", Content = weapon });
        CharacterMainControl.Main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem,
            CurrentHoldItemAgent = new DuckovItemAgent { Item = weapon }
        };
        var published = new List<EquipmentSnapshot>();
        using var adapter = new NativeEquipmentAdapter(
            () => true,
            snapshot =>
            {
                published.Add(snapshot);
                return true;
            },
            () => true,
            _ => { },
            _ => { },
            () => now,
            () => observationContext);

        adapter.Initialize();
        Assert.Single(published);

        observationContext = "run-one\nsegment-two";
        var association = adapter.CaptureAssociation();

        Assert.Equal(2, published.Count);
        Assert.Equal(published[0].SnapshotId, published[1].SnapshotId);
        Assert.Equal(published[1].LoadoutId, association.LoadoutId);
    }

    [Fact]
    public void PermanentlyMissingSegmentContextKeepsOverallEquipmentAssociationCached()
    {
        var now = 0d;
        var invalidations = 0;
        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        var weapon = new Item { TypeID = 700, DisplayName = "Weapon" };
        characterItem.Slots.Add(new Slot { Key = "PrimaryWeapon", DisplayName = "Primary", Content = weapon });
        CharacterMainControl.Main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem,
            CurrentHoldItemAgent = new DuckovItemAgent { Item = weapon }
        };
        var published = new List<EquipmentSnapshot>();
        using var adapter = new NativeEquipmentAdapter(
            () => true,
            snapshot =>
            {
                published.Add(snapshot);
                return true;
            },
            () =>
            {
                invalidations++;
                return true;
            },
            _ => { },
            _ => { },
            () => now,
            () => null);

        adapter.Initialize();
        var initial = Assert.Single(published);

        for (var index = 0; index < 60; index++)
        {
            var association = adapter.CaptureAssociation();
            Assert.Equal(initial.LoadoutId, association.LoadoutId);
            Assert.Equal(initial.SelectedWeaponId, association.SelectedWeaponId);
            Assert.Equal(initial.SelectedWeaponSlotId, association.SelectedWeaponSlotId);
            Assert.Equal(initial.TotemSetId, association.TotemSetId);
        }

        now = 1;
        adapter.Tick();

        Assert.Single(published);
        Assert.Equal(0, invalidations);
    }

    [Fact]
    public void LosingSegmentContextRepublishesOverallOnceWithoutInvalidatingAssociation()
    {
        var now = 0d;
        string? observationContext = "run-one\nsegment-one";
        var invalidations = 0;
        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        var weapon = new Item { TypeID = 700, DisplayName = "Weapon" };
        characterItem.Slots.Add(new Slot { Key = "PrimaryWeapon", DisplayName = "Primary", Content = weapon });
        CharacterMainControl.Main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem,
            CurrentHoldItemAgent = new DuckovItemAgent { Item = weapon }
        };
        var published = new List<EquipmentSnapshot>();
        using var adapter = new NativeEquipmentAdapter(
            () => true,
            snapshot =>
            {
                published.Add(snapshot);
                return true;
            },
            () =>
            {
                invalidations++;
                return true;
            },
            _ => { },
            _ => { },
            () => now,
            () => observationContext);

        adapter.Initialize();
        var initial = Assert.Single(published);
        observationContext = null;

        var association = adapter.CaptureAssociation();
        var repeatedAssociation = adapter.CaptureAssociation();

        Assert.Equal(2, published.Count);
        Assert.Equal(initial.SnapshotId, published[1].SnapshotId);
        Assert.Equal(initial.LoadoutId, association.LoadoutId);
        Assert.Equal(initial.SelectedWeaponId, association.SelectedWeaponId);
        Assert.Equal(association.LoadoutId, repeatedAssociation.LoadoutId);
        Assert.Equal(0, invalidations);
    }

    [Fact]
    [Trait("Category", "M14")]
    public void PartialNestedSlotEvidenceDegradesOnlyNestedTrackingWhileRootDurationsContinue()
    {
        var now = 0d;
        var tracker = new RunLifecycleTracker(() => "run-partial-nested-evidence");
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.RaidInitialized,
            TimestampUtc = DateTime.UnixEpoch,
            MonotonicSeconds = 0,
            NativeRaidId = "raid"
        });
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.ControlReady,
            TimestampUtc = DateTime.UnixEpoch,
            MonotonicSeconds = 0,
            NativeRaidId = "raid",
            StartContext = new RunStartContext
            {
                SaveGenerationId = "generation",
                NativeRaidId = "raid",
                Map = new MapIdentity
                {
                    MapId = "duckov:map:test",
                    DisplayName = "Test",
                    IsKnown = true
                },
                IntegrityTags = IntegrityTags.Normal,
                GameVersion = "2.3.30",
                GameBuild = "test",
                LifecycleCapability = AdapterCapabilityState.Supported,
                EquipmentCapabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities()
            }
        });

        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        var armor = new Item { TypeID = 800, DisplayName = "Armor" };
        armor.Slots.Add(new Slot
        {
            Key = "Pouch",
            DisplayName = "Pouch",
            Content = new Item { TypeID = 801, DisplayName = "Valid child" }
        });
        var backpack = new Item { TypeID = 900, DisplayName = "Backpack" };
        backpack.Slots.Add(null!);
        backpack.Slots.Add(new Slot
        {
            Key = "Cube",
            DisplayName = "Cube",
            Content = new Item { TypeID = 901, DisplayName = "Readable sibling" }
        });
        characterItem.Slots.Add(new Slot { Key = "Armor", DisplayName = "Armor", Content = armor });
        characterItem.Slots.Add(new Slot { Key = "Backpack", DisplayName = "Backpack", Content = backpack });
        CharacterMainControl.Main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem
        };
        var invalidations = 0;
        using var adapter = new NativeEquipmentAdapter(
            () => true,
            snapshot => tracker.ObserveEquipment(snapshot, DateTime.UnixEpoch.AddSeconds(now), now),
            () =>
            {
                invalidations++;
                return tracker.SuspendEquipment(DateTime.UnixEpoch.AddSeconds(now), now);
            },
            _ => { },
            _ => { },
            () => now,
            () => tracker.ActiveSegmentId);

        adapter.Initialize();
        now = 5;
        adapter.Tick();
        tracker.Tick(DateTime.UnixEpoch.AddSeconds(now), now);
        var equipment = tracker.CreateCheckpoint(DateTime.UnixEpoch.AddSeconds(now), now)!.EquipmentStatistics;

        Assert.Equal(0, invalidations);
        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, adapter.MetricCapabilities.NestedSlotState.State);
        Assert.Equal(AdapterCapabilityState.Supported, equipment.Capabilities.CharacterSlotState.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, equipment.Capabilities.NestedSlotState.State);
        Assert.Equal(5, Assert.Single(equipment.CharacterSlotStates.Values, value =>
            value.SlotId == "duckov:slot:Armor"
            && value.ItemId == "duckov:item:800").ActiveDurationSeconds);
        Assert.Equal(5, Assert.Single(equipment.CharacterSlotStates.Values, value =>
            value.SlotId == "duckov:slot:Backpack"
            && value.ItemId == "duckov:item:900").ActiveDurationSeconds);
        Assert.Equal(5, Assert.Single(equipment.NestedSlotStates.Values, value =>
            value.ParentSlotId == "duckov:slot:Armor"
            && value.Path == "5:Pouch/"
            && value.ItemId == "duckov:item:801").ActiveDurationSeconds);
        Assert.DoesNotContain(equipment.NestedSlotStates.Values, value =>
            value.ParentSlotId == "duckov:slot:Backpack");
    }

    [Fact]
    [Trait("Category", "M14")]
    public void CleanupDetachesGlobalAndItemTreeCallbacksAndRemainsIdempotent()
    {
        var characterItem = new Item { TypeID = 1, DisplayName = "Main duck", Inventory = new Inventory() };
        var slot = new Slot { Key = "PrimaryWeapon", DisplayName = "Primary" };
        characterItem.Slots.Add(slot);
        var main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = characterItem
        };
        CharacterMainControl.Main = main;
        var publications = 0;
        var adapter = new NativeEquipmentAdapter(
            () => true,
            _ =>
            {
                publications++;
                return true;
            },
            () => true,
            _ => { },
            _ => { },
            () => 0);
        adapter.Initialize();
        Assert.Equal(1, publications);

        Assert.True(adapter.TryCleanup());
        Assert.True(adapter.TryCleanup());
        slot.Content = new Item { TypeID = 700, DisplayName = "Weapon" };
        CharacterMainControl.RaiseSlotChanged(main, slot);
        characterItem.RaiseItemTreeChanged();

        Assert.Equal(1, publications);
        Assert.Equal(EquipmentEventAssociation.UnavailableId, adapter.CaptureAssociation().LoadoutId);
    }

    public void Dispose() => CharacterMainControl.ResetNativeState();
}
