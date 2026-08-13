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
        }
        RebuildGroups(target);
        target.HistoricalUnavailable |= source.HistoricalUnavailable;
        target.WasRepairedFromInvalidState |= source.WasRepairedFromInvalidState;
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
