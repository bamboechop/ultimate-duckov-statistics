using Duckov;
using Duckov.Modding;
using Duckov.Rules;
using UltimateDuckovStatistics.Core;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Adapters;

internal static class NativeIntegrityProbe
{
    public static IntegrityTags Read()
    {
        try
        {
            var result = IntegrityTags.Unknown;
            if (CheatMode.Active || GameRulesManager.SelectedRuleIndex == RuleIndex.Custom)
            {
                result |= IntegrityTags.CheatOrCustomDifficulty;
            }

            var activeMods = ModManager.GetCurrentActiveModList();
            if (activeMods.Any(name => !string.Equals(name, ProductInfo.ModId, StringComparison.Ordinal)))
            {
                result |= IntegrityTags.ModdedContent;
            }

            return result == IntegrityTags.Unknown ? IntegrityTags.Normal : result;
        }
        catch
        {
            return IntegrityTags.Unknown;
        }
    }
}
