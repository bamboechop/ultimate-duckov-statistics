using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Compatibility;

public enum HealingCapabilityCondition
{
    Available,
    MissingContracts,
    MissingHarmony,
    IncompatibleHarmony,
    ForeignTranspiler,
    ActivationFailure
}

public static class HealingCapabilityPolicy
{
    public static AdapterCapabilityState GetState(HealingCapabilityCondition condition) =>
        condition == HealingCapabilityCondition.Available
            ? AdapterCapabilityState.Supported
            : AdapterCapabilityState.DisabledIncompatible;
}
