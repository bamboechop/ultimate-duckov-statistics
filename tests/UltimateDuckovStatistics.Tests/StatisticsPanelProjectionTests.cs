using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class StatisticsPanelProjectionTests
{
    private static readonly string?[] ProceduralModifierHierarchy =
    {
        "UniformModifier",
        NativeMenuPresentationPolicy.ProceduralImageModifierTypeName,
        "UnityEngine.MonoBehaviour"
    };

    private static readonly string?[] ActionBehaviourHierarchy =
    {
        "SomeActionBehaviour",
        "UnityEngine.MonoBehaviour"
    };

    [Fact]
    public void UiTextUsesNativeLocalizationAndFallsBackToEnglishSafely()
    {
        Assert.Equal("Übersicht", UiText.Resolve("ui.overview", key => key == "ui.overview" ? "Übersicht" : null));
        Assert.Equal("Overview", UiText.Resolve("ui.overview", _ => null));
        Assert.Equal("Overview", UiText.Resolve("ui.overview", key => key));
        Assert.Equal("Overview", UiText.Resolve("ui.overview", _ => throw new InvalidOperationException("native failure")));
    }

    [Theory]
    [InlineData((int)PanelAccessSurface.MainMenu)]
    [InlineData((int)PanelAccessSurface.BasePauseMenu)]
    [InlineData((int)PanelAccessSurface.Hotkey)]
    public void AllAccessSurfacesRejectRaidsWithTheSameReason(int surfaceValue)
    {
        var surface = (PanelAccessSurface)surfaceValue;
        var allowed = StatisticsPanelAccessPolicy.Resolve(surface, isRaid: false);
        var rejected = StatisticsPanelAccessPolicy.Resolve(surface, isRaid: true);

        Assert.True(allowed.CanOpen);
        Assert.Null(allowed.RejectionTextKey);
        Assert.False(rejected.CanOpen);
        Assert.Equal("ui.raid_unavailable", rejected.RejectionTextKey);
    }

    [Fact]
    public void NativeMenuAnchorPolicyIsBoundedToKnownSafeAnchors()
    {
        Assert.True(NativeMenuAnchorPolicy.Score("SettingsButton") > NativeMenuAnchorPolicy.Score("ModsButton"));
        Assert.Equal(0, NativeMenuAnchorPolicy.Score("ContinueButton"));
        Assert.Equal(0, NativeMenuAnchorPolicy.Score(null));
    }

    [Theory]
    [InlineData((int)NativeTypographyRole.Title, true, (int)NativeTypographySource.LiveMenuButton)]
    [InlineData((int)NativeTypographyRole.Navigation, true, (int)NativeTypographySource.LiveMenuButton)]
    [InlineData((int)NativeTypographyRole.Body, true, (int)NativeTypographySource.PublicTextTemplate)]
    [InlineData((int)NativeTypographyRole.Secondary, true, (int)NativeTypographySource.PublicTextTemplate)]
    [InlineData((int)NativeTypographyRole.Title, false, (int)NativeTypographySource.PublicTextTemplate)]
    [InlineData((int)NativeTypographyRole.Navigation, false, (int)NativeTypographySource.PublicTextTemplate)]
    public void TypographyRolesPreferLiveMenuTreatmentAndFailBackToThePublicTemplate(
        int roleValue,
        bool hasLiveMenuButton,
        int expectedValue)
    {
        var role = (NativeTypographyRole)roleValue;
        var expected = (NativeTypographySource)expectedValue;
        Assert.Equal(expected, NativeTypographyRolePolicy.Resolve(role, hasLiveMenuButton));
    }

    [Fact]
    public void VerifiedNativeHeadingIsDistinctFromNavigationTypography()
    {
        Assert.Equal(
            NativeTypographySource.NativeHeading,
            NativeTypographyRolePolicy.Resolve(NativeTypographyRole.Title, hasLiveMenuButton: true, hasNativeHeading: true));
        Assert.Equal(
            NativeTypographySource.LiveMenuButton,
            NativeTypographyRolePolicy.Resolve(NativeTypographyRole.Navigation, hasLiveMenuButton: true, hasNativeHeading: true));
    }

    [Fact]
    public void NativeShellDiscoveryPrefersInstalledOptionsPanelTreatments()
    {
        const string root = "Canvas/MainMenuContainer/Menu/OptionsPanel";
        Assert.True(NativeShellTemplatePolicy.ScoreHeading($"{root}/Text (TMP)", 116.75f) >
                    NativeShellTemplatePolicy.ScoreHeading("Canvas/MainTitle/Text (TMP)", 256f));
        Assert.True(NativeShellTemplatePolicy.ScoreBack($"{root}/Return", hasIcon: true) >
                    NativeShellTemplatePolicy.ScoreBack("Canvas/Credits/Return", hasIcon: true));
        Assert.True(NativeShellTemplatePolicy.ScoreTab($"{root}/Tabs/Common") > 0);
        Assert.True(NativeShellTemplatePolicy.ScoreSurface($"{root}/ScrollView/Background") > 0);
        Assert.True(NativeShellTemplatePolicy.ScoreRail($"{root}/Tabs/Image") > 0);
        Assert.Equal(0, NativeShellTemplatePolicy.ScoreBack($"{root}/Return", hasIcon: false));
    }

    [Fact]
    public void ClonedMenuButtonsRetainTheirProceduralImageModifierHierarchy()
    {
        Assert.True(NativeMenuPresentationPolicy.PreservesProceduralImageState(ProceduralModifierHierarchy));
        Assert.False(NativeMenuPresentationPolicy.PreservesProceduralImageState(ActionBehaviourHierarchy));
    }

    [Fact]
    public void NavigationUsesFinalM17Order()
    {
        Assert.Equal(
            new[]
            {
                StatisticsPanelTab.Overview,
                StatisticsPanelTab.Runs,
                StatisticsPanelTab.Records,
                StatisticsPanelTab.Combat,
                StatisticsPanelTab.Equipment,
                StatisticsPanelTab.Economy,
                StatisticsPanelTab.Crafting,
                StatisticsPanelTab.ItemUse,
                StatisticsPanelTab.Diagnostics
            },
            PanelInteractionState.NavigationOrder);
    }

    [Fact]
    public void NarrowLayoutStacksLeftFirstAndScrollsTabs()
    {
        var narrow = StatisticsPanelLayoutPolicy.Create(1024, 768);
        var desktop = StatisticsPanelLayoutPolicy.Create(2560, 1440);

        Assert.Equal(PanelColumnLayout.Stacked, narrow.Columns);
        Assert.True(narrow.TabStripRequiresScrolling);
        Assert.InRange(narrow.PageSize, 12, 48);
        Assert.Equal(PanelColumnLayout.SideBySide, desktop.Columns);
        Assert.False(desktop.TabStripRequiresScrolling);
    }

    [Fact]
    public void TabScrollMovesOnlyEnoughToKeepTheSelectionVisible()
    {
        Assert.Equal(0f, TabStripScrollPolicy.EnsureVisible(900f, 860f, 700f, 150f, 0f));
        Assert.Equal(150f, TabStripScrollPolicy.EnsureVisible(500f, 1400f, 500f, 150f, 0f));
        Assert.Equal(100f, TabStripScrollPolicy.EnsureVisible(500f, 1400f, 100f, 150f, 420f));
        Assert.Equal(900f, TabStripScrollPolicy.EnsureVisible(500f, 1400f, 1300f, 150f, 0f));
    }

    [Fact]
    public void RuntimeTabScrollToleratesTransientUnityLayoutGeometry()
    {
        Assert.True(RuntimeTabStripScrollPolicy.TryEnsureVisible(
            500f,
            1400f,
            -50f,
            150f,
            float.NaN,
            out var targetOffset));
        Assert.Equal(0f, targetOffset);

        Assert.False(RuntimeTabStripScrollPolicy.TryEnsureVisible(
            0f,
            1400f,
            0f,
            150f,
            0f,
            out _));
        Assert.False(RuntimeTabStripScrollPolicy.TryEnsureVisible(
            500f,
            1400f,
            0f,
            0f,
            0f,
            out _));
    }

    [Fact]
    public void RetainedShellUsesNearFullViewportAndOnlyOverflowsAtNarrowWidth()
    {
        var desktop = RetainedShellLayoutPolicy.Create(2560f, 1440f);
        var narrow = RetainedShellLayoutPolicy.Create(1024f, 768f);

        Assert.False(desktop.TabStripRequiresScrolling);
        Assert.True(narrow.TabStripRequiresScrolling);
        Assert.InRange(desktop.MarginPixels, 80f, 90f);
        Assert.InRange(narrow.MarginPixels, 18f, 30f);
        Assert.True(desktop.TabViewportWidthPixels > desktop.TabContentWidthPixels);
        Assert.True(narrow.TabViewportWidthPixels < narrow.TabContentWidthPixels);
    }

    [Fact]
    public void RetainedShellPreservesResponsiveTitleAndNavigationHierarchy()
    {
        var desktop = RetainedShellLayoutPolicy.Create(2560f, 1440f);
        var narrow = RetainedShellLayoutPolicy.Create(1024f, 768f);

        Assert.True(desktop.TitleFontPixels >= desktop.NavigationFontPixels * 2f);
        Assert.True(narrow.TitleFontPixels >= narrow.NavigationFontPixels * 2f);
        Assert.True(desktop.HeaderHeightPixels > desktop.TabRowHeightPixels);
        Assert.True(narrow.HeaderHeightPixels > narrow.TabRowHeightPixels);
        Assert.True(desktop.TabHeightPixels > 60f);
        Assert.True(narrow.TabHeightPixels >= 60f);
    }

    [Fact]
    public void NativeFontMetricsCanWidenLongLocalizedNavigationWithoutShrinkingLabels()
    {
        Assert.Equal(202f, RetainedTabWidthPolicy.Resolve(202f, 120f, 38f));
        Assert.Equal(358f, RetainedTabWidthPolicy.Resolve(202f, 320f, 38f));
        Assert.Equal(938f, RetainedTabWidthPolicy.Resolve(202f, 900f, 38f));
    }

    [Fact]
    public void InstalledNativeEnglishMetricsFitAllNineDesktopTabsWithoutEllipsis()
    {
        // Audited from installed Duckov 2.3.30 ResourceHanRoundedCN-Medium SDF
        // glyph advances at the final retained navigation size of 27 px.
        var installedEnglishWidths = new[]
        {
            121.439063f, 64.260937f, 104.521875f, 102.656250f, 142.935937f,
            120.285938f, 104.217187f, 113.803125f, 149.498437f
        };
        var layout = RetainedShellLayoutPolicy.Create(2560f, 1440f);
        var geometry = RetainedTabGeometryPolicy.Create(
            layout.TabViewportWidthPixels,
            layout.TabWidthPixels,
            layout.TabSpacingPixels,
            layout.TabPaddingPixels,
            38f,
            installedEnglishWidths);

        Assert.Equal(9, geometry.Widths.Count);
        Assert.False(geometry.RequiresScrolling);
        Assert.True(geometry.ContentWidth < layout.TabViewportWidthPixels);
        for (var index = 0; index < installedEnglishWidths.Length; index++)
            Assert.True(geometry.Widths[index] >= installedEnglishWidths[index] + 38f);
    }

    [Fact]
    public void LongLocalizedNavigationRemainsOneScrollableRow()
    {
        var layout = RetainedShellLayoutPolicy.Create(1024f, 768f);
        var geometry = RetainedTabGeometryPolicy.Create(
            layout.TabViewportWidthPixels,
            layout.TabWidthPixels,
            layout.TabSpacingPixels,
            layout.TabPaddingPixels,
            38f,
            Enumerable.Repeat(420f, 9).ToArray());

        Assert.True(geometry.RequiresScrolling);
        Assert.All(geometry.Widths, width => Assert.Equal(458f, width));
        Assert.Equal(9, geometry.Widths.Count);
    }

    [Fact]
    public void TabSelectionMovesInExactOrderAndWraps()
    {
        var state = new PanelInteractionState();

        Assert.Equal(StatisticsPanelTab.Overview, state.SelectedTab);
        state.MoveTab(1);
        Assert.Equal(StatisticsPanelTab.Runs, state.SelectedTab);
        state.MoveTab(-2);
        Assert.Equal(StatisticsPanelTab.Diagnostics, state.SelectedTab);
        state.SelectTab(StatisticsPanelTab.Crafting);
        Assert.Equal(StatisticsPanelTab.Crafting, state.SelectedTab);
    }

    [Fact]
    public void ExactlyOneTabOwnsTheSelectedSurfaceAcrossEveryTransition()
    {
        foreach (var selected in PanelInteractionState.NavigationOrder)
        {
            var selectedCount = PanelInteractionState.NavigationOrder.Count(
                candidate => RetainedTabSelectionPolicy.IsSelected(candidate, selected));

            Assert.Equal(1, selectedCount);
        }
    }

    [Fact]
    public void Gate1cLayersMakeTheShellDistinctFromTheDimmedScene()
    {
        var outsideFrame = RetainedShellLayerPolicy.BackgroundTransmission(
            RetainedShellLayerPolicy.BlockerOpacity);
        var insideFrame = RetainedShellLayerPolicy.BackgroundTransmission(
            RetainedShellLayerPolicy.BlockerOpacity,
            RetainedShellLayerPolicy.FrameOpacity);
        var insideContent = RetainedShellLayerPolicy.BackgroundTransmission(
            RetainedShellLayerPolicy.BlockerOpacity,
            RetainedShellLayerPolicy.FrameOpacity,
            RetainedShellLayerPolicy.ContentOpacity);

        Assert.InRange(outsideFrame, 0.319f, 0.321f);
        Assert.InRange(insideFrame, 0.057f, 0.058f);
        Assert.InRange(insideContent, 0.016f, 0.017f);
        Assert.True(insideFrame < outsideFrame / 5f);
        Assert.True(insideContent < insideFrame / 3f);
    }

    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public void FocusRestorationRequiresACapturedLiveActiveTarget(
        bool captured,
        bool exists,
        bool active,
        bool expected)
    {
        Assert.Equal(expected, PanelFocusRestorePolicy.ShouldRestore(captured, exists, active));
    }

    [Fact]
    public void OverflowCuesDescribeBothDirectionsWithoutInventingOverflow()
    {
        var fits = OverflowCuePolicy.Resolve(900f, 860f, 0f);
        var start = OverflowCuePolicy.Resolve(500f, 1400f, 0f);
        var middle = OverflowCuePolicy.Resolve(500f, 1400f, 450f);
        var end = OverflowCuePolicy.Resolve(500f, 1400f, 900f);

        Assert.False(fits.ShowLeading);
        Assert.False(fits.ShowTrailing);
        Assert.False(start.ShowLeading);
        Assert.True(start.ShowTrailing);
        Assert.True(middle.ShowLeading);
        Assert.True(middle.ShowTrailing);
        Assert.True(end.ShowLeading);
        Assert.False(end.ShowTrailing);
    }

    [Fact]
    public void RetainedShellLifecycleAllowsOneOpenRootAndCleansUpDeterministically()
    {
        var lifecycle = new RetainedShellLifecycleState();

        Assert.True(lifecycle.TryOpen());
        Assert.False(lifecycle.TryOpen());
        Assert.True(lifecycle.IsOpen);
        Assert.True(lifecycle.Close());
        Assert.False(lifecycle.Close());
        Assert.True(lifecycle.TryOpen());
        lifecycle.Dispose();
        Assert.False(lifecycle.IsOpen);
        Assert.True(lifecycle.IsDisposed);
        Assert.False(lifecycle.TryOpen());
    }

    [Fact]
    public void BoundedPagesNeverRenderUnboundedHistory()
    {
        var source = Enumerable.Range(0, 1000).ToArray();
        var page = BoundedPageFactory.Create(source, requestedPage: 999, pageSize: 40);

        Assert.Equal(25, page.PageCount);
        Assert.Equal(24, page.PageIndex);
        Assert.Equal(40, page.Items.Count);
        Assert.Equal(960, page.Items[0]);
    }

    [Fact]
    public void OperationsAreSingleFlightAcrossExportAndReset()
    {
        var gate = new PanelOperationGate();

        Assert.True(gate.TryBegin(PanelOperation.Export));
        Assert.False(gate.TryBegin(PanelOperation.Export));
        Assert.False(gate.TryBegin(PanelOperation.Reset));
        gate.Complete(PanelOperation.Export);
        Assert.True(gate.TryBegin(PanelOperation.Reset));
        gate.Complete(PanelOperation.Reset);
        Assert.Equal(PanelOperation.None, gate.Current);
    }

    [Fact]
    public void ResetModalDefaultsToCancelAndEscapeCancelsOnlyTheModal()
    {
        var state = new PanelInteractionState();
        state.SelectTab(StatisticsPanelTab.Diagnostics);
        state.ShowResetConfirmation();

        Assert.True(state.ResetConfirmationVisible);
        Assert.True(state.ResetCancelHasInitialFocus);
        Assert.True(state.CancelModal());
        Assert.False(state.ResetConfirmationVisible);
        Assert.Equal(StatisticsPanelTab.Diagnostics, state.SelectedTab);
    }

    [Fact]
    public void ProjectionRejectsAnAmbiguousGeneration()
    {
        var profile = Profile("generation-a");
        profile.Statistics.SaveGenerationId = "generation-b";

        Assert.False(StatisticsPanelProjectionFactory.HasProvableGeneration(profile, "generation-a"));
        Assert.Throws<InvalidOperationException>(() => StatisticsPanelProjectionFactory.Create(
            profile,
            new EconomyMetricCapabilities(),
            new CraftingMetricCapabilities()));
    }

    [Fact]
    public void WeaponProjectionKeepsAmmunitionWithinItsWeapon()
    {
        var profile = Profile("generation-a");
        var aggregate = profile.Statistics.RunTotals.WeaponStatistics;
        aggregate.Totals.FiringActions = 14;
        aggregate.Weapons["weapon-a"] = new WeaponAggregate
        {
            WeaponId = "weapon-a",
            DisplayName = "A",
            Totals = new WeaponMetricTotals { FiringActions = 10 }
        };
        aggregate.Weapons["weapon-b"] = new WeaponAggregate
        {
            WeaponId = "weapon-b",
            DisplayName = "B",
            Totals = new WeaponMetricTotals { FiringActions = 4 }
        };
        aggregate.AmmunitionTypes["ammo-x"] = new AmmunitionAggregate
        {
            AmmunitionId = "ammo-x",
            DisplayName = "X",
            Totals = new WeaponMetricTotals { FiringActions = 10 }
        };
        aggregate.AmmunitionTypes["ammo-y"] = new AmmunitionAggregate
        {
            AmmunitionId = "ammo-y",
            DisplayName = "Y",
            Totals = new WeaponMetricTotals { FiringActions = 3 }
        };
        aggregate.WeaponAmmunitionPairs[WeaponStatisticsReducer.PairKey("weapon-a", "ammo-x")] =
            Pair("weapon-a", "A", "ammo-x", "X", 6);
        aggregate.WeaponAmmunitionPairs[WeaponStatisticsReducer.PairKey("weapon-a", "ammo-y")] =
            Pair("weapon-a", "A", "ammo-y", "Y", 3);
        aggregate.WeaponAmmunitionPairs[WeaponStatisticsReducer.PairKey("weapon-b", "ammo-x")] =
            Pair("weapon-b", "B", "ammo-x", "X", 4);
        aggregate.UncorrelatedWeaponFiringActions["weapon-a"] = 1;

        var projection = Create(profile);
        var weaponA = Assert.Single(projection.WeaponAmmunitionGroups, value => value.WeaponId == "weapon-a");
        var weaponB = Assert.Single(projection.WeaponAmmunitionGroups, value => value.WeaponId == "weapon-b");

        Assert.Equal(9, weaponA.CorrelatedFiringActions);
        Assert.Equal(1, weaponA.UncorrelatedFiringActions);
        Assert.Equal(2, weaponA.Ammunition.Count);
        Assert.Equal(66.66666666666667, weaponA.Ammunition[0].PercentageWithinObservedWeaponPairs, 10);
        Assert.Single(weaponB.Ammunition);
        Assert.Equal("ammo-x", weaponB.Ammunition[0].Pair.AmmunitionId);
    }

    [Fact]
    public void EconomyProjectionNeverDerivesHoldingsFromFlows()
    {
        var profile = Profile("generation-a");
        profile.Statistics.Holdings = Holdings("generation-a", money: 100, cash: 50);
        profile.Statistics.Economy.Currencies[CurrencyKind.Money.ToString()] = new CurrencyEconomyAggregate
        {
            Currency = CurrencyKind.Money,
            Totals = new CurrencyFlowTotals { GrossInflow = 900, GrossOutflow = 300 }
        };

        var projection = Create(profile);

        Assert.Equal(100, projection.Holdings.Money.Value);
        Assert.Equal(50, projection.Holdings.Cash.Value);
        Assert.Equal(150, projection.Holdings.LiquidWealth.Value);
        Assert.Equal(600, projection.Economy.Currencies["Money"].Totals.NetFlow);
    }

    [Fact]
    public void CraftingProjectionKeepsActionsProducedUnitsAndResourcesDistinct()
    {
        var profile = Profile("generation-a");
        profile.Statistics.Crafting.Outputs["trap"] = new CraftedOutputAggregate
        {
            OutputItemId = "trap",
            DisplayName = "Animal Trap",
            CompletionActions = 9,
            ProducedQuantity = 9,
            Recipes = new Dictionary<string, CraftingRecipeAggregate>(StringComparer.Ordinal)
            {
                ["recipe-trap"] = new()
                {
                    RecipeId = "recipe-trap",
                    CompletionActions = 9,
                    ProducedQuantity = 9,
                    Resources = new Dictionary<string, CraftingResourceAssociationAggregate>(StringComparer.Ordinal)
                    {
                        ["metal"] = new()
                        {
                            ResourceItemId = "metal",
                            DisplayName = "Metal Plate",
                            ConsumptionActions = 9,
                            ConsumedQuantity = 18
                        }
                    }
                }
            }
        };
        profile.Statistics.Crafting.Resources["metal"] = new CraftingResourceAggregate
        {
            ResourceItemId = "metal",
            DisplayName = "Metal Plate",
            ConsumedQuantity = 18
        };

        var projection = Create(profile);
        var output = Assert.Single(projection.CraftingOutputs);
        var resource = Assert.Single(projection.CraftingResources);

        Assert.Equal(9, output.Output.CompletionActions);
        Assert.Equal(9, output.Output.ProducedQuantity);
        Assert.Equal(18, Assert.Single(output.Resources).ConsumedQuantity);
        Assert.Equal(9, Assert.Single(resource.Outputs).ProducedQuantity);
        Assert.Equal(18, resource.Outputs[0].ConsumedQuantity);
    }

    [Fact]
    public void ItemUseProjectionRetainsUnknownIdentityAndIndependentFacts()
    {
        var profile = Profile("generation-a");
        profile.Statistics.Items["mod:item"] = new ItemAggregate
        {
            ItemId = "mod:item",
            DisplayName = string.Empty,
            Group = CanonicalItemGroup.OtherUnknown,
            EffectTags = new List<ItemEffectTag> { ItemEffectTag.Buff },
            Totals = new AggregateTotals
            {
                ActivationCount = 2,
                ActualHealthRestored = 3,
                AmountsByUnit = new Dictionary<string, double>(StringComparer.Ordinal) { ["Item"] = 5 }
            }
        };
        profile.Statistics.Overall.ActivationCount = 2;
        profile.Statistics.Overall.ActualHealthRestored = 3;
        profile.Statistics.Overall.AmountsByUnit["Item"] = 5;
        profile.Statistics.Groups[CanonicalItemGroup.OtherUnknown.ToString()] =
            profile.Statistics.Items["mod:item"].Totals;

        var row = Assert.Single(Create(profile).ItemUse.Items);

        Assert.Contains("mod:item", row.DisplayName, StringComparison.Ordinal);
        Assert.Equal(2, row.Totals.ActivationCount);
        Assert.Equal(5, row.Totals.AmountsByUnit["Item"]);
        Assert.Equal(3, row.Totals.ActualHealthRestored);
        Assert.Equal(CanonicalItemGroup.OtherUnknown, row.Group);
        Assert.Equal(new[] { ItemEffectTag.Buff }, row.EffectTags);
    }

    private static StatisticsPanelProjection Create(ProfileDocument profile) =>
        StatisticsPanelProjectionFactory.Create(
            profile,
            new EconomyMetricCapabilities(),
            new CraftingMetricCapabilities());

    private static ProfileDocument Profile(string generationId) => new()
    {
        GenerationId = generationId,
        Statistics = new ProfileStatistics
        {
            SaveGenerationId = generationId
        }
    };

    private static WeaponAmmunitionPairAggregate Pair(
        string weaponId,
        string weaponName,
        string ammunitionId,
        string ammunitionName,
        long actions) => new()
        {
            WeaponId = weaponId,
            WeaponDisplayName = weaponName,
            AmmunitionId = ammunitionId,
            AmmunitionDisplayName = ammunitionName,
            FiringActions = actions
        };

    private static EconomyHoldingsSnapshot Holdings(string generationId, long money, long cash)
    {
        var supported = new MetricAvailability { State = AdapterCapabilityState.Supported };
        return new EconomyHoldingsSnapshot
        {
            SaveGenerationId = generationId,
            Money = Observation(generationId, money),
            Cash = Observation(generationId, cash),
            Capabilities = new EconomyHoldingsMetricCapabilities
            {
                Money = supported,
                Cash = new MetricAvailability { State = AdapterCapabilityState.Supported },
                LiquidWealth = new MetricAvailability { State = AdapterCapabilityState.Supported }
            }
        };
    }

    private static EconomyHoldingObservation Observation(string generationId, long value) => new()
    {
        State = EconomyHoldingObservationState.Current,
        Value = value,
        ObservedUtc = DateTime.UnixEpoch,
        SaveGenerationId = generationId
    };
}
