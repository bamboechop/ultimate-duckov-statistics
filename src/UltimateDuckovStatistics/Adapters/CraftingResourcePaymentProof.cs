using System.Globalization;
using Duckov.Economy;
using ItemStatsSystem;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

// Values only: no native Items survive the synchronous payment observation.
internal sealed class CraftingResourcePaymentProof
{
    private sealed class Holdings
    {
        public long Money { get; }
        public Dictionary<int, (long Quantity, string Name)> Items { get; }
        public Holdings(long money, Dictionary<int, (long Quantity, string Name)> items)
        { Money = money; Items = items; }
    }
    private Holdings? before;
    private bool started, completed, successful, ambiguous, cashIsResource, moneyWasRequested;
    private string failure = string.Empty;
    public IReadOnlyList<CraftingResourceCostEvidence> Resources { get; private set; } = Array.Empty<CraftingResourceCostEvidence>();
    public long CurrencyCharged { get; private set; }
    public bool ResourcesProven { get; private set; }
    public bool CurrencyProven { get; private set; }
    public string ResourceDetail => ResourcesProven ? string.Empty : Detail;
    public string CurrencyDetail => CurrencyProven ? string.Empty : Detail;
    private string Detail => !string.IsNullOrEmpty(failure) ? failure
        : !started ? "Delivered output has no observed payment call; costs are unknown."
        : !completed || !successful ? "Delivered output has no successfully completed payment observation; costs are unknown."
        : "Conflicting or nested payment observations prevent exact cost attribution.";

    public bool TryBegin(Cost cost)
    {
        if (started) { ambiguous = true; ResourcesProven = CurrencyProven = false; return false; }
        started = true;
        cashIsResource = cost.items?.Any(entry => entry.id == EconomyManager.CashItemID && entry.amount > 0) == true;
        moneyWasRequested = cost.money > 0;
        try { before = ReadHoldings(); }
        catch (Exception exception) { failure = "Payment holdings could not be observed: " + exception.GetType().Name + "."; }
        return true;
    }

    public void Complete(bool paymentSucceeded)
    {
        if (completed) { ambiguous = true; ResourcesProven = CurrencyProven = false; return; }
        completed = true;
        successful = paymentSucceeded;
        if (!successful || ambiguous || before == null) return;
        try
        {
            var after = ReadHoldings();
            var resources = new List<CraftingResourceCostEvidence>();
            foreach (var entry in before.Items.OrderBy(value => value.Key))
            {
                after.Items.TryGetValue(entry.Key, out var retained);
                var removed = checked(entry.Value.Quantity - retained.Quantity);
                if (removed <= 0 || (entry.Key == EconomyManager.CashItemID && !cashIsResource)) continue;
                resources.Add(new(entry.Key.ToString(CultureInfo.InvariantCulture), entry.Value.Name, removed));
            }
            before.Items.TryGetValue(EconomyManager.CashItemID, out var priorCash);
            after.Items.TryGetValue(EconomyManager.CashItemID, out var laterCash);
            var cashLoss = checked(priorCash.Quantity - laterCash.Quantity);
            if (cashIsResource && moneyWasRequested && cashLoss != 0)
            {
                failure = "Physical Cash changed while serving as both an item resource and possible currency; cost allocation is unknown.";
                return;
            }
            Resources = resources;
            ResourcesProven = true;
            var currency = checked(before.Money - after.Money + (cashIsResource ? 0 : cashLoss));
            if (currency < 0)
            {
                failure = "Payment produced a net currency gain; it cannot be represented as a nonnegative crafting charge.";
                return;
            }
            CurrencyCharged = currency;
            CurrencyProven = true;
        }
        catch (Exception exception)
        {
            ResourcesProven = CurrencyProven = false;
            failure = "Payment holdings could not be reconciled: " + exception.GetType().Name + ".";
        }
    }

    public void AbandonPayment() { completed = true; successful = false; ResourcesProven = CurrencyProven = false; }

    private static Holdings ReadHoldings()
    {
        var money = EconomyManager.Money;
        if (money < 0) throw new InvalidOperationException("Negative currency holdings.");
        var values = new Dictionary<int, (long Quantity, string Name)>();
        var seen = new HashSet<int>();
        var pending = new Stack<Item>(ItemUtilities.FindAllBelongsToPlayer(_ => true));
        while (pending.Count > 0)
        {
            var item = pending.Pop();
            if (item == null || item.IsBeingDestroyed || !seen.Add(item.GetInstanceID())) continue;
            if (seen.Count > 32768) throw new InvalidOperationException("Payment observation limit exceeded.");
            // Native ownership enumeration is flat. Attachments returned during
            // payment were owned before the call too, so include the full tree.
            if (item.Slots != null)
                foreach (var slot in item.Slots)
                    if (slot?.Content != null) pending.Push(slot.Content);
            if (item.Inventory?.Content != null)
                foreach (var child in item.Inventory.Content)
                    if (child != null) pending.Push(child);
            if (item.TypeID <= 0) continue;
            var quantity = item.Stackable ? item.StackCount : 1;
            if (quantity < 0) throw new InvalidOperationException("Negative owned item quantity.");
            values.TryGetValue(item.TypeID, out var existing);
            string name;
            try { name = item.DisplayName; }
            catch { name = string.Empty; }
            values[item.TypeID] = (checked(existing.Quantity + quantity), string.IsNullOrWhiteSpace(name)
                ? "Item " + item.TypeID.ToString(CultureInfo.InvariantCulture) : name);
        }
        return new(money, values);
    }
}
