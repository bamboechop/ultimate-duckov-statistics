using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

public static class ItemStatisticsAggregateReducer
{
    public static bool Record(ItemStatisticsAggregate target, string saveGenerationId, ItemUseRecorded value)
    {
        var profile = Wrap(target, saveGenerationId, value.TimestampUtc);
        var changed = ItemUseReducer.Apply(profile, value);
        Unwrap(target, profile);
        return changed;
    }

    public static bool Record(ItemStatisticsAggregate target, string saveGenerationId, HealingApplied value)
    {
        var profile = Wrap(target, saveGenerationId, value.TimestampUtc);
        var changed = HealingReducer.Apply(profile, value);
        Unwrap(target, profile);
        return changed;
    }

    public static bool RecordOutcomeHealing(ItemStatisticsAggregate target, string saveGenerationId, HealingApplied value)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (value == null || value.GameplayContext != GameplayContext.Raid) return false;
        if (string.IsNullOrWhiteSpace(value.EventId) || string.IsNullOrWhiteSpace(value.ItemId)
            || value.ActualHealthRestored <= 0 || double.IsNaN(value.ActualHealthRestored)
            || double.IsInfinity(value.ActualHealthRestored))
            throw new ArgumentException("Healing event is invalid.", nameof(value));
        if (!string.Equals(saveGenerationId, value.SaveGenerationId, StringComparison.Ordinal))
            throw new InvalidOperationException("A healing event cannot be reduced into a different save generation.");
        NormalizePersisted(target);
        if (target.RecentEventIds.Contains(value.EventId, StringComparer.Ordinal)) return false;
        if (!target.Items.TryGetValue(value.ItemId, out var item))
        {
            item = new ItemAggregate
            {
                ItemId = value.ItemId,
                DisplayName = string.IsNullOrWhiteSpace(value.DisplayName) ? value.ItemId : value.DisplayName,
                Group = CanonicalItemGroup.Healing,
                EffectTags = new List<ItemEffectTag> { ItemEffectTag.Healing }
            };
            target.Items[value.ItemId] = item;
        }
        item.Group = CanonicalItemGroup.Healing;
        if (!item.EffectTags.Contains(ItemEffectTag.Healing)) item.EffectTags.Add(ItemEffectTag.Healing);
        item.Totals.ActualHealthRestored += value.ActualHealthRestored;
        target.Overall.ActualHealthRestored += value.ActualHealthRestored;
        target.RecentEventIds.Add(value.EventId);
        while (target.RecentEventIds.Count > 512) target.RecentEventIds.RemoveAt(0);
        RebuildGroups(target);
        return true;
    }

    public static ItemStatisticsAggregate Clone(ItemStatisticsAggregate source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        NormalizePersisted(source);
        return new ItemStatisticsAggregate
        {
            Overall = Clone(source.Overall),
            Items = source.Items.ToDictionary(
                entry => entry.Key,
                entry => new ItemAggregate
                {
                    ItemId = entry.Value.ItemId,
                    DisplayName = entry.Value.DisplayName,
                    Group = entry.Value.Group,
                    EffectTags = entry.Value.EffectTags.ToList(),
                    Totals = Clone(entry.Value.Totals)
                },
                StringComparer.Ordinal),
            Groups = source.Groups.ToDictionary(entry => entry.Key, entry => Clone(entry.Value), StringComparer.Ordinal),
            RecentEventIds = source.RecentEventIds.ToList(),
            HistoricalUnavailable = source.HistoricalUnavailable,
            WasRepairedFromInvalidState = source.WasRepairedFromInvalidState
        };
    }

    public static void Merge(ItemStatisticsAggregate target, ItemStatisticsAggregate source)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (source == null) throw new ArgumentNullException(nameof(source));
        NormalizePersisted(target);
        NormalizePersisted(source);
        Add(target.Overall, source.Overall);
        foreach (var entry in source.Items)
        {
            if (!target.Items.TryGetValue(entry.Key, out var item))
            {
                var cloned = Clone(new ItemStatisticsAggregate
                {
                    Items = new Dictionary<string, ItemAggregate>(StringComparer.Ordinal) { [entry.Key] = entry.Value }
                });
                target.Items[entry.Key] = cloned.Items[entry.Key];
                continue;
            }
            item.DisplayName = entry.Value.DisplayName;
            item.EffectTags = item.EffectTags.Concat(entry.Value.EffectTags).Distinct().ToList();
            Add(item.Totals, entry.Value.Totals);
            PromoteProvenHealing(item);
        }
        RebuildGroups(target);
        target.HistoricalUnavailable |= source.HistoricalUnavailable;
        target.WasRepairedFromInvalidState |= source.WasRepairedFromInvalidState;
    }

    public static bool TrySubtract(
        ItemStatisticsAggregate total,
        ItemStatisticsAggregate baseline,
        out ItemStatisticsAggregate difference)
    {
        if (total == null) throw new ArgumentNullException(nameof(total));
        if (baseline == null) throw new ArgumentNullException(nameof(baseline));
        Validate(total);
        Validate(baseline);
        difference = new ItemStatisticsAggregate();
        if (!IsCompositionConsistent(total) || !IsCompositionConsistent(baseline))
        {
            return false;
        }
        if (!TrySubtract(total.Overall, baseline.Overall, out var overall))
        {
            return false;
        }

        difference.Overall = overall;
        foreach (var baselineItem in baseline.Items)
        {
            if (!total.Items.ContainsKey(baselineItem.Key))
            {
                return false;
            }
        }

        foreach (var entry in total.Items)
        {
            var baselineTotals = baseline.Items.TryGetValue(entry.Key, out var baselineItem)
                ? baselineItem.Totals
                : new AggregateTotals();
            if (!TrySubtract(entry.Value.Totals, baselineTotals, out var itemDifference))
            {
                return false;
            }

            if (!HasValues(itemDifference))
            {
                continue;
            }

            difference.Items[entry.Key] = new ItemAggregate
            {
                ItemId = entry.Value.ItemId,
                DisplayName = entry.Value.DisplayName,
                Group = entry.Value.Group,
                EffectTags = entry.Value.EffectTags.ToList(),
                Totals = itemDifference
            };
        }

        var baselineEvents = new HashSet<string>(baseline.RecentEventIds, StringComparer.Ordinal);
        difference.RecentEventIds = total.RecentEventIds
            .Where(eventId => !baselineEvents.Contains(eventId))
            .ToList();
        difference.HistoricalUnavailable = total.HistoricalUnavailable;
        difference.WasRepairedFromInvalidState = total.WasRepairedFromInvalidState;
        RebuildGroups(difference);
        return IsCompositionConsistent(difference);
    }

    public static bool IsCompositionConsistent(ItemStatisticsAggregate value)
    {
        if (value?.Overall == null
            || value.Items == null
            || value.Groups == null
            || value.RecentEventIds == null
            || value.RecentEventIds.Any(string.IsNullOrWhiteSpace)
            || value.RecentEventIds.Distinct(StringComparer.Ordinal).Count() != value.RecentEventIds.Count)
        {
            return false;
        }

        var composed = new AggregateTotals();
        var composedGroups = new Dictionary<string, AggregateTotals>(StringComparer.Ordinal);
        foreach (var entry in value.Items)
        {
            var item = entry.Value;
            if (string.IsNullOrWhiteSpace(entry.Key)
                || item?.Totals == null
                || item.EffectTags == null
                || !string.Equals(entry.Key, item.ItemId, StringComparison.Ordinal)
                || !Enum.IsDefined(typeof(CanonicalItemGroup), item.Group))
            {
                return false;
            }
            Add(composed, item.Totals);

            var groupKey = item.Group.ToString();
            if (!composedGroups.TryGetValue(groupKey, out var groupTotals))
            {
                composedGroups[groupKey] = groupTotals = new AggregateTotals();
            }
            Add(groupTotals, item.Totals);
        }
        if (!TotalsEqual(value.Overall, composed) || value.Groups.Count != composedGroups.Count)
        {
            return false;
        }
        foreach (var group in value.Groups)
        {
            if (string.IsNullOrWhiteSpace(group.Key)
                || group.Value?.AmountsByUnit == null
                || !composedGroups.TryGetValue(group.Key, out var composedGroup)
                || !TotalsEqual(group.Value, composedGroup))
            {
                return false;
            }
        }
        return true;
    }

    public static void ApplyRecoveryDelta(ProfileStatistics target, ItemStatisticsAggregate difference)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (difference == null) throw new ArgumentNullException(nameof(difference));
        Validate(difference);
        Add(target.Overall, difference.Overall);
        foreach (var entry in difference.Items)
        {
            if (!target.Items.TryGetValue(entry.Key, out var item))
            {
                item = new ItemAggregate
                {
                    ItemId = entry.Value.ItemId,
                    DisplayName = entry.Value.DisplayName,
                    Group = entry.Value.Group,
                    EffectTags = entry.Value.EffectTags.ToList(),
                    Totals = Clone(entry.Value.Totals)
                };
                target.Items[entry.Key] = item;
                continue;
            }

            item.DisplayName = entry.Value.DisplayName;
            item.EffectTags = item.EffectTags.Concat(entry.Value.EffectTags).Distinct().ToList();
            Add(item.Totals, entry.Value.Totals);
            PromoteProvenHealing(item);
        }

        foreach (var eventId in difference.RecentEventIds)
        {
            if (!target.RecentEventIds.Contains(eventId, StringComparer.Ordinal))
            {
                target.RecentEventIds.Add(eventId);
            }
        }
        while (target.RecentEventIds.Count > 512)
        {
            target.RecentEventIds.RemoveAt(0);
        }

        var aggregate = new ItemStatisticsAggregate
        {
            Overall = target.Overall,
            Items = target.Items,
            Groups = target.Groups,
            RecentEventIds = target.RecentEventIds
        };
        RebuildGroups(aggregate);
        target.Groups = aggregate.Groups;
    }

    public static bool NormalizePersisted(ItemStatisticsAggregate value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        var repaired = false;
        value.Overall ??= Repair(new AggregateTotals(), ref repaired);
        value.Items ??= Repair(new Dictionary<string, ItemAggregate>(StringComparer.Ordinal), ref repaired);
        value.Groups ??= Repair(new Dictionary<string, AggregateTotals>(StringComparer.Ordinal), ref repaired);
        value.RecentEventIds ??= Repair(new List<string>(), ref repaired);
        Normalize(value.Overall, ref repaired);
        foreach (var item in value.Items.Values)
        {
            item.EffectTags ??= Repair(new List<ItemEffectTag>(), ref repaired);
            item.Totals ??= Repair(new AggregateTotals(), ref repaired);
            Normalize(item.Totals, ref repaired);
        }
        RebuildGroups(value);
        value.WasRepairedFromInvalidState |= repaired;
        return repaired;
    }

    public static void Validate(ItemStatisticsAggregate value)
    {
        if (value?.Overall == null || value.Items == null || value.Groups == null || value.RecentEventIds == null)
            throw new ArgumentException("Item statistics are incomplete.", nameof(value));
        Validate(value.Overall);
        foreach (var item in value.Items.Values)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ItemId) || item.Totals == null)
                throw new ArgumentException("Item statistics contain an invalid item.", nameof(value));
            Validate(item.Totals);
        }
    }

    private static ProfileStatistics Wrap(ItemStatisticsAggregate target, string generationId, DateTime observedUtc)
    {
        NormalizePersisted(target);
        return new ProfileStatistics
        {
            SaveGenerationId = generationId,
            CreatedUtc = observedUtc,
            UpdatedUtc = observedUtc,
            Overall = target.Overall,
            Items = target.Items,
            Groups = target.Groups,
            RecentEventIds = target.RecentEventIds
        };
    }

    private static void Unwrap(ItemStatisticsAggregate target, ProfileStatistics profile)
    {
        target.Overall = profile.Overall;
        target.Items = profile.Items;
        target.Groups = profile.Groups;
        target.RecentEventIds = profile.RecentEventIds;
    }

    private static void RebuildGroups(ItemStatisticsAggregate target)
    {
        var groups = new Dictionary<string, AggregateTotals>(StringComparer.Ordinal);
        foreach (var item in target.Items.Values)
        {
            var key = item.Group.ToString();
            if (!groups.TryGetValue(key, out var totals)) groups[key] = totals = new AggregateTotals();
            Add(totals, item.Totals);
        }
        target.Groups = groups;
    }

    private static AggregateTotals Clone(AggregateTotals value) => new()
    {
        ActivationCount = value.ActivationCount,
        ActualHealthRestored = value.ActualHealthRestored,
        AmountsByUnit = value.AmountsByUnit.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal)
    };

    private static void Add(AggregateTotals target, AggregateTotals source)
    {
        target.ActivationCount = SaturatingAdd(target.ActivationCount, source.ActivationCount);
        target.ActualHealthRestored += source.ActualHealthRestored;
        foreach (var amount in source.AmountsByUnit)
        {
            target.AmountsByUnit.TryGetValue(amount.Key, out var current);
            target.AmountsByUnit[amount.Key] = current + amount.Value;
        }
    }

    private static void PromoteProvenHealing(ItemAggregate item)
    {
        if (item.Totals.ActualHealthRestored <= 0) return;
        item.Group = CanonicalItemGroup.Healing;
        if (!item.EffectTags.Contains(ItemEffectTag.Healing))
        {
            item.EffectTags.Add(ItemEffectTag.Healing);
        }
    }

    private static bool TrySubtract(AggregateTotals total, AggregateTotals baseline, out AggregateTotals difference)
    {
        difference = new AggregateTotals();
        if (baseline.ActivationCount > total.ActivationCount
            || !TrySubtract(total.ActualHealthRestored, baseline.ActualHealthRestored, out var health))
        {
            return false;
        }

        difference.ActivationCount = total.ActivationCount - baseline.ActivationCount;
        difference.ActualHealthRestored = health;
        foreach (var baselineAmount in baseline.AmountsByUnit)
        {
            if (!total.AmountsByUnit.ContainsKey(baselineAmount.Key))
            {
                return false;
            }
        }
        foreach (var amount in total.AmountsByUnit)
        {
            baseline.AmountsByUnit.TryGetValue(amount.Key, out var baselineAmount);
            if (!TrySubtract(amount.Value, baselineAmount, out var value))
            {
                return false;
            }
            if (value > 0)
            {
                difference.AmountsByUnit[amount.Key] = value;
            }
        }
        return true;
    }

    private static bool TrySubtract(double total, double baseline, out double difference)
    {
        const double tolerance = 0.000000001;
        if (!FiniteNonNegative(total) || !FiniteNonNegative(baseline) || baseline - total > tolerance)
        {
            difference = 0;
            return false;
        }

        difference = Math.Max(0, total - baseline);
        return true;
    }

    private static bool HasValues(AggregateTotals value) =>
        value.ActivationCount != 0
        || value.ActualHealthRestored > 0
        || value.AmountsByUnit.Values.Any(amount => amount > 0);

    private static bool TotalsEqual(AggregateTotals left, AggregateTotals right)
    {
        if (left.ActivationCount != right.ActivationCount
            || Math.Abs(left.ActualHealthRestored - right.ActualHealthRestored) > 0.000000001
            || left.AmountsByUnit.Count != right.AmountsByUnit.Count)
        {
            return false;
        }

        foreach (var amount in left.AmountsByUnit)
        {
            if (!right.AmountsByUnit.TryGetValue(amount.Key, out var other)
                || Math.Abs(amount.Value - other) > 0.000000001)
            {
                return false;
            }
        }
        return true;
    }

    private static void Normalize(AggregateTotals value, ref bool repaired)
    {
        value.AmountsByUnit ??= Repair(new Dictionary<string, double>(StringComparer.Ordinal), ref repaired);
        if (value.ActivationCount < 0) { value.ActivationCount = 0; repaired = true; }
        if (!FiniteNonNegative(value.ActualHealthRestored)) { value.ActualHealthRestored = 0; repaired = true; }
        foreach (var key in value.AmountsByUnit.Keys.ToArray())
        {
            if (!FiniteNonNegative(value.AmountsByUnit[key])) { value.AmountsByUnit[key] = 0; repaired = true; }
        }
    }

    private static void Validate(AggregateTotals value)
    {
        if (value.ActivationCount < 0 || !FiniteNonNegative(value.ActualHealthRestored)
            || value.AmountsByUnit == null || value.AmountsByUnit.Values.Any(amount => !FiniteNonNegative(amount)))
            throw new ArgumentException("Item aggregate contains invalid counters.", nameof(value));
    }

    private static bool FiniteNonNegative(double value) => value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    private static long SaturatingAdd(long left, long right)
    {
        if (left < 0 || right < 0) return 0;
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }
    private static T Repair<T>(T value, ref bool repaired) { repaired = true; return value; }
}
