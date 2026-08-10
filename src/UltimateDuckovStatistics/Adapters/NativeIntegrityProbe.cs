using Duckov;
using Duckov.Modding;
using Duckov.Rules;
using UltimateDuckovStatistics.Core;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Adapters;

internal static class NativeIntegrityProbe
{
    public static IntegrityTags Read()
    {
        try
        {
            return RunIntegrityPolicy.Evaluate(
                CheatMode.Active || GameRulesManager.SelectedRuleIndex == RuleIndex.Custom,
                ModManager.GetCurrentActiveModList());
        }
        catch
        {
            return IntegrityTags.Unknown;
        }
    }
}
