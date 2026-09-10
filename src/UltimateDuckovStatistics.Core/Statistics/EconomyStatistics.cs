using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

[DataContract]
public sealed class CurrencyFlowTotals
{
    [DataMember(Order = 1)] public long GrossInflow { get; set; }
    [DataMember(Order = 2)] public long GrossOutflow { get; set; }

    public long NetFlow => EconomyStatisticsReducer.SaturatingDifference(GrossInflow, GrossOutflow);
}

[DataContract]
public sealed class CurrencyEconomyAggregate
{
    [DataMember(Order = 1)] public CurrencyKind Currency { get; set; }
    [DataMember(Order = 2)] public CurrencyFlowTotals Totals { get; set; } = new();
    [DataMember(Order = 3)] public Dictionary<string, CurrencyFlowTotals> Sources { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 4)] public Dictionary<string, CurrencyFlowTotals> Contexts { get; set; } = new(StringComparer.Ordinal);
}



[DataContract]
public sealed class EconomyReplayCursor
{
    [DataMember(Order = 1)] public string ActivationId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public long ClosedThroughSequence { get; set; }
}

[DataContract]
public sealed class EconomyStatisticsAggregate
{
    [DataMember(Order = 1)] public Dictionary<string, CurrencyEconomyAggregate> Currencies { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 2)] public long CashAcquired { get; set; }
    [DataMember(Order = 3)] public EconomyMetricCapabilities Capabilities { get; set; } = new();
    [DataMember(Order = 6)] public bool WasRepairedFromInvalidState { get; set; }
    [DataMember(Order = 10)] public bool MoneyArithmeticSaturated { get; set; }
    [DataMember(Order = 11)] public bool CashArithmeticSaturated { get; set; }
    [DataMember(Order = 12)] public EconomyReplayCursor ReplayCursor { get; set; } = new();
}

public static class EconomyStatisticsReducer
{

    public static bool Record(EconomyStatisticsAggregate aggregate, string saveGenerationId, CurrencyFlowRecorded value)
        => Record(aggregate, saveGenerationId, value, out _);

    public static bool Record(
        EconomyStatisticsAggregate aggregate,
        string saveGenerationId,
        CurrencyFlowRecorded value,
        out bool capabilityChanged)
    {
        capabilityChanged = false;
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (value == null) throw new ArgumentNullException(nameof(value));
        ValidateEvent(value, saveGenerationId);
        NormalizePersisted(aggregate);
        if (!TryAcceptReplayIdentity(aggregate.ReplayCursor!, value)) return false;
        if (value.Currency == CurrencyKind.Money && aggregate.MoneyArithmeticSaturated) return false;
        if (value.Currency == CurrencyKind.Cash && aggregate.CashArithmeticSaturated) return false;

        var currency = GetCurrency(aggregate, value.Currency);
        if (WouldOverflow(currency.Totals, value.Direction, value.Amount)
            || (value.Currency == CurrencyKind.Cash
                && value.ProvenExternalRaidAcquisition
                && WouldOverflow(aggregate.CashAcquired, value.Amount)))
        {
            ApplyArithmeticSaturation(aggregate, value.Currency);
            capabilityChanged = true;
            return false;
        }
        Apply(currency.Totals, value.Direction, value.Amount);
        Apply(GetBreakdown(currency.Sources, value.Source.ToString()), value.Direction, value.Amount);
        Apply(GetBreakdown(currency.Contexts, value.GameplayContext.ToString()), value.Direction, value.Amount);

        if (value.Currency == CurrencyKind.Cash && !string.IsNullOrWhiteSpace(value.RunId))
        {
            if (value.ProvenExternalRaidAcquisition)
            {
                aggregate.CashAcquired = SaturatingAdd(aggregate.CashAcquired, value.Amount);
            }

        }

        return true;
    }

    public static void Merge(EconomyStatisticsAggregate target, EconomyStatisticsAggregate source)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (source == null) throw new ArgumentNullException(nameof(source));
        var targetWasUninitialized = IsUninitialized(target);
        NormalizePersisted(target);
        NormalizePersisted(source);
        var moneyOverflow = CurrencyMergeWouldOverflow(target, source, CurrencyKind.Money);
        var cashOverflow = CurrencyMergeWouldOverflow(target, source, CurrencyKind.Cash)
                           || WouldOverflow(target.CashAcquired, source.CashAcquired);
        var mergeMoney = !target.MoneyArithmeticSaturated && !moneyOverflow;
        var mergeCash = !target.CashArithmeticSaturated && !cashOverflow;
        foreach (var row in source.Currencies.Values)
        {
            if (row.Currency == CurrencyKind.Money && !mergeMoney) continue;
            if (row.Currency == CurrencyKind.Cash && !mergeCash) continue;
            var destination = GetCurrency(target, row.Currency);
            Merge(destination.Totals, row.Totals);
            foreach (var entry in row.Sources) Merge(GetBreakdown(destination.Sources, entry.Key), entry.Value);
            foreach (var entry in row.Contexts) Merge(GetBreakdown(destination.Contexts, entry.Key), entry.Value);
        }
        if (mergeCash)
        {
            target.CashAcquired = SaturatingAdd(target.CashAcquired, source.CashAcquired);
        }
        target.WasRepairedFromInvalidState |= source.WasRepairedFromInvalidState;
        target.MoneyArithmeticSaturated |= source.MoneyArithmeticSaturated;
        target.CashArithmeticSaturated |= source.CashArithmeticSaturated;
        target.Capabilities = targetWasUninitialized
            ? CloneCapabilities(source.Capabilities)
            : MergeCapabilities(target.Capabilities, source.Capabilities);
        if (moneyOverflow || source.MoneyArithmeticSaturated)
            ApplyArithmeticSaturation(target, CurrencyKind.Money);
        if (cashOverflow || source.CashArithmeticSaturated)
            ApplyArithmeticSaturation(target, CurrencyKind.Cash);
    }

    public static EconomyStatisticsAggregate Clone(EconomyStatisticsAggregate? source)
    {
        source ??= new EconomyStatisticsAggregate();
        NormalizePersisted(source);
        var clone = new EconomyStatisticsAggregate
        {
            CashAcquired = source.CashAcquired,
            Capabilities = CloneCapabilities(source.Capabilities),
            WasRepairedFromInvalidState = source.WasRepairedFromInvalidState,
            MoneyArithmeticSaturated = source.MoneyArithmeticSaturated,
            CashArithmeticSaturated = source.CashArithmeticSaturated,
            ReplayCursor = CloneReplayCursor(source.ReplayCursor)
        };
        foreach (var entry in source.Currencies)
        {
            var row = new CurrencyEconomyAggregate { Currency = entry.Value.Currency };
            Merge(row.Totals, entry.Value.Totals);
            foreach (var sourceRow in entry.Value.Sources) Merge(GetBreakdown(row.Sources, sourceRow.Key), sourceRow.Value);
            foreach (var contextRow in entry.Value.Contexts) Merge(GetBreakdown(row.Contexts, contextRow.Key), contextRow.Value);
            clone.Currencies[entry.Key] = row;
        }
        return clone;
    }

    public static bool TrySubtract(
        EconomyStatisticsAggregate total,
        EconomyStatisticsAggregate baseline,
        out EconomyStatisticsAggregate difference)
    {
        if (total == null) throw new ArgumentNullException(nameof(total));
        if (baseline == null) throw new ArgumentNullException(nameof(baseline));
        ValidateRecoveryCandidate(total);
        ValidateRecoveryCandidate(baseline);
        if (baseline.MoneyArithmeticSaturated && !total.MoneyArithmeticSaturated
            || baseline.CashArithmeticSaturated && !total.CashArithmeticSaturated)
        {
            difference = new EconomyStatisticsAggregate();
            return false;
        }
        difference = new EconomyStatisticsAggregate
        {
            Capabilities = CloneCapabilities(total.Capabilities),
            WasRepairedFromInvalidState = total.WasRepairedFromInvalidState,
            MoneyArithmeticSaturated = total.MoneyArithmeticSaturated,
            CashArithmeticSaturated = total.CashArithmeticSaturated
        };
        foreach (var totalEntry in total.Currencies)
        {
            baseline.Currencies.TryGetValue(totalEntry.Key, out var baselineCurrency);
            var row = new CurrencyEconomyAggregate { Currency = totalEntry.Value.Currency };
            if (!TrySubtract(totalEntry.Value.Totals, baselineCurrency?.Totals, row.Totals)) return false;
            if (!TrySubtractRows(totalEntry.Value.Sources, baselineCurrency?.Sources, row.Sources)) return false;
            if (!TrySubtractRows(totalEntry.Value.Contexts, baselineCurrency?.Contexts, row.Contexts)) return false;
            if (row.Totals.GrossInflow > 0 || row.Totals.GrossOutflow > 0) difference.Currencies[totalEntry.Key] = row;
        }
        if (baseline.Currencies.Keys.Any(key => !total.Currencies.ContainsKey(key))) return false;
        if (!TrySubtract(total.CashAcquired, baseline.CashAcquired, out var acquired))
            return false;
        difference.CashAcquired = acquired;
        return true;
    }

    public static bool IsEmpty(EconomyStatisticsAggregate value)
    {
        if (value == null) return true;
        if (value.Currencies == null) return false;
        return value.Currencies.Values.All(row => row.Totals.GrossInflow == 0 && row.Totals.GrossOutflow == 0)
               && value.CashAcquired == 0;
    }

    public static bool HasExactSupportedCurrency(EconomyStatisticsAggregate aggregate, CurrencyKind currency)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        return currency switch
        {
            CurrencyKind.Money => !aggregate.MoneyArithmeticSaturated
                                  && aggregate.Capabilities.MoneyAmountDirection.State == AdapterCapabilityState.Supported,
            CurrencyKind.Cash => !aggregate.CashArithmeticSaturated
                                 && aggregate.Capabilities.CashAmountDirection.State == AdapterCapabilityState.Supported,
            _ => false
        };
    }

    public static bool IsExactCurrencyComposition(
        EconomyStatisticsAggregate total,
        IEnumerable<EconomyStatisticsAggregate> components,
        CurrencyKind currency)
    {
        if (total == null) throw new ArgumentNullException(nameof(total));
        if (components == null) throw new ArgumentNullException(nameof(components));
        var supportedComposition = HasExactSupportedCurrency(total, currency);
        if (!supportedComposition) return true;

        var expected = new CurrencyEconomyAggregate { Currency = currency };
        foreach (var component in components)
        {
            if (component == null) return false;
            if (supportedComposition && !HasExactSupportedCurrency(component, currency)) return false;
            if (!component.Currencies.TryGetValue(currency.ToString(), out var row)) continue;
            if (!TryMergeExact(expected, row)) return false;
        }

        total.Currencies.TryGetValue(currency.ToString(), out var actual);
        return CurrencyRowsEqual(actual, expected);
    }

    public static bool IsExactCashAcquisitionComposition(
        EconomyStatisticsAggregate total,
        IEnumerable<EconomyStatisticsAggregate> components)
    {
        if (total == null) throw new ArgumentNullException(nameof(total));
        if (components == null) throw new ArgumentNullException(nameof(components));
        if (total.CashArithmeticSaturated) return true;

        long expected = 0;
        foreach (var component in components)
        {
            if (component == null || component.CashArithmeticSaturated
                                  || !TryAddExact(expected, component.CashAcquired, out expected))
                return false;
        }
        return total.CashAcquired == expected;
    }

    private static bool IsCurrencyArithmeticSaturated(EconomyStatisticsAggregate aggregate, CurrencyKind currency) =>
        currency switch
        {
            CurrencyKind.Money => aggregate.MoneyArithmeticSaturated,
            CurrencyKind.Cash => aggregate.CashArithmeticSaturated,
            _ => true
        };

    public static bool NormalizePersisted(EconomyStatisticsAggregate aggregate)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        var repaired = false;
        aggregate.Currencies ??= Repair(new Dictionary<string, CurrencyEconomyAggregate>(StringComparer.Ordinal), ref repaired);
        aggregate.Capabilities ??= Repair(new EconomyMetricCapabilities(), ref repaired);
        aggregate.ReplayCursor ??= Repair(new EconomyReplayCursor(), ref repaired);
        NormalizeCapabilities(aggregate.Capabilities, ref repaired);
        NormalizeReplayCursor(aggregate.ReplayCursor, ref repaired);
        var normalized = new Dictionary<string, CurrencyEconomyAggregate>(StringComparer.Ordinal);
        foreach (var entry in aggregate.Currencies)
        {
            if (entry.Value == null || !Enum.IsDefined(typeof(CurrencyKind), entry.Value.Currency)) { repaired = true; continue; }
            NormalizeCurrency(entry.Value, ref repaired);
            var key = entry.Value.Currency.ToString();
            if (normalized.TryGetValue(key, out var current)) { Merge(current, entry.Value); repaired = true; }
            else normalized[key] = entry.Value;
            if (!string.Equals(entry.Key, key, StringComparison.Ordinal)) repaired = true;
        }
        aggregate.Currencies = normalized;
        if (aggregate.MoneyArithmeticSaturated)
        {
            if (MoneyCapabilities(aggregate.Capabilities).Any(value => value.State != AdapterCapabilityState.DisabledIncompatible))
                repaired = true;
            ApplyArithmeticSaturation(aggregate, CurrencyKind.Money);
        }
        if (aggregate.CashArithmeticSaturated)
        {
            if (CashCapabilities(aggregate.Capabilities).Any(value => value.State != AdapterCapabilityState.DisabledIncompatible))
                repaired = true;
            ApplyArithmeticSaturation(aggregate, CurrencyKind.Cash);
        }
        aggregate.CashAcquired = NonNegative(aggregate.CashAcquired, ref repaired);
        aggregate.WasRepairedFromInvalidState |= repaired;
        return repaired;
    }

    public static void Validate(EconomyStatisticsAggregate aggregate)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (aggregate.Currencies == null || aggregate.Capabilities == null || aggregate.ReplayCursor == null)
            throw new ArgumentException("Economy roots are missing.", nameof(aggregate));
        ValidateReplayCursor(aggregate.ReplayCursor);
        foreach (var entry in aggregate.Currencies)
        {
            if (entry.Value == null || !Enum.IsDefined(typeof(CurrencyKind), entry.Value.Currency) || entry.Key != entry.Value.Currency.ToString())
                throw new ArgumentException("Economy currency identity is invalid.", nameof(aggregate));
            ValidateCurrency(entry.Value);
        }
        ValidateCashAcquired(aggregate.CashAcquired);
        ValidateCapabilities(aggregate.Capabilities);
        if (aggregate.MoneyArithmeticSaturated
            && MoneyCapabilities(aggregate.Capabilities).Any(value => value.State != AdapterCapabilityState.DisabledIncompatible))
            throw new ArgumentException("Money arithmetic saturation state is inconsistent with its capabilities.", nameof(aggregate));
        if (aggregate.CashArithmeticSaturated
            && CashCapabilities(aggregate.Capabilities).Any(value => value.State != AdapterCapabilityState.DisabledIncompatible))
            throw new ArgumentException("Cash arithmetic saturation state is inconsistent with its capabilities.", nameof(aggregate));
    }

    public static void ValidateRecoveryCandidate(EconomyStatisticsAggregate aggregate)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (aggregate.Currencies == null
            || aggregate.Capabilities == null || aggregate.ReplayCursor == null)
            throw new ArgumentException("Economy roots are missing.", nameof(aggregate));
        ValidateReplayCursor(aggregate.ReplayCursor);
        foreach (var entry in aggregate.Currencies)
        {
            var value = entry.Value;
            if (value == null || !Enum.IsDefined(typeof(CurrencyKind), value.Currency)
                || !string.Equals(entry.Key, value.Currency.ToString(), StringComparison.Ordinal)
                || value.Totals == null || value.Sources == null || value.Contexts == null)
                throw new ArgumentException("Economy currency evidence is incomplete.", nameof(aggregate));
            if (value.Totals.GrossInflow < 0 || value.Totals.GrossOutflow < 0
                || value.Sources.Values.Any(row => row == null || row.GrossInflow < 0 || row.GrossOutflow < 0)
                || value.Contexts.Values.Any(row => row == null || row.GrossInflow < 0 || row.GrossOutflow < 0))
                throw new ArgumentException("Economy counters cannot be negative.", nameof(aggregate));
            if (!Composes(value.Totals, value.Sources) || !Composes(value.Totals, value.Contexts))
                throw new ArgumentException("Economy breakdowns do not compose to their currency totals.", nameof(aggregate));
        }
        ValidateCashAcquired(aggregate.CashAcquired);
    }

    public static EconomyMetricCapabilities CloneCapabilities(EconomyMetricCapabilities source) => new()
    {
        MoneyAmountDirection = Clone(source.MoneyAmountDirection),
        MoneySourceAttribution = Clone(source.MoneySourceAttribution),
        MoneyContextAttribution = Clone(source.MoneyContextAttribution),
        CashAmountDirection = Clone(source.CashAmountDirection),
        CashExternalAcquisition = Clone(source.CashExternalAcquisition),
        CashContextAttribution = Clone(source.CashContextAttribution),
        RouteAttribution = Clone(source.RouteAttribution)
    };

    public static void SetCapabilities(
        EconomyStatisticsAggregate aggregate,
        EconomyMetricCapabilities capabilities)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
        aggregate.Capabilities = CloneCapabilities(capabilities);
        if (aggregate.MoneyArithmeticSaturated) ApplyArithmeticSaturation(aggregate, CurrencyKind.Money);
        if (aggregate.CashArithmeticSaturated) ApplyArithmeticSaturation(aggregate, CurrencyKind.Cash);
    }

    public static void InitializeOrRestrictCapabilities(
        EconomyStatisticsAggregate aggregate,
        EconomyMetricCapabilities capabilities)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
        var aggregateWasUninitialized = IsUninitialized(aggregate);
        NormalizePersisted(aggregate);
        aggregate.Capabilities = aggregateWasUninitialized
            ? CloneCapabilities(capabilities)
            : MergeLifetimeCapabilities(aggregate.Capabilities, capabilities);
        if (aggregate.MoneyArithmeticSaturated) ApplyArithmeticSaturation(aggregate, CurrencyKind.Money);
        if (aggregate.CashArithmeticSaturated) ApplyArithmeticSaturation(aggregate, CurrencyKind.Cash);
    }

    public static bool BeginReplayActivation(EconomyStatisticsAggregate aggregate, string activationId)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        ValidateActivationId(activationId);
        NormalizePersisted(aggregate);
        if (string.Equals(aggregate.ReplayCursor!.ActivationId, activationId, StringComparison.Ordinal)) return false;
        aggregate.ReplayCursor.ActivationId = activationId;
        aggregate.ReplayCursor.ClosedThroughSequence = 0;
        return true;
    }

    public static bool ClearReplayCursor(EconomyStatisticsAggregate aggregate)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (string.IsNullOrEmpty(aggregate.ReplayCursor?.ActivationId)
            && aggregate.ReplayCursor?.ClosedThroughSequence == 0) return false;
        aggregate.ReplayCursor = new EconomyReplayCursor();
        return true;
    }

    public static void ApplyArithmeticSaturation(EconomyStatisticsAggregate aggregate, CurrencyKind currency)
    {
        const string reason = "The economy aggregate reached the Int64 arithmetic limit; prior exact totals remain available, but further capture for this currency is disabled instead of storing an approximate value.";
        if (currency == CurrencyKind.Money)
        {
            aggregate.MoneyArithmeticSaturated = true;
            aggregate.Capabilities.MoneyAmountDirection = RestrictForSaturation(aggregate.Capabilities.MoneyAmountDirection, reason);
            aggregate.Capabilities.MoneySourceAttribution = RestrictForSaturation(aggregate.Capabilities.MoneySourceAttribution, reason);
            aggregate.Capabilities.MoneyContextAttribution = RestrictForSaturation(aggregate.Capabilities.MoneyContextAttribution, reason);
            return;
        }
        aggregate.CashArithmeticSaturated = true;
        aggregate.Capabilities.CashAmountDirection = RestrictForSaturation(aggregate.Capabilities.CashAmountDirection, reason);
        aggregate.Capabilities.CashExternalAcquisition = RestrictForSaturation(aggregate.Capabilities.CashExternalAcquisition, reason);
        aggregate.Capabilities.CashContextAttribution = RestrictForSaturation(aggregate.Capabilities.CashContextAttribution, reason);
    }

    public static long SaturatingDifference(long inflow, long outflow)
    {
        if (inflow < 0 || outflow < 0) throw new ArgumentOutOfRangeException(nameof(inflow));
        if (inflow >= outflow) return inflow - outflow;
        var magnitude = outflow - inflow;
        return magnitude == long.MinValue ? long.MinValue : -magnitude;
    }

    private static void ValidateEvent(CurrencyFlowRecorded value, string generation)
    {
        if (value.SchemaVersion > ProductInfo.SchemaVersion || string.IsNullOrWhiteSpace(value.EventId)
            || string.IsNullOrWhiteSpace(value.SaveGenerationId) || !string.Equals(value.SaveGenerationId, generation, StringComparison.Ordinal)
            || value.TimestampUtc == default || value.Amount <= 0 || !Enum.IsDefined(typeof(CurrencyKind), value.Currency)
            || !Enum.IsDefined(typeof(CurrencyFlowDirection), value.Direction)
            || !Enum.IsDefined(typeof(CurrencySourceCategory), value.Source)
            || !Enum.IsDefined(typeof(GameplayContext), value.GameplayContext)
            || !EventIdentityMatches(value))
            throw new ArgumentException("Currency flow is invalid.", nameof(value));
        var hasRun = !string.IsNullOrWhiteSpace(value.RunId);
        var hasSegment = !string.IsNullOrWhiteSpace(value.SegmentId);
        if ((value.GameplayContext == GameplayContext.Raid) != hasRun)
            throw new ArgumentException("Raid currency flow and run identity must agree.", nameof(value));
        if (hasSegment && !hasRun)
            throw new ArgumentException("A segment identity requires a run identity.", nameof(value));
        if (hasRun && string.IsNullOrWhiteSpace(value.MapId))
            throw new ArgumentException("A run currency flow requires an event-time map identity.", nameof(value));
        if (value.ProvenExternalRaidAcquisition
            && (value.Currency != CurrencyKind.Cash
                || !hasRun
                || value.GameplayContext != GameplayContext.Raid
                || value.Direction != CurrencyFlowDirection.Inflow
                || value.Source != CurrencySourceCategory.LootOrPickup))
            throw new ArgumentException("A proven external raid acquisition must be a Cash flow in an active raid.", nameof(value));
    }

    private static bool TryAcceptReplayIdentity(
        EconomyReplayCursor cursor,
        CurrencyFlowRecorded value)
    {
        if (string.IsNullOrEmpty(cursor.ActivationId))
        {
            cursor.ActivationId = value.ProducerActivationId;
            cursor.ClosedThroughSequence = value.ProducerSequence;
            return true;
        }
        if (!string.Equals(cursor.ActivationId, value.ProducerActivationId, StringComparison.Ordinal))
            return false;
        if (value.ProducerSequence <= cursor.ClosedThroughSequence) return false;
        cursor.ClosedThroughSequence = value.ProducerSequence;
        return true;
    }

    private static bool EventIdentityMatches(CurrencyFlowRecorded value)
    {
        try { ValidateActivationId(value.ProducerActivationId); }
        catch (ArgumentException) { return false; }
        return value.ProducerSequence > 0;
    }

    private static void ValidateActivationId(string activationId)
    {
        if (string.IsNullOrWhiteSpace(activationId) || activationId.Contains(':'))
            throw new ArgumentException("An economy producer activation identity must be non-empty and contain no separator.", nameof(activationId));
    }

    private static void NormalizeReplayCursor(EconomyReplayCursor cursor, ref bool repaired)
    {
        var empty = string.IsNullOrEmpty(cursor.ActivationId) && cursor.ClosedThroughSequence == 0;
        if (empty) return;
        if (!string.IsNullOrWhiteSpace(cursor.ActivationId)
            && !cursor.ActivationId.Contains(':')
            && cursor.ClosedThroughSequence >= 0)
            return;
        cursor.ActivationId = string.Empty;
        cursor.ClosedThroughSequence = 0;
        repaired = true;
    }

    private static void ValidateReplayCursor(EconomyReplayCursor cursor)
    {
        if (cursor == null) throw new ArgumentNullException(nameof(cursor));
        if (string.IsNullOrEmpty(cursor.ActivationId) && cursor.ClosedThroughSequence == 0) return;
        if (string.IsNullOrWhiteSpace(cursor.ActivationId)
            || cursor.ActivationId.Contains(':')
            || cursor.ClosedThroughSequence < 0)
            throw new ArgumentException("Economy replay watermark is invalid.", nameof(cursor));
    }

    private static CurrencyEconomyAggregate GetCurrency(EconomyStatisticsAggregate aggregate, CurrencyKind kind)
    {
        var key = kind.ToString();
        if (!aggregate.Currencies.TryGetValue(key, out var value))
        {
            value = new CurrencyEconomyAggregate { Currency = kind };
            aggregate.Currencies[key] = value;
        }
        return value;
    }

    private static CurrencyFlowTotals GetBreakdown(Dictionary<string, CurrencyFlowTotals> rows, string key)
    {
        if (!rows.TryGetValue(key, out var row)) { row = new CurrencyFlowTotals(); rows[key] = row; }
        return row;
    }

    private static bool TrySubtractRows(
        Dictionary<string, CurrencyFlowTotals> total,
        Dictionary<string, CurrencyFlowTotals>? baseline,
        Dictionary<string, CurrencyFlowTotals> difference)
    {
        baseline ??= new Dictionary<string, CurrencyFlowTotals>(StringComparer.Ordinal);
        foreach (var row in total)
        {
            baseline.TryGetValue(row.Key, out var baselineRow);
            var result = new CurrencyFlowTotals();
            if (!TrySubtract(row.Value, baselineRow, result)) return false;
            if (result.GrossInflow > 0 || result.GrossOutflow > 0) difference[row.Key] = result;
        }
        return !baseline.Keys.Any(key => !total.ContainsKey(key));
    }

    private static bool TrySubtract(CurrencyFlowTotals total, CurrencyFlowTotals? baseline, CurrencyFlowTotals difference)
    {
        baseline ??= new CurrencyFlowTotals();
        if (!TrySubtract(total.GrossInflow, baseline.GrossInflow, out var inflow)
            || !TrySubtract(total.GrossOutflow, baseline.GrossOutflow, out var outflow)) return false;
        difference.GrossInflow = inflow;
        difference.GrossOutflow = outflow;
        return true;
    }

    private static bool TrySubtract(long total, long baseline, out long difference)
    {
        difference = 0;
        if (total < 0 || baseline < 0 || baseline > total) return false;
        difference = total - baseline;
        return true;
    }

    private static void Apply(CurrencyFlowTotals totals, CurrencyFlowDirection direction, long amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (direction == CurrencyFlowDirection.Inflow) totals.GrossInflow = SaturatingAdd(totals.GrossInflow, amount);
        else totals.GrossOutflow = SaturatingAdd(totals.GrossOutflow, amount);
    }

    private static bool WouldOverflow(CurrencyFlowTotals totals, CurrencyFlowDirection direction, long amount) =>
        WouldOverflow(
            direction == CurrencyFlowDirection.Inflow ? totals.GrossInflow : totals.GrossOutflow,
            amount);

    private static bool WouldOverflow(CurrencyFlowTotals target, CurrencyFlowTotals source) =>
        WouldOverflow(target.GrossInflow, source.GrossInflow)
        || WouldOverflow(target.GrossOutflow, source.GrossOutflow);

    private static bool CurrencyMergeWouldOverflow(
        EconomyStatisticsAggregate target,
        EconomyStatisticsAggregate source,
        CurrencyKind currency)
    {
        if (!source.Currencies.TryGetValue(currency.ToString(), out var sourceValue)) return false;
        return target.Currencies.TryGetValue(currency.ToString(), out var targetValue)
               && WouldOverflow(targetValue.Totals, sourceValue.Totals);
    }


    private static bool TryAddExact(long left, long right, out long result)
    {
        result = 0;
        if (left < 0 || right < 0 || WouldOverflow(left, right)) return false;
        result = left + right;
        return true;
    }

    private static bool WouldOverflow(long left, long right) => left > long.MaxValue - right;

    private static void Merge(CurrencyFlowTotals target, CurrencyFlowTotals source)
    {
        if (WouldOverflow(target, source))
            throw new InvalidOperationException("Economy aggregate merge would exceed the exact arithmetic range.");
        target.GrossInflow += source.GrossInflow;
        target.GrossOutflow += source.GrossOutflow;
    }

    private static void Merge(CurrencyEconomyAggregate target, CurrencyEconomyAggregate source)
    {
        Merge(target.Totals, source.Totals);
        foreach (var row in source.Sources) Merge(GetBreakdown(target.Sources, row.Key), row.Value);
        foreach (var row in source.Contexts) Merge(GetBreakdown(target.Contexts, row.Key), row.Value);
    }

    private static bool TryMergeExact(CurrencyEconomyAggregate target, CurrencyEconomyAggregate source)
    {
        if (WouldOverflow(target.Totals, source.Totals)
            || source.Sources.Any(row =>
                target.Sources.TryGetValue(row.Key, out var current) && WouldOverflow(current, row.Value))
            || source.Contexts.Any(row =>
                target.Contexts.TryGetValue(row.Key, out var current) && WouldOverflow(current, row.Value)))
            return false;

        Merge(target, source);
        return true;
    }

    private static bool CurrencyRowsEqual(CurrencyEconomyAggregate? actual, CurrencyEconomyAggregate expected)
    {
        actual ??= new CurrencyEconomyAggregate { Currency = expected.Currency };
        return actual.Currency == expected.Currency
               && TotalsEqual(actual.Totals, expected.Totals)
               && BreakdownEqual(actual.Sources, expected.Sources)
               && BreakdownEqual(actual.Contexts, expected.Contexts);
    }

    private static bool BreakdownEqual(
        IReadOnlyDictionary<string, CurrencyFlowTotals> actual,
        IReadOnlyDictionary<string, CurrencyFlowTotals> expected)
    {
        var actualRows = actual.Where(row => row.Value.GrossInflow != 0 || row.Value.GrossOutflow != 0)
            .ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        var expectedRows = expected.Where(row => row.Value.GrossInflow != 0 || row.Value.GrossOutflow != 0)
            .ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        return actualRows.Count == expectedRows.Count
               && actualRows.All(row => expectedRows.TryGetValue(row.Key, out var value) && TotalsEqual(row.Value, value));
    }

    private static bool TotalsEqual(CurrencyFlowTotals left, CurrencyFlowTotals right) =>
        left.GrossInflow == right.GrossInflow && left.GrossOutflow == right.GrossOutflow;

    private static EconomyMetricCapabilities MergeCapabilities(EconomyMetricCapabilities a, EconomyMetricCapabilities b) => new()
    {
        MoneyAmountDirection = Restrict(a.MoneyAmountDirection, b.MoneyAmountDirection),
        MoneySourceAttribution = Restrict(a.MoneySourceAttribution, b.MoneySourceAttribution),
        MoneyContextAttribution = Restrict(a.MoneyContextAttribution, b.MoneyContextAttribution),
        CashAmountDirection = Restrict(a.CashAmountDirection, b.CashAmountDirection),
        CashExternalAcquisition = Restrict(a.CashExternalAcquisition, b.CashExternalAcquisition),
        CashContextAttribution = Restrict(a.CashContextAttribution, b.CashContextAttribution),
        RouteAttribution = Restrict(a.RouteAttribution, b.RouteAttribution)
    };

    private static EconomyMetricCapabilities MergeLifetimeCapabilities(
        EconomyMetricCapabilities recorded,
        EconomyMetricCapabilities current) => new()
        {
            MoneyAmountDirection = RestrictLifetime(recorded.MoneyAmountDirection, current.MoneyAmountDirection),
            MoneySourceAttribution = RestrictLifetime(recorded.MoneySourceAttribution, current.MoneySourceAttribution),
            MoneyContextAttribution = RestrictLifetime(recorded.MoneyContextAttribution, current.MoneyContextAttribution),
            CashAmountDirection = RestrictLifetime(recorded.CashAmountDirection, current.CashAmountDirection),
            CashExternalAcquisition = RestrictLifetime(recorded.CashExternalAcquisition, current.CashExternalAcquisition),
            CashContextAttribution = RestrictLifetime(recorded.CashContextAttribution, current.CashContextAttribution),
            RouteAttribution = RestrictLifetime(recorded.RouteAttribution, current.RouteAttribution)
        };

    private static MetricAvailability Restrict(MetricAvailability a, MetricAvailability b) =>
        (int)a.State >= (int)b.State ? Clone(a) : Clone(b);
    private static MetricAvailability RestrictLifetime(MetricAvailability recorded, MetricAvailability current)
    {
        if (IsBootstrapPlaceholder(current)) return Clone(recorded);
        if (IsBlankDefault(recorded) || IsBootstrapPlaceholder(recorded)) return Clone(current);
        return Restrict(recorded, current);
    }
    private static bool IsBlankDefault(MetricAvailability value) =>
        value.State == AdapterCapabilityState.DisabledIncompatible
        && string.IsNullOrWhiteSpace(value.Provenance);
    private static bool IsBootstrapPlaceholder(MetricAvailability value) =>
        value.State == AdapterCapabilityState.DisabledIncompatible
        && string.Equals(
            value.Provenance,
            EconomyNativeContractPolicy.BootstrapProvenance,
            StringComparison.Ordinal);
    private static MetricAvailability Clone(MetricAvailability value) => new() { State = value.State, Provenance = value.Provenance ?? string.Empty };
    private static MetricAvailability Unavailable(string provenance) => new()
    { State = AdapterCapabilityState.DisabledIncompatible, Provenance = provenance };
    private static MetricAvailability RestrictForSaturation(MetricAvailability current, string provenance) =>
        current.State == AdapterCapabilityState.DisabledIncompatible ? Clone(current) : Unavailable(provenance);

    private static bool IsUninitialized(EconomyStatisticsAggregate value)
    {
        if (!IsEmpty(value)
            || value.WasRepairedFromInvalidState
            || value.MoneyArithmeticSaturated
            || value.CashArithmeticSaturated
            || value.Capabilities == null)
            return false;

        return Capabilities(value.Capabilities).All(capability =>
            capability != null
            && capability.State == AdapterCapabilityState.DisabledIncompatible
            && string.IsNullOrWhiteSpace(capability.Provenance));
    }

    private static void NormalizeCurrency(CurrencyEconomyAggregate value, ref bool repaired)
    {
        value.Totals ??= Repair(new CurrencyFlowTotals(), ref repaired);
        value.Sources ??= Repair(new Dictionary<string, CurrencyFlowTotals>(StringComparer.Ordinal), ref repaired);
        value.Contexts ??= Repair(new Dictionary<string, CurrencyFlowTotals>(StringComparer.Ordinal), ref repaired);
        NormalizeTotals(value.Totals, ref repaired);
        NormalizeRows(value.Sources, ref repaired);
        NormalizeRows(value.Contexts, ref repaired);
        value.Sources = NormalizeBreakdownKeys(
            value.Sources,
            Enum.GetNames(typeof(CurrencySourceCategory)),
            CurrencySourceCategory.UnknownAdjustment.ToString(),
            ref repaired);
        value.Contexts = NormalizeBreakdownKeys(
            value.Contexts,
            Enum.GetNames(typeof(GameplayContext)),
            GameplayContext.Unknown.ToString(),
            ref repaired);
        if (!Composes(value.Totals, value.Sources) || !Composes(value.Totals, value.Contexts))
        {
            value.Sources = new Dictionary<string, CurrencyFlowTotals>(StringComparer.Ordinal)
            { [CurrencySourceCategory.UnknownAdjustment.ToString()] = CloneTotals(value.Totals) };
            value.Contexts = new Dictionary<string, CurrencyFlowTotals>(StringComparer.Ordinal)
            { [GameplayContext.Unknown.ToString()] = CloneTotals(value.Totals) };
            repaired = true;
        }
    }

    private static void NormalizeRows(Dictionary<string, CurrencyFlowTotals> rows, ref bool repaired)
    {
        foreach (var key in rows.Where(row => string.IsNullOrWhiteSpace(row.Key) || row.Value == null).Select(row => row.Key).ToList()) { rows.Remove(key); repaired = true; }
        foreach (var row in rows.Values) NormalizeTotals(row, ref repaired);
    }

    private static Dictionary<string, CurrencyFlowTotals> NormalizeBreakdownKeys(
        Dictionary<string, CurrencyFlowTotals> rows,
        IReadOnlyCollection<string> validKeys,
        string fallback,
        ref bool repaired)
    {
        var normalized = new Dictionary<string, CurrencyFlowTotals>(StringComparer.Ordinal);
        foreach (var row in rows.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            var key = validKeys.Contains(row.Key, StringComparer.Ordinal) ? row.Key : fallback;
            if (!string.Equals(key, row.Key, StringComparison.Ordinal)) repaired = true;
            if (normalized.TryGetValue(key, out var existing))
            {
                Merge(existing, row.Value);
                repaired = true;
            }
            else
            {
                normalized[key] = row.Value;
            }
        }
        return normalized;
    }
    private static void NormalizeTotals(CurrencyFlowTotals totals, ref bool repaired)
    {
        totals.GrossInflow = NonNegative(totals.GrossInflow, ref repaired);
        totals.GrossOutflow = NonNegative(totals.GrossOutflow, ref repaired);
    }
    private static long NonNegative(long value, ref bool repaired)
    {
        if (value >= 0) return value;
        repaired = true;
        return 0;
    }
    private static void NormalizeCapabilities(EconomyMetricCapabilities value, ref bool repaired)
    {
        value.MoneyAmountDirection ??= Repair(new MetricAvailability(), ref repaired); value.MoneySourceAttribution ??= Repair(new MetricAvailability(), ref repaired);
        value.MoneyContextAttribution ??= Repair(new MetricAvailability(), ref repaired); value.CashAmountDirection ??= Repair(new MetricAvailability(), ref repaired);
        value.CashExternalAcquisition ??= Repair(new MetricAvailability(), ref repaired); value.CashContextAttribution ??= Repair(new MetricAvailability(), ref repaired);
        value.RouteAttribution ??= Repair(new MetricAvailability(), ref repaired);
        foreach (var availability in Capabilities(value))
        {
            if (!Enum.IsDefined(typeof(AdapterCapabilityState), availability.State))
            {
                availability.State = AdapterCapabilityState.DisabledIncompatible;
                availability.Provenance = "Invalid persisted economy capability state was repaired.";
                repaired = true;
            }
            else if (availability.Provenance == null)
            {
                availability.Provenance = string.Empty;
                repaired = true;
            }
        }
    }

    private static bool Composes(CurrencyFlowTotals total, Dictionary<string, CurrencyFlowTotals> rows) =>
        TrySumExactly(rows.Values.Select(row => row.GrossInflow), out var inflow)
        && inflow == total.GrossInflow
        && TrySumExactly(rows.Values.Select(row => row.GrossOutflow), out var outflow)
        && outflow == total.GrossOutflow;
    private static bool CanSumExactly(IEnumerable<long> values) => TrySumExactly(values, out _);
    private static bool TrySumExactly(IEnumerable<long> values, out long result)
    {
        result = 0;
        foreach (var value in values)
        {
            if (value < 0 || WouldOverflow(result, value)) return false;
            result += value;
        }
        return true;
    }
    private static CurrencyFlowTotals CloneTotals(CurrencyFlowTotals source) => new() { GrossInflow = source.GrossInflow, GrossOutflow = source.GrossOutflow };
    private static EconomyReplayCursor CloneReplayCursor(EconomyReplayCursor source) => new()
    {
        ActivationId = source.ActivationId,
        ClosedThroughSequence = source.ClosedThroughSequence
    };
    private static void ValidateCurrency(CurrencyEconomyAggregate value)
    {
        var validSources = Enum.GetNames(typeof(CurrencySourceCategory));
        var validContexts = Enum.GetNames(typeof(GameplayContext));
        if (value.Totals == null || value.Sources == null || value.Contexts == null || value.Totals.GrossInflow < 0 || value.Totals.GrossOutflow < 0
            || value.Sources.Any(row => string.IsNullOrWhiteSpace(row.Key) || row.Value == null || row.Value.GrossInflow < 0 || row.Value.GrossOutflow < 0)
            || value.Contexts.Any(row => string.IsNullOrWhiteSpace(row.Key) || row.Value == null || row.Value.GrossInflow < 0 || row.Value.GrossOutflow < 0)
            || value.Sources.Keys.Any(key => !validSources.Contains(key, StringComparer.Ordinal))
            || value.Contexts.Keys.Any(key => !validContexts.Contains(key, StringComparer.Ordinal))
            || !Composes(value.Totals, value.Sources) || !Composes(value.Totals, value.Contexts))
            throw new ArgumentException("Economy totals do not compose.", nameof(value));
    }

    private static IEnumerable<MetricAvailability> Capabilities(EconomyMetricCapabilities value)
    {
        yield return value.MoneyAmountDirection;
        yield return value.MoneySourceAttribution;
        yield return value.MoneyContextAttribution;
        yield return value.CashAmountDirection;
        yield return value.CashExternalAcquisition;
        yield return value.CashContextAttribution;
        yield return value.RouteAttribution;
    }

    private static IEnumerable<MetricAvailability> CaptureCapabilities(EconomyMetricCapabilities value)
    {
        yield return value.MoneyAmountDirection;
        yield return value.MoneySourceAttribution;
        yield return value.MoneyContextAttribution;
        yield return value.CashAmountDirection;
        yield return value.CashExternalAcquisition;
        yield return value.CashContextAttribution;
        yield return value.RouteAttribution;
    }

    private static IEnumerable<MetricAvailability> MoneyCapabilities(EconomyMetricCapabilities value)
    {
        yield return value.MoneyAmountDirection;
        yield return value.MoneySourceAttribution;
        yield return value.MoneyContextAttribution;
    }

    private static IEnumerable<MetricAvailability> CashCapabilities(EconomyMetricCapabilities value)
    {
        yield return value.CashAmountDirection;
        yield return value.CashExternalAcquisition;
        yield return value.CashContextAttribution;
    }

    private static void ValidateCapabilities(EconomyMetricCapabilities value)
    {
        if (Capabilities(value).Any(availability =>
                availability == null || !Enum.IsDefined(typeof(AdapterCapabilityState), availability.State)))
            throw new ArgumentException("Economy capabilities are incomplete.", nameof(value));
    }
    private static void ValidateCashAcquired(long value)
    {
        if (value < 0) throw new ArgumentException("Cash acquisition must be non-negative.", nameof(value));
    }
    private static T Repair<T>(T value, ref bool repaired) { repaired = true; return value; }
    private static long SaturatingAdd(long left, long right)
    {
        if (left < 0 || right < 0) throw new ArgumentOutOfRangeException(nameof(left));
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }
}
