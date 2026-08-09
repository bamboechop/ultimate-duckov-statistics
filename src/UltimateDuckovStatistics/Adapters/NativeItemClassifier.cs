using Duckov.ItemUsage;
using ItemStatsSystem;
using UltimateDuckovStatistics.Core.Classification;

namespace UltimateDuckovStatistics.Adapters;

internal static class NativeItemClassifier
{
    public static ItemClassificationInput Describe(Item item)
    {
        var result = new ItemClassificationInput();
        var behaviors = item.UsageUtilities?.behaviors;
        if (behaviors == null)
        {
            return result;
        }

        foreach (var behavior in behaviors)
        {
            switch (behavior)
            {
                case Drug drug when drug.healValue > 0:
                    result.AppliesPositiveHealing = true;
                    break;
                case FoodDrink foodDrink:
                    result.AppliesFoodEnergy |= foodDrink.energyValue != 0;
                    result.AppliesDrinkHydration |= foodDrink.waterValue != 0;
                    break;
                case AddBuff:
                    result.AppliesBuff = true;
                    break;
                case RemoveBuff:
                    result.RemovesDebuff = true;
                    break;
                case SpawnEgg:
                case DeadByChance:
                case UseToCreateItem:
                    result.HasSpecialBehavior = true;
                    break;
            }
        }

        return result;
    }
}
