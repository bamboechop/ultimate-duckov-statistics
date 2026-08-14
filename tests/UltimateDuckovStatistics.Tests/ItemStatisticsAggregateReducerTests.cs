using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class ItemStatisticsAggregateReducerTests
{
    [Fact]
    [Trait("Category", "Persistence")]
    public void MergePromotesAccumulatedItemWhenLaterRunProvesHealing()
    {
        var target = Aggregate(
            CanonicalItemGroup.OtherUnknown,
            activations: 1,
            healing: 0);
        var source = Aggregate(
            CanonicalItemGroup.Healing,
            activations: 1,
            healing: 12);

        ItemStatisticsAggregateReducer.Merge(target, source);

        var item = Assert.Single(target.Items).Value;
        Assert.Equal(CanonicalItemGroup.Healing, item.Group);
        Assert.Contains(ItemEffectTag.Healing, item.EffectTags);
        Assert.Equal(2, item.Totals.ActivationCount);
        Assert.Equal(12, item.Totals.ActualHealthRestored);
        Assert.False(target.Groups.ContainsKey(nameof(CanonicalItemGroup.OtherUnknown)));
        Assert.Equal(2, target.Groups[nameof(CanonicalItemGroup.Healing)].ActivationCount);
        Assert.True(ItemStatisticsAggregateReducer.IsCompositionConsistent(target));
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void RecoveryDeltaCannotDowngradeAnAlreadyProvenHealingItem()
    {
        var target = new ProfileStatistics
        {
            Overall = Totals(1, 12),
            Items = new Dictionary<string, ItemAggregate>(StringComparer.Ordinal)
            {
                ["item:medkit"] = Item(CanonicalItemGroup.Healing, 1, 12)
            },
            Groups = new Dictionary<string, AggregateTotals>(StringComparer.Ordinal)
            {
                [nameof(CanonicalItemGroup.Healing)] = Totals(1, 12)
            }
        };
        var difference = Aggregate(
            CanonicalItemGroup.OtherUnknown,
            activations: 1,
            healing: 0);

        ItemStatisticsAggregateReducer.ApplyRecoveryDelta(target, difference);

        var item = Assert.Single(target.Items).Value;
        Assert.Equal(CanonicalItemGroup.Healing, item.Group);
        Assert.Contains(ItemEffectTag.Healing, item.EffectTags);
        Assert.Equal(2, item.Totals.ActivationCount);
        Assert.Equal(12, item.Totals.ActualHealthRestored);
        Assert.False(target.Groups.ContainsKey(nameof(CanonicalItemGroup.OtherUnknown)));
        Assert.Equal(2, target.Groups[nameof(CanonicalItemGroup.Healing)].ActivationCount);
    }

    private static ItemStatisticsAggregate Aggregate(
        CanonicalItemGroup group,
        long activations,
        double healing)
    {
        var totals = Totals(activations, healing);
        return new ItemStatisticsAggregate
        {
            Overall = Totals(activations, healing),
            Items = new Dictionary<string, ItemAggregate>(StringComparer.Ordinal)
            {
                ["item:medkit"] = Item(group, activations, healing)
            },
            Groups = new Dictionary<string, AggregateTotals>(StringComparer.Ordinal)
            {
                [group.ToString()] = totals
            }
        };
    }

    private static ItemAggregate Item(
        CanonicalItemGroup group,
        long activations,
        double healing) => new()
    {
        ItemId = "item:medkit",
        DisplayName = "Med-Kit (S)",
        Group = group,
        EffectTags = group == CanonicalItemGroup.Healing
            ? new List<ItemEffectTag> { ItemEffectTag.Healing }
            : new List<ItemEffectTag>(),
        Totals = Totals(activations, healing)
    };

    private static AggregateTotals Totals(long activations, double healing) => new()
    {
        ActivationCount = activations,
        ActualHealthRestored = healing
    };
}
