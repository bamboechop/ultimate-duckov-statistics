using System.Globalization;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
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
    private PanelTab tab;
    private bool visible;
    private bool confirmReset;
    private string status = string.Empty;
    private DateTime statusExpiresUtc;
    private KeyCode hotkey = KeyCode.F8;
    private string hotkeyInput = "F8";

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
        var combat = WeaponStatisticsViewModelFactory.Create(profile);
        GUILayout.Label(
            $"{UiText.Get("ui.firing_actions")}: "
            + FormatMetric(combat.Lifetime.Totals.FiringActions, combat.Capabilities.FiringActions.State));
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
        GUILayout.Space(8);
        if (model.Runs.Count == 0)
        {
            GUILayout.Label(UiText.Get("ui.no_runs"));
            return;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label(UiText.Get("ui.outcome"), GUILayout.Width(95));
        GUILayout.Label(UiText.Get("ui.map"), GUILayout.Width(180));
        GUILayout.Label(UiText.Get("ui.active_time"), GUILayout.Width(110));
        GUILayout.Label(UiText.Get("ui.wall_time"), GUILayout.Width(135));
        GUILayout.Label(UiText.Get("ui.physical_distance"), GUILayout.Width(125));
        GUILayout.Label(UiText.Get("ui.teleport_distance"));
        GUILayout.EndHorizontal();
        runScroll = GUILayout.BeginScrollView(runScroll);
        foreach (var row in model.RunRows)
        {
            var run = row.Run;
            var movementSupported = run.MovementCapability == AdapterCapabilityState.Supported;
            GUILayout.BeginHorizontal();
            GUILayout.Label(run.Outcome.ToString(), GUILayout.Width(95));
            GUILayout.Label(run.MapDisplayName, GUILayout.Width(180));
            GUILayout.Label(FormatDuration(run.ActiveDurationSeconds), GUILayout.Width(110));
            GUILayout.Label(FormatDuration(run.WallClockDurationSeconds), GUILayout.Width(135));
            GUILayout.Label(FormatDistance(run.PhysicalDistance, movementSupported), GUILayout.Width(125));
            GUILayout.Label(FormatDistance(run.TeleportDistance, movementSupported));
            GUILayout.EndHorizontal();
            GUILayout.Label(
                $"  {UiText.Get("ui.integrity")}: {row.IntegrityTags}; "
                + $"{UiText.Get("ui.record_status")}: {FormatRecordEligibility(row)}");
            var combat = run.CombatStatistics;
            GUILayout.Label(
                $"  {UiText.Get("ui.damage_dealt")}: {FormatMetric(combat.Totals.DamageDealt, combat.Capabilities.DamageDealt.State)}; "
                + $"{UiText.Get("ui.damage_received")}: {FormatMetric(combat.Totals.DamageReceived, combat.Capabilities.DamageReceived.State)}; "
                + $"{UiText.Get("ui.kills")}: {FormatMetric(combat.Totals.EnemiesKilled, combat.Capabilities.EnemiesKilled.State)}; "
                + $"{UiText.Get("ui.deaths")}: {FormatMetric(combat.Totals.PlayerDeaths, combat.Capabilities.PlayerDeaths.State)}");
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
            GUILayout.Label(UiText.Get("ui.per_map"));
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
        GUILayout.Label($"{UiText.Get("ui.damage_dealt")}: {FormatMetric(damage.Lifetime.Totals.DamageDealt, damage.Capabilities.DamageDealt.State)}");
        GUILayout.Label($"{UiText.Get("ui.damage_received")}: {FormatMetric(damage.Lifetime.Totals.DamageReceived, damage.Capabilities.DamageReceived.State)}");
        GUILayout.Label($"{UiText.Get("ui.accuracy")}: {FormatAccuracy(damage)}");
        GUILayout.Label($"{UiText.Get("ui.melee")}: {FormatMetric(damage.Lifetime.Totals.MeleeSwings, damage.Capabilities.MeleeSwings.State)} / {FormatMetric(damage.Lifetime.Totals.MeleeHits, damage.Capabilities.MeleeHits.State)}");
        GUILayout.Label($"{UiText.Get("ui.kills")}: {FormatMetric(damage.Lifetime.Totals.EnemiesKilled, damage.Capabilities.EnemiesKilled.State)}; {UiText.Get("ui.deaths")}: {FormatMetric(damage.Lifetime.Totals.PlayerDeaths, damage.Capabilities.PlayerDeaths.State)}");
        GUILayout.Label($"{UiText.Get("ui.headshots")}: {FormatMetric(damage.Lifetime.Totals.Headshots, damage.Capabilities.Headshots.State)} / {FormatMetric(damage.Lifetime.Totals.HeadshotFinalBlows, damage.Capabilities.HeadshotFinalBlows.State)}");
        GUILayout.Space(8);
        GUILayout.Label(UiText.Get("ui.metric_contract"));
        GUILayout.Label(
            $"{UiText.Get("ui.firing_actions")}: "
            + FormatMetric(model.Lifetime.Totals.FiringActions, model.Capabilities.FiringActions.State));
        GUILayout.Label(
            $"{UiText.Get("ui.ammunition_consumed")}: "
            + FormatMetric(model.Lifetime.Totals.AmmunitionUnitsConsumed, model.Capabilities.AmmunitionConsumption.State));
        GUILayout.Label(
            $"{UiText.Get("ui.projectiles")}: "
            + FormatMetric(model.Lifetime.Totals.Projectiles, model.Capabilities.Projectiles.State));

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
                GUILayout.Label($"{enemy.DisplayName} [{enemy.Id}]: {enemy.Totals.DamageCaused.ToString("0.###", CultureInfo.InvariantCulture)} damage, {enemy.Totals.EnemiesKilled.ToString(CultureInfo.InvariantCulture)} kills");
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
                    $"{run.MapDisplayName} ({run.Outcome}, {run.RunId}): "
                    + $"{FormatMetric(run.WeaponStatistics.Totals.FiringActions, WeaponStatisticsReducer.RestrictAvailability(run.WeaponStatistics.Capabilities.FiringActions, model.Capabilities.FiringActions.State))} actions, "
                    + $"{FormatMetric(run.WeaponStatistics.Totals.AmmunitionUnitsConsumed, WeaponStatisticsReducer.RestrictAvailability(run.WeaponStatistics.Capabilities.AmmunitionConsumption, model.Capabilities.AmmunitionConsumption.State))} ammo, "
                    + $"{FormatMetric(run.WeaponStatistics.Totals.Projectiles, WeaponStatisticsReducer.RestrictAvailability(run.WeaponStatistics.Capabilities.Projectiles, model.Capabilities.Projectiles.State))} projectiles");
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
            GUILayout.Label($"{run.MapDisplayName} / {run.RunId}");
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
        GUILayout.Label(FormatMetric(totals.FiringActions, capabilities.FiringActions.State), GUILayout.Width(125));
        GUILayout.Label(FormatMetric(totals.AmmunitionUnitsConsumed, capabilities.AmmunitionConsumption.State), GUILayout.Width(190));
        GUILayout.Label(FormatMetric(totals.Projectiles, capabilities.Projectiles.State));
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
        GUILayout.Label($"{UiText.Get("ui.data_path")}: {coordinator.DataRoot}");
        GUILayout.Space(6);
        GUILayout.Label(UiText.Get("ui.capabilities"));
        if (profile != null)
        {
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
        diagnosticScroll = GUILayout.BeginScrollView(diagnosticScroll);
        foreach (var entry in coordinator.DiagnosticEntries.Reverse().Take(50))
        {
            GUILayout.Label(
                $"{entry.TimestampUtc.ToString("u", CultureInfo.InvariantCulture)} " +
                $"[{entry.Severity}] {entry.Message}");
        }

        GUILayout.EndScrollView();
        GUILayout.BeginHorizontal();
        GUILayout.Label(UiText.Get("ui.hotkey"), GUILayout.Width(120));
        hotkeyInput = GUILayout.TextField(hotkeyInput, GUILayout.Width(120));
        if (GUILayout.Button(UiText.Get("ui.apply"), GUILayout.Width(80)))
        {
            ApplyHotkey();
        }

        GUILayout.EndHorizontal();
        GUILayout.Label(UiText.Get("ui.open_hint"));
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
        if (GUILayout.Toggle(wasSelected, UiText.Get(key), GUI.skin.button, GUILayout.Width(110)) && !wasSelected)
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

    private static string FormatMetric(long value, AdapterCapabilityState state) =>
        state == AdapterCapabilityState.DisabledIncompatible
            ? UiText.Get("ui.unsupported")
            : value.ToString(CultureInfo.InvariantCulture);

    private static string FormatMetric(double value, AdapterCapabilityState state) =>
        state == AdapterCapabilityState.DisabledIncompatible
            ? UiText.Get("ui.unsupported")
            : value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatAccuracy(CombatStatisticsViewModel model) =>
        model.Capabilities.Accuracy.State == AdapterCapabilityState.DisabledIncompatible
            ? UiText.Get("ui.unsupported")
            : model.Accuracy.HasValue
                ? model.Accuracy.Value.ToString("P1", CultureInfo.InvariantCulture)
                : "—";

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
        Items,
        Diagnostics
    }
}
