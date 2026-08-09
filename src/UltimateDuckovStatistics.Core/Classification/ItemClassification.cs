using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Classification;

public sealed class ItemClassificationInput
{
    public bool AppliesPositiveHealing { get; set; }

    public bool AppliesFoodEnergy { get; set; }

    public bool AppliesDrinkHydration { get; set; }

    public bool AppliesBuff { get; set; }

    public bool RemovesDebuff { get; set; }

    public bool HasSpecialBehavior { get; set; }
}

public sealed class ItemClassification
{
    public ItemClassification(CanonicalItemGroup group, IReadOnlyList<ItemEffectTag> effectTags)
    {
        Group = group;
        EffectTags = effectTags;
    }

    public CanonicalItemGroup Group { get; }

    public IReadOnlyList<ItemEffectTag> EffectTags { get; }
}

public static class ItemClassifier
{
    public static ItemClassification Classify(ItemClassificationInput input)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var effectTags = new List<ItemEffectTag>(6);
        AddIf(effectTags, input.AppliesPositiveHealing, ItemEffectTag.Healing);
        AddIf(effectTags, input.AppliesFoodEnergy, ItemEffectTag.Food);
        AddIf(effectTags, input.AppliesDrinkHydration, ItemEffectTag.Drink);
        AddIf(effectTags, input.AppliesBuff, ItemEffectTag.Buff);
        AddIf(effectTags, input.RemovesDebuff, ItemEffectTag.DebuffRemoval);
        AddIf(effectTags, input.HasSpecialBehavior, ItemEffectTag.Special);

        // A single deterministic primary group prevents multi-effect totals from
        // double-counting. The effect tag list preserves every proven behavior.
        var group = input.AppliesPositiveHealing
            ? CanonicalItemGroup.Healing
            : input.RemovesDebuff
                ? CanonicalItemGroup.RemedyDebuffRemoval
                : input.AppliesFoodEnergy
                    ? CanonicalItemGroup.Food
                    : input.AppliesDrinkHydration
                        ? CanonicalItemGroup.Drink
                        : input.AppliesBuff
                            ? CanonicalItemGroup.StimulantBuff
                            : input.HasSpecialBehavior
                                ? CanonicalItemGroup.Special
                                : CanonicalItemGroup.OtherUnknown;

        return new ItemClassification(group, effectTags);
    }

    private static void AddIf(List<ItemEffectTag> tags, bool condition, ItemEffectTag tag)
    {
        if (condition)
        {
            tags.Add(tag);
        }
    }
}
