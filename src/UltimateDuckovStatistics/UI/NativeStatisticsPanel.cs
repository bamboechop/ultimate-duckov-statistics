using System.Globalization;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.UI;

internal sealed class NativeStatisticsPanel
{
    private const int WindowId = 9048127;
    private readonly NativeProfileCoordinator coordinator;
    private readonly AtomicJsonStore<UserSettings> settingsStore = new();
    private readonly string settingsPath;
    private Rect windowRect = new(80, 60, 880, 650);
    private Vector2 itemScroll;
    private Vector2 diagnosticScroll;
    private Vector2 runScroll;
    private Vector2 recordScroll;
    private Vector2 combatScroll;
    private Vector2 equipmentScroll;
    private Vector2 economyScroll;
    private Vector2 craftingScroll;
    private PanelTab tab;
    private bool visible;
    private bool confirmReset;
    private string status = string.Empty;
    private DateTime statusExpiresUtc;
    private KeyCode hotkey = KeyCode.F8;
    private string hotkeyInput = "F8";
    private readonly HashSet<string> expandedRunIds = new(StringComparer.Ordinal);

    public NativeStatisticsPanel(NativeProfileCoordinator coordinator)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        settingsPath = Path.Combine(coordinator.DataRoot, "settings.json");
        LoadSettings();
    }

    public void Tick()
    {
        if (!Input.GetKeyDown(hotkey))
        {
            return;
        }

        if (NativeRaidContext.IsRaidMap())
        {
            visible = false;
            ShowStatus(UiText.Get("ui.raid_unavailable"));
            return;
        }

        visible = !visible;
        confirmReset = false;
    }

    public void Draw()
    {
        if (statusExpiresUtc > DateTime.UtcNow)
        {
            var width = Mathf.Min(520, Screen.width - 40);
            GUI.Box(new Rect((Screen.width - width) / 2f, 20, width, 42), status);
        }

        if (!visible)
        {
            return;
        }

        if (NativeRaidContext.IsRaidMap())
        {
            visible = false;
            ShowStatus(UiText.Get("ui.raid_unavailable"));
            return;
        }

        windowRect.width = Mathf.Min(880, Screen.width - 30);
        windowRect.height = Mathf.Min(650, Screen.height - 30);
        windowRect.x = Mathf.Clamp(windowRect.x, 0, Mathf.Max(0, Screen.width - windowRect.width));
        windowRect.y = Mathf.Clamp(windowRect.y, 0, Mathf.Max(0, Screen.height - windowRect.height));
        windowRect = GUI.Window(WindowId, windowRect, DrawWindow, UiText.Get("ui.title"));
    }

    private void DrawWindow(int windowId)
    {
        GUILayout.BeginVertical();
        GUILayout.Label(UiText.Get("ui.integrity_note"));
        GUILayout.BeginHorizontal();
        DrawTabButton(PanelTab.Overview, "ui.overview");
        DrawTabButton(PanelTab.Runs, "ui.runs");
        DrawTabButton(PanelTab.Records, "ui.records");
        DrawTabButton(PanelTab.Combat, "ui.combat");
        DrawTabButton(PanelTab.Equipment, "ui.equipment");
        DrawTabButton(PanelTab.Economy, "ui.economy");
        DrawTabButton(PanelTab.Crafting, "ui.crafting");
        DrawTabButton(PanelTab.Items, "ui.items");
        DrawTabButton(PanelTab.Diagnostics, "ui.diagnostics");
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(UiText.Get("ui.close"), GUILayout.Width(90)))
        {
            visible = false;
        }

        GUILayout.EndHorizontal();

        switch (tab)
        {
            case PanelTab.Overview:
                DrawOverview();
                break;
            case PanelTab.Items:
                DrawItems();
                break;
            case PanelTab.Runs:
                DrawRuns();
                break;
            case PanelTab.Records:
                DrawRecords();
                break;
            case PanelTab.Combat:
                DrawCombat();
                break;
            case PanelTab.Equipment:
                DrawEquipment();
                break;
            case PanelTab.Economy:
                DrawEconomy();
                break;
            case PanelTab.Crafting:
                DrawCrafting();
                break;
            case PanelTab.Diagnostics:
                DrawDiagnostics();
                break;
        }

        GUILayout.FlexibleSpace();
        DrawActions();
        GUILayout.EndVertical();
        GUI.DragWindow(new Rect(0, 0, windowRect.width - 100, 24));
    }

    private void DrawOverview()
    {
        var profile = coordinator.Current;
        if (profile == null)
        {
            return;
        }

        GUILayout.Space(12);
        GUILayout.Label($"{UiText.Get("ui.save_slot")}: {profile.Slot.ToString(CultureInfo.InvariantCulture)}");
        GUILayout.Label($"{UiText.Get("ui.generation")}: {profile.GenerationId}");
        GUILayout.Label($"{UiText.Get("ui.total_uses")}: {profile.Statistics.Overall.ActivationCount.ToString(CultureInfo.InvariantCulture)}");
        GUILayout.Label($"{UiText.Get("ui.actual_hp")}: {FormatHealing(profile.Statistics.Overall)}");
        GUILayout.Label($"{UiText.Get("ui.amount")}: {FormatAmounts(profile.Statistics.Overall)}");
        GUILayout.Label($"{UiText.Get("ui.interrupted_sessions")}: {profile.InterruptedSessionCount.ToString(CultureInfo.InvariantCulture)}");
        var runs = RunStatisticsViewModelFactory.Create(profile);
        GUILayout.Space(8);
        GUILayout.Label(
            $"{UiText.Get("ui.total_runs")}: {runs.TotalRuns.ToString(CultureInfo.InvariantCulture)} " +
            $"({UiText.Get("ui.extracted_runs")}: {runs.ExtractedRuns.ToString(CultureInfo.InvariantCulture)}, " +
            $"{UiText.Get("ui.died_runs")}: {runs.DiedRuns.ToString(CultureInfo.InvariantCulture)}, " +
            $"{UiText.Get("ui.interrupted_runs")}: {runs.InterruptedRuns.ToString(CultureInfo.InvariantCulture)})");
        GUILayout.Label($"{UiText.Get("ui.physical_distance")}: {FormatDistance(runs.PhysicalDistance, runs.MovementSupported)}");
        GUILayout.Label($"{UiText.Get("ui.teleport_distance")}: {FormatDistance(runs.TeleportDistance, runs.MovementSupported)}");
        var containers = ContainerStatisticsViewModelFactory.Create(profile);
        GUILayout.Label(
            $"{UiText.Get("ui.containers_looted")}: {FormatContainers(containers.Lifetime, containers.CurrentCapability)}");
        var combat = WeaponStatisticsViewModelFactory.Create(profile);
        GUILayout.Label(
            $"{UiText.Get("ui.firing_actions")}: "
            + UiText.FormatMetric(combat.Lifetime.Totals.FiringActions, combat.Capabilities.FiringActions.State));
        var holdings = EconomyHoldingsReducer.Project(profile.Statistics.Holdings);
        GUILayout.Space(8);
        GUILayout.Label(UiText.Get("ui.current_holdings"));
        GUILayout.Label($"{UiText.Get("ui.money_holding")}: {UiText.FormatHolding(holdings.Money, holdings.Capabilities.Money)}");
        GUILayout.Label($"{UiText.Get("ui.cash_holding")}: {UiText.FormatHolding(holdings.Cash, holdings.Capabilities.Cash)}");
        GUILayout.Label($"{UiText.Get("ui.liquid_wealth")}: {UiText.FormatHolding(holdings.LiquidWealth, holdings.Capabilities.LiquidWealth)}");
        if (profile.Statistics.Holdings.HistoricalUnavailable)
            GUILayout.Label($"  {UiText.Get("ui.pre_m15_unavailable")}");
        GUILayout.Label($"{UiText.Get("ui.economy")}: {UiText.FormatEconomyCompact(profile.Statistics.Economy, coordinator.CurrentEconomyCapabilities)}");
        var worldTime = profile.Statistics.WorldTime;
        var worldTimeCapabilities = WorldTimeStatisticsReducer.RestrictWithCurrent(
            worldTime.Capabilities,
            coordinator.CurrentWorldTimeCapabilities);
        GUILayout.Space(8);
        GUILayout.Label(UiText.Get("ui.world_time_contract"));
        GUILayout.Label($"{UiText.Get("ui.calendar_days_advanced")}: {UiText.FormatWorldTimeCount(worldTime.CalendarDaysAdvanced, worldTimeCapabilities.CalendarDays)}");
        GUILayout.Label($"{UiText.Get("ui.observed_world_time")}: {UiText.FormatWorldTimeDuration(worldTime.ObservedGameTimeTicks, worldTimeCapabilities.ObservedElapsed)}");
        GUILayout.Label($"{UiText.Get("ui.completed_sleep_sessions")}: {UiText.FormatWorldTimeCount(worldTime.CompletedSleepSessions, worldTimeCapabilities.CompletedSleepSessions)}");
        GUILayout.Label($"{UiText.Get("ui.sleep_advanced_time")}: {UiText.FormatWorldTimeDuration(worldTime.SleepAdvancedTimeTicks, worldTimeCapabilities.SleepAdvancedTime)}");
        if (worldTime.HistoricalUnavailable)
            GUILayout.Label($"  {UiText.Get("ui.pre_m12_unavailable")}");
        var crafting = profile.Statistics.Crafting;
        var craftingCapabilities = CraftingStatisticsReducer.RestrictWithCurrent(
            crafting.Capabilities,
            coordinator.CurrentCraftingCapabilities);
        GUILayout.Space(8);
        GUILayout.Label(
            $"{UiText.Get("ui.crafting_actions")}: "
            + UiText.FormatCraftingCount(crafting.CompletionActions, craftingCapabilities.CompletionActions));
        GUILayout.Label(
            $"{UiText.Get("ui.crafting_quantity")}: "
            + UiText.FormatCraftingCount(crafting.ProducedQuantity, craftingCapabilities.ProducedQuantity));
        GUILayout.Label(
            $"{UiText.Get("ui.crafting_currency")}: "
            + UiText.FormatCraftingCount(crafting.CurrencyCharged, craftingCapabilities.CurrencyCharge));
        if (crafting.HistoricalUnavailable)
            GUILayout.Label($"  {UiText.Get("ui.pre_m13_unavailable")}");
        if (crafting.ResourceHistoryUnavailable)
            GUILayout.Label($"  {UiText.Get("ui.pre_m16_resources_unavailable")}");
        if (crafting.CurrencyHistoryUnavailable)
            GUILayout.Label($"  {UiText.Get("ui.pre_m16_currency_unavailable")}");
        GUILayout.Space(12);
        GUILayout.Label(UiText.Get("ui.group_totals"));
        foreach (var group in profile.Statistics.Groups.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            GUILayout.Label(
                $"{group.Key}: {group.Value.ActivationCount.ToString(CultureInfo.InvariantCulture)} " +
                $"({FormatAmounts(group.Value)}; {FormatHealing(group.Value)} HP)");
        }
    }

    private void DrawItems()
    {
        var profile = coordinator.Current;
        if (profile == null)
        {
            return;
        }

        GUILayout.Space(8);
        if (profile.Statistics.Items.Count == 0)
        {
            GUILayout.Label(UiText.Get("ui.no_items"));
            return;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label(UiText.Get("ui.item_name"), GUILayout.Width(250));
        GUILayout.Label(UiText.Get("ui.group"), GUILayout.Width(170));
        GUILayout.Label(UiText.Get("ui.activations"), GUILayout.Width(90));
        GUILayout.Label(UiText.Get("ui.actual_hp"), GUILayout.Width(120));
        GUILayout.Label(UiText.Get("ui.amount"));
        GUILayout.EndHorizontal();
        itemScroll = GUILayout.BeginScrollView(itemScroll);
        foreach (var item in profile.Statistics.Items.Values
                     .OrderByDescending(item => item.Totals.ActivationCount)
                     .ThenBy(item => item.DisplayName, StringComparer.Ordinal))
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(item.DisplayName, GUILayout.Width(250));
            GUILayout.Label(item.Group.ToString(), GUILayout.Width(170));
            GUILayout.Label(item.Totals.ActivationCount.ToString(CultureInfo.InvariantCulture), GUILayout.Width(90));
            GUILayout.Label(FormatHealing(item.Totals), GUILayout.Width(120));
            GUILayout.Label(FormatAmounts(item.Totals));
            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();
    }

    private void DrawRuns()
    {
        var profile = coordinator.Current;
        if (profile == null)
        {
            return;
        }

        var model = RunStatisticsViewModelFactory.Create(profile);
        var currentEconomyCapabilities = coordinator.CurrentEconomyCapabilities;
        GUILayout.Space(8);
        if (model.Runs.Count == 0)
        {
            GUILayout.Label(UiText.Get("ui.no_runs"));
            return;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label(UiText.Get("ui.outcome"), GUILayout.Width(95));
        GUILayout.Label(UiText.Get("ui.route"), GUILayout.Width(260));
        GUILayout.Label(UiText.Get("ui.active_time"), GUILayout.Width(110));
        GUILayout.Label(UiText.Get("ui.wall_time"), GUILayout.Width(135));
        GUILayout.Label(UiText.Get("ui.physical_distance"), GUILayout.Width(125));
        GUILayout.Label(UiText.Get("ui.route_movement"));
        GUILayout.EndHorizontal();
        runScroll = GUILayout.BeginScrollView(runScroll);
        foreach (var row in model.RunRows)
        {
            var run = row.Run;
            var movementSupported = run.MovementCapability == AdapterCapabilityState.Supported;
            GUILayout.BeginHorizontal();
            GUILayout.Label(run.Outcome.ToString(), GUILayout.Width(95));
            GUILayout.Label(UiText.FormatRoute(run), GUILayout.Width(260));
            GUILayout.Label(FormatDuration(run.ActiveDurationSeconds), GUILayout.Width(110));
            GUILayout.Label(FormatDuration(run.WallClockDurationSeconds), GUILayout.Width(135));
            GUILayout.Label(FormatDistance(run.PhysicalDistance, movementSupported), GUILayout.Width(125));
            GUILayout.Label(
                $"{FormatDistance(run.TeleportDistance, movementSupported)} / "
                + FormatDistance(run.TransitionExcludedDistance, movementSupported));
            GUILayout.EndHorizontal();
            GUILayout.Label(
                $"  {UiText.Get("ui.integrity")}: {row.IntegrityTags}; "
                + $"{UiText.Get("ui.record_status")}: {FormatRecordEligibility(row)}");
            var combat = run.CombatStatistics;
            GUILayout.Label(
                $"  {UiText.Get("ui.damage_dealt")}: {UiText.FormatMetric(combat.Totals.DamageDealt, combat.Capabilities.DamageDealt.State)}; "
                + $"{UiText.Get("ui.damage_received")}: {UiText.FormatMetric(combat.Totals.DamageReceived, combat.Capabilities.DamageReceived.State)}; "
                + $"{UiText.Get("ui.kills_by_you")}: {UiText.FormatMetric(combat.Totals.KillsByYou, combat.Capabilities.KillsByYou.State)}; "
                + $"{UiText.Get("ui.observed_world_deaths")}: {UiText.FormatMetric(combat.Totals.ObservedWorldDeaths, combat.Capabilities.ObservedWorldDeaths.State)}; "
                + $"{UiText.Get("ui.deaths")}: {UiText.FormatMetric(combat.Totals.PlayerDeaths, combat.Capabilities.PlayerDeaths.State)}");
            GUILayout.Label(
                $"  {UiText.Get("ui.economy")}: "
                + $"{UiText.FormatEconomyCompact(run.Economy, currentEconomyCapabilities)}; "
                + UiText.FormatCashOutcome(run.Economy, currentEconomyCapabilities));
            GUILayout.Label(
                $"  {UiText.Get("ui.containers_looted")}: "
                + FormatContainers(run.ContainerStatistics, run.ContainerStatistics.Capabilities.UniqueContainersLooted.State));
            if (UiText.HasAvailableSegments(run))
            {
                if (GUILayout.Button(
                        expandedRunIds.Contains(run.RunId) ? UiText.Get("ui.hide_segments") : UiText.Get("ui.show_segments"),
                        GUILayout.Width(125)))
                {
                    if (!expandedRunIds.Add(run.RunId)) expandedRunIds.Remove(run.RunId);
                }
                if (expandedRunIds.Contains(run.RunId))
                {
                    if (run.HistoricalEventAttributionIncomplete)
                    {
                        GUILayout.Label(
                            $"    {UiText.Get("ui.segment_event_partial")}: {run.HistoricalEventAttributionProvenance}");
                        if (run.RouteCapabilities.CurrentEventAttributionCapture.State == AdapterCapabilityState.Supported)
                            GUILayout.Label($"    {UiText.Get("ui.segment_event_capture_supported")}.");
                    }
                    foreach (var segment in run.Segments.OrderBy(value => value.SegmentIndex))
                    {
                        GUILayout.Label(
                            $"    {segment.SegmentIndex + 1}. {segment.MapDisplayName}: {FormatDuration(segment.ActiveDurationSeconds)}, "
                            + $"physical {segment.PhysicalDistance:0.##} m, teleport {segment.TeleportDistance:0.##} m, "
                            + $"transition-excluded {segment.TransitionExcludedDistance:0.##} m, {segment.ExitReason}");
                        if (UiText.HasKnownEventAttribution(run))
                        {
                            GUILayout.Label(
                                $"       items {segment.ItemStatistics.Overall.ActivationCount}, healing {segment.ItemStatistics.Overall.ActualHealthRestored:0.##} HP, "
                                + $"shots {segment.WeaponStatistics.Totals.FiringActions}, damage {UiText.FormatMetric(segment.CombatStatistics.Totals.DamageDealt, segment.CombatStatistics.Capabilities.DamageDealt.State)}, "
                                + $"kills by you {UiText.FormatMetric(segment.CombatStatistics.Totals.KillsByYou, segment.CombatStatistics.Capabilities.KillsByYou.State)}, observed deaths {UiText.FormatMetric(segment.CombatStatistics.Totals.ObservedWorldDeaths, segment.CombatStatistics.Capabilities.ObservedWorldDeaths.State)}, "
                                + $"containers {segment.ContainerStatistics.UniqueContainersLooted}");
                        }
                        else
                        {
                            GUILayout.Label(
                                $"       {UiText.Get("ui.segment_event_unavailable")}: "
                                + run.RouteCapabilities.EventAttribution.Provenance);
                        }
                    }
                }
            }
            GUILayout.Space(4);
        }

        GUILayout.EndScrollView();
    }

    private void DrawRecords()
    {
        var profile = coordinator.Current;
        if (profile == null)
        {
            return;
        }

        var model = RunStatisticsViewModelFactory.Create(profile);
        var records = model.Records;
        GUILayout.Space(8);
        var hasOverallRecords = records.Extraction.Shortest != null
                                || records.Extraction.Longest != null
                                || records.Death.Shortest != null
                                || records.Death.Longest != null;
        if (!hasOverallRecords && model.Maps.Count == 0)
        {
            GUILayout.Label(UiText.Get("ui.no_records"));
            return;
        }

        recordScroll = GUILayout.BeginScrollView(recordScroll);
        if (hasOverallRecords)
        {
            DrawRecordPair(UiText.Get("ui.extraction_records"), records.Extraction);
            DrawRecordPair(UiText.Get("ui.death_records"), records.Death);
        }
        else
        {
            GUILayout.Label(UiText.Get("ui.no_records"));
        }

        if (model.Maps.Count > 0)
        {
            GUILayout.Space(8);
            GUILayout.Label(UiText.Get("ui.per_starting_map"));
        }

        foreach (var map in model.Maps)
        {
            GUILayout.Label(
                $"{map.DisplayName}: {map.TotalRuns.ToString(CultureInfo.InvariantCulture)} {UiText.Get("ui.total_runs")}, "
                + $"{FormatDistance(map.PhysicalDistance, model.MovementSupported)} {UiText.Get("ui.physical_distance")}, "
                + $"{FormatDistance(map.TeleportDistance, model.MovementSupported)} {UiText.Get("ui.teleport_distance")}");
            if (records.Maps.TryGetValue(map.MapId, out var mapRecords))
            {
                DrawRecordPair($"  {UiText.Get("ui.extraction_records")}", mapRecords.Extraction);
                DrawRecordPair($"  {UiText.Get("ui.death_records")}", mapRecords.Death);
            }
        }

        GUILayout.EndScrollView();
    }

    private void DrawCombat()
    {
        var profile = coordinator.Current;
        if (profile == null)
        {
            return;
        }

        var model = WeaponStatisticsViewModelFactory.Create(profile);
        var damage = CombatStatisticsViewModelFactory.Create(profile);
        GUILayout.Space(8);
        GUILayout.Label(UiText.Get("ui.damage_contract"));
        GUILayout.Label($"{UiText.Get("ui.damage_dealt")}: {UiText.FormatMetric(damage.Lifetime.Totals.DamageDealt, damage.Capabilities.DamageDealt.State)}");
        GUILayout.Label($"{UiText.Get("ui.damage_received")}: {UiText.FormatMetric(damage.Lifetime.Totals.DamageReceived, damage.Capabilities.DamageReceived.State)}");
        GUILayout.Label($"{UiText.Get("ui.accuracy")}: {FormatAccuracy(damage)}");
        GUILayout.Label($"{UiText.Get("ui.melee")}: {UiText.FormatMetric(damage.Lifetime.Totals.MeleeSwings, damage.Capabilities.MeleeSwings.State)} / {UiText.FormatMetric(damage.Lifetime.Totals.MeleeHits, damage.Capabilities.MeleeHits.State)}");
        GUILayout.Label($"{UiText.Get("ui.kills_by_you")}: {UiText.FormatMetric(damage.Lifetime.Totals.KillsByYou, damage.Capabilities.KillsByYou.State)}; {UiText.Get("ui.deaths")}: {UiText.FormatMetric(damage.Lifetime.Totals.PlayerDeaths, damage.Capabilities.PlayerDeaths.State)}");
        GUILayout.Label($"{UiText.Get("ui.observed_world_deaths")}: {UiText.FormatMetric(damage.Lifetime.Totals.ObservedWorldDeaths, damage.Capabilities.ObservedWorldDeaths.State)}");
        if (damage.Lifetime.Totals.LegacyUnclassifiedDeaths > 0 || damage.Lifetime.HistoricalOwnershipUnavailable)
        {
            GUILayout.Label($"{UiText.Get("ui.legacy_unclassified_deaths")}: {damage.Lifetime.Totals.LegacyUnclassifiedDeaths.ToString(CultureInfo.InvariantCulture)}");
            GUILayout.Label($"{UiText.Get("ui.historical_ownership_unavailable")}: {damage.Lifetime.HistoricalOwnershipProvenance}");
        }
        GUILayout.Label($"{UiText.Get("ui.headshots")}: {UiText.FormatMetric(damage.Lifetime.Totals.Headshots, damage.Capabilities.Headshots.State)} / {UiText.FormatMetric(damage.Lifetime.Totals.HeadshotFinalBlows, damage.Capabilities.HeadshotFinalBlows.State)}");
        GUILayout.Space(8);
        GUILayout.Label(UiText.Get("ui.metric_contract"));
        GUILayout.Label(
            $"{UiText.Get("ui.firing_actions")}: "
            + UiText.FormatMetric(model.Lifetime.Totals.FiringActions, model.Capabilities.FiringActions.State));
        GUILayout.Label(
            $"{UiText.Get("ui.ammunition_consumed")}: "
            + UiText.FormatMetric(model.Lifetime.Totals.AmmunitionUnitsConsumed, model.Capabilities.AmmunitionConsumption.State));
        GUILayout.Label(
            $"{UiText.Get("ui.projectiles")}: "
            + UiText.FormatMetric(model.Lifetime.Totals.Projectiles, model.Capabilities.Projectiles.State));

        combatScroll = GUILayout.BeginScrollView(combatScroll);
        if (model.Lifetime.Totals.FiringActions == 0 && damage.Lifetime.Totals.DamageCaused == 0)
        {
            GUILayout.Space(6);
            GUILayout.Label(UiText.Get("ui.no_combat"));
        }

        if (damage.Enemies.Count > 0)
        {
            GUILayout.Space(8);
            GUILayout.Label(UiText.Get("ui.enemies"));
            foreach (var enemy in damage.Enemies.Take(20))
                GUILayout.Label($"{enemy.DisplayName} [{enemy.Id}]: {UiText.FormatMetric(enemy.Totals.DamageCaused, damage.Capabilities.EnemyIdentity.State)} damage, {UiText.FormatMetric(enemy.Totals.KillsByYou, damage.Capabilities.KillsByYou.State)} kills by you, {UiText.FormatMetric(enemy.Totals.ObservedWorldDeaths, damage.Capabilities.ObservedWorldDeaths.State)} observed deaths, {enemy.Totals.LegacyUnclassifiedDeaths.ToString(CultureInfo.InvariantCulture)} legacy unclassified");
        }

        if (damage.Ownership.Count > 0)
        {
            GUILayout.Space(8);
            GUILayout.Label(UiText.Get("ui.ownership"));
            foreach (var ownership in damage.Ownership)
                GUILayout.Label($"{ownership.DisplayName}: {UiText.FormatMetric(ownership.Totals.ObservedWorldDeaths, damage.Capabilities.ObservedWorldDeaths.State)} observed deaths, {UiText.FormatMetric(ownership.Totals.DamageCaused, damage.Capabilities.Ownership.State)} damage");
        }

        if (damage.Killers.Count > 0)
        {
            GUILayout.Space(8);
            GUILayout.Label(UiText.Get("ui.killers"));
            foreach (var killer in damage.Killers.Take(20))
                GUILayout.Label($"{killer.DisplayName} [{killer.Id}]: {killer.Totals.DamageReceived.ToString("0.###", CultureInfo.InvariantCulture)} damage, {killer.Totals.PlayerDeaths.ToString(CultureInfo.InvariantCulture)} deaths");
        }

        if (model.Weapons.Count > 0)
        {
            GUILayout.Space(8);
            GUILayout.Label(UiText.Get("ui.weapon"));
            DrawCombatHeader(UiText.Get("ui.weapon"));
            foreach (var weapon in model.Weapons)
            {
                DrawCombatRow(weapon.DisplayName, weapon.WeaponId, weapon.Totals, model.Capabilities);
            }
        }

        GUILayout.Space(8);
        GUILayout.Label($"Weapon-ammunition pairing: {model.Capabilities.WeaponAmmunitionPairing.State}");
        if (model.Lifetime.HistoricalPairingUnavailable)
            GUILayout.Label($"Historical pairing unavailable: {model.Lifetime.HistoricalPairingProvenance}");
        foreach (var weaponGroup in model.WeaponAmmunitionPairs
                     .GroupBy(value => value.Pair.WeaponId, StringComparer.Ordinal))
        {
            var weapon = weaponGroup.First().Pair;
            GUILayout.Label($"{weapon.WeaponDisplayName} [{weapon.WeaponId}]");
            foreach (var value in weaponGroup)
                GUILayout.Label($"  {value.Pair.AmmunitionDisplayName} [{value.Pair.AmmunitionId}]: "
                    + $"{value.Pair.FiringActions.ToString(CultureInfo.InvariantCulture)} accepted firing actions "
                    + $"({value.PercentageWithinObservedWeaponPairs.ToString("0.##", CultureInfo.InvariantCulture)}% of observed pairs for this weapon)");
        }

        if (model.AmmunitionTypes.Count > 0)
        {
            GUILayout.Space(8);
            GUILayout.Label(UiText.Get("ui.ammunition"));
            DrawCombatHeader(UiText.Get("ui.ammunition"));
            foreach (var ammunition in model.AmmunitionTypes)
            {
                DrawCombatRow(ammunition.DisplayName, ammunition.AmmunitionId, ammunition.Totals, model.Capabilities);
            }
        }

        if (model.Runs.Count > 0)
        {
            GUILayout.Space(8);
            GUILayout.Label(UiText.Get("ui.runs"));
            foreach (var run in model.Runs)
            {
                GUILayout.Label(
                    $"{UiText.FormatRoute(run)} ({run.Outcome}, {run.RunId}): "
                    + $"{UiText.FormatMetric(run.WeaponStatistics.Totals.FiringActions, WeaponStatisticsReducer.RestrictAvailability(run.WeaponStatistics.Capabilities.FiringActions, model.Capabilities.FiringActions.State))} actions, "
                    + $"{UiText.FormatMetric(run.WeaponStatistics.Totals.AmmunitionUnitsConsumed, WeaponStatisticsReducer.RestrictAvailability(run.WeaponStatistics.Capabilities.AmmunitionConsumption, model.Capabilities.AmmunitionConsumption.State))} ammo, "
                    + $"{UiText.FormatMetric(run.WeaponStatistics.Totals.Projectiles, WeaponStatisticsReducer.RestrictAvailability(run.WeaponStatistics.Capabilities.Projectiles, model.Capabilities.Projectiles.State))} projectiles");
            }
        }

        GUILayout.EndScrollView();
    }

    private static void DrawCombatHeader(string identityLabel)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(identityLabel, GUILayout.Width(280));
        GUILayout.Label(UiText.Get("ui.firing_actions"), GUILayout.Width(125));
        GUILayout.Label(UiText.Get("ui.ammunition_consumed"), GUILayout.Width(190));
        GUILayout.Label(UiText.Get("ui.projectiles"));
        GUILayout.EndHorizontal();
    }

    private void DrawEquipment()
    {
        var profile = coordinator.Current;
        if (profile == null) return;
        var model = EquipmentStatisticsViewModelFactory.Create(profile);
        var equipment = model.Lifetime;
        GUILayout.Space(8);
        GUILayout.Label(UiText.Get("ui.equipment_contract"));
        GUILayout.Label($"Slots: {model.Capabilities.EquipmentSlots.State}; selected weapon: {model.Capabilities.SelectedWeapon.State}; attachments: {model.Capabilities.AttachmentMetadata.State}");
        GUILayout.Label($"Direct totems: {model.Capabilities.DirectTotems.State}; tote contents: {model.Capabilities.ToteContents.State}; tote activation: {model.Capabilities.ToteActivation.State}");
        GUILayout.Label($"Character slot state: {model.Capabilities.CharacterSlotState.State}; nested equipped-item slot state: {model.Capabilities.NestedSlotState.State}");
        GUILayout.Label($"Transitions: {equipment.TransitionCount.ToString(CultureInfo.InvariantCulture)}{(equipment.TransitionsTruncated ? " (bounded history truncated)" : string.Empty)}");
        equipmentScroll = GUILayout.BeginScrollView(equipmentScroll);
        GUILayout.Space(6);
        GUILayout.Label("Equipment slot occupied time");
        foreach (var row in equipment.Slots.Values.OrderByDescending(x => x.ActiveDurationSeconds).ThenBy(x => x.Id, StringComparer.Ordinal).Take(30))
            GUILayout.Label($"{row.DisplayName} [{row.Id}]: {FormatDuration(row.ActiveDurationSeconds)}");
        GUILayout.Space(6);
        GUILayout.Label("Equipped item time");
        foreach (var row in equipment.Items.Values.OrderByDescending(x => x.ActiveDurationSeconds).ThenBy(x => x.Id, StringComparer.Ordinal).Take(30))
            GUILayout.Label($"{row.DisplayName} [{row.Id}]: {FormatDuration(row.ActiveDurationSeconds)}");
        GUILayout.Space(6);
        GUILayout.Label(UiText.Get("ui.weapon_equipment"));
        foreach (var weapon in model.Weapons)
        {
            GUILayout.Label($"{weapon.DisplayName} [{weapon.WeaponId}]: {FormatDuration(weapon.TotalEquippedDurationSeconds)} total equipped");
            foreach (var slot in weapon.CharacterSlots)
                GUILayout.Label($"  {slot.SlotDisplayName} [{slot.SlotId}]: {FormatDuration(slot.EquippedDurationSeconds)}");
            foreach (var group in weapon.NestedSlotGroups)
            {
                GUILayout.Label($"  {group.DisplayName}");
                foreach (var row in group.Rows)
                    GUILayout.Label($"    {row.SlotDisplayName} [{row.ParentSlotId}; {row.Path}]: "
                        + (row.State == EquipmentSlotState.Empty
                            ? UiText.FormatProvenEmpty(row.SlotDisplayName)
                            : $"{row.ItemDisplayName} [{row.ItemId}]")
                        + $" — {FormatDuration(row.ActiveDurationSeconds)}");
            }
        }
        GUILayout.Space(6);
        GUILayout.Label(UiText.Get("ui.armor_and_gear"));
        foreach (var slot in model.ArmorAndGearSlots)
        {
            GUILayout.Label($"{slot.SlotDisplayName} [{slot.SlotId}]");
            foreach (var row in slot.Rows)
                GUILayout.Label("  " + (row.State == EquipmentSlotState.Empty
                    ? UiText.FormatProvenEmpty(row.SlotDisplayName)
                    : $"{row.ItemDisplayName} [{row.ItemId}]")
                    + $" — {FormatDuration(row.ActiveDurationSeconds)}");
        }
        GUILayout.Space(6);
        GUILayout.Label("Character equipment slot state time (Empty requires proven native slot membership)");
        if (equipment.HistoricalCharacterSlotStateUnavailable)
            GUILayout.Label($"Historical character-slot state unavailable: {equipment.HistoricalCharacterSlotStateProvenance}");
        foreach (var row in equipment.CharacterSlotStates.Values
                     .OrderBy(value => value.SlotId, StringComparer.Ordinal)
                     .ThenBy(value => value.State)
                     .ThenBy(value => value.ItemId, StringComparer.Ordinal).Take(50))
            GUILayout.Label($"{row.SlotDisplayName} [{row.SlotId}]: "
                + (row.State == EquipmentSlotState.Empty
                    ? "Empty"
                    : $"{row.ItemDisplayName} [{row.ItemId}]")
                + $" — {FormatDuration(row.ActiveDurationSeconds)}");
        GUILayout.Space(6);
        GUILayout.Label("Nested equipped-item slot state time (parent-equipped active-raid time)");
        if (equipment.HistoricalNestedSlotStateUnavailable)
            GUILayout.Label($"Historical nested-slot state unavailable: {equipment.HistoricalNestedSlotStateProvenance}");
        foreach (var row in equipment.NestedSlotStates.Values
                     .Where(value => value.ParentItemKind != EquipmentItemKind.Weapon)
                     .OrderBy(value => value.ParentSlotId, StringComparer.Ordinal)
                     .ThenBy(value => value.ParentItemId, StringComparer.Ordinal)
                     .ThenBy(value => value.Path, StringComparer.Ordinal)
                     .ThenBy(value => value.State).Take(80))
            GUILayout.Label($"{row.ParentItemDisplayName} [{row.ParentSlotId}|{row.ParentItemId}] / "
                + $"{row.SlotDisplayName} [{row.Path}]: "
                + (row.State == EquipmentSlotState.Empty
                    ? UiText.FormatProvenEmpty(row.SlotDisplayName)
                    : $"{row.ItemDisplayName} [{row.ItemId}]")
                + $" — {FormatDuration(row.ActiveDurationSeconds)}");
        GUILayout.Space(6);
        GUILayout.Label("Slotted weapon time");
        foreach (var row in equipment.SlottedWeapons.Values.OrderByDescending(x => x.ActiveDurationSeconds).ThenBy(x => x.Id, StringComparer.Ordinal).Take(20))
            GUILayout.Label($"{row.DisplayName} [{row.Id}]: {FormatDuration(row.ActiveDurationSeconds)}");
        GUILayout.Space(6);
        GUILayout.Label("Selected weapon time");
        foreach (var row in equipment.SelectedWeapons.Values.OrderByDescending(x => x.ActiveDurationSeconds).ThenBy(x => x.Id, StringComparer.Ordinal).Take(20))
            GUILayout.Label($"{row.Id}: {FormatDuration(row.ActiveDurationSeconds)}");
        GUILayout.Space(6);
        GUILayout.Label("Observed totem state time (presence; Unknown is not active effect time)");
        foreach (var row in equipment.TotemStates.Values.OrderByDescending(x => x.ActiveDurationSeconds).ThenBy(x => x.Id, StringComparer.Ordinal).Take(30))
            GUILayout.Label($"{row.DisplayName}: {FormatDuration(row.ActiveDurationSeconds)}");
        GUILayout.Space(6);
        GUILayout.Label("Proven-active totem-set time");
        foreach (var row in equipment.TotemSets.Values.OrderByDescending(x => x.ActiveDurationSeconds).ThenBy(x => x.Id, StringComparer.Ordinal).Take(20))
            GUILayout.Label($"{row.DisplayName}: {FormatDuration(row.ActiveDurationSeconds)}");
        GUILayout.Space(6);
        GUILayout.Label("Recurring loadouts (at least two completed runs)");
        foreach (var row in equipment.Loadouts.Values.Where(x => x.RunOccurrences >= 2)
                     .OrderByDescending(x => x.ActiveDurationSeconds).ThenBy(x => x.Id, StringComparer.Ordinal).Take(20))
            GUILayout.Label($"{row.DisplayName}: {FormatDuration(row.ActiveDurationSeconds)}, {row.RunOccurrences.ToString(CultureInfo.InvariantCulture)} runs");
        GUILayout.Space(6);
        GUILayout.Label("Recurring proven-active totem sets (at least two completed runs)");
        foreach (var row in equipment.TotemSets.Values.Where(x => x.RunOccurrences >= 2)
                     .OrderByDescending(x => x.ActiveDurationSeconds).ThenBy(x => x.Id, StringComparer.Ordinal).Take(20))
            GUILayout.Label($"{row.DisplayName}: {FormatDuration(row.ActiveDurationSeconds)}, {row.RunOccurrences.ToString(CultureInfo.InvariantCulture)} runs");
        GUILayout.Space(6);
        GUILayout.Label("Recent run loadouts");
        foreach (var run in profile.Statistics.Runs.OrderByDescending(x => x.EndedUtc).ThenBy(x => x.RunId, StringComparer.Ordinal).Take(5))
        {
            GUILayout.Label($"{UiText.FormatRoute(run)} / {run.RunId}");
            foreach (var row in run.EquipmentStatistics.Loadouts.Values
                         .OrderByDescending(x => x.ActiveDurationSeconds).ThenBy(x => x.Id, StringComparer.Ordinal).Take(10))
                GUILayout.Label($"  {row.DisplayName}: {FormatDuration(row.ActiveDurationSeconds)}");
            foreach (var transition in run.EquipmentStatistics.Transitions.OrderBy(x => x.ActiveTimeSeconds).TakeLast(10))
                GUILayout.Label($"  t={FormatDuration(transition.ActiveTimeSeconds)}: {transition.FromLoadoutId} -> {transition.ToLoadoutId}; selected={transition.SelectedWeaponSlotId}|{transition.SelectedWeaponId}; totems={transition.TotemSetId}");
        }
        GUILayout.EndScrollView();
    }

    private static void DrawCombatRow(
        string displayName,
        string stableId,
        WeaponMetricTotals totals,
        WeaponMetricCapabilities capabilities)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{displayName} [{stableId}]", GUILayout.Width(280));
        GUILayout.Label(UiText.FormatMetric(totals.FiringActions, capabilities.FiringActions.State), GUILayout.Width(125));
        GUILayout.Label(UiText.FormatMetric(totals.AmmunitionUnitsConsumed, capabilities.AmmunitionConsumption.State), GUILayout.Width(190));
        GUILayout.Label(UiText.FormatMetric(totals.Projectiles, capabilities.Projectiles.State));
        GUILayout.EndHorizontal();
    }

    private static void DrawRecordPair(string title, DurationRecordPair records)
    {
        GUILayout.Label(title);
        if (records.Shortest != null)
        {
            GUILayout.Label(
                $"  {UiText.Get("ui.shortest")}: {FormatDuration(records.Shortest.ActiveDurationSeconds)} " +
                $"({records.Shortest.MapDisplayName}, {records.Shortest.RunId})");
        }

        if (records.Longest != null)
        {
            GUILayout.Label(
                $"  {UiText.Get("ui.longest")}: {FormatDuration(records.Longest.ActiveDurationSeconds)} " +
                $"({records.Longest.MapDisplayName}, {records.Longest.RunId})");
        }
    }

    private void DrawDiagnostics()
    {
        var profile = coordinator.Current;
        GUILayout.Space(8);
        diagnosticScroll = GUILayout.BeginScrollView(diagnosticScroll);
        GUILayout.Label($"{UiText.Get("ui.data_path")}: {coordinator.DataRoot}");
        GUILayout.Space(6);
        GUILayout.Label(UiText.Get("ui.capabilities"));
        if (profile != null)
        {
            GUILayout.Label(
                $"Schema: profile {profile.SchemaVersion.ToString(CultureInfo.InvariantCulture)}, "
                + $"statistics {profile.Statistics.SchemaVersion.ToString(CultureInfo.InvariantCulture)}; "
                + $"economy history {(profile.Statistics.Economy.HistoricalUnavailable ? "partially unavailable before M9" : "captured from generation start")}; "
                + $"economy repair {(profile.Statistics.Economy.WasRepairedFromInvalidState ? "present" : "none")}; "
                + "economy replay identity exact/bounded; "
                + $"legacy economy saturation evidence {(profile.Statistics.Economy.LegacyIdentitySaturationIncomplete ? "incomplete" : "none")}; "
                + $"Money arithmetic {(profile.Statistics.Economy.MoneyArithmeticSaturated ? "saturated" : "available")}; "
                + $"Cash arithmetic {(profile.Statistics.Economy.CashArithmeticSaturated ? "saturated" : "available")}");
            GUILayout.Label(
                $"World-time history {(profile.Statistics.WorldTime.HistoricalUnavailable ? "partially unavailable before M12" : "captured from generation start")}; "
                + $"repair {(profile.Statistics.WorldTime.WasRepairedFromInvalidState ? "present" : "none")}; "
                + $"clock units {WorldTimeObservationTracker.NativeSecondsPerDay.ToString(CultureInfo.InvariantCulture)} seconds/native day; "
                + $"arithmetic calendar={(profile.Statistics.WorldTime.CalendarArithmeticUnavailable ? "unavailable" : "available")}, "
                + $"elapsed={(profile.Statistics.WorldTime.ObservedElapsedArithmeticUnavailable ? "unavailable" : "available")}, "
                + $"sleep-sessions={(profile.Statistics.WorldTime.SleepSessionArithmeticUnavailable ? "unavailable" : "available")}, "
                + $"sleep-time={(profile.Statistics.WorldTime.SleepElapsedArithmeticUnavailable ? "unavailable" : "available")}");
            GUILayout.Label(
                $"Crafting history {(profile.Statistics.Crafting.HistoricalUnavailable ? "partially unavailable before M13" : "captured from generation start")}; "
                + $"resource history={(profile.Statistics.Crafting.ResourceHistoryUnavailable ? "partially unavailable before M16" : "captured from generation start")}; "
                + $"currency history={(profile.Statistics.Crafting.CurrencyHistoryUnavailable ? "partially unavailable before M16" : "captured from generation start")}; "
                + $"repair {(profile.Statistics.Crafting.WasRepairedFromInvalidState ? "present" : "none")}; "
                + $"arithmetic actions={(profile.Statistics.Crafting.CompletionArithmeticUnavailable ? "unavailable" : "available")}, "
                + $"quantity={(profile.Statistics.Crafting.QuantityArithmeticUnavailable ? "unavailable" : "available")}, "
                + $"resource-actions={(profile.Statistics.Crafting.ResourceActionArithmeticUnavailable ? "unavailable" : "available")}, "
                + $"resource-quantity={(profile.Statistics.Crafting.ResourceQuantityArithmeticUnavailable ? "unavailable" : "available")}, "
                + $"currency-actions={(profile.Statistics.Crafting.CurrencyActionArithmeticUnavailable ? "unavailable" : "available")}, "
                + $"currency-amount={(profile.Statistics.Crafting.CurrencyAmountArithmeticUnavailable ? "unavailable" : "available")}");
            var holdings = EconomyHoldingsReducer.Project(profile.Statistics.Holdings);
            GUILayout.Label(
                $"Holdings generation={profile.Statistics.Holdings.SaveGenerationId}; "
                + $"Money={holdings.Money.State}/{holdings.Capabilities.Money.State} observed={FormatHoldingTimestamp(holdings.Money)}; "
                + $"Cash={holdings.Cash.State}/{holdings.Capabilities.Cash.State} observed={FormatHoldingTimestamp(holdings.Cash)}; "
                + $"liquid={holdings.LiquidWealth.State}/{holdings.Capabilities.LiquidWealth.State}; "
                + $"history={(profile.Statistics.Holdings.HistoricalUnavailable ? "unavailable before M15" : "M15")}; "
                + $"repair={(profile.Statistics.Holdings.WasRepairedFromInvalidState ? "present" : "none")}");
            if (!string.IsNullOrWhiteSpace(holdings.Money.FreshnessProvenance))
                GUILayout.Label($"  Money freshness: {holdings.Money.FreshnessProvenance}");
            if (!string.IsNullOrWhiteSpace(holdings.Cash.FreshnessProvenance))
                GUILayout.Label($"  Cash freshness: {holdings.Cash.FreshnessProvenance}");
            if (!string.IsNullOrWhiteSpace(holdings.LiquidWealth.FreshnessProvenance))
                GUILayout.Label($"  Liquid freshness/arithmetic: {holdings.LiquidWealth.FreshnessProvenance}");
            foreach (var capability in profile.Capabilities)
            {
                GUILayout.Label($"{capability.AdapterId}: {capability.State} ({capability.Version})");
                if (!string.IsNullOrWhiteSpace(capability.Detail))
                {
                    GUILayout.Label($"  {capability.Detail}");
                }
            }
        }

        GUILayout.Space(6);
        GUILayout.Label(UiText.Get("ui.diagnostic_log"));
        foreach (var entry in coordinator.DiagnosticEntries.Reverse().Take(50))
        {
            GUILayout.Label(
                $"{entry.TimestampUtc.ToString("u", CultureInfo.InvariantCulture)} " +
                $"[{entry.Severity}] {entry.Message}");
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label(UiText.Get("ui.hotkey"), GUILayout.Width(120));
        hotkeyInput = GUILayout.TextField(hotkeyInput, GUILayout.Width(120));
        if (GUILayout.Button(UiText.Get("ui.apply"), GUILayout.Width(80)))
        {
            ApplyHotkey();
        }

        GUILayout.EndHorizontal();
        GUILayout.Label(UiText.Get("ui.open_hint"));
        GUILayout.EndScrollView();
    }

    private void DrawEconomy()
    {
        var profile = coordinator.Current;
        if (profile == null) return;
        var economy = profile.Statistics.Economy;
        var currentEconomyCapabilities = coordinator.CurrentEconomyCapabilities;
        GUILayout.Space(8);
        var holdings = EconomyHoldingsReducer.Project(profile.Statistics.Holdings);
        GUILayout.Label(UiText.Get("ui.current_holdings"));
        GUILayout.Label(UiText.Get("ui.holdings_contract"));
        GUILayout.Label($"  {UiText.Get("ui.money_holding")}: {UiText.FormatHolding(holdings.Money, holdings.Capabilities.Money)}");
        GUILayout.Label($"  {UiText.Get("ui.cash_holding")}: {UiText.FormatHolding(holdings.Cash, holdings.Capabilities.Cash)}");
        GUILayout.Label($"  {UiText.Get("ui.liquid_wealth")}: {UiText.FormatHolding(holdings.LiquidWealth, holdings.Capabilities.LiquidWealth)}");
        if (profile.Statistics.Holdings.HistoricalUnavailable)
            GUILayout.Label($"  {UiText.Get("ui.pre_m15_unavailable")}: {profile.Statistics.Holdings.HistoricalProvenance}");
        GUILayout.Space(12);
        GUILayout.Label(UiText.Get("ui.currency_flows"));
        GUILayout.Label(UiText.Get("ui.economy_contract"));
        if (economy.LegacyIdentitySaturationIncomplete)
            GUILayout.Label("Economy totals captured by an earlier schema-9 build may be incomplete after its legacy identity limit; current capture continues with exact bounded replay protection.");
        economyScroll = GUILayout.BeginScrollView(economyScroll);
        DrawCurrency(
            economy,
            CurrencyKind.Money,
            economy.Capabilities.MoneyAmountDirection,
            currentEconomyCapabilities.MoneyAmountDirection,
            economy.Capabilities.MoneySourceAttribution,
            economy.Capabilities.MoneyContextAttribution);
        GUILayout.Space(12);
        DrawCurrency(
            economy,
            CurrencyKind.Cash,
            economy.Capabilities.CashAmountDirection,
            currentEconomyCapabilities.CashAmountDirection,
            economy.Capabilities.CashExternalAcquisition,
            economy.Capabilities.CashContextAttribution);
        GUILayout.Space(12);
        GUILayout.Label(UiText.Get("ui.raid_cash"));
        if (economy.HistoricalUnavailable)
            GUILayout.Label($"  {UiText.Get("ui.pre_m9_unavailable")}");
        GUILayout.Label($"  {UiText.Get("ui.acquired")}: {UiText.FormatEconomyValue(economy.CashRaidOutcomes.Acquired, economy.Capabilities.CashExternalAcquisition, currentEconomyCapabilities.CashExternalAcquisition)}");
        GUILayout.Label($"  {UiText.Get("ui.secured")}: {UiText.FormatEconomyValue(economy.CashRaidOutcomes.Secured, economy.Capabilities.CashTerminalOutcomes, currentEconomyCapabilities.CashTerminalOutcomes)}");
        GUILayout.Label($"  {UiText.Get("ui.lost")}: {UiText.FormatEconomyValue(economy.CashRaidOutcomes.Lost, economy.Capabilities.CashTerminalOutcomes, currentEconomyCapabilities.CashTerminalOutcomes)}");
        GUILayout.Label($"  {UiText.Get("ui.unresolved")}: {UiText.FormatEconomyValue(economy.CashRaidOutcomes.Unresolved, economy.Capabilities.CashTerminalOutcomes, currentEconomyCapabilities.CashTerminalOutcomes)}");
        GUILayout.Space(12);
        GUILayout.Label("Recent run economy");
        foreach (var run in profile.Statistics.Runs.OrderByDescending(value => value.EndedUtc).ThenBy(value => value.RunId, StringComparer.Ordinal).Take(8))
            GUILayout.Label(
                $"  {run.EndedUtc.ToString("u", CultureInfo.InvariantCulture)} {run.Outcome}: "
                + $"{UiText.FormatEconomyCompact(run.Economy, currentEconomyCapabilities)}; "
                + UiText.FormatCashOutcome(run.Economy, currentEconomyCapabilities));
        GUILayout.EndScrollView();
    }

    private void DrawCrafting()
    {
        var profile = coordinator.Current;
        if (profile == null) return;
        var crafting = profile.Statistics.Crafting;
        var capabilities = CraftingStatisticsReducer.RestrictWithCurrent(
            crafting.Capabilities,
            coordinator.CurrentCraftingCapabilities);
        GUILayout.Space(8);
        GUILayout.Label(UiText.Get("ui.crafting_contract"));
        if (crafting.HistoricalUnavailable)
            GUILayout.Label($"{UiText.Get("ui.pre_m13_unavailable")}: {crafting.HistoricalProvenance}");
        if (crafting.ResourceHistoryUnavailable)
            GUILayout.Label($"{UiText.Get("ui.pre_m16_resources_unavailable")}: {crafting.ResourceHistoryProvenance}");
        if (crafting.CurrencyHistoryUnavailable)
            GUILayout.Label($"{UiText.Get("ui.pre_m16_currency_unavailable")}: {crafting.CurrencyHistoryProvenance}");
        GUILayout.Label(
            $"{UiText.Get("ui.crafting_actions")}: "
            + UiText.FormatCraftingCount(crafting.CompletionActions, capabilities.CompletionActions));
        GUILayout.Label(
            $"{UiText.Get("ui.crafting_quantity")}: "
            + UiText.FormatCraftingCount(crafting.ProducedQuantity, capabilities.ProducedQuantity));
        GUILayout.Label(
            $"{UiText.Get("ui.crafting_currency_actions")}: "
            + UiText.FormatCraftingCount(crafting.CurrencyChargeActions, capabilities.CurrencyCharge));
        GUILayout.Label(
            $"{UiText.Get("ui.crafting_currency")}: "
            + UiText.FormatCraftingCount(crafting.CurrencyCharged, capabilities.CurrencyCharge));
        GUILayout.Label(
            $"Capture: actions={capabilities.CompletionActions.State}; quantity={capabilities.ProducedQuantity.State}; "
            + $"output={capabilities.OutputIdentity.State}; recipe={capabilities.RecipeIdentity.State}; "
            + $"batch={capabilities.BatchMetadata.State}; item resources={capabilities.ItemResourceIdentity.State}; "
            + $"output/resource association={capabilities.OutputResourceAssociation.State}; currency={capabilities.CurrencyCharge.State}");
        GUILayout.Label(
            $"Unavailable dimensions: workstation={capabilities.WorkstationIdentity.State}; run/map context={capabilities.ContextAttribution.State}; "
            + $"multiple-output recipes={capabilities.MultipleOutputRecipes.State}; Money/Cash split={capabilities.CurrencyMoneyCashSplit.State}");

        craftingScroll = GUILayout.BeginScrollView(craftingScroll);
        GUILayout.Label(UiText.Get("ui.crafting_outputs"));
        if (crafting.Outputs.Count == 0) GUILayout.Label(UiText.Get("ui.no_crafting"));
        foreach (var output in crafting.Outputs.Values
                     .OrderByDescending(value => value.CompletionActions)
                     .ThenBy(value => value.OutputItemId, StringComparer.Ordinal))
        {
            GUILayout.Label(
                $"{output.DisplayName} [{output.OutputItemId}]: "
                + $"{UiText.Get("ui.crafting_actions").ToLowerInvariant()} "
                + UiText.FormatCraftingCount(output.CompletionActions, capabilities.CompletionActions)
                + $", {UiText.Get("ui.crafting_quantity").ToLowerInvariant()} "
                + UiText.FormatCraftingCount(output.ProducedQuantity, capabilities.ProducedQuantity));
            foreach (var recipe in output.Recipes.Values.OrderBy(value => value.RecipeId, StringComparer.Ordinal))
            {
                GUILayout.Label(
                    $"  {UiText.Get("ui.crafting_recipe")} {recipe.RecipeId}: actions "
                    + UiText.FormatCraftingCount(
                        recipe.CompletionActions,
                        capabilities.CompletionActions,
                        capabilities.RecipeIdentity)
                    + ", quantity "
                    + UiText.FormatCraftingCount(
                        recipe.ProducedQuantity,
                        capabilities.ProducedQuantity,
                        capabilities.RecipeIdentity));
                if (recipe.CurrencyChargeActions != 0 || recipe.CurrencyCharged != 0)
                {
                    GUILayout.Label(
                        $"    {UiText.Get("ui.crafting_currency")}: "
                        + UiText.FormatCraftingCount(recipe.CurrencyCharged, capabilities.CurrencyCharge)
                        + " across "
                        + UiText.FormatCraftingCount(recipe.CurrencyChargeActions, capabilities.CurrencyCharge)
                        + " action(s)");
                }
                foreach (var resource in recipe.Resources.Values
                             .OrderByDescending(value => value.ConsumedQuantity)
                             .ThenBy(value => value.ResourceItemId, StringComparer.Ordinal))
                {
                    GUILayout.Label(
                        $"    {UiText.Get("ui.crafting_resource")}: {resource.DisplayName} [{resource.ResourceItemId}] "
                        + UiText.FormatCraftingCount(
                            resource.ConsumedQuantity,
                            capabilities.ItemResourceIdentity,
                            capabilities.OutputResourceAssociation)
                        + " across "
                        + UiText.FormatCraftingCount(
                            resource.ConsumptionActions,
                            capabilities.CompletionActions,
                            capabilities.OutputResourceAssociation)
                        + " action(s)");
                }
                if (recipe.BatchActions.Count != 0)
                {
                    GUILayout.Label(
                        $"    {UiText.Get("ui.crafting_batch")}: "
                        + string.Join(", ", recipe.BatchActions
                            .OrderBy(value => ParseCraftingBatch(value.Key))
                            .ThenBy(value => value.Key, StringComparer.Ordinal)
                            .Select(value => $"{value.Key} x {value.Value.ToString(CultureInfo.InvariantCulture)} action(s)"))
                        + (capabilities.BatchMetadata.State == AdapterCapabilityState.DisabledIncompatible
                            ? $" ({UiText.Get("ui.crafting_capture_incomplete")})"
                            : string.Empty));
                }
            }
        }
        GUILayout.Space(12);
        GUILayout.Label(UiText.Get("ui.crafting_resources"));
        if (crafting.Resources.Count == 0) GUILayout.Label(UiText.Get("ui.no_crafting_resources"));
        foreach (var resource in crafting.Resources.Values
                     .OrderByDescending(value => value.ConsumedQuantity)
                     .ThenBy(value => value.ResourceItemId, StringComparer.Ordinal))
        {
            GUILayout.Label(
                $"{resource.DisplayName} [{resource.ResourceItemId}]: "
                + UiText.FormatCraftingCount(resource.ConsumedQuantity, capabilities.ItemResourceIdentity));
            foreach (var association in crafting.Outputs.Values
                         .SelectMany(output => output.Recipes.Values.Select(recipe => new { Output = output, Recipe = recipe }))
                         .Where(value => value.Recipe.Resources.ContainsKey(resource.ResourceItemId))
                         .Select(value => new
                         {
                             value.Output,
                             value.Recipe,
                             Resource = value.Recipe.Resources[resource.ResourceItemId]
                         })
                         .OrderByDescending(value => value.Resource.ConsumedQuantity)
                         .ThenByDescending(value => value.Resource.ConsumptionActions)
                         .ThenBy(value => value.Output.OutputItemId, StringComparer.Ordinal)
                         .ThenBy(value => value.Recipe.RecipeId, StringComparer.Ordinal))
            {
                GUILayout.Label(
                    $"  {association.Output.DisplayName} [{association.Output.OutputItemId}] / {association.Recipe.RecipeId}: "
                    + UiText.FormatCraftingCount(
                        association.Resource.ConsumedQuantity,
                        capabilities.ItemResourceIdentity,
                        capabilities.OutputResourceAssociation)
                    + " across "
                    + UiText.FormatCraftingCount(
                        association.Resource.ConsumptionActions,
                        capabilities.CompletionActions,
                        capabilities.OutputResourceAssociation)
                    + " action(s)");
            }
        }
        GUILayout.EndScrollView();
    }

    private static long ParseCraftingBatch(string value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity)
            ? quantity
            : long.MaxValue;

    private static string FormatHoldingTimestamp(EconomyHoldingObservation observation) =>
        observation.ObservedUtc.HasValue
            ? observation.ObservedUtc.Value.ToString("O", CultureInfo.InvariantCulture)
            : "none";

    private static void DrawCurrency(
        EconomyStatisticsAggregate economy,
        CurrencyKind kind,
        MetricAvailability amountAvailability,
        MetricAvailability currentAmountAvailability,
        MetricAvailability sourceAvailability,
        MetricAvailability contextAvailability)
    {
        GUILayout.Label(kind.ToString());
        if ((kind == CurrencyKind.Money && economy.MoneyArithmeticSaturated)
            || (kind == CurrencyKind.Cash && economy.CashArithmeticSaturated))
            GUILayout.Label("  Capture stopped before Int64 overflow; retained totals remain exact.");
        if (economy.HistoricalUnavailable) GUILayout.Label($"  {UiText.Get("ui.pre_m9_unavailable")}");
        if (!economy.Currencies.TryGetValue(kind.ToString(), out var currency))
        {
            if (economy.HistoricalUnavailable)
            {
                GUILayout.Label($"  {UiText.Get("ui.no_m9_flows")}");
                GUILayout.Label($"  {UiText.Get("ui.sources")}: {sourceAvailability.State}");
                GUILayout.Label($"  {UiText.Get("ui.contexts")}: {contextAvailability.State}");
                return;
            }
            GUILayout.Label($"  {UiText.Get("ui.gross_inflow")}: {UiText.FormatEconomyValue(0, amountAvailability, currentAmountAvailability)}");
            GUILayout.Label($"  {UiText.Get("ui.gross_outflow")}: {UiText.FormatEconomyValue(0, amountAvailability, currentAmountAvailability)}");
            GUILayout.Label($"  {UiText.Get("ui.net_flow")}: {UiText.FormatEconomyValue(0, amountAvailability, currentAmountAvailability)}");
            GUILayout.Label($"  {UiText.Get("ui.sources")}: {sourceAvailability.State}");
            GUILayout.Label($"  {UiText.Get("ui.contexts")}: {contextAvailability.State}");
            return;
        }
        GUILayout.Label($"  {UiText.Get("ui.gross_inflow")}: {UiText.FormatEconomyValue(currency.Totals.GrossInflow, amountAvailability, currentAmountAvailability)}");
        GUILayout.Label($"  {UiText.Get("ui.gross_outflow")}: {UiText.FormatEconomyValue(currency.Totals.GrossOutflow, amountAvailability, currentAmountAvailability)}");
        GUILayout.Label($"  {UiText.Get("ui.net_flow")}: {UiText.FormatEconomyValue(currency.Totals.NetFlow, amountAvailability, currentAmountAvailability)}");
        GUILayout.Label($"  {UiText.Get("ui.sources")} ({sourceAvailability.State}): " + string.Join(", ", currency.Sources.OrderBy(row => row.Key, StringComparer.Ordinal).Select(row => $"{row.Key} +{row.Value.GrossInflow.ToString(CultureInfo.InvariantCulture)}/-{row.Value.GrossOutflow.ToString(CultureInfo.InvariantCulture)}")));
        GUILayout.Label($"  {UiText.Get("ui.contexts")} ({contextAvailability.State}): " + string.Join(", ", currency.Contexts.OrderBy(row => row.Key, StringComparer.Ordinal).Select(row => $"{row.Key} +{row.Value.GrossInflow.ToString(CultureInfo.InvariantCulture)}/-{row.Value.GrossOutflow.ToString(CultureInfo.InvariantCulture)}")));
    }

    private void DrawActions()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(UiText.Get("ui.export"), GUILayout.Width(180)))
        {
            try
            {
                var result = coordinator.ExportCurrent();
                ShowStatus($"{UiText.Get("ui.export_complete")}: {result.Directory}", seconds: 8);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowStatus(UiText.Get("ui.export_failed"), seconds: 8);
            }
        }

        if (!confirmReset)
        {
            if (GUILayout.Button(UiText.Get("ui.reset"), GUILayout.Width(190)))
            {
                confirmReset = true;
            }
        }
        else
        {
            GUILayout.Label(UiText.Get("ui.reset_warning"));
            if (GUILayout.Button(UiText.Get("ui.confirm_reset"), GUILayout.Width(120)))
            {
                coordinator.ResetCurrent();
                confirmReset = false;
                ShowStatus(UiText.Get("ui.reset_complete"), seconds: 8);
            }

            if (GUILayout.Button(UiText.Get("ui.cancel"), GUILayout.Width(80)))
            {
                confirmReset = false;
            }
        }

        GUILayout.EndHorizontal();
    }

    private void DrawTabButton(PanelTab target, string key)
    {
        var wasSelected = tab == target;
        if (GUILayout.Toggle(wasSelected, UiText.Get(key), GUI.skin.button, GUILayout.Width(84)) && !wasSelected)
        {
            tab = target;
        }
    }

    private void LoadSettings()
    {
        try
        {
            var settings = settingsStore.Load(settingsPath).Value ?? new UserSettings();
            if (!Enum.TryParse(settings.PanelHotkey, ignoreCase: true, out hotkey) || hotkey == KeyCode.None)
            {
                hotkey = KeyCode.F8;
            }

            hotkeyInput = hotkey.ToString();
            settings.PanelHotkey = hotkeyInput;
            settingsStore.Save(settingsPath, settings);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            hotkey = KeyCode.F8;
            hotkeyInput = hotkey.ToString();
        }
    }

    private void ApplyHotkey()
    {
        if (!Enum.TryParse(hotkeyInput.Trim(), ignoreCase: true, out KeyCode parsed) || parsed == KeyCode.None)
        {
            ShowStatus(UiText.Get("ui.hotkey_invalid"));
            return;
        }

        hotkey = parsed;
        hotkeyInput = parsed.ToString();
        settingsStore.Save(settingsPath, new UserSettings { PanelHotkey = hotkeyInput });
        ShowStatus(UiText.Get("ui.hotkey_saved"));
    }

    private void ShowStatus(string message, int seconds = 4)
    {
        status = message;
        statusExpiresUtc = DateTime.UtcNow.AddSeconds(seconds);
    }

    private static string FormatAmounts(AggregateTotals totals)
    {
        if (totals.AmountsByUnit.Count == 0)
        {
            return "0";
        }

        return string.Join(
            ", ",
            totals.AmountsByUnit
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{entry.Value.ToString("0.###", CultureInfo.InvariantCulture)} {entry.Key}"));
    }

    private static string FormatHealing(AggregateTotals totals) =>
        totals.ActualHealthRestored.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatDuration(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    private static string FormatDistance(double meters, bool supported) => supported
        ? $"{meters.ToString("0.##", CultureInfo.InvariantCulture)} m"
        : UiText.Get("ui.unsupported");

    private static string FormatAccuracy(CombatStatisticsViewModel model) =>
        model.Capabilities.Accuracy.State == AdapterCapabilityState.DisabledIncompatible
            ? UiText.Get("ui.unsupported")
            : model.Accuracy.HasValue
                ? model.Accuracy.Value.ToString("P1", CultureInfo.InvariantCulture)
                : "—";

    private static string FormatContainers(
        ContainerStatisticsAggregate statistics,
        AdapterCapabilityState currentCapability)
    {
        var value = statistics.UniqueContainersLooted.ToString(CultureInfo.InvariantCulture);
        if (statistics.WasRepairedFromInvalidState)
            return $"{value} ({UiText.Get("ui.repaired_unavailable")})";
        if (statistics.HistoricalUnavailable)
            return $"{value} since M7 ({UiText.Get("ui.container_history_unavailable")})";
        return currentCapability == AdapterCapabilityState.Supported
            ? value
            : $"{value} ({UiText.Get("ui.unsupported")})";
    }

    private static string FormatRecordEligibility(RunPresentationRow row) => row.RecordEligibilityReason switch
    {
        RunRecordEligibilityReason.Eligible => UiText.Get("ui.record_eligible"),
        RunRecordEligibilityReason.Interrupted =>
            $"{UiText.Get("ui.record_excluded")} ({UiText.Get("ui.reason_interrupted")})",
        RunRecordEligibilityReason.Integrity =>
            $"{UiText.Get("ui.record_excluded")} ({UiText.Get("ui.integrity")}: {row.IntegrityTags})",
        RunRecordEligibilityReason.LifecycleUnsupported =>
            $"{UiText.Get("ui.record_excluded")} ({UiText.Get("ui.reason_lifecycle")})",
        _ => $"{UiText.Get("ui.record_excluded")} ({UiText.Get("ui.reason_other")})"
    };

    private enum PanelTab
    {
        Overview,
        Runs,
        Records,
        Combat,
        Equipment,
        Economy,
        Crafting,
        Items,
        Diagnostics
    }
}
