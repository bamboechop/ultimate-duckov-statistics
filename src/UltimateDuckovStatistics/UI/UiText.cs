using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.UI;

internal static class UiText
{
    private static Func<string, string?>? nativeResolver;

    private static readonly Dictionary<string, string> English =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ui.title"] = "Ultimate Duckov Statistics",
            ["ui.menu_entry"] = "Statistics",
            ["ui.close"] = "Close",
            ["ui.overview"] = "Overview",
            ["ui.items"] = "Items",
            ["ui.item_use"] = "Item Use",
            ["ui.runs"] = "Runs",
            ["ui.records"] = "Records",
            ["ui.combat"] = "Combat",
            ["ui.equipment"] = "Equipment",
            ["ui.economy"] = "Economy",
            ["ui.crafting"] = "Crafting",
            ["ui.diagnostics"] = "Diagnostics",
            ["ui.total_uses"] = "Successful raid uses",
            ["ui.actual_hp"] = "Actual HP restored",
            ["ui.save_slot"] = "Save slot",
            ["ui.generation"] = "UDS generation",
            ["ui.interrupted_sessions"] = "Interrupted sessions recovered",
            ["ui.total_runs"] = "Runs",
            ["ui.extracted_runs"] = "Extracted",
            ["ui.died_runs"] = "Died",
            ["ui.interrupted_runs"] = "Interrupted",
            ["ui.physical_distance"] = "Physical distance",
            ["ui.teleport_distance"] = "Teleport distance",
            ["ui.route_movement"] = "Teleport / transition-excluded",
            ["ui.segment_event_unavailable"] = "Segment M1-M7 attribution unavailable",
            ["ui.segment_event_partial"] = "Earlier segment attribution is incomplete; shown values are exact known events",
            ["ui.segment_event_capture_supported"] = "Schema-10 current capture remains supported",
            ["ui.firing_actions"] = "Firing actions",
            ["ui.containers_looted"] = "Unique containers opened",
            ["ui.container_history_unavailable"] = "earlier history unavailable",
            ["ui.repaired_unavailable"] = "repaired data; unavailable",
            ["ui.ammunition_consumed"] = "Loaded ammunition units consumed",
            ["ui.projectiles"] = "Projectiles created",
            ["ui.weapon"] = "Weapon",
            ["ui.ammunition"] = "Ammunition",
            ["ui.no_combat"] = "No accepted firing actions recorded for this save generation.",
            ["ui.metric_contract"] = "A firing action is one accepted native firing callback. Dry-fire attempts, actual loaded-ammunition consumption, and completed projectile creation are unavailable on this game contract.",
            ["ui.damage_contract"] = "Damage is measured from actual Health.Hurt HP loss. Accuracy is unique player projectiles that damaged an enemy divided by completed player projectiles; critical hits alone never prove headshots.",
            ["ui.damage_dealt"] = "Main-duck damage dealt",
            ["ui.damage_received"] = "Main-duck damage received",
            ["ui.accuracy"] = "Ranged accuracy",
            ["ui.melee"] = "Melee swings / hits",
            ["ui.kills_by_you"] = "Kills by you",
            ["ui.observed_world_deaths"] = "Observed world deaths",
            ["ui.legacy_unclassified_deaths"] = "Legacy unclassified deaths",
            ["ui.ownership"] = "Observed-death ownership",
            ["ui.historical_ownership_unavailable"] = "Earlier combat ownership is incomplete",
            ["ui.deaths"] = "Player deaths",
            ["ui.headshots"] = "Headshots / final blows",
            ["ui.enemies"] = "Enemies",
            ["ui.summary"] = "Summary",
            ["ui.weapons_ammo"] = "Weapons & Ammo",
            ["ui.incoming_damage"] = "Incoming Damage",
            ["ui.killers"] = "Killers",
            ["ui.integrity"] = "Integrity",
            ["ui.record_status"] = "Records",
            ["ui.record_eligible"] = "Eligible",
            ["ui.record_excluded"] = "Excluded",
            ["ui.reason_interrupted"] = "interrupted run",
            ["ui.reason_lifecycle"] = "lifecycle unsupported",
            ["ui.reason_other"] = "not eligible",
            ["ui.no_runs"] = "No completed runs recorded for this save generation.",
            ["ui.no_records"] = "No eligible extraction or death duration records recorded.",
            ["ui.outcome"] = "Outcome",
            ["ui.map"] = "Map",
            ["ui.route"] = "Route",
            ["ui.show_segments"] = "Show segments",
            ["ui.hide_segments"] = "Hide segments",
            ["ui.active_time"] = "Active time",
            ["ui.wall_time"] = "Wall-clock diagnostic",
            ["ui.shortest"] = "Shortest",
            ["ui.longest"] = "Longest",
            ["ui.extraction_records"] = "Extraction records",
            ["ui.death_records"] = "Death records",
            ["ui.per_map"] = "Per-map totals and records",
            ["ui.per_starting_map"] = "Starting-map complete-run totals and records",
            ["ui.unsupported"] = "Unsupported",
            ["ui.group_totals"] = "Canonical groups",
            ["ui.no_items"] = "No successful raid item uses recorded for this save generation.",
            ["ui.item_name"] = "Item",
            ["ui.effects"] = "Effects",
            ["ui.recent_runs"] = "Recent runs",
            ["ui.group"] = "Group",
            ["ui.activations"] = "Activations",
            ["ui.amount"] = "Amount consumed",
            ["ui.capabilities"] = "Adapter capabilities",
            ["ui.diagnostic_log"] = "Recent bounded diagnostics",
            ["ui.data_settings"] = "Data & settings",
            ["ui.recent_issues"] = "Recent issues",
            ["ui.no_recent_issues"] = "No recent warnings or errors.",
            ["ui.technical_details"] = "Technical details",
            ["ui.tracking_health"] = "Tracking-system health",
            ["ui.working"] = "Working",
            ["ui.limited"] = "Limited",
            ["ui.error"] = "Error",
            ["ui.health_legend"] = "Working: current supported capture is active. Limited: one or more dimensions are unavailable but supported siblings continue. Error: a current failure may prevent affected capture; inspect the issue and Player.log.",
            ["ui.menu_access"] = "Menu access",
            ["ui.main_menu_entry"] = "Main-menu entry",
            ["ui.base_pause_entry"] = "Base pause-menu entry",
            ["ui.hotkey_fallback"] = "Configured hotkey fallback",
            ["ui.not_observed"] = "Not observed this lifecycle",
            ["ui.attached_unverified"] = "Attached; activation not yet observed",
            ["ui.menu_access_limited"] = "Statistics tracking remains healthy. Use the configured hotkey outside raids until every native entry is activated and inspect Recent issues for unavailable paths.",
            ["ui.runtime_issues"] = "Runtime issues",
            ["ui.issue_guidance"] = "If the issue persists, inspect Player.log. Unaffected tracking continues unless its health group reports an error.",
            ["ui.data_path"] = "Data path",
            ["ui.export"] = "Export JSON + CSV",
            ["ui.reset"] = "Reset this UDS profile",
            ["ui.reset_warning"] = "Reset archives the current UDS generation read-only and starts at zero. It cannot be undone from within UDS. Duckov saves are not changed.",
            ["ui.confirm_reset"] = "Confirm reset",
            ["ui.cancel"] = "Cancel",
            ["ui.hotkey"] = "Panel hotkey",
            ["ui.apply"] = "Apply",
            ["ui.previous"] = "Previous",
            ["ui.next"] = "Next",
            ["ui.page"] = "Page",
            ["ui.more_above"] = "More above",
            ["ui.more_below"] = "More below",
            ["ui.hotkey_invalid"] = "Unknown Unity key name; hotkey was not changed.",
            ["ui.hotkey_saved"] = "Panel hotkey saved.",
            ["ui.raid_unavailable"] = "Statistics are available outside raids.",
            ["ui.profile_unavailable"] = "Statistics are unavailable until the active UDS save generation is known exactly.",
            ["ui.operation_busy"] = "Another statistics operation is already in progress.",
            ["ui.export_complete"] = "Export complete",
            ["ui.export_failed"] = "Export failed; see Diagnostics and Player.log.",
            ["ui.reset_complete"] = "UDS profile reset; prior generation archived read-only.",
            ["ui.reset_pending"] = "Reset is still pending safely; completion has not been reported. See Diagnostics and Player.log.",
            ["ui.reset_failed"] = "Reset failed before it was queued; the existing profile remains active and no statistics were removed. Duckov save data was not changed. See Diagnostics and Player.log.",
            ["ui.export_path_copied"] = "Export complete; the folder path was copied to the clipboard.",
            ["ui.integrity_note"] = "Run time, weapon, and combat tracking exclude pause/loading and non-raid contexts. Integrity-flagged and interrupted runs remain visible; only eligible runs enter default duration records.",
            ["ui.equipment_contract"] = "Equipment time uses monotonic active raid time. Direct totem and tote presence are tracked separately; tote activation remains unavailable until gameplay proves it.",
            ["ui.open_hint"] = "Press the configured hotkey outside raids to show or hide this panel.",
            ["ui.economy_contract"] = "Money and physical Cash are independent currencies. Gross inflow is not profit, current balance, or net worth. Unknown adjustments retain exact amount and direction without inventing a reason.",
            ["ui.holdings_contract"] = "Current holdings are direct observations of Duckov Money and top-level owned Cash. They are never reconstructed from currency flows. Liquid wealth is a checked Money + Cash sum only while both observations are current.",
            ["ui.current_holdings"] = "Current economy holdings",
            ["ui.money_holding"] = "Money",
            ["ui.cash_holding"] = "Owned Cash",
            ["ui.liquid_wealth"] = "Liquid wealth",
            ["ui.currency_flows"] = "Currency flows since M9 tracking",
            ["ui.last_observed"] = "Last observed",
            ["ui.current"] = "Current",
            ["ui.unavailable"] = "Unavailable",
            ["ui.pre_m15_unavailable"] = "earlier holdings unavailable; not reconstructed",
            ["ui.gross_inflow"] = "Gross inflow",
            ["ui.gross_outflow"] = "Gross outflow",
            ["ui.net_flow"] = "Net flow",
            ["ui.sources"] = "Sources",
            ["ui.contexts"] = "Contexts",
            ["ui.raid_cash"] = "Raid Cash",
            ["ui.acquired"] = "Acquired",
            ["ui.secured"] = "Secured",
            ["ui.lost"] = "Lost",
            ["ui.unresolved"] = "Unresolved",
            ["ui.pre_m9_unavailable"] = "earlier economy history unavailable",
            ["ui.no_m9_flows"] = "no recorded M9 flow",
            ["ui.scope_capture_partly_unavailable"] = "capture unavailable for part of this scope",
            ["ui.scope_capture_unavailable"] = "capture unavailable for this scope",
            ["ui.current_capture_unavailable"] = "current capture unavailable",
            ["ui.calendar_days_advanced"] = "Calendar days advanced",
            ["ui.observed_world_time"] = "Observed Duckov world-clock advancement",
            ["ui.completed_sleep_sessions"] = "Completed sleep sessions",
            ["ui.sleep_advanced_time"] = "Time advanced through sleep",
            ["ui.pre_m12_unavailable"] = "earlier world-time and sleep history unavailable",
            ["ui.world_time_capture_incomplete"] = "capture incomplete",
            ["ui.world_time_contract"] = "Counts proven forward Duckov world-clock movement, including automatic boot/time-target jumps, exact sleep, and other native fast-forward. It is not real-world play time, active raid time, loading time, or wall-clock time.",
            ["ui.crafting_capture_incomplete"] = "capture incomplete",
            ["ui.crafting_actions"] = "Successful crafting actions",
            ["ui.crafting_quantity"] = "Produced item quantity",
            ["ui.crafting_recipe"] = "Recipe",
            ["ui.crafting_batch"] = "Declared batch",
            ["ui.crafting_resources"] = "Most used crafting resources",
            ["ui.crafting_outputs"] = "Most crafted items",
            ["ui.crafting_resource"] = "Declared resource cost",
            ["ui.crafting_currency"] = "Declared currency charged",
            ["ui.crafting_currency_actions"] = "Currency-charged crafting actions",
            ["ui.no_crafting_resources"] = "No proven crafting-resource consumption recorded for this save generation.",
            ["ui.no_crafting"] = "No proven crafting completions recorded for this save generation.",
            ["ui.pre_m13_unavailable"] = "earlier crafted-item history unavailable",
            ["ui.pre_m16_resources_unavailable"] = "earlier crafting resource-cost history unavailable",
            ["ui.pre_m16_currency_unavailable"] = "earlier crafting currency-cost history unavailable",
            ["ui.crafting_contract"] = "One action is one correlated completion of native output delivery before downstream crafting callbacks. Produced quantity and item/currency costs are immutable formula declarations captured when Craft starts and published only after the preceding native Pay succeeds and delivery completes. Attempts, failed payment, inventory deltas, holdings, historical action counts, current recipe metadata, and Money/Cash split inference are excluded. Totals are save-generation lifetime only; workstation and run/map attribution are unavailable.",
            ["ui.weapon_equipment"] = "Weapons by equipped character slot and nested slot",
            ["ui.armor_and_gear"] = "Armor, gear & other native slots",
            ["ui.loadouts"] = "Loadouts",
            ["ui.weapons"] = "Weapons",
            ["ui.totems"] = "Totems",
            ["ui.recurring_loadouts"] = "Recurring loadouts (at least two completed runs)",
            ["ui.recent_run_loadouts"] = "Recent run loadouts",
            ["ui.observed_totem_time"] = "Observed totem state time (presence; Unknown is not active effect time)",
            ["ui.proven_active_totem_time"] = "Proven-active totem-set time",
            ["ui.recent_run_economy"] = "Recent run economy",
            ["ui.proven_empty"] = "No"
        };

    public static void ConfigureNativeResolver(Func<string, string?>? resolver) => nativeResolver = resolver;

    internal static IReadOnlyDictionary<string, string> EnglishFallbacks => English;

    public static string Get(string key) => Resolve(key, nativeResolver);

    internal static string Resolve(string key, Func<string, string?>? resolver)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A UI localization key is required.", nameof(key));
        if (resolver != null)
        {
            try
            {
                var localized = resolver(key);
                if (!string.IsNullOrWhiteSpace(localized)
                    && !string.Equals(localized, key, StringComparison.Ordinal))
                {
                    return localized;
                }
            }
            catch
            {
                // A native localization failure must never make the statistics panel unusable.
            }
        }

        return English.TryGetValue(key, out var fallback) ? fallback : key;
    }

    public static string FormatProvenEmpty(string slotDisplayName) =>
        $"{Get("ui.proven_empty")} {(string.IsNullOrWhiteSpace(slotDisplayName) ? "slot item" : slotDisplayName)}";

    public static string FormatMetric(long value, AdapterCapabilityState state) =>
        state == AdapterCapabilityState.DisabledIncompatible
            ? Get("ui.unsupported")
            : value.ToString(CultureInfo.InvariantCulture);

    public static string FormatMetric(double value, AdapterCapabilityState state) =>
        state == AdapterCapabilityState.DisabledIncompatible
            ? Get("ui.unsupported")
            : value.ToString("0.###", CultureInfo.InvariantCulture);

    public static string FormatWorldTimeCount(long value, MetricAvailability availability)
    {
        if (availability.State != AdapterCapabilityState.DisabledIncompatible)
            return value.ToString(CultureInfo.InvariantCulture);
        return value == 0
            ? Get("ui.unsupported")
            : $"{value.ToString(CultureInfo.InvariantCulture)} ({Get("ui.world_time_capture_incomplete")})";
    }

    public static string FormatWorldTimeDuration(long ticks, MetricAvailability availability)
    {
        if (availability.State == AdapterCapabilityState.DisabledIncompatible && ticks == 0)
            return Get("ui.unsupported");
        var duration = TimeSpan.FromTicks(ticks);
        var formatted = duration.TotalDays >= 1
            ? $"{(long)duration.TotalDays}d {duration.Hours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        return availability.State == AdapterCapabilityState.DisabledIncompatible
            ? $"{formatted} ({Get("ui.world_time_capture_incomplete")})"
            : formatted;
    }

    public static string FormatCraftingCount(long value, MetricAvailability availability)
    {
        if (availability.State != AdapterCapabilityState.DisabledIncompatible)
            return value.ToString(CultureInfo.InvariantCulture);
        return value == 0
            ? Get("ui.unsupported")
            : $"{value.ToString(CultureInfo.InvariantCulture)} ({Get("ui.crafting_capture_incomplete")})";
    }

    public static string FormatCraftingCount(
        long value,
        MetricAvailability metricAvailability,
        MetricAvailability identityAvailability) =>
        FormatCraftingCount(
            value,
            metricAvailability.State >= identityAvailability.State
                ? metricAvailability
                : identityAvailability);

    public static string FormatHolding(
        EconomyHoldingObservation observation,
        MetricAvailability capability)
    {
        if (observation == null) return Get("ui.unavailable");
        if (capability.State == AdapterCapabilityState.DisabledIncompatible)
            return $"{Get("ui.unavailable")} ({Get("ui.unsupported").ToLowerInvariant()})";
        if (observation.State == EconomyHoldingObservationState.Unavailable || !observation.Value.HasValue)
            return Get("ui.unavailable");
        var value = observation.Value.Value.ToString(CultureInfo.InvariantCulture);
        if (observation.State == EconomyHoldingObservationState.Current)
            return $"{value} ({Get("ui.current").ToLowerInvariant()})";
        var timestamp = observation.ObservedUtc.HasValue
            ? observation.ObservedUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd - HH:mm:ss", CultureInfo.InvariantCulture)
            : Get("ui.unavailable");
        return $"{value} ({Get("ui.last_observed").ToLowerInvariant()} {timestamp})";
    }

    public static string FormatEconomyCompact(
        EconomyStatisticsAggregate economy,
        EconomyMetricCapabilities? currentCapabilities = null)
    {
        if (economy == null) return Get("ui.unsupported");
        string Part(CurrencyKind kind, MetricAvailability availability, MetricAvailability? currentAvailability)
        {
            if (!economy.Currencies.TryGetValue(kind.ToString(), out var row))
            {
                if (economy.HistoricalUnavailable)
                    return $"{kind} {Get("ui.no_m9_flows")}";
                return $"{kind} {FormatEconomyValue(0, availability, currentAvailability)}";
            }
            var totals = $"{kind} +{row.Totals.GrossInflow.ToString(CultureInfo.InvariantCulture)}"
                         + $"/-{row.Totals.GrossOutflow.ToString(CultureInfo.InvariantCulture)}"
                         + $" net {row.Totals.NetFlow.ToString(CultureInfo.InvariantCulture)}";
            return availability.State == AdapterCapabilityState.DisabledIncompatible
                ? $"{totals} ({FormatUnavailableScope(currentAvailability)})"
                : totals;
        }
        var result = $"{Part(CurrencyKind.Money, economy.Capabilities.MoneyAmountDirection, currentCapabilities?.MoneyAmountDirection)}; "
                     + Part(CurrencyKind.Cash, economy.Capabilities.CashAmountDirection, currentCapabilities?.CashAmountDirection);
        return economy.HistoricalUnavailable ? $"{result} ({Get("ui.pre_m9_unavailable")})" : result;
    }

    public static string FormatCashOutcome(
        EconomyStatisticsAggregate economy,
        EconomyMetricCapabilities? currentCapabilities = null) =>
        $"{Get("ui.raid_cash")} {Get("ui.acquired").ToLowerInvariant()} {FormatEconomyValue(economy.CashRaidOutcomes.Acquired, economy.Capabilities.CashExternalAcquisition, currentCapabilities?.CashExternalAcquisition)}, "
        + $"{Get("ui.secured").ToLowerInvariant()} {FormatEconomyValue(economy.CashRaidOutcomes.Secured, economy.Capabilities.CashTerminalOutcomes, currentCapabilities?.CashTerminalOutcomes)}, "
        + $"{Get("ui.lost").ToLowerInvariant()} {FormatEconomyValue(economy.CashRaidOutcomes.Lost, economy.Capabilities.CashTerminalOutcomes, currentCapabilities?.CashTerminalOutcomes)}, "
        + $"{Get("ui.unresolved").ToLowerInvariant()} {FormatEconomyValue(economy.CashRaidOutcomes.Unresolved, economy.Capabilities.CashTerminalOutcomes, currentCapabilities?.CashTerminalOutcomes)}";

    internal static string FormatEconomyValue(
        long value,
        MetricAvailability scopeAvailability,
        MetricAvailability? currentAvailability = null) =>
        scopeAvailability.State == AdapterCapabilityState.DisabledIncompatible
            ? value == 0 ? Get("ui.unsupported") : $"{value.ToString(CultureInfo.InvariantCulture)} ({FormatUnavailableScope(currentAvailability)})"
            : value.ToString(CultureInfo.InvariantCulture);

    private static string FormatUnavailableScope(MetricAvailability? currentAvailability) =>
        currentAvailability == null
            ? Get("ui.scope_capture_unavailable")
            : currentAvailability.State == AdapterCapabilityState.DisabledIncompatible
                ? Get("ui.current_capture_unavailable")
                : Get("ui.scope_capture_partly_unavailable");

    public static string FormatRoute(RunSummary run)
    {
        if (run == null) throw new ArgumentNullException(nameof(run));
        if (run.HistoricalRouteUnavailable) return "Route unavailable (pre-M8)";
        if (!HasAvailableSegments(run))
            return "Route unavailable";
        return string.Join(" → ", run.Segments.OrderBy(value => value.SegmentIndex).Select(value => value.MapDisplayName));
    }

    public static bool HasAvailableSegments(RunSummary run)
    {
        if (run == null) throw new ArgumentNullException(nameof(run));
        return !run.HistoricalRouteUnavailable
               && run.RouteCapabilities.OrderedRoute.State == AdapterCapabilityState.Supported
               && run.RouteCapabilities.Segments.State == AdapterCapabilityState.Supported
               && run.Segments.Count > 0;
    }

    public static bool HasAvailableEventAttribution(RunSummary run)
    {
        if (run == null) throw new ArgumentNullException(nameof(run));
        return HasAvailableSegments(run)
               && run.RouteCapabilities.EventAttribution.State == AdapterCapabilityState.Supported;
    }

    public static bool HasKnownEventAttribution(RunSummary run)
    {
        if (run == null) throw new ArgumentNullException(nameof(run));
        return HasAvailableEventAttribution(run)
               || (HasAvailableSegments(run) && run.HistoricalEventAttributionIncomplete);
    }
}
