using UltimateDuckovStatistics.Core.Encounters;

namespace UltimateDuckovStatistics.Encounters;

internal static class EncounterLootPresentation
{
    internal static long? FirstObservedQuantity(IEnumerable<EncounterRecord> records, int itemTypeId)
    {
        // Reopening after a deposit must not replace the original observation with
        // the larger stack. An earlier hidden/absent item provides no quantity.
        foreach (var record in records.Where(row => row.Inventory != null)
            .OrderBy(row => row.Inventory!.ObservedSeconds).ThenBy(row => row.Id, StringComparer.Ordinal))
        {
            var slots = record.Inventory!.Slots.Where(slot => slot.Inspected
                && slot.ItemTypeId == itemTypeId && slot.Quantity.HasValue).ToArray();
            if (slots.Length > 0) return slots.Sum(slot => slot.Quantity!.Value);
        }
        // Transfer-only evidence cannot establish the corpse's observed contents.
        return null;
    }
}
