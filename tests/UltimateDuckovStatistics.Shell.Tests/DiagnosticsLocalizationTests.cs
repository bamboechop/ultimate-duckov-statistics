using SodaCraft.Localizations;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed partial class ShellAccessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiagnosticsLanguageChangeRebuildsSnapshotWithoutProfileOrRuntimeChanges(bool diagnosticsVisible)
    {
        LocalizationManager.SetLanguage(SystemLanguage.English);
        using var panel = new NativeStatisticsPanel(coordinator);
        try
        {
            Press(panel, KeyCode.F8);
            if (diagnosticsVisible) Find("DiagnosticsTab").GetComponent<Button>().onClick.Invoke();
            panel.Tick();
            var english = Field<DiagnosticsPresentation>(panel, "diagnostics");
            var bannerKey = UiText.EnglishFallbacks.Single(entry => entry.Value == english.BannerTitle).Key;
            var revision = coordinator.Current.Revision;
            var receipt = coordinator.LastSaveReceipt;
            var entries = coordinator.DiagnosticEntries.ToArray();
            var profile = System.Text.Json.JsonSerializer.Serialize(coordinator.Current);
            panel.Tick();
            Assert.Same(english, Field<DiagnosticsPresentation>(panel, "diagnostics"));

            LocalizationManager.SetLanguage(SystemLanguage.German);
            // The entity-name callback runs before native translation overrides.
            // Tick must rebuild after the entire language event has completed.
            panel.Tick();
            var german = Field<DiagnosticsPresentation>(panel, "diagnostics");
            Assert.NotSame(english, german);
            Assert.Equal(UiText.GermanFallbacks[bannerKey], german.BannerTitle);
            Assert.NotEqual(english.BannerDetail, german.BannerDetail);
            Assert.Equal(english.Health, german.Health);
            Assert.Equal(UiText.Get("ui.menu_access"), german.Systems.Single(system => system.Id == "menu").Name);
            if (!diagnosticsVisible) Find("DiagnosticsTab").GetComponent<Button>().onClick.Invoke();
            panel.Tick();
            Assert.Same(german, Field<DiagnosticsPresentation>(panel, "diagnostics"));

            LocalizationManager.SetLanguage(SystemLanguage.English);
            panel.Tick();
            var restored = Field<DiagnosticsPresentation>(panel, "diagnostics");
            Assert.NotSame(german, restored);
            Assert.Equal(english.BannerTitle, restored.BannerTitle);
            Assert.Equal(english.BannerDetail, restored.BannerDetail);
            panel.Tick();
            Assert.Same(restored, Field<DiagnosticsPresentation>(panel, "diagnostics"));
            Assert.Equal(revision, coordinator.Current.Revision);
            Assert.Same(receipt, coordinator.LastSaveReceipt);
            Assert.Equal(entries, coordinator.DiagnosticEntries.ToArray());
            Assert.Equal(profile, System.Text.Json.JsonSerializer.Serialize(coordinator.Current));
        }
        finally { LocalizationManager.SetLanguage(SystemLanguage.English); }
    }
}
