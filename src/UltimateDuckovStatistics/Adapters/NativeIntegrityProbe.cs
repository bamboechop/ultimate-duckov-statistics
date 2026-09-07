using Duckov;
using Duckov.Modding;
using Duckov.Rules;
using System.Reflection;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Adapters;

internal static class NativeIntegrityProbe
{
    private static readonly FieldInfo? ActiveModsField = ResolveActiveModsField(typeof(ModManager));

    internal static FieldInfo? ResolveActiveModsField(Type managerType)
    {
        try
        {
            var field = managerType.GetField("activeMods", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            return field != null && field.IsPrivate && !field.IsStatic
                && field.FieldType == typeof(Dictionary<string, Duckov.Modding.ModBehaviour>) ? field : null;
        }
        catch { return null; }
    }

    public static IntegrityTags Read()
    {
        try
        {
            var cheat = CheatMode.Active || GameRulesManager.SelectedRuleIndex == RuleIndex.Custom;
            if (ActiveModsField == null) return IntegrityTags.Unknown;
            var manager = ModManager.Instance;
            // Native GetCurrentActiveModList treats an absent manager as empty.
            if (manager == null) return RunIntegrityPolicy.Evaluate(cheat, hasGameplayMod: false);
            if (ActiveModsField.GetValue(manager) is not Dictionary<string, Duckov.Modding.ModBehaviour> activeMods)
                return IntegrityTags.Unknown;
            var hasGameplayMod = false;
            foreach (var entry in activeMods)
            {
                var mod = entry.Value;
                if (mod != null && RunIntegrityPolicy.IsGameplayMod(mod.info.name)) hasGameplayMod = true;
            }
            // Finish enumeration so a later failed native read cannot be hidden by an earlier match.
            return RunIntegrityPolicy.Evaluate(cheat, hasGameplayMod);
        }
        catch
        {
            return IntegrityTags.Unknown;
        }
    }
}
