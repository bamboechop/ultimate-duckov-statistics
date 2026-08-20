using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Compatibility;

public static class WorldTimeCapabilityIds
{
    public const string CalendarDays = "native-world-time-calendar-days";
    public const string ObservedElapsed = "native-world-time-observed-elapsed";
    public const string CompletedSleepSessions = "native-world-time-completed-sleep";
    public const string SleepAdvancedTime = "native-world-time-sleep-advanced";

    public static IReadOnlyList<string> All { get; } =
        [CalendarDays, ObservedElapsed, CompletedSleepSessions, SleepAdvancedTime];
}

public static class WorldTimeNativeContractPolicy
{
    public const string BootstrapProvenance = "World-time capability has not been initialized.";

    public static WorldTimeMetricCapabilities Unavailable(string provenance)
    {
        var unavailable = Availability(AdapterCapabilityState.DisabledIncompatible, provenance);
        return new WorldTimeMetricCapabilities
        {
            CalendarDays = Clone(unavailable),
            ObservedElapsed = Clone(unavailable),
            CompletedSleepSessions = Clone(unavailable),
            SleepAdvancedTime = Clone(unavailable)
        };
    }

    public static WorldTimeMetricCapabilities ClockSupportedSleepUnavailable(string clockProvenance, string sleepProvenance) =>
        new()
        {
            CalendarDays = Availability(AdapterCapabilityState.Supported, clockProvenance),
            ObservedElapsed = Availability(AdapterCapabilityState.Supported, clockProvenance),
            CompletedSleepSessions = Availability(AdapterCapabilityState.DisabledIncompatible, sleepProvenance),
            SleepAdvancedTime = Availability(AdapterCapabilityState.DisabledIncompatible, sleepProvenance)
        };

    public static WorldTimeMetricCapabilities Supported(string clockProvenance, string sleepProvenance) => new()
    {
        CalendarDays = Availability(AdapterCapabilityState.Supported, clockProvenance),
        ObservedElapsed = Availability(AdapterCapabilityState.Supported, clockProvenance),
        CompletedSleepSessions = Availability(AdapterCapabilityState.Supported, sleepProvenance),
        SleepAdvancedTime = Availability(AdapterCapabilityState.Supported, sleepProvenance)
    };

    public static IReadOnlyList<CapabilityRecord> ToRecords(WorldTimeMetricCapabilities value, string version) =>
    [
        Record(WorldTimeCapabilityIds.CalendarDays, value.CalendarDays, version),
        Record(WorldTimeCapabilityIds.ObservedElapsed, value.ObservedElapsed, version),
        Record(WorldTimeCapabilityIds.CompletedSleepSessions, value.CompletedSleepSessions, version),
        Record(WorldTimeCapabilityIds.SleepAdvancedTime, value.SleepAdvancedTime, version)
    ];

    public static MetricAvailability Availability(AdapterCapabilityState state, string provenance) => new()
    { State = state, Provenance = provenance ?? string.Empty };

    private static MetricAvailability Clone(MetricAvailability source) => new()
    { State = source.State, Provenance = source.Provenance };

    private static CapabilityRecord Record(string id, MetricAvailability value, string version) => new()
    { AdapterId = id, State = value.State, Version = version, Detail = value.Provenance };
}
