using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.UI;

// Retain the complete publication boundary, including the detached holdings observations.
internal sealed class EconomyProjectionBinding
{
    private readonly ProfileDocument profile;
    private readonly EconomyStatisticsAggregate economy;
    private readonly EconomyHoldingsProjection holdings;
    private readonly EconomyMetricCapabilities capabilities;
    private readonly IReadOnlyList<RunSummary> recent;
    private readonly RunStatisticsViewModel runs;
    private readonly object currencies, money, cash, liquid, runRows;
    private readonly string generation;
    public EconomyProjectionBinding(StatisticsPanelProjection p)
    {
        profile = p.Profile; economy = p.Economy; holdings = p.Holdings; capabilities = p.CurrentEconomyCapabilities;
        recent = p.RecentEconomyRuns; runs = p.Runs; generation = profile.GenerationId;
        currencies = economy.Currencies; money = holdings.Money; cash = holdings.Cash; liquid = holdings.LiquidWealth; runRows = runs.Runs;
    }
    public bool Matches(StatisticsPanelProjection p, string current) => generation == current
        && StatisticsPanelProjectionFactory.HasProvableGeneration(profile, current)
        && ReferenceEquals(profile, p.Profile) && ReferenceEquals(economy, p.Economy)
        && ReferenceEquals(economy, profile.Statistics.Economy) && ReferenceEquals(currencies, economy.Currencies)
        && ReferenceEquals(holdings, p.Holdings) && ReferenceEquals(money, holdings.Money)
        && ReferenceEquals(cash, holdings.Cash) && ReferenceEquals(liquid, holdings.LiquidWealth)
        && ReferenceEquals(capabilities, p.CurrentEconomyCapabilities) && ReferenceEquals(recent, p.RecentEconomyRuns)
        && ReferenceEquals(runs, p.Runs) && ReferenceEquals(runRows, runs.Runs);
}

internal sealed class EconomyHolding
{
    public string Name { get; }
    public long? Value { get; }
    public EconomyHoldingObservationState State { get; }
    public string Caption { get; }
    public EconomyHolding(string name, long? value, EconomyHoldingObservationState state, string caption)
    { Name = name; Value = value; State = state; Caption = caption; }
}

internal sealed class EconomyFlowRow
{
    public string Id { get; }
    public string Name { get; }
    public long? Inflow { get; }
    public long? Outflow { get; }
    public long? Net { get; }
    public EconomyFlowRow(string id, string name, long? inflow, long? outflow, long? net)
    { Id = id; Name = name; Inflow = inflow; Outflow = outflow; Net = net; }
}

internal sealed class EconomyFlow
{
    public CurrencyKind Currency { get; }
    public EconomyFlowRow Totals { get; }
    public string Notice { get; }
    public IReadOnlyList<EconomyFlowRow> Sources { get; }
    public string SourceNotice { get; }
    public IReadOnlyList<EconomyFlowRow> Contexts { get; }
    public string ContextNotice { get; }
    public long? ProvenRaidAcquired { get; }
    public string AcquisitionNotice { get; }
    public EconomyFlow(CurrencyKind currency, EconomyFlowRow totals, string notice, IEnumerable<EconomyFlowRow> sources,
        string sourceNotice, IEnumerable<EconomyFlowRow> contexts, string contextNotice, long? acquired, string acquisitionNotice)
    {
        Currency = currency; Totals = totals; Notice = notice; Sources = Array.AsReadOnly(sources.ToArray()); SourceNotice = sourceNotice;
        Contexts = Array.AsReadOnly(contexts.ToArray()); ContextNotice = contextNotice; ProvenRaidAcquired = acquired; AcquisitionNotice = acquisitionNotice;
    }
}

internal sealed class EconomyRun
{
    public string RunId { get; }
    public string Title { get; }
    public string Metadata { get; }
    public RetainedRunBadgeState Outcome { get; }
    public EconomyFlow Money { get; }
    public EconomyFlow Cash { get; }
    public EconomyRun(string id, string title, string metadata, RetainedRunBadgeState outcome, EconomyFlow money, EconomyFlow cash)
    { RunId = id; Title = title; Metadata = metadata; Outcome = outcome; Money = money; Cash = cash; }
}

internal sealed class EconomyPresentation
{
    public string GenerationId { get; }
    public IReadOnlyList<EconomyHolding> Holdings { get; }
    public EconomyFlow Money { get; }
    public EconomyFlow Cash { get; }
    public IReadOnlyList<EconomyRun> RecentRuns { get; }
    public EconomyPresentation(string generation, IEnumerable<EconomyHolding> holdings, EconomyFlow money, EconomyFlow cash, IEnumerable<EconomyRun> recent)
    { GenerationId = generation; Holdings = Array.AsReadOnly(holdings.ToArray()); Money = money; Cash = cash; RecentRuns = Array.AsReadOnly(recent.ToArray()); }
    public bool CanRoute(string generation, string id) => generation == GenerationId && RecentRuns.Any(run => run.RunId == id);
}

internal static class EconomyPresentationFactory
{
    public static EconomyPresentation? Create(StatisticsPanelProjection p, string generation, Func<string, string>? text = null)
    {
        if (p == null || string.IsNullOrWhiteSpace(generation) || p.EconomyBinding?.Matches(p, generation) != true
            || p.Runs.Runs.Any(r => r.SaveGenerationId != generation || string.IsNullOrWhiteSpace(r.RunId))
            || p.Runs.Runs.Select(r => r.RunId).Distinct(StringComparer.Ordinal).Count() != p.Runs.Runs.Count) return null;
        var runs = new HashSet<RunSummary>(p.Runs.Runs);
        if (p.RecentEconomyRuns.Any(r => !runs.Contains(r))) return null;
        var t = text ?? UiText.Get;
        var holdings = p.Holdings;
        // The reducer owns comparability and checked addition. Do not recalculate wealth from flows.
        var values = new[] {
            Holding(holdings.LiquidWealth, holdings.Capabilities.LiquidWealth, t("ui.liquid_wealth"), "ui.economy_liquid_caption", "ui.economy_liquid_unavailable"),
            Holding(holdings.Money, holdings.Capabilities.Money, t("ui.money_holding"), "ui.economy_money_caption", "ui.unavailable"),
            Holding(holdings.Cash, holdings.Capabilities.Cash, t("ui.economy_cash"), "ui.economy_cash_caption", "ui.unavailable") };
        // Reconfirm independent current components even if a consumer replaced a capability after publication.
        if (values[1].State != EconomyHoldingObservationState.Current || values[2].State != EconomyHoldingObservationState.Current)
            values[0] = new EconomyHolding(t("ui.liquid_wealth"), null, EconomyHoldingObservationState.Unavailable, t("ui.economy_liquid_unavailable"));
        var order = p.Runs.Runs.OrderByDescending(r => r.StartedUtc).ThenBy(r => r.RunId, StringComparer.Ordinal)
            .Select((r, index) => (r.RunId, Number: p.Runs.Runs.Count - index)).ToDictionary(r => r.RunId, r => r.Number, StringComparer.Ordinal);
        var recent = p.RecentEconomyRuns.OrderByDescending(r => r.EndedUtc).ThenBy(r => r.RunId, StringComparer.Ordinal).Select(r =>
        {
            var route = r.Segments.OrderBy(s => s.SegmentIndex).ToArray();
            string Map(bool known, string name) => known && !string.IsNullOrWhiteSpace(name) ? name : t("ui.overview_latest_run_unknown_map");
            var title = route.Length == 0 ? Map(r.StartingMapKnown || r.MapKnown, r.StartingMapKnown ? r.StartingMapDisplayName : r.MapDisplayName)
                : Map(route[0].MapKnown, route[0].MapDisplayName);
            if (route.Length > 1 && route[0].MapId != route[route.Length - 1].MapId)
                title += " - " + Map(route[route.Length - 1].MapKnown, route[route.Length - 1].MapDisplayName);
            var exactMaps = !r.HistoricalRouteUnavailable && !r.RouteWasRepairedFromInvalidState && route.Length > 0
                && r.RouteCapabilities.OrderedRoute.State == AdapterCapabilityState.Supported
                && r.RouteCapabilities.Segments.State == AdapterCapabilityState.Supported && route.All(s => s.MapKnown);
            var count = route.Select(s => s.MapId).Distinct(StringComparer.Ordinal).Count();
            var mapText = exactMaps ? count.ToString(CultureInfo.InvariantCulture) + " " + t(count == 1 ? "ui.runs_map" : "ui.runs_maps") : t("ui.unavailable");
            var metadata = string.Format(CultureInfo.CurrentCulture, t("ui.economy_run_metadata"), order[r.RunId], Timestamp(r.StartedUtc, t, runDate: true), mapText);
            return new EconomyRun(r.RunId, title, metadata, RetainedRunBadgePresentationFactory.MapOutcome(r.Outcome),
                Flow(r.Economy, CurrencyKind.Money, p.CurrentEconomyCapabilities, t), Flow(r.Economy, CurrencyKind.Cash, p.CurrentEconomyCapabilities, t));
        });
        return new EconomyPresentation(generation, values, Flow(p.Economy, CurrencyKind.Money, p.CurrentEconomyCapabilities, t),
            Flow(p.Economy, CurrencyKind.Cash, p.CurrentEconomyCapabilities, t), recent);

        EconomyHolding Holding(EconomyHoldingObservation o, MetricAvailability capability, string name, string caption, string unavailable)
        {
            if (o.State == EconomyHoldingObservationState.Unavailable || !o.Value.HasValue || o.Value < 0
                || !o.ObservedUtc.HasValue || o.ObservedUtc == default(DateTime) || o.SaveGenerationId != generation
                || o.State == EconomyHoldingObservationState.Current && capability.State != AdapterCapabilityState.Supported)
                return new EconomyHolding(name, null, EconomyHoldingObservationState.Unavailable, t(unavailable));
            return new EconomyHolding(name, o.Value, o.State, o.State == EconomyHoldingObservationState.Current ? t(caption)
                : t("ui.last_observed") + ": " + Timestamp(o.ObservedUtc.Value, t));
        }
    }

    internal static EconomyFlow Flow(EconomyStatisticsAggregate a, CurrencyKind kind, EconomyMetricCapabilities current, Func<string, string>? text = null)
    {
        var t = text ?? UiText.Get; var money = kind == CurrencyKind.Money;
        var amount = money ? a.Capabilities.MoneyAmountDirection : a.Capabilities.CashAmountDirection;
        var nowAmount = money ? current.MoneyAmountDirection : current.CashAmountDirection;
        var source = money ? a.Capabilities.MoneySourceAttribution : a.Capabilities.CashExternalAcquisition;
        var nowSource = money ? current.MoneySourceAttribution : current.CashExternalAcquisition;
        var context = money ? a.Capabilities.MoneyContextAttribution : a.Capabilities.CashContextAttribution;
        var nowContext = money ? current.MoneyContextAttribution : current.CashContextAttribution;
        a.Currencies.TryGetValue(kind.ToString(), out var row);
        var broken = a.WasRepairedFromInvalidState || (money ? a.MoneyArithmeticSaturated : a.CashArithmeticSaturated);
        var knownZero = row == null && !a.HistoricalUnavailable && !broken
            && amount.State == AdapterCapabilityState.Supported && nowAmount.State == AdapterCapabilityState.Supported;
        var hasEvidence = row != null || knownZero;
        var totals = new EconomyFlowRow(kind.ToString(), "", hasEvidence ? row?.Totals.GrossInflow ?? 0 : null,
            hasEvidence ? row?.Totals.GrossOutflow ?? 0 : null, hasEvidence ? row?.Totals.NetFlow ?? 0 : null);
        var sources = (row?.Sources ?? new Dictionary<string, CurrencyFlowTotals>()).OrderBy(r => SourceOrder(r.Key)).ThenBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => Present(r.Key, SourceName(r.Key, t), r.Value)).ToArray();
        var contexts = (row?.Contexts ?? new Dictionary<string, CurrencyFlowTotals>()).OrderBy(r => ContextOrder(r.Key)).ThenBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => Present(r.Key, ContextName(r.Key, t), r.Value)).ToList();
        long? acquired = null;
        if (!money)
        {
            if (a.CashRaidOutcomes.Acquired > 0) acquired = a.CashRaidOutcomes.Acquired;
            else if (!a.HistoricalUnavailable && !broken && a.Capabilities.CashExternalAcquisition.State == AdapterCapabilityState.Supported
                && current.CashExternalAcquisition.State == AdapterCapabilityState.Supported) acquired = 0;
            // Acquired evidence is a subset of Raid inflow. Missing context remains missing.
            if (acquired > 0 && !contexts.Any(r => r.Id == GameplayContext.Raid.ToString()))
                contexts.Add(new EconomyFlowRow(GameplayContext.Raid.ToString(), t("ui.economy_context_raid"), null, null, null));
        }
        return new EconomyFlow(kind, totals, Notice(amount, nowAmount, broken), sources,
            sources.Length == 0 && (!hasEvidence || source.State != AdapterCapabilityState.Supported || nowSource.State != AdapterCapabilityState.Supported)
                ? t("ui.unavailable") : Notice(source, nowSource, broken), contexts,
            contexts.Count == 0 && (!hasEvidence || context.State != AdapterCapabilityState.Supported || nowContext.State != AdapterCapabilityState.Supported)
                ? t("ui.unavailable") : Notice(context, nowContext, broken), acquired,
            money ? "" : Notice(a.Capabilities.CashExternalAcquisition, current.CashExternalAcquisition, broken));

        string Notice(MetricAvailability scope, MetricAvailability now, bool incomplete) => now.State == AdapterCapabilityState.Experimental
            ? t("ui.economy_current_limited") : now.State != AdapterCapabilityState.Supported
            ? t("ui.economy_current_unavailable") : incomplete || scope.State != AdapterCapabilityState.Supported ? t("ui.economy_incomplete") : "";
        static EconomyFlowRow Present(string id, string name, CurrencyFlowTotals value) => new(id, name, value.GrossInflow, value.GrossOutflow, value.NetFlow);
    }
    private static int SourceOrder(string key) => key switch {
        nameof(CurrencySourceCategory.Sale) => 0, nameof(CurrencySourceCategory.Reward) => 1, nameof(CurrencySourceCategory.Purchase) => 2,
        nameof(CurrencySourceCategory.FeeOrCraftingCost) => 3, nameof(CurrencySourceCategory.LootOrPickup) => 4, _ => 5 };
    private static int ContextOrder(string key) => key switch {
        nameof(GameplayContext.Base) => 0, nameof(GameplayContext.Raid) => 1, nameof(GameplayContext.Shop) => 2,
        nameof(GameplayContext.Reward) => 3, nameof(GameplayContext.Paused) => 4, _ => 5 };
    private static string SourceName(string key, Func<string, string> t) => t(key switch {
        nameof(CurrencySourceCategory.Sale) => "ui.economy_source_sales", nameof(CurrencySourceCategory.Reward) => "ui.economy_source_rewards",
        nameof(CurrencySourceCategory.Purchase) => "ui.economy_source_purchases", nameof(CurrencySourceCategory.FeeOrCraftingCost) => "ui.economy_source_fees",
        nameof(CurrencySourceCategory.LootOrPickup) => "ui.economy_source_loot", _ => "ui.economy_source_unknown" });
    private static string ContextName(string key, Func<string, string> t) => t(key switch {
        nameof(GameplayContext.Base) => "ui.economy_context_base", nameof(GameplayContext.Raid) => "ui.economy_context_raid",
        nameof(GameplayContext.Shop) => "ui.economy_context_shop", nameof(GameplayContext.Reward) => "ui.economy_context_reward",
        nameof(GameplayContext.Paused) => "ui.economy_context_paused", _ => "ui.economy_context_unknown" });
    internal static string Number(long? value, bool signed = false) => !value.HasValue ? UiText.Get("ui.unavailable")
        : (signed && value > 0 ? "+" : "") + value.Value.ToString("#,0", CultureInfo.InvariantCulture);
    internal static string Timestamp(DateTime value, Func<string, string>? text = null, bool runDate = false)
    {
        if (value == default) return (text ?? UiText.Get)("ui.unavailable");
        try { return DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime().ToString(runDate ? RunDateStyle.Format : "yyyy-MM-dd - HH:mm:ss", CultureInfo.InvariantCulture); }
        catch (ArgumentException) { return (text ?? UiText.Get)("ui.unavailable"); }
    }
}
