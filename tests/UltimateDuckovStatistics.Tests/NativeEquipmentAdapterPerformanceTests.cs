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

    public void Dispose() => CharacterMainControl.ResetNativeState();
}
