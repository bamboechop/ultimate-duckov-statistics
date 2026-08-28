using ItemStatsSystem;
using ItemStatsSystem.Items;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;

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
