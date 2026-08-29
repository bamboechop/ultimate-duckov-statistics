using Duckov.Economy;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class CraftingResourcePaymentProof
{
    private readonly Dictionary<int, RepeatedRequirement> repeatedRequirements;
    private readonly long declaredMoney;
    private readonly Cost.ItemEntry[] declaredItems;
    private readonly Dictionary<int, Observation> observations = new();
    private bool paymentStarted;
    private bool paymentCompleted;
    private bool exact;

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
        if (!paymentStarted || paymentCompleted || !repeatedRequirements.ContainsKey(itemTypeId)) return;
        if (!observations.TryGetValue(itemTypeId, out var observation))
            observation = new Observation { MinimumCount = count };
        observation.Count = checked(observation.Count + 1);
        observation.MinimumCount = Math.Min(observation.MinimumCount, count);
        observations[itemTypeId] = observation;
    }

    public string Complete(bool paymentSucceeded)
    {
        if (!paymentStarted || paymentCompleted)
            return "Repeated crafting-resource consumption is unavailable because the expected native payment proof was not unique.";
        paymentCompleted = true;
        if (!paymentSucceeded) return string.Empty;
        if (declaredMoney > 0 && repeatedRequirements.ContainsKey(EconomyManager.CashItemID))
            return "Repeated crafting-resource consumption is unavailable because physical Cash is both an item resource and a possible source for the same recipe's currency charge.";
        foreach (var entry in repeatedRequirements.OrderBy(value => value.Key))
        {
            if (!observations.TryGetValue(entry.Key, out var observation)
                || observation.Count < entry.Value.EntryCount)
                return $"Repeated crafting-resource consumption is unavailable because native Pay did not expose every affordability observation for resource {entry.Key}.";
            if (observation.MinimumCount < entry.Value.CombinedQuantity)
                return $"Repeated crafting-resource consumption is unavailable because native Pay accepted resource {entry.Key} entries totaling {entry.Value.CombinedQuantity} while its minimum affordability observation proved only {observation.MinimumCount} available.";
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
