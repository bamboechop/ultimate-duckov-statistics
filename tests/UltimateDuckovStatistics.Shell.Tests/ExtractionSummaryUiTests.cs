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

    [Theory]
    [InlineData(1080, false)]
    [InlineData(1440, false)]
    [InlineData(2160, false)]
    [InlineData(1440, true)]
    public void NativeResultsKeepCenteredMarginsAndOnlyGrowByTheCompactSummary(float viewportHeight, bool death)
    {
        // Installed resources.assets: Content GO14885, Rect68773, VerticalLayoutGroup114534.
        // It stretches the full viewport height, has no fitter, and centers its non-expanding children.
        var nativeLayout = content.AddComponent<VerticalLayoutGroup>();
        nativeLayout.padding = new RectOffset(16, 16, 0, 0);
        nativeLayout.childAlignment = TextAnchor.MiddleCenter;
        nativeLayout.childForceExpandWidth = false; nativeLayout.childForceExpandHeight = false;
        nativeLayout.childControlWidth = true; nativeLayout.childControlHeight = true;
        nativeLayout.childScaleWidth = true; nativeLayout.childScaleHeight = true;
        nativeTitle.AddComponent<LayoutElement>().preferredHeight = 140; // Representative measured native title.
        nativeExp.AddComponent<LayoutElement>().preferredHeight = 248.85f; // Serialized native element86988.
        nativeContinue.AddComponent<LayoutElement>().preferredHeight = 104.88f; // Serialized native element91948.
        if (death)
        {
            var reason = new GameObject("ReasonOfDeath", typeof(LayoutElement));
            reason.transform.SetParent(content.transform); reason.transform.SetSiblingIndex(1);
            reason.GetComponent<LayoutElement>().preferredHeight = 36;
        }
        var before = ExtractionVerticalLayout.Rebuild(content, viewportHeight);
        var nativePreferences = NativePreferences(nativeLayout);
        using (var ui = Ready())
        {
            if (death) UiText.ConfigureNativeResolver(key => UiText.GermanFallbacks.TryGetValue(key, out var text) ? text : null);
            view.Open();
            var after = ExtractionVerticalLayout.Rebuild(content, viewportHeight);
            var block = content.transform.Find("UDS.ResultsSummary")!.gameObject;
            var row = block.transform.Find("Metrics")!.gameObject;
            var footer = block.transform.GetChild(1).gameObject;
            var blockBounds = after[block];
            Assert.InRange(blockBounds.Height, 168, 174);
            Assert.InRange(after[row].Top - blockBounds.Top, 24, 28);
            Assert.InRange(after[footer].Top - after[row].Bottom, 8, 9);
            Assert.InRange(after[footer].Bottom, blockBounds.Bottom - 4, blockBounds.Bottom);
            Assert.Equal(before[nativeTitle].Top - blockBounds.Height / 2, after[nativeTitle].Top, 2);
            Assert.Equal(after[nativeTitle].Top, viewportHeight - after[nativeContinue].Bottom, 2);
            Assert.True(after[nativeTitle].Top > 100);
            Assert.Equal(before[nativeContinue].Height, after[nativeContinue].Height);
            Assert.Equal(before[nativeExp].Height, after[nativeExp].Height);
            Assert.Equal(nativePreferences, NativePreferences(nativeLayout));
            view.Close();
            var closed = ExtractionVerticalLayout.Rebuild(content, viewportHeight);
            Assert.Equal(before[nativeTitle], closed[nativeTitle]);
            Assert.Equal(before[nativeContinue], closed[nativeContinue]);
        }
        UiText.ConfigureNativeResolver(null);
    }

    private static object NativePreferences(VerticalLayoutGroup layout) =>
        (layout.padding.left, layout.padding.right, layout.padding.top, layout.padding.bottom,
            layout.childAlignment, layout.spacing, layout.childForceExpandWidth, layout.childForceExpandHeight,
            layout.childControlWidth, layout.childControlHeight, layout.childScaleWidth, layout.childScaleHeight);

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
