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
        GUILayout.Label($"{UiText.Get("ui.amount")}: {FormatAmounts(profile.Statistics.Overall)}");
        GUILayout.Label($"{UiText.Get("ui.interrupted_sessions")}: {profile.InterruptedSessionCount.ToString(CultureInfo.InvariantCulture)}");
        GUILayout.Space(12);
        GUILayout.Label(UiText.Get("ui.group_totals"));
        foreach (var group in profile.Statistics.Groups.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            GUILayout.Label(
                $"{group.Key}: {group.Value.ActivationCount.ToString(CultureInfo.InvariantCulture)} " +
                $"({FormatAmounts(group.Value)})");
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
            GUILayout.Label(FormatAmounts(item.Totals));
            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();
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

    private enum PanelTab
    {
        Overview,
        Items,
        Diagnostics
    }
}
