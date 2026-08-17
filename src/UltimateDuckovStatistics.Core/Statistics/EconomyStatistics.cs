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
public sealed class CashRaidOutcomeAggregate
{
    [DataMember(Order = 1)] public long Acquired { get; set; }
    [DataMember(Order = 2)] public long Secured { get; set; }
    [DataMember(Order = 3)] public long Lost { get; set; }
    [DataMember(Order = 4)] public long Unresolved { get; set; }
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
    [DataMember(Order = 2)] public CashRaidOutcomeAggregate CashRaidOutcomes { get; set; } = new();
    [DataMember(Order = 3)] public EconomyMetricCapabilities Capabilities { get; set; } = new();
    // Legacy schema-9 candidate evidence. Corrected M9 never appends here; it is
    // compacted only after old checkpoint recovery artifacts are no longer replayable.
    [DataMember(Order = 4)] public List<string> RecentEventIds { get; set; } = new();
    [DataMember(Order = 5)] public bool HistoricalUnavailable { get; set; }
    [DataMember(Order = 6)] public bool WasRepairedFromInvalidState { get; set; }
    [DataMember(Order = 7)] public bool CashTerminalDispositionAmbiguous { get; set; }
    [DataMember(Order = 8)] public bool CashTerminalDispositionRecorded { get; set; }
    // Legacy schema-9 candidate marker. It is migrated to
    // LegacyIdentitySaturationIncomplete at the post-recovery compaction boundary.
    [DataMember(Order = 9)] public bool DeduplicationSaturated { get; set; }
    [DataMember(Order = 10)] public bool MoneyArithmeticSaturated { get; set; }
    [DataMember(Order = 11)] public bool CashArithmeticSaturated { get; set; }
    [DataMember(Order = 12, EmitDefaultValue = false)] public EconomyReplayCursor? ReplayCursor { get; set; } = new();
    [DataMember(Order = 13)] public bool LegacyIdentitySaturationIncomplete { get; set; }
}

public static class EconomyStatisticsReducer
{
    private const int LegacyMaximumRecentEventIds = 2048;

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
                && WouldOverflow(aggregate.CashRaidOutcomes.Acquired, value.Amount)))
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
                aggregate.CashRaidOutcomes.Acquired = SaturatingAdd(aggregate.CashRaidOutcomes.Acquired, value.Amount);
            }
            else if (value.Direction == CurrencyFlowDirection.Outflow && aggregate.CashRaidOutcomes.Acquired > 0)
            {
                aggregate.CashTerminalDispositionAmbiguous = true;
            }
        }

        return true;
    }

    public static void FinalizeCashRaidOutcome(EconomyStatisticsAggregate aggregate, RunOutcome outcome)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        NormalizePersisted(aggregate);
        if (aggregate.CashTerminalDispositionRecorded) return;
        var acquired = aggregate.CashRaidOutcomes.Acquired;
        if (acquired > 0)
        {
            if (outcome == RunOutcome.Interrupted || aggregate.CashTerminalDispositionAmbiguous
                || aggregate.Capabilities.CashTerminalOutcomes.State != AdapterCapabilityState.Supported)
                aggregate.CashRaidOutcomes.Unresolved = SaturatingAdd(aggregate.CashRaidOutcomes.Unresolved, acquired);
            else if (outcome == RunOutcome.Extracted)
                aggregate.CashRaidOutcomes.Secured = SaturatingAdd(aggregate.CashRaidOutcomes.Secured, acquired);
            else if (outcome == RunOutcome.Died)
                aggregate.CashRaidOutcomes.Lost = SaturatingAdd(aggregate.CashRaidOutcomes.Lost, acquired);
        }
        aggregate.CashTerminalDispositionRecorded = true;
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
                           || CashOutcomeMergeWouldOverflow(target.CashRaidOutcomes, source.CashRaidOutcomes);
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
            target.CashRaidOutcomes.Acquired = SaturatingAdd(target.CashRaidOutcomes.Acquired, source.CashRaidOutcomes.Acquired);
            target.CashRaidOutcomes.Secured = SaturatingAdd(target.CashRaidOutcomes.Secured, source.CashRaidOutcomes.Secured);
            target.CashRaidOutcomes.Lost = SaturatingAdd(target.CashRaidOutcomes.Lost, source.CashRaidOutcomes.Lost);
            target.CashRaidOutcomes.Unresolved = SaturatingAdd(target.CashRaidOutcomes.Unresolved, source.CashRaidOutcomes.Unresolved);
        }
        target.HistoricalUnavailable |= source.HistoricalUnavailable;
        target.WasRepairedFromInvalidState |= source.WasRepairedFromInvalidState;
        target.CashTerminalDispositionAmbiguous |= source.CashTerminalDispositionAmbiguous;
        target.CashTerminalDispositionRecorded |= source.CashTerminalDispositionRecorded;
        target.LegacyIdentitySaturationIncomplete |= source.LegacyIdentitySaturationIncomplete
                                                     || source.DeduplicationSaturated;
        target.MoneyArithmeticSaturated |= source.MoneyArithmeticSaturated;
        target.CashArithmeticSaturated |= source.CashArithmeticSaturated;
        target.Capabilities = targetWasUninitialized && !target.HistoricalUnavailable
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
            CashRaidOutcomes = new CashRaidOutcomeAggregate
            {
                Acquired = source.CashRaidOutcomes.Acquired,
                Secured = source.CashRaidOutcomes.Secured,
                Lost = source.CashRaidOutcomes.Lost,
                Unresolved = source.CashRaidOutcomes.Unresolved
            },
            Capabilities = CloneCapabilities(source.Capabilities),
            RecentEventIds = source.RecentEventIds.ToList(),
            HistoricalUnavailable = source.HistoricalUnavailable,
            WasRepairedFromInvalidState = source.WasRepairedFromInvalidState,
            CashTerminalDispositionAmbiguous = source.CashTerminalDispositionAmbiguous,
            CashTerminalDispositionRecorded = source.CashTerminalDispositionRecorded,
            DeduplicationSaturated = source.DeduplicationSaturated,
            MoneyArithmeticSaturated = source.MoneyArithmeticSaturated,
            CashArithmeticSaturated = source.CashArithmeticSaturated,
            ReplayCursor = CloneReplayCursor(source.ReplayCursor),
            LegacyIdentitySaturationIncomplete = source.LegacyIdentitySaturationIncomplete
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
            HistoricalUnavailable = total.HistoricalUnavailable,
            WasRepairedFromInvalidState = total.WasRepairedFromInvalidState,
            CashTerminalDispositionAmbiguous = total.CashTerminalDispositionAmbiguous,
            CashTerminalDispositionRecorded = total.CashTerminalDispositionRecorded,
            MoneyArithmeticSaturated = total.MoneyArithmeticSaturated,
            CashArithmeticSaturated = total.CashArithmeticSaturated,
            LegacyIdentitySaturationIncomplete = total.LegacyIdentitySaturationIncomplete
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
        if (!TrySubtract(total.CashRaidOutcomes.Acquired, baseline.CashRaidOutcomes.Acquired, out var acquired)
            || !TrySubtract(total.CashRaidOutcomes.Secured, baseline.CashRaidOutcomes.Secured, out var secured)
            || !TrySubtract(total.CashRaidOutcomes.Lost, baseline.CashRaidOutcomes.Lost, out var lost)
            || !TrySubtract(total.CashRaidOutcomes.Unresolved, baseline.CashRaidOutcomes.Unresolved, out var unresolved))
            return false;
        difference.CashRaidOutcomes = new CashRaidOutcomeAggregate
        { Acquired = acquired, Secured = secured, Lost = lost, Unresolved = unresolved };
        return true;
    }

    public static bool IsEmpty(EconomyStatisticsAggregate value)
    {
        if (value == null) return true;
        if (value.Currencies == null || value.CashRaidOutcomes == null) return false;
        return value.Currencies.Values.All(row => row.Totals.GrossInflow == 0 && row.Totals.GrossOutflow == 0)
               && value.CashRaidOutcomes.Acquired == 0 && value.CashRaidOutcomes.Secured == 0
               && value.CashRaidOutcomes.Lost == 0 && value.CashRaidOutcomes.Unresolved == 0;
    }

    public static bool HasExactSupportedCurrency(EconomyStatisticsAggregate aggregate, CurrencyKind currency)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (aggregate.HistoricalUnavailable) return false;
        return currency switch
        {
            CurrencyKind.Money => !aggregate.MoneyArithmeticSaturated
                                  && aggregate.Capabilities.MoneyAmountDirection.State == AdapterCapabilityState.Supported,
            CurrencyKind.Cash => !aggregate.CashArithmeticSaturated
                                 && aggregate.Capabilities.CashAmountDirection.State == AdapterCapabilityState.Supported,
            _ => false
        };
    }

    public static bool HasExactCapturedCurrency(EconomyStatisticsAggregate aggregate, CurrencyKind currency)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        var saturated = currency switch
        {
            CurrencyKind.Money => aggregate.MoneyArithmeticSaturated,
            CurrencyKind.Cash => aggregate.CashArithmeticSaturated,
            _ => true
        };
        return !saturated && aggregate.Currencies.ContainsKey(currency.ToString());
    }

    public static bool IsExactCurrencyComposition(
        EconomyStatisticsAggregate total,
        IEnumerable<EconomyStatisticsAggregate> components,
        CurrencyKind currency)
    {
        if (total == null) throw new ArgumentNullException(nameof(total));
        if (components == null) throw new ArgumentNullException(nameof(components));
        var supportedComposition = HasExactSupportedCurrency(total, currency);
        var historicalCapturedComposition = total.HistoricalUnavailable
                                            && HasExactCapturedCurrency(total, currency);
        if (!supportedComposition && !historicalCapturedComposition) return true;

        var expected = new CurrencyEconomyAggregate { Currency = currency };
        foreach (var component in components)
        {
            if (component == null) return false;
            if (supportedComposition && !HasExactSupportedCurrency(component, currency)) return false;
            if (historicalCapturedComposition
                && !HasExactCapturedCurrency(component, currency))
            {
                if (!component.Currencies.ContainsKey(currency.ToString())
                    && (component.HistoricalUnavailable
                        || HasExactSupportedCurrency(component, currency)))
                    continue;
                return false;
            }
            if (!component.Currencies.TryGetValue(currency.ToString(), out var row)) continue;
            if (!TryMergeExact(expected, row)) return false;
        }

        total.Currencies.TryGetValue(currency.ToString(), out var actual);
        return CurrencyRowsEqual(actual, expected);
    }

    public static void MergeTerminalOutcomes(EconomyStatisticsAggregate target, EconomyStatisticsAggregate run)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (run == null) throw new ArgumentNullException(nameof(run));
        NormalizePersisted(target);
        NormalizePersisted(run);
        if (target.CashArithmeticSaturated)
        {
            target.CashTerminalDispositionAmbiguous = true;
            target.CashTerminalDispositionRecorded |= run.CashTerminalDispositionRecorded;
            return;
        }
        var arithmeticSaturated = WouldOverflow(target.CashRaidOutcomes.Secured, run.CashRaidOutcomes.Secured)
                                  || WouldOverflow(target.CashRaidOutcomes.Lost, run.CashRaidOutcomes.Lost)
                                  || WouldOverflow(target.CashRaidOutcomes.Unresolved, run.CashRaidOutcomes.Unresolved);
        if (arithmeticSaturated)
        {
            target.CashTerminalDispositionAmbiguous = true;
            target.CashTerminalDispositionRecorded |= run.CashTerminalDispositionRecorded;
            ApplyArithmeticSaturation(target, CurrencyKind.Cash);
            return;
        }
        target.CashRaidOutcomes.Secured = SaturatingAdd(target.CashRaidOutcomes.Secured, run.CashRaidOutcomes.Secured);
        target.CashRaidOutcomes.Lost = SaturatingAdd(target.CashRaidOutcomes.Lost, run.CashRaidOutcomes.Lost);
        target.CashRaidOutcomes.Unresolved = SaturatingAdd(target.CashRaidOutcomes.Unresolved, run.CashRaidOutcomes.Unresolved);
        target.CashTerminalDispositionAmbiguous |= run.CashTerminalDispositionAmbiguous;
        target.CashTerminalDispositionRecorded |= run.CashTerminalDispositionRecorded;
        if (run.CashArithmeticSaturated)
            ApplyArithmeticSaturation(target, CurrencyKind.Cash);
    }

    public static bool NormalizePersisted(EconomyStatisticsAggregate aggregate)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        var repaired = false;
        aggregate.Currencies ??= Repair(new Dictionary<string, CurrencyEconomyAggregate>(StringComparer.Ordinal), ref repaired);
        aggregate.CashRaidOutcomes ??= Repair(new CashRaidOutcomeAggregate(), ref repaired);
        aggregate.Capabilities ??= Repair(new EconomyMetricCapabilities(), ref repaired);
        aggregate.RecentEventIds ??= Repair(new List<string>(), ref repaired);
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
        var deduped = aggregate.RecentEventIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Take(LegacyMaximumRecentEventIds).ToList();
        if (deduped.Count != aggregate.RecentEventIds.Count) repaired = true;
        aggregate.RecentEventIds = deduped;
        if (aggregate.RecentEventIds.Count == LegacyMaximumRecentEventIds && !aggregate.DeduplicationSaturated)
        {
            aggregate.DeduplicationSaturated = true;
            repaired = true;
        }
        if (aggregate.DeduplicationSaturated)
        {
            if (!aggregate.LegacyIdentitySaturationIncomplete) repaired = true;
            aggregate.LegacyIdentitySaturationIncomplete = true;
        }
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
        aggregate.CashRaidOutcomes.Acquired = NonNegative(aggregate.CashRaidOutcomes.Acquired, ref repaired);
        aggregate.CashRaidOutcomes.Secured = NonNegative(aggregate.CashRaidOutcomes.Secured, ref repaired);
        aggregate.CashRaidOutcomes.Lost = NonNegative(aggregate.CashRaidOutcomes.Lost, ref repaired);
        aggregate.CashRaidOutcomes.Unresolved = NonNegative(aggregate.CashRaidOutcomes.Unresolved, ref repaired);
        if (!TrySumExactly(
                new[]
                {
                    aggregate.CashRaidOutcomes.Secured,
                    aggregate.CashRaidOutcomes.Lost,
                    aggregate.CashRaidOutcomes.Unresolved
                },
                out var resolvedCash)
            || resolvedCash > aggregate.CashRaidOutcomes.Acquired)
        {
            aggregate.CashRaidOutcomes.Secured = 0;
            aggregate.CashRaidOutcomes.Lost = 0;
            aggregate.CashRaidOutcomes.Unresolved = aggregate.CashRaidOutcomes.Acquired;
            aggregate.CashTerminalDispositionAmbiguous = true;
            repaired = true;
        }
        aggregate.WasRepairedFromInvalidState |= repaired;
        return repaired;
    }

    public static void Validate(EconomyStatisticsAggregate aggregate)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (aggregate.Currencies == null || aggregate.CashRaidOutcomes == null || aggregate.Capabilities == null
            || aggregate.RecentEventIds == null || aggregate.ReplayCursor == null)
            throw new ArgumentException("Economy roots are missing.", nameof(aggregate));
        if (aggregate.RecentEventIds.Count > LegacyMaximumRecentEventIds || aggregate.RecentEventIds.Any(string.IsNullOrWhiteSpace)
            || aggregate.RecentEventIds.Distinct(StringComparer.Ordinal).Count() != aggregate.RecentEventIds.Count)
            throw new ArgumentException("Legacy economy identity evidence is invalid.", nameof(aggregate));
        if (aggregate.RecentEventIds.Count == LegacyMaximumRecentEventIds && !aggregate.DeduplicationSaturated)
            throw new ArgumentException("Legacy economy identity saturation state is invalid.", nameof(aggregate));
        ValidateReplayCursor(aggregate.ReplayCursor);
        foreach (var entry in aggregate.Currencies)
        {
            if (entry.Value == null || !Enum.IsDefined(typeof(CurrencyKind), entry.Value.Currency) || entry.Key != entry.Value.Currency.ToString())
                throw new ArgumentException("Economy currency identity is invalid.", nameof(aggregate));
            ValidateCurrency(entry.Value);
        }
        ValidateOutcome(aggregate.CashRaidOutcomes);
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
        if (aggregate.Currencies == null || aggregate.CashRaidOutcomes == null
            || aggregate.Capabilities == null || aggregate.RecentEventIds == null)
            throw new ArgumentException("Economy roots are missing.", nameof(aggregate));
        if (aggregate.RecentEventIds.Count > LegacyMaximumRecentEventIds
            || aggregate.RecentEventIds.Any(string.IsNullOrWhiteSpace)
            || aggregate.RecentEventIds.Distinct(StringComparer.Ordinal).Count() != aggregate.RecentEventIds.Count)
            throw new ArgumentException("Legacy economy identity evidence is unsafe.", nameof(aggregate));
        if (aggregate.RecentEventIds.Count == LegacyMaximumRecentEventIds && !aggregate.DeduplicationSaturated)
            throw new ArgumentException("Legacy economy identity saturation evidence is inconsistent.", nameof(aggregate));
        if (aggregate.ReplayCursor != null) ValidateReplayCursor(aggregate.ReplayCursor);
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
        ValidateOutcome(aggregate.CashRaidOutcomes);
    }

    public static EconomyMetricCapabilities CloneCapabilities(EconomyMetricCapabilities source) => new()
    {
        MoneyAmountDirection = Clone(source.MoneyAmountDirection),
        MoneySourceAttribution = Clone(source.MoneySourceAttribution),
        MoneyContextAttribution = Clone(source.MoneyContextAttribution),
        CashAmountDirection = Clone(source.CashAmountDirection),
        CashExternalAcquisition = Clone(source.CashExternalAcquisition),
        CashContextAttribution = Clone(source.CashContextAttribution),
        CashTerminalOutcomes = Clone(source.CashTerminalOutcomes),
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
        aggregate.Capabilities = aggregateWasUninitialized && !aggregate.HistoricalUnavailable
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

    public static bool CompactLegacyReplayEvidence(
        EconomyStatisticsAggregate aggregate,
        bool clearReplayCursor)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        NormalizePersisted(aggregate);
        var changed = aggregate.RecentEventIds.Count > 0 || aggregate.DeduplicationSaturated;
        if (aggregate.DeduplicationSaturated)
            aggregate.LegacyIdentitySaturationIncomplete = true;
        aggregate.RecentEventIds.Clear();
        aggregate.DeduplicationSaturated = false;
        if (clearReplayCursor
            && (!string.IsNullOrEmpty(aggregate.ReplayCursor!.ActivationId)
                || aggregate.ReplayCursor.ClosedThroughSequence != 0))
        {
            aggregate.ReplayCursor = new EconomyReplayCursor();
            changed = true;
        }
        return changed;
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
        aggregate.Capabilities.CashTerminalOutcomes = RestrictForSaturation(aggregate.Capabilities.CashTerminalOutcomes, reason);
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

    private static bool CashOutcomeMergeWouldOverflow(CashRaidOutcomeAggregate target, CashRaidOutcomeAggregate source) =>
        WouldOverflow(target.Acquired, source.Acquired)
        || WouldOverflow(target.Secured, source.Secured)
        || WouldOverflow(target.Lost, source.Lost)
        || WouldOverflow(target.Unresolved, source.Unresolved);

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
        CashTerminalOutcomes = Restrict(a.CashTerminalOutcomes, b.CashTerminalOutcomes),
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
            CashTerminalOutcomes = RestrictLifetime(recorded.CashTerminalOutcomes, current.CashTerminalOutcomes),
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
            || value.HistoricalUnavailable
            || value.WasRepairedFromInvalidState
            || value.CashTerminalDispositionAmbiguous
            || value.CashTerminalDispositionRecorded
            || value.DeduplicationSaturated
            || value.MoneyArithmeticSaturated
            || value.CashArithmeticSaturated
            || value.LegacyIdentitySaturationIncomplete
            || value.RecentEventIds == null
            || value.RecentEventIds.Count != 0
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
        value.CashTerminalOutcomes ??= Repair(new MetricAvailability(), ref repaired); value.RouteAttribution ??= Repair(new MetricAvailability(), ref repaired);
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
    private static EconomyReplayCursor? CloneReplayCursor(EconomyReplayCursor? source) => source == null
        ? null
        : new EconomyReplayCursor
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
        yield return value.CashTerminalOutcomes;
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
        yield return value.CashTerminalOutcomes;
    }

    private static void ValidateCapabilities(EconomyMetricCapabilities value)
    {
        if (Capabilities(value).Any(availability =>
                availability == null || !Enum.IsDefined(typeof(AdapterCapabilityState), availability.State)))
            throw new ArgumentException("Economy capabilities are incomplete.", nameof(value));
    }
    private static void ValidateOutcome(CashRaidOutcomeAggregate value)
    {
        if (value.Acquired < 0 || value.Secured < 0 || value.Lost < 0 || value.Unresolved < 0
            || !TrySumExactly(new[] { value.Secured, value.Lost, value.Unresolved }, out var resolved)
            || resolved > value.Acquired)
            throw new ArgumentException("Cash raid outcomes are invalid.", nameof(value));
    }
    private static T Repair<T>(T value, ref bool repaired) { repaired = true; return value; }
    private static long SaturatingAdd(long left, long right)
    {
        if (left < 0 || right < 0) throw new ArgumentOutOfRangeException(nameof(left));
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }
}
