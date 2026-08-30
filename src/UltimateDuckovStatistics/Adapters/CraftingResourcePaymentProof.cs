using Duckov.Economy;
using ItemStatsSystem;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class CraftingResourcePaymentProof
{
    private readonly Dictionary<int, RepeatedRequirement> repeatedRequirements;
    private readonly long declaredMoney;
    private readonly Cost.ItemEntry[] declaredItems;
    private readonly Dictionary<int, Observation> observations = new();
    private readonly Dictionary<int, long> netRemovedQuantities = new();
    private bool paymentStarted;
    private bool paymentCompleted;
    private bool exact;
    private string mutationFailure = string.Empty;

    public CraftingResourcePaymentProof(Cost cost, bool resourceEvidenceProven)
    {
        declaredMoney = cost.money;
        declaredItems = cost.items?.ToArray() ?? Array.Empty<Cost.ItemEntry>();
        if (!resourceEvidenceProven)
        {
            repeatedRequirements = new Dictionary<int, RepeatedRequirement>();
            exact = true;
            return;
        }
        var requirements = new Dictionary<int, RepeatedRequirement>();
        foreach (var entry in declaredItems)
        {
            if (!requirements.TryGetValue(entry.id, out var requirement))
                requirement = new RepeatedRequirement();
            requirement.EntryCount = checked(requirement.EntryCount + 1);
            requirement.CombinedQuantity = checked(requirement.CombinedQuantity + entry.amount);
            requirements[entry.id] = requirement;
        }
        repeatedRequirements = requirements
            .Where(entry => entry.Value.EntryCount > 1)
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        exact = repeatedRequirements.Count == 0;
    }

    public bool RequiresPaymentProof => repeatedRequirements.Count != 0;

    public bool IsExact => exact;

    public bool TryBegin(Cost cost)
    {
        if (!RequiresPaymentProof || paymentStarted || !Matches(cost)) return false;
        paymentStarted = true;
        return true;
    }

    public void ObserveItemCount(int itemTypeId, int count)
    {
        if (!paymentStarted
            || paymentCompleted
            || !repeatedRequirements.TryGetValue(itemTypeId, out var requirement)) return;
        if (!observations.TryGetValue(itemTypeId, out var observation))
            observation = new Observation { MinimumCount = count };
        else if (observation.Count >= requirement.EntryCount)
            return;
        observation.Count = checked(observation.Count + 1);
        observation.MinimumCount = Math.Min(observation.MinimumCount, count);
        observations[itemTypeId] = observation;
    }

    public void ObserveStackCountMutation(Item item, int beforeCount, int afterCount, bool wasBeingDestroyed)
    {
        if (!CanObserveMutation(item, wasBeingDestroyed)) return;
        if (beforeCount < 0 || afterCount < 0)
        {
            mutationFailure = $"Repeated crafting-resource consumption is unavailable because native Pay changed resource {item.TypeID} between invalid stack quantities {beforeCount} and {afterCount}.";
            return;
        }
        if (beforeCount == afterCount) return;
        AddNetRemoved(item.TypeID, checked((long)beforeCount - afterCount));
    }

    public void ObserveStackDestroyed(Item item)
    {
        if (!CanObserveMutation(item, item.IsBeingDestroyed)) return;
        if (item.StackCount < 0)
        {
            mutationFailure = $"Repeated crafting-resource consumption is unavailable because native Pay destroyed resource {item.TypeID} with negative stack quantity {item.StackCount}.";
            return;
        }
        AddNetRemoved(item.TypeID, item.StackCount);
    }

    public string Complete(bool paymentSucceeded)
    {
        if (!paymentStarted || paymentCompleted)
            return "Repeated crafting-resource consumption is unavailable because the expected native payment proof was not unique.";
        paymentCompleted = true;
        if (!paymentSucceeded) return string.Empty;
        if (declaredMoney > 0 && repeatedRequirements.ContainsKey(EconomyManager.CashItemID))
            return "Repeated crafting-resource consumption is unavailable because physical Cash is both an item resource and a possible source for the same recipe's currency charge.";
        if (!string.IsNullOrWhiteSpace(mutationFailure)) return mutationFailure;
        foreach (var entry in repeatedRequirements.OrderBy(value => value.Key))
        {
            if (!observations.TryGetValue(entry.Key, out var observation)
                || observation.Count < entry.Value.EntryCount)
                return $"Repeated crafting-resource consumption is unavailable because native Pay did not expose every affordability observation for resource {entry.Key}.";
            if (observation.MinimumCount < entry.Value.CombinedQuantity)
                return $"Repeated crafting-resource consumption is unavailable because native Pay accepted resource {entry.Key} entries totaling {entry.Value.CombinedQuantity} while its minimum affordability observation proved only {observation.MinimumCount} available.";
            netRemovedQuantities.TryGetValue(entry.Key, out var netRemovedQuantity);
            if (netRemovedQuantity != entry.Value.CombinedQuantity)
                return $"Repeated crafting-resource consumption is unavailable because native Pay accepted resource {entry.Key} entries totaling {entry.Value.CombinedQuantity} while its matched net ownership-ending stack mutations proved {netRemovedQuantity} actually removed.";
        }
        exact = true;
        return string.Empty;
    }

    public void AbandonPayment() => paymentCompleted = true;

    public string DeliveryDetail()
    {
        if (exact) return string.Empty;
        if (!paymentStarted)
            return "Repeated crafting-resource consumption is unavailable because successful output delivery was not preceded by the expected native payment callback.";
        if (!paymentCompleted)
            return "Repeated crafting-resource consumption is unavailable because successful output delivery preceded completion of the native payment proof.";
        return "Repeated crafting-resource consumption is unavailable because native payment did not prove the canonical combined quantity.";
    }

    private bool Matches(Cost cost)
    {
        if (cost.money != declaredMoney || cost.items == null || cost.items.Length != declaredItems.Length) return false;
        for (var index = 0; index < declaredItems.Length; index++)
        {
            if (cost.items[index].id != declaredItems[index].id
                || cost.items[index].amount != declaredItems[index].amount)
                return false;
        }
        return true;
    }

    private bool CanObserveMutation(Item item, bool wasBeingDestroyed) =>
        paymentStarted
        && !paymentCompleted
        && !wasBeingDestroyed
        && repeatedRequirements.ContainsKey(item.TypeID)
        && string.IsNullOrWhiteSpace(mutationFailure);

    private void AddNetRemoved(int itemTypeId, long quantity)
    {
        if (quantity == 0) return;
        try
        {
            netRemovedQuantities.TryGetValue(itemTypeId, out var current);
            netRemovedQuantities[itemTypeId] = checked(current + quantity);
        }
        catch (OverflowException)
        {
            mutationFailure = $"Repeated crafting-resource consumption is unavailable because native Pay mutation evidence overflowed for resource {itemTypeId}.";
        }
    }

    private struct RepeatedRequirement
    {
        public int EntryCount;
        public long CombinedQuantity;
    }

    private struct Observation
    {
        public int Count;
        public int MinimumCount;
    }
}
