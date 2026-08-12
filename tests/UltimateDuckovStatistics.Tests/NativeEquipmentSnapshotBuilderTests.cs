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
        character.Inventory!.Content.Add(Tote(Totem(966, "Phys. RES III")));
        character.Inventory.Content.Add(Tote(Totem(966, "Phys. RES III")));

        var snapshot = NativeEquipmentSnapshotBuilder.Build(new CharacterMainControl(), character);

        Assert.Equal(2, snapshot.Totems.Count);
        Assert.All(snapshot.Totems, value =>
        {
            Assert.Equal("duckov:totem:966", value.ItemId);
            Assert.Equal(TotemCarryKind.ToteInventory, value.CarryKind);
            Assert.Equal("duckov:tote:1255", value.ContainerId);
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
        character.Slots.Add(new Slot { Key = "Backpack", Content = Tote(Totem(966, "Nested")) });
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
        character.Inventory!.Content.Add(Tote(Totem(967, "Tote")));

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

    [Fact]
    [Trait("Category", "Equipment")]
    public void ToteRequiresExactNativeTypeAndAnyThingSlotButIgnoresDisplayName()
    {
        var character = Character();
        var wrongType = Tote(Totem(966, "Wrong type"));
        wrongType.TypeID = 1200;
        var wrongSlot = Tote();
        wrongSlot.Slots.Add(new Slot { Key = "Attachment", Content = Totem(968, "Wrong slot") });
        var renamed = Tote(Totem(969, "Renamed tote child"));
        renamed.DisplayName = "Localized or modded name";
        renamed.DisplayNameRaw = "Changed_Raw_Name";
        character.Inventory!.Content.Add(wrongType);
        character.Inventory.Content.Add(wrongSlot);
        character.Inventory.Content.Add(renamed);

        var snapshot = NativeEquipmentSnapshotBuilder.Build(new CharacterMainControl(), character);

        var tote = Assert.Single(snapshot.Totems);
        Assert.Equal("duckov:totem:969", tote.ItemId);
        Assert.Equal("duckov:tote:1255", tote.ContainerId);
    }

    [Fact]
    [Trait("Category", "Equipment")]
    public void NestedAttachmentIdentityIncludesTheFullAncestorSlotPath()
    {
        var firstCharacter = Character();
        firstCharacter.Slots.Add(new Slot
        {
            Key = "PrimaryWeapon",
            Content = WeaponWithNestedAttachments(leftChildTypeId: 301, rightChildTypeId: 302)
        });
        var secondCharacter = Character();
        secondCharacter.Slots.Add(new Slot
        {
            Key = "PrimaryWeapon",
            Content = WeaponWithNestedAttachments(leftChildTypeId: 302, rightChildTypeId: 301)
        });

        var first = NativeEquipmentSnapshotBuilder.Build(new CharacterMainControl(), firstCharacter);
        var second = NativeEquipmentSnapshotBuilder.Build(new CharacterMainControl(), secondCharacter);

        Assert.NotEqual(first.Items.Single().AttachmentSignature, second.Items.Single().AttachmentSignature);
        Assert.NotEqual(first.LoadoutId, second.LoadoutId);
        Assert.NotEqual(first.SnapshotId, second.SnapshotId);
    }

    private static Item Character() => new() { Inventory = new Inventory() };

    private static Item Tote(Item? content = null)
    {
        var tote = new Item
        {
            TypeID = 1255,
            DisplayName = "Tote Bag",
            DisplayNameRaw = "Item_ToteBag"
        };
        if (content != null)
        {
            tote.Slots.Add(new Slot { Key = "AnyThing", Content = content });
        }
        return tote;
    }

    private static Item Totem(int typeId, string name)
    {
        var item = new Item { TypeID = typeId, DisplayName = name };
        item.Tags.Add("Totem");
        return item;
    }

    private static Item WeaponWithNestedAttachments(int leftChildTypeId, int rightChildTypeId)
    {
        var weapon = new Item { TypeID = 242, DisplayName = "MF" };
        var left = new Item { TypeID = 401, DisplayName = "Left root" };
        left.Slots.Add(new Slot { Key = "Nested", Content = new Item { TypeID = leftChildTypeId } });
        var right = new Item { TypeID = 402, DisplayName = "Right root" };
        right.Slots.Add(new Slot { Key = "Nested", Content = new Item { TypeID = rightChildTypeId } });
        weapon.Slots.Add(new Slot { Key = "Left", Content = left });
        weapon.Slots.Add(new Slot { Key = "Right", Content = right });
        return weapon;
    }
}
