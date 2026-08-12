using ItemStatsSystem;
using ItemStatsSystem.Items;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Tests;

public sealed class NativeEquipmentSnapshotBuilderTests
{
    [Fact]
    [Trait("Category", "Equipment")]
    public void ToteTotemsComeFromOrdinaryInventoryAndRemainActivationUnknown()
    {
        var character = Character();
        var tote = Tote(1200, Totem(966, "Phys. RES III"), Totem(966, "Phys. RES III"));
        character.Inventory!.Content.Add(tote);

        var snapshot = NativeEquipmentSnapshotBuilder.Build(new CharacterMainControl(), character);

        Assert.Equal(2, snapshot.Totems.Count);
        Assert.All(snapshot.Totems, value =>
        {
            Assert.Equal("duckov:totem:966", value.ItemId);
            Assert.Equal(TotemCarryKind.ToteInventory, value.CarryKind);
            Assert.Equal("duckov:tote:1200", value.ContainerId);
            Assert.Equal(TotemActivationState.Unknown, value.ActivationState);
        });
        Assert.Equal(
            EquipmentIdentity.ActiveTotemSetId(Array.Empty<TotemSnapshot>()),
            snapshot.TotemSetId);
    }

    [Fact]
    [Trait("Category", "Equipment")]
    public void SlottedToteAndLooseInventoryTotemDoNotMasqueradeAsToteContents()
    {
        var character = Character();
        character.Slots.Add(new Slot { Key = "Backpack", Content = Tote(1200, Totem(966, "Nested")) });
        character.Inventory!.Content.Add(Totem(967, "Loose"));

        var snapshot = NativeEquipmentSnapshotBuilder.Build(new CharacterMainControl(), character);

        Assert.Empty(snapshot.Totems);
        Assert.Single(snapshot.Items);
    }

    [Fact]
    [Trait("Category", "Equipment")]
    public void DirectTotemAndOrdinaryInventoryToteAreTrackedSeparately()
    {
        var character = Character();
        character.Slots.Add(new Slot { Key = "Totem2", Content = Totem(966, "Direct") });
        character.Inventory!.Content.Add(Tote(1200, Totem(967, "Tote")));

        var snapshot = NativeEquipmentSnapshotBuilder.Build(new CharacterMainControl(), character);

        Assert.Collection(
            snapshot.Totems,
            direct =>
            {
                Assert.Equal(TotemCarryKind.DirectSlot, direct.CarryKind);
                Assert.Equal(TotemActivationState.ProvenActive, direct.ActivationState);
            },
            tote =>
            {
                Assert.Equal(TotemCarryKind.ToteInventory, tote.CarryKind);
                Assert.Equal(TotemActivationState.Unknown, tote.ActivationState);
            });
    }

    private static Item Character() => new() { Inventory = new Inventory() };

    private static Item Tote(int typeId, params Item[] contents)
    {
        var tote = new Item
        {
            TypeID = typeId,
            DisplayName = "Tote Bag",
            DisplayNameRaw = "Item_ToteBag",
            Inventory = new Inventory()
        };
        tote.Inventory.Content.AddRange(contents);
        return tote;
    }

    private static Item Totem(int typeId, string name)
    {
        var item = new Item { TypeID = typeId, DisplayName = name };
        item.Tags.Add("Totem");
        return item;
    }
}
