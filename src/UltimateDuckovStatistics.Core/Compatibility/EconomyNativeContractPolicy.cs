using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Compatibility;

public static class EconomyCapabilityIds
{
    public const string MoneyAmountDirection = "native-economy-money-flow";
    public const string MoneySourceAttribution = "native-economy-money-source";
    public const string MoneyContextAttribution = "native-economy-money-context";
    public const string CashAmountDirection = "native-economy-cash-flow";
    public const string CashExternalAcquisition = "native-economy-cash-acquisition";
    public const string CashContextAttribution = "native-economy-cash-context";
    public const string CashTerminalOutcomes = "native-economy-cash-terminal";
    public const string RouteAttribution = "native-economy-route";
}

public static class EconomyNativeContractPolicy
{
    public static EconomyMetricCapabilities Unavailable(string provenance)
    {
        var unavailable = Availability(AdapterCapabilityState.DisabledIncompatible, provenance);
        return new EconomyMetricCapabilities
        {
            MoneyAmountDirection = Clone(unavailable),
            MoneySourceAttribution = Clone(unavailable),
            MoneyContextAttribution = Clone(unavailable),
            CashAmountDirection = Clone(unavailable),
            CashExternalAcquisition = Clone(unavailable),
            CashContextAttribution = Clone(unavailable),
            CashTerminalOutcomes = Clone(unavailable),
            RouteAttribution = Clone(unavailable)
        };
    }

    public static IReadOnlyList<CapabilityRecord> ToRecords(EconomyMetricCapabilities value, string version) =>
        new[]
        {
            Record(EconomyCapabilityIds.MoneyAmountDirection, value.MoneyAmountDirection, version),
            Record(EconomyCapabilityIds.MoneySourceAttribution, value.MoneySourceAttribution, version),
            Record(EconomyCapabilityIds.MoneyContextAttribution, value.MoneyContextAttribution, version),
            Record(EconomyCapabilityIds.CashAmountDirection, value.CashAmountDirection, version),
            Record(EconomyCapabilityIds.CashExternalAcquisition, value.CashExternalAcquisition, version),
            Record(EconomyCapabilityIds.CashContextAttribution, value.CashContextAttribution, version),
            Record(EconomyCapabilityIds.CashTerminalOutcomes, value.CashTerminalOutcomes, version),
            Record(EconomyCapabilityIds.RouteAttribution, value.RouteAttribution, version)
        };

    public static MetricAvailability Availability(AdapterCapabilityState state, string provenance) => new()
    { State = state, Provenance = provenance ?? string.Empty };

    private static MetricAvailability Clone(MetricAvailability source) => new()
    { State = source.State, Provenance = source.Provenance };

    private static CapabilityRecord Record(string id, MetricAvailability value, string version) => new()
    { AdapterId = id, State = value.State, Version = version, Detail = value.Provenance };
}
