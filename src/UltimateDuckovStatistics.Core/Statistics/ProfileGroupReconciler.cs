using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

public static class ProfileGroupReconciler
{
    public static bool PromoteProvenHealingItems(ProfileStatistics profile)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var changed = false;
        foreach (var item in profile.Items.Values)
        {
            if (item.Totals.ActualHealthRestored <= 0)
            {
                continue;
            }

            changed |= PromoteItem(item);
        }

        if (changed)
        {
            RebuildGroups(profile);
        }

        return changed;
    }

    public static bool PromoteItemToHealing(ProfileStatistics profile, ItemAggregate item)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        if (!PromoteItem(item))
        {
            return false;
        }

        RebuildGroups(profile);
        return true;
    }

    private static bool PromoteItem(ItemAggregate item)
    {
        var changed = false;
        if (item.Group != CanonicalItemGroup.Healing)
        {
            item.Group = CanonicalItemGroup.Healing;
            changed = true;
        }

        if (!item.EffectTags.Contains(ItemEffectTag.Healing))
        {
            item.EffectTags.Add(ItemEffectTag.Healing);
            changed = true;
        }

        return changed;
    }

    private static void RebuildGroups(ProfileStatistics profile)
    {
        var groups = new Dictionary<string, AggregateTotals>(StringComparer.Ordinal);
        foreach (var item in profile.Items.Values)
        {
            var key = item.Group.ToString();
            if (!groups.TryGetValue(key, out var group))
            {
                group = new AggregateTotals();
                groups[key] = group;
            }

            group.ActivationCount += item.Totals.ActivationCount;
            group.ActualHealthRestored += item.Totals.ActualHealthRestored;
            foreach (var amount in item.Totals.AmountsByUnit)
            {
                group.AmountsByUnit.TryGetValue(amount.Key, out var current);
                group.AmountsByUnit[amount.Key] = current + amount.Value;
            }
        }

        profile.Groups = groups;
    }
}
