using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

public static class RunIntegrityPolicy
{
    public const string HarmonyLoaderModId = "HarmonyLoadMod";

    public static IntegrityTags Accumulate(IntegrityTags accumulated, IntegrityTags observed)
    {
        var accumulatedFlags = accumulated & ~IntegrityTags.Normal;
        var observedFlags = observed & ~IntegrityTags.Normal;
        if (accumulatedFlags != IntegrityTags.Unknown || observedFlags != IntegrityTags.Unknown)
        {
            return accumulatedFlags | observedFlags;
        }

        return accumulated == IntegrityTags.Unknown || observed == IntegrityTags.Unknown
            ? IntegrityTags.Unknown
            : IntegrityTags.Normal;
    }

    public static IntegrityTags Evaluate(bool cheatOrCustomDifficulty, IEnumerable<string>? activeModIds)
    {
        if (activeModIds == null)
        {
            return IntegrityTags.Unknown;
        }

        var result = cheatOrCustomDifficulty
            ? IntegrityTags.CheatOrCustomDifficulty
            : IntegrityTags.Unknown;
        if (activeModIds.Any(IsGameplayMod))
        {
            result |= IntegrityTags.ModdedContent;
        }

        return result == IntegrityTags.Unknown ? IntegrityTags.Normal : result;
    }

    private static bool IsGameplayMod(string? modId) =>
        !string.IsNullOrWhiteSpace(modId)
        && !string.Equals(modId, ProductInfo.ModId, StringComparison.Ordinal)
        && !string.Equals(modId, HarmonyLoaderModId, StringComparison.Ordinal);
}
