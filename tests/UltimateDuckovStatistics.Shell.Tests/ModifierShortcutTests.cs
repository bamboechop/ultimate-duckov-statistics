using System.Reflection;
using SodaCraft.Localizations;
using TMPro;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed partial class ShellAccessTests
{
    public static IEnumerable<object[]> ModifierBindings()
    {
        for (var mask = 0; mask < 8; mask++)
        {
            var keys = new List<KeyCode>();
            if ((mask & 1) != 0) keys.Add(KeyCode.LeftControl);
            if ((mask & 2) != 0) keys.Add(KeyCode.LeftAlt);
            if ((mask & 4) != 0) keys.Add(KeyCode.LeftShift);
            yield return new object[] { mask, keys.ToArray() };
        }
    }

    [Theory]
    [MemberData(nameof(ModifierBindings))]
    public void CapturedShortcutRoundTripsAndRequiresExactModifiers(int mask, KeyCode[] modifiers)
    {
        using (var panel = new NativeStatisticsPanel(coordinator))
        {
            Press(panel, KeyCode.F8);
            BeginCapture(panel);
            // Modifier-only presses keep capture open without a spurious error.
            foreach (var modifier in modifiers)
            {
                Input.Held.Add(modifier);
                Press(panel, modifier);
                Assert.True(Field<bool>(panel, "capturingHotkey"));
                Assert.Empty(Field<string>(panel, "hotkeyWarning"));
            }
            Input.Held.Add(KeyCode.S);
            Press(panel, KeyCode.S);
            Assert.False(Field<bool>(panel, "capturingHotkey"));
            Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
            Assert.Equal((PanelHotkeyModifiers)mask, Field<PanelHotkey>(panel, "hotkey").Modifiers);
            Tick(panel, 5); // Holding the newly assigned chord cannot close the panel.
            Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
            Input.Held.Clear();
            Press(panel, KeyCode.Escape);
        }

        var stored = new AtomicJsonStore<UserSettings>().Load(Path.Combine(fixtureRoot, "settings.json")).Value!;
        Assert.Equal(new PanelHotkey(KeyCode.S, (PanelHotkeyModifiers)mask).ToString(), stored.PanelHotkey);
        using var reloaded = new NativeStatisticsPanel(coordinator);
        for (var other = 0; other < 8; other++)
        {
            if (other == mask) continue;
            SetHeldModifiers(other);
            Press(reloaded, KeyCode.S);
            Assert.DoesNotContain(GameObject.Live, go => go.name == RetainedDimmerPolicy.RootName && go.activeInHierarchy);
        }
        SetHeldModifiers(mask);
        Press(reloaded, KeyCode.S);
        Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
        Input.Held.Add(KeyCode.S);
        Tick(reloaded, 5);
        Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
        Input.Held.Remove(KeyCode.S);
        Press(reloaded, KeyCode.S);
        Assert.False(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
        Input.Held.Clear();
    }

    [Theory]
    [InlineData("f8", "F8")]
    [InlineData("shift + CTRL + alt + s", "Ctrl+Alt+Shift+S")]
    [InlineData("Alt+K", "Alt+K")]
    [InlineData("Ctrl+S", "Ctrl+S")]
    public void SettingsNormalizeAndPreserveValidBindings(string saved, string expected)
    {
        SaveBinding(saved);
        using var panel = new NativeStatisticsPanel(coordinator);
        Assert.Equal(expected, Field<PanelHotkey>(panel, "hotkey").ToString());
        Assert.Equal(expected, new AtomicJsonStore<UserSettings>().Load(Path.Combine(fixtureRoot, "settings.json")).Value!.PanelHotkey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl+S")]
    [InlineData("Command+S")]
    [InlineData("Ctrl+Escape")]
    [InlineData("Alt+Tab")]
    [InlineData("Shift+Return")]
    [InlineData("Ctrl+LeftControl")]
    [InlineData("Ctrl+Mouse0")]
    [InlineData("Ctrl+JoystickButton0")]
    [InlineData("99999")]
    [InlineData("2")]
    [InlineData("garbage")]
    public void InvalidSettingsUseNewDefaultWithoutChangingSchema(string? saved)
    {
        File.WriteAllText(Path.Combine(fixtureRoot, "settings.json"), System.Text.Json.JsonSerializer.Serialize(new { SchemaVersion = 1, PanelHotkey = saved }));
        using var panel = new NativeStatisticsPanel(coordinator);
        Assert.Equal(UserSettings.DefaultPanelHotkey, Field<PanelHotkey>(panel, "hotkey").ToString());
        var stored = new AtomicJsonStore<UserSettings>().Load(Path.Combine(fixtureRoot, "settings.json")).Value!;
        Assert.Equal(1, stored.SchemaVersion);
        Assert.Equal(UserSettings.DefaultPanelHotkey, stored.PanelHotkey);
    }

    [Fact]
    public void FreshSettingsDefaultToCtrlAltSAndDoNotRespondToF8()
    {
        File.Delete(Path.Combine(fixtureRoot, "settings.json"));
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        Assert.DoesNotContain(GameObject.Live, go => go.name == RetainedDimmerPolicy.RootName);
        SetHeldModifiers(3);
        Press(panel, KeyCode.S);
        Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
    }

    [Theory]
    [InlineData("Ctrl+Shift+S", KeyCode.RightControl, KeyCode.RightShift)]
    [InlineData("Alt+Shift+S", KeyCode.RightAlt, KeyCode.RightShift)]
    [InlineData("Ctrl+Alt+S", KeyCode.RightControl, KeyCode.LeftAlt)]
    public void RightSideModifiersWorkExceptAmbiguousAltGrPair(string binding, KeyCode first, KeyCode second)
    {
        SaveBinding(binding);
        using var panel = new NativeStatisticsPanel(coordinator);
        Input.Held.Add(first); Input.Held.Add(second);
        Press(panel, KeyCode.S);
        Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
    }

    [Theory]
    [InlineData(KeyCode.AltGr)]
    [InlineData(KeyCode.RightAlt)]
    [InlineData(KeyCode.LeftWindows)]
    [InlineData(KeyCode.RightCommand)]
    public void UnsupportedOrAltGrModifiersCannotTriggerOrBeCaptured(KeyCode extra)
    {
        SaveBinding("Ctrl+Alt+S");
        using var panel = new NativeStatisticsPanel(coordinator);
        SetHeldModifiers(3); Input.Held.Add(extra);
        Press(panel, KeyCode.S);
        Assert.DoesNotContain(GameObject.Live, go => go.name == RetainedDimmerPolicy.RootName);
        Input.Held.Clear();
        Open(panel, PanelAccessSurface.MainMenu);
        BeginCapture(panel);
        SetHeldModifiers(3); Input.Held.Add(extra);
        Press(panel, KeyCode.S);
        Assert.True(Field<bool>(panel, "capturingHotkey"));
        Assert.Equal("Ctrl+Alt+S", Field<PanelHotkey>(panel, "hotkey").ToString());
        Assert.NotEmpty(Field<string>(panel, "hotkeyWarning"));
        Input.Held.Clear();
        Press(panel, KeyCode.Escape);
        Assert.False(Field<bool>(panel, "capturingHotkey"));
        Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TypingInFocusedInputSuppressesShortcutUntilANewKeyPress(bool tmp)
    {
        SaveBinding("Ctrl+S");
        using var panel = new NativeStatisticsPanel(coordinator);
        var field = new GameObject("Native text input");
        if (tmp) field.AddComponent<TMP_InputField>().isFocused = true;
        else field.AddComponent<InputField>().isFocused = true;
        var child = new GameObject("Input label"); child.transform.SetParent(field.transform);
        GameManager.EventSystem!.SetSelectedGameObject(child);
        Input.Held.Add(KeyCode.LeftControl); Input.Held.Add(KeyCode.S);
        Press(panel, KeyCode.S);
        Assert.DoesNotContain(GameObject.Live, go => go.name == RetainedDimmerPolicy.RootName);
        GameManager.EventSystem.SetSelectedGameObject(null);
        Tick(panel);
        Assert.DoesNotContain(GameObject.Live, go => go.name == RetainedDimmerPolicy.RootName);
        Input.Held.Remove(KeyCode.S);
        Press(panel, KeyCode.S);
        Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
    }

    [Fact]
    public void FocusReturnRequiresReleaseBeforeTogglingOrCapturing()
    {
        SaveBinding("Ctrl+S");
        using var panel = new NativeStatisticsPanel(coordinator);
        Input.Held.Add(KeyCode.LeftControl); Input.Held.Add(KeyCode.S);
        Application.isFocused = false;
        Press(panel, KeyCode.S);
        Application.isFocused = true;
        Press(panel, KeyCode.S);
        Assert.DoesNotContain(GameObject.Live, go => go.name == RetainedDimmerPolicy.RootName);
        Input.Held.Clear(); Tick(panel);
        Input.Held.Add(KeyCode.LeftControl);
        Press(panel, KeyCode.S);
        BeginCapture(panel);
        Application.isFocused = false; Tick(panel);
        Application.isFocused = true;
        Press(panel, KeyCode.K);
        Assert.True(Field<bool>(panel, "capturingHotkey"));
        Input.Held.Clear(); Tick(panel);
        Press(panel, KeyCode.K);
        Assert.Equal("K", Field<PanelHotkey>(panel, "hotkey").ToString());
    }

    [Fact]
    public void CapturingOnClickFrameDoesNotBindAndOtherModalsSuppressShortcut()
    {
        SaveBinding("Ctrl+S");
        using var panel = new NativeStatisticsPanel(coordinator);
        Input.Held.Add(KeyCode.LeftControl); Press(panel, KeyCode.S);
        typeof(NativeStatisticsPanel).GetMethod("BeginHotkeyCapture", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(panel, null);
        Input.Down.Add(KeyCode.S); panel.Tick(); Input.Down.Clear();
        Assert.True(Field<bool>(panel, "capturingHotkey"));
        Input.Held.Clear(); Press(panel, KeyCode.Escape);
        Assert.True(Field<PanelOperationController>(panel, "operations").RequestResetConfirmation());
        Input.Held.Add(KeyCode.LeftControl); Press(panel, KeyCode.S);
        Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
        Assert.True(Field<PanelOperationController>(panel, "operations").ModalVisible);
    }

    [Fact]
    public void ShortcutLabelsLocalizeWithoutChangingStoredChord()
    {
        SaveBinding("Ctrl+Alt+Shift+S");
        using var panel = new NativeStatisticsPanel(coordinator);
        try
        {
            LocalizationManager.SetLanguage(SystemLanguage.German);
            Assert.Equal("Strg+Alt+Umschalt+S", Field<PanelHotkey>(panel, "hotkey").DisplayText);
            LocalizationManager.SetLanguage(SystemLanguage.English);
            Assert.Equal("Ctrl+Alt+Shift+S", Field<PanelHotkey>(panel, "hotkey").DisplayText);
            Assert.Equal("Ctrl+Alt+Shift+S", new AtomicJsonStore<UserSettings>().Load(Path.Combine(fixtureRoot, "settings.json")).Value!.PanelHotkey);
        }
        finally { LocalizationManager.SetLanguage(SystemLanguage.English); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChordPreservesBasePauseOwnershipAndRaidRestriction(bool alreadyPaused)
    {
        SaveBinding("Ctrl+Alt+S");
        PreparePauseMenu(canvas);
        if (!alreadyPaused) PauseMenu.Hide();
        using var panel = new NativeStatisticsPanel(coordinator);
        SetHeldModifiers(3);
        Press(panel, KeyCode.S);
        Assert.True(GameManager.Paused);
        Assert.Single(InputManager.Blocks);
        Press(panel, KeyCode.S);
        Assert.Equal(alreadyPaused, GameManager.Paused);
        Assert.Empty(InputManager.Blocks);
        NativeRaidContext.InRaid = true;
        Press(panel, KeyCode.S);
        Assert.False(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
        Assert.Empty(InputManager.Blocks);
    }

    [Fact]
    public void HoldingMainKeyBeforeModifiersDoesNotTriggerUntilPressedAgain()
    {
        SaveBinding("Ctrl+Alt+S");
        using var panel = new NativeStatisticsPanel(coordinator);
        Input.Held.Add(KeyCode.S); Press(panel, KeyCode.S);
        Input.Held.Add(KeyCode.LeftControl); Input.Held.Add(KeyCode.LeftAlt);
        Tick(panel);
        Assert.DoesNotContain(GameObject.Live, go => go.name == RetainedDimmerPolicy.RootName);
        Input.Held.Remove(KeyCode.S); Press(panel, KeyCode.S);
        Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
    }

    [Fact]
    public void FailedShortcutSaveLeavesPreviousBindingAndCaptureAvailable()
    {
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        BeginCapture(panel);
        Directory.CreateDirectory(AtomicJsonPaths.GetTemporaryPath(Path.Combine(fixtureRoot, "settings.json")));
        SetHeldModifiers(7);
        Press(panel, KeyCode.S);
        Assert.True(Field<bool>(panel, "capturingHotkey"));
        Assert.Equal("F8", Field<PanelHotkey>(panel, "hotkey").ToString());
        Assert.Equal("F8", new AtomicJsonStore<UserSettings>().Load(Path.Combine(fixtureRoot, "settings.json")).Value!.PanelHotkey);
        Assert.NotEmpty(Field<string>(panel, "hotkeyWarning"));
        Input.Held.Clear();
        Press(panel, KeyCode.Escape);
        Press(panel, KeyCode.F8);
        Assert.False(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
    }

    [Fact]
    public void ExistingCanonicalBindingLoadsWithoutRewritingSettings()
    {
        SaveBinding("Ctrl+Shift+K");
        Directory.CreateDirectory(AtomicJsonPaths.GetTemporaryPath(Path.Combine(fixtureRoot, "settings.json")));
        using var panel = new NativeStatisticsPanel(coordinator);
        Input.Held.Add(KeyCode.RightControl); Input.Held.Add(KeyCode.RightShift);
        Press(panel, KeyCode.K);
        Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
        Assert.Equal("Ctrl+Shift+K", Field<PanelHotkey>(panel, "hotkey").ToString());
    }

    private void SaveBinding(string value) => new AtomicJsonStore<UserSettings>().Save(Path.Combine(fixtureRoot, "settings.json"), new UserSettings { PanelHotkey = value });
    private static void BeginCapture(NativeStatisticsPanel panel)
    {
        typeof(NativeStatisticsPanel).GetMethod("BeginHotkeyCapture", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(panel, null);
        Tick(panel);
    }
    private static void SetHeldModifiers(int mask)
    {
        Input.Held.Clear();
        if ((mask & 1) != 0) Input.Held.Add(KeyCode.LeftControl);
        if ((mask & 2) != 0) Input.Held.Add(KeyCode.LeftAlt);
        if ((mask & 4) != 0) Input.Held.Add(KeyCode.LeftShift);
    }
}
