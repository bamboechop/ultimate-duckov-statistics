using UltimateDuckovStatistics.Core.Classification;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Tests;

public sealed class ItemClassifierTests
{
    [Fact]
    [Trait("Category", "ItemUse")]
    public void MultiEffectItemGetsOnePrimaryGroupAndEveryProvenEffectTag()
    {
        var result = ItemClassifier.Classify(new ItemClassificationInput
        {
            AppliesPositiveHealing = true,
            AppliesFoodEnergy = true,
            AppliesDrinkHydration = true,
            AppliesBuff = true,
            RemovesDebuff = true,
            HasSpecialBehavior = true
        });

        Assert.Equal(CanonicalItemGroup.Healing, result.Group);
        Assert.Equal(
            new[]
            {
                ItemEffectTag.Healing,
                ItemEffectTag.Food,
                ItemEffectTag.Drink,
                ItemEffectTag.Buff,
                ItemEffectTag.DebuffRemoval,
                ItemEffectTag.Special
            },
            result.EffectTags);
    }

    [Theory]
    [Trait("Category", "ItemUse")]
    [InlineData(false, false, false, false, true, false, CanonicalItemGroup.RemedyDebuffRemoval)]
    [InlineData(false, true, true, false, false, false, CanonicalItemGroup.Food)]
    [InlineData(false, false, true, false, false, false, CanonicalItemGroup.Drink)]
    [InlineData(false, false, false, true, false, false, CanonicalItemGroup.StimulantBuff)]
    [InlineData(false, false, false, false, false, true, CanonicalItemGroup.Special)]
    [InlineData(false, false, false, false, false, false, CanonicalItemGroup.OtherUnknown)]
    public void ClassificationPriorityIsDeterministic(
        bool healing,
        bool food,
        bool drink,
        bool buff,
        bool remedy,
        bool special,
        CanonicalItemGroup expected)
    {
        var result = ItemClassifier.Classify(new ItemClassificationInput
        {
            AppliesPositiveHealing = healing,
            AppliesFoodEnergy = food,
            AppliesDrinkHydration = drink,
            AppliesBuff = buff,
            RemovesDebuff = remedy,
            HasSpecialBehavior = special
        });

        Assert.Equal(expected, result.Group);
    }
}
