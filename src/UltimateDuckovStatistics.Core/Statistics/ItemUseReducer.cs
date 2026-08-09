using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

public static class ItemUseReducer
{
    private const int MaximumRecentEventIds = 512;

    public static bool Apply(ProfileStatistics profile, ItemUseRecorded itemUse)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (itemUse == null)
        {
            throw new ArgumentNullException(nameof(itemUse));
        }

        if (itemUse.GameplayContext != GameplayContext.Raid)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(itemUse.EventId)
            || string.IsNullOrWhiteSpace(itemUse.ItemId)
            || itemUse.ActivationCount <= 0
            || itemUse.AmountConsumed < 0)
        {
            throw new ArgumentException("Item-use event is invalid.", nameof(itemUse));
        }

        if (!string.Equals(profile.SaveGenerationId, itemUse.SaveGenerationId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("An event cannot be reduced into a different save generation.");
        }

        if (profile.RecentEventIds.Contains(itemUse.EventId, StringComparer.Ordinal))
        {
            return false;
        }

        var item = GetOrCreateItem(profile, itemUse);
        Add(item.Totals, itemUse);

        var groupKey = itemUse.Group.ToString();
        if (!profile.Groups.TryGetValue(groupKey, out var group))
        {
            group = new AggregateTotals();
            profile.Groups[groupKey] = group;
        }

        Add(group, itemUse);
        Add(profile.Overall, itemUse);
        profile.UpdatedUtc = itemUse.TimestampUtc;
        RememberEvent(profile, itemUse.EventId);
        return true;
    }

    private static ItemAggregate GetOrCreateItem(ProfileStatistics profile, ItemUseRecorded itemUse)
    {
        if (!profile.Items.TryGetValue(itemUse.ItemId, out var item))
        {
            item = new ItemAggregate
            {
                ItemId = itemUse.ItemId,
                DisplayName = itemUse.DisplayName,
                Group = itemUse.Group,
                EffectTags = itemUse.EffectTags.Distinct().ToList()
            };
            profile.Items[itemUse.ItemId] = item;
            return item;
        }

        item.DisplayName = itemUse.DisplayName;
        item.Group = itemUse.Group;
        item.EffectTags = itemUse.EffectTags.Distinct().ToList();
        return item;
    }

    private static void Add(AggregateTotals target, ItemUseRecorded itemUse)
    {
        target.ActivationCount += itemUse.ActivationCount;
        var unitKey = itemUse.ConsumptionUnit.ToString();
        target.AmountsByUnit.TryGetValue(unitKey, out var current);
        target.AmountsByUnit[unitKey] = current + itemUse.AmountConsumed;
    }

    private static void RememberEvent(ProfileStatistics profile, string eventId)
    {
        profile.RecentEventIds.Add(eventId);
        while (profile.RecentEventIds.Count > MaximumRecentEventIds)
        {
            profile.RecentEventIds.RemoveAt(0);
        }
    }
}
