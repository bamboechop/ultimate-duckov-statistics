using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Compatibility;

public sealed class StartupCapabilityWarningPolicy
{
    private bool checkedStartup;
    public IReadOnlyList<string> Check(IEnumerable<CapabilityRecord> capabilities, double secondsSinceActivation)
    {
        if (checkedStartup || secondsSinceActivation < 2) return Array.Empty<string>();
        checkedStartup = true;
        return capabilities.Where(value => value.State == AdapterCapabilityState.DisabledIncompatible)
            .Select(value => value.AdapterId).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }
}
