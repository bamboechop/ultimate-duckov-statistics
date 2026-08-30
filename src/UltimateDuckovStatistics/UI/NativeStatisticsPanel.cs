using System.Globalization;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UnityEngine;

namespace UltimateDuckovStatistics.UI;

internal sealed class NativeStatisticsPanel : IDisposable
{
    private const int WindowId = 9048127;
    private readonly NativeProfileCoordinator coordinator;
    private readonly NativeUiIntegration nativeUi;
    private readonly NativeItemIconResolver iconResolver = new();
    private readonly NativePanelTheme theme = new();
    private readonly PanelInteractionState interaction = new();
    private readonly PanelOperationGate operationGate = new();
    private readonly AtomicJsonStore<UserSettings> settingsStore = new();
    private readonly string settingsPath;
    private Rect windowRect = new(80, 60, 880, 650);
    private Vector2 itemScroll;
    private Vector2 diagnosticScroll;
    private Vector2 diagnosticHealthScroll;
    private Vector2 runScroll;
    private Vector2 recordScroll;
    private Vector2 combatScroll;
    private Vector2 equipmentScroll;
    private Vector2 economyScroll;
    private Vector2 craftingScroll;
    private Vector2 tabStripScroll;
    private StatisticsPanelLayout layout = new();
    private StatisticsPanelProjection? projection;
    private string projectedGenerationId = string.Empty;
    private long projectedRevision = -1;
    private bool visible;
    private PanelAccessSurface? openSurface;
    private bool disposed;
    private string status = string.Empty;
    private DateTime statusExpiresUtc;
    private KeyCode hotkey = KeyCode.F8;
    private string hotkeyInput = "F8";
    private readonly HashSet<string> expandedRunIds = new(StringComparer.Ordinal);
    private readonly Dictionary<StatisticsPanelTab, int> pageByTab = new();
    private readonly HashSet<string> expandedCapabilityGroups = new(StringComparer.Ordinal);
    private bool technicalDetailsExpanded;
    private bool diagnosticLogExpanded;
    private int craftingOutputPage;
    private int craftingResourcePage;
    private bool cursorStateCaptured;
    private bool priorCursorVisible;
    private CursorLockMode priorCursorLockMode;
    private GameObject? priorSelectedGameObject;

    public NativeStatisticsPanel(NativeProfileCoordinator coordinator)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        settingsPath = Path.Combine(coordinator.DataRoot, "settings.json");
        LoadSettings();
        nativeUi = new NativeUiIntegration(coordinator, RequestOpen, HandleSurfaceClosed);
        nativeUi.Initialize();
    }

    public void Tick()
    {
        ObservePendingReset();
        if (!Input.GetKeyDown(hotkey))
        {
            return;
        }

        if (visible)
        {
            if (!interaction.CancelModal()) SetVisible(false);
        }
        else
            RequestOpen(PanelAccessSurface.Hotkey);
    }

    public void Draw()
    {
        theme.EnsureInitialized();
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
            SetVisible(false);
            ShowStatus(UiText.Get("ui.raid_unavailable"));
            return;
        }

        layout = StatisticsPanelLayoutPolicy.Create(Screen.width, Screen.height);
        windowRect.width = layout.Width;
        windowRect.height = layout.Height;
        windowRect.x = Mathf.Clamp(windowRect.x, 0, Mathf.Max(0, Screen.width - windowRect.width));
        windowRect.y = Mathf.Clamp(windowRect.y, 0, Mathf.Max(0, Screen.height - windowRect.height));
        windowRect = GUI.Window(WindowId, windowRect, DrawWindow, UiText.Get("ui.title"), theme.Window);
    }

    private void RequestOpen(PanelAccessSurface surface)
    {
        if (disposed) return;
        var isRaid = NativeRaidContext.IsRaidMap();
        var decision = StatisticsPanelAccessPolicy.Resolve(surface, isRaid);
        if (surface == PanelAccessSurface.BasePauseMenu
            && (LevelManager.Instance == null || !LevelManager.Instance.IsBaseLevel))
        {
            decision = new PanelAccessDecision { RejectionTextKey = "ui.raid_unavailable" };
        }

        if (!decision.CanOpen)
        {
            SetVisible(false);
            var rejection = UiText.Get(decision.RejectionTextKey ?? "ui.raid_unavailable");
            ShowStatus(rejection);
            nativeUi.ShowToast(rejection);
            return;
        }

        var profile = coordinator.Current;
        if (!StatisticsPanelProjectionFactory.HasProvableGeneration(profile, coordinator.CurrentGenerationId))
        {
            var rejection = UiText.Get("ui.profile_unavailable");
            ShowStatus(rejection, seconds: 8);
            nativeUi.ShowToast(rejection);
            return;
        }

        SetVisible(true);
        openSurface = surface;
    }

    private void HandleSurfaceClosed(PanelAccessSurface surface)
    {
        if (openSurface == surface) SetVisible(false);
    }

    private void SetVisible(bool value)
    {
        if (visible == value) return;
        visible = value;
        interaction.CancelModal();
        if (value)
        {
            if (!cursorStateCaptured)
            {
                priorCursorVisible = Cursor.visible;
                priorCursorLockMode = Cursor.lockState;
                cursorStateCaptured = true;
            }

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            var eventSystem = GameManager.EventSystem;
            priorSelectedGameObject = eventSystem?.currentSelectedGameObject;
            eventSystem?.SetSelectedGameObject(null);
            layout = StatisticsPanelLayoutPolicy.Create(Screen.width, Screen.height);
            windowRect = new Rect(
                (Screen.width - layout.Width) / 2f,
                (Screen.height - layout.Height) / 2f,
                layout.Width,
                layout.Height);
            return;
        }

        openSurface = null;
        if (!cursorStateCaptured) return;
        Cursor.visible = priorCursorVisible;
        Cursor.lockState = priorCursorLockMode;
        var restoreEventSystem = GameManager.EventSystem;
        if (restoreEventSystem != null
            && priorSelectedGameObject != null
            && priorSelectedGameObject.activeInHierarchy)
        {
            restoreEventSystem.SetSelectedGameObject(priorSelectedGameObject);
        }
        priorSelectedGameObject = null;
        cursorStateCaptured = false;
    }

    private StatisticsPanelProjection? GetProjection()
    {
        var profile = coordinator.Current;
        if (!StatisticsPanelProjectionFactory.HasProvableGeneration(profile, coordinator.CurrentGenerationId)) return null;
        if (profile == null) return null;
        if (projection != null
            && projectedRevision == profile.Revision
            && string.Equals(projectedGenerationId, profile.GenerationId, StringComparison.Ordinal))
        {
            return projection;
        }

        projection = StatisticsPanelProjectionFactory.Create(
            profile,
            coordinator.CurrentEconomyCapabilities,
            coordinator.CurrentCraftingCapabilities);
        projectedGenerationId = profile.GenerationId;
        projectedRevision = profile.Revision;
        return projection;
    }

    private void HandleKeyboardNavigation()
    {
        var current = Event.current;
        if (current == null || current.type != EventType.KeyDown) return;
        if (current.keyCode == KeyCode.Escape)
        {
            if (!interaction.CancelModal()) SetVisible(false);
            current.Use();
            return;
        }

        if (interaction.ResetConfirmationVisible) return;

        if (current.control && current.keyCode == KeyCode.Tab)
        {
            interaction.MoveTab(current.shift ? -1 : 1);
            if (layout.TabStripRequiresScrolling)
            {
                var selectedIndex = (int)interaction.SelectedTab;
                tabStripScroll.x = Math.Max(0f, selectedIndex * 108f - layout.Width * 0.35f);
            }
            current.Use();
        }
    }

    private void DrawWindow(int windowId)
    {
        HandleKeyboardNavigation();
        var modalActive = interaction.ResetConfirmationVisible;
        GUI.enabled = !modalActive;
        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        GUILayout.Label(UiText.Get("ui.integrity_note"), theme.Muted);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(UiText.Get("ui.close"), GUILayout.Width(90)))
        {
            SetVisible(false);
        }
        GUILayout.EndHorizontal();

        if (layout.TabStripRequiresScrolling)
            tabStripScroll = GUILayout.BeginScrollView(
                tabStripScroll,
                alwaysShowHorizontal: true,
                alwaysShowVertical: false,
                GUILayout.Height(44));
        GUILayout.BeginHorizontal();
        DrawTabButton(StatisticsPanelTab.Overview, "ui.overview");
        DrawTabButton(StatisticsPanelTab.Runs, "ui.runs");
        DrawTabButton(StatisticsPanelTab.Records, "ui.records");
        DrawTabButton(StatisticsPanelTab.Combat, "ui.combat");
        DrawTabButton(StatisticsPanelTab.Equipment, "ui.equipment");
        DrawTabButton(StatisticsPanelTab.Economy, "ui.economy");
        DrawTabButton(StatisticsPanelTab.Crafting, "ui.crafting");
        DrawTabButton(StatisticsPanelTab.ItemUse, "ui.item_use");
        DrawTabButton(StatisticsPanelTab.Diagnostics, "ui.diagnostics");
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        if (layout.TabStripRequiresScrolling) GUILayout.EndScrollView();

        switch (interaction.SelectedTab)
        {
            case StatisticsPanelTab.Overview:
                DrawOverview();
                break;
            case StatisticsPanelTab.ItemUse:
                DrawItems();
                break;
            case StatisticsPanelTab.Runs:
                DrawRuns();
                break;
            case StatisticsPanelTab.Records:
                DrawRecords();
                break;
            case StatisticsPanelTab.Combat:
                DrawCombat();
                break;
            case StatisticsPanelTab.Equipment:
                DrawEquipment();
                break;
            case StatisticsPanelTab.Economy:
                DrawEconomy();
                break;
            case StatisticsPanelTab.Crafting:
                DrawCrafting();
                break;
            case StatisticsPanelTab.Diagnostics:
                DrawDiagnostics();
                break;
        }

        GUI.enabled = true;
        GUILayout.FlexibleSpace();
        DrawActions();
        GUILayout.EndVertical();
        GUI.DragWindow(new Rect(0, 0, windowRect.width - 100, 24));
    }

    private void DrawOverview()
    {
        var model = GetProjection();
        if (model == null)
        {
            GUILayout.Label(UiText.Get("ui.profile_unavailable"));
            return;
        }
        var profile = model.Profile;

        GUILayout.Space(12);
        if (layout.Columns == PanelColumnLayout.SideBySide) GUILayout.BeginHorizontal();
        GUILayout.BeginVertical();
        GUILayout.Label($"{UiText.Get("ui.save_slot")}: {profile.Slot.ToString(CultureInfo.InvariantCulture)}");
        GUILayout.Label($"{UiText.Get("ui.generation")}: {profile.GenerationId}");
        GUILayout.Label($"{UiText.Get("ui.total_uses")}: {profile.Statistics.Overall.ActivationCount.ToString(CultureInfo.InvariantCulture)}");
        GUILayout.Label($"{UiText.Get("ui.actual_hp")}: {FormatHealing(profile.Statistics.Overall)}");
        GUILayout.Label($"{UiText.Get("ui.amount")}: {FormatAmounts(profile.Statistics.Overall)}");
        GUILayout.Label($"{UiText.Get("ui.interrupted_sessions")}: {profile.InterruptedSessionCount.ToString(CultureInfo.InvariantCulture)}");
        var runs = model.Runs;
        GUILayout.Space(8);
        GUILayout.Label(
            $"{UiText.Get("ui.total_runs")}: {runs.TotalRuns.ToString(CultureInfo.InvariantCulture)} " +
            $"({UiText.Get("ui.extracted_runs")}: {runs.ExtractedRuns.ToString(CultureInfo.InvariantCulture)}, " +
            $"{UiText.Get("ui.died_runs")}: {runs.DiedRuns.ToString(CultureInfo.InvariantCulture)}, " +
            $"{UiText.Get("ui.interrupted_runs")}: {runs.InterruptedRuns.ToString(CultureInfo.InvariantCulture)})");
        GUILayout.Label($"{UiText.Get("ui.physical_distance")}: {FormatDistance(runs.PhysicalDistance, runs.MovementSupported)}");
        GUILayout.Label($"{UiText.Get("ui.teleport_distance")}: {FormatDistance(runs.TeleportDistance, runs.MovementSupported)}");
        var containers = model.Containers;
        GUILayout.Label(
            $"{UiText.Get("ui.containers_looted")}: {FormatContainers(containers.Lifetime, containers.CurrentCapability)}");
        var combat = model.Weapons;
        GUILayout.Label(
            $"{UiText.Get("ui.firing_actions")}: "
            + UiText.FormatMetric(combat.Lifetime.Totals.FiringActions, combat.Capabilities.FiringActions.State));
        GUILayout.EndVertical();
        GUILayout.BeginVertical();
        var holdings = model.Holdings;
        GUILayout.Space(8);
        GUILayout.Label(UiText.Get("ui.current_holdings"));
        GUILayout.Label($"{UiText.Get("ui.money_holding")}: {UiText.FormatHolding(holdings.Money, holdings.Capabilities.Money)}");
        GUILayout.Label($"{UiText.Get("ui.cash_holding")}: {UiText.FormatHolding(holdings.Cash, holdings.Capabilities.Cash)}");
        GUILayout.Label($"{UiText.Get("ui.liquid_wealth")}: {UiText.FormatHolding(holdings.LiquidWealth, holdings.Capabilities.LiquidWealth)}");
        if (profile.Statistics.Holdings.HistoricalUnavailable)
            GUILayout.Label($"  {UiText.Get("ui.pre_m15_unavailable")}");
        GUILayout.Label($"{UiText.Get("ui.economy")}: {UiText.FormatEconomyCompact(model.Economy, model.CurrentEconomyCapabilities)}");
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
        var crafting = model.Crafting;
        var craftingCapabilities = model.CraftingCapabilities;
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
        GUILayout.EndVertical();
        if (layout.Columns == PanelColumnLayout.SideBySide) GUILayout.EndHorizontal();
    }

    private void DrawItems()
    {
        var model = GetProjection();
        if (model == null)
        {
            GUILayout.Label(UiText.Get("ui.profile_unavailable"));
            return;
        }

        GUILayout.Space(8);
        if (model.ItemUse.Items.Count == 0)
        {
            GUILayout.Label(UiText.Get("ui.no_items"));
            return;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label(UiText.Get("ui.item_name"), GUILayout.Width(250));
        GUILayout.Label(UiText.Get("ui.group"), GUILayout.Width(140));
        GUILayout.Label(UiText.Get("ui.effects"), GUILayout.Width(150));
        GUILayout.Label(UiText.Get("ui.activations"), GUILayout.Width(90));
        GUILayout.Label(UiText.Get("ui.actual_hp"), GUILayout.Width(120));
        GUILayout.Label(UiText.Get("ui.amount"));
        GUILayout.EndHorizontal();
        itemScroll = GUILayout.BeginScrollView(itemScroll);
        var page = CreatePage(model.ItemUse.Items, StatisticsPanelTab.ItemUse);
        foreach (var item in page.Items)
        {
            GUILayout.BeginHorizontal();
            DrawItemIcon(item.ItemId);
            GUILayout.Label(item.DisplayName, GUILayout.Width(210));
            GUILayout.Label(item.Group.ToString(), GUILayout.Width(140));
            GUILayout.Label(
                item.EffectTags.Count == 0 ? UiText.Get("ui.unavailable") : string.Join(", ", item.EffectTags),
                GUILayout.Width(150));
            GUILayout.Label(item.Totals.ActivationCount.ToString(CultureInfo.InvariantCulture), GUILayout.Width(90));
            GUILayout.Label(FormatHealing(item.Totals), GUILayout.Width(120));
            GUILayout.Label(FormatAmounts(item.Totals));
            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();
        DrawPageControls(page, StatisticsPanelTab.ItemUse);
        GUILayout.Space(8);
        GUILayout.Label(UiText.Get("ui.group_totals"));
        foreach (var group in model.ItemUse.Groups.Where(value => value.Uses > 0))
            GUILayout.Label($"{group.Group}: {group.Uses.ToString(CultureInfo.InvariantCulture)}");
        GUILayout.Space(8);
        GUILayout.Label(UiText.Get("ui.recent_runs"));
        foreach (var run in model.ItemUse.RecentRuns.Take(5))
            GUILayout.Label($"{UiText.FormatRoute(run)} ({run.Outcome}): {run.ItemStatistics.Overall.ActivationCount.ToString(CultureInfo.InvariantCulture)} uses, {FormatHealing(run.ItemStatistics.Overall)} HP");
    }

    private void DrawRuns()
    {
        var projectionModel = GetProjection();
        if (projectionModel == null)
        {
            GUILayout.Label(UiText.Get("ui.profile_unavailable"));
            return;
        }

        var model = projectionModel.Runs;
        var currentEconomyCapabilities = projectionModel.CurrentEconomyCapabilities;
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
        var page = CreatePage(model.RunRows, StatisticsPanelTab.Runs);
        foreach (var row in page.Items)
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
        DrawPageControls(page, StatisticsPanelTab.Runs);
    }

    private void DrawRecords()
    {
        var projectionModel = GetProjection();
        if (projectionModel == null)
        {
            GUILayout.Label(UiText.Get("ui.profile_unavailable"));
            return;
        }

        var model = projectionModel.Runs;
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

        var mapPage = CreatePage(model.Maps, StatisticsPanelTab.Records);
        foreach (var map in mapPage.Items)
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
        DrawPageControls(mapPage, StatisticsPanelTab.Records);
    }

    private void DrawCombat()
    {
        var projectionModel = GetProjection();
        if (projectionModel == null)
        {
            GUILayout.Label(UiText.Get("ui.profile_unavailable"));
            return;
        }

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        DrawCombatSectionButton(CombatPanelSection.Summary, UiText.Get("ui.summary"));
        DrawCombatSectionButton(CombatPanelSection.Enemies, UiText.Get("ui.enemies"));
        DrawCombatSectionButton(CombatPanelSection.WeaponsAndAmmunition, UiText.Get("ui.weapons_ammo"));
        DrawCombatSectionButton(CombatPanelSection.IncomingDamage, UiText.Get("ui.incoming_damage"));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        combatScroll = GUILayout.BeginScrollView(combatScroll);

        var weapons = projectionModel.Weapons;
        var damage = projectionModel.Combat;
        switch (interaction.CombatSection)
        {
            case CombatPanelSection.Summary:
                GUILayout.Label(UiText.Get("ui.damage_contract"));
                GUILayout.Label($"{UiText.Get("ui.damage_dealt")}: {UiText.FormatMetric(damage.Lifetime.Totals.DamageDealt, damage.Capabilities.DamageDealt.State)}");
                GUILayout.Label($"{UiText.Get("ui.damage_received")}: {UiText.FormatMetric(damage.Lifetime.Totals.DamageReceived, damage.Capabilities.DamageReceived.State)}");
                GUILayout.Label($"{UiText.Get("ui.accuracy")}: {FormatAccuracy(damage)}");
                GUILayout.Label($"{UiText.Get("ui.melee")}: {UiText.FormatMetric(damage.Lifetime.Totals.MeleeSwings, damage.Capabilities.MeleeSwings.State)} / {UiText.FormatMetric(damage.Lifetime.Totals.MeleeHits, damage.Capabilities.MeleeHits.State)}");
                GUILayout.Label($"{UiText.Get("ui.kills_by_you")}: {UiText.FormatMetric(damage.Lifetime.Totals.KillsByYou, damage.Capabilities.KillsByYou.State)}");
                GUILayout.Label($"{UiText.Get("ui.observed_world_deaths")}: {UiText.FormatMetric(damage.Lifetime.Totals.ObservedWorldDeaths, damage.Capabilities.ObservedWorldDeaths.State)}");
                GUILayout.Label($"{UiText.Get("ui.headshots")}: {UiText.FormatMetric(damage.Lifetime.Totals.Headshots, damage.Capabilities.Headshots.State)} / {UiText.FormatMetric(damage.Lifetime.Totals.HeadshotFinalBlows, damage.Capabilities.HeadshotFinalBlows.State)}");
                GUILayout.Space(8);
                GUILayout.Label(UiText.Get("ui.metric_contract"));
                GUILayout.Label($"{UiText.Get("ui.firing_actions")}: {UiText.FormatMetric(weapons.Lifetime.Totals.FiringActions, weapons.Capabilities.FiringActions.State)}");
                GUILayout.Label($"{UiText.Get("ui.ammunition_consumed")}: {UiText.FormatMetric(weapons.Lifetime.Totals.AmmunitionUnitsConsumed, weapons.Capabilities.AmmunitionConsumption.State)}");
                GUILayout.Label($"{UiText.Get("ui.projectiles")}: {UiText.FormatMetric(weapons.Lifetime.Totals.Projectiles, weapons.Capabilities.Projectiles.State)}");
                if (damage.Lifetime.Totals.LegacyUnclassifiedDeaths > 0 || damage.Lifetime.HistoricalOwnershipUnavailable)
                {
                    GUILayout.Label($"{UiText.Get("ui.legacy_unclassified_deaths")}: {damage.Lifetime.Totals.LegacyUnclassifiedDeaths.ToString(CultureInfo.InvariantCulture)}");
                    GUILayout.Label($"{UiText.Get("ui.historical_ownership_unavailable")}: {damage.Lifetime.HistoricalOwnershipProvenance}");
                }
                break;

            case CombatPanelSection.Enemies:
                if (damage.Enemies.Count == 0)
                {
                    GUILayout.Label(UiText.Get("ui.no_combat"));
                    break;
                }
                var enemyPage = CreatePage(damage.Enemies, StatisticsPanelTab.Combat);
                foreach (var enemy in enemyPage.Items)
                    GUILayout.Label($"{enemy.DisplayName} [{enemy.Id}]: {UiText.FormatMetric(enemy.Totals.DamageCaused, damage.Capabilities.EnemyIdentity.State)} damage, {UiText.FormatMetric(enemy.Totals.KillsByYou, damage.Capabilities.KillsByYou.State)} kills by you, {UiText.FormatMetric(enemy.Totals.ObservedWorldDeaths, damage.Capabilities.ObservedWorldDeaths.State)} observed deaths");
                GUILayout.Space(8);
                GUILayout.Label(UiText.Get("ui.ownership"));
                foreach (var ownership in damage.Ownership)
                    GUILayout.Label($"{ownership.DisplayName}: {UiText.FormatMetric(ownership.Totals.ObservedWorldDeaths, damage.Capabilities.ObservedWorldDeaths.State)} observed deaths, {UiText.FormatMetric(ownership.Totals.DamageCaused, damage.Capabilities.Ownership.State)} damage");
                DrawPageControls(enemyPage, StatisticsPanelTab.Combat);
                break;

            case CombatPanelSection.WeaponsAndAmmunition:
                GUILayout.Label($"Weapon-ammunition pairing: {weapons.Capabilities.WeaponAmmunitionPairing.State}");
                if (weapons.Lifetime.HistoricalPairingUnavailable)
                    GUILayout.Label($"Historical pairing unavailable: {weapons.Lifetime.HistoricalPairingProvenance}");
                var weaponPage = CreatePage(projectionModel.WeaponAmmunitionGroups, StatisticsPanelTab.Combat);
                foreach (var group in weaponPage.Items)
                {
                    GUILayout.BeginHorizontal();
                    DrawItemIcon(group.WeaponId);
                    GUILayout.BeginVertical();
                    GUILayout.Label($"{group.DisplayName} [{group.WeaponId}]: {group.TotalFiringActions.ToString(CultureInfo.InvariantCulture)} accepted firing actions");
                    foreach (var ammunition in group.Ammunition)
                        GUILayout.Label($"  {ammunition.Pair.AmmunitionDisplayName} [{ammunition.Pair.AmmunitionId}]: {ammunition.Pair.FiringActions.ToString(CultureInfo.InvariantCulture)} ({ammunition.PercentageWithinObservedWeaponPairs.ToString("0.##", CultureInfo.InvariantCulture)}% within observed pairs for this weapon)");
                    if (group.UncorrelatedFiringActions > 0)
                        GUILayout.Label($"  Uncorrelated: {group.UncorrelatedFiringActions.ToString(CultureInfo.InvariantCulture)} accepted firing actions");
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                }
                DrawPageControls(weaponPage, StatisticsPanelTab.Combat);
                break;

            case CombatPanelSection.IncomingDamage:
                GUILayout.Label($"{UiText.Get("ui.damage_received")}: {UiText.FormatMetric(damage.Lifetime.Totals.DamageReceived, damage.Capabilities.DamageReceived.State)}");
                GUILayout.Label($"{UiText.Get("ui.deaths")}: {UiText.FormatMetric(damage.Lifetime.Totals.PlayerDeaths, damage.Capabilities.PlayerDeaths.State)}");
                if (damage.Killers.Count == 0)
                {
                    GUILayout.Label(UiText.Get("ui.no_combat"));
                    break;
                }
                var killerPage = CreatePage(damage.Killers, StatisticsPanelTab.Combat);
                foreach (var killer in killerPage.Items)
                    GUILayout.Label($"{killer.DisplayName} [{killer.Id}]: {killer.Totals.DamageReceived.ToString("0.###", CultureInfo.InvariantCulture)} damage, {killer.Totals.PlayerDeaths.ToString(CultureInfo.InvariantCulture)} deaths");
                DrawPageControls(killerPage, StatisticsPanelTab.Combat);
                break;
        }

        GUILayout.EndScrollView();
    }

    private void DrawCombatSectionButton(CombatPanelSection section, string label)
    {
        var selected = interaction.CombatSection == section;
        if (GUILayout.Toggle(selected, label, theme.Tab, GUILayout.Width(150)) && !selected)
        {
            interaction.CombatSection = section;
            pageByTab[StatisticsPanelTab.Combat] = 0;
        }
    }

    private void DrawEquipment()
    {
        var projectionModel = GetProjection();
        if (projectionModel == null)
        {
            GUILayout.Label(UiText.Get("ui.profile_unavailable"));
            return;
        }

        var model = projectionModel.Equipment;
        var equipment = model.Lifetime;
        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        DrawEquipmentSectionButton(EquipmentPanelSection.Loadouts, UiText.Get("ui.loadouts"));
        DrawEquipmentSectionButton(EquipmentPanelSection.Weapons, UiText.Get("ui.weapons"));
        DrawEquipmentSectionButton(EquipmentPanelSection.ArmorAndGear, UiText.Get("ui.armor_and_gear"));
        DrawEquipmentSectionButton(EquipmentPanelSection.Totems, UiText.Get("ui.totems"));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Label(UiText.Get("ui.equipment_contract"));
        equipmentScroll = GUILayout.BeginScrollView(equipmentScroll);

        switch (interaction.EquipmentSection)
        {
            case EquipmentPanelSection.Loadouts:
                GUILayout.Label(UiText.Get("ui.recurring_loadouts"));
                var loadoutPage = CreatePage(projectionModel.RecurringLoadouts, StatisticsPanelTab.Equipment);
                foreach (var row in loadoutPage.Items)
                    GUILayout.Label($"{row.DisplayName}: {FormatDuration(row.ActiveDurationSeconds)}, {row.RunOccurrences.ToString(CultureInfo.InvariantCulture)} runs");
                GUILayout.Space(8);
                GUILayout.Label(UiText.Get("ui.recent_run_loadouts"));
                foreach (var run in projectionModel.RecentEquipmentRuns)
                {
                    GUILayout.Label($"{UiText.FormatRoute(run)} / {run.RunId}");
                    foreach (var row in run.EquipmentStatistics.Loadouts.Values
                                 .OrderByDescending(value => value.ActiveDurationSeconds)
                                 .ThenBy(value => value.Id, StringComparer.Ordinal)
                                 .Take(6))
                        GUILayout.Label($"  {row.DisplayName}: {FormatDuration(row.ActiveDurationSeconds)}");
                }
                DrawPageControls(loadoutPage, StatisticsPanelTab.Equipment);
                break;

            case EquipmentPanelSection.Weapons:
                if (model.Weapons.Count == 0) GUILayout.Label(UiText.Get("ui.proven_empty"));
                var weaponPage = CreatePage(model.Weapons, StatisticsPanelTab.Equipment);
                foreach (var weapon in weaponPage.Items)
                {
                    GUILayout.BeginHorizontal();
                    DrawItemIcon(weapon.WeaponId);
                    GUILayout.BeginVertical();
                    GUILayout.Label($"{weapon.DisplayName} [{weapon.WeaponId}]: {FormatDuration(weapon.TotalEquippedDurationSeconds)} total equipped");
                    foreach (var slot in weapon.CharacterSlots)
                        GUILayout.Label($"  {slot.SlotDisplayName} [{slot.SlotId}]: {FormatDuration(slot.EquippedDurationSeconds)}");
                    foreach (var group in weapon.NestedSlotGroups)
                    {
                        GUILayout.Label($"  {group.DisplayName}");
                        foreach (var row in group.Rows)
                            GUILayout.Label($"    {row.SlotDisplayName}: "
                                + (row.State == EquipmentSlotState.Empty
                                    ? UiText.FormatProvenEmpty(row.SlotDisplayName)
                                    : $"{row.ItemDisplayName} [{row.ItemId}]")
                                + $" — {FormatDuration(row.ActiveDurationSeconds)}");
                    }
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                }
                DrawPageControls(weaponPage, StatisticsPanelTab.Equipment);
                break;

            case EquipmentPanelSection.ArmorAndGear:
                GUILayout.Label($"Slots: {model.Capabilities.EquipmentSlots.State}; character slot state: {model.Capabilities.CharacterSlotState.State}; nested slot state: {model.Capabilities.NestedSlotState.State}");
                if (equipment.HistoricalCharacterSlotStateUnavailable)
                    GUILayout.Label($"Historical character-slot state unavailable: {equipment.HistoricalCharacterSlotStateProvenance}");
                var armorPage = CreatePage(model.ArmorAndGearSlots, StatisticsPanelTab.Equipment);
                foreach (var slot in armorPage.Items)
                {
                    GUILayout.Label($"{slot.SlotDisplayName} [{slot.SlotId}]");
                    foreach (var row in slot.Rows)
                    {
                        GUILayout.BeginHorizontal();
                        if (row.State == EquipmentSlotState.Occupied) DrawItemIcon(row.ItemId);
                        GUILayout.Label("  " + (row.State == EquipmentSlotState.Empty
                            ? UiText.FormatProvenEmpty(row.SlotDisplayName)
                            : $"{row.ItemDisplayName} [{row.ItemId}]")
                            + $" — {FormatDuration(row.ActiveDurationSeconds)}");
                        GUILayout.EndHorizontal();
                    }
                }
                DrawPageControls(armorPage, StatisticsPanelTab.Equipment);
                break;

            case EquipmentPanelSection.Totems:
                GUILayout.Label($"Direct totems: {model.Capabilities.DirectTotems.State}; tote contents: {model.Capabilities.ToteContents.State}; tote activation: {model.Capabilities.ToteActivation.State}");
                GUILayout.Label(UiText.Get("ui.observed_totem_time"));
                var totemPage = CreatePage(projectionModel.TotemStates, StatisticsPanelTab.Equipment);
                foreach (var row in totemPage.Items)
                    GUILayout.Label($"{row.DisplayName}: {FormatDuration(row.ActiveDurationSeconds)}");
                GUILayout.Space(8);
                GUILayout.Label(UiText.Get("ui.proven_active_totem_time"));
                foreach (var row in projectionModel.TotemSets.Take(layout.PageSize))
                    GUILayout.Label($"{row.DisplayName}: {FormatDuration(row.ActiveDurationSeconds)}, {row.RunOccurrences.ToString(CultureInfo.InvariantCulture)} runs");
                DrawPageControls(totemPage, StatisticsPanelTab.Equipment);
                break;
        }

        GUILayout.EndScrollView();
    }

    private void DrawEquipmentSectionButton(EquipmentPanelSection section, string label)
    {
        var selected = interaction.EquipmentSection == section;
        if (GUILayout.Toggle(selected, label, theme.Tab, GUILayout.Width(135)) && !selected)
        {
            interaction.EquipmentSection = section;
            pageByTab[StatisticsPanelTab.Equipment] = 0;
        }
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
        var paneHeight = layout.Columns == PanelColumnLayout.SideBySide
            ? layout.ContentHeight
            : Math.Max(180f, (layout.ContentHeight - 12f) / 2f);
        if (layout.Columns == PanelColumnLayout.SideBySide) GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(layout.Columns == PanelColumnLayout.SideBySide
            ? GUILayout.Width((layout.Width - 54f) / 2f)
            : GUILayout.ExpandWidth(true));
        DrawDiagnosticsLeft(profile, paneHeight);
        GUILayout.EndVertical();
        if (layout.Columns == PanelColumnLayout.SideBySide) GUILayout.Space(12);
        GUILayout.BeginVertical(layout.Columns == PanelColumnLayout.SideBySide
            ? GUILayout.Width((layout.Width - 54f) / 2f)
            : GUILayout.ExpandWidth(true));
        DrawDiagnosticsHealth(profile, paneHeight);
        GUILayout.EndVertical();
        if (layout.Columns == PanelColumnLayout.SideBySide) GUILayout.EndHorizontal();
    }

    private void DrawDiagnosticsLeft(ProfileDocument? profile, float height)
    {
        diagnosticScroll = GUILayout.BeginScrollView(diagnosticScroll, GUILayout.Height(height));
        GUILayout.Label(UiText.Get("ui.data_settings"), theme.Section);
        GUILayout.Label($"UDS version: {UltimateDuckovStatistics.Core.ProductInfo.Version}");
        GUILayout.Label($"Duckov version: {GetDuckovVersion()}");
        GUILayout.Label($"{UiText.Get("ui.data_path")}: {coordinator.DataRoot}");
        GUILayout.Label($"Profile path: {(string.IsNullOrWhiteSpace(coordinator.CurrentProfilePath) ? UiText.Get("ui.unavailable") : coordinator.CurrentProfilePath)}");
        GUILayout.Label($"Export root: {Path.Combine(coordinator.DataRoot, "exports")}");
        GUILayout.BeginHorizontal();
        GUILayout.Label(UiText.Get("ui.hotkey"), GUILayout.Width(120));
        hotkeyInput = GUILayout.TextField(hotkeyInput, GUILayout.Width(120));
        if (GUILayout.Button(UiText.Get("ui.apply"), GUILayout.Width(80))) ApplyHotkey();
        GUILayout.EndHorizontal();
        GUILayout.Label(UiText.Get("ui.open_hint"), theme.Muted);

        GUILayout.Space(10);
        GUILayout.Label(UiText.Get("ui.recent_issues"), theme.Section);
        var issues = coordinator.DiagnosticEntries
            .Reverse()
            .Where(entry => string.Equals(entry.Severity, "Warning", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(entry.Severity, "Error", StringComparison.OrdinalIgnoreCase))
            .Take(12)
            .ToArray();
        if (issues.Length == 0) GUILayout.Label(UiText.Get("ui.no_recent_issues"));
        foreach (var issue in issues)
        {
            GUILayout.Label($"{FormatTimestamp(issue.TimestampUtc)} [{issue.Severity}] {issue.Message}");
            GUILayout.Label($"  {UiText.Get("ui.issue_guidance")}", theme.Muted);
        }

        GUILayout.Space(10);
        if (GUILayout.Button(
                $"{(technicalDetailsExpanded ? "−" : "+")} {UiText.Get("ui.technical_details")}"))
            technicalDetailsExpanded = !technicalDetailsExpanded;
        if (technicalDetailsExpanded)
        {
            GUILayout.Label($"Latest profile open/recovery result: {coordinator.LastOpenStatus}");
            if (profile != null)
            {
                GUILayout.Label($"Generation: {profile.GenerationId}; revision: {profile.Revision.ToString(CultureInfo.InvariantCulture)}; reason: {profile.GenerationReason}");
                GUILayout.Label($"Schema: profile {profile.SchemaVersion.ToString(CultureInfo.InvariantCulture)}, statistics {profile.Statistics.SchemaVersion.ToString(CultureInfo.InvariantCulture)}");
                GUILayout.Label($"Interrupted sessions recovered: {profile.InterruptedSessionCount.ToString(CultureInfo.InvariantCulture)}");
                GUILayout.Label($"Economy history: {(profile.Statistics.Economy.HistoricalUnavailable ? "limited" : "complete from generation start")}; repair: {(profile.Statistics.Economy.WasRepairedFromInvalidState ? "present" : "none")}");
                GUILayout.Label($"World-time history: {(profile.Statistics.WorldTime.HistoricalUnavailable ? "limited" : "complete from generation start")}; repair: {(profile.Statistics.WorldTime.WasRepairedFromInvalidState ? "present" : "none")}");
                GUILayout.Label($"Crafting history: {(profile.Statistics.Crafting.HistoricalUnavailable ? "limited" : "complete from generation start")}; resource history: {(profile.Statistics.Crafting.ResourceHistoryUnavailable ? "limited" : "complete from generation start")}");
            }
        }

        if (GUILayout.Button($"{(diagnosticLogExpanded ? "−" : "+")} {UiText.Get("ui.diagnostic_log")}"))
            diagnosticLogExpanded = !diagnosticLogExpanded;
        if (diagnosticLogExpanded)
        {
            foreach (var entry in coordinator.DiagnosticEntries.Reverse().Take(50))
                GUILayout.Label($"{FormatTimestamp(entry.TimestampUtc)} [{entry.Severity}] {entry.Message}");
        }
        GUILayout.EndScrollView();
    }

    private void DrawDiagnosticsHealth(ProfileDocument? profile, float height)
    {
        diagnosticHealthScroll = GUILayout.BeginScrollView(diagnosticHealthScroll, GUILayout.Height(height));
        GUILayout.Label(UiText.Get("ui.tracking_health"), theme.Section);
        GUILayout.Label(
            profile == null ? $"{UiText.Get("ui.error")}: {UiText.Get("ui.profile_unavailable")}" : UiText.Get("ui.working"));
        GUILayout.Label(UiText.Get("ui.health_legend"), theme.Muted);
        if (profile != null)
        {
            DrawMenuAccessHealth();
            DrawRuntimeIssueHealth();
            foreach (var group in profile.Capabilities
                         .GroupBy(value => CapabilityFamily(value.AdapterId), StringComparer.Ordinal)
                         .OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                var values = group.OrderBy(value => value.AdapterId, StringComparer.Ordinal).ToArray();
                var status = values.All(value => value.State == AdapterCapabilityState.Supported)
                    ? UiText.Get("ui.working")
                    : UiText.Get("ui.limited");
                var expanded = expandedCapabilityGroups.Contains(group.Key);
                if (GUILayout.Button($"{(expanded ? "−" : "+")} {group.Key}: {status}"))
                {
                    if (!expandedCapabilityGroups.Add(group.Key)) expandedCapabilityGroups.Remove(group.Key);
                    expanded = !expanded;
                }
                if (!expanded) continue;
                foreach (var capability in values)
                {
                    GUILayout.Label($"{capability.AdapterId}: {capability.State} ({capability.Version})");
                    if (!string.IsNullOrWhiteSpace(capability.Detail))
                        GUILayout.Label($"  {capability.Detail}", theme.Muted);
                }
            }
        }
        GUILayout.EndScrollView();
    }

    private void DrawMenuAccessHealth()
    {
        const string groupName = "Menu access";
        var limited = nativeUi.MainMenuState == NativeMenuIntegrationState.Unavailable
                      || nativeUi.BasePauseMenuState == NativeMenuIntegrationState.Unavailable;
        var status = limited ? UiText.Get("ui.limited") : UiText.Get("ui.working");
        var expanded = expandedCapabilityGroups.Contains(groupName);
        if (GUILayout.Button($"{(expanded ? "−" : "+")} {UiText.Get("ui.menu_access")}: {status}"))
        {
            if (!expandedCapabilityGroups.Add(groupName)) expandedCapabilityGroups.Remove(groupName);
            expanded = !expanded;
        }
        if (!expanded) return;
        GUILayout.Label($"{UiText.Get("ui.main_menu_entry")}: {FormatMenuIntegrationState(nativeUi.MainMenuState)}");
        GUILayout.Label($"{UiText.Get("ui.base_pause_entry")}: {FormatMenuIntegrationState(nativeUi.BasePauseMenuState)}");
        GUILayout.Label($"{UiText.Get("ui.hotkey_fallback")}: {UiText.Get("ui.working")}");
        if (limited) GUILayout.Label(UiText.Get("ui.menu_access_limited"), theme.Muted);
    }

    private static string FormatMenuIntegrationState(NativeMenuIntegrationState state) => state switch
    {
        NativeMenuIntegrationState.Available => UiText.Get("ui.working"),
        NativeMenuIntegrationState.Unavailable => UiText.Get("ui.limited"),
        _ => UiText.Get("ui.not_observed")
    };

    private void DrawRuntimeIssueHealth()
    {
        var errors = coordinator.DiagnosticEntries
            .Reverse()
            .Where(entry => string.Equals(entry.Severity, "Error", StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToArray();
        if (errors.Length == 0) return;
        const string groupName = "Runtime issues";
        var expanded = expandedCapabilityGroups.Contains(groupName);
        if (GUILayout.Button($"{(expanded ? "−" : "+")} {UiText.Get("ui.runtime_issues")}: {UiText.Get("ui.error")}"))
        {
            if (!expandedCapabilityGroups.Add(groupName)) expandedCapabilityGroups.Remove(groupName);
            expanded = !expanded;
        }
        if (!expanded) return;
        foreach (var error in errors)
            GUILayout.Label($"{FormatTimestamp(error.TimestampUtc)} {error.Message}", theme.Muted);
        GUILayout.Label(UiText.Get("ui.issue_guidance"), theme.Muted);
    }

    private static string CapabilityFamily(string adapterId)
    {
        if (adapterId.Contains("crafting", StringComparison.Ordinal)) return "Crafting";
        if (adapterId.Contains("economy", StringComparison.Ordinal)) return "Economy";
        if (adapterId.Contains("equipment", StringComparison.Ordinal)) return "Equipment";
        if (adapterId.Contains("combat", StringComparison.Ordinal)) return "Combat";
        if (adapterId.Contains("weapon", StringComparison.Ordinal)
            || adapterId.Contains("ammunition", StringComparison.Ordinal)) return "Weapons & ammunition";
        if (adapterId.Contains("world-time", StringComparison.Ordinal)) return "World time";
        if (adapterId.Contains("container", StringComparison.Ordinal)) return "Containers";
        if (adapterId.Contains("healing", StringComparison.Ordinal)) return "Healing";
        if (adapterId.Contains("item-use", StringComparison.Ordinal)) return "Item Use";
        if (adapterId.Contains("run", StringComparison.Ordinal)
            || adapterId.Contains("movement", StringComparison.Ordinal)
            || adapterId.Contains("map", StringComparison.Ordinal)
            || adapterId.Contains("route", StringComparison.Ordinal)) return "Runs & routes";
        return "Profile & lifecycle";
    }

    private static string FormatTimestamp(DateTime timestampUtc) =>
        timestampUtc.ToUniversalTime().ToString("yyyy-MM-dd - HH:mm:ss", CultureInfo.InvariantCulture);

    private void DrawEconomy()
    {
        var projectionModel = GetProjection();
        if (projectionModel == null)
        {
            GUILayout.Label(UiText.Get("ui.profile_unavailable"));
            return;
        }
        var profile = projectionModel.Profile;
        var economy = projectionModel.Economy;
        var currentEconomyCapabilities = projectionModel.CurrentEconomyCapabilities;
        GUILayout.Space(8);
        var holdings = projectionModel.Holdings;
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
        GUILayout.Label(UiText.Get("ui.recent_run_economy"));
        foreach (var run in projectionModel.RecentEconomyRuns.Take(8))
            GUILayout.Label(
                $"  {FormatTimestamp(run.EndedUtc)} {run.Outcome}: "
                + $"{UiText.FormatEconomyCompact(run.Economy, currentEconomyCapabilities)}; "
                + UiText.FormatCashOutcome(run.Economy, currentEconomyCapabilities));
        GUILayout.EndScrollView();
    }

    private void DrawCrafting()
    {
        var projectionModel = GetProjection();
        if (projectionModel == null)
        {
            GUILayout.Label(UiText.Get("ui.profile_unavailable"));
            return;
        }
        var crafting = projectionModel.Crafting;
        var capabilities = projectionModel.CraftingCapabilities;
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
        var outputPage = BoundedPageFactory.Create(
            projectionModel.CraftingOutputs,
            craftingOutputPage,
            Math.Max(6, layout.PageSize / 2));
        craftingOutputPage = outputPage.PageIndex;
        foreach (var outputProjection in outputPage.Items)
        {
            var output = outputProjection.Output;
            DrawItemIcon(output.OutputItemId);
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
        DrawIndependentPageControls(outputPage, page => craftingOutputPage = page);
        GUILayout.Space(12);
        GUILayout.Label(UiText.Get("ui.crafting_resources"));
        if (crafting.Resources.Count == 0) GUILayout.Label(UiText.Get("ui.no_crafting_resources"));
        var resourcePage = BoundedPageFactory.Create(
            projectionModel.CraftingResources,
            craftingResourcePage,
            Math.Max(6, layout.PageSize / 2));
        craftingResourcePage = resourcePage.PageIndex;
        foreach (var resourceProjection in resourcePage.Items)
        {
            var resource = resourceProjection.Resource;
            DrawItemIcon(resource.ResourceItemId);
            GUILayout.Label(
                $"{resource.DisplayName} [{resource.ResourceItemId}]: "
                + UiText.FormatCraftingCount(resource.ConsumedQuantity, capabilities.ItemResourceIdentity));
            foreach (var association in resourceProjection.Outputs)
            {
                GUILayout.Label(
                    $"  {association.DisplayName} [{association.OutputItemId}]: "
                    + UiText.FormatCraftingCount(
                        association.ConsumedQuantity,
                        capabilities.ItemResourceIdentity,
                        capabilities.OutputResourceAssociation)
                    + " consumed for "
                    + UiText.FormatCraftingCount(
                        association.ProducedQuantity,
                        capabilities.ProducedQuantity,
                        capabilities.OutputResourceAssociation)
                    + " produced item(s)");
            }
        }
        DrawIndependentPageControls(resourcePage, page => craftingResourcePage = page);
        GUILayout.EndScrollView();
    }

    private static long ParseCraftingBatch(string value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity)
            ? quantity
            : long.MaxValue;

    private static string GetDuckovVersion()
    {
        try
        {
            return Duckov.GameMetaData.Instance?.Version.ToString() ?? UiText.Get("ui.unavailable");
        }
        catch
        {
            return UiText.Get("ui.unavailable");
        }
    }

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

    private BoundedPage<T> CreatePage<T>(IReadOnlyList<T> source, StatisticsPanelTab owner)
    {
        pageByTab.TryGetValue(owner, out var requestedPage);
        return BoundedPageFactory.Create(source, requestedPage, layout.PageSize);
    }

    private void DrawPageControls<T>(BoundedPage<T> page, StatisticsPanelTab owner)
    {
        pageByTab[owner] = page.PageIndex;
        if (page.PageCount <= 1) return;
        DrawOverflowCue(page.PageIndex > 0, page.PageIndex + 1 < page.PageCount);
        GUILayout.BeginHorizontal();
        GUI.enabled = page.PageIndex > 0;
        if (GUILayout.Button(UiText.Get("ui.previous"), GUILayout.Width(100)))
            pageByTab[owner] = page.PageIndex - 1;
        GUI.enabled = true;
        GUILayout.FlexibleSpace();
        GUILayout.Label(
            $"{UiText.Get("ui.page")} {(page.PageIndex + 1).ToString(CultureInfo.InvariantCulture)} / {page.PageCount.ToString(CultureInfo.InvariantCulture)}");
        GUILayout.FlexibleSpace();
        GUI.enabled = page.PageIndex + 1 < page.PageCount;
        if (GUILayout.Button(UiText.Get("ui.next"), GUILayout.Width(100)))
            pageByTab[owner] = page.PageIndex + 1;
        GUI.enabled = true;
        GUILayout.EndHorizontal();
    }

    private void DrawIndependentPageControls<T>(BoundedPage<T> page, Action<int> selectPage)
    {
        if (page.PageCount <= 1) return;
        DrawOverflowCue(page.PageIndex > 0, page.PageIndex + 1 < page.PageCount);
        GUILayout.BeginHorizontal();
        GUI.enabled = page.PageIndex > 0;
        if (GUILayout.Button(UiText.Get("ui.previous"), GUILayout.Width(100)))
            selectPage(page.PageIndex - 1);
        GUI.enabled = true;
        GUILayout.FlexibleSpace();
        GUILayout.Label(
            $"{UiText.Get("ui.page")} {(page.PageIndex + 1).ToString(CultureInfo.InvariantCulture)} / {page.PageCount.ToString(CultureInfo.InvariantCulture)}");
        GUILayout.FlexibleSpace();
        GUI.enabled = page.PageIndex + 1 < page.PageCount;
        if (GUILayout.Button(UiText.Get("ui.next"), GUILayout.Width(100)))
            selectPage(page.PageIndex + 1);
        GUI.enabled = true;
        GUILayout.EndHorizontal();
    }

    private void DrawOverflowCue(bool hasMoreAbove, bool hasMoreBelow)
    {
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (hasMoreAbove) GUILayout.Label(UiText.Get("ui.more_above"), theme.Muted);
        if (hasMoreAbove && hasMoreBelow) GUILayout.Label("  ", theme.Muted);
        if (hasMoreBelow) GUILayout.Label(UiText.Get("ui.more_below"), theme.Muted);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    private void DrawItemIcon(string stableItemId, float size = 30f)
    {
        var rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
        var sprite = iconResolver.Resolve(stableItemId);
        if (sprite == null || sprite.texture == null)
        {
            GUI.Box(rect, "?");
            return;
        }

        try
        {
            var textureRect = sprite.textureRect;
            var texture = sprite.texture;
            var coordinates = new Rect(
                textureRect.x / texture.width,
                textureRect.y / texture.height,
                textureRect.width / texture.width,
                textureRect.height / texture.height);
            GUI.DrawTextureWithTexCoords(rect, texture, coordinates, alphaBlend: true);
        }
        catch
        {
            GUI.Box(rect, "?");
        }
    }

    private void DrawActions()
    {
        GUILayout.BeginHorizontal();
        GUI.enabled = operationGate.Current == PanelOperation.None && !interaction.ResetConfirmationVisible;
        if (GUILayout.Button(UiText.Get("ui.export"), GUILayout.Width(180)))
        {
            if (!operationGate.TryBegin(PanelOperation.Export))
            {
                ShowStatus(UiText.Get("ui.operation_busy"));
            }
            else
            {
                try
                {
                    var result = coordinator.ExportCurrent();
                    GUIUtility.systemCopyBuffer = result.Directory;
                    var success = $"{UiText.Get("ui.export_path_copied")} {result.Directory}";
                    ShowStatus(success, seconds: 8);
                    nativeUi.ShowToast(UiText.Get("ui.export_complete"));
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    coordinator.ReportUiDiagnostic(
                        $"M17 UI export failed: {exception.GetType().Name}: {exception.Message}",
                        "Error");
                    ShowStatus(UiText.Get("ui.export_failed"), seconds: 8);
                    nativeUi.ShowToast(UiText.Get("ui.export_failed"));
                }
                finally
                {
                    operationGate.Complete(PanelOperation.Export);
                }
            }
        }

        if (!interaction.ResetConfirmationVisible)
        {
            if (GUILayout.Button(UiText.Get("ui.reset"), GUILayout.Width(190)))
            {
                interaction.ShowResetConfirmation();
            }
        }
        else
        {
            GUILayout.Label(UiText.Get("ui.reset_warning"));
            GUI.enabled = operationGate.Current == PanelOperation.None;
            GUI.SetNextControlName("uds-reset-cancel");
            if (GUILayout.Button(UiText.Get("ui.cancel"), GUILayout.Width(90)))
                interaction.CancelModal();
            if (interaction.ResetCancelHasInitialFocus)
            {
                GUI.FocusControl("uds-reset-cancel");
                interaction.ConsumeInitialResetFocus();
            }

            if (GUILayout.Button(UiText.Get("ui.confirm_reset"), GUILayout.Width(120)))
            {
                if (!operationGate.TryBegin(PanelOperation.Reset))
                {
                    ShowStatus(UiText.Get("ui.operation_busy"));
                }
                else
                {
                    try
                    {
                        var completed = coordinator.ResetCurrent();
                        interaction.CancelModal();
                        projection = null;
                        projectedGenerationId = string.Empty;
                        projectedRevision = -1;
                        if (completed)
                        {
                            operationGate.Complete(PanelOperation.Reset);
                            ShowStatus(UiText.Get("ui.reset_complete"), seconds: 8);
                            nativeUi.ShowToast(UiText.Get("ui.reset_complete"));
                        }
                        else
                        {
                            ShowStatus(UiText.Get("ui.reset_pending"), seconds: 8);
                            nativeUi.ShowToast(UiText.Get("ui.reset_pending"));
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                        coordinator.ReportUiDiagnostic(
                            $"M17 UI reset failed: {exception.GetType().Name}: {exception.Message}",
                            "Error");
                        ShowStatus(UiText.Get("ui.reset_failed"), seconds: 8);
                        nativeUi.ShowToast(UiText.Get("ui.reset_failed"));
                    }
                    finally
                    {
                        if (operationGate.Current == PanelOperation.Reset
                            && !coordinator.HasPendingProfileTransition)
                        {
                            operationGate.Complete(PanelOperation.Reset);
                        }
                    }
                }
            }
        }

        GUI.enabled = true;
        GUILayout.EndHorizontal();
    }

    private void ObservePendingReset()
    {
        if (operationGate.Current != PanelOperation.Reset || coordinator.HasPendingProfileTransition) return;
        operationGate.Complete(PanelOperation.Reset);
        projection = null;
        projectedGenerationId = string.Empty;
        projectedRevision = -1;
        ShowStatus(UiText.Get("ui.reset_complete"), seconds: 8);
        nativeUi.ShowToast(UiText.Get("ui.reset_complete"));
    }

    private void DrawTabButton(StatisticsPanelTab target, string key)
    {
        var wasSelected = interaction.SelectedTab == target;
        if (GUILayout.Toggle(wasSelected, UiText.Get(key), theme.Tab, GUILayout.Width(108)) && !wasSelected)
        {
            interaction.SelectTab(target);
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

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        SetVisible(false);
        nativeUi.Dispose();
        theme.Dispose();
        projection = null;
    }
}
