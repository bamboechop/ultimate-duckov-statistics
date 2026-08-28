using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Compatibility;

public static class EconomyHoldingsCapabilityIds
{
    public const string CurrentMoney = "native-economy-holdings-current-money";
    public const string CurrentCash = "native-economy-holdings-current-cash";
    public const string LiquidWealth = "native-economy-holdings-liquid-wealth";
    public static IReadOnlyList<string> All { get; } = [CurrentMoney, CurrentCash, LiquidWealth];
}

public static class EconomyHoldingsNativeContractPolicy
{
    public const string BootstrapProvenance = "Economy-holdings capability has not been initialized.";

    public static EconomyHoldingsMetricCapabilities Supported(
        string moneyProvenance,
        string cashProvenance,
        string liquidProvenance) => new()
        {
            Money = Availability(AdapterCapabilityState.Supported, moneyProvenance),
            Cash = Availability(AdapterCapabilityState.Supported, cashProvenance),
            LiquidWealth = Availability(AdapterCapabilityState.Supported, liquidProvenance)
        };

    public static EconomyHoldingsMetricCapabilities MoneySupportedCashUnavailable(
        string moneyProvenance,
        string cashProvenance) => new()
        {
            Money = Availability(AdapterCapabilityState.Supported, moneyProvenance),
            Cash = Availability(AdapterCapabilityState.DisabledIncompatible, cashProvenance),
            LiquidWealth = Availability(AdapterCapabilityState.DisabledIncompatible, cashProvenance)
        };

    public static EconomyHoldingsMetricCapabilities Unavailable(string provenance)
    {
        var unavailable = Availability(AdapterCapabilityState.DisabledIncompatible, provenance);
        return new EconomyHoldingsMetricCapabilities
        {
            Money = Clone(unavailable),
            Cash = Clone(unavailable),
            LiquidWealth = Clone(unavailable)
        };
    }

    public static IReadOnlyList<CapabilityRecord> ToRecords(
        EconomyHoldingsMetricCapabilities value,
        string version) =>
    [
        Record(EconomyHoldingsCapabilityIds.CurrentMoney, value.Money, version),
        Record(EconomyHoldingsCapabilityIds.CurrentCash, value.Cash, version),
        Record(EconomyHoldingsCapabilityIds.LiquidWealth, value.LiquidWealth, version)
    ];

    public static MetricAvailability Availability(AdapterCapabilityState state, string provenance) => new()
    { State = state, Provenance = provenance ?? string.Empty };

    private static MetricAvailability Clone(MetricAvailability source) => new()
    { State = source.State, Provenance = source.Provenance };

    private static CapabilityRecord Record(string id, MetricAvailability value, string version) => new()
    { AdapterId = id, State = value.State, Version = version, Detail = value.Provenance };
}
