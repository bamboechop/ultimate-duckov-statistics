namespace UltimateDuckovStatistics.UI;

internal static class UiText
{
    private static readonly Dictionary<string, string> English =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ui.title"] = "Ultimate Duckov Statistics",
            ["ui.close"] = "Close",
            ["ui.overview"] = "Overview",
            ["ui.items"] = "Items",
            ["ui.diagnostics"] = "Diagnostics",
            ["ui.total_uses"] = "Successful raid uses",
            ["ui.save_slot"] = "Save slot",
            ["ui.generation"] = "UDS generation",
            ["ui.interrupted_sessions"] = "Interrupted sessions recovered",
            ["ui.group_totals"] = "Canonical groups",
            ["ui.no_items"] = "No successful raid item uses recorded for this save generation.",
            ["ui.item_name"] = "Item",
            ["ui.group"] = "Group",
            ["ui.activations"] = "Activations",
            ["ui.amount"] = "Amount consumed",
            ["ui.capabilities"] = "Adapter capabilities",
            ["ui.diagnostic_log"] = "Recent bounded diagnostics",
            ["ui.data_path"] = "Data path",
            ["ui.export"] = "Export JSON + CSV",
            ["ui.reset"] = "Reset this UDS profile",
            ["ui.reset_warning"] = "Reset archives the current UDS generation read-only and starts at zero. Duckov saves are not changed.",
            ["ui.confirm_reset"] = "Confirm reset",
            ["ui.cancel"] = "Cancel",
            ["ui.hotkey"] = "Panel hotkey",
            ["ui.apply"] = "Apply",
            ["ui.hotkey_invalid"] = "Unknown Unity key name; hotkey was not changed.",
            ["ui.hotkey_saved"] = "Panel hotkey saved.",
            ["ui.raid_unavailable"] = "Statistics are available outside raids.",
            ["ui.export_complete"] = "Export complete",
            ["ui.export_failed"] = "Export failed; see Diagnostics and Player.log.",
            ["ui.reset_complete"] = "UDS profile reset; prior generation archived read-only.",
            ["ui.integrity_note"] = "Only successful raid uses count. Base, cancelled, interrupted, and failed uses do not count.",
            ["ui.open_hint"] = "Press the configured hotkey outside raids to show or hide this panel."
        };

    public static string Get(string key) => English.TryGetValue(key, out var value) ? value : key;
}
