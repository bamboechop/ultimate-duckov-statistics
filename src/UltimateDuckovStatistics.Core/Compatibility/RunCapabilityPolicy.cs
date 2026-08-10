using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Compatibility;

public enum RunCapabilityCondition
{
    Available,
    UnsupportedGameVersion,
    MissingLifecycleContract,
    MissingMovementContract,
    MissingMapContract,
    ActivationFailure
}

public static class RunCapabilityPolicy
{
    public static AdapterCapabilityState GetState(RunCapabilityCondition condition) =>
        condition == RunCapabilityCondition.Available
            ? AdapterCapabilityState.Supported
            : AdapterCapabilityState.DisabledIncompatible;

    public static bool IsSupportedGameVersion(string? observedVersion, string expectedVersion) =>
        !string.IsNullOrWhiteSpace(observedVersion)
        && string.Equals(observedVersion, expectedVersion, StringComparison.Ordinal);
}
