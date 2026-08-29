using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

public enum EconomyHoldingObservationState
{
    Unavailable = 0,
    LastObserved = 1,
    Current = 2
}

[DataContract]
public sealed class EconomyHoldingObservation
{
    [DataMember(Order = 1)] public EconomyHoldingObservationState State { get; set; }
    [DataMember(Order = 2, EmitDefaultValue = false)] public long? Value { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public DateTime? ObservedUtc { get; set; }
    [DataMember(Order = 4)] public string SaveGenerationId { get; set; } = string.Empty;
    [DataMember(Order = 5)] public string ObservationProvenance { get; set; } = string.Empty;
    [DataMember(Order = 6)] public string FreshnessProvenance { get; set; } = string.Empty;
}

[DataContract]
public sealed class EconomyHoldingsMetricCapabilities
{
    [DataMember(Order = 1)] public MetricAvailability Money { get; set; } = Bootstrap();
    [DataMember(Order = 2)] public MetricAvailability Cash { get; set; } = Bootstrap();
    [DataMember(Order = 3)] public MetricAvailability LiquidWealth { get; set; } = Bootstrap();

    private static MetricAvailability Bootstrap() => new()
    {
        State = AdapterCapabilityState.DisabledIncompatible,
        Provenance = EconomyHoldingsNativeContractPolicy.BootstrapProvenance
    };
}

[DataContract]
public sealed class EconomyHoldingsSnapshot
{
    [DataMember(Order = 1)] public string SaveGenerationId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public EconomyHoldingObservation Money { get; set; } = new();
    [DataMember(Order = 3)] public EconomyHoldingObservation Cash { get; set; } = new();
    [DataMember(Order = 4)] public EconomyHoldingsMetricCapabilities Capabilities { get; set; } = new();
    [DataMember(Order = 5)] public bool HistoricalUnavailable { get; set; }
    [DataMember(Order = 6)] public string HistoricalProvenance { get; set; } = string.Empty;
    [DataMember(Order = 7)] public bool WasRepairedFromInvalidState { get; set; }
}

public sealed class EconomyHoldingsMutation
{
    public EconomyHoldingsMutation(
        string saveGenerationId,
        DateTime timestampUtc,
        long? money,
        long? cash,
        string provenance)
    {
        SaveGenerationId = saveGenerationId ?? string.Empty;
        TimestampUtc = timestampUtc;
        Money = money;
        Cash = cash;
        Provenance = provenance ?? string.Empty;
    }

    public string SaveGenerationId { get; }
    public DateTime TimestampUtc { get; }
    public long? Money { get; }
    public long? Cash { get; }
    public string Provenance { get; }
    public bool IsEmpty => !Money.HasValue && !Cash.HasValue;
}

public sealed class EconomyHoldingsProjection
{
    public EconomyHoldingObservation Money { get; set; } = new();
    public EconomyHoldingObservation Cash { get; set; } = new();
    public EconomyHoldingObservation LiquidWealth { get; set; } = new();
    public EconomyHoldingsMetricCapabilities Capabilities { get; set; } = new();
}

public static class EconomyHoldingsReducer
{
    public const string RestartFreshnessProvenance =
        "The value was persisted for this save generation, but the current process has not confirmed it.";
    public const string LiquidUnavailableProvenance =
        "Liquid wealth requires current Money and current Cash observations from the same save generation.";
    public const string LiquidOverflowProvenance =
        "Liquid wealth is unavailable because the checked Money plus Cash sum exceeded Int64.";

    public static bool Apply(EconomyHoldingsSnapshot snapshot, EconomyHoldingsMutation mutation)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (mutation == null) throw new ArgumentNullException(nameof(mutation));
        if (string.IsNullOrWhiteSpace(mutation.SaveGenerationId))
            throw new ArgumentException("A save generation is required.", nameof(mutation));
        if (!string.Equals(snapshot.SaveGenerationId, mutation.SaveGenerationId, StringComparison.Ordinal))
            throw new InvalidOperationException("Economy holdings observation belongs to a different save generation.");
        if (mutation.TimestampUtc == default || mutation.TimestampUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Economy holdings timestamp must be a non-default UTC value.", nameof(mutation));
        if (mutation.Money < 0 || mutation.Cash < 0)
            throw new ArgumentOutOfRangeException(nameof(mutation), "Economy holdings cannot be negative.");
        if (mutation.IsEmpty) return false;

        var changed = false;
        if (mutation.Money.HasValue)
            changed |= ReplaceCurrentIfChanged(
                snapshot,
                money: true,
                Current(mutation.Money.Value, mutation, "Duckov EconomyManager.money"));
        if (mutation.Cash.HasValue)
            changed |= ReplaceCurrentIfChanged(
                snapshot,
                money: false,
                Current(mutation.Cash.Value, mutation, "Duckov top-level owned Cash inventories"));
        return changed;
    }

    public static bool MarkNotCurrent(
        EconomyHoldingsSnapshot snapshot,
        string saveGenerationId,
        bool money,
        bool cash,
        string provenance)
    {
        ValidateBoundary(snapshot, saveGenerationId, "freshness");
        var changed = false;
        if (money) changed |= MarkNotCurrent(snapshot.Money, provenance);
        if (cash) changed |= MarkNotCurrent(snapshot.Cash, provenance);
        return changed;
    }

    public static bool MarkUnavailable(
        EconomyHoldingsSnapshot snapshot,
        string saveGenerationId,
        bool money,
        bool cash,
        string provenance)
    {
        ValidateBoundary(snapshot, saveGenerationId, "availability");
        var changed = false;
        if (money)
        {
            var replacement = Unavailable(provenance);
            changed |= !ObservationEqual(snapshot.Money, replacement);
            snapshot.Money = replacement;
        }
        if (cash)
        {
            var replacement = Unavailable(provenance);
            changed |= !ObservationEqual(snapshot.Cash, replacement);
            snapshot.Cash = replacement;
        }
        return changed;
    }

    public static bool NormalizePersisted(
        EconomyHoldingsSnapshot snapshot,
        string expectedSaveGenerationId,
        bool downgradeCurrent)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (string.IsNullOrWhiteSpace(expectedSaveGenerationId))
            throw new ArgumentException("A save generation is required.", nameof(expectedSaveGenerationId));

        var changed = false;
        snapshot.HistoricalProvenance ??= string.Empty;
        if (!string.Equals(snapshot.SaveGenerationId, expectedSaveGenerationId, StringComparison.Ordinal))
        {
            snapshot.SaveGenerationId = expectedSaveGenerationId;
            snapshot.Money = Unavailable("Persisted Money belonged to a different or missing save generation.");
            snapshot.Cash = Unavailable("Persisted Cash belonged to a different or missing save generation.");
            snapshot.WasRepairedFromInvalidState = true;
            changed = true;
        }
        if (snapshot.Money == null)
        {
            snapshot.Money = Unavailable("Persisted Money observation was missing.");
            snapshot.WasRepairedFromInvalidState = true;
            changed = true;
        }
        if (snapshot.Cash == null)
        {
            snapshot.Cash = Unavailable("Persisted Cash observation was missing.");
            snapshot.WasRepairedFromInvalidState = true;
            changed = true;
        }
        if (snapshot.Capabilities == null)
        {
            snapshot.Capabilities = new EconomyHoldingsMetricCapabilities();
            snapshot.WasRepairedFromInvalidState = true;
            changed = true;
        }

        changed |= NormalizeObservation(snapshot, expectedSaveGenerationId, downgradeCurrent, true);
        changed |= NormalizeObservation(snapshot, expectedSaveGenerationId, downgradeCurrent, false);
        changed |= NormalizeCapabilities(snapshot);
        return changed;
    }

    public static void ValidateRecoveryCandidate(EconomyHoldingsSnapshot snapshot, string expectedSaveGenerationId)
    {
        if (snapshot == null) throw new ArgumentException("Economy holdings root is missing.", nameof(snapshot));
        if (!string.Equals(snapshot.SaveGenerationId, expectedSaveGenerationId, StringComparison.Ordinal))
            throw new ArgumentException("Economy holdings save generation does not match the profile.", nameof(snapshot));
        ValidateObservation(snapshot.Money, expectedSaveGenerationId, "Money");
        ValidateObservation(snapshot.Cash, expectedSaveGenerationId, "Cash");
        if (snapshot.Capabilities == null)
            throw new ArgumentException("Economy holdings capabilities are missing.", nameof(snapshot));
        ValidateAvailability(snapshot.Capabilities.Money, "Money");
        ValidateAvailability(snapshot.Capabilities.Cash, "Cash");
        ValidateAvailability(snapshot.Capabilities.LiquidWealth, "liquid wealth");
        if (snapshot.Money.State == EconomyHoldingObservationState.Current
            && snapshot.Capabilities.Money.State != AdapterCapabilityState.Supported)
            throw new ArgumentException("Current Money observation has no supported current capability.");
        if (snapshot.Cash.State == EconomyHoldingObservationState.Current
            && snapshot.Capabilities.Cash.State != AdapterCapabilityState.Supported)
            throw new ArgumentException("Current Cash observation has no supported current capability.");
        if (snapshot.HistoricalProvenance == null)
            throw new ArgumentException("Economy holdings historical provenance is missing.", nameof(snapshot));
    }

    public static EconomyHoldingsProjection Project(EconomyHoldingsSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        var result = new EconomyHoldingsProjection
        {
            Money = Clone(snapshot.Money),
            Cash = Clone(snapshot.Cash),
            Capabilities = Clone(snapshot.Capabilities),
            LiquidWealth = Unavailable(LiquidUnavailableProvenance)
        };
        if (snapshot.Capabilities.Money.State != AdapterCapabilityState.Supported
            || snapshot.Capabilities.Cash.State != AdapterCapabilityState.Supported
            || snapshot.Capabilities.LiquidWealth.State != AdapterCapabilityState.Supported
            || snapshot.Money.State != EconomyHoldingObservationState.Current
            || snapshot.Cash.State != EconomyHoldingObservationState.Current
            || !snapshot.Money.Value.HasValue
            || !snapshot.Cash.Value.HasValue
            || !string.Equals(snapshot.Money.SaveGenerationId, snapshot.SaveGenerationId, StringComparison.Ordinal)
            || !string.Equals(snapshot.Cash.SaveGenerationId, snapshot.SaveGenerationId, StringComparison.Ordinal))
            return result;

        try
        {
            result.LiquidWealth = new EconomyHoldingObservation
            {
                State = EconomyHoldingObservationState.Current,
                Value = checked(snapshot.Money.Value.Value + snapshot.Cash.Value.Value),
                ObservedUtc = snapshot.Money.ObservedUtc!.Value <= snapshot.Cash.ObservedUtc!.Value
                    ? snapshot.Money.ObservedUtc.Value
                    : snapshot.Cash.ObservedUtc.Value,
                SaveGenerationId = snapshot.SaveGenerationId,
                ObservationProvenance = "Checked Money plus total owned Cash; native ATM exchange proves 1:1 units.",
                FreshnessProvenance = "Current only while both component observations remain current."
            };
        }
        catch (OverflowException)
        {
            result.LiquidWealth = Unavailable(LiquidOverflowProvenance);
        }
        return result;
    }

    public static bool ApplyCapabilities(
        EconomyHoldingsSnapshot snapshot,
        EconomyHoldingsMetricCapabilities capabilities)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
        var replacement = Clone(capabilities);
        if (CapabilitiesEqual(snapshot.Capabilities, replacement)) return false;
        snapshot.Capabilities = replacement;
        return true;
    }

    public static EconomyHoldingsSnapshot Clone(EconomyHoldingsSnapshot source) => new()
    {
        SaveGenerationId = source.SaveGenerationId,
        Money = Clone(source.Money),
        Cash = Clone(source.Cash),
        Capabilities = Clone(source.Capabilities),
        HistoricalUnavailable = source.HistoricalUnavailable,
        HistoricalProvenance = source.HistoricalProvenance,
        WasRepairedFromInvalidState = source.WasRepairedFromInvalidState
    };

    public static bool HasObservedValue(EconomyHoldingsSnapshot snapshot) =>
        snapshot?.Money?.State is EconomyHoldingObservationState.Current or EconomyHoldingObservationState.LastObserved
        || snapshot?.Cash?.State is EconomyHoldingObservationState.Current or EconomyHoldingObservationState.LastObserved;

    public static EconomyHoldingsSnapshot HistoricalUnavailable(string saveGenerationId, string provenance) => new()
    {
        SaveGenerationId = saveGenerationId,
        Money = Unavailable(provenance),
        Cash = Unavailable(provenance),
        Capabilities = EconomyHoldingsNativeContractPolicy.Unavailable(provenance),
        HistoricalUnavailable = true,
        HistoricalProvenance = provenance
    };

    public static EconomyHoldingsMetricCapabilities Clone(EconomyHoldingsMetricCapabilities source) => new()
    {
        Money = Clone(source.Money),
        Cash = Clone(source.Cash),
        LiquidWealth = Clone(source.LiquidWealth)
    };

    private static EconomyHoldingObservation Current(
        long value,
        EconomyHoldingsMutation mutation,
        string nativeSource) => new()
        {
            State = EconomyHoldingObservationState.Current,
            Value = value,
            ObservedUtc = mutation.TimestampUtc,
            SaveGenerationId = mutation.SaveGenerationId,
            ObservationProvenance = string.IsNullOrWhiteSpace(mutation.Provenance)
            ? nativeSource
            : $"{nativeSource}; {mutation.Provenance}",
            FreshnessProvenance = "Confirmed from the active native save generation in this process."
        };

    private static bool ReplaceCurrentIfChanged(
        EconomyHoldingsSnapshot owner,
        bool money,
        EconomyHoldingObservation replacement)
    {
        var existing = money ? owner.Money : owner.Cash;
        if (existing.State == EconomyHoldingObservationState.Current
            && existing.Value == replacement.Value
            && string.Equals(existing.SaveGenerationId, replacement.SaveGenerationId, StringComparison.Ordinal))
            return false;
        if (money) owner.Money = replacement; else owner.Cash = replacement;
        return true;
    }

    private static void ValidateBoundary(EconomyHoldingsSnapshot snapshot, string generation, string label)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (!string.Equals(snapshot.SaveGenerationId, generation, StringComparison.Ordinal))
            throw new InvalidOperationException($"Economy holdings {label} boundary belongs to a different save generation.");
    }

    private static bool MarkNotCurrent(EconomyHoldingObservation observation, string provenance)
    {
        if (observation.State != EconomyHoldingObservationState.Current) return false;
        observation.State = EconomyHoldingObservationState.LastObserved;
        observation.FreshnessProvenance = provenance ?? string.Empty;
        return true;
    }

    private static EconomyHoldingObservation Unavailable(string provenance) => new()
    {
        State = EconomyHoldingObservationState.Unavailable,
        FreshnessProvenance = provenance ?? string.Empty
    };

    private static bool NormalizeObservation(
        EconomyHoldingsSnapshot owner,
        string expectedSaveGenerationId,
        bool downgradeCurrent,
        bool money)
    {
        var observation = money ? owner.Money : owner.Cash;
        var label = money ? "Money" : "Cash";
        observation.ObservationProvenance ??= string.Empty;
        observation.FreshnessProvenance ??= string.Empty;
        var defined = Enum.IsDefined(typeof(EconomyHoldingObservationState), observation.State);
        var observed = observation.State is EconomyHoldingObservationState.Current
            or EconomyHoldingObservationState.LastObserved;
        var validObserved = defined
                            && observed
                            && observation.Value >= 0
                            && observation.ObservedUtc.HasValue
                            && observation.ObservedUtc.Value.Kind == DateTimeKind.Utc
                            && string.Equals(observation.SaveGenerationId, expectedSaveGenerationId, StringComparison.Ordinal);
        var validUnavailable = defined
                               && observation.State == EconomyHoldingObservationState.Unavailable
                               && !observation.Value.HasValue
                               && !observation.ObservedUtc.HasValue
                               && string.IsNullOrEmpty(observation.SaveGenerationId);
        if (!validObserved && !validUnavailable)
        {
            observation = Unavailable($"Persisted {label} observation was invalid and was discarded.");
            if (money) owner.Money = observation; else owner.Cash = observation;
            owner.WasRepairedFromInvalidState = true;
            return true;
        }
        if (downgradeCurrent && observation.State == EconomyHoldingObservationState.Current)
        {
            observation.State = EconomyHoldingObservationState.LastObserved;
            observation.FreshnessProvenance = RestartFreshnessProvenance;
            return true;
        }
        return false;
    }

    private static bool NormalizeCapabilities(EconomyHoldingsSnapshot owner)
    {
        var changed = false;
        if (!AvailabilityValid(owner.Capabilities.Money))
        {
            owner.Capabilities.Money = InvalidAvailability();
            changed = true;
        }
        if (!AvailabilityValid(owner.Capabilities.Cash))
        {
            owner.Capabilities.Cash = InvalidAvailability();
            changed = true;
        }
        if (!AvailabilityValid(owner.Capabilities.LiquidWealth))
        {
            owner.Capabilities.LiquidWealth = InvalidAvailability();
            changed = true;
        }
        if (changed) owner.WasRepairedFromInvalidState = true;
        return changed;
    }

    private static bool AvailabilityValid(MetricAvailability value) =>
        value != null
        && Enum.IsDefined(typeof(AdapterCapabilityState), value.State)
        && value.Provenance != null;

    private static MetricAvailability InvalidAvailability() => new()
    {
        State = AdapterCapabilityState.DisabledIncompatible,
        Provenance = "Invalid persisted economy-holdings capability was disabled."
    };

    private static void ValidateObservation(
        EconomyHoldingObservation observation,
        string expectedSaveGenerationId,
        string label)
    {
        if (observation == null || !Enum.IsDefined(typeof(EconomyHoldingObservationState), observation.State))
            throw new ArgumentException($"{label} observation state is invalid.");
        if (observation.ObservationProvenance == null || observation.FreshnessProvenance == null)
            throw new ArgumentException($"{label} observation provenance is missing.");
        if (observation.State == EconomyHoldingObservationState.Unavailable)
        {
            if (observation.Value.HasValue || observation.ObservedUtc.HasValue || !string.IsNullOrEmpty(observation.SaveGenerationId))
                throw new ArgumentException($"Unavailable {label} exposes stale value evidence.");
            return;
        }
        if (!observation.Value.HasValue || observation.Value.Value < 0)
            throw new ArgumentException($"Observed {label} value is invalid.");
        if (!observation.ObservedUtc.HasValue || observation.ObservedUtc.Value.Kind != DateTimeKind.Utc)
            throw new ArgumentException($"Observed {label} timestamp is not UTC.");
        if (!string.Equals(observation.SaveGenerationId, expectedSaveGenerationId, StringComparison.Ordinal))
            throw new ArgumentException($"Observed {label} belongs to a different save generation.");
    }

    private static void ValidateAvailability(MetricAvailability value, string label)
    {
        if (!AvailabilityValid(value))
            throw new ArgumentException($"Economy holdings {label} capability is invalid.");
    }

    private static EconomyHoldingObservation Clone(EconomyHoldingObservation source) => new()
    {
        State = source.State,
        Value = source.Value,
        ObservedUtc = source.ObservedUtc,
        SaveGenerationId = source.SaveGenerationId,
        ObservationProvenance = source.ObservationProvenance,
        FreshnessProvenance = source.FreshnessProvenance
    };

    private static MetricAvailability Clone(MetricAvailability source) => new()
    { State = source.State, Provenance = source.Provenance };

    private static bool ObservationEqual(EconomyHoldingObservation left, EconomyHoldingObservation right) =>
        left.State == right.State
        && left.Value == right.Value
        && left.ObservedUtc == right.ObservedUtc
        && string.Equals(left.SaveGenerationId, right.SaveGenerationId, StringComparison.Ordinal)
        && string.Equals(left.ObservationProvenance, right.ObservationProvenance, StringComparison.Ordinal)
        && string.Equals(left.FreshnessProvenance, right.FreshnessProvenance, StringComparison.Ordinal);

    private static bool CapabilitiesEqual(
        EconomyHoldingsMetricCapabilities left,
        EconomyHoldingsMetricCapabilities right) =>
        AvailabilityEqual(left.Money, right.Money)
        && AvailabilityEqual(left.Cash, right.Cash)
        && AvailabilityEqual(left.LiquidWealth, right.LiquidWealth);

    private static bool AvailabilityEqual(MetricAvailability left, MetricAvailability right) =>
        left.State == right.State && string.Equals(left.Provenance, right.Provenance, StringComparison.Ordinal);
}
