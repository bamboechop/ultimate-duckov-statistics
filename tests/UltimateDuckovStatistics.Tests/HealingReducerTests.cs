using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class HealingReducerTests
{
    private static readonly DateTime TestTime = new(2026, 8, 9, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "Healing")]
    public void ActualHealingAggregatesSeparatelyOverallByGroupAndByItem()
    {
        var profile = CreateProfile();
        ItemUseReducer.Apply(profile, CreateUse());
        var healing = CreateHealing("heal-a", 17.5);

        Assert.True(HealingReducer.Apply(profile, healing));
        Assert.False(HealingReducer.Apply(profile, healing));

        Assert.Equal(1, profile.Overall.ActivationCount);
        Assert.Equal(1, profile.Overall.AmountsByUnit[nameof(ConsumptionUnit.Item)]);
        Assert.Equal(17.5, profile.Overall.ActualHealthRestored, precision: 6);
        Assert.Equal(17.5, profile.Groups[nameof(CanonicalItemGroup.Healing)].ActualHealthRestored, precision: 6);
        Assert.Equal(17.5, profile.Items["item:a"].Totals.ActualHealthRestored, precision: 6);
    }

    [Fact]
    [Trait("Category", "Healing")]
    public void HealingCannotCrossGenerationOrInventAnUnprovenItem()
    {
        var profile = CreateProfile();
        var missing = CreateHealing("heal-a", 5);
        Assert.Throws<InvalidOperationException>(() => HealingReducer.Apply(profile, missing));

        ItemUseReducer.Apply(profile, CreateUse());
        var wrongGeneration = CreateHealing("heal-b", 5);
        wrongGeneration.SaveGenerationId = "generation-b";
        Assert.Throws<InvalidOperationException>(() => HealingReducer.Apply(profile, wrongGeneration));
    }

    private static ProfileStatistics CreateProfile() => new()
    {
        SaveGenerationId = "generation-a",
        CreatedUtc = TestTime,
        UpdatedUtc = TestTime
    };

    private static ItemUseRecorded CreateUse() => new()
    {
        EventId = "use-a",
        TimestampUtc = TestTime,
        SaveGenerationId = "generation-a",
        GameplayContext = GameplayContext.Raid,
        ItemId = "item:a",
        DisplayName = "Medkit",
        Group = CanonicalItemGroup.Healing,
        ActivationCount = 1,
        AmountConsumed = 1,
        ConsumptionUnit = ConsumptionUnit.Item
    };

    private static HealingApplied CreateHealing(string eventId, double amount) => new()
    {
        EventId = eventId,
        ApplicationId = $"application-{eventId}",
        SourceItemUseEventId = "use-a",
        TimestampUtc = TestTime,
        SaveGenerationId = "generation-a",
        GameplayContext = GameplayContext.Raid,
        ItemId = "item:a",
        DisplayName = "Medkit",
        Group = CanonicalItemGroup.Healing,
        ActualHealthRestored = amount
    };
}
