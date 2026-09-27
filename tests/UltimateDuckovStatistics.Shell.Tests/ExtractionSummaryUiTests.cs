using Xunit;
using Duckov.UI;
using Duckov.Utilities;
using TMPro;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed class ExtractionSummaryUiTests : IDisposable
{
    private readonly ClosureView view;
    private readonly GameObject content;
    private readonly GameObject nativeTitle;
    private readonly GameObject nativeExp;
    private readonly GameObject nativeContinue;
    private readonly GameObject template;

    public ExtractionSummaryUiTests()
    {
        view = new GameObject("Closure").AddComponent<ClosureView>();
        content = new GameObject("Content"); content.transform.SetParent(view.transform);
        nativeTitle = new GameObject("Title"); nativeTitle.transform.SetParent(content.transform);
        nativeExp = new GameObject("ExpBarContainer"); nativeExp.transform.SetParent(content.transform);
        nativeContinue = new GameObject("Continue", typeof(Button)); nativeContinue.transform.SetParent(content.transform);
        template = new GameObject("Template", typeof(TextMeshProUGUI));
        GameplayDataSettings.UIStyle.TemplateTextUGUI = template.GetComponent<TextMeshProUGUI>();
        RaidUtilities.CurrentRaid = new RaidUtilities.RaidInfo { ID = 1, valid = true };
        UiText.ConfigureNativeResolver(null);
    }

    [Fact]
    public void AddsOneOwnedBlockWithoutCloningInputsAndCleansUpOnlyThatBlock()
    {
        var subscribers = ManagedUIElement.Subscribers;
        using (var ui = Ready())
        {
            view.Open(); view.Open();
            var block = Assert.Single(content.transform.Children, child => child.gameObject.name == "UDS.ResultsSummary");
            Assert.True(block.GetSiblingIndex() < nativeExp.transform.GetSiblingIndex());
            Assert.Empty(block.GetComponentsInChildren<Button>(true));
            Assert.All(block.GetComponentsInChildren<TextMeshProUGUI>(true), text => Assert.False(text.raycastTarget));
            Assert.Contains(block.GetComponentsInChildren<TextMeshProUGUI>(true), text => text.text == "3");
            view.Close();
            Assert.Equal(3, content.transform.childCount);
            Assert.False(nativeTitle.Destroyed); Assert.False(nativeExp.Destroyed); Assert.False(nativeContinue.Destroyed);
        }
        Assert.Equal(subscribers, ManagedUIElement.Subscribers);
        view.Open();
        Assert.Equal(3, content.transform.childCount);
    }

    [Fact]
    public void ReopeningAfterCloseCannotShowLastRunAndChangedRaidIdentityCannotLeak()
    {
        using var ui = Ready();
        RaidUtilities.CurrentRaid = new RaidUtilities.RaidInfo { ID = 2, valid = true };
        view.Open();
        Assert.DoesNotContain(content.GetComponentsInChildren<TextMeshProUGUI>(true), text => text.text == "3");
        view.Close();
        RaidUtilities.CurrentRaid = new RaidUtilities.RaidInfo { ID = 1, valid = true };
        view.Open();
        Assert.DoesNotContain(content.GetComponentsInChildren<TextMeshProUGUI>(true), text => text.text == "3");
    }

    [Fact]
    public void LateTerminalCompletionRefreshesOpenViewWithoutRebuildingNativeContent()
    {
        using var ui = new NativeExtractionSummary(() => "g", _ => { }, () => 100);
        ui.Begin("g", "r", true); ui.Terminal(RunOutcome.Extracted);
        view.Open();
        var block = content.transform.Children.Single(child => child.gameObject.name == "UDS.ResultsSummary");
        ui.Complete(Run());
        Assert.Same(block, content.transform.Children.Single(child => child.gameObject.name == "UDS.ResultsSummary"));
        Assert.Contains(block.GetComponentsInChildren<TextMeshProUGUI>(true), text => text.text == "3");
    }

    [Fact]
    public void CreationFailureRemovesPartialOwnedUiAndNeverBreaksNativeOpen()
    {
        using var ui = Ready();
        GameObject.FailCreationOf = "Metrics";
        try { view.Open(); }
        finally { GameObject.FailCreationOf = null; }
        Assert.True(view.open);
        Assert.Equal(3, content.transform.childCount);
        view.Open();
        Assert.Equal(4, content.transform.childCount);
    }

    [Fact]
    public void ProfileResetRemovesVisibleResultsAndCallbacksUnsubscribeOnDispose()
    {
        var ui = Ready();
        view.Open(); ui.Reset();
        Assert.Equal(3, content.transform.childCount);
        ui.Dispose(); ui.Dispose();
        view.Open();
        Assert.Equal(3, content.transform.childCount);
    }

    [Fact]
    public void NativeLanguageIsResolvedWhenResultsOpenAndDisposedObserversDoNotReadValues()
    {
        using (var ui = Ready())
        {
            UiText.ConfigureNativeResolver(key => UiText.GermanFallbacks.TryGetValue(key, out var text) ? text : null);
            view.Open();
            Assert.Contains(content.GetComponentsInChildren<TextMeshProUGUI>(true), text => text.text == "Deine Eliminierungen");
            view.Close();
        }
        var reads = 0;
        var disposed = new NativeExtractionSummary(() => "g", _ => { }, () => { reads++; return 100; });
        disposed.Dispose();
        disposed.Begin("g", "r", true);
        disposed.Terminal(RunOutcome.Extracted);
        disposed.Complete(Run());
        Assert.Equal(0, reads);
        UiText.ConfigureNativeResolver(null);
    }

    private static NativeExtractionSummary Ready()
    {
        var ui = new NativeExtractionSummary(() => "g", _ => { }, () => 100);
        ui.Begin("g", "r", true); ui.Terminal(RunOutcome.Extracted); ui.Complete(Run());
        return ui;
    }

    private static RunSummary Run() => new()
    {
        SaveGenerationId = "g",
        RunId = "r",
        NativeRaidId = "1",
        Outcome = RunOutcome.Extracted,
        ActiveDurationSeconds = 62,
        LifecycleCapability = AdapterCapabilityState.Supported,
        CombatStatistics = new CombatStatisticsAggregate
        {
            Totals = new CombatMetricTotals { KillsByYou = 3 },
            Capabilities = new CombatMetricCapabilities { KillsByYou = new MetricAvailability { State = AdapterCapabilityState.Supported } }
        }
    };

    public void Dispose()
    {
        UnityEngine.Object.Destroy(view.gameObject); UnityEngine.Object.Destroy(template);
        GameplayDataSettings.UIStyle.TemplateTextUGUI = null!;
        GameObject.FailCreationOf = null;
    }
}
