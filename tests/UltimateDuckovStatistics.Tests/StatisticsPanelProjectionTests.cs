using System.Security.Cryptography;
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
    public void StepZeroDimmerRemainsTheSoleRaycastBlocker()
    {
        Assert.Equal("UltimateDuckovStatisticsRetainedShell", RetainedDimmerPolicy.RootName);
        Assert.Equal(0f, RetainedDimmerPolicy.Red);
        Assert.Equal(0f, RetainedDimmerPolicy.Green);
        Assert.Equal(0f, RetainedDimmerPolicy.Blue);
        Assert.Equal(0.50f, RetainedDimmerPolicy.VisualAlpha);
        Assert.True(RetainedDimmerPolicy.BlocksRaycasts);
        Assert.True(RetainedDimmerPolicy.IsValidGraphic(0f, 0f, 0f, 0.50f, blockerRaycastTarget: true));
        Assert.False(RetainedDimmerPolicy.IsValidGraphic(0f, 0f, 0f, 0.25f, blockerRaycastTarget: true));
        Assert.False(RetainedDimmerPolicy.IsValidGraphic(0f, 0f, 0f, 0.50f, blockerRaycastTarget: false));
    }

    [Fact]
    public void StepOneHeaderUsesExactPixelGeometryAndIndependentOpacity()
    {
        Assert.Equal("HeaderBackground", RetainedHeaderPolicy.Name);
        Assert.Equal(2560f, RetainedHeaderPolicy.BaselineWidthPixels);
        Assert.Equal(1440f, RetainedHeaderPolicy.BaselineHeightPixels);
        Assert.Equal(85f, RetainedHeaderPolicy.LeftPixels);
        Assert.Equal(113f, RetainedHeaderPolicy.TopPixels);
        Assert.Equal(2392f, RetainedHeaderPolicy.WidthPixels);
        Assert.Equal(217f, RetainedHeaderPolicy.HeightPixels);
        Assert.Equal(2477f, RetainedHeaderPolicy.RightExclusivePixels);
        Assert.Equal(330f, RetainedHeaderPolicy.BottomExclusivePixels);
        Assert.Equal(0f, RetainedHeaderPolicy.Red);
        Assert.Equal(0f, RetainedHeaderPolicy.Green);
        Assert.Equal(0f, RetainedHeaderPolicy.Blue);
        Assert.Equal(0.50f, RetainedHeaderPolicy.VisualAlpha);
        Assert.Equal(20f, RetainedHeaderPolicy.CornerRadiusPixels);
        Assert.False(RetainedHeaderPolicy.BlocksRaycasts);
        Assert.Equal(0.75f, RetainedHeaderPolicy.EffectiveOpacity);
        Assert.True(RetainedHeaderPolicy.IsValidGraphic(0f, 0f, 0f, 0.50f, raycastTarget: false));
    }

    [Fact]
    public void StepOneHeaderPreservesExactReferenceGeometryAtBaselineViewport()
    {
        var transform = RetainedReferenceTransformPolicy.Create(2560f, 1440f, 1f);
        var layout = RetainedHeaderPolicy.CreateCanvasLayout(transform);

        Assert.Same(transform, layout.ReferenceTransform);
        Assert.Equal(1f, transform.ReferenceScale);
        Assert.Equal(0f, transform.ReferenceOriginX);
        Assert.Equal(0f, transform.ReferenceOriginY);
        Assert.Equal(85f, layout.Left);
        Assert.Equal(113f, layout.Top);
        Assert.Equal(2392f, layout.Width);
        Assert.Equal(217f, layout.Height);
        Assert.Equal(20f, layout.CornerRadius);
    }

    [Fact]
    public void StepOneHeaderConvertsScaledCanvasUnitsBackToBaselinePhysicalPixels()
    {
        const float canvasScale = 2f;
        var transform = RetainedReferenceTransformPolicy.Create(2560f, 1440f, canvasScale);
        var layout = RetainedHeaderPolicy.CreateCanvasLayout(transform);

        Assert.Equal(RetainedHeaderPolicy.LeftPixels, layout.Left * canvasScale);
        Assert.Equal(RetainedHeaderPolicy.TopPixels, layout.Top * canvasScale);
        Assert.Equal(RetainedHeaderPolicy.WidthPixels, layout.Width * canvasScale);
        Assert.Equal(RetainedHeaderPolicy.HeightPixels, layout.Height * canvasScale);
        Assert.Equal(RetainedHeaderPolicy.CornerRadiusPixels, layout.CornerRadius * canvasScale);
    }

    [Theory]
    [InlineData(1920f, 1080f, 0.75f, 63.75f, 84.75f, 1794f, 162.75f, 15f)]
    [InlineData(1280f, 720f, 0.50f, 42.5f, 56.5f, 1196f, 108.5f, 10f)]
    public void StepOneHeaderScalesUniformlyWithinSixteenByNineViewports(
        float viewportWidth,
        float viewportHeight,
        float expectedScale,
        float expectedLeft,
        float expectedTop,
        float expectedWidth,
        float expectedHeight,
        float expectedRadius)
    {
        var transform = RetainedReferenceTransformPolicy.Create(viewportWidth, viewportHeight, 1f);
        var layout = RetainedHeaderPolicy.CreateCanvasLayout(transform);

        Assert.Equal(expectedScale, transform.ReferenceScale);
        Assert.Equal(0f, transform.ReferenceOriginX);
        Assert.Equal(0f, transform.ReferenceOriginY);
        Assert.Equal(expectedLeft, layout.Left);
        Assert.Equal(expectedTop, layout.Top);
        Assert.Equal(expectedWidth, layout.Width);
        Assert.Equal(expectedHeight, layout.Height);
        Assert.Equal(expectedRadius, layout.CornerRadius);
    }

    [Fact]
    public void StepOneHeaderUsesSmallerScaleAxisAndCentresUnusedViewportAxis()
    {
        var transform = RetainedReferenceTransformPolicy.Create(1600f, 1200f, 1f);
        var layout = RetainedHeaderPolicy.CreateCanvasLayout(transform);

        Assert.Equal(0.625f, transform.ReferenceScale);
        Assert.Equal(0f, transform.ReferenceOriginX);
        Assert.Equal(150f, transform.ReferenceOriginY);
        Assert.Equal(53.125f, layout.Left);
        Assert.Equal(220.625f, layout.Top);
        Assert.Equal(1495f, layout.Width);
        Assert.Equal(135.625f, layout.Height);
        Assert.Equal(12.5f, layout.CornerRadius);
        Assert.Equal(2392f / 217f, layout.Width / layout.Height, precision: 5);
    }

    [Fact]
    public void StepOneHeaderRejectsInvalidViewportAndCanvasInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RetainedReferenceTransformPolicy.Create(0f, 1440f, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RetainedReferenceTransformPolicy.Create(2560f, -1f, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RetainedReferenceTransformPolicy.Create(float.NaN, 1440f, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RetainedReferenceTransformPolicy.Create(2560f, float.PositiveInfinity, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RetainedReferenceTransformPolicy.Create(2560f, 1440f, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RetainedReferenceTransformPolicy.Create(2560f, 1440f, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RetainedReferenceTransformPolicy.Create(2560f, 1440f, float.NegativeInfinity));
    }

    [Fact]
    public void StepTwoRBackControlUsesExactReferenceGeometryAndOwnedArrowContract()
    {
        Assert.Equal("BackButton", RetainedBackControlPolicy.ButtonName);
        Assert.Equal("BackArrow", RetainedBackControlPolicy.ArrowName);
        Assert.Equal(80f, RetainedBackControlPolicy.LeftPixels);
        Assert.Equal(41f, RetainedBackControlPolicy.TopPixels);
        Assert.Equal(68f, RetainedBackControlPolicy.WidthPixels);
        Assert.Equal(68f, RetainedBackControlPolicy.HeightPixels);
        Assert.Equal(148f, RetainedBackControlPolicy.RightExclusivePixels);
        Assert.Equal(109f, RetainedBackControlPolicy.BottomExclusivePixels);
        Assert.Equal(114f, RetainedBackControlPolicy.CenterXPixels);
        Assert.Equal(75f, RetainedBackControlPolicy.CenterYPixels);
        Assert.Equal(34f, RetainedBackControlPolicy.CornerRadiusPixels);
        Assert.Equal(97f, RetainedBackControlPolicy.ArrowLeftPixels);
        Assert.Equal(58f, RetainedBackControlPolicy.ArrowTopPixels);
        Assert.Equal(34f, RetainedBackControlPolicy.ArrowWidthPixels);
        Assert.Equal(34f, RetainedBackControlPolicy.ArrowHeightPixels);
        Assert.Equal(131f, RetainedBackControlPolicy.ArrowRightExclusivePixels);
        Assert.Equal(92f, RetainedBackControlPolicy.ArrowBottomExclusivePixels);
        Assert.Equal(4f, RetainedHeaderPolicy.TopPixels - RetainedBackControlPolicy.BottomExclusivePixels);
        Assert.Equal("UltimateDuckovStatisticsBackArrowTexture", RetainedBackArrowAssetPolicy.TextureName);
        Assert.Equal("UltimateDuckovStatisticsBackArrow", RetainedBackArrowAssetPolicy.SpriteName);
        Assert.Equal(34, RetainedBackArrowAssetPolicy.WidthPixels);
        Assert.Equal(34, RetainedBackArrowAssetPolicy.HeightPixels);
        Assert.Equal(34f, RetainedBackArrowAssetPolicy.PixelsPerUnit);
    }

    [Fact]
    public void StepTwoROwnedArrowAlphaMatchesReferenceSilhouetteAndVisibleBounds()
    {
        var alpha = RetainedBackArrowAssetPolicy.DecodeTopDownAlpha();
        var sha256 = Convert.ToHexString(SHA256.HashData(alpha)).ToLowerInvariant();

        Assert.Equal(34 * 34, alpha.Length);
        Assert.True(RetainedBackArrowAssetPolicy.HasExactVisibleBounds(alpha));
        Assert.Equal(0, RetainedBackArrowAssetPolicy.VisibleLeftPixels);
        Assert.Equal(0, RetainedBackArrowAssetPolicy.VisibleTopPixels);
        Assert.Equal(34, RetainedBackArrowAssetPolicy.VisibleWidthPixels);
        Assert.Equal(34, RetainedBackArrowAssetPolicy.VisibleHeightPixels);
        Assert.Equal(406, alpha.Count(value => value > 0));
        Assert.Equal(255, alpha.Max());
        Assert.Equal(RetainedBackArrowAssetPolicy.TopDownAlphaSha256, sha256);
    }

    [Fact]
    public void GateFourAHeaderBottomBarUsesExactCropAndFullRoundedHeaderContract()
    {
        Assert.Equal("HeaderBottomBar", RetainedHeaderBottomBarPolicy.Name);
        Assert.Equal("HeaderBottomBarGraphic", RetainedHeaderBottomBarPolicy.GraphicName);
        Assert.Equal(85f, RetainedHeaderBottomBarPolicy.LeftPixels);
        Assert.Equal(321f, RetainedHeaderBottomBarPolicy.TopPixels);
        Assert.Equal(2392f, RetainedHeaderBottomBarPolicy.WidthPixels);
        Assert.Equal(9f, RetainedHeaderBottomBarPolicy.HeightPixels);
        Assert.Equal(2477f, RetainedHeaderBottomBarPolicy.RightExclusivePixels);
        Assert.Equal(330f, RetainedHeaderBottomBarPolicy.BottomExclusivePixels);
        Assert.Equal(88f, RetainedHeaderBottomBarPolicy.VisibleLeftPixels);
        Assert.Equal(321f, RetainedHeaderBottomBarPolicy.VisibleTopPixels);
        Assert.Equal(2386f, RetainedHeaderBottomBarPolicy.VisibleWidthPixels);
        Assert.Equal(9f, RetainedHeaderBottomBarPolicy.VisibleHeightPixels);
        Assert.Equal(2474f, RetainedHeaderBottomBarPolicy.VisibleRightExclusivePixels);
        Assert.Equal(330f, RetainedHeaderBottomBarPolicy.VisibleBottomExclusivePixels);
        Assert.Equal(85f, RetainedHeaderBottomBarPolicy.SurfaceLeftPixels);
        Assert.Equal(113f, RetainedHeaderBottomBarPolicy.SurfaceTopPixels);
        Assert.Equal(2392f, RetainedHeaderBottomBarPolicy.SurfaceWidthPixels);
        Assert.Equal(217f, RetainedHeaderBottomBarPolicy.SurfaceHeightPixels);
        Assert.Equal(20f, RetainedHeaderBottomBarPolicy.SurfaceCornerRadiusPixels);
        Assert.Equal(0f, RetainedHeaderBottomBarPolicy.MaskPaddingPixels);
        Assert.Equal(0, RetainedHeaderBottomBarPolicy.MaskSoftnessPixels);
        Assert.True(RetainedHeaderBottomBarPolicy.UsesRectMask2D);
        Assert.False(RetainedHeaderBottomBarPolicy.UsesFilledImage);
        Assert.False(RetainedHeaderBottomBarPolicy.UsesOnlyOneEdgeModifier);
        Assert.Equal(78f / 255f, RetainedHeaderBottomBarPolicy.Red);
        Assert.Equal(189f / 255f, RetainedHeaderBottomBarPolicy.Green);
        Assert.Equal(1f, RetainedHeaderBottomBarPolicy.Blue);
        Assert.Equal(1f, RetainedHeaderBottomBarPolicy.Alpha);
        Assert.False(RetainedHeaderBottomBarPolicy.BlocksRaycasts);
        Assert.Equal(
            RetainedHeaderPolicy.BottomExclusivePixels,
            RetainedHeaderBottomBarPolicy.BottomExclusivePixels);
        Assert.Equal(
            RetainedHeaderPolicy.CornerRadiusPixels,
            RetainedHeaderBottomBarPolicy.SurfaceCornerRadiusPixels);
        Assert.Equal(RetainedHeaderPolicy.LeftPixels, RetainedHeaderBottomBarPolicy.SurfaceLeftPixels);
        Assert.Equal(RetainedHeaderPolicy.TopPixels, RetainedHeaderBottomBarPolicy.SurfaceTopPixels);
        Assert.Equal(RetainedHeaderPolicy.WidthPixels, RetainedHeaderBottomBarPolicy.SurfaceWidthPixels);
        Assert.Equal(RetainedHeaderPolicy.HeightPixels, RetainedHeaderBottomBarPolicy.SurfaceHeightPixels);
        Assert.True(RetainedHeaderBottomBarPolicy.IsValidGraphic(
            78f / 255f,
            189f / 255f,
            1f,
            1f,
            raycastTarget: false));
        Assert.False(RetainedHeaderBottomBarPolicy.IsValidGraphic(
            78f / 255f,
            189f / 255f,
            1f,
            1f,
            raycastTarget: true));
    }

    [Theory]
    [InlineData(1280f, 720f, 42.5f, 160.5f, 1196f, 4.5f, 42.5f, 56.5f, 1196f, 108.5f, 10f)]
    [InlineData(1680f, 1050f, 55.78125f, 263.15625f, 1569.75f, 5.90625f, 55.78125f, 126.65625f, 1569.75f, 142.40625f, 13.125f)]
    [InlineData(1920f, 1080f, 63.75f, 240.75f, 1794f, 6.75f, 63.75f, 84.75f, 1794f, 162.75f, 15f)]
    [InlineData(1920f, 1200f, 63.75f, 300.75f, 1794f, 6.75f, 63.75f, 144.75f, 1794f, 162.75f, 15f)]
    [InlineData(2560f, 1440f, 85f, 321f, 2392f, 9f, 85f, 113f, 2392f, 217f, 20f)]
    public void GateFourAHeaderBottomBarUsesSharedReferenceTransformAtEveryRequiredViewport(
        float viewportWidth,
        float viewportHeight,
        float expectedLeft,
        float expectedTop,
        float expectedWidth,
        float expectedHeight,
        float expectedSurfaceLeft,
        float expectedSurfaceTop,
        float expectedSurfaceWidth,
        float expectedSurfaceHeight,
        float expectedRadius)
    {
        var layout = CreateRetainedVisualLayout(viewportWidth, viewportHeight);
        var bar = layout.HeaderBottomBar;

        Assert.Same(layout.ReferenceTransform, bar.ReferenceTransform);
        Assert.Equal(expectedLeft, bar.Left);
        Assert.Equal(expectedTop, bar.Top);
        Assert.Equal(expectedWidth, bar.Width);
        Assert.Equal(expectedHeight, bar.Height);
        Assert.Equal(expectedSurfaceLeft, bar.SurfaceLeft);
        Assert.Equal(expectedSurfaceTop, bar.SurfaceTop);
        Assert.Equal(expectedSurfaceWidth, bar.SurfaceWidth);
        Assert.Equal(expectedSurfaceHeight, bar.SurfaceHeight);
        Assert.Equal(expectedRadius, bar.SurfaceCornerRadius);
        Assert.Equal(layout.Header.Top + layout.Header.Height, bar.Top + bar.Height);
        Assert.Equal(layout.Header.Left, bar.SurfaceLeft);
        Assert.Equal(layout.Header.Top, bar.SurfaceTop);
        Assert.Equal(layout.Header.Width, bar.SurfaceWidth);
        Assert.Equal(layout.Header.Height, bar.SurfaceHeight);
        Assert.Equal(layout.Header.CornerRadius, bar.SurfaceCornerRadius);
        Assert.Equal(layout.Header.Top + layout.Header.Height, bar.SurfaceTop + bar.SurfaceHeight);
    }

    [Fact]
    public void GateFiveOverviewTabUsesExactUnselectedNativeContract()
    {
        Assert.Equal("OverviewTab", RetainedOverviewTabPolicy.BackgroundName);
        Assert.Equal("OverviewTabLabel", RetainedOverviewTabPolicy.LabelName);
        Assert.Equal("ui.overview", RetainedOverviewTabPolicy.TextKey);
        Assert.Equal("Overview", RetainedOverviewTabPolicy.EnglishFallback);
        Assert.Equal("Overview", UiText.EnglishFallbacks[RetainedOverviewTabPolicy.TextKey]);
        Assert.Equal("ResourceHanRoundedCN-Medium SDF", RetainedOverviewTabPolicy.FontAssetName);
        Assert.Equal(
            "ResourceHanRoundedCN-Medium Atlas Material Shadow",
            RetainedOverviewTabPolicy.MaterialName);
        Assert.Equal("UNDERLAY_ON", RetainedOverviewTabPolicy.NativeUnderlayKeyword);
        Assert.Equal(115f, RetainedOverviewTabPolicy.LeftPixels);
        Assert.Equal(251f, RetainedOverviewTabPolicy.TopPixels);
        Assert.Equal(79f, RetainedOverviewTabPolicy.HeightPixels);
        Assert.Equal(330f, RetainedOverviewTabPolicy.BottomExclusivePixels);
        Assert.Equal(70f, RetainedOverviewTabPolicy.ExposedHeightPixels);
        Assert.Equal(20f, RetainedOverviewTabPolicy.TopLeftCornerRadiusPixels);
        Assert.Equal(20f, RetainedOverviewTabPolicy.TopRightCornerRadiusPixels);
        Assert.Equal(0f, RetainedOverviewTabPolicy.BottomLeftCornerRadiusPixels);
        Assert.Equal(0f, RetainedOverviewTabPolicy.BottomRightCornerRadiusPixels);
        Assert.Equal(30f, RetainedOverviewTabPolicy.LeftPaddingPixels);
        Assert.Equal(30f, RetainedOverviewTabPolicy.RightPaddingPixels);
        Assert.Equal(25f, RetainedOverviewTabPolicy.TopPaddingPixels);
        Assert.Equal(25f, RetainedOverviewTabPolicy.BottomPaddingPixels);
        Assert.Equal(29f, RetainedOverviewTabPolicy.NominalLabelHeightPixels);
        Assert.Equal(36f, RetainedOverviewTabPolicy.ReferenceFontSize);
        Assert.Equal(161.91875f, RetainedOverviewTabPolicy.AuditedNativePreferredWidthPixels);
        Assert.Equal(221.91875f, RetainedOverviewTabPolicy.AuditedReferenceTabWidthPixels);
        Assert.Equal(30f / 255f, RetainedOverviewTabVisualStatePolicy.UnselectedRed);
        Assert.Equal(66f / 255f, RetainedOverviewTabVisualStatePolicy.UnselectedGreen);
        Assert.Equal(94f / 255f, RetainedOverviewTabVisualStatePolicy.UnselectedBlue);
        Assert.Equal(0.75f, RetainedOverviewTabVisualStatePolicy.UnselectedAlpha);
        Assert.True(RetainedOverviewTabPolicy.BackgroundBlocksRaycasts);
        Assert.False(RetainedOverviewTabPolicy.LabelBlocksRaycasts);
        Assert.False(RetainedOverviewTabPolicy.WordWrapping);
        Assert.False(RetainedOverviewTabPolicy.AutoSizing);
        Assert.Equal(0f, RetainedOverviewTabPolicy.CharacterSpacing);
        Assert.Equal(0f, RetainedOverviewTabPolicy.WordSpacing);
        Assert.Equal(0f, RetainedOverviewTabPolicy.LineSpacing);
        Assert.Equal(0f, RetainedOverviewTabPolicy.ParagraphSpacing);
        Assert.True(RetainedOverviewTabPolicy.UsesTopEdgeModifier);
        Assert.False(RetainedOverviewTabPolicy.UsesFilledImage);
        Assert.False(RetainedOverviewTabPolicy.UsesHorizontalTypographyCompensation);
        Assert.True(RetainedOverviewTabPolicy.RequiresNativeUnderlay);
        Assert.Equal(
            RetainedHeaderPolicy.BottomExclusivePixels,
            RetainedOverviewTabPolicy.BottomExclusivePixels);
        Assert.Equal(
            RetainedHeaderBottomBarPolicy.TopPixels,
            RetainedOverviewTabPolicy.TopPixels + RetainedOverviewTabPolicy.ExposedHeightPixels);
        Assert.Equal(
            RetainedOverviewTabPolicy.NominalLabelHeightPixels,
            RetainedOverviewTabPolicy.HeightPixels
            - RetainedOverviewTabPolicy.TopPaddingPixels
            - RetainedOverviewTabPolicy.BottomPaddingPixels);
        var unselectedColor = RetainedOverviewTabVisualStatePolicy.Resolve(
            StatisticsPanelTab.Runs,
            StatisticsPanelTab.Overview);
        Assert.True(RetainedOverviewTabVisualStatePolicy.IsExactColor(
            unselectedColor,
            30f / 255f,
            66f / 255f,
            94f / 255f,
            0.75f));
        Assert.False(RetainedOverviewTabVisualStatePolicy.IsExactColor(
            unselectedColor,
            78f / 255f,
            189f / 255f,
            1f,
            1f));
    }

    [Theory]
    [InlineData(1280f, 720f, 0.5f, 0f, 57.5f, 125.5f, 39.5f, 10f, 15f, 12.5f, 18f)]
    [InlineData(1680f, 1050f, 0.65625f, 52.5f, 75.46875f, 217.21875f, 51.84375f, 13.125f, 19.6875f, 16.40625f, 23.625f)]
    [InlineData(1920f, 1080f, 0.75f, 0f, 86.25f, 188.25f, 59.25f, 15f, 22.5f, 18.75f, 27f)]
    [InlineData(1920f, 1200f, 0.75f, 60f, 86.25f, 248.25f, 59.25f, 15f, 22.5f, 18.75f, 27f)]
    [InlineData(2560f, 1440f, 1f, 0f, 115f, 251f, 79f, 20f, 30f, 25f, 36f)]
    public void GateFiveOverviewTabUsesSharedReferenceTransformAtEveryEstablishedViewport(
        float viewportWidth,
        float viewportHeight,
        float expectedScale,
        float expectedOriginY,
        float expectedLeft,
        float expectedTop,
        float expectedHeight,
        float expectedRadius,
        float expectedHorizontalPadding,
        float expectedVerticalPadding,
        float expectedFontSize)
    {
        var layout = CreateRetainedVisualLayout(viewportWidth, viewportHeight);
        var tab = layout.OverviewTab;
        var expectedPreferredWidth =
            RetainedOverviewTabPolicy.AuditedNativePreferredWidthPixels * expectedScale;

        Assert.Same(layout.ReferenceTransform, tab.ReferenceTransform);
        Assert.Equal(expectedScale, layout.ReferenceTransform.ReferenceScale);
        Assert.Equal(expectedOriginY, layout.ReferenceTransform.ReferenceOriginY);
        Assert.Equal(expectedLeft, tab.Left);
        Assert.Equal(expectedTop, tab.Top);
        Assert.Equal(expectedHeight, tab.Height);
        Assert.Equal(RetainedOverviewTabPolicy.ExposedHeightPixels * expectedScale, tab.ExposedHeight);
        Assert.Equal(expectedRadius, tab.CornerRadius);
        Assert.Equal(expectedHorizontalPadding, tab.LeftPadding);
        Assert.Equal(expectedHorizontalPadding, tab.RightPadding);
        Assert.Equal(expectedVerticalPadding, tab.TopPadding);
        Assert.Equal(expectedVerticalPadding, tab.BottomPadding);
        Assert.Equal(expectedFontSize, tab.FontSize);
        Assert.Equal(expectedPreferredWidth, tab.PreferredLabelWidth);
        Assert.Equal(expectedPreferredWidth + expectedHorizontalPadding * 2f, tab.Width);
        Assert.Equal(expectedPreferredWidth, tab.LabelWidth);
        Assert.Equal(RetainedOverviewTabPolicy.NominalLabelHeightPixels * expectedScale, tab.LabelHeight);
        Assert.Equal(tab.Left + tab.LeftPadding, tab.LabelLeft);
        Assert.Equal(tab.Top + tab.TopPadding, tab.LabelTop);
        Assert.Equal(layout.Header.Top + layout.Header.Height, tab.Top + tab.Height);
        Assert.Equal(layout.HeaderBottomBar.Top, tab.Top + tab.ExposedHeight);
    }

    [Fact]
    public void GateFiveAOverviewLayoutAcceptsOnlyTheSharedReferenceTransform()
    {
        var overviewLayoutMethod = Assert.Single(
            typeof(RetainedOverviewTabPolicy)
                .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public),
            method => method.Name == nameof(RetainedOverviewTabPolicy.CreateCanvasLayout));
        var visualLayoutMethods = typeof(RetainedVisualLayoutPolicy)
            .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
            .Where(method => method.Name == nameof(RetainedVisualLayoutPolicy.Create))
            .ToArray();

        var overviewParameter = Assert.Single(overviewLayoutMethod.GetParameters());
        Assert.Equal(typeof(RetainedReferenceTransform), overviewParameter.ParameterType);
        Assert.Equal(2, visualLayoutMethods.Length);
        Assert.DoesNotContain(
            visualLayoutMethods.SelectMany(method => method.GetParameters()),
            parameter => parameter.Name != null
                && parameter.Name.Contains("preferred", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GateFiveAOverviewUsesExactAuthoritativeReferenceGeometry()
    {
        var tab = CreateRetainedVisualLayout(2560f, 1440f).OverviewTab;

        Assert.Equal(115f, tab.Left);
        Assert.Equal(251f, tab.Top);
        Assert.Equal(221.91875f, tab.Width);
        Assert.Equal(79f, tab.Height);
        Assert.Equal(336.91875f, tab.Left + tab.Width);
        Assert.Equal(330f, tab.Top + tab.Height);
        Assert.Equal(145f, tab.LabelLeft);
        Assert.Equal(276f, tab.LabelTop);
        Assert.Equal(161.91875f, tab.LabelWidth);
        Assert.Equal(29f, tab.LabelHeight);
        Assert.Equal(306.91875f, tab.LabelLeft + tab.LabelWidth);
        Assert.Equal(305f, tab.LabelTop + tab.LabelHeight);
        Assert.Equal(30f, tab.LabelLeft - tab.Left);
        Assert.Equal(30f, tab.Left + tab.Width - tab.LabelLeft - tab.LabelWidth, 4);
        Assert.Equal(25f, tab.LabelTop - tab.Top);
        Assert.Equal(25f, tab.Top + tab.Height - tab.LabelTop - tab.LabelHeight);
        Assert.True(tab.Width > tab.Height);
        Assert.True(tab.LabelWidth > tab.Height);
    }

    [Theory]
    [InlineData(1280f, 720f, 57.5f, 125.5f, 110.959375f, 39.5f, 72.5f, 138f, 80.959375f, 14.5f, 15f, 12.5f, 10f, 18f)]
    [InlineData(1680f, 1050f, 75.46875f, 217.21875f, 145.6341796875f, 51.84375f, 95.15625f, 233.625f, 106.2591796875f, 19.03125f, 19.6875f, 16.40625f, 13.125f, 23.625f)]
    [InlineData(1920f, 1080f, 86.25f, 188.25f, 166.4390625f, 59.25f, 108.75f, 207f, 121.4390625f, 21.75f, 22.5f, 18.75f, 15f, 27f)]
    [InlineData(1920f, 1200f, 86.25f, 248.25f, 166.4390625f, 59.25f, 108.75f, 267f, 121.4390625f, 21.75f, 22.5f, 18.75f, 15f, 27f)]
    [InlineData(2560f, 1440f, 115f, 251f, 221.91875f, 79f, 145f, 276f, 161.91875f, 29f, 30f, 25f, 20f, 36f)]
    public void GateFiveAOverviewPreservesPhysicalGeometryAcrossCanvasScaleFactors(
        float viewportWidth,
        float viewportHeight,
        float expectedTabLeft,
        float expectedTabTop,
        float expectedTabWidth,
        float expectedTabHeight,
        float expectedLabelLeft,
        float expectedLabelTop,
        float expectedLabelWidth,
        float expectedLabelHeight,
        float expectedHorizontalPadding,
        float expectedVerticalPadding,
        float expectedCornerRadius,
        float expectedFontSize)
    {
        foreach (var canvasScaleFactor in new[] { 1f, 2f, 3f })
        {
            var tab = CreateRetainedVisualLayout(
                viewportWidth,
                viewportHeight,
                canvasScaleFactor).OverviewTab;

            Assert.Equal(expectedTabLeft, tab.Left * canvasScaleFactor, 4);
            Assert.Equal(expectedTabTop, tab.Top * canvasScaleFactor, 4);
            Assert.Equal(expectedTabWidth, tab.Width * canvasScaleFactor, 4);
            Assert.Equal(expectedTabHeight, tab.Height * canvasScaleFactor, 4);
            Assert.Equal(expectedLabelLeft, tab.LabelLeft * canvasScaleFactor, 4);
            Assert.Equal(expectedLabelTop, tab.LabelTop * canvasScaleFactor, 4);
            Assert.Equal(expectedLabelWidth, tab.LabelWidth * canvasScaleFactor, 4);
            Assert.Equal(expectedLabelHeight, tab.LabelHeight * canvasScaleFactor, 4);
            Assert.Equal(expectedHorizontalPadding, tab.LeftPadding * canvasScaleFactor, 4);
            Assert.Equal(expectedHorizontalPadding, tab.RightPadding * canvasScaleFactor, 4);
            Assert.Equal(expectedVerticalPadding, tab.TopPadding * canvasScaleFactor, 4);
            Assert.Equal(expectedVerticalPadding, tab.BottomPadding * canvasScaleFactor, 4);
            Assert.Equal(expectedCornerRadius, tab.CornerRadius * canvasScaleFactor, 4);
            Assert.Equal(expectedFontSize, tab.FontSize * canvasScaleFactor, 4);
        }
    }

    [Fact]
    public void GateFiveOverviewActivationUpdatesAuthoritativeSelectionAndSynchronizesIdempotently()
    {
        var instanceFields = typeof(RetainedOverviewTabActivation).GetFields(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var staticFields = typeof(RetainedOverviewTabActivation).GetFields(
            System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);
        var interaction = new PanelInteractionState();
        interaction.SelectTab(StatisticsPanelTab.Crafting);
        var synchronizedTabs = new List<StatisticsPanelTab>();
        var activation = new RetainedOverviewTabActivation(selected =>
            RetainedTabSelectionPolicy.SelectAndSynchronize(
                interaction,
                synchronizedTabs.Add,
                selected));

        activation.Invoke();
        activation.Invoke();

        Assert.Single(instanceFields);
        Assert.Equal(typeof(Action<StatisticsPanelTab>), instanceFields[0].FieldType);
        Assert.Empty(staticFields);
        Assert.Equal(StatisticsPanelTab.Overview, interaction.SelectedTab);
        Assert.Equal(
            new[] { StatisticsPanelTab.Overview, StatisticsPanelTab.Overview },
            synchronizedTabs);
    }

    [Fact]
    public void GateSixResolvesExactSelectedAndUnselectedOverviewColors()
    {
        var selected = RetainedOverviewTabVisualStatePolicy.Resolve(
            StatisticsPanelTab.Overview,
            StatisticsPanelTab.Overview);
        var unselected = RetainedOverviewTabVisualStatePolicy.Resolve(
            StatisticsPanelTab.Runs,
            StatisticsPanelTab.Overview);

        Assert.Equal(78f / 255f, selected.Red);
        Assert.Equal(189f / 255f, selected.Green);
        Assert.Equal(1f, selected.Blue);
        Assert.Equal(1f, selected.Alpha);
        Assert.Equal(RetainedHeaderBottomBarPolicy.Red, selected.Red);
        Assert.Equal(RetainedHeaderBottomBarPolicy.Green, selected.Green);
        Assert.Equal(RetainedHeaderBottomBarPolicy.Blue, selected.Blue);
        Assert.Equal(RetainedHeaderBottomBarPolicy.Alpha, selected.Alpha);
        Assert.Equal(30f / 255f, unselected.Red);
        Assert.Equal(66f / 255f, unselected.Green);
        Assert.Equal(94f / 255f, unselected.Blue);
        Assert.Equal(0.75f, unselected.Alpha);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RetainedOverviewTabVisualStatePolicy.Resolve(
                (StatisticsPanelTab)int.MaxValue,
                StatisticsPanelTab.Overview));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RetainedOverviewTabVisualStatePolicy.Resolve(
                StatisticsPanelTab.Overview,
                (StatisticsPanelTab)int.MaxValue));
    }

    [Fact]
    public void GateSixProductionVisualStatePathAppliesInitialAndSubsequentStatesToOneTarget()
    {
        var background = new object();
        var assignments = new List<(object Target, RetainedRgbaColor Color)>();
        var visualState = new RetainedOverviewTabVisualState<object>(
            background,
            StatisticsPanelTab.Overview,
            (target, color) => assignments.Add((target, color)));

        visualState.Apply(StatisticsPanelTab.Overview);
        visualState.Apply(StatisticsPanelTab.Runs);
        visualState.Apply(StatisticsPanelTab.Overview);
        visualState.Apply(StatisticsPanelTab.Overview);

        Assert.Same(background, visualState.Target);
        Assert.Equal(4, assignments.Count);
        Assert.All(assignments, assignment => Assert.Same(background, assignment.Target));
        Assert.Equal(1f, assignments[0].Color.Alpha);
        Assert.Equal(30f / 255f, assignments[1].Color.Red);
        Assert.Equal(0.75f, assignments[1].Color.Alpha);
        Assert.Equal(78f / 255f, assignments[2].Color.Red);
        Assert.Equal(1f, assignments[2].Color.Alpha);
        Assert.Equal(assignments[2].Color.Red, assignments[3].Color.Red);
        Assert.Equal(assignments[2].Color.Green, assignments[3].Color.Green);
        Assert.Equal(assignments[2].Color.Blue, assignments[3].Color.Blue);
        Assert.Equal(assignments[2].Color.Alpha, assignments[3].Color.Alpha);
    }

    [Fact]
    public void GateSixOverviewClickUpdatesAuthoritativeStateThenAppliesSelectedColor()
    {
        var interaction = new PanelInteractionState();
        interaction.SelectTab(StatisticsPanelTab.Crafting);
        var background = new object();
        var assignments = new List<RetainedRgbaColor>();
        var visualState = new RetainedOverviewTabVisualState<object>(
            background,
            StatisticsPanelTab.Overview,
            (_, color) => assignments.Add(color));
        visualState.Apply(interaction.SelectedTab);
        var activation = new RetainedOverviewTabActivation(selected =>
            RetainedTabSelectionPolicy.SelectAndSynchronize(
                interaction,
                visualState.Apply,
                selected));

        activation.Invoke();

        Assert.Equal(StatisticsPanelTab.Overview, interaction.SelectedTab);
        Assert.Equal(2, assignments.Count);
        Assert.Equal(0.75f, assignments[0].Alpha);
        Assert.Equal(78f / 255f, assignments[1].Red);
        Assert.Equal(189f / 255f, assignments[1].Green);
        Assert.Equal(1f, assignments[1].Blue);
        Assert.Equal(1f, assignments[1].Alpha);
        Assert.Same(background, visualState.Target);
    }

    [Fact]
    public void GateSixKeepsGateFiveCompositionGeometryAndTypographyFrozen()
    {
        var tab = CreateRetainedVisualLayout(2560f, 1440f).OverviewTab;

        Assert.Equal(5, RetainedShellCompositionPolicy.RootChildCount);
        Assert.Equal(8, RetainedShellCompositionPolicy.GraphicCount);
        Assert.Equal(2, RetainedShellCompositionPolicy.ButtonCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.OverviewTabChildCount);
        Assert.Equal(115f, tab.Left);
        Assert.Equal(251f, tab.Top);
        Assert.Equal(221.91875f, tab.Width);
        Assert.Equal(79f, tab.Height);
        Assert.Equal(145f, tab.LabelLeft);
        Assert.Equal(276f, tab.LabelTop);
        Assert.Equal(161.91875f, tab.LabelWidth);
        Assert.Equal(29f, tab.LabelHeight);
        Assert.Equal(36f, tab.FontSize);
        Assert.Equal("ResourceHanRoundedCN-Medium SDF", RetainedOverviewTabPolicy.FontAssetName);
        Assert.Equal(
            "ResourceHanRoundedCN-Medium Atlas Material Shadow",
            RetainedOverviewTabPolicy.MaterialName);
        Assert.Equal("UNDERLAY_ON", RetainedOverviewTabPolicy.NativeUnderlayKeyword);
    }

    [Fact]
    public void StepThreeHeaderTitleUsesExactNativeTypographyAndReferenceBoundsContract()
    {
        Assert.Equal("HeaderTitle", RetainedHeaderTitlePolicy.Name);
        Assert.Equal("Ultimate Duckov Statistics", RetainedHeaderTitlePolicy.Text);
        Assert.Equal(
            "Canvas/MainMenuContainer/Menu/OptionsPanel/Text (TMP)",
            RetainedHeaderTitlePolicy.NativeSourcePath);
        Assert.Equal("ResourceHanRoundedCN-Medium SDF", RetainedHeaderTitlePolicy.FontAssetName);
        Assert.Equal(
            "ResourceHanRoundedCN-Medium Atlas Material Shadow",
            RetainedHeaderTitlePolicy.MaterialName);
        Assert.Equal(85f, RetainedHeaderTitlePolicy.ReferenceFontSize);
        Assert.Equal(113f, RetainedHeaderTitlePolicy.LeftPixels);
        Assert.Equal(141f, RetainedHeaderTitlePolicy.TopPixels);
        Assert.Equal(921f, RetainedHeaderTitlePolicy.WidthPixels);
        Assert.Equal(83f, RetainedHeaderTitlePolicy.HeightPixels);
        Assert.Equal(1034f, RetainedHeaderTitlePolicy.RightExclusivePixels);
        Assert.Equal(224f, RetainedHeaderTitlePolicy.BottomExclusivePixels);
        Assert.Equal(114f, RetainedHeaderTitlePolicy.PrincipalLeftPixels);
        Assert.Equal(144f, RetainedHeaderTitlePolicy.PrincipalTopPixels);
        Assert.Equal(905f, RetainedHeaderTitlePolicy.PrincipalWidthPixels);
        Assert.Equal(68f, RetainedHeaderTitlePolicy.PrincipalHeightPixels);
        Assert.Equal(1019f, RetainedHeaderTitlePolicy.PrincipalRightExclusivePixels);
        Assert.Equal(212f, RetainedHeaderTitlePolicy.PrincipalBottomExclusivePixels);
        Assert.Equal(1f, RetainedHeaderTitlePolicy.Red);
        Assert.Equal(1f, RetainedHeaderTitlePolicy.Green);
        Assert.Equal(1f, RetainedHeaderTitlePolicy.Blue);
        Assert.Equal(1f, RetainedHeaderTitlePolicy.Alpha);
        Assert.False(RetainedHeaderTitlePolicy.BlocksRaycasts);
        Assert.False(RetainedHeaderTitlePolicy.WordWrapping);
        Assert.False(RetainedHeaderTitlePolicy.AutoSizing);
    }

    [Theory]
    [InlineData(1280f, 720f, 56.5f, 70.5f, 460.5f, 41.5f, 42.5f)]
    [InlineData(1680f, 1050f, 74.15625f, 145.03125f, 604.40625f, 54.46875f, 55.78125f)]
    [InlineData(1920f, 1080f, 84.75f, 105.75f, 690.75f, 62.25f, 63.75f)]
    [InlineData(1920f, 1200f, 84.75f, 165.75f, 690.75f, 62.25f, 63.75f)]
    [InlineData(2560f, 1440f, 113f, 141f, 921f, 83f, 85f)]
    public void StepThreeHeaderTitleUsesSharedReferenceTransformAtEveryRequiredViewport(
        float viewportWidth,
        float viewportHeight,
        float expectedLeft,
        float expectedTop,
        float expectedWidth,
        float expectedHeight,
        float expectedFontSize)
    {
        var layout = CreateRetainedVisualLayout(viewportWidth, viewportHeight);

        Assert.Same(layout.ReferenceTransform, layout.Header.ReferenceTransform);
        Assert.Same(layout.ReferenceTransform, layout.BackControl.ReferenceTransform);
        Assert.Same(layout.ReferenceTransform, layout.HeaderTitle.ReferenceTransform);
        Assert.Equal(expectedLeft, layout.HeaderTitle.Left);
        Assert.Equal(expectedTop, layout.HeaderTitle.Top);
        Assert.Equal(expectedWidth, layout.HeaderTitle.Width);
        Assert.Equal(expectedHeight, layout.HeaderTitle.Height);
        Assert.Equal(expectedFontSize, layout.HeaderTitle.FontSize);
    }

    [Fact]
    public void StepTwoBackCircleAndArrowShareExactCentreAndCircleRadius()
    {
        var layout = CreateRetainedVisualLayout(2560f, 1440f).BackControl;

        Assert.Equal(layout.Left + layout.Width / 2f, layout.ArrowLeft + layout.ArrowWidth / 2f);
        Assert.Equal(layout.Top + layout.Height / 2f, layout.ArrowTop + layout.ArrowHeight / 2f);
        Assert.Equal(layout.Width, layout.Height);
        Assert.Equal(layout.Width / 2f, layout.CornerRadius);
    }

    [Fact]
    public void StepTwoBackPresentationUsesExactColoursOpacityAndRaycasts()
    {
        Assert.Equal(0f, RetainedBackControlPolicy.BackgroundRed);
        Assert.Equal(0f, RetainedBackControlPolicy.BackgroundGreen);
        Assert.Equal(0f, RetainedBackControlPolicy.BackgroundBlue);
        Assert.Equal(0.50f, RetainedBackControlPolicy.BackgroundAlpha);
        Assert.True(RetainedBackControlPolicy.BackgroundBlocksRaycasts);
        Assert.Equal(1f, RetainedBackControlPolicy.ArrowRed);
        Assert.Equal(1f, RetainedBackControlPolicy.ArrowGreen);
        Assert.Equal(1f, RetainedBackControlPolicy.ArrowBlue);
        Assert.Equal(1f, RetainedBackControlPolicy.ArrowAlpha);
        Assert.False(RetainedBackControlPolicy.ArrowBlocksRaycasts);
        Assert.True(RetainedBackControlPolicy.PreserveArrowAspect);
        Assert.True(RetainedBackControlPolicy.IsValidBackgroundGraphic(
            0f, 0f, 0f, 0.50f, raycastTarget: true));
        Assert.False(RetainedBackControlPolicy.IsValidBackgroundGraphic(
            0f, 0f, 0f, 0.50f, raycastTarget: false));
        Assert.True(RetainedBackControlPolicy.IsValidArrowGraphic(
            1f, 1f, 1f, 1f, raycastTarget: false, preserveAspect: true));
        Assert.False(RetainedBackControlPolicy.IsValidArrowGraphic(
            1f, 1f, 1f, 1f, raycastTarget: true, preserveAspect: true));
        Assert.Equal(0.75f, RetainedBackControlPolicy.EffectiveBackgroundOpacity);
    }

    [Fact]
    public void StepTwoBackActivationInvokesSuppliedCloseExactlyOnce()
    {
        var closeCount = 0;
        var activation = new RetainedBackControlActivation(() => closeCount++);

        activation.Invoke();

        Assert.Equal(1, closeCount);
    }

    [Theory]
    [InlineData(2560f, 1440f, 1f, 0f, 80f, 41f, 68f, 34f, 97f, 58f, 34f)]
    [InlineData(1920f, 1080f, 0.75f, 0f, 60f, 30.75f, 51f, 25.5f, 72.75f, 43.5f, 25.5f)]
    [InlineData(1280f, 720f, 0.50f, 0f, 40f, 20.5f, 34f, 17f, 48.5f, 29f, 17f)]
    [InlineData(1920f, 1200f, 0.75f, 60f, 60f, 90.75f, 51f, 25.5f, 72.75f, 103.5f, 25.5f)]
    public void StepTwoBackControlUsesSharedReferenceTransformAtEveryRequiredViewport(
        float viewportWidth,
        float viewportHeight,
        float expectedScale,
        float expectedOriginY,
        float expectedLeft,
        float expectedTop,
        float expectedSize,
        float expectedRadius,
        float expectedArrowLeft,
        float expectedArrowTop,
        float expectedArrowSize)
    {
        var layout = CreateRetainedVisualLayout(viewportWidth, viewportHeight);

        Assert.Same(layout.ReferenceTransform, layout.Header.ReferenceTransform);
        Assert.Same(layout.ReferenceTransform, layout.BackControl.ReferenceTransform);
        Assert.Equal(expectedScale, layout.ReferenceTransform.ReferenceScale);
        Assert.Equal(0f, layout.ReferenceTransform.ReferenceOriginX);
        Assert.Equal(expectedOriginY, layout.ReferenceTransform.ReferenceOriginY);
        Assert.Equal(expectedLeft, layout.BackControl.Left);
        Assert.Equal(expectedTop, layout.BackControl.Top);
        Assert.Equal(expectedSize, layout.BackControl.Width);
        Assert.Equal(expectedSize, layout.BackControl.Height);
        Assert.Equal(expectedRadius, layout.BackControl.CornerRadius);
        Assert.Equal(layout.BackControl.Width / 2f, layout.BackControl.CornerRadius);
        Assert.Equal(expectedArrowLeft, layout.BackControl.ArrowLeft);
        Assert.Equal(expectedArrowTop, layout.BackControl.ArrowTop);
        Assert.Equal(expectedArrowSize, layout.BackControl.ArrowWidth);
        Assert.Equal(expectedArrowSize, layout.BackControl.ArrowHeight);
    }

    [Fact]
    public void GateFiveViewportChangeRefreshesAllRetainedVisualsWithoutHierarchyDuplication()
    {
        var baseline = CreateRetainedVisualLayout(2560f, 1440f);
        var resized = CreateRetainedVisualLayout(1280f, 720f);

        Assert.NotSame(baseline.ReferenceTransform, resized.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.Header.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.OverviewTab.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.HeaderBottomBar.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.BackControl.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.HeaderTitle.ReferenceTransform);
        Assert.Equal(baseline.Header.Width / 2f, resized.Header.Width);
        Assert.Equal(baseline.OverviewTab.Width / 2f, resized.OverviewTab.Width);
        Assert.Equal(baseline.OverviewTab.Height / 2f, resized.OverviewTab.Height);
        Assert.Equal(baseline.OverviewTab.FontSize / 2f, resized.OverviewTab.FontSize);
        Assert.Equal(baseline.HeaderBottomBar.Width / 2f, resized.HeaderBottomBar.Width);
        Assert.Equal(baseline.HeaderBottomBar.Height / 2f, resized.HeaderBottomBar.Height);
        Assert.Equal(baseline.BackControl.Width / 2f, resized.BackControl.Width);
        Assert.Equal(baseline.HeaderTitle.Width / 2f, resized.HeaderTitle.Width);
        Assert.Equal(baseline.HeaderTitle.FontSize / 2f, resized.HeaderTitle.FontSize);
        Assert.Equal(5, RetainedShellCompositionPolicy.RootChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.HeaderChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.OverviewTabChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewTabLabelChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.HeaderBottomBarChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.HeaderBottomBarGraphicChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.HeaderTitleChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.BackButtonChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.BackArrowChildCount);
        Assert.Equal(8, RetainedShellCompositionPolicy.GraphicCount);
        Assert.Equal(2, RetainedShellCompositionPolicy.ButtonCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.RectMaskCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.OnlyOneEdgeModifierCount);
    }

    private static RetainedVisualCanvasLayout CreateRetainedVisualLayout(
        float viewportWidth,
        float viewportHeight,
        float canvasScaleFactor = 1f)
    {
        var referenceTransform = RetainedReferenceTransformPolicy.Create(
            viewportWidth,
            viewportHeight,
            canvasScaleFactor);
        return RetainedVisualLayoutPolicy.Create(referenceTransform);
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
        var geometry = RetainedTabGeometryPolicy.Create(
            2334f,
            148f,
            8f,
            0f,
            38f,
            installedEnglishWidths);

        Assert.Equal(9, geometry.Widths.Count);
        Assert.False(geometry.RequiresScrolling);
        Assert.True(geometry.ContentWidth < 2334f);
        for (var index = 0; index < installedEnglishWidths.Length; index++)
            Assert.True(geometry.Widths[index] >= installedEnglishWidths[index] + 38f);
    }

    [Fact]
    public void LongLocalizedNavigationRemainsOneScrollableRow()
    {
        var geometry = RetainedTabGeometryPolicy.Create(
            939f,
            168f,
            10f,
            10f,
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
