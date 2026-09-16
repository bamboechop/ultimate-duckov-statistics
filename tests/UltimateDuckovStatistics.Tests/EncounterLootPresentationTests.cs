using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Encounters;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterLootPresentationTests
{
    [Fact]
    public void DepositThenTakeDoesNotInflateFirstObservedQuantity()
    {
        var first = Opening("first", 10, (594, 15));
        var deposited = Opening("second", 20, (594, 105));
        var emptied = Opening("third", 30);
        var transfer = new EncounterRecord { Loot = new() { ItemTypeId = 594, TakenToPlayer = 105, ReturnedByPlayer = 90 } };
        // Storage order is not observation order; transfer totals are not inventory.
        Assert.Equal(15, EncounterLootPresentation.FirstObservedQuantity([emptied, deposited, transfer, first], 594));
        Assert.Equal(105, deposited.Inventory!.Slots[0].Quantity);
        Assert.Equal(90, transfer.Loot!.ReturnedByPlayer);
    }

    [Fact]
    public void EachItemUsesItsFirstInspectedOpeningAndCombinesMatchingStacks()
    {
        var first = Opening("first", 10, (451, 19));
        first.Inventory!.Slots.Add(new() { Slot = 1, Inspected = false });
        var inspected = Opening("second", 20, (451, 8), (594, 20), (594, 6));
        var later = Opening("third", 30, (594, 9));
        EncounterRecord[] records = [later, inspected, first];
        Assert.Equal(19, EncounterLootPresentation.FirstObservedQuantity(records, 451));
        Assert.Equal(26, EncounterLootPresentation.FirstObservedQuantity(records, 594));
    }

    [Fact]
    public void TransferWithoutInspectedContentsDoesNotInventAnInventoryQuantity()
    {
        var hidden = Opening("first", 10);
        hidden.Inventory!.Slots.Add(new() { Inspected = false });
        var transfer = new EncounterRecord { Loot = new() { ItemTypeId = 594, TakenToPlayer = 30, ReturnedByPlayer = 1 } };
        Assert.Null(EncounterLootPresentation.FirstObservedQuantity([hidden, transfer], 594));
        Assert.Null(EncounterLootPresentation.FirstObservedQuantity([], 594));
    }

    private static EncounterRecord Opening(string id, double seconds, params (int Type, long Quantity)[] items) => new()
    {
        Id = id,
        Inventory = new()
        {
            ObservedSeconds = seconds,
            Slots = items.Select((item, index) => new EncounterInventorySlot
            {
                Slot = index, Inspected = true, ItemTypeId = item.Type, Quantity = item.Quantity
            }).ToList()
        }
    };
}
