using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class ItemUseReducerTests
{
    [Fact]
    [Trait("Category", "ItemUse")]
    public void SuccessfulUseIncrementsItemGroupAndOverallExactlyOnce()
    {
        var profile = CreateProfile();
        var itemUse = CreateEvent("event-a", "type:42", CanonicalItemGroup.Healing, 2.5, ConsumptionUnit.Durability);

        Assert.True(ItemUseReducer.Apply(profile, itemUse));
        Assert.False(ItemUseReducer.Apply(profile, itemUse));

        var item = Assert.Single(profile.Items).Value;
        Assert.Equal(1, item.Totals.ActivationCount);
        Assert.Equal(2.5, item.Totals.AmountsByUnit[nameof(ConsumptionUnit.Durability)], precision: 6);
        Assert.Equal(1, profile.Groups[nameof(CanonicalItemGroup.Healing)].ActivationCount);
        Assert.Equal(1, profile.Overall.ActivationCount);
        Assert.Single(profile.RecentEventIds);
    }

    [Fact]
    [Trait("Category", "ItemUse")]
    public void GroupTotalsEqualSumOfItemsWithoutEffectTagDoubleCounting()
    {
        var profile = CreateProfile();
        var first = CreateEvent("event-a", "type:42", CanonicalItemGroup.Healing, 1, ConsumptionUnit.Item);
        first.EffectTags = new List<ItemEffectTag>
        {
            ItemEffectTag.Healing,
            ItemEffectTag.Drink,
            ItemEffectTag.Buff
        };
        var second = CreateEvent("event-b", "type:99", CanonicalItemGroup.Healing, 1, ConsumptionUnit.Item);

        ItemUseReducer.Apply(profile, first);
        ItemUseReducer.Apply(profile, second);

        var itemActivationSum = profile.Items.Values.Sum(item => item.Totals.ActivationCount);
        var groupActivationSum = profile.Groups.Values.Sum(group => group.ActivationCount);
        Assert.Equal(2, itemActivationSum);
        Assert.Equal(itemActivationSum, groupActivationSum);
        Assert.Equal(groupActivationSum, profile.Overall.ActivationCount);
    }

    [Fact]
    [Trait("Category", "ItemUse")]
    public void ConflictingLaterClassificationKeepsOriginalItemAndGroupInvariant()
    {
        var profile = CreateProfile();
        var first = CreateEvent("event-a", "type:42", CanonicalItemGroup.Healing, 1, ConsumptionUnit.Item);
        var reclassified = CreateEvent("event-b", "type:42", CanonicalItemGroup.Drink, 2, ConsumptionUnit.Durability);

        ItemUseReducer.Apply(profile, first);
        ItemUseReducer.Apply(profile, reclassified);

        var item = Assert.Single(profile.Items).Value;
        Assert.Equal(CanonicalItemGroup.Healing, item.Group);
        Assert.Equal(2, item.Totals.ActivationCount);
        Assert.Equal(2, profile.Groups[nameof(CanonicalItemGroup.Healing)].ActivationCount);
        Assert.False(profile.Groups.ContainsKey(nameof(CanonicalItemGroup.Drink)));
        Assert.Equal(
            profile.Items.Values.Sum(value => value.Totals.ActivationCount),
            profile.Groups.Values.Sum(value => value.ActivationCount));
    }

    [Fact]
    [Trait("Category", "ItemUse")]
    public void UnknownModdedItemIsPreservedByStableIdAndFallbackName()
    {
        var profile = CreateProfile();
        var unknown = CreateEvent("event-modded", "type:mod:9001", CanonicalItemGroup.OtherUnknown, 1, ConsumptionUnit.StackUnit);
        unknown.DisplayName = "Unknown item 9001";
        unknown.IntegrityTags = IntegrityTags.ModdedContent;

        ItemUseReducer.Apply(profile, unknown);

        var item = Assert.Single(profile.Items).Value;
        Assert.Equal("type:mod:9001", item.ItemId);
        Assert.Equal("Unknown item 9001", item.DisplayName);
        Assert.Equal(CanonicalItemGroup.OtherUnknown, item.Group);
    }

    [Fact]
    [Trait("Category", "ItemUse")]
    public void BaseUseIsRejectedByReducer()
    {
        var profile = CreateProfile();
        var itemUse = CreateEvent("event-base", "type:42", CanonicalItemGroup.Healing, 1, ConsumptionUnit.Item);
        itemUse.GameplayContext = GameplayContext.Base;

        Assert.False(ItemUseReducer.Apply(profile, itemUse));
        Assert.Empty(profile.Items);
        Assert.Equal(0, profile.Overall.ActivationCount);
    }

    [Fact]
    [Trait("Category", "ItemUse")]
    public void SaveGenerationMismatchCannotLeakData()
    {
        var profile = CreateProfile();
        var itemUse = CreateEvent("event-a", "type:42", CanonicalItemGroup.Healing, 1, ConsumptionUnit.Item);
        itemUse.SaveGenerationId = "generation-b";

        Assert.Throws<InvalidOperationException>(() => ItemUseReducer.Apply(profile, itemUse));
        Assert.Empty(profile.Items);
    }

    private static ProfileStatistics CreateProfile() => new()
    {
        SaveGenerationId = "generation-a",
        CreatedUtc = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc)
    };

    private static ItemUseRecorded CreateEvent(
        string eventId,
        string itemId,
        CanonicalItemGroup group,
        double amount,
        ConsumptionUnit unit) => new()
    {
        EventId = eventId,
        TimestampUtc = new DateTime(2026, 8, 9, 12, 0, 5, DateTimeKind.Utc),
        SaveGenerationId = "generation-a",
        GameVersion = "2.3.30",
        GameBuild = "24013657",
        GameplayContext = GameplayContext.Raid,
        IntegrityTags = IntegrityTags.Normal,
        AdapterCapability = AdapterCapabilityState.Supported,
        AdapterVersion = "native-2.3.30",
        ItemId = itemId,
        DisplayName = itemId,
        Group = group,
        EffectTags = new List<ItemEffectTag> { ItemEffectTag.Healing },
        ActivationCount = 1,
        AmountConsumed = amount,
        ConsumptionUnit = unit
    };
}
