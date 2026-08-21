using System.Globalization;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Export;

[DataContract]
public sealed class StatisticsExportDocument
{
    [DataMember(Order = 1)]
    public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;

    [DataMember(Order = 2)]
    public DateTime ExportedUtc { get; set; }

    [DataMember(Order = 3)]
    public string GenerationId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int Slot { get; set; }

    [DataMember(Order = 5)]
    public long Revision { get; set; }

    [DataMember(Order = 6)]
    public AggregateTotals Overall { get; set; } = new();

    [DataMember(Order = 7)]
    public List<GroupExportRow> Groups { get; set; } = new();

    [DataMember(Order = 8)]
    public List<ItemExportRow> Items { get; set; } = new();

    [DataMember(Order = 9)]
    public RunAggregateTotals RunTotals { get; set; } = new();

    [DataMember(Order = 10)]
    public List<RunSummary> Runs { get; set; } = new();

    [DataMember(Order = 11)]
    public RunDurationRecords RunRecords { get; set; } = new();

    [DataMember(Order = 12)]
    public List<CapabilityRecord> Capabilities { get; set; } = new();

    [DataMember(Order = 13)]
    public EconomyStatisticsAggregate Economy { get; set; } = new();

    [DataMember(Order = 14)]
    public WorldTimeStatisticsAggregate WorldTime { get; set; } = new();
}

[DataContract]
public sealed class GroupExportRow
{
    [DataMember(Order = 1)]
    public string Group { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public AggregateTotals Totals { get; set; } = new();
}

[DataContract]
public sealed class ItemExportRow
{
    [DataMember(Order = 1)]
    public string ItemId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Group { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<string> EffectTags { get; set; } = new();

    [DataMember(Order = 5)]
    public AggregateTotals Totals { get; set; } = new();
}

public sealed class StatisticsExportBundle
{
    public StatisticsExportBundle(
        StatisticsExportDocument document,
        string json,
        string overviewCsv,
        string groupsCsv,
        string itemsCsv,
        string runsCsv,
        string runTotalsCsv,
        string mapTotalsCsv,
        string recordsCsv,
        string combatTotalsCsv,
        string combatAttributionCsv,
        string weaponTotalsCsv,
        string ammunitionTotalsCsv,
        string equipmentTotalsCsv,
        string recurringLoadoutsCsv,
        string equipmentCombatCsv,
        string containersCsv,
        string routesCsv,
        string segmentsCsv,
        string segmentEventsCsv,
        string routeMapTotalsCsv,
        string economyTotalsCsv,
        string economySourcesCsv,
        string economyContextsCsv,
        string cashRaidOutcomesCsv,
        string worldTimeCsv)
    {
        Document = document;
        Json = json;
        OverviewCsv = overviewCsv;
        GroupsCsv = groupsCsv;
        ItemsCsv = itemsCsv;
        RunsCsv = runsCsv;
        RunTotalsCsv = runTotalsCsv;
        MapTotalsCsv = mapTotalsCsv;
        RecordsCsv = recordsCsv;
        CombatTotalsCsv = combatTotalsCsv;
        CombatAttributionCsv = combatAttributionCsv;
        WeaponTotalsCsv = weaponTotalsCsv;
        AmmunitionTotalsCsv = ammunitionTotalsCsv;
        EquipmentTotalsCsv = equipmentTotalsCsv;
        RecurringLoadoutsCsv = recurringLoadoutsCsv;
        EquipmentCombatCsv = equipmentCombatCsv;
        ContainersCsv = containersCsv;
        RoutesCsv = routesCsv;
        SegmentsCsv = segmentsCsv;
        SegmentEventsCsv = segmentEventsCsv;
        RouteMapTotalsCsv = routeMapTotalsCsv;
        EconomyTotalsCsv = economyTotalsCsv;
        EconomySourcesCsv = economySourcesCsv;
        EconomyContextsCsv = economyContextsCsv;
        CashRaidOutcomesCsv = cashRaidOutcomesCsv;
        WorldTimeCsv = worldTimeCsv;
    }

    public StatisticsExportDocument Document { get; }

    public string Json { get; }

    public string OverviewCsv { get; }

    public string GroupsCsv { get; }

    public string ItemsCsv { get; }

    public string RunsCsv { get; }

    public string RunTotalsCsv { get; }

    public string MapTotalsCsv { get; }

    public string RecordsCsv { get; }

    public string CombatTotalsCsv { get; }

    public string CombatAttributionCsv { get; }

    public string WeaponTotalsCsv { get; }

    public string AmmunitionTotalsCsv { get; }

    public string EquipmentTotalsCsv { get; }

    public string RecurringLoadoutsCsv { get; }

    public string EquipmentCombatCsv { get; }

    public string ContainersCsv { get; }

    public string RoutesCsv { get; }

    public string SegmentsCsv { get; }

    public string SegmentEventsCsv { get; }

    public string RouteMapTotalsCsv { get; }

    public string EconomyTotalsCsv { get; }

    public string EconomySourcesCsv { get; }

    public string EconomyContextsCsv { get; }

    public string CashRaidOutcomesCsv { get; }

    public string WorldTimeCsv { get; }
}

public static class StatisticsExporter
{
    private static readonly string[] AmountUnits =
    {
        "Item",
        "StackUnit",
        "Durability",
        "UnknownAmount"
    };
    private static readonly char[] CsvSpecialCharacters = { ',', '"', '\r', '\n' };

    public static StatisticsExportBundle Create(ProfileDocument profile, DateTime exportedUtc)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        exportedUtc = EnsureUtc(exportedUtc);
        var runTotals = CloneRunTotals(profile.Statistics.RunTotals);
        runTotals.WeaponStatistics.Capabilities = ApplyCurrentWeaponCapabilityStates(
            runTotals.WeaponStatistics,
            profile.Capabilities,
            allowUninitializedFallback: true);
        runTotals.CombatStatistics.Capabilities = ApplyCurrentCombatCapabilityStates(
            runTotals.CombatStatistics, profile.Capabilities, allowUninitializedFallback: true);
        runTotals.EquipmentStatistics.Capabilities = ApplyCurrentEquipmentCapabilityStates(
            runTotals.EquipmentStatistics, profile.Capabilities, allowUninitializedFallback: true);
        ApplyCurrentContainerCapability(runTotals.ContainerStatistics, profile.Capabilities, allowUninitializedFallback: true);
        foreach (var map in runTotals.Maps.Values)
        {
            map.WeaponStatistics.Capabilities = ApplyCurrentWeaponCapabilityStates(
                map.WeaponStatistics,
                profile.Capabilities,
                allowUninitializedFallback: false);
            map.CombatStatistics.Capabilities = ApplyCurrentCombatCapabilityStates(
                map.CombatStatistics, profile.Capabilities, allowUninitializedFallback: false);
            map.EquipmentStatistics.Capabilities = ApplyCurrentEquipmentCapabilityStates(
                map.EquipmentStatistics, profile.Capabilities, allowUninitializedFallback: false);
            ApplyCurrentContainerCapability(map.ContainerStatistics, profile.Capabilities, allowUninitializedFallback: false);
        }
        foreach (var map in runTotals.RouteMaps.Values)
        {
            map.WeaponStatistics.Capabilities = ApplyCurrentWeaponCapabilityStates(
                map.WeaponStatistics, profile.Capabilities, allowUninitializedFallback: false);
            map.CombatStatistics.Capabilities = ApplyCurrentCombatCapabilityStates(
                map.CombatStatistics, profile.Capabilities, allowUninitializedFallback: false);
            map.EquipmentStatistics.Capabilities = ApplyCurrentEquipmentCapabilityStates(
                map.EquipmentStatistics, profile.Capabilities, allowUninitializedFallback: false);
            ApplyCurrentContainerCapability(map.ContainerStatistics, profile.Capabilities, allowUninitializedFallback: false);
        }

        var runs = profile.Statistics.Runs.Select(CloneRun).ToList();
        foreach (var run in runs)
        {
            run.WeaponStatistics.Capabilities = ApplyCurrentWeaponCapabilityStates(
                run.WeaponStatistics,
                profile.Capabilities,
                allowUninitializedFallback: false);
            run.CombatStatistics.Capabilities = ApplyCurrentCombatCapabilityStates(
                run.CombatStatistics, profile.Capabilities, allowUninitializedFallback: false);
            run.EquipmentStatistics.Capabilities = ApplyCurrentEquipmentCapabilityStates(
                run.EquipmentStatistics, profile.Capabilities, allowUninitializedFallback: false);
            ApplyCurrentContainerCapability(run.ContainerStatistics, profile.Capabilities, allowUninitializedFallback: false);
        }

        var document = new StatisticsExportDocument
        {
            ExportedUtc = exportedUtc,
            GenerationId = profile.GenerationId,
            Slot = profile.Slot,
            Revision = profile.Revision,
            Overall = CloneTotals(profile.Statistics.Overall),
            Groups = profile.Statistics.Groups
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new GroupExportRow
                {
                    Group = entry.Key,
                    Totals = CloneTotals(entry.Value)
                })
                .ToList(),
            Items = profile.Statistics.Items.Values
                .OrderBy(item => item.ItemId, StringComparer.Ordinal)
                .Select(item => new ItemExportRow
                {
                    ItemId = item.ItemId,
                    DisplayName = item.DisplayName,
                    Group = item.Group.ToString(),
                    EffectTags = item.EffectTags.Select(tag => tag.ToString()).OrderBy(tag => tag, StringComparer.Ordinal).ToList(),
                    Totals = CloneTotals(item.Totals)
                })
                .ToList(),
            RunTotals = runTotals,
            Runs = runs,
            RunRecords = CloneRunRecords(profile.Statistics.RunRecords),
            Capabilities = profile.Capabilities.Select(CloneCapability).ToList(),
            Economy = EconomyStatisticsReducer.Clone(profile.Statistics.Economy),
            WorldTime = WorldTimeStatisticsReducer.Clone(profile.Statistics.WorldTime)
        };

        return new StatisticsExportBundle(
            document,
            SerializeJson(document),
            CreateOverviewCsv(document),
            CreateGroupsCsv(document),
            CreateItemsCsv(document),
            CreateRunsCsv(document),
            CreateRunTotalsCsv(document),
            CreateMapTotalsCsv(document),
            CreateRecordsCsv(document),
            CreateCombatTotalsCsv(document),
            CreateCombatAttributionCsv(document),
            CreateWeaponTotalsCsv(document),
            CreateAmmunitionTotalsCsv(document),
            CreateEquipmentTotalsCsv(document),
            CreateRecurringLoadoutsCsv(document),
            CreateEquipmentCombatCsv(document),
            CreateContainersCsv(document),
            CreateRoutesCsv(document),
            CreateSegmentsCsv(document),
            CreateSegmentEventsCsv(document),
            CreateRouteMapTotalsCsv(document),
            CreateEconomyTotalsCsv(document),
            CreateEconomySourcesCsv(document),
            CreateEconomyContextsCsv(document),
            CreateCashRaidOutcomesCsv(document),
            CreateWorldTimeCsv(document));
    }

    private static string CreateWorldTimeCsv(StatisticsExportDocument document)
    {
        var value = document.WorldTime;
        var builder = new StringBuilder();
        builder.AppendLine("calendar_days_advanced,observed_game_time_ticks,observed_game_time_seconds,completed_sleep_sessions,sleep_advanced_time_ticks,sleep_advanced_time_seconds,calendar_capability,calendar_provenance,observed_elapsed_capability,observed_elapsed_provenance,sleep_sessions_capability,sleep_sessions_provenance,sleep_time_capability,sleep_time_provenance,historical_unavailable,historical_provenance,repaired_invalid_state");
        builder.Append(value.CalendarDaysAdvanced.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(value.ObservedGameTimeTicks.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append((value.ObservedGameTimeTicks / (double)TimeSpan.TicksPerSecond).ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(value.CompletedSleepSessions.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(value.SleepAdvancedTimeTicks.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append((value.SleepAdvancedTimeTicks / (double)TimeSpan.TicksPerSecond).ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(value.Capabilities.CalendarDays.State).Append(',').Append(Csv(value.Capabilities.CalendarDays.Provenance)).Append(',')
            .Append(value.Capabilities.ObservedElapsed.State).Append(',').Append(Csv(value.Capabilities.ObservedElapsed.Provenance)).Append(',')
            .Append(value.Capabilities.CompletedSleepSessions.State).Append(',').Append(Csv(value.Capabilities.CompletedSleepSessions.Provenance)).Append(',')
            .Append(value.Capabilities.SleepAdvancedTime.State).Append(',').Append(Csv(value.Capabilities.SleepAdvancedTime.Provenance)).Append(',')
            .Append(value.HistoricalUnavailable.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(Csv(value.HistoricalProvenance)).Append(',')
            .Append(value.WasRepairedFromInvalidState.ToString(CultureInfo.InvariantCulture)).AppendLine();
        return builder.ToString();
    }

    private static string CreateEconomyTotalsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scope,scope_id,run_id,segment_id,map_id,map_display_name,currency,gross_inflow,gross_outflow,net_flow,amount_capability,amount_capability_provenance,source_capability,source_capability_provenance,context_capability,context_capability_provenance,historical_unavailable,repaired_invalid_state,arithmetic_saturated,legacy_identity_saturation_incomplete");
        foreach (var scope in EconomyScopes(document))
            foreach (var currency in Enum.GetValues(typeof(CurrencyKind)).Cast<CurrencyKind>())
            {
                scope.Economy.Currencies.TryGetValue(currency.ToString(), out var value);
                var totals = value?.Totals ?? new CurrencyFlowTotals();
                var capabilities = CurrencyCapabilities(scope.Economy, currency);
                var unavailableHistoryWithoutM9Flow = scope.Economy.HistoricalUnavailable && value == null;
                builder.Append(Csv(scope.Scope)).Append(',').Append(Csv(scope.ScopeId)).Append(',')
                    .Append(Csv(scope.RunId)).Append(',').Append(Csv(scope.SegmentId)).Append(',')
                    .Append(Csv(scope.MapId)).Append(',').Append(Csv(scope.MapDisplayName)).Append(',')
                    .Append(currency).Append(',')
                    .Append(unavailableHistoryWithoutM9Flow ? string.Empty : totals.GrossInflow.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(unavailableHistoryWithoutM9Flow ? string.Empty : totals.GrossOutflow.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(unavailableHistoryWithoutM9Flow ? string.Empty : totals.NetFlow.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(capabilities.Amount.State).Append(',').Append(Csv(capabilities.Amount.Provenance)).Append(',')
                    .Append(capabilities.Source.State).Append(',').Append(Csv(capabilities.Source.Provenance)).Append(',')
                    .Append(capabilities.Context.State).Append(',').Append(Csv(capabilities.Context.Provenance)).Append(',')
                    .Append(scope.Economy.HistoricalUnavailable ? "true" : "false").Append(',')
                    .Append(scope.Economy.WasRepairedFromInvalidState ? "true" : "false").Append(',')
                    .Append(IsArithmeticSaturated(scope.Economy, currency) ? "true" : "false").Append(',')
                    .Append(scope.Economy.LegacyIdentitySaturationIncomplete ? "true" : "false").AppendLine();
            }
        return builder.ToString();
    }

    private static string CreateEconomySourcesCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scope,scope_id,run_id,segment_id,map_id,currency,source,gross_inflow,gross_outflow,net_flow,source_capability,source_capability_provenance,historical_unavailable,repaired_invalid_state,arithmetic_saturated,legacy_identity_saturation_incomplete");
        foreach (var scope in EconomyScopes(document))
            foreach (var currency in scope.Economy.Currencies.Values.OrderBy(value => value.Currency))
                foreach (var row in currency.Sources.OrderBy(value => value.Key, StringComparer.Ordinal))
                    builder.Append(Csv(scope.Scope)).Append(',').Append(Csv(scope.ScopeId)).Append(',')
                        .Append(Csv(scope.RunId)).Append(',').Append(Csv(scope.SegmentId)).Append(',').Append(Csv(scope.MapId)).Append(',')
                        .Append(currency.Currency).Append(',').Append(Csv(row.Key)).Append(',')
                        .Append(row.Value.GrossInflow.ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append(row.Value.GrossOutflow.ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append(row.Value.NetFlow.ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append(CurrencyCapabilities(scope.Economy, currency.Currency).Source.State).Append(',')
                        .Append(Csv(CurrencyCapabilities(scope.Economy, currency.Currency).Source.Provenance)).Append(',')
                        .Append(scope.Economy.HistoricalUnavailable ? "true" : "false").Append(',')
                        .Append(scope.Economy.WasRepairedFromInvalidState ? "true" : "false").Append(',')
                        .Append(IsArithmeticSaturated(scope.Economy, currency.Currency) ? "true" : "false").Append(',')
                        .Append(scope.Economy.LegacyIdentitySaturationIncomplete ? "true" : "false").AppendLine();
        return builder.ToString();
    }

    private static string CreateEconomyContextsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scope,scope_id,run_id,segment_id,map_id,currency,gameplay_context,gross_inflow,gross_outflow,net_flow,context_capability,context_capability_provenance,historical_unavailable,repaired_invalid_state,arithmetic_saturated,legacy_identity_saturation_incomplete");
        foreach (var scope in EconomyScopes(document))
            foreach (var currency in scope.Economy.Currencies.Values.OrderBy(value => value.Currency))
                foreach (var row in currency.Contexts.OrderBy(value => value.Key, StringComparer.Ordinal))
                    builder.Append(Csv(scope.Scope)).Append(',').Append(Csv(scope.ScopeId)).Append(',')
                        .Append(Csv(scope.RunId)).Append(',').Append(Csv(scope.SegmentId)).Append(',').Append(Csv(scope.MapId)).Append(',')
                        .Append(currency.Currency).Append(',').Append(Csv(row.Key)).Append(',')
                        .Append(row.Value.GrossInflow.ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append(row.Value.GrossOutflow.ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append(row.Value.NetFlow.ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append(CurrencyCapabilities(scope.Economy, currency.Currency).Context.State).Append(',')
                        .Append(Csv(CurrencyCapabilities(scope.Economy, currency.Currency).Context.Provenance)).Append(',')
                        .Append(scope.Economy.HistoricalUnavailable ? "true" : "false").Append(',')
                        .Append(scope.Economy.WasRepairedFromInvalidState ? "true" : "false").Append(',')
                        .Append(IsArithmeticSaturated(scope.Economy, currency.Currency) ? "true" : "false").Append(',')
                        .Append(scope.Economy.LegacyIdentitySaturationIncomplete ? "true" : "false").AppendLine();
        return builder.ToString();
    }

    private static string CreateCashRaidOutcomesCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scope,scope_id,run_id,segment_id,map_id,acquired,secured,lost,unresolved,acquisition_capability,acquisition_capability_provenance,terminal_capability,terminal_capability_provenance,terminal_ambiguous,terminal_recorded,historical_unavailable,repaired_invalid_state,cash_arithmetic_saturated,legacy_identity_saturation_incomplete");
        foreach (var scope in EconomyScopes(document))
        {
            var value = scope.Economy.CashRaidOutcomes;
            var unavailableHistoryWithoutM9Outcome = scope.Economy.HistoricalUnavailable
                                                     && value.Acquired == 0
                                                     && value.Secured == 0
                                                     && value.Lost == 0
                                                     && value.Unresolved == 0;
            string Outcome(long amount) => unavailableHistoryWithoutM9Outcome
                ? string.Empty
                : amount.ToString(CultureInfo.InvariantCulture);
            builder.Append(Csv(scope.Scope)).Append(',').Append(Csv(scope.ScopeId)).Append(',')
                .Append(Csv(scope.RunId)).Append(',').Append(Csv(scope.SegmentId)).Append(',').Append(Csv(scope.MapId)).Append(',')
                .Append(Outcome(value.Acquired)).Append(',').Append(Outcome(value.Secured)).Append(',')
                .Append(Outcome(value.Lost)).Append(',').Append(Outcome(value.Unresolved)).Append(',')
                .Append(scope.Economy.Capabilities.CashExternalAcquisition.State).Append(',')
                .Append(Csv(scope.Economy.Capabilities.CashExternalAcquisition.Provenance)).Append(',')
                .Append(scope.Economy.Capabilities.CashTerminalOutcomes.State).Append(',')
                .Append(Csv(scope.Economy.Capabilities.CashTerminalOutcomes.Provenance)).Append(',')
                .Append(scope.Economy.CashTerminalDispositionAmbiguous ? "true" : "false").Append(',')
                .Append(scope.Economy.CashTerminalDispositionRecorded ? "true" : "false").Append(',')
                .Append(scope.Economy.HistoricalUnavailable ? "true" : "false").Append(',')
                .Append(scope.Economy.WasRepairedFromInvalidState ? "true" : "false").Append(',')
                .Append(scope.Economy.CashArithmeticSaturated ? "true" : "false").Append(',')
                .Append(scope.Economy.LegacyIdentitySaturationIncomplete ? "true" : "false").AppendLine();
        }
        return builder.ToString();
    }

    private static IEnumerable<EconomyScope> EconomyScopes(StatisticsExportDocument document)
    {
        yield return new EconomyScope("lifetime", document.GenerationId, string.Empty, string.Empty, string.Empty, string.Empty, document.Economy);
        yield return new EconomyScope("completed_runs", document.GenerationId, string.Empty, string.Empty, string.Empty, string.Empty, document.RunTotals.Economy);
        foreach (var map in document.RunTotals.Maps.Values.OrderBy(value => value.MapId, StringComparer.Ordinal))
            yield return new EconomyScope("starting_map", map.MapId, string.Empty, string.Empty, map.MapId, map.DisplayName, map.Economy);
        foreach (var map in document.RunTotals.RouteMaps.Values.OrderBy(value => value.MapId, StringComparer.Ordinal))
            yield return new EconomyScope("route_map", map.MapId, string.Empty, string.Empty, map.MapId, map.DisplayName, map.Economy);
        foreach (var run in document.Runs.OrderBy(value => value.StartedUtc).ThenBy(value => value.RunId, StringComparer.Ordinal))
        {
            yield return new EconomyScope("run", run.RunId, run.RunId, string.Empty, run.StartingMapId, run.StartingMapDisplayName, run.Economy);
            foreach (var segment in run.Segments.OrderBy(value => value.SegmentIndex))
                yield return new EconomyScope("segment", segment.SegmentId, run.RunId, segment.SegmentId, segment.MapId, segment.MapDisplayName, segment.Economy);
        }
    }

    private static bool IsArithmeticSaturated(EconomyStatisticsAggregate economy, CurrencyKind currency) =>
        currency == CurrencyKind.Money
            ? economy.MoneyArithmeticSaturated
            : economy.CashArithmeticSaturated;

    private static (MetricAvailability Amount, MetricAvailability Source, MetricAvailability Context) CurrencyCapabilities(
        EconomyStatisticsAggregate economy,
        CurrencyKind currency) => currency == CurrencyKind.Money
        ? (economy.Capabilities.MoneyAmountDirection, economy.Capabilities.MoneySourceAttribution, economy.Capabilities.MoneyContextAttribution)
        : (economy.Capabilities.CashAmountDirection, economy.Capabilities.CashExternalAcquisition, economy.Capabilities.CashContextAttribution);

    private static string CreateRoutesCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("run_id,starting_map_id,starting_map_display_name,ending_map_id,ending_map_display_name,route_signature,segment_count,ordered_route_capability,ordered_route_provenance,segment_capability,segment_provenance,event_attribution_capability,event_attribution_provenance,route_map_totals_capability,route_map_totals_provenance,historical_route_unavailable,repaired_invalid_state,current_event_capture_capability,current_event_capture_provenance,historical_event_attribution_incomplete,historical_event_attribution_provenance,associated_event_count,association_row_count");
        foreach (var run in document.Runs.OrderBy(value => value.StartedUtc).ThenBy(value => value.RunId, StringComparer.Ordinal))
            builder.Append(Csv(run.RunId)).Append(',').Append(Csv(run.StartingMapId)).Append(',')
                .Append(Csv(run.StartingMapDisplayName)).Append(',').Append(Csv(run.EndingMapId)).Append(',')
                .Append(Csv(run.EndingMapDisplayName)).Append(',').Append(Csv(run.RouteSignature)).Append(',')
                .Append(run.Segments.Count.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(run.RouteCapabilities.OrderedRoute.State).Append(',').Append(Csv(run.RouteCapabilities.OrderedRoute.Provenance)).Append(',')
                .Append(run.RouteCapabilities.Segments.State).Append(',').Append(Csv(run.RouteCapabilities.Segments.Provenance)).Append(',')
                .Append(run.RouteCapabilities.EventAttribution.State).Append(',').Append(Csv(run.RouteCapabilities.EventAttribution.Provenance)).Append(',')
                .Append(run.RouteCapabilities.RouteAwareMapTotals.State).Append(',').Append(Csv(run.RouteCapabilities.RouteAwareMapTotals.Provenance)).Append(',')
                .Append(run.HistoricalRouteUnavailable ? "true" : "false").Append(',')
                .Append(run.RouteWasRepairedFromInvalidState ? "true" : "false").Append(',')
                .Append(run.RouteCapabilities.CurrentEventAttributionCapture.State).Append(',')
                .Append(Csv(run.RouteCapabilities.CurrentEventAttributionCapture.Provenance)).Append(',')
                .Append(run.HistoricalEventAttributionIncomplete ? "true" : "false").Append(',')
                .Append(Csv(run.HistoricalEventAttributionProvenance)).Append(',')
                .Append(run.SegmentEventAssociations
                    .Aggregate(System.Numerics.BigInteger.Zero, (total, value) => total + value.Count)
                    .ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(run.SegmentEventAssociations.Count.ToString(CultureInfo.InvariantCulture)).AppendLine();
        return builder.ToString();
    }

    private static string CreateSegmentsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("run_id,segment_id,segment_index,map_id,map_display_name,map_known,entered_utc,exited_utc,active_duration_seconds,physical_distance,teleport_distance,transition_excluded_distance,exit_reason,segment_capability,event_attribution_capability,item_activations,actual_health_restored,firing_actions,ammunition_units_consumed,projectiles,damage_dealt,damage_received,ranged_hits,melee_hits,kills_by_you,observed_world_deaths,legacy_unclassified_deaths,player_deaths,unique_containers_looted,integrity_tags,repaired_invalid_state,current_event_capture_capability,historical_event_attribution_incomplete,damage_dealt_state,damage_received_state,ranged_hits_state,melee_hits_state,kills_by_you_state,observed_world_deaths_state,player_deaths_state");
        foreach (var run in document.Runs.OrderBy(value => value.StartedUtc).ThenBy(value => value.RunId, StringComparer.Ordinal))
            foreach (var segment in run.Segments.OrderBy(value => value.SegmentIndex))
                builder.Append(Csv(run.RunId)).Append(',').Append(Csv(segment.SegmentId)).Append(',')
                    .Append(segment.SegmentIndex.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Csv(segment.MapId)).Append(',')
                    .Append(Csv(segment.MapDisplayName)).Append(',').Append(segment.MapKnown ? "true" : "false").Append(',')
                    .Append(Csv(segment.EnteredUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                    .Append(Csv(segment.ExitedUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty)).Append(',')
                    .Append(segment.ActiveDurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(segment.PhysicalDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(segment.TeleportDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(segment.TransitionExcludedDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(segment.ExitReason).Append(',').Append(run.RouteCapabilities.Segments.State).Append(',')
                    .Append(run.RouteCapabilities.EventAttribution.State).Append(',').Append(segment.ItemStatistics.Overall.ActivationCount).Append(',')
                    .Append(segment.ItemStatistics.Overall.ActualHealthRestored.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(segment.WeaponStatistics.Totals.FiringActions).Append(',').Append(segment.WeaponStatistics.Totals.AmmunitionUnitsConsumed).Append(',')
                    .Append(segment.WeaponStatistics.Totals.Projectiles).Append(',')
                    .Append(segment.CombatStatistics.Totals.DamageDealt.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(segment.CombatStatistics.Totals.DamageReceived.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(segment.CombatStatistics.Totals.RangedHits).Append(',').Append(segment.CombatStatistics.Totals.MeleeHits).Append(',')
                    .Append(segment.CombatStatistics.Totals.KillsByYou).Append(',')
                    .Append(segment.CombatStatistics.Totals.ObservedWorldDeaths).Append(',')
                    .Append(segment.CombatStatistics.Totals.LegacyUnclassifiedDeaths).Append(',')
                    .Append(segment.CombatStatistics.Totals.PlayerDeaths).Append(',')
                    .Append(segment.ContainerStatistics.UniqueContainersLooted).Append(',').Append(Csv(segment.IntegrityTags.ToString())).Append(',')
                    .Append(segment.WasRepairedFromInvalidState ? "true" : "false").Append(',')
                    .Append(run.RouteCapabilities.CurrentEventAttributionCapture.State).Append(',')
                    .Append(run.HistoricalEventAttributionIncomplete ? "true" : "false").Append(',')
                    .Append(segment.CombatStatistics.Capabilities.DamageDealt.State).Append(',')
                    .Append(segment.CombatStatistics.Capabilities.DamageReceived.State).Append(',')
                    .Append(segment.CombatStatistics.Capabilities.RangedHits.State).Append(',')
                    .Append(segment.CombatStatistics.Capabilities.MeleeHits.State).Append(',')
                    .Append(segment.CombatStatistics.Capabilities.KillsByYou.State).Append(',')
                    .Append(segment.CombatStatistics.Capabilities.ObservedWorldDeaths.State).Append(',')
                    .Append(segment.CombatStatistics.Capabilities.PlayerDeaths.State).AppendLine();
        return builder.ToString();
    }

    private static string CreateSegmentEventsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("run_id,event_id,event_kind,timestamp_utc,source_segment_id,source_map_id,outcome_segment_id,outcome_map_id,association_count,representation,first_timestamp_utc,last_timestamp_utc,historical_event_attribution_incomplete,current_event_capture_capability");
        foreach (var run in document.Runs.OrderBy(value => value.StartedUtc).ThenBy(value => value.RunId, StringComparer.Ordinal))
            foreach (var value in run.SegmentEventAssociations.OrderBy(value => value.TimestampUtc).ThenBy(value => value.EventId, StringComparer.Ordinal))
                builder.Append(Csv(run.RunId)).Append(',').Append(Csv(value.EventId)).Append(',').Append(Csv(value.EventKind)).Append(',')
                    .Append(Csv(value.TimestampUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                    .Append(Csv(value.SourceSegmentId)).Append(',').Append(Csv(value.SourceMapId)).Append(',')
                    .Append(Csv(value.OutcomeSegmentId)).Append(',').Append(Csv(value.OutcomeMapId)).Append(',')
                    .Append(value.Count.ToString(CultureInfo.InvariantCulture)).Append(',').Append(value.Representation).Append(',')
                    .Append(Csv(value.FirstTimestampUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                    .Append(Csv(value.LastTimestampUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                    .Append(run.HistoricalEventAttributionIncomplete ? "true" : "false").Append(',')
                    .Append(run.RouteCapabilities.CurrentEventAttributionCapture.State).AppendLine();
        return builder.ToString();
    }

    private static string CreateRouteMapTotalsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("map_id,map_display_name,map_known,runs_visited,segment_visits,active_duration_seconds,physical_distance,teleport_distance,transition_excluded_distance,item_activations,actual_health_restored,firing_actions,damage_dealt,damage_received,kills_by_you,observed_world_deaths,legacy_unclassified_deaths,unique_containers_looted,historical_unavailable,repaired_invalid_state,damage_dealt_state,damage_received_state,kills_by_you_state,observed_world_deaths_state");
        foreach (var map in document.RunTotals.RouteMaps.Values.OrderBy(value => value.MapId, StringComparer.Ordinal))
            builder.Append(Csv(map.MapId)).Append(',').Append(Csv(map.DisplayName)).Append(',').Append(map.IsKnown ? "true" : "false").Append(',')
                .Append(map.RunsVisited).Append(',').Append(map.SegmentVisits).Append(',')
                .Append(map.ActiveDurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(map.PhysicalDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(map.TeleportDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(map.TransitionExcludedDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(map.ItemStatistics.Overall.ActivationCount).Append(',')
                .Append(map.ItemStatistics.Overall.ActualHealthRestored.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(map.WeaponStatistics.Totals.FiringActions).Append(',')
                .Append(map.CombatStatistics.Totals.DamageDealt.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(map.CombatStatistics.Totals.DamageReceived.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(map.CombatStatistics.Totals.KillsByYou).Append(',')
                .Append(map.CombatStatistics.Totals.ObservedWorldDeaths).Append(',')
                .Append(map.CombatStatistics.Totals.LegacyUnclassifiedDeaths).Append(',')
                .Append(map.ContainerStatistics.UniqueContainersLooted).Append(',')
                .Append(map.HistoricalUnavailable ? "true" : "false").Append(',')
                .Append(map.WasRepairedFromInvalidState ? "true" : "false").Append(',')
                .Append(map.CombatStatistics.Capabilities.DamageDealt.State).Append(',')
                .Append(map.CombatStatistics.Capabilities.DamageReceived.State).Append(',')
                .Append(map.CombatStatistics.Capabilities.KillsByYou.State).Append(',')
                .Append(map.CombatStatistics.Capabilities.ObservedWorldDeaths.State).AppendLine();
        return builder.ToString();
    }

    private static string CreateContainersCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scope,scope_id,map_display_name,unique_containers_looted,capability,historical_unavailable,repaired_invalid_state");
        Append("lifetime", document.GenerationId, string.Empty, document.RunTotals.ContainerStatistics);
        foreach (var map in document.RunTotals.Maps.Values.OrderBy(value => value.MapId, StringComparer.Ordinal))
            Append("starting_map", map.MapId, map.DisplayName, map.ContainerStatistics);
        foreach (var map in document.RunTotals.RouteMaps.Values.OrderBy(value => value.MapId, StringComparer.Ordinal))
            Append("route_map", map.MapId, map.DisplayName, map.ContainerStatistics);
        foreach (var run in document.Runs.OrderBy(value => value.StartedUtc).ThenBy(value => value.RunId, StringComparer.Ordinal))
            Append("run", run.RunId, run.MapDisplayName, run.ContainerStatistics);
        return builder.ToString();

        void Append(string scope, string scopeId, string mapName, ContainerStatisticsAggregate statistics)
        {
            builder.Append(Csv(scope)).Append(',').Append(Csv(scopeId)).Append(',').Append(Csv(mapName)).Append(',')
                .Append(statistics.UniqueContainersLooted.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(statistics.Capabilities.UniqueContainersLooted.State).Append(',')
                .Append(statistics.HistoricalUnavailable ? "true" : "false").Append(',')
                .Append(statistics.WasRepairedFromInvalidState ? "true" : "false").AppendLine();
        }
    }

    private static string CreateEquipmentTotalsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scope,scope_id,breakdown,entity_id,display_name,active_duration_seconds,run_occurrences");
        AppendEquipmentDurations(builder, "lifetime", document.GenerationId, document.RunTotals.EquipmentStatistics);
        foreach (var map in document.RunTotals.Maps.Values.OrderBy(x => x.MapId, StringComparer.Ordinal))
            AppendEquipmentDurations(builder, "starting_map", map.MapId, map.EquipmentStatistics);
        foreach (var map in document.RunTotals.RouteMaps.Values.OrderBy(x => x.MapId, StringComparer.Ordinal))
            AppendEquipmentDurations(builder, "route_map", map.MapId, map.EquipmentStatistics);
        foreach (var run in document.Runs.OrderBy(x => x.StartedUtc).ThenBy(x => x.RunId, StringComparer.Ordinal))
            AppendEquipmentDurations(builder, "run", run.RunId, run.EquipmentStatistics);
        return builder.ToString();
    }

    private static void AppendEquipmentDurations(StringBuilder builder, string scope, string scopeId, EquipmentStatisticsAggregate statistics)
    {
        Append("slot", statistics.Slots);
        Append("item", statistics.Items);
        Append("slotted_weapon", statistics.SlottedWeapons);
        Append("selected_weapon", statistics.SelectedWeapons);
        Append("loadout", statistics.Loadouts);
        Append("totem_state", statistics.TotemStates);
        Append("totem_set", statistics.TotemSets);
        return;
        void Append(string kind, Dictionary<string, EquipmentDurationAggregate> values)
        {
            foreach (var row in values.Values.OrderByDescending(x => x.ActiveDurationSeconds).ThenBy(x => x.Id, StringComparer.Ordinal))
                builder.Append(scope).Append(',').Append(Csv(scopeId)).Append(',').Append(kind).Append(',')
                    .Append(Csv(row.Id)).Append(',').Append(Csv(row.DisplayName)).Append(',')
                    .Append(row.ActiveDurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.RunOccurrences.ToString(CultureInfo.InvariantCulture)).AppendLine();
        }
    }

    private static string CreateRecurringLoadoutsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("loadout_id,active_duration_seconds,run_occurrences");
        foreach (var row in document.RunTotals.EquipmentStatistics.Loadouts.Values
                     .Where(x => x.RunOccurrences >= 2)
                     .OrderByDescending(x => x.ActiveDurationSeconds).ThenBy(x => x.Id, StringComparer.Ordinal))
            builder.Append(Csv(row.Id)).Append(',')
                .Append(row.ActiveDurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.RunOccurrences.ToString(CultureInfo.InvariantCulture)).AppendLine();
        return builder.ToString();
    }

    private static string CreateEquipmentCombatCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scope,scope_id,loadout_id,selected_weapon_slot_id,selected_weapon_id,totem_set_id,firing_actions,ammunition_units_consumed,projectiles,damage_dealt,damage_received,ranged_hits,melee_hits,kills_by_you,legacy_unclassified_death_credit,player_deaths,historical_combat_ownership_unavailable,historical_combat_ownership_provenance,damage_dealt_state,damage_received_state,ranged_hits_state,melee_hits_state,kills_by_you_state,player_deaths_state,ownership_state");
        AppendEquipmentCombat(builder, "lifetime", document.GenerationId, document.RunTotals.EquipmentStatistics, document.RunTotals.CombatStatistics);
        foreach (var map in document.RunTotals.Maps.Values.OrderBy(x => x.MapId, StringComparer.Ordinal))
            AppendEquipmentCombat(builder, "starting_map", map.MapId, map.EquipmentStatistics, map.CombatStatistics);
        foreach (var map in document.RunTotals.RouteMaps.Values.OrderBy(x => x.MapId, StringComparer.Ordinal))
            AppendEquipmentCombat(builder, "route_map", map.MapId, map.EquipmentStatistics, map.CombatStatistics);
        foreach (var run in document.Runs.OrderBy(x => x.StartedUtc).ThenBy(x => x.RunId, StringComparer.Ordinal))
            AppendEquipmentCombat(builder, "run", run.RunId, run.EquipmentStatistics, run.CombatStatistics);
        return builder.ToString();
    }

    private static void AppendEquipmentCombat(
        StringBuilder builder,
        string scope,
        string scopeId,
        EquipmentStatisticsAggregate statistics,
        CombatStatisticsAggregate combatStatistics)
    {
        foreach (var row in statistics.CombatAssociations.Values.OrderBy(x => x.LoadoutId, StringComparer.Ordinal)
                     .ThenBy(x => x.SelectedWeaponId, StringComparer.Ordinal).ThenBy(x => x.TotemSetId, StringComparer.Ordinal))
            builder.Append(scope).Append(',').Append(Csv(scopeId)).Append(',').Append(Csv(row.LoadoutId)).Append(',')
                .Append(Csv(row.SelectedWeaponSlotId)).Append(',').Append(Csv(row.SelectedWeaponId)).Append(',').Append(Csv(row.TotemSetId)).Append(',')
                .Append(row.FiringActions.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.AmmunitionUnitsConsumed.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Projectiles.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.DamageDealt.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.DamageReceived.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.RangedHits.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.MeleeHits.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.KillsByYou.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.LegacyUnclassifiedDeathCredit.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.PlayerDeaths.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(statistics.HistoricalCombatOwnershipUnavailable ? "true" : "false").Append(',')
                .Append(Csv(statistics.HistoricalCombatOwnershipProvenance)).Append(',')
                .Append(combatStatistics.Capabilities.DamageDealt.State).Append(',')
                .Append(combatStatistics.Capabilities.DamageReceived.State).Append(',')
                .Append(combatStatistics.Capabilities.RangedHits.State).Append(',')
                .Append(combatStatistics.Capabilities.MeleeHits.State).Append(',')
                .Append(combatStatistics.Capabilities.KillsByYou.State).Append(',')
                .Append(combatStatistics.Capabilities.PlayerDeaths.State).Append(',')
                .Append(combatStatistics.Capabilities.Ownership.State).AppendLine();
    }

    private static string SerializeJson(StatisticsExportDocument document)
    {
        var serializer = new DataContractJsonSerializer(
            typeof(StatisticsExportDocument),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, document);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string CreateOverviewCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        AppendTotalsHeader(builder, "generation_id,slot,revision,exported_utc");
        builder.Append(Csv(document.GenerationId)).Append(',')
            .Append(document.Slot.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(document.Revision.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(Csv(document.ExportedUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',');
        AppendTotals(builder, document.Overall);
        return builder.ToString();
    }

    private static string CreateGroupsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        AppendTotalsHeader(builder, "group");
        foreach (var group in document.Groups)
        {
            builder.Append(Csv(group.Group)).Append(',');
            AppendTotals(builder, group.Totals);
        }

        return builder.ToString();
    }

    private static string CreateItemsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        AppendTotalsHeader(builder, "item_id,display_name,group,effect_tags");
        foreach (var item in document.Items)
        {
            builder.Append(Csv(item.ItemId)).Append(',')
                .Append(Csv(item.DisplayName)).Append(',')
                .Append(Csv(item.Group)).Append(',')
                .Append(Csv(string.Join("|", item.EffectTags))).Append(',');
            AppendTotals(builder, item.Totals);
        }

        return builder.ToString();
    }

    private static string CreateRunsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            "run_id,save_generation_id,native_raid_id,map_id,map_display_name,map_known,starting_map_id,starting_map_display_name,ending_map_id,ending_map_display_name,route_signature,started_utc,ended_utc,active_duration_seconds,wall_clock_duration_seconds,outcome,physical_distance,teleport_distance,transition_excluded_distance,kills_by_you,observed_world_deaths,legacy_unclassified_deaths,unique_containers_looted,container_capability,integrity_tags,record_eligible,game_version,game_build,lifecycle_capability,lifecycle_adapter_version,movement_capability,movement_adapter_version,map_capability,map_adapter_version,kills_by_you_state,observed_world_deaths_state");
        foreach (var run in document.Runs.OrderBy(run => run.StartedUtc).ThenBy(run => run.RunId, StringComparer.Ordinal))
        {
            builder.Append(Csv(run.RunId)).Append(',')
                .Append(Csv(run.SaveGenerationId)).Append(',')
                .Append(Csv(run.NativeRaidId ?? string.Empty)).Append(',')
                .Append(Csv(run.MapId)).Append(',')
                .Append(Csv(run.MapDisplayName)).Append(',')
                .Append(run.MapKnown ? "true" : "false").Append(',')
                .Append(Csv(run.StartingMapId)).Append(',').Append(Csv(run.StartingMapDisplayName)).Append(',')
                .Append(Csv(run.EndingMapId)).Append(',').Append(Csv(run.EndingMapDisplayName)).Append(',')
                .Append(Csv(run.RouteSignature)).Append(',')
                .Append(Csv(run.StartedUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                .Append(Csv(run.EndedUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                .Append(run.ActiveDurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(run.WallClockDurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(run.Outcome).Append(',')
                .Append(run.PhysicalDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(run.TeleportDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(run.TransitionExcludedDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(run.CombatStatistics.Totals.KillsByYou).Append(',')
                .Append(run.CombatStatistics.Totals.ObservedWorldDeaths).Append(',')
                .Append(run.CombatStatistics.Totals.LegacyUnclassifiedDeaths).Append(',')
                .Append(run.ContainerStatistics.UniqueContainersLooted.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(run.ContainerStatistics.Capabilities.UniqueContainersLooted.State).Append(',')
                .Append(Csv(run.IntegrityTags.ToString())).Append(',')
                .Append(run.RecordEligible ? "true" : "false").Append(',')
                .Append(Csv(run.GameVersion)).Append(',')
                .Append(Csv(run.GameBuild)).Append(',')
                .Append(run.LifecycleCapability).Append(',')
                .Append(Csv(run.LifecycleAdapterVersion)).Append(',')
                .Append(run.MovementCapability).Append(',')
                .Append(Csv(run.MovementAdapterVersion)).Append(',')
                .Append(run.MapCapability).Append(',')
                .Append(Csv(run.MapAdapterVersion)).Append(',')
                .Append(run.CombatStatistics.Capabilities.KillsByYou.State).Append(',')
                .Append(run.CombatStatistics.Capabilities.ObservedWorldDeaths.State).AppendLine();
        }

        return builder.ToString();
    }

    private static string CreateRunTotalsCsv(StatisticsExportDocument document)
    {
        var totals = document.RunTotals;
        var builder = new StringBuilder();
        builder.AppendLine("generation_id,total_runs,extracted,died,interrupted,physical_distance,teleport_distance,transition_excluded_distance,route_aware_history_unavailable,kills_by_you,observed_world_deaths,legacy_unclassified_deaths,unique_containers_looted,container_capability,kills_by_you_state,observed_world_deaths_state");
        builder.Append(Csv(document.GenerationId)).Append(',')
            .Append(totals.TotalRuns.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(ReadOutcome(totals.Outcomes, RunOutcome.Extracted)).Append(',')
            .Append(ReadOutcome(totals.Outcomes, RunOutcome.Died)).Append(',')
            .Append(ReadOutcome(totals.Outcomes, RunOutcome.Interrupted)).Append(',')
            .Append(totals.PhysicalDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.TeleportDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.TransitionExcludedDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.RouteAwareHistoryUnavailable ? "true" : "false").Append(',')
            .Append(totals.CombatStatistics.Totals.KillsByYou).Append(',')
            .Append(totals.CombatStatistics.Totals.ObservedWorldDeaths).Append(',')
            .Append(totals.CombatStatistics.Totals.LegacyUnclassifiedDeaths).Append(',')
            .Append(totals.ContainerStatistics.UniqueContainersLooted.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.ContainerStatistics.Capabilities.UniqueContainersLooted.State).Append(',')
            .Append(totals.CombatStatistics.Capabilities.KillsByYou.State).Append(',')
            .Append(totals.CombatStatistics.Capabilities.ObservedWorldDeaths.State).AppendLine();
        return builder.ToString();
    }

    private static string CreateMapTotalsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("aggregation_scope,map_id,map_display_name,map_known,total_runs,extracted,died,interrupted,physical_distance,teleport_distance,kills_by_you,observed_world_deaths,legacy_unclassified_deaths,unique_containers_looted,container_capability,item_activations,actual_health_restored,item_history_unavailable,item_repaired_invalid_state,kills_by_you_state,observed_world_deaths_state");
        foreach (var map in document.RunTotals.Maps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal))
        {
            builder.Append("starting_map,").Append(Csv(map.MapId)).Append(',')
                .Append(Csv(map.DisplayName)).Append(',')
                .Append(map.IsKnown ? "true" : "false").Append(',')
                .Append(map.TotalRuns.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(ReadOutcome(map.Outcomes, RunOutcome.Extracted)).Append(',')
                .Append(ReadOutcome(map.Outcomes, RunOutcome.Died)).Append(',')
                .Append(ReadOutcome(map.Outcomes, RunOutcome.Interrupted)).Append(',')
                .Append(map.PhysicalDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(map.TeleportDistance.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(map.CombatStatistics.Totals.KillsByYou).Append(',')
                .Append(map.CombatStatistics.Totals.ObservedWorldDeaths).Append(',')
                .Append(map.CombatStatistics.Totals.LegacyUnclassifiedDeaths).Append(',')
                .Append(map.ContainerStatistics.UniqueContainersLooted.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(map.ContainerStatistics.Capabilities.UniqueContainersLooted.State).Append(',')
                .Append(map.ItemStatistics.Overall.ActivationCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(map.ItemStatistics.Overall.ActualHealthRestored.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(map.ItemStatistics.HistoricalUnavailable ? "true" : "false").Append(',')
                .Append(map.ItemStatistics.WasRepairedFromInvalidState ? "true" : "false").Append(',')
                .Append(map.CombatStatistics.Capabilities.KillsByYou.State).Append(',')
                .Append(map.CombatStatistics.Capabilities.ObservedWorldDeaths.State).AppendLine();
        }

        return builder.ToString();
    }

    private static string CreateRecordsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scope,map_id,map_display_name,outcome,record,run_id,active_duration_seconds,started_utc");
        AppendRecordPair(builder, "overall", string.Empty, string.Empty, RunOutcome.Extracted, document.RunRecords.Extraction);
        AppendRecordPair(builder, "overall", string.Empty, string.Empty, RunOutcome.Died, document.RunRecords.Death);
        foreach (var map in document.RunRecords.Maps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal))
        {
            AppendRecordPair(builder, "starting_map", map.MapId, map.DisplayName, RunOutcome.Extracted, map.Extraction);
            AppendRecordPair(builder, "starting_map", map.MapId, map.DisplayName, RunOutcome.Died, map.Death);
        }

        return builder.ToString();
    }

    private static string CreateCombatTotalsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scope,scope_id,scope_display_name,firing_actions,ammunition_units_consumed,projectiles,trigger_attempts_state,firing_actions_state,ammunition_consumption_state,projectiles_state,weapon_identity_state,ammunition_identity_state");
        AppendCombatTotals(builder, "lifetime", document.GenerationId, "Lifetime", document.RunTotals.WeaponStatistics, document.Capabilities);
        foreach (var map in document.RunTotals.Maps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal))
        {
            AppendCombatTotals(builder, "starting_map", map.MapId, map.DisplayName, map.WeaponStatistics, document.Capabilities);
        }
        foreach (var map in document.RunTotals.RouteMaps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal))
            AppendCombatTotals(builder, "route_map", map.MapId, map.DisplayName, map.WeaponStatistics, document.Capabilities);

        foreach (var run in document.Runs.OrderBy(run => run.StartedUtc).ThenBy(run => run.RunId, StringComparer.Ordinal))
        {
            AppendCombatTotals(builder, "run", run.RunId, run.MapDisplayName, run.WeaponStatistics, document.Capabilities);
        }

        return builder.ToString();
    }

    private static string CreateWeaponTotalsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scope,scope_id,weapon_id,display_name,firing_actions,ammunition_units_consumed,projectiles,firing_actions_state,ammunition_consumption_state,projectiles_state");
        AppendWeaponTotals(builder, "lifetime", document.GenerationId, document.RunTotals.WeaponStatistics);
        foreach (var map in document.RunTotals.Maps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal))
        {
            AppendWeaponTotals(builder, "starting_map", map.MapId, map.WeaponStatistics);
        }
        foreach (var map in document.RunTotals.RouteMaps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal))
            AppendWeaponTotals(builder, "route_map", map.MapId, map.WeaponStatistics);

        foreach (var run in document.Runs.OrderBy(run => run.StartedUtc).ThenBy(run => run.RunId, StringComparer.Ordinal))
        {
            AppendWeaponTotals(builder, "run", run.RunId, run.WeaponStatistics);
        }

        return builder.ToString();
    }

    private static string CreateCombatAttributionCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scope,scope_id,breakdown,entity_id,display_name,damage_caused,damage_dealt,damage_received,completed_player_projectiles,ranged_hits,accuracy,melee_swings,melee_hits,kills_by_you,observed_world_deaths,legacy_unclassified_deaths,player_deaths,headshots,headshot_final_blows,damage_dealt_state,damage_received_state,accuracy_state,melee_swings_state,melee_hits_state,kills_by_you_state,observed_world_deaths_state,player_deaths_state,ownership_state,enemy_identity_state,enemy_family_state,cause_state,weapon_identity_state,ammunition_identity_state,damage_over_time_state,headshots_state,headshot_final_blows_state,historical_ownership_unavailable,historical_ownership_provenance,repaired");
        AppendCombatAttributionScope(builder, "lifetime", document.GenerationId, document.RunTotals.CombatStatistics);
        foreach (var map in document.RunTotals.Maps.Values.OrderBy(x => x.MapId, StringComparer.Ordinal))
            AppendCombatAttributionScope(builder, "starting_map", map.MapId, map.CombatStatistics);
        foreach (var map in document.RunTotals.RouteMaps.Values.OrderBy(x => x.MapId, StringComparer.Ordinal))
            AppendCombatAttributionScope(builder, "route_map", map.MapId, map.CombatStatistics);
        foreach (var run in document.Runs.OrderBy(x => x.StartedUtc).ThenBy(x => x.RunId, StringComparer.Ordinal))
            AppendCombatAttributionScope(builder, "run", run.RunId, run.CombatStatistics);
        return builder.ToString();
    }

    private static void AppendCombatAttributionScope(
        StringBuilder builder, string scope, string scopeId, CombatStatisticsAggregate statistics)
    {
        AppendCombatAttributionRow(builder, scope, scopeId, "total", string.Empty, "Total", statistics.Totals, statistics);
        AppendRows("enemy", statistics.Enemies);
        AppendRows("killer", statistics.Killers);
        AppendRows("family", statistics.Families);
        AppendRows("cause", statistics.Causes);
        AppendRows("weapon", statistics.Weapons);
        AppendRows("ammunition", statistics.Ammunition);
        AppendRows("ownership", statistics.Ownership);
        return;

        void AppendRows(string kind, Dictionary<string, CombatBreakdownAggregate> rows)
        {
            foreach (var row in rows.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
                AppendCombatAttributionRow(builder, scope, scopeId, kind, row.Id, row.DisplayName, row.Totals, statistics);
        }
    }

    private static void AppendCombatAttributionRow(
        StringBuilder builder, string scope, string scopeId, string breakdown, string entityId,
        string displayName, CombatMetricTotals totals, CombatStatisticsAggregate statistics)
    {
        var caps = statistics.Capabilities;
        var accuracy = totals.CompletedPlayerProjectiles > 0
            ? ((double)totals.RangedHits / totals.CompletedPlayerProjectiles).ToString("R", CultureInfo.InvariantCulture)
            : string.Empty;
        builder.Append(scope).Append(',').Append(Csv(scopeId)).Append(',').Append(breakdown).Append(',')
            .Append(Csv(entityId)).Append(',').Append(Csv(displayName)).Append(',')
            .Append(totals.DamageCaused.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.DamageDealt.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.DamageReceived.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.CompletedPlayerProjectiles.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.RangedHits.ToString(CultureInfo.InvariantCulture)).Append(',').Append(accuracy).Append(',')
            .Append(totals.MeleeSwings.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.MeleeHits.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.KillsByYou.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.ObservedWorldDeaths.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.LegacyUnclassifiedDeaths.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.PlayerDeaths.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.Headshots.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.HeadshotFinalBlows.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(caps.DamageDealt.State).Append(',').Append(caps.DamageReceived.State).Append(',')
            .Append(caps.Accuracy.State).Append(',').Append(caps.MeleeSwings.State).Append(',')
            .Append(caps.MeleeHits.State).Append(',').Append(caps.KillsByYou.State).Append(',')
            .Append(caps.ObservedWorldDeaths.State).Append(',')
            .Append(caps.PlayerDeaths.State).Append(',').Append(caps.Ownership.State).Append(',')
            .Append(caps.EnemyIdentity.State).Append(',').Append(caps.EnemyFamily.State).Append(',')
            .Append(caps.Cause.State).Append(',').Append(caps.WeaponIdentity.State).Append(',')
            .Append(caps.AmmunitionIdentity.State).Append(',').Append(caps.DamageOverTime.State).Append(',')
            .Append(caps.Headshots.State).Append(',').Append(caps.HeadshotFinalBlows.State).Append(',')
            .Append(statistics.HistoricalOwnershipUnavailable ? "true" : "false").Append(',')
            .Append(Csv(statistics.HistoricalOwnershipProvenance)).Append(',')
            .Append(statistics.WasRepairedFromInvalidState ? "true" : "false").AppendLine();
    }

    private static string CreateAmmunitionTotalsCsv(StatisticsExportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("scope,scope_id,ammunition_id,display_name,firing_actions,ammunition_units_consumed,projectiles,firing_actions_state,ammunition_consumption_state,projectiles_state");
        AppendAmmunitionTotals(builder, "lifetime", document.GenerationId, document.RunTotals.WeaponStatistics);
        foreach (var map in document.RunTotals.Maps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal))
        {
            AppendAmmunitionTotals(builder, "starting_map", map.MapId, map.WeaponStatistics);
        }
        foreach (var map in document.RunTotals.RouteMaps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal))
            AppendAmmunitionTotals(builder, "route_map", map.MapId, map.WeaponStatistics);

        foreach (var run in document.Runs.OrderBy(run => run.StartedUtc).ThenBy(run => run.RunId, StringComparer.Ordinal))
        {
            AppendAmmunitionTotals(builder, "run", run.RunId, run.WeaponStatistics);
        }

        return builder.ToString();
    }

    private static void AppendCombatTotals(
        StringBuilder builder,
        string scope,
        string scopeId,
        string scopeDisplayName,
        WeaponStatisticsAggregate statistics,
        IReadOnlyList<CapabilityRecord>? currentCapabilities = null)
    {
        var capabilities = statistics.Capabilities;
        builder.Append(scope).Append(',')
            .Append(Csv(scopeId)).Append(',')
            .Append(Csv(scopeDisplayName)).Append(',')
            .Append(statistics.Totals.FiringActions.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(statistics.Totals.AmmunitionUnitsConsumed.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(statistics.Totals.Projectiles.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(ReadCapabilityState(currentCapabilities, WeaponCapabilityIds.TriggerAttempts, AdapterCapabilityState.DisabledIncompatible)).Append(',')
            .Append(capabilities.FiringActions.State).Append(',')
            .Append(capabilities.AmmunitionConsumption.State).Append(',')
            .Append(capabilities.Projectiles.State).Append(',')
            .Append(capabilities.WeaponIdentity.State).Append(',')
            .Append(capabilities.AmmunitionIdentity.State).AppendLine();
    }

    private static void AppendWeaponTotals(
        StringBuilder builder,
        string scope,
        string scopeId,
        WeaponStatisticsAggregate statistics)
    {
        foreach (var weapon in statistics.Weapons.Values.OrderBy(value => value.WeaponId, StringComparer.Ordinal))
        {
            builder.Append(scope).Append(',').Append(Csv(scopeId)).Append(',')
                .Append(Csv(weapon.WeaponId)).Append(',').Append(Csv(weapon.DisplayName)).Append(',');
            AppendWeaponMetricTotals(builder, weapon.Totals, statistics.Capabilities);
        }
    }

    private static void AppendAmmunitionTotals(
        StringBuilder builder,
        string scope,
        string scopeId,
        WeaponStatisticsAggregate statistics)
    {
        foreach (var ammunition in statistics.AmmunitionTypes.Values.OrderBy(value => value.AmmunitionId, StringComparer.Ordinal))
        {
            builder.Append(scope).Append(',').Append(Csv(scopeId)).Append(',')
                .Append(Csv(ammunition.AmmunitionId)).Append(',').Append(Csv(ammunition.DisplayName)).Append(',');
            AppendWeaponMetricTotals(builder, ammunition.Totals, statistics.Capabilities);
        }
    }

    private static void AppendWeaponMetricTotals(
        StringBuilder builder,
        WeaponMetricTotals totals,
        WeaponMetricCapabilities capabilities)
    {
        builder.Append(totals.FiringActions.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.AmmunitionUnitsConsumed.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(totals.Projectiles.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(capabilities.FiringActions.State).Append(',')
            .Append(capabilities.AmmunitionConsumption.State).Append(',')
            .Append(capabilities.Projectiles.State).AppendLine();
    }

    private static AdapterCapabilityState ReadCapabilityState(
        IReadOnlyList<CapabilityRecord>? capabilities,
        string adapterId,
        AdapterCapabilityState fallback) => capabilities?
            .FirstOrDefault(capability => string.Equals(capability.AdapterId, adapterId, StringComparison.Ordinal))
            ?.State ?? fallback;

    private static WeaponMetricCapabilities ApplyCurrentWeaponCapabilityStates(
        WeaponStatisticsAggregate aggregate,
        IReadOnlyList<CapabilityRecord> current,
        bool allowUninitializedFallback)
    {
        var clone = WeaponStatisticsReducer.CloneCapabilities(aggregate.Capabilities);
        clone.FiringActions.State = ResolveAvailability(
            aggregate,
            clone.FiringActions,
            ReadCapabilityState(current, WeaponCapabilityIds.FiringActions, clone.FiringActions.State),
            allowUninitializedFallback);
        clone.AmmunitionConsumption.State = ResolveAvailability(
            aggregate,
            clone.AmmunitionConsumption,
            ReadCapabilityState(current, WeaponCapabilityIds.AmmunitionConsumption, clone.AmmunitionConsumption.State),
            allowUninitializedFallback);
        clone.Projectiles.State = ResolveAvailability(
            aggregate,
            clone.Projectiles,
            ReadCapabilityState(current, WeaponCapabilityIds.Projectiles, clone.Projectiles.State),
            allowUninitializedFallback);
        clone.WeaponIdentity.State = ResolveAvailability(
            aggregate,
            clone.WeaponIdentity,
            ReadCapabilityState(current, WeaponCapabilityIds.WeaponIdentity, clone.WeaponIdentity.State),
            allowUninitializedFallback);
        clone.AmmunitionIdentity.State = ResolveAvailability(
            aggregate,
            clone.AmmunitionIdentity,
            ReadCapabilityState(current, WeaponCapabilityIds.AmmunitionIdentity, clone.AmmunitionIdentity.State),
            allowUninitializedFallback);
        return clone;
    }

    private static CombatMetricCapabilities ApplyCurrentCombatCapabilityStates(
        CombatStatisticsAggregate aggregate,
        IReadOnlyList<CapabilityRecord> current,
        bool allowUninitializedFallback)
    {
        var result = CombatStatisticsReducer.CloneCapabilities(aggregate.Capabilities);
        Apply(result.DamageDealt, CombatCapabilityIds.DamageDealt);
        Apply(result.DamageReceived, CombatCapabilityIds.DamageReceived);
        Apply(result.RangedHits, CombatCapabilityIds.RangedHits);
        Apply(result.Accuracy, CombatCapabilityIds.Accuracy);
        Apply(result.MeleeSwings, CombatCapabilityIds.MeleeSwings);
        Apply(result.MeleeHits, CombatCapabilityIds.MeleeHits);
        Apply(result.EnemiesKilled, CombatCapabilityIds.EnemiesKilled);
        Apply(result.PlayerDeaths, CombatCapabilityIds.PlayerDeaths);
        Apply(result.Ownership, CombatCapabilityIds.Ownership);
        Apply(result.EnemyIdentity, CombatCapabilityIds.EnemyIdentity);
        Apply(result.EnemyFamily, CombatCapabilityIds.EnemyFamily);
        Apply(result.Cause, CombatCapabilityIds.Cause);
        Apply(result.WeaponIdentity, CombatCapabilityIds.WeaponIdentity);
        Apply(result.AmmunitionIdentity, CombatCapabilityIds.AmmunitionIdentity);
        Apply(result.DamageOverTime, CombatCapabilityIds.DamageOverTime);
        Apply(result.Headshots, CombatCapabilityIds.Headshots);
        Apply(result.HeadshotFinalBlows, CombatCapabilityIds.HeadshotFinalBlows);
        Apply(result.KillsByYou, CombatCapabilityIds.KillsByYou);
        Apply(result.ObservedWorldDeaths, CombatCapabilityIds.ObservedWorldDeaths);
        return result;

        void Apply(MetricAvailability value, string id)
        {
            var state = ReadCapabilityState(current, id, AdapterCapabilityState.DisabledIncompatible);
            value.State = allowUninitializedFallback
                ? CombatStatisticsReducer.ResolveCurrentAvailability(aggregate, value, state)
                : CombatStatisticsReducer.RestrictAvailability(value, state);
        }
    }

    private static EquipmentMetricCapabilities ApplyCurrentEquipmentCapabilityStates(
        EquipmentStatisticsAggregate aggregate,
        IReadOnlyList<CapabilityRecord> current,
        bool allowUninitializedFallback)
    {
        var result = EquipmentStatisticsReducer.CloneCapabilities(aggregate.Capabilities);
        Apply(result.EquipmentSlots, EquipmentCapabilityIds.EquipmentSlots);
        Apply(result.SelectedWeapon, EquipmentCapabilityIds.SelectedWeapon);
        Apply(result.AttachmentMetadata, EquipmentCapabilityIds.AttachmentMetadata);
        Apply(result.DirectTotems, EquipmentCapabilityIds.DirectTotems);
        Apply(result.ToteContents, EquipmentCapabilityIds.ToteContents);
        Apply(result.ToteActivation, EquipmentCapabilityIds.ToteActivation);
        return result;
        void Apply(MetricAvailability value, string id)
        {
            var capability = current.FirstOrDefault(candidate =>
                string.Equals(candidate.AdapterId, id, StringComparison.Ordinal));
            EquipmentStatisticsReducer.ApplyCurrentAvailability(
                aggregate,
                value,
                capability?.State ?? AdapterCapabilityState.DisabledIncompatible,
                capability?.Detail,
                allowUninitializedFallback);
        }
    }

    private static void ApplyCurrentContainerCapability(
        ContainerStatisticsAggregate aggregate,
        IReadOnlyList<CapabilityRecord> current,
        bool allowUninitializedFallback)
    {
        if (!allowUninitializedFallback) return;
        var capability = current.FirstOrDefault(candidate => string.Equals(
            candidate.AdapterId,
            ContainerCapabilityIds.UniqueContainersLooted,
            StringComparison.Ordinal));
        ContainerStatisticsReducer.ApplyCurrentAvailability(
            aggregate,
            capability?.State ?? AdapterCapabilityState.DisabledIncompatible,
            capability?.Detail);
    }

    private static AdapterCapabilityState ResolveAvailability(
        WeaponStatisticsAggregate aggregate,
        MetricAvailability recorded,
        AdapterCapabilityState current,
        bool allowUninitializedFallback) => allowUninitializedFallback
            ? WeaponStatisticsReducer.ResolveCurrentAvailability(aggregate, recorded, current)
            : WeaponStatisticsReducer.RestrictAvailability(recorded, current);

    private static void AppendRecordPair(
        StringBuilder builder,
        string scope,
        string mapId,
        string mapName,
        RunOutcome outcome,
        DurationRecordPair pair)
    {
        AppendRecord(builder, scope, mapId, mapName, outcome, "shortest", pair.Shortest);
        AppendRecord(builder, scope, mapId, mapName, outcome, "longest", pair.Longest);
    }

    private static void AppendRecord(
        StringBuilder builder,
        string scope,
        string mapId,
        string mapName,
        RunOutcome outcome,
        string recordKind,
        DurationRecordReference? record)
    {
        if (record == null)
        {
            return;
        }

        builder.Append(scope).Append(',')
            .Append(Csv(mapId)).Append(',')
            .Append(Csv(mapName)).Append(',')
            .Append(outcome).Append(',')
            .Append(recordKind).Append(',')
            .Append(Csv(record.RunId)).Append(',')
            .Append(record.ActiveDurationSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
            .Append(Csv(record.StartedUtc.ToString("O", CultureInfo.InvariantCulture))).AppendLine();
    }

    private static void AppendTotalsHeader(StringBuilder builder, string prefix)
    {
        builder.Append(prefix).Append(",activation_count,actual_hp_restored");
        foreach (var unit in AmountUnits)
        {
            builder.Append(',').Append(GetAmountColumnName(unit));
        }

        builder.AppendLine();
    }

    private static void AppendTotals(StringBuilder builder, AggregateTotals totals)
    {
        builder.Append(totals.ActivationCount.ToString(CultureInfo.InvariantCulture))
            .Append(',')
            .Append(totals.ActualHealthRestored.ToString("R", CultureInfo.InvariantCulture));
        foreach (var unit in AmountUnits)
        {
            builder.Append(',').Append(ReadAmount(totals, unit).ToString("R", CultureInfo.InvariantCulture));
        }

        builder.AppendLine();
    }

    private static double ReadAmount(AggregateTotals totals, string unit) =>
        totals.AmountsByUnit.TryGetValue(unit, out var value) ? value : 0;

    private static AggregateTotals CloneTotals(AggregateTotals source) => new()
    {
        ActivationCount = source.ActivationCount,
        ActualHealthRestored = source.ActualHealthRestored,
        AmountsByUnit = source.AmountsByUnit.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal)
    };

    private static RunAggregateTotals CloneRunTotals(RunAggregateTotals source) => new()
    {
        TotalRuns = source.TotalRuns,
        Outcomes = source.Outcomes.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
        PhysicalDistance = source.PhysicalDistance,
        TeleportDistance = source.TeleportDistance,
        TransitionExcludedDistance = source.TransitionExcludedDistance,
        RouteAwareHistoryUnavailable = source.RouteAwareHistoryUnavailable,
        ItemStatistics = ItemStatisticsAggregateReducer.Clone(source.ItemStatistics),
        WeaponStatistics = WeaponStatisticsReducer.Clone(source.WeaponStatistics),
        CombatStatistics = CombatStatisticsReducer.Clone(source.CombatStatistics),
        EquipmentStatistics = EquipmentStatisticsReducer.Clone(source.EquipmentStatistics),
        ContainerStatistics = ContainerStatisticsReducer.Clone(source.ContainerStatistics),
        Economy = EconomyStatisticsReducer.Clone(source.Economy),
        Maps = source.Maps.ToDictionary(
            entry => entry.Key,
            entry => new MapRunAggregate
            {
                MapId = entry.Value.MapId,
                DisplayName = entry.Value.DisplayName,
                IsKnown = entry.Value.IsKnown,
                TotalRuns = entry.Value.TotalRuns,
                Outcomes = entry.Value.Outcomes.ToDictionary(outcome => outcome.Key, outcome => outcome.Value, StringComparer.Ordinal),
                PhysicalDistance = entry.Value.PhysicalDistance,
                TeleportDistance = entry.Value.TeleportDistance,
                WeaponStatistics = WeaponStatisticsReducer.Clone(entry.Value.WeaponStatistics),
                CombatStatistics = CombatStatisticsReducer.Clone(entry.Value.CombatStatistics),
                EquipmentStatistics = EquipmentStatisticsReducer.Clone(entry.Value.EquipmentStatistics),
                ContainerStatistics = ContainerStatisticsReducer.Clone(entry.Value.ContainerStatistics),
                ItemStatistics = ItemStatisticsAggregateReducer.Clone(entry.Value.ItemStatistics),
                Economy = EconomyStatisticsReducer.Clone(entry.Value.Economy)
            },
            StringComparer.Ordinal),
        RouteMaps = source.RouteMaps.ToDictionary(
            entry => entry.Key,
            entry => CloneRouteMap(entry.Value),
            StringComparer.Ordinal)
    };

    private static RunSummary CloneRun(RunSummary source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        RunId = source.RunId,
        SaveGenerationId = source.SaveGenerationId,
        NativeRaidId = source.NativeRaidId,
        MapId = source.MapId,
        MapDisplayName = source.MapDisplayName,
        MapKnown = source.MapKnown,
        StartedUtc = source.StartedUtc,
        EndedUtc = source.EndedUtc,
        ActiveDurationSeconds = source.ActiveDurationSeconds,
        WallClockDurationSeconds = source.WallClockDurationSeconds,
        Outcome = source.Outcome,
        PhysicalDistance = source.PhysicalDistance,
        TeleportDistance = source.TeleportDistance,
        IntegrityTags = source.IntegrityTags,
        RecordEligible = source.RecordEligible,
        GameVersion = source.GameVersion,
        GameBuild = source.GameBuild,
        LifecycleCapability = source.LifecycleCapability,
        LifecycleAdapterVersion = source.LifecycleAdapterVersion,
        MovementCapability = source.MovementCapability,
        MovementAdapterVersion = source.MovementAdapterVersion,
        MapCapability = source.MapCapability,
        MapAdapterVersion = source.MapAdapterVersion,
        WeaponStatistics = WeaponStatisticsReducer.Clone(source.WeaponStatistics),
        CombatStatistics = CombatStatisticsReducer.Clone(source.CombatStatistics),
        EquipmentStatistics = EquipmentStatisticsReducer.Clone(source.EquipmentStatistics),
        ContainerStatistics = ContainerStatisticsReducer.Clone(source.ContainerStatistics),
        StartingMapId = source.StartingMapId,
        StartingMapDisplayName = source.StartingMapDisplayName,
        StartingMapKnown = source.StartingMapKnown,
        EndingMapId = source.EndingMapId,
        EndingMapDisplayName = source.EndingMapDisplayName,
        EndingMapKnown = source.EndingMapKnown,
        RouteSignature = source.RouteSignature,
        Segments = source.Segments.Select(RouteStatisticsReducer.CloneSegment).ToList(),
        TransitionExcludedDistance = source.TransitionExcludedDistance,
        RouteCapabilities = RouteStatisticsReducer.CloneCapabilities(source.RouteCapabilities),
        HistoricalRouteUnavailable = source.HistoricalRouteUnavailable,
        RouteWasRepairedFromInvalidState = source.RouteWasRepairedFromInvalidState,
        SegmentEventAssociations = source.SegmentEventAssociations.Select(RouteStatisticsReducer.CloneAssociation).ToList(),
        ItemStatistics = ItemStatisticsAggregateReducer.Clone(source.ItemStatistics),
        Economy = EconomyStatisticsReducer.Clone(source.Economy),
        HistoricalEventAttributionIncomplete = source.HistoricalEventAttributionIncomplete,
        HistoricalEventAttributionProvenance = source.HistoricalEventAttributionProvenance
    };

    private static RouteAwareMapAggregate CloneRouteMap(RouteAwareMapAggregate source) => new()
    {
        MapId = source.MapId,
        DisplayName = source.DisplayName,
        IsKnown = source.IsKnown,
        RunsVisited = source.RunsVisited,
        SegmentVisits = source.SegmentVisits,
        ActiveDurationSeconds = source.ActiveDurationSeconds,
        PhysicalDistance = source.PhysicalDistance,
        TeleportDistance = source.TeleportDistance,
        TransitionExcludedDistance = source.TransitionExcludedDistance,
        ItemStatistics = ItemStatisticsAggregateReducer.Clone(source.ItemStatistics),
        WeaponStatistics = WeaponStatisticsReducer.Clone(source.WeaponStatistics),
        CombatStatistics = CombatStatisticsReducer.Clone(source.CombatStatistics),
        EquipmentStatistics = EquipmentStatisticsReducer.Clone(source.EquipmentStatistics),
        ContainerStatistics = ContainerStatisticsReducer.Clone(source.ContainerStatistics),
        Economy = EconomyStatisticsReducer.Clone(source.Economy),
        HistoricalUnavailable = source.HistoricalUnavailable,
        WasRepairedFromInvalidState = source.WasRepairedFromInvalidState
    };

    private static CapabilityRecord CloneCapability(CapabilityRecord source) => new()
    {
        AdapterId = source.AdapterId,
        State = source.State,
        Version = source.Version,
        Detail = source.Detail
    };

    private sealed class EconomyScope
    {
        public EconomyScope(string scope, string scopeId, string runId, string segmentId, string mapId, string mapDisplayName, EconomyStatisticsAggregate economy)
        { Scope = scope; ScopeId = scopeId; RunId = runId; SegmentId = segmentId; MapId = mapId; MapDisplayName = mapDisplayName; Economy = economy; }
        public string Scope { get; }
        public string ScopeId { get; }
        public string RunId { get; }
        public string SegmentId { get; }
        public string MapId { get; }
        public string MapDisplayName { get; }
        public EconomyStatisticsAggregate Economy { get; }
    }

    private static RunDurationRecords CloneRunRecords(RunDurationRecords source) => new()
    {
        Extraction = ClonePair(source.Extraction),
        Death = ClonePair(source.Death),
        Maps = source.Maps.ToDictionary(
            entry => entry.Key,
            entry => new MapRunDurationRecords
            {
                MapId = entry.Value.MapId,
                DisplayName = entry.Value.DisplayName,
                Extraction = ClonePair(entry.Value.Extraction),
                Death = ClonePair(entry.Value.Death)
            },
            StringComparer.Ordinal)
    };

    private static DurationRecordPair ClonePair(DurationRecordPair source) => new()
    {
        Shortest = CloneRecord(source.Shortest),
        Longest = CloneRecord(source.Longest)
    };

    private static DurationRecordReference? CloneRecord(DurationRecordReference? source) => source == null
        ? null
        : new DurationRecordReference
        {
            RunId = source.RunId,
            ActiveDurationSeconds = source.ActiveDurationSeconds,
            StartedUtc = source.StartedUtc,
            MapId = source.MapId,
            MapDisplayName = source.MapDisplayName
        };

    private static long ReadOutcome(Dictionary<string, long> outcomes, RunOutcome outcome) =>
        outcomes.TryGetValue(outcome.ToString(), out var value) ? value : 0;

    private static string Csv(string value)
    {
        value ??= string.Empty;
        return value.IndexOfAny(CsvSpecialCharacters) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character) && index > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static string GetAmountColumnName(string unit) =>
        string.Equals(unit, "UnknownAmount", StringComparison.Ordinal)
            ? "unknown_amount"
            : $"{ToSnakeCase(unit)}_amount";

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
