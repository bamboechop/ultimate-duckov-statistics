using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

[DataContract]
public sealed class WorldTimeMetricCapabilities
{
    [DataMember(Order = 1)] public MetricAvailability CalendarDays { get; set; } = Bootstrap();
    [DataMember(Order = 2)] public MetricAvailability ObservedElapsed { get; set; } = Bootstrap();
    [DataMember(Order = 3)] public MetricAvailability CompletedSleepSessions { get; set; } = Bootstrap();
    [DataMember(Order = 4)] public MetricAvailability SleepAdvancedTime { get; set; } = Bootstrap();

    private static MetricAvailability Bootstrap() => new()
    {
        State = AdapterCapabilityState.DisabledIncompatible,
        Provenance = WorldTimeNativeContractPolicy.BootstrapProvenance
    };
}

[DataContract]
public sealed class WorldTimeStatisticsAggregate
{
    [DataMember(Order = 1)] public long CalendarDaysAdvanced { get; set; }
    [DataMember(Order = 2)] public long ObservedGameTimeTicks { get; set; }
    [DataMember(Order = 3)] public long CompletedSleepSessions { get; set; }
    [DataMember(Order = 4)] public long SleepAdvancedTimeTicks { get; set; }
    [DataMember(Order = 5)] public WorldTimeMetricCapabilities Capabilities { get; set; } = new();
    [DataMember(Order = 6)] public bool HistoricalUnavailable { get; set; }
    [DataMember(Order = 7)] public string HistoricalProvenance { get; set; } = string.Empty;
    [DataMember(Order = 8)] public bool CalendarArithmeticUnavailable { get; set; }
    [DataMember(Order = 9)] public bool ObservedElapsedArithmeticUnavailable { get; set; }
    [DataMember(Order = 10)] public bool SleepSessionArithmeticUnavailable { get; set; }
    [DataMember(Order = 11)] public bool SleepElapsedArithmeticUnavailable { get; set; }
    [DataMember(Order = 12)] public bool WasRepairedFromInvalidState { get; set; }
}

public readonly struct WorldTimeMutation
{
    public WorldTimeMutation(
        long calendarDaysAdvanced,
        long observedGameTimeTicks,
        long completedSleepSessions,
        long sleepAdvancedTimeTicks)
    {
        CalendarDaysAdvanced = calendarDaysAdvanced;
        ObservedGameTimeTicks = observedGameTimeTicks;
        CompletedSleepSessions = completedSleepSessions;
        SleepAdvancedTimeTicks = sleepAdvancedTimeTicks;
    }

    public long CalendarDaysAdvanced { get; }
    public long ObservedGameTimeTicks { get; }
    public long CompletedSleepSessions { get; }
    public long SleepAdvancedTimeTicks { get; }

    public bool IsEmpty => CalendarDaysAdvanced == 0
                           && ObservedGameTimeTicks == 0
                           && CompletedSleepSessions == 0
                           && SleepAdvancedTimeTicks == 0;
}

public static class WorldTimeStatisticsReducer
{
    private const string ArithmeticProvenance =
        "The metric reached the Int64 arithmetic limit; prior exact totals remain available, but further capture is disabled.";

    public static bool Apply(WorldTimeStatisticsAggregate aggregate, WorldTimeMutation mutation)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        ValidateMutation(mutation);
        NormalizePersisted(aggregate);
        var changed = false;
        changed |= TryApply(
            mutation.CalendarDaysAdvanced,
            aggregate.CalendarArithmeticUnavailable,
            aggregate.CalendarDaysAdvanced,
            value => aggregate.CalendarDaysAdvanced = value,
            () =>
            {
                aggregate.CalendarArithmeticUnavailable = true;
                aggregate.Capabilities.CalendarDays = Unavailable(ArithmeticProvenance);
            });
        changed |= TryApply(
            mutation.ObservedGameTimeTicks,
            aggregate.ObservedElapsedArithmeticUnavailable,
            aggregate.ObservedGameTimeTicks,
            value => aggregate.ObservedGameTimeTicks = value,
            () =>
            {
                aggregate.ObservedElapsedArithmeticUnavailable = true;
                aggregate.Capabilities.ObservedElapsed = Unavailable(ArithmeticProvenance);
            });
        changed |= TryApply(
            mutation.CompletedSleepSessions,
            aggregate.SleepSessionArithmeticUnavailable,
            aggregate.CompletedSleepSessions,
            value => aggregate.CompletedSleepSessions = value,
            () =>
            {
                aggregate.SleepSessionArithmeticUnavailable = true;
                aggregate.Capabilities.CompletedSleepSessions = Unavailable(ArithmeticProvenance);
            });
        changed |= TryApply(
            mutation.SleepAdvancedTimeTicks,
            aggregate.SleepElapsedArithmeticUnavailable,
            aggregate.SleepAdvancedTimeTicks,
            value => aggregate.SleepAdvancedTimeTicks = value,
            () =>
            {
                aggregate.SleepElapsedArithmeticUnavailable = true;
                aggregate.Capabilities.SleepAdvancedTime = Unavailable(ArithmeticProvenance);
            });
        return changed;
    }

    public static bool NormalizePersisted(WorldTimeStatisticsAggregate aggregate)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        var changed = false;
        if (aggregate.Capabilities == null)
        {
            aggregate.Capabilities = new WorldTimeMetricCapabilities();
            aggregate.WasRepairedFromInvalidState = true;
            changed = true;
        }
        if (aggregate.CalendarDaysAdvanced < 0)
        {
            aggregate.CalendarDaysAdvanced = 0;
            aggregate.CalendarArithmeticUnavailable = true;
            aggregate.Capabilities.CalendarDays = Unavailable("Invalid persisted metric was repaired to zero; current capture is disabled.");
            changed = true;
        }
        if (aggregate.ObservedGameTimeTicks < 0)
        {
            aggregate.ObservedGameTimeTicks = 0;
            aggregate.ObservedElapsedArithmeticUnavailable = true;
            aggregate.Capabilities.ObservedElapsed = Unavailable("Invalid persisted metric was repaired to zero; current capture is disabled.");
            changed = true;
        }
        if (aggregate.CompletedSleepSessions < 0)
        {
            aggregate.CompletedSleepSessions = 0;
            aggregate.SleepSessionArithmeticUnavailable = true;
            aggregate.Capabilities.CompletedSleepSessions = Unavailable("Invalid persisted metric was repaired to zero; current capture is disabled.");
            changed = true;
        }
        if (aggregate.SleepAdvancedTimeTicks < 0)
        {
            aggregate.SleepAdvancedTimeTicks = 0;
            aggregate.SleepElapsedArithmeticUnavailable = true;
            aggregate.Capabilities.SleepAdvancedTime = Unavailable("Invalid persisted metric was repaired to zero; current capture is disabled.");
            changed = true;
        }
        aggregate.HistoricalProvenance ??= string.Empty;
        return changed;
    }

    public static void Validate(WorldTimeStatisticsAggregate aggregate)
    {
        if (aggregate == null || aggregate.Capabilities == null)
            throw new ArgumentException("World-time roots are missing.", nameof(aggregate));
        if (aggregate.CalendarDaysAdvanced < 0 || aggregate.ObservedGameTimeTicks < 0
            || aggregate.CompletedSleepSessions < 0 || aggregate.SleepAdvancedTimeTicks < 0)
            throw new ArgumentException("World-time counters cannot be negative.", nameof(aggregate));
        ValidateAvailability(aggregate.Capabilities.CalendarDays);
        ValidateAvailability(aggregate.Capabilities.ObservedElapsed);
        ValidateAvailability(aggregate.Capabilities.CompletedSleepSessions);
        ValidateAvailability(aggregate.Capabilities.SleepAdvancedTime);
    }

    public static WorldTimeStatisticsAggregate Clone(WorldTimeStatisticsAggregate? source)
    {
        source ??= new WorldTimeStatisticsAggregate();
        NormalizePersisted(source);
        return new WorldTimeStatisticsAggregate
        {
            CalendarDaysAdvanced = source.CalendarDaysAdvanced,
            ObservedGameTimeTicks = source.ObservedGameTimeTicks,
            CompletedSleepSessions = source.CompletedSleepSessions,
            SleepAdvancedTimeTicks = source.SleepAdvancedTimeTicks,
            Capabilities = CloneCapabilities(source.Capabilities),
            HistoricalUnavailable = source.HistoricalUnavailable,
            HistoricalProvenance = source.HistoricalProvenance,
            CalendarArithmeticUnavailable = source.CalendarArithmeticUnavailable,
            ObservedElapsedArithmeticUnavailable = source.ObservedElapsedArithmeticUnavailable,
            SleepSessionArithmeticUnavailable = source.SleepSessionArithmeticUnavailable,
            SleepElapsedArithmeticUnavailable = source.SleepElapsedArithmeticUnavailable,
            WasRepairedFromInvalidState = source.WasRepairedFromInvalidState
        };
    }

    public static WorldTimeMetricCapabilities CloneCapabilities(WorldTimeMetricCapabilities source) => new()
    {
        CalendarDays = Clone(source.CalendarDays),
        ObservedElapsed = Clone(source.ObservedElapsed),
        CompletedSleepSessions = Clone(source.CompletedSleepSessions),
        SleepAdvancedTime = Clone(source.SleepAdvancedTime)
    };

    public static void InitializeOrRestrictCapabilities(
        WorldTimeStatisticsAggregate aggregate,
        WorldTimeMetricCapabilities current)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (current == null) throw new ArgumentNullException(nameof(current));
        NormalizePersisted(aggregate);
        aggregate.Capabilities = new WorldTimeMetricCapabilities
        {
            CalendarDays = InitializeOrRestrict(aggregate.Capabilities.CalendarDays, current.CalendarDays),
            ObservedElapsed = InitializeOrRestrict(aggregate.Capabilities.ObservedElapsed, current.ObservedElapsed),
            CompletedSleepSessions = InitializeOrRestrict(aggregate.Capabilities.CompletedSleepSessions, current.CompletedSleepSessions),
            SleepAdvancedTime = InitializeOrRestrict(aggregate.Capabilities.SleepAdvancedTime, current.SleepAdvancedTime)
        };
        if (aggregate.CalendarArithmeticUnavailable) aggregate.Capabilities.CalendarDays = Unavailable(ArithmeticProvenance);
        if (aggregate.ObservedElapsedArithmeticUnavailable) aggregate.Capabilities.ObservedElapsed = Unavailable(ArithmeticProvenance);
        if (aggregate.SleepSessionArithmeticUnavailable) aggregate.Capabilities.CompletedSleepSessions = Unavailable(ArithmeticProvenance);
        if (aggregate.SleepElapsedArithmeticUnavailable) aggregate.Capabilities.SleepAdvancedTime = Unavailable(ArithmeticProvenance);
    }

    public static WorldTimeMetricCapabilities RestrictWithCurrent(
        WorldTimeMetricCapabilities recorded,
        WorldTimeMetricCapabilities current) =>
        new()
        {
            CalendarDays = Restrict(recorded.CalendarDays, current.CalendarDays),
            ObservedElapsed = Restrict(recorded.ObservedElapsed, current.ObservedElapsed),
            CompletedSleepSessions = Restrict(recorded.CompletedSleepSessions, current.CompletedSleepSessions),
            SleepAdvancedTime = Restrict(recorded.SleepAdvancedTime, current.SleepAdvancedTime)
        };

    private static bool TryApply(long delta, bool disabled, long current, Action<long> apply, Action disable)
    {
        if (delta == 0 || disabled) return false;
        if (current > long.MaxValue - delta)
        {
            disable();
            return true;
        }
        apply(current + delta);
        return true;
    }

    private static void ValidateMutation(WorldTimeMutation mutation)
    {
        if (mutation.CalendarDaysAdvanced < 0 || mutation.ObservedGameTimeTicks < 0
            || mutation.CompletedSleepSessions < 0 || mutation.SleepAdvancedTimeTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(mutation), "World-time mutations cannot be negative.");
    }

    private static MetricAvailability InitializeOrRestrict(MetricAvailability recorded, MetricAvailability current)
    {
        if (IsBootstrap(recorded) || IsBlankDefault(recorded)) return Clone(current);
        return Restrict(recorded, current);
    }

    private static MetricAvailability Restrict(MetricAvailability recorded, MetricAvailability current) =>
        (int)recorded.State >= (int)current.State ? Clone(recorded) : Clone(current);

    private static bool IsBootstrap(MetricAvailability value) =>
        value.State == AdapterCapabilityState.DisabledIncompatible
        && string.Equals(value.Provenance, WorldTimeNativeContractPolicy.BootstrapProvenance, StringComparison.Ordinal);

    private static bool IsBlankDefault(MetricAvailability value) =>
        value.State == AdapterCapabilityState.DisabledIncompatible && string.IsNullOrWhiteSpace(value.Provenance);

    private static MetricAvailability Clone(MetricAvailability value) => new()
    { State = value.State, Provenance = value.Provenance ?? string.Empty };

    private static MetricAvailability Unavailable(string provenance) => new()
    { State = AdapterCapabilityState.DisabledIncompatible, Provenance = provenance };

    private static void ValidateAvailability(MetricAvailability value)
    {
        if (value == null || !Enum.IsDefined(typeof(AdapterCapabilityState), value.State))
            throw new ArgumentException("World-time capability metadata is invalid.");
    }
}
