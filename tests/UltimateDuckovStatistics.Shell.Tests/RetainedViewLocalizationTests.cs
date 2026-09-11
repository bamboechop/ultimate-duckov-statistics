global using UniformModifier = UnityEngine.UI.ProceduralImage.UniformModifier;
using SodaCraft.Localizations;
using TMPro;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed partial class ShellAccessTests
{
    [Theory]
    [InlineData("Records", true, 1280)]
    [InlineData("Records", false, 720)]
    [InlineData("ItemUse", true, 720)]
    [InlineData("ItemUse", false, 1280)]
    [InlineData("Combat", true, 1280)]
    [InlineData("Combat", false, 720)]
    public void RetainedViewCaptionsFollowLanguageBeforeMeasurement(string tab, bool visibleDuringSwitch, int width)
    {
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, width == 720 ? 480 : 720);
        LocalizationManager.SetLanguage(SystemLanguage.English);
        var run = new RunSummary
        {
            RunId = "caption-run",
            SaveGenerationId = coordinator.CurrentGenerationId,
            StartedUtc = new DateTime(2026, 9, 1, 1, 2, 3, DateTimeKind.Utc),
            ActiveDurationSeconds = 60,
            Outcome = RunOutcome.Extracted,
            RecordEligible = true,
            LifecycleCapability = AdapterCapabilityState.Supported
        };
        coordinator.Current.Statistics.Runs.Add(run);
        coordinator.Current.Statistics.RunRecords.Extraction.Shortest = new DurationRecordReference
        { RunId = run.RunId, StartedUtc = run.StartedUtc, ActiveDurationSeconds = run.ActiveDurationSeconds };
        var profile = System.Text.Json.JsonSerializer.Serialize(coordinator.Current);
        var revision = coordinator.Current.Revision;
        using var panel = new NativeStatisticsPanel(coordinator);
        try
        {
            Press(panel, KeyCode.F8);
            var root = Find(tab + "ContentView");
            var keys = tab switch
            {
                "Records" => new[] { "ui.records_overall", "ui.records_per_map", "ui.records_no_maps", "ui.profile_unavailable", RetainedOverviewLatestRunViewRunPolicy.TextKey },
                "ItemUse" => new[] { "ui.item_use_empty", "ui.profile_unavailable" },
                _ => new[] { "ui.combat_firing_footer", "ui.profile_unavailable" }
            };
            var captions = keys.SelectMany(key => root.GetComponentsInChildren<TextMeshProUGUI>(true)
                .Where(label => label.text == UiText.EnglishFallbacks[key]).Select(label => (label, key))).ToArray();
            Assert.All(keys, key => Assert.Contains(captions, caption => caption.key == key));
            var retainedButtons = root.GetComponentsInChildren<Button>(true);
            OpenTab();
            if (tab == "Combat")
            {
                var weapons = root.GetComponentsInChildren<TextMeshProUGUI>().Single(label => label.text == UiText.Get("ui.combat_weapons_ammunition"));
                weapons.transform.parent!.GetComponent<Button>().onClick.Invoke(); panel.Tick();
                Assert.True(Find("FiringActionContract").activeInHierarchy);
            }
            if (!visibleDuringSwitch) { Find("OverviewTab").GetComponent<Button>().onClick.Invoke(); panel.Tick(); }
            LocalizationManager.SetLanguage(SystemLanguage.German); panel.Tick();
            OpenTab();
            Check(UiText.GermanFallbacks);
            Assert.Same(root, Find(tab + "ContentView"));
            if (tab == "Records") Assert.Equal(retainedButtons, root.GetComponentsInChildren<Button>(true));
            LocalizationManager.SetLanguage(SystemLanguage.English); panel.Tick();
            Check(UiText.EnglishFallbacks);
            Assert.Equal(revision, coordinator.Current.Revision);
            Assert.Equal(profile, System.Text.Json.JsonSerializer.Serialize(coordinator.Current));
            var measurements = TextMeshProUGUI.Measurements;
            panel.Tick();
            Assert.Equal(measurements, TextMeshProUGUI.Measurements);

            void OpenTab() { Find(tab + "Tab").GetComponent<Button>().onClick.Invoke(); panel.Tick(); }
            void Check(IReadOnlyDictionary<string, string> translations)
            {
                foreach (var (label, key) in captions)
                {
                    Assert.Equal(translations[key], label.text);
                    if (!label.gameObject.activeInHierarchy) continue;
                    Assert.True(label.rectTransform.rect.height + .01f >= label.GetPreferredValues(label.rectTransform.rect.width, float.PositiveInfinity).y,
                        key + " must be measured using its refreshed caption");
                    if (key == RetainedOverviewLatestRunViewRunPolicy.TextKey)
                    {
                        Assert.True(label.transform.parent!.gameObject.activeInHierarchy);
                        Assert.Equal(1, label.transform.parent.GetComponent<Button>().onClick.ListenerCount);
                    }
                }
            }
        }
        finally { LocalizationManager.SetLanguage(SystemLanguage.English); }
    }
}
