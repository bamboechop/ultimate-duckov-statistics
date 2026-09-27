using UnityEngine;

namespace UltimateDuckovStatistics.UI;

[Flags]
internal enum PanelHotkeyModifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4 }

/// <summary>A keyboard key with an exact, side-independent modifier set.</summary>
internal readonly struct PanelHotkey
{
    public KeyCode Key { get; }
    public PanelHotkeyModifiers Modifiers { get; }
    public PanelHotkey(KeyCode key, PanelHotkeyModifiers modifiers) { Key = key; Modifiers = modifiers; }
    public static PanelHotkey Default => new(KeyCode.S, PanelHotkeyModifiers.Ctrl | PanelHotkeyModifiers.Alt);

    public static bool TryParse(string? text, out PanelHotkey value)
    {
        value = Default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text!.Split('+');
        if (parts.Length > 4) return false;
        var modifiers = PanelHotkeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i].Trim();
            var modifier = part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ? PanelHotkeyModifiers.Ctrl
                : part.Equals("Alt", StringComparison.OrdinalIgnoreCase) ? PanelHotkeyModifiers.Alt
                : part.Equals("Shift", StringComparison.OrdinalIgnoreCase) ? PanelHotkeyModifiers.Shift
                : PanelHotkeyModifiers.None;
            if (modifier == PanelHotkeyModifiers.None || (modifiers & modifier) != 0) return false;
            modifiers |= modifier;
        }
        var keyName = parts[parts.Length - 1].Trim();
        if (int.TryParse(keyName, out _) || !Enum.TryParse<KeyCode>(keyName, true, out var key)
            || !Enum.IsDefined(typeof(KeyCode), key) || !PanelHotkeyPolicy.IsAllowed(key.ToString())) return false;
        value = new PanelHotkey(key, modifiers);
        return true;
    }

    public override string ToString() => ((Modifiers & PanelHotkeyModifiers.Ctrl) != 0 ? "Ctrl+" : "")
        + ((Modifiers & PanelHotkeyModifiers.Alt) != 0 ? "Alt+" : "")
        + ((Modifiers & PanelHotkeyModifiers.Shift) != 0 ? "Shift+" : "") + Key;

    public string DisplayText => ToString().Replace("Ctrl+", UiText.Get("ui.hotkey_ctrl") + "+")
        .Replace("Shift+", UiText.Get("ui.hotkey_shift") + "+");
}

internal static class NativePanelHotkeyInput
{
    public static PanelHotkeyModifiers Modifiers =>
        (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ? PanelHotkeyModifiers.Ctrl : 0)
        | (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) ? PanelHotkeyModifiers.Alt : 0)
        | (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? PanelHotkeyModifiers.Shift : 0);

    public static bool IsModifier(KeyCode key) => key is KeyCode.LeftControl or KeyCode.RightControl
        or KeyCode.LeftAlt or KeyCode.RightAlt or KeyCode.LeftShift or KeyCode.RightShift;

    // On Windows, AltGr can arrive as Ctrl+RightAlt. Legacy Input cannot prove
    // whether that pair was intentional, so reserve it for text entry.
    public static bool UnsupportedModifiers => Input.GetKey(KeyCode.AltGr)
        || Input.GetKey(KeyCode.RightAlt) && (Modifiers & PanelHotkeyModifiers.Ctrl) != 0
        || Input.GetKey(KeyCode.LeftWindows) || Input.GetKey(KeyCode.RightWindows)
        || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);

    public static bool Pressed(PanelHotkey hotkey) => Input.GetKeyDown(hotkey.Key)
        && Modifiers == hotkey.Modifiers && !UnsupportedModifiers;

    public static bool TextInputFocused()
    {
        var selected = GameManager.EventSystem?.currentSelectedGameObject;
        if (selected == null || !selected.activeInHierarchy) return false;
        // Native DigitInputPanel and CustomFaceSlider use TMP_InputField;
        // Unity InputField also covers standard mod-provided search fields.
        var tmp = selected.GetComponent<TMPro.TMP_InputField>() ?? selected.transform.GetComponentInParent<TMPro.TMP_InputField>();
        if (tmp != null && tmp.isActiveAndEnabled && tmp.isFocused) return true;
        var legacy = selected.GetComponent<UnityEngine.UI.InputField>() ?? selected.transform.GetComponentInParent<UnityEngine.UI.InputField>();
        return legacy != null && legacy.isActiveAndEnabled && legacy.isFocused;
    }
}
