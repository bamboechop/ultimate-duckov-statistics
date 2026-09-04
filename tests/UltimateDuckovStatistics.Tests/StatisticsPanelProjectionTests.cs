using System.Security.Cryptography;
using System.Globalization;
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

    private static readonly string?[] ButtonAnimationHierarchy =
    {
        NativeMenuPresentationPolicy.ButtonAnimationTypeName,
        "UnityEngine.MonoBehaviour"
    };

    private static readonly string?[] ToggleAnimationHierarchy =
    {
        "Duckov.UI.Animations.ScaleToggle",
        NativeMenuPresentationPolicy.ToggleAnimationTypeName,
        "UnityEngine.MonoBehaviour"
    };

    private static readonly string?[] ToggleComponentHierarchy =
    {
        "Duckov.UI.Animations.ChangeGraphicsColorToggle",
        NativeMenuPresentationPolicy.ToggleComponentTypeName,
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
        Assert.Equal(30f / 255f, RetainedTabVisualStatePolicy.UnselectedRed);
        Assert.Equal(66f / 255f, RetainedTabVisualStatePolicy.UnselectedGreen);
        Assert.Equal(94f / 255f, RetainedTabVisualStatePolicy.UnselectedBlue);
        Assert.Equal(0.75f, RetainedTabVisualStatePolicy.UnselectedAlpha);
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
        var unselectedColor = RetainedTabVisualStatePolicy.Resolve(
            StatisticsPanelTab.Runs,
            StatisticsPanelTab.Overview);
        Assert.True(RetainedTabVisualStatePolicy.IsExactColor(
            unselectedColor,
            30f / 255f,
            66f / 255f,
            94f / 255f,
            0.75f));
        Assert.False(RetainedTabVisualStatePolicy.IsExactColor(
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
        Assert.Equal(3, visualLayoutMethods.Length);
        var measuredStripOverload = Assert.Single(
            visualLayoutMethods,
            method => method.GetParameters().Length == 2
                      && method.GetParameters()[0].ParameterType == typeof(RetainedReferenceTransform));
        Assert.Equal(typeof(IReadOnlyList<float>), measuredStripOverload.GetParameters()[1].ParameterType);
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
        var instanceFields = typeof(RetainedTabActivation).GetFields(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var staticFields = typeof(RetainedTabActivation).GetFields(
            System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);
        var interaction = new PanelInteractionState();
        interaction.SelectTab(StatisticsPanelTab.Crafting);
        var synchronizedTabs = new List<StatisticsPanelTab>();
        var activation = new RetainedTabActivation(selected =>
            RetainedTabSelectionPolicy.SelectAndSynchronize(
                interaction,
                synchronizedTabs.Add,
                selected),
            StatisticsPanelTab.Overview);

        activation.Invoke();
        activation.Invoke();

        Assert.Equal(2, instanceFields.Length);
        Assert.Contains(instanceFields, field => field.FieldType == typeof(Action<StatisticsPanelTab>));
        Assert.Contains(instanceFields, field => field.FieldType == typeof(StatisticsPanelTab));
        Assert.Empty(staticFields);
        Assert.Equal(StatisticsPanelTab.Overview, interaction.SelectedTab);
        Assert.Equal(
            new[] { StatisticsPanelTab.Overview, StatisticsPanelTab.Overview },
            synchronizedTabs);
    }

    [Fact]
    public void GateSixResolvesExactSelectedAndUnselectedOverviewColors()
    {
        var selected = RetainedTabVisualStatePolicy.Resolve(
            StatisticsPanelTab.Overview,
            StatisticsPanelTab.Overview);
        var unselected = RetainedTabVisualStatePolicy.Resolve(
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
            RetainedTabVisualStatePolicy.Resolve(
                (StatisticsPanelTab)int.MaxValue,
                StatisticsPanelTab.Overview));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RetainedTabVisualStatePolicy.Resolve(
                StatisticsPanelTab.Overview,
                (StatisticsPanelTab)int.MaxValue));
    }

    [Fact]
    public void GateSixProductionVisualStatePathAppliesInitialAndSubsequentStatesToOneTarget()
    {
        var background = new object();
        var assignments = new List<(object Target, RetainedRgbaColor Color)>();
        var visualState = new RetainedTabVisualState<object>(
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
        var visualState = new RetainedTabVisualState<object>(
            background,
            StatisticsPanelTab.Overview,
            (_, color) => assignments.Add(color));
        visualState.Apply(interaction.SelectedTab);
        var activation = new RetainedTabActivation(selected =>
            RetainedTabSelectionPolicy.SelectAndSynchronize(
                interaction,
                visualState.Apply,
                selected),
            StatisticsPanelTab.Overview);

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
    public void GateEightPreservesGateFiveGeometryAndTypographyWhileAddingOnlyOverviewContent()
    {
        var tab = CreateRetainedVisualLayout(2560f, 1440f).OverviewTab;

        Assert.Equal(14, RetainedShellCompositionPolicy.RootChildCount);
        Assert.Equal(76, RetainedShellCompositionPolicy.GraphicCount);
        Assert.Equal(10, RetainedShellCompositionPolicy.ButtonCount);
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
    public void GateSixBTabLabelShadowRestoresNativeTmpSpatialContract()
    {
        Assert.Equal(
            "UltimateDuckovStatistics Retained Tab Label Material",
            RetainedTabLabelShadowPolicy.OwnedMaterialName);
        Assert.Equal("UNDERLAY_ON", RetainedTabLabelShadowPolicy.UnderlayKeyword);
        Assert.Equal("RATIOS_OFF", RetainedTabLabelShadowPolicy.RatioBypassKeyword);
        Assert.Equal("_MainTex", RetainedTabLabelShadowPolicy.MainTextureProperty);
        Assert.Equal("_UnderlayColor", RetainedTabLabelShadowPolicy.UnderlayColorProperty);
        Assert.Equal("_UnderlayOffsetX", RetainedTabLabelShadowPolicy.UnderlayOffsetXProperty);
        Assert.Equal("_UnderlayOffsetY", RetainedTabLabelShadowPolicy.UnderlayOffsetYProperty);
        Assert.Equal("_UnderlayDilate", RetainedTabLabelShadowPolicy.UnderlayDilateProperty);
        Assert.Equal("_UnderlaySoftness", RetainedTabLabelShadowPolicy.UnderlaySoftnessProperty);
        Assert.Equal("_ScaleRatioC", RetainedTabLabelShadowPolicy.ScaleRatioCProperty);
        Assert.Equal(0f, RetainedTabLabelShadowPolicy.Red);
        Assert.Equal(0f, RetainedTabLabelShadowPolicy.Green);
        Assert.Equal(0f, RetainedTabLabelShadowPolicy.Blue);
        Assert.Equal(0.57f, RetainedTabLabelShadowPolicy.Alpha);
        Assert.Equal(1f, RetainedTabLabelShadowPolicy.NativeUnderlayOffsetX);
        Assert.Equal(-1f, RetainedTabLabelShadowPolicy.NativeUnderlayOffsetY);
        Assert.Equal(-0.25f, RetainedTabLabelShadowPolicy.NativeUnderlayDilate);
        Assert.Equal(1f, RetainedTabLabelShadowPolicy.NativeUnderlaySoftness);
        Assert.Equal(0.41785714f, RetainedTabLabelShadowPolicy.ExpectedNativeScaleRatioC, 7);
        Assert.Equal(0.000001f, RetainedTabLabelShadowPolicy.ScaleRatioTolerance);
    }

    [Fact]
    public void GateSixBPrivateCloneChangesOnlyUnderlayColorAndDisposesExactlyOnce()
    {
        var atlas = new object();
        var source = new RetainedMaterialProbe(atlas, 1f);
        var destroyed = new List<RetainedMaterialProbe>();
        var owned = RetainedOwnedResource<RetainedMaterialProbe>.CreatePrivateClone(
            source,
            original => original.Clone(),
            destroyed.Add);
        var privateClone = owned.Resource;

        privateClone.UnderlayAlpha = RetainedTabLabelShadowPolicy.Alpha;

        Assert.NotSame(source, privateClone);
        Assert.Same(atlas, source.Atlas);
        Assert.Same(source.Atlas, privateClone.Atlas);
        Assert.Equal(1f, source.UnderlayAlpha);
        Assert.Equal(0.57f, privateClone.UnderlayAlpha);
        Assert.Equal(source.UnderlayOffsetX, privateClone.UnderlayOffsetX);
        Assert.Equal(1f, privateClone.UnderlayOffsetX);
        Assert.Equal(source.UnderlayOffsetY, privateClone.UnderlayOffsetY);
        Assert.Equal(-1f, privateClone.UnderlayOffsetY);
        Assert.Equal(source.UnderlayDilate, privateClone.UnderlayDilate);
        Assert.Equal(-0.25f, privateClone.UnderlayDilate);
        Assert.Equal(source.UnderlaySoftness, privateClone.UnderlaySoftness);
        Assert.Equal(1f, privateClone.UnderlaySoftness);
        Assert.Equal(source.ScaleRatioC, privateClone.ScaleRatioC);
        Assert.Equal(0.41785714f, privateClone.ScaleRatioC, 7);
        Assert.True(privateClone.UnderlayEnabled);
        Assert.False(privateClone.RatioBypassEnabled);
        Assert.False(owned.IsDisposed);

        owned.Dispose();
        owned.Dispose();

        Assert.True(owned.IsDisposed);
        Assert.Same(privateClone, Assert.Single(destroyed));
        Assert.Throws<ObjectDisposedException>(() => _ = owned.Resource);
    }

    [Fact]
    public void GateEightSelectionChangesRetainOnePrivateMaterialAndCurrentComposition()
    {
        var source = new RetainedMaterialProbe(new object(), 1f);
        var destroyed = 0;
        using var owned = RetainedOwnedResource<RetainedMaterialProbe>.CreatePrivateClone(
            source,
            original => original.Clone(RetainedTabLabelShadowPolicy.Alpha),
            _ => destroyed++);
        var material = owned.Resource;
        var background = new object();
        var visualState = new RetainedTabVisualState<object>(
            background,
            StatisticsPanelTab.Overview,
            (_, _) => { });

        visualState.Apply(StatisticsPanelTab.Overview);
        visualState.Apply(StatisticsPanelTab.Runs);
        visualState.Apply(StatisticsPanelTab.Overview);

        Assert.Same(material, owned.Resource);
        Assert.False(owned.IsDisposed);
        Assert.Equal(0, destroyed);
        Assert.Equal(14, RetainedShellCompositionPolicy.RootChildCount);
        Assert.Equal(76, RetainedShellCompositionPolicy.GraphicCount);
        Assert.Equal(10, RetainedShellCompositionPolicy.ButtonCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.OverviewTabChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewTabLabelChildCount);
    }

    [Fact]
    public void GateSevenSpecificationMatchesNavigationOrderAndLocalizationExactly()
    {
        var specifications = RetainedTabStripPolicy.Specifications;
        var expected = new[]
        {
            (StatisticsPanelTab.Overview, "ui.overview", "Overview"),
            (StatisticsPanelTab.Runs, "ui.runs", "Runs"),
            (StatisticsPanelTab.Records, "ui.records", "Records"),
            (StatisticsPanelTab.Combat, "ui.combat", "Combat"),
            (StatisticsPanelTab.Equipment, "ui.equipment", "Equipment"),
            (StatisticsPanelTab.Economy, "ui.economy", "Economy"),
            (StatisticsPanelTab.Crafting, "ui.crafting", "Crafting"),
            (StatisticsPanelTab.ItemUse, "ui.item_use", "Item Use"),
            (StatisticsPanelTab.Diagnostics, "ui.diagnostics", "Diagnostics")
        };

        Assert.Equal(PanelInteractionState.NavigationOrder, specifications.Select(item => item.Tab));
        Assert.Equal(expected.Length, specifications.Count);
        Assert.Equal(expected.Length, specifications.Select(item => item.Tab).Distinct().Count());
        Assert.Equal(expected.Length, specifications.Select(item => item.BackgroundName).Distinct().Count());
        Assert.Equal(expected.Length, specifications.Select(item => item.LabelName).Distinct().Count());
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].Item1, specifications[index].Tab);
            Assert.Equal(expected[index].Item2, specifications[index].TextKey);
            Assert.Equal(expected[index].Item3, specifications[index].EnglishFallback);
            Assert.Equal(expected[index].Item3, UiText.EnglishFallbacks[specifications[index].TextKey]);
        }

        Assert.Equal(
            StatisticsPanelTab.Overview,
            Assert.Single(specifications, item => item.IsAbsolutelyAnchored).Tab);
    }

    [Fact]
    public void GateSevenRelationalLayoutUsesOnlyMeasuredWidthsPaddingAndGaps()
    {
        var suppliedWidths = new[] { 100f, 110f, 120f, 130f, 140f, 150f, 160f, 170f, 180f };
        var transform = RetainedReferenceTransformPolicy.Create(2560f, 1440f, 1f);
        var strip = RetainedTabStripPolicy.CreateCanvasLayout(transform, suppliedWidths);

        Assert.Equal(115f, strip.Tabs[0].Left);
        for (var index = 0; index < strip.Tabs.Count; index++)
        {
            var tab = strip.Tabs[index];
            Assert.Equal(suppliedWidths[index], tab.ReferencePreferredLabelWidth);
            Assert.Equal(suppliedWidths[index], tab.PreferredLabelWidth);
            Assert.Equal(suppliedWidths[index] + 60f, tab.Width);
            Assert.Equal(30f, tab.LeftPadding);
            Assert.Equal(30f, tab.RightPadding);
            Assert.Equal(25f, tab.TopPadding);
            Assert.Equal(25f, tab.BottomPadding);
            Assert.Equal(29f, tab.LabelHeight);
            Assert.Equal(251f, tab.Top);
            Assert.Equal(79f, tab.Height);
            Assert.Equal(36f, tab.FontSize);
            if (index > 0)
                Assert.Equal(strip.Tabs[index - 1].Left + strip.Tabs[index - 1].Width + 10f, tab.Left);
        }
    }

    [Fact]
    public void GateSevenMeasurementNormalizationUsesReferenceAndCanvasScales()
    {
        const float referenceWidth = 161.91875f;
        Assert.True(RetainedTabMeasurementPolicy.RequiresActiveHierarchy);
        Assert.Equal(
            RetainedReferenceTransformPolicy.BaselineWidthPixels,
            RetainedTabMeasurementPolicy.TemporaryLabelWidthPixels);
        foreach (var referenceScale in new[] { 0.5f, 0.65625f, 0.75f, 1f })
        foreach (var canvasScaleFactor in new[] { 1f, 2f, 3f })
        {
            var measuredCanvasWidth = referenceWidth * referenceScale / canvasScaleFactor;
            var temporaryCanvasWidth = RetainedTabMeasurementPolicy.TemporaryLabelWidthPixels
                                       * referenceScale
                                       / canvasScaleFactor;
            Assert.Equal(
                RetainedReferenceTransformPolicy.BaselineWidthPixels,
                RetainedTabMeasurementPolicy.NormalizeCanvasWidth(
                    temporaryCanvasWidth,
                    canvasScaleFactor,
                    referenceScale),
                3);
            Assert.Equal(
                referenceWidth,
                RetainedTabMeasurementPolicy.NormalizeCanvasWidth(
                    measuredCanvasWidth,
                    canvasScaleFactor,
                    referenceScale),
                3);
        }
    }

    [Fact]
    public void GateSevenMeasurementRejectsInvalidProductionWidths()
    {
        foreach (var invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RetainedTabMeasurementPolicy.NormalizeCanvasWidth(invalid, 1f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RetainedTabStripPolicy.CreateCanvasLayout(
                    RetainedReferenceTransformPolicy.Create(2560f, 1440f, 1f),
                    new[] { invalid, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f }));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RetainedTabMeasurementPolicy.NormalizeCanvasWidth(100f, 0f, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RetainedTabMeasurementPolicy.NormalizeCanvasWidth(100f, 1f, float.NaN));
    }

    [Fact]
    public void GateSevenANativeFeedbackTargetsExactlyTheNineTabBackgroundObjects()
    {
        var targets = RetainedTabStripPolicy.Specifications
            .Select(specification => new RetainedFeedbackTarget(specification.BackgroundName))
            .ToArray();

        foreach (var target in targets)
        {
            NativeButtonInteractionFeedbackPolicy.AttachIfMissing(
                target,
                candidate => candidate.HasFeedback,
                candidate => candidate.Attach());
        }

        Assert.Equal(9, targets.Length);
        Assert.All(targets, target =>
        {
            Assert.True(target.HasFeedback);
            Assert.Equal(1, target.AttachCount);
        });
        Assert.Equal(
            RetainedTabStripPolicy.Specifications.Select(specification => specification.BackgroundName),
            targets.Select(target => target.Name));
        Assert.DoesNotContain(targets, target =>
            RetainedTabStripPolicy.Specifications.Any(specification => specification.LabelName == target.Name));
        Assert.DoesNotContain(targets, target => target.Name == RetainedBackControlPolicy.ButtonName);
    }

    [Fact]
    public void GateSevenANativeFeedbackAttachmentIsIdempotentFailOpenAndStateless()
    {
        var retained = new RetainedFeedbackTarget("retained");
        NativeButtonInteractionFeedbackPolicy.AttachIfMissing(
            retained,
            target => target.HasFeedback,
            target => target.Attach());
        NativeButtonInteractionFeedbackPolicy.AttachIfMissing(
            retained,
            target => target.HasFeedback,
            target => target.Attach());
        Assert.Equal(1, retained.AttachCount);

        var unavailable = new RetainedFeedbackTarget("unavailable") { ThrowOnAttach = true };
        NativeButtonInteractionFeedbackPolicy.AttachIfMissing(
            unavailable,
            target => target.HasFeedback,
            target => target.Attach());
        Assert.False(unavailable.HasFeedback);
        Assert.Equal(1, unavailable.AttachCount);

        var nextShellTarget = new RetainedFeedbackTarget("next-shell");
        NativeButtonInteractionFeedbackPolicy.AttachIfMissing(
            nextShellTarget,
            target => target.HasFeedback,
            target => target.Attach());
        Assert.True(nextShellTarget.HasFeedback);
        Assert.Equal(1, nextShellTarget.AttachCount);
    }

    [Fact]
    public void GateSevenBNativeFeedbackScopeIncludesTabsBackAndMainMenuOnly()
    {
        Assert.Equal(
            "Duckov.UI.Animations.ButtonAnimation",
            NativeButtonInteractionFeedbackPolicy.NativeComponentTypeName);
        Assert.True(NativeButtonInteractionFeedbackPolicy.UsesNativePointerHandlers);
        Assert.False(NativeButtonInteractionFeedbackPolicy.SynthesizesAudio);
        Assert.False(NativeButtonInteractionFeedbackPolicy.AppliesToLabels);
        Assert.False(NativeButtonInteractionFeedbackPolicy.AppliesToBackArrow);
        Assert.True(NativeButtonInteractionFeedbackPolicy.AppliesToRetainedTabs);
        Assert.True(NativeButtonInteractionFeedbackPolicy.AppliesToBackButton);
        Assert.True(NativeButtonInteractionFeedbackPolicy.AppliesToMainMenuButton);
        Assert.False(NativeButtonInteractionFeedbackPolicy.AppliesToBasePauseMenuButton);
        Assert.True(NativeButtonInteractionFeedbackPolicy.UsesDefaultConfigurationForCreatedComponents);
    }

    [Fact]
    public void GateSevenBBackFeedbackTargetsTheButtonRootOnlyAndRemainsIdempotent()
    {
        var backRoot = new RetainedFeedbackTarget(RetainedBackControlPolicy.ButtonName);
        var arrow = new RetainedFeedbackTarget(RetainedBackControlPolicy.ArrowName);

        NativeButtonInteractionFeedbackPolicy.AttachIfMissing(
            backRoot,
            target => target.HasFeedback,
            target => target.Attach());
        NativeButtonInteractionFeedbackPolicy.AttachIfMissing(
            backRoot,
            target => target.HasFeedback,
            target => target.Attach());

        Assert.True(backRoot.HasFeedback);
        Assert.Equal(1, backRoot.AttachCount);
        Assert.False(arrow.HasFeedback);
        Assert.Equal(0, arrow.AttachCount);
    }

    [Fact]
    public void GateSevenBMainMenuPreservesOneUsableRootFeedbackAndItsSafeDependencies()
    {
        Assert.True(NativeMenuPresentationPolicy.PreservesUsableRootButtonAnimation(
            PanelAccessSurface.MainMenu,
            ButtonAnimationHierarchy,
            isPrimaryButtonRoot: true,
            isEnabled: true,
            alreadyPreserved: false));
        Assert.False(NativeMenuPresentationPolicy.PreservesUsableRootButtonAnimation(
            PanelAccessSurface.MainMenu,
            ButtonAnimationHierarchy,
            isPrimaryButtonRoot: true,
            isEnabled: true,
            alreadyPreserved: true));
        Assert.False(NativeMenuPresentationPolicy.PreservesUsableRootButtonAnimation(
            PanelAccessSurface.MainMenu,
            ButtonAnimationHierarchy,
            isPrimaryButtonRoot: false,
            isEnabled: true,
            alreadyPreserved: false));
        Assert.False(NativeMenuPresentationPolicy.PreservesUsableRootButtonAnimation(
            PanelAccessSurface.MainMenu,
            ButtonAnimationHierarchy,
            isPrimaryButtonRoot: true,
            isEnabled: false,
            alreadyPreserved: false));
        Assert.False(NativeMenuPresentationPolicy.PreservesUsableRootButtonAnimation(
            PanelAccessSurface.BasePauseMenu,
            ButtonAnimationHierarchy,
            isPrimaryButtonRoot: true,
            isEnabled: true,
            alreadyPreserved: false));

        Assert.True(NativeMenuPresentationPolicy.PreservesNativeInteractionDependency(
            PanelAccessSurface.MainMenu,
            ToggleAnimationHierarchy));
        Assert.True(NativeMenuPresentationPolicy.PreservesNativeInteractionDependency(
            PanelAccessSurface.MainMenu,
            ToggleComponentHierarchy));
        Assert.False(NativeMenuPresentationPolicy.PreservesNativeInteractionDependency(
            PanelAccessSurface.MainMenu,
            ActionBehaviourHierarchy));
        Assert.False(NativeMenuPresentationPolicy.PreservesNativeInteractionDependency(
            PanelAccessSurface.BasePauseMenu,
            ToggleAnimationHierarchy));
        Assert.False(NativeMenuPresentationPolicy.PreservesNativeInteractionDependency(
            PanelAccessSurface.BasePauseMenu,
            ToggleComponentHierarchy));
    }

    [Fact]
    public void GateSevenBMainMenuAddsFeedbackOnlyWhenNoUsableInstanceSurvives()
    {
        var preserved = new RetainedFeedbackTarget("preserved");
        preserved.Attach();
        NativeButtonInteractionFeedbackPolicy.AttachIfMissing(
            preserved,
            target => target.HasFeedback,
            target => target.Attach());
        Assert.Equal(1, preserved.AttachCount);

        var disabledScheduledForRemoval = new RetainedFeedbackTarget("disabled");
        NativeButtonInteractionFeedbackPolicy.AttachIfMissing(
            disabledScheduledForRemoval,
            target => target.HasFeedback,
            target => target.Attach());
        Assert.True(disabledScheduledForRemoval.HasFeedback);
        Assert.Equal(1, disabledScheduledForRemoval.AttachCount);
    }

    [Fact]
    public void GateSevenBMainMenuReplacementCallbackRemainsSingleFire()
    {
        var activationCount = 0;
        var activation = new NativeMenuButtonActivation(() => activationCount++);

        activation.Invoke();

        Assert.True(NativeMenuButtonActivation.ReplacesInheritedCallbacks);
        Assert.Equal(1, NativeMenuButtonActivation.RegisteredUdsCallbackCount);
        Assert.Equal(1, activationCount);
    }

    [Fact]
    public void GateEightOverviewLeftPanelUsesExactReferenceGeometryStyleAndPadding()
    {
        var layout = CreateRetainedVisualLayout(2560f, 1440f).OverviewLeftPanel;

        Assert.Equal("OverviewContentView", RetainedOverviewLeftPanelPolicy.ViewName);
        Assert.Equal("OverviewLeftPanelBackground", RetainedOverviewLeftPanelPolicy.BackgroundName);
        Assert.Equal(StatisticsPanelTab.Overview, RetainedOverviewLeftPanelPolicy.OwnerTab);
        Assert.Equal(85f, RetainedOverviewLeftPanelPolicy.LeftPixels);
        Assert.Equal(370f, RetainedOverviewLeftPanelPolicy.TopPixels);
        Assert.Equal(1175f, RetainedOverviewLeftPanelPolicy.WidthPixels);
        Assert.Equal(1040f, RetainedOverviewLeftPanelPolicy.HeightPixels);
        Assert.Equal(1260f, RetainedOverviewLeftPanelPolicy.RightExclusivePixels);
        Assert.Equal(1410f, RetainedOverviewLeftPanelPolicy.BottomExclusivePixels);
        Assert.Equal(40f, RetainedOverviewLeftPanelPolicy.HeaderGapPixels);
        Assert.Equal(30f, RetainedOverviewLeftPanelPolicy.ScreenBottomMarginPixels);
        Assert.Equal(20f, RetainedOverviewLeftPanelPolicy.CornerRadiusPixels);
        Assert.Equal(30f, RetainedOverviewLeftPanelPolicy.ContentPaddingPixels);
        Assert.Equal(0f, RetainedOverviewLeftPanelPolicy.Red);
        Assert.Equal(0f, RetainedOverviewLeftPanelPolicy.Green);
        Assert.Equal(0f, RetainedOverviewLeftPanelPolicy.Blue);
        Assert.Equal(0.50f, RetainedOverviewLeftPanelPolicy.LayerAlpha);
        Assert.False(RetainedOverviewLeftPanelPolicy.BlocksRaycasts);
        Assert.Equal(
            RetainedHeaderPolicy.BottomExclusivePixels + RetainedOverviewLeftPanelPolicy.HeaderGapPixels,
            RetainedOverviewLeftPanelPolicy.TopPixels);
        Assert.Equal(
            RetainedReferenceTransformPolicy.BaselineHeightPixels
            - RetainedOverviewLeftPanelPolicy.ScreenBottomMarginPixels,
            RetainedOverviewLeftPanelPolicy.BottomExclusivePixels);

        Assert.Equal(85f, layout.Left);
        Assert.Equal(370f, layout.Top);
        Assert.Equal(1175f, layout.Width);
        Assert.Equal(1040f, layout.Height);
        Assert.Equal(20f, layout.CornerRadius);
        Assert.Equal(115f, layout.ContentLeft);
        Assert.Equal(400f, layout.ContentTop);
        Assert.Equal(1115f, layout.ContentWidth);
        Assert.Equal(980f, layout.ContentHeight);
    }

    [Theory]
    [InlineData(1280f, 720f, 42.5f, 185f, 587.5f, 520f, 10f, 15f)]
    [InlineData(1680f, 1050f, 55.78125f, 295.3125f, 771.09375f, 682.5f, 13.125f, 19.6875f)]
    [InlineData(1920f, 1080f, 63.75f, 277.5f, 881.25f, 780f, 15f, 22.5f)]
    [InlineData(1920f, 1200f, 63.75f, 337.5f, 881.25f, 780f, 15f, 22.5f)]
    [InlineData(2560f, 1440f, 85f, 370f, 1175f, 1040f, 20f, 30f)]
    public void GateEightOverviewLeftPanelUsesTheSharedReferenceTransform(
        float viewportWidth,
        float viewportHeight,
        float expectedLeft,
        float expectedTop,
        float expectedWidth,
        float expectedHeight,
        float expectedRadius,
        float expectedPadding)
    {
        var layout = CreateRetainedVisualLayout(viewportWidth, viewportHeight);
        var panel = layout.OverviewLeftPanel;

        Assert.Same(layout.ReferenceTransform, panel.ReferenceTransform);
        Assert.Equal(expectedLeft, panel.Left, 5);
        Assert.Equal(expectedTop, panel.Top, 5);
        Assert.Equal(expectedWidth, panel.Width, 5);
        Assert.Equal(expectedHeight, panel.Height, 5);
        Assert.Equal(expectedRadius, panel.CornerRadius, 5);
        Assert.Equal(expectedPadding, panel.ContentLeft - panel.Left, 5);
        Assert.Equal(expectedPadding, panel.ContentTop - panel.Top, 5);
        Assert.Equal(expectedPadding * 2f, panel.Width - panel.ContentWidth, 5);
        Assert.Equal(expectedPadding * 2f, panel.Height - panel.ContentHeight, 5);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(3f)]
    public void GateNineOverviewPanelsPreservePhysicalGeometryAcrossCanvasScaleFactors(
        float canvasScaleFactor)
    {
        var referenceTransform = RetainedReferenceTransformPolicy.Create(
            2560f,
            1440f,
            canvasScaleFactor);
        var left = RetainedOverviewLeftPanelPolicy.CreateCanvasLayout(referenceTransform);
        var right = RetainedOverviewRightPanelPolicy.CreateCanvasLayout(referenceTransform);

        Assert.Equal(85f, left.Left * canvasScaleFactor, 3);
        Assert.Equal(1300f, right.Left * canvasScaleFactor, 3);
        Assert.Equal(370f, left.Top * canvasScaleFactor, 3);
        Assert.Equal(370f, right.Top * canvasScaleFactor, 3);
        Assert.Equal(1175f, left.Width * canvasScaleFactor, 3);
        Assert.Equal(1175f, right.Width * canvasScaleFactor, 3);
        Assert.Equal(1040f, left.Height * canvasScaleFactor, 3);
        Assert.Equal(1040f, right.Height * canvasScaleFactor, 3);
        Assert.Equal(20f, left.CornerRadius * canvasScaleFactor, 3);
        Assert.Equal(20f, right.CornerRadius * canvasScaleFactor, 3);
        Assert.Equal(30f, (left.ContentLeft - left.Left) * canvasScaleFactor, 3);
        Assert.Equal(30f, (right.ContentLeft - right.Left) * canvasScaleFactor, 3);
        Assert.Equal(30f, (left.ContentTop - left.Top) * canvasScaleFactor, 3);
        Assert.Equal(30f, (right.ContentTop - right.Top) * canvasScaleFactor, 3);
    }

    [Fact]
    public void GateNineOverviewContentVisibilityTracksBothPanelsOnlyWithOverviewSelection()
    {
        var view = new object();
        var assignments = new List<(object Target, bool Visible)>();
        var visibility = new RetainedTabViewVisibility<object>(
            view,
            StatisticsPanelTab.Overview,
            (target, visible) => assignments.Add((target, visible)));

        foreach (var tab in PanelInteractionState.NavigationOrder) visibility.Apply(tab);
        visibility.Apply(StatisticsPanelTab.Overview);

        Assert.Same(view, visibility.Target);
        Assert.Equal(StatisticsPanelTab.Overview, visibility.OwnerTab);
        Assert.Equal(PanelInteractionState.NavigationOrder.Count + 1, assignments.Count);
        for (var index = 0; index < PanelInteractionState.NavigationOrder.Count; index++)
        {
            Assert.Equal(
                PanelInteractionState.NavigationOrder[index] == StatisticsPanelTab.Overview,
                assignments[index].Visible);
        }
        Assert.True(assignments[^1].Visible);
        Assert.All(assignments, assignment => Assert.Same(view, assignment.Target));
    }

    [Fact]
    public void GateTwelveCompositionRetainsTheAcceptedFirstRowAndAddsTenSiblingRows()
    {
        Assert.Equal(14, RetainedShellCompositionPolicy.RootChildCount);
        Assert.Equal(2, RetainedShellCompositionPolicy.OverviewContentViewChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.OverviewLeftPanelChildCount);
        Assert.Equal(12, RetainedShellCompositionPolicy.OverviewLeftPanelContentChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewProfileSummaryHeadingChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.OverviewFirstStatisticsRowChildCount);
        Assert.Equal(2, RetainedShellCompositionPolicy.OverviewFirstStatisticsRowContentChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewFirstStatisticsRowLabelChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewFirstStatisticsRowValueChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.OverviewRightPanelChildCount);
        Assert.Equal(7, RetainedShellCompositionPolicy.OverviewRightPanelContentChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewHighlightsHeadingChildCount);
        Assert.Equal(76, RetainedShellCompositionPolicy.GraphicCount);
        Assert.Equal(10, RetainedShellCompositionPolicy.ButtonCount);
        Assert.Equal(9, RetainedShellCompositionPolicy.OnlyOneEdgeModifierCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.RectMaskCount);
    }

    [Fact]
    public void GateNineOverviewRightPanelUsesSharedStyleAndExactReferenceGeometry()
    {
        var layout = CreateRetainedVisualLayout(2560f, 1440f);
        var left = layout.OverviewLeftPanel;
        var right = layout.OverviewRightPanel;

        Assert.Equal("OverviewRightPanelBackground", RetainedOverviewRightPanelPolicy.BackgroundName);
        Assert.Equal(StatisticsPanelTab.Overview, RetainedOverviewPanelStylePolicy.OwnerTab);
        Assert.Equal(1300f, RetainedOverviewRightPanelPolicy.LeftPixels);
        Assert.Equal(2475f, RetainedOverviewRightPanelPolicy.RightExclusivePixels);
        Assert.Equal(40f, RetainedOverviewRightPanelPolicy.PanelGapPixels);
        Assert.Equal(85f, RetainedOverviewRightPanelPolicy.ScreenRightMarginPixels);
        Assert.Equal(370f, RetainedOverviewPanelStylePolicy.TopPixels);
        Assert.Equal(1175f, RetainedOverviewPanelStylePolicy.WidthPixels);
        Assert.Equal(1040f, RetainedOverviewPanelStylePolicy.HeightPixels);
        Assert.Equal(1410f, RetainedOverviewPanelStylePolicy.BottomExclusivePixels);
        Assert.Equal(40f, RetainedOverviewPanelStylePolicy.HeaderGapPixels);
        Assert.Equal(30f, RetainedOverviewPanelStylePolicy.ScreenBottomMarginPixels);
        Assert.Equal(20f, RetainedOverviewPanelStylePolicy.CornerRadiusPixels);
        Assert.Equal(30f, RetainedOverviewPanelStylePolicy.ContentPaddingPixels);
        Assert.Equal(0f, RetainedOverviewPanelStylePolicy.Red);
        Assert.Equal(0f, RetainedOverviewPanelStylePolicy.Green);
        Assert.Equal(0f, RetainedOverviewPanelStylePolicy.Blue);
        Assert.Equal(0.50f, RetainedOverviewPanelStylePolicy.LayerAlpha);
        Assert.False(RetainedOverviewPanelStylePolicy.BlocksRaycasts);

        Assert.Equal(1300f, right.Left);
        Assert.Equal(370f, right.Top);
        Assert.Equal(1175f, right.Width);
        Assert.Equal(1040f, right.Height);
        Assert.Equal(20f, right.CornerRadius);
        Assert.Equal(1330f, right.ContentLeft);
        Assert.Equal(400f, right.ContentTop);
        Assert.Equal(1115f, right.ContentWidth);
        Assert.Equal(980f, right.ContentHeight);
        Assert.Equal(RetainedOverviewRightPanelPolicy.PanelGapPixels, right.Left - left.Left - left.Width);
        Assert.Equal(
            RetainedOverviewRightPanelPolicy.ScreenRightMarginPixels,
            RetainedReferenceTransformPolicy.BaselineWidthPixels - right.Left - right.Width);
        Assert.Equal(left.Top, right.Top);
        Assert.Equal(left.Width, right.Width);
        Assert.Equal(left.Height, right.Height);
        Assert.Equal(left.CornerRadius, right.CornerRadius);
        Assert.Equal(left.ContentWidth, right.ContentWidth);
        Assert.Equal(left.ContentHeight, right.ContentHeight);
    }

    [Theory]
    [InlineData(1280f, 720f, 650f, 185f, 587.5f, 520f, 10f, 15f, 20f, 42.5f)]
    [InlineData(1680f, 1050f, 853.125f, 295.3125f, 771.09375f, 682.5f, 13.125f, 19.6875f, 26.25f, 55.78125f)]
    [InlineData(1920f, 1080f, 975f, 277.5f, 881.25f, 780f, 15f, 22.5f, 30f, 63.75f)]
    [InlineData(1920f, 1200f, 975f, 337.5f, 881.25f, 780f, 15f, 22.5f, 30f, 63.75f)]
    [InlineData(2560f, 1440f, 1300f, 370f, 1175f, 1040f, 20f, 30f, 40f, 85f)]
    public void GateNineOverviewPanelsUseOneSharedReferenceTransform(
        float viewportWidth,
        float viewportHeight,
        float expectedRightLeft,
        float expectedTop,
        float expectedWidth,
        float expectedHeight,
        float expectedRadius,
        float expectedPadding,
        float expectedGap,
        float expectedOuterMargin)
    {
        var layout = CreateRetainedVisualLayout(viewportWidth, viewportHeight);
        var left = layout.OverviewLeftPanel;
        var right = layout.OverviewRightPanel;

        Assert.Same(layout.ReferenceTransform, left.ReferenceTransform);
        Assert.Same(layout.ReferenceTransform, right.ReferenceTransform);
        Assert.Equal(expectedRightLeft, right.Left, 5);
        Assert.Equal(expectedTop, right.Top, 5);
        Assert.Equal(expectedWidth, right.Width, 5);
        Assert.Equal(expectedHeight, right.Height, 5);
        Assert.Equal(expectedRadius, right.CornerRadius, 5);
        Assert.Equal(expectedPadding, right.ContentLeft - right.Left, 5);
        Assert.Equal(expectedPadding, right.ContentTop - right.Top, 5);
        Assert.Equal(expectedGap, right.Left - left.Left - left.Width, 5);
        Assert.Equal(
            expectedOuterMargin,
            viewportWidth / layout.ReferenceTransform.CanvasScaleFactor - right.Left - right.Width,
            5);
    }

    [Fact]
    public void GateTenOverviewProfileSummaryHeadingUsesPanelPaddingAndNativeTypography()
    {
        var layout = CreateRetainedVisualLayout(2560f, 1440f);
        var panel = layout.OverviewLeftPanel;
        var heading = layout.OverviewProfileSummaryHeading;

        Assert.Equal("OverviewLeftPanelContent", RetainedOverviewLeftPanelPolicy.ContentName);
        Assert.Equal("OverviewProfileSummaryHeading", RetainedOverviewProfileSummaryHeadingPolicy.Name);
        Assert.Equal(
            RetainedOverviewLeftPanelPolicy.ContentName,
            RetainedOverviewProfileSummaryHeadingPolicy.ParentName);
        Assert.Equal("ui.profile_summary", RetainedOverviewProfileSummaryHeadingPolicy.TextKey);
        Assert.Equal("Profile Summary", RetainedOverviewProfileSummaryHeadingPolicy.EnglishFallback);
        Assert.Equal(
            "Profile Summary",
            UiText.EnglishFallbacks[RetainedOverviewProfileSummaryHeadingPolicy.TextKey]);
        Assert.Equal(
            RetainedHeaderTitlePolicy.FontAssetName,
            RetainedOverviewProfileSummaryHeadingPolicy.FontAssetName);
        Assert.Equal(
            RetainedHeaderTitlePolicy.MaterialName,
            RetainedOverviewProfileSummaryHeadingPolicy.SourceMaterialName);
        Assert.Equal(
            RetainedTabLabelShadowPolicy.OwnedMaterialName,
            RetainedOverviewProfileSummaryHeadingPolicy.MaterialName);
        Assert.Equal(46.3f, RetainedOverviewProfileSummaryHeadingPolicy.ReferenceFontSize);
        Assert.Equal(60f, RetainedOverviewProfileSummaryHeadingPolicy.HeightPixels);
        Assert.Equal(1f, RetainedOverviewProfileSummaryHeadingPolicy.Red);
        Assert.Equal(1f, RetainedOverviewProfileSummaryHeadingPolicy.Green);
        Assert.Equal(1f, RetainedOverviewProfileSummaryHeadingPolicy.Blue);
        Assert.Equal(1f, RetainedOverviewProfileSummaryHeadingPolicy.Alpha);
        Assert.False(RetainedOverviewProfileSummaryHeadingPolicy.BlocksRaycasts);
        Assert.False(RetainedOverviewProfileSummaryHeadingPolicy.WordWrapping);
        Assert.False(RetainedOverviewProfileSummaryHeadingPolicy.AutoSizing);
        Assert.True(RetainedOverviewProfileSummaryHeadingPolicy.UsesTopLeftAlignment);
        Assert.True(RetainedOverviewProfileSummaryHeadingPolicy.UsesOwnedTabLabelMaterial);
        Assert.True(RetainedOverviewProfileSummaryHeadingPolicy.UsesZeroTextMargin);
        Assert.Equal(0f, RetainedOverviewProfileSummaryHeadingPolicy.AdditionalPaddingPixels);
        Assert.True(RetainedOverviewProfileSummaryHeadingPolicy.UsesFixedOpticalOffset);
        Assert.False(RetainedOverviewProfileSummaryHeadingPolicy.UsesHorizontalScaleCompensation);
        Assert.Equal(-5f, RetainedOverviewProfileSummaryHeadingPolicy.ReferenceOpticalOffsetX);
        Assert.Equal(19f, RetainedOverviewProfileSummaryHeadingPolicy.ReferenceOpticalOffsetY);

        Assert.Same(layout.ReferenceTransform, heading.ReferenceTransform);
        Assert.Equal(115f, heading.Left);
        Assert.Equal(400f, heading.Top);
        Assert.Equal(1115f, heading.Width);
        Assert.Equal(60f, heading.Height);
        Assert.Equal(46.3f, heading.FontSize);
        Assert.Equal(-5f, heading.OpticalOffsetX);
        Assert.Equal(19f, heading.OpticalOffsetY);
        Assert.Equal(panel.ContentLeft, heading.Left);
        Assert.Equal(panel.ContentTop, heading.Top);
        Assert.Equal(panel.ContentWidth, heading.Width);
        Assert.Equal(30f, panel.ContentLeft - panel.Left);
        Assert.Equal(30f, panel.ContentTop - panel.Top);
        Assert.Equal(30f, panel.Left + panel.Width - panel.ContentLeft - panel.ContentWidth);
        Assert.Equal(30f, panel.Top + panel.Height - panel.ContentTop - panel.ContentHeight);
        Assert.Equal(0f, heading.Left - panel.ContentLeft);
        Assert.Equal(0f, heading.Top - panel.ContentTop);
    }

    [Theory]
    [InlineData(1280f, 720f, 57.5f, 200f, 557.5f, 30f, 23.15f, -2.5f, 9.5f)]
    [InlineData(1680f, 1050f, 75.46875f, 315f, 731.71875f, 39.375f, 30.384375f, -3.28125f, 12.46875f)]
    [InlineData(1920f, 1080f, 86.25f, 300f, 836.25f, 45f, 34.725f, -3.75f, 14.25f)]
    [InlineData(1920f, 1200f, 86.25f, 360f, 836.25f, 45f, 34.725f, -3.75f, 14.25f)]
    [InlineData(2560f, 1440f, 115f, 400f, 1115f, 60f, 46.3f, -5f, 19f)]
    public void GateTenOverviewProfileSummaryHeadingUsesTheSharedReferenceTransform(
        float viewportWidth,
        float viewportHeight,
        float expectedLeft,
        float expectedTop,
        float expectedWidth,
        float expectedHeight,
        float expectedFontSize,
        float expectedOpticalOffsetX,
        float expectedOpticalOffsetY)
    {
        var layout = CreateRetainedVisualLayout(viewportWidth, viewportHeight);
        var panel = layout.OverviewLeftPanel;
        var heading = layout.OverviewProfileSummaryHeading;

        Assert.Same(layout.ReferenceTransform, heading.ReferenceTransform);
        Assert.Equal(expectedLeft, heading.Left, 5);
        Assert.Equal(expectedTop, heading.Top, 5);
        Assert.Equal(expectedWidth, heading.Width, 5);
        Assert.Equal(expectedHeight, heading.Height, 5);
        Assert.Equal(expectedFontSize, heading.FontSize, 5);
        Assert.Equal(expectedOpticalOffsetX, heading.OpticalOffsetX, 5);
        Assert.Equal(expectedOpticalOffsetY, heading.OpticalOffsetY, 5);
        Assert.Equal(panel.ContentLeft, heading.Left, 5);
        Assert.Equal(panel.ContentTop, heading.Top, 5);
        Assert.Equal(panel.ContentWidth, heading.Width, 5);
    }

    [Fact]
    public void GateElevenOneFirstStatisticsRowUsesExactReferenceGeometryStyleAndEmptyContentHost()
    {
        var layout = CreateRetainedVisualLayout(2560f, 1440f);
        var panel = layout.OverviewLeftPanel;
        var row = layout.OverviewFirstStatisticsRow;

        Assert.Equal("OverviewFirstStatisticsRowBackground", RetainedOverviewFirstStatisticsRowPolicy.BackgroundName);
        Assert.Equal("OverviewFirstStatisticsRowContent", RetainedOverviewFirstStatisticsRowPolicy.ContentName);
        Assert.Equal(RetainedOverviewLeftPanelPolicy.ContentName, RetainedOverviewFirstStatisticsRowPolicy.ParentName);
        Assert.Equal(StatisticsPanelTab.Overview, RetainedOverviewFirstStatisticsRowPolicy.OwnerTab);
        Assert.Equal(115f, RetainedOverviewFirstStatisticsRowPolicy.LeftPixels);
        Assert.Equal(456f, RetainedOverviewFirstStatisticsRowPolicy.TopPixels);
        Assert.Equal(1230f, RetainedOverviewFirstStatisticsRowPolicy.RightExclusivePixels);
        Assert.Equal(522f, RetainedOverviewFirstStatisticsRowPolicy.BottomExclusivePixels);
        Assert.Equal(1115f, RetainedOverviewFirstStatisticsRowPolicy.WidthPixels);
        Assert.Equal(66f, RetainedOverviewFirstStatisticsRowPolicy.HeightPixels);
        Assert.Equal(20f, RetainedOverviewFirstStatisticsRowPolicy.HeadingToCardMarginPixels);
        Assert.Equal(56f, RetainedOverviewFirstStatisticsRowPolicy.ContentTopOffsetPixels);
        Assert.Equal(20f, RetainedOverviewFirstStatisticsRowPolicy.ContentPaddingPixels);
        Assert.Equal(10f, RetainedOverviewFirstStatisticsRowPolicy.CornerRadiusPixels);
        Assert.Equal(0f, RetainedOverviewFirstStatisticsRowPolicy.Red);
        Assert.Equal(0f, RetainedOverviewFirstStatisticsRowPolicy.Green);
        Assert.Equal(0f, RetainedOverviewFirstStatisticsRowPolicy.Blue);
        Assert.Equal(0.50f, RetainedOverviewFirstStatisticsRowPolicy.LayerAlpha);
        Assert.False(RetainedOverviewFirstStatisticsRowPolicy.BlocksRaycasts);

        Assert.Same(layout.ReferenceTransform, row.ReferenceTransform);
        Assert.Equal(115f, row.Left);
        Assert.Equal(456f, row.Top);
        Assert.Equal(1115f, row.Width);
        Assert.Equal(66f, row.Height);
        Assert.Equal(10f, row.CornerRadius);
        Assert.Equal(135f, row.ContentLeft);
        Assert.Equal(476f, row.ContentTop);
        Assert.Equal(1075f, row.ContentWidth);
        Assert.Equal(26f, row.ContentHeight);
        Assert.Equal(panel.ContentLeft, row.Left);
        Assert.Equal(panel.ContentWidth, row.Width);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowPolicy.ContentTopOffsetPixels,
            row.Top - panel.ContentTop);
        Assert.Equal(20f, row.ContentLeft - row.Left);
        Assert.Equal(20f, row.ContentTop - row.Top);
        Assert.Equal(20f, row.Left + row.Width - row.ContentLeft - row.ContentWidth);
        Assert.Equal(20f, row.Top + row.Height - row.ContentTop - row.ContentHeight);
    }

    [Theory]
    [InlineData(1280f, 720f, 57.5f, 228f, 557.5f, 33f, 5f, 10f)]
    [InlineData(1680f, 1050f, 75.46875f, 351.75f, 731.71875f, 43.3125f, 6.5625f, 13.125f)]
    [InlineData(1920f, 1080f, 86.25f, 342f, 836.25f, 49.5f, 7.5f, 15f)]
    [InlineData(1920f, 1200f, 86.25f, 402f, 836.25f, 49.5f, 7.5f, 15f)]
    [InlineData(2560f, 1440f, 115f, 456f, 1115f, 66f, 10f, 20f)]
    public void GateElevenOneFirstStatisticsRowUsesTheSharedReferenceTransform(
        float viewportWidth,
        float viewportHeight,
        float expectedLeft,
        float expectedTop,
        float expectedWidth,
        float expectedHeight,
        float expectedRadius,
        float expectedPadding)
    {
        var layout = CreateRetainedVisualLayout(viewportWidth, viewportHeight);
        var panel = layout.OverviewLeftPanel;
        var row = layout.OverviewFirstStatisticsRow;

        Assert.Same(layout.ReferenceTransform, row.ReferenceTransform);
        Assert.Equal(expectedLeft, row.Left, 5);
        Assert.Equal(expectedTop, row.Top, 5);
        Assert.Equal(expectedWidth, row.Width, 5);
        Assert.Equal(expectedHeight, row.Height, 5);
        Assert.Equal(expectedRadius, row.CornerRadius, 5);
        Assert.Equal(expectedPadding, row.ContentLeft - row.Left, 5);
        Assert.Equal(expectedPadding, row.ContentTop - row.Top, 5);
        Assert.Equal(expectedPadding, row.Left + row.Width - row.ContentLeft - row.ContentWidth, 5);
        Assert.Equal(expectedPadding, row.Top + row.Height - row.ContentTop - row.ContentHeight, 5);
        Assert.Equal(panel.ContentLeft, row.Left, 5);
        Assert.Equal(panel.ContentWidth, row.Width, 5);
    }

    [Fact]
    public void GateElevenTwoFirstStatisticsRowEntryUsesExactColumnsAndNativeTypography()
    {
        var layout = CreateRetainedVisualLayout(2560f, 1440f);
        var row = layout.OverviewFirstStatisticsRow;
        var entry = layout.OverviewFirstStatisticsRowEntry;

        Assert.Equal("OverviewFirstStatisticsRowLabel", RetainedOverviewFirstStatisticsRowEntryPolicy.LabelName);
        Assert.Equal("OverviewFirstStatisticsRowValue", RetainedOverviewFirstStatisticsRowEntryPolicy.ValueName);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowPolicy.ContentName,
            RetainedOverviewFirstStatisticsRowEntryPolicy.ParentName);
        Assert.Equal(StatisticsPanelTab.Overview, RetainedOverviewFirstStatisticsRowEntryPolicy.OwnerTab);
        Assert.Equal("ui.overview_total_runs", RetainedOverviewFirstStatisticsRowEntryPolicy.LabelTextKey);
        Assert.Equal("Total runs", RetainedOverviewFirstStatisticsRowEntryPolicy.LabelEnglishFallback);
        Assert.Equal(
            RetainedHeaderTitlePolicy.FontAssetName,
            RetainedOverviewFirstStatisticsRowEntryPolicy.FontAssetName);
        Assert.Equal(
            RetainedTabLabelShadowPolicy.OwnedMaterialName,
            RetainedOverviewFirstStatisticsRowEntryPolicy.MaterialName);
        Assert.Equal(29.8f, RetainedOverviewFirstStatisticsRowEntryPolicy.ReferenceFontSize);
        Assert.Equal(135f, RetainedOverviewFirstStatisticsRowEntryPolicy.LabelColumnLeftPixels);
        Assert.Equal(465f, RetainedOverviewFirstStatisticsRowEntryPolicy.LabelColumnWidthPixels);
        Assert.Equal(600f, RetainedOverviewFirstStatisticsRowEntryPolicy.ValueColumnLeftPixels);
        Assert.Equal(1210f, RetainedOverviewFirstStatisticsRowEntryPolicy.ValueColumnRightExclusivePixels);
        Assert.Equal(1f, RetainedOverviewFirstStatisticsRowEntryPolicy.Red);
        Assert.Equal(1f, RetainedOverviewFirstStatisticsRowEntryPolicy.Green);
        Assert.Equal(1f, RetainedOverviewFirstStatisticsRowEntryPolicy.Blue);
        Assert.Equal(1f, RetainedOverviewFirstStatisticsRowEntryPolicy.Alpha);
        Assert.Equal(0f, RetainedOverviewFirstStatisticsRowEntryPolicy.CharacterSpacing);
        Assert.Equal(0f, RetainedOverviewFirstStatisticsRowEntryPolicy.WordSpacing);
        Assert.Equal(0f, RetainedOverviewFirstStatisticsRowEntryPolicy.LineSpacing);
        Assert.Equal(0f, RetainedOverviewFirstStatisticsRowEntryPolicy.ParagraphSpacing);
        Assert.False(RetainedOverviewFirstStatisticsRowEntryPolicy.BlocksRaycasts);
        Assert.False(RetainedOverviewFirstStatisticsRowEntryPolicy.WordWrapping);
        Assert.False(RetainedOverviewFirstStatisticsRowEntryPolicy.AutoSizing);
        Assert.True(RetainedOverviewFirstStatisticsRowEntryPolicy.UsesVisibleOverflow);
        Assert.True(RetainedOverviewFirstStatisticsRowEntryPolicy.UsesLeftAlignment);
        Assert.True(RetainedOverviewFirstStatisticsRowEntryPolicy.UsesVerticalCentering);
        Assert.True(RetainedOverviewFirstStatisticsRowEntryPolicy.UsesZeroTextMargins);
        Assert.True(RetainedOverviewFirstStatisticsRowEntryPolicy.UsesNormalStyle);
        Assert.True(RetainedOverviewFirstStatisticsRowEntryPolicy.UsesRegularWeight);
        Assert.True(RetainedOverviewFirstStatisticsRowEntryPolicy.UsesOwnedSubtleShadowMaterial);
        Assert.True(RetainedOverviewFirstStatisticsRowEntryPolicy.UsesProjectionRunsTotalRuns);

        Assert.Same(layout.ReferenceTransform, entry.ReferenceTransform);
        Assert.Equal(135f, entry.LabelLeft);
        Assert.Equal(476f, entry.LabelTop);
        Assert.Equal(465f, entry.LabelWidth);
        Assert.Equal(26f, entry.LabelHeight);
        Assert.Equal(600f, entry.ValueLeft);
        Assert.Equal(476f, entry.ValueTop);
        Assert.Equal(610f, entry.ValueWidth);
        Assert.Equal(26f, entry.ValueHeight);
        Assert.Equal(29.8f, entry.FontSize);
        Assert.Equal(row.ContentLeft, entry.LabelLeft);
        Assert.Equal(row.ContentTop, entry.LabelTop);
        Assert.Equal(row.ContentHeight, entry.LabelHeight);
        Assert.Equal(entry.LabelLeft + entry.LabelWidth, entry.ValueLeft);
        Assert.Equal(row.ContentLeft + row.ContentWidth, entry.ValueLeft + entry.ValueWidth);
        Assert.Equal(row.ContentTop, entry.ValueTop);
        Assert.Equal(row.ContentHeight, entry.ValueHeight);
    }

    [Theory]
    [InlineData(1280f, 720f, 67.5f, 238f, 232.5f, 300f, 305f, 13f, 14.9f)]
    [InlineData(1680f, 1050f, 88.59375f, 364.875f, 305.15625f, 393.75f, 400.3125f, 17.0625f, 19.55625f)]
    [InlineData(1920f, 1080f, 101.25f, 357f, 348.75f, 450f, 457.5f, 19.5f, 22.35f)]
    [InlineData(1920f, 1200f, 101.25f, 417f, 348.75f, 450f, 457.5f, 19.5f, 22.35f)]
    [InlineData(2560f, 1440f, 135f, 476f, 465f, 600f, 610f, 26f, 29.8f)]
    public void GateElevenTwoFirstStatisticsRowEntryUsesTheSharedReferenceTransform(
        float viewportWidth,
        float viewportHeight,
        float expectedLabelLeft,
        float expectedTop,
        float expectedLabelWidth,
        float expectedValueLeft,
        float expectedValueWidth,
        float expectedHeight,
        float expectedFontSize)
    {
        var layout = CreateRetainedVisualLayout(viewportWidth, viewportHeight);
        var row = layout.OverviewFirstStatisticsRow;
        var entry = layout.OverviewFirstStatisticsRowEntry;

        Assert.Same(layout.ReferenceTransform, entry.ReferenceTransform);
        Assert.Equal(expectedLabelLeft, entry.LabelLeft, 5);
        Assert.Equal(expectedTop, entry.LabelTop, 5);
        Assert.Equal(expectedLabelWidth, entry.LabelWidth, 5);
        Assert.Equal(expectedValueLeft, entry.ValueLeft, 5);
        Assert.Equal(expectedTop, entry.ValueTop, 5);
        Assert.Equal(expectedValueWidth, entry.ValueWidth, 5);
        Assert.Equal(expectedHeight, entry.LabelHeight, 5);
        Assert.Equal(expectedHeight, entry.ValueHeight, 5);
        Assert.Equal(expectedFontSize, entry.FontSize, 5);
        Assert.Equal(row.ContentLeft, entry.LabelLeft, 5);
        Assert.Equal(row.ContentTop, entry.LabelTop, 5);
        Assert.Equal(row.ContentWidth, entry.LabelWidth + entry.ValueWidth, 5);
    }

    [Fact]
    public void GateElevenTwoUsesDedicatedLocalizationAndTheProjectedTotalRunCount()
    {
        const string localized = "Gesamte Läufe";
        var projection = new StatisticsPanelProjection
        {
            Runs = new RunStatisticsViewModel { TotalRuns = 2 }
        };

        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.LabelEnglishFallback,
            UiText.EnglishFallbacks[RetainedOverviewFirstStatisticsRowEntryPolicy.LabelTextKey]);
        Assert.Equal("Runs", UiText.EnglishFallbacks["ui.total_runs"]);
        Assert.Equal(
            localized,
            UiText.Resolve(
                RetainedOverviewFirstStatisticsRowEntryPolicy.LabelTextKey,
                key => key == RetainedOverviewFirstStatisticsRowEntryPolicy.LabelTextKey ? localized : null));
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.LabelEnglishFallback,
            UiText.Resolve(RetainedOverviewFirstStatisticsRowEntryPolicy.LabelTextKey, _ => null));
        Assert.Equal("2", RetainedOverviewFirstStatisticsRowEntryPolicy.FormatProjectedValue(projection));

        projection.Runs.TotalRuns = 37;
        Assert.Equal("37", RetainedOverviewFirstStatisticsRowEntryPolicy.FormatProjectedValue(projection));
        Assert.Throws<ArgumentNullException>(
            () => RetainedOverviewFirstStatisticsRowEntryPolicy.FormatProjectedValue(null!));
    }

    [Fact]
    public void GateElevenTwoFirstStatisticsRowEntryInheritsOverviewOnlyVisibility()
    {
        var overviewView = new object();
        var visibilityStates = new List<bool>();
        var visibility = new RetainedTabViewVisibility<object>(
            overviewView,
            RetainedOverviewFirstStatisticsRowEntryPolicy.OwnerTab,
            (_, visible) => visibilityStates.Add(visible));

        foreach (var tab in PanelInteractionState.NavigationOrder) visibility.Apply(tab);
        visibility.Apply(StatisticsPanelTab.Overview);

        Assert.Equal(
            RetainedOverviewFirstStatisticsRowPolicy.ContentName,
            RetainedOverviewFirstStatisticsRowEntryPolicy.ParentName);
        Assert.Equal(StatisticsPanelTab.Overview, visibility.OwnerTab);
        Assert.True(visibilityStates[0]);
        Assert.All(
            visibilityStates.Skip(1).Take(PanelInteractionState.NavigationOrder.Count - 1),
            visible => Assert.False(visible));
        Assert.True(visibilityStates[^1]);
    }

    [Fact]
    public void GateTwelveDefinesExactlyElevenLocalizedRowsInTheRequiredOrder()
    {
        var specifications = RetainedProfileSummaryRowsPolicy.Specifications;

        Assert.Equal(11, specifications.Count);
        var expectedMetrics = new[]
        {
            ProfileSummaryMetric.TotalRuns,
            ProfileSummaryMetric.ExtractionRate,
            ProfileSummaryMetric.TotalActiveRaidTime,
            ProfileSummaryMetric.TotalDistanceTravelled,
            ProfileSummaryMetric.KillsByYou,
            ProfileSummaryMetric.Deaths,
            ProfileSummaryMetric.DamageDealt,
            ProfileSummaryMetric.DamageTaken,
            ProfileSummaryMetric.HealthRestored,
            ProfileSummaryMetric.UniqueContainersOpened,
            ProfileSummaryMetric.Economy
        };
        var expectedFallbacks = new[]
        {
            "Total runs",
            "Extraction rate",
            "Total active raid time",
            "Total distance travelled",
            "Kills by you",
            "Deaths",
            "Damage dealt",
            "Damage taken",
            "HP restored",
            "Unique containers opened",
            "Economy"
        };
        Assert.Equal(expectedMetrics, specifications.Select(value => value.Metric));
        Assert.Equal(expectedFallbacks, specifications.Select(value => value.LabelEnglishFallback));
        Assert.All(
            specifications,
            specification => Assert.Equal(
                specification.LabelEnglishFallback,
                UiText.EnglishFallbacks[specification.LabelTextKey]));
        Assert.Equal("Money net:", UiText.EnglishFallbacks["ui.overview_money_net"]);
        Assert.Equal("Cash net:", UiText.EnglishFallbacks["ui.overview_cash_net"]);
        Assert.All(specifications.Take(10), specification => Assert.False(specification.HasSecondaryValue));
        Assert.True(specifications[^1].HasSecondaryValue);
    }

    [Fact]
    public void GateTwelveRowsUseTheAcceptedSharedStyleAndExactReferenceGeometry()
    {
        var layout = CreateRetainedVisualLayout(2560f, 1440f);
        var rows = layout.OverviewProfileSummaryRows;

        Assert.Equal(11, rows.Count);
        Assert.Equal(10f, RetainedProfileSummaryRowsPolicy.RowGapPixels);
        Assert.Equal(76f, RetainedProfileSummaryRowsPolicy.RowStepPixels);
        Assert.Equal(1216f, RetainedProfileSummaryRowsPolicy.LastRowTopPixels);
        Assert.Equal(1282f, RetainedProfileSummaryRowsPolicy.LastRowBottomExclusivePixels);
        Assert.Equal(310f, RetainedProfileSummaryRowsPolicy.EconomyPrimaryValueWidthPixels);
        Assert.Equal(300f, RetainedProfileSummaryRowsPolicy.EconomySecondaryValueWidthPixels);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            Assert.Same(layout.ReferenceTransform, row.Surface.ReferenceTransform);
            Assert.Equal(115f, row.Surface.Left);
            Assert.Equal(456f + 76f * index, row.Surface.Top);
            Assert.Equal(1115f, row.Surface.Width);
            Assert.Equal(66f, row.Surface.Height);
            Assert.Equal(10f, row.Surface.CornerRadius);
            Assert.Equal(135f, row.Entry.LabelLeft);
            Assert.Equal(row.Surface.ContentTop, row.Entry.LabelTop);
            Assert.Equal(465f, row.Entry.LabelWidth);
            Assert.Equal(29.8f, row.Entry.FontSize);
            Assert.False(RetainedOverviewFirstStatisticsRowPolicy.BlocksRaycasts);
            if (index > 0)
                Assert.Equal(10f, row.Surface.Top - rows[index - 1].Surface.Top - rows[index - 1].Surface.Height);
        }

        var normal = rows[1].Entry;
        Assert.False(normal.HasSecondaryValue);
        Assert.Equal(600f, normal.ValueLeft);
        Assert.Equal(610f, normal.ValueWidth);
        var economy = rows[^1].Entry;
        Assert.True(economy.HasSecondaryValue);
        Assert.Equal(600f, economy.ValueLeft);
        Assert.Equal(310f, economy.ValueWidth);
        Assert.Equal(910f, economy.SecondaryValueLeft);
        Assert.Equal(300f, economy.SecondaryValueWidth);
        Assert.Equal(1210f, economy.SecondaryValueLeft + economy.SecondaryValueWidth);
    }

    [Theory]
    [InlineData(1280f, 720f, 228f, 608f, 33f)]
    [InlineData(1680f, 1050f, 351.75f, 850.5f, 43.3125f)]
    [InlineData(1920f, 1080f, 342f, 912f, 49.5f)]
    [InlineData(1920f, 1200f, 402f, 972f, 49.5f)]
    [InlineData(2560f, 1440f, 456f, 1216f, 66f)]
    public void GateTwelveRowStackUsesTheSharedReferenceTransform(
        float viewportWidth,
        float viewportHeight,
        float expectedFirstTop,
        float expectedLastTop,
        float expectedHeight)
    {
        var layout = CreateRetainedVisualLayout(viewportWidth, viewportHeight);

        Assert.Equal(expectedFirstTop, layout.OverviewProfileSummaryRows[0].Surface.Top, 5);
        Assert.Equal(expectedLastTop, layout.OverviewProfileSummaryRows[^1].Surface.Top, 5);
        Assert.All(
            layout.OverviewProfileSummaryRows,
            row => Assert.Equal(expectedHeight, row.Surface.Height, 5));
    }

    [Fact]
    public void GateTwelvePresentationBindsEveryRequestedValueFromTheProjection()
    {
        var supported = new MetricAvailability { State = AdapterCapabilityState.Supported };
        var projection = new StatisticsPanelProjection
        {
            Profile = new ProfileDocument
            {
                Statistics = new ProfileStatistics
                {
                    Overall = new AggregateTotals { ActualHealthRestored = 48.84 }
                }
            },
            Runs = new RunStatisticsViewModel
            {
                TotalRuns = 2,
                ExtractedRuns = 2,
                DiedRuns = 0,
                PhysicalDistance = 2325.94,
                MovementSupported = true,
                Runs = new[] { new RunSummary { ActiveDurationSeconds = 671.795 } }
            },
            Combat = new CombatStatisticsViewModel
            {
                Lifetime = new CombatStatisticsAggregate
                {
                    Totals = new CombatMetricTotals
                    {
                        KillsByYou = 19,
                        DamageDealt = 1298.25,
                        DamageReceived = 85.8
                    }
                },
                Capabilities = new CombatMetricCapabilities
                {
                    KillsByYou = supported,
                    DamageDealt = new MetricAvailability { State = AdapterCapabilityState.Supported },
                    DamageReceived = new MetricAvailability { State = AdapterCapabilityState.Supported }
                }
            },
            Containers = new ContainerStatisticsViewModel
            {
                Lifetime = new ContainerStatisticsAggregate { UniqueContainersLooted = 7 },
                CurrentCapability = AdapterCapabilityState.Supported
            },
            Economy = new EconomyStatisticsAggregate
            {
                Currencies = new Dictionary<string, CurrencyEconomyAggregate>(StringComparer.Ordinal)
                {
                    [CurrencyKind.Money.ToString()] = new CurrencyEconomyAggregate
                    {
                        Currency = CurrencyKind.Money,
                        Totals = new CurrencyFlowTotals { GrossInflow = 2500, GrossOutflow = 270 }
                    },
                    [CurrencyKind.Cash.ToString()] = new CurrencyEconomyAggregate
                    {
                        Currency = CurrencyKind.Cash,
                        Totals = new CurrencyFlowTotals { GrossOutflow = 9744 }
                    }
                },
                Capabilities = new EconomyMetricCapabilities
                {
                    MoneyAmountDirection = new MetricAvailability { State = AdapterCapabilityState.Supported },
                    CashAmountDirection = new MetricAvailability { State = AdapterCapabilityState.Supported }
                }
            },
            CurrentEconomyCapabilities = new EconomyMetricCapabilities
            {
                MoneyAmountDirection = new MetricAvailability { State = AdapterCapabilityState.Supported },
                CashAmountDirection = new MetricAvailability { State = AdapterCapabilityState.Supported }
            }
        };

        var rows = ProfileSummaryPresentationFactory.Create(projection, key => UiText.EnglishFallbacks[key]);

        Assert.Equal(11, rows.Count);
        Assert.Equal("2", rows[0].Value);
        Assert.Equal("100% - 2/2", rows[1].Value);
        Assert.Equal("11:11.795", rows[2].Value);
        Assert.Equal("2,325.94 m", rows[3].Value);
        Assert.Equal("19", rows[4].Value);
        Assert.Equal("0", rows[5].Value);
        Assert.Equal("1,298.25", rows[6].Value);
        Assert.Equal("85.80", rows[7].Value);
        Assert.Equal("48.84", rows[8].Value);
        Assert.Equal("7", rows[9].Value);
        Assert.Equal("Money net: +2,230", rows[10].Value);
        Assert.Equal("Cash net: -9,744", rows[10].SecondaryValue);

        projection.Runs.TotalRuns = 3000;
        projection.Runs.ExtractedRuns = 1;
        projection.Runs.DiedRuns = 1;
        projection.Runs.Runs = new[] { new RunSummary { ActiveDurationSeconds = 60.25 } };
        projection.Runs.PhysicalDistance = 12.3;
        projection.Combat.Lifetime.Totals.KillsByYou = 42;
        projection.Combat.Lifetime.Totals.DamageDealt = 7.5;
        projection.Combat.Lifetime.Totals.DamageReceived = 6.25;
        projection.Profile.Statistics.Overall.ActualHealthRestored = 5.5;
        projection.Containers.Lifetime.UniqueContainersLooted = 4;
        projection.Economy.Currencies[CurrencyKind.Money.ToString()].Totals.GrossInflow = 8;
        projection.Economy.Currencies[CurrencyKind.Money.ToString()].Totals.GrossOutflow = 8;
        projection.Economy.Currencies[CurrencyKind.Cash.ToString()].Totals.GrossOutflow = 3;
        var changed = ProfileSummaryPresentationFactory.Create(projection, key => UiText.EnglishFallbacks[key]);
        Assert.Equal("3000", changed[0].Value);
        Assert.Equal("0% - 1/3,000", changed[1].Value);
        Assert.Equal("1:00.250", changed[2].Value);
        Assert.Equal("12.30 m", changed[3].Value);
        Assert.Equal("42", changed[4].Value);
        Assert.Equal("1", changed[5].Value);
        Assert.Equal("7.50", changed[6].Value);
        Assert.Equal("6.25", changed[7].Value);
        Assert.Equal("5.50", changed[8].Value);
        Assert.Equal("4", changed[9].Value);
        Assert.Equal("Money net: 0", changed[10].Value);
        Assert.Equal("Cash net: -3", changed[10].SecondaryValue);
    }

    [Theory]
    [InlineData(3L, 2L, 0L, "67% - 2/3")]
    [InlineData(2L, 2L, 0L, "100% - 2/2")]
    [InlineData(2L, 1L, 1L, "50% - 1/2")]
    [InlineData(0L, 0L, 0L, "loc:ui.em_dash - 0/0")]
    public void GateTwelveExtractionRateUsesAllTrackedRuns(
        long totalRuns,
        long extractedRuns,
        long diedRuns,
        string expected)
    {
        var projection = new StatisticsPanelProjection
        {
            Runs = new RunStatisticsViewModel
            {
                TotalRuns = totalRuns,
                ExtractedRuns = extractedRuns,
                DiedRuns = diedRuns
            }
        };

        var rows = ProfileSummaryPresentationFactory.Create(projection, key => "loc:" + key);

        Assert.Equal(expected, rows[(int)ProfileSummaryMetric.ExtractionRate].Value);
    }

    [Fact]
    public void GateTwelveFormattingPreservesUnavailablePartialAndRepairedStates()
    {
        var projection = new StatisticsPanelProjection
        {
            Runs = new RunStatisticsViewModel
            {
                Runs = new[]
                {
                    new RunSummary { ActiveDurationSeconds = double.NaN },
                    new RunSummary { ActiveDurationSeconds = -4 },
                    new RunSummary { ActiveDurationSeconds = 3600.001 }
                }
            },
            Containers = new ContainerStatisticsViewModel
            {
                Lifetime = new ContainerStatisticsAggregate
                {
                    UniqueContainersLooted = 1234,
                    HistoricalUnavailable = true
                },
                CurrentCapability = AdapterCapabilityState.Supported
            },
            Economy = new EconomyStatisticsAggregate { HistoricalUnavailable = true }
        };
        string Resolve(string key) => "loc:" + key;

        var rows = ProfileSummaryPresentationFactory.Create(projection, Resolve);

        Assert.Equal("loc:ui.em_dash - 0/0", rows[1].Value);
        Assert.Equal("1:00:00.001", rows[2].Value);
        Assert.Equal("loc:ui.unsupported", rows[3].Value);
        Assert.Equal("loc:ui.unsupported", rows[4].Value);
        Assert.Equal("loc:ui.unsupported", rows[6].Value);
        Assert.Equal("1234 since M7 (loc:ui.container_history_unavailable)", rows[9].Value);
        Assert.Equal("loc:ui.overview_money_net loc:ui.unavailable (loc:ui.pre_m9_unavailable)", rows[10].Value);
        Assert.Equal("loc:ui.overview_cash_net loc:ui.unavailable (loc:ui.pre_m9_unavailable)", rows[10].SecondaryValue);
        Assert.All(rows, row => Assert.Equal("loc:" + RetainedProfileSummaryRowsPolicy.Specifications[(int)row.Metric].LabelTextKey, row.Label));

        projection.Containers.Lifetime.WasRepairedFromInvalidState = true;
        projection.Economy = new EconomyStatisticsAggregate
        {
            MoneyArithmeticSaturated = true,
            Currencies = new Dictionary<string, CurrencyEconomyAggregate>(StringComparer.Ordinal)
            {
                [CurrencyKind.Money.ToString()] = new CurrencyEconomyAggregate
                {
                    Currency = CurrencyKind.Money,
                    Totals = new CurrencyFlowTotals { GrossInflow = 5 }
                }
            },
            Capabilities = new EconomyMetricCapabilities
            {
                MoneyAmountDirection = new MetricAvailability { State = AdapterCapabilityState.Supported }
            }
        };
        rows = ProfileSummaryPresentationFactory.Create(projection, Resolve);
        Assert.Equal("1234 (loc:ui.repaired_unavailable)", rows[9].Value);
        Assert.Equal("loc:ui.overview_money_net +5 (loc:ui.capture_incomplete)", rows[10].Value);
        Assert.Throws<ArgumentNullException>(() => ProfileSummaryPresentationFactory.Create(null!, Resolve));
        Assert.Throws<ArgumentNullException>(() => ProfileSummaryPresentationFactory.Create(projection, null!));
    }

    [Fact]
    public void GateTwelveCompositionAndVisibilityRemainOverviewOwnedAndNonInteractive()
    {
        var overviewView = new object();
        var visibilityStates = new List<bool>();
        var visibility = new RetainedTabViewVisibility<object>(
            overviewView,
            RetainedOverviewPanelStylePolicy.OwnerTab,
            (_, visible) => visibilityStates.Add(visible));

        foreach (var tab in PanelInteractionState.NavigationOrder) visibility.Apply(tab);
        visibility.Apply(StatisticsPanelTab.Overview);

        Assert.Equal(11, RetainedShellCompositionPolicy.ProfileSummaryRowCount);
        Assert.Equal(12, RetainedShellCompositionPolicy.OverviewLeftPanelContentChildCount);
        Assert.Equal(2, RetainedShellCompositionPolicy.ProfileSummaryStandardRowContentChildCount);
        Assert.Equal(3, RetainedShellCompositionPolicy.ProfileSummaryEconomyRowContentChildCount);
        Assert.Equal(76, RetainedShellCompositionPolicy.GraphicCount);
        Assert.Equal(10, RetainedShellCompositionPolicy.ButtonCount);
        Assert.False(RetainedOverviewFirstStatisticsRowPolicy.BlocksRaycasts);
        Assert.False(RetainedOverviewFirstStatisticsRowEntryPolicy.BlocksRaycasts);
        Assert.True(visibilityStates[0]);
        Assert.All(
            visibilityStates.Skip(1).Take(PanelInteractionState.NavigationOrder.Count - 1),
            visible => Assert.False(visible));
        Assert.True(visibilityStates[^1]);
    }

    [Fact]
    public void GateThirteenHighlightsHeadingUsesRightPanelPaddingAndAcceptedTypography()
    {
        var layout = CreateRetainedVisualLayout(2560f, 1440f);
        var leftPanel = layout.OverviewLeftPanel;
        var rightPanel = layout.OverviewRightPanel;
        var profileSummary = layout.OverviewProfileSummaryHeading;
        var highlights = layout.OverviewHighlightsHeading;

        Assert.Equal("OverviewRightPanelContent", RetainedOverviewRightPanelPolicy.ContentName);
        Assert.Equal(StatisticsPanelTab.Overview, RetainedOverviewRightPanelPolicy.OwnerTab);
        Assert.Equal("OverviewHighlightsHeading", RetainedOverviewHighlightsHeadingPolicy.Name);
        Assert.Equal(
            RetainedOverviewRightPanelPolicy.ContentName,
            RetainedOverviewHighlightsHeadingPolicy.ParentName);
        Assert.Equal(StatisticsPanelTab.Overview, RetainedOverviewHighlightsHeadingPolicy.OwnerTab);
        Assert.Equal("ui.overview_highlights", RetainedOverviewHighlightsHeadingPolicy.TextKey);
        Assert.Equal("Highlights", RetainedOverviewHighlightsHeadingPolicy.EnglishFallback);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.EnglishFallback,
            UiText.EnglishFallbacks[RetainedOverviewHighlightsHeadingPolicy.TextKey]);

        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.FontAssetName,
            RetainedOverviewHighlightsHeadingPolicy.FontAssetName);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.SourceMaterialName,
            RetainedOverviewHighlightsHeadingPolicy.SourceMaterialName);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.MaterialName,
            RetainedOverviewHighlightsHeadingPolicy.MaterialName);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.ReferenceFontSize,
            RetainedOverviewHighlightsHeadingPolicy.ReferenceFontSize);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.HeightPixels,
            RetainedOverviewHighlightsHeadingPolicy.HeightPixels);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.ReferenceOpticalOffsetX,
            RetainedOverviewHighlightsHeadingPolicy.ReferenceOpticalOffsetX);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.ReferenceOpticalOffsetY,
            RetainedOverviewHighlightsHeadingPolicy.ReferenceOpticalOffsetY);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.Red,
            RetainedOverviewHighlightsHeadingPolicy.Red);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.Green,
            RetainedOverviewHighlightsHeadingPolicy.Green);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.Blue,
            RetainedOverviewHighlightsHeadingPolicy.Blue);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.Alpha,
            RetainedOverviewHighlightsHeadingPolicy.Alpha);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.WordWrapping,
            RetainedOverviewHighlightsHeadingPolicy.WordWrapping);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.AutoSizing,
            RetainedOverviewHighlightsHeadingPolicy.AutoSizing);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.UsesTopLeftAlignment,
            RetainedOverviewHighlightsHeadingPolicy.UsesTopLeftAlignment);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.UsesOwnedTabLabelMaterial,
            RetainedOverviewHighlightsHeadingPolicy.UsesOwnedTabLabelMaterial);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.UsesZeroTextMargin,
            RetainedOverviewHighlightsHeadingPolicy.UsesZeroTextMargin);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.AdditionalPaddingPixels,
            RetainedOverviewHighlightsHeadingPolicy.AdditionalPaddingPixels);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.UsesFixedOpticalOffset,
            RetainedOverviewHighlightsHeadingPolicy.UsesFixedOpticalOffset);
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.UsesHorizontalScaleCompensation,
            RetainedOverviewHighlightsHeadingPolicy.UsesHorizontalScaleCompensation);
        Assert.False(RetainedOverviewHighlightsHeadingPolicy.BlocksRaycasts);

        Assert.Same(layout.ReferenceTransform, highlights.ReferenceTransform);
        Assert.Equal(1330f, highlights.Left);
        Assert.Equal(400f, highlights.Top);
        Assert.Equal(1115f, highlights.Width);
        Assert.Equal(60f, highlights.Height);
        Assert.Equal(46.3f, highlights.FontSize);
        Assert.Equal(-5f, highlights.OpticalOffsetX);
        Assert.Equal(19f, highlights.OpticalOffsetY);
        Assert.Equal(rightPanel.ContentLeft, highlights.Left);
        Assert.Equal(rightPanel.ContentTop, highlights.Top);
        Assert.Equal(rightPanel.ContentWidth, highlights.Width);
        Assert.Equal(30f, rightPanel.ContentLeft - rightPanel.Left);
        Assert.Equal(30f, rightPanel.ContentTop - rightPanel.Top);
        Assert.Equal(30f, rightPanel.Left + rightPanel.Width - rightPanel.ContentLeft - rightPanel.ContentWidth);
        Assert.Equal(30f, rightPanel.Top + rightPanel.Height - rightPanel.ContentTop - rightPanel.ContentHeight);
        Assert.Equal(profileSummary.Top, highlights.Top);
        Assert.Equal(profileSummary.Width, highlights.Width);
        Assert.Equal(profileSummary.Height, highlights.Height);
        Assert.Equal(profileSummary.FontSize, highlights.FontSize);
        Assert.Equal(profileSummary.OpticalOffsetX, highlights.OpticalOffsetX);
        Assert.Equal(profileSummary.OpticalOffsetY, highlights.OpticalOffsetY);
        Assert.Equal(profileSummary.Left - leftPanel.Left, highlights.Left - rightPanel.Left);
    }

    [Theory]
    [InlineData(1280f, 720f, 665f, 200f, 557.5f, 30f, 23.15f, -2.5f, 9.5f)]
    [InlineData(1680f, 1050f, 872.8125f, 315f, 731.71875f, 39.375f, 30.384375f, -3.28125f, 12.46875f)]
    [InlineData(1920f, 1080f, 997.5f, 300f, 836.25f, 45f, 34.725f, -3.75f, 14.25f)]
    [InlineData(1920f, 1200f, 997.5f, 360f, 836.25f, 45f, 34.725f, -3.75f, 14.25f)]
    [InlineData(2560f, 1440f, 1330f, 400f, 1115f, 60f, 46.3f, -5f, 19f)]
    public void GateThirteenHighlightsHeadingUsesTheSharedReferenceTransform(
        float viewportWidth,
        float viewportHeight,
        float expectedLeft,
        float expectedTop,
        float expectedWidth,
        float expectedHeight,
        float expectedFontSize,
        float expectedOpticalOffsetX,
        float expectedOpticalOffsetY)
    {
        var layout = CreateRetainedVisualLayout(viewportWidth, viewportHeight);
        var panel = layout.OverviewRightPanel;
        var heading = layout.OverviewHighlightsHeading;

        Assert.Same(layout.ReferenceTransform, heading.ReferenceTransform);
        Assert.Equal(expectedLeft, heading.Left, 5);
        Assert.Equal(expectedTop, heading.Top, 5);
        Assert.Equal(expectedWidth, heading.Width, 5);
        Assert.Equal(expectedHeight, heading.Height, 5);
        Assert.Equal(expectedFontSize, heading.FontSize, 5);
        Assert.Equal(expectedOpticalOffsetX, heading.OpticalOffsetX, 5);
        Assert.Equal(expectedOpticalOffsetY, heading.OpticalOffsetY, 5);
        Assert.Equal(panel.ContentLeft, heading.Left, 5);
        Assert.Equal(panel.ContentTop, heading.Top, 5);
        Assert.Equal(panel.ContentWidth, heading.Width, 5);
    }

    [Fact]
    public void GateThirteenHighlightsLocalizationNeverDependsOnEnglishWidth()
    {
        const string localized = "Besondere Höhepunkte dieses ausführlichen Spielstands";

        Assert.Equal(
            localized,
            UiText.Resolve(
                RetainedOverviewHighlightsHeadingPolicy.TextKey,
                key => key == RetainedOverviewHighlightsHeadingPolicy.TextKey ? localized : null));
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.EnglishFallback,
            UiText.Resolve(RetainedOverviewHighlightsHeadingPolicy.TextKey, _ => null));
    }

    [Fact]
    public void GateThirteenHighlightsHeadingInheritsOverviewVisibility()
    {
        var overviewView = new object();
        var visibilityStates = new List<bool>();
        var visibility = new RetainedTabViewVisibility<object>(
            overviewView,
            RetainedOverviewHighlightsHeadingPolicy.OwnerTab,
            (_, visible) => visibilityStates.Add(visible));

        foreach (var tab in PanelInteractionState.NavigationOrder) visibility.Apply(tab);
        visibility.Apply(StatisticsPanelTab.Overview);

        Assert.Equal(StatisticsPanelTab.Overview, visibility.OwnerTab);
        Assert.Equal(PanelInteractionState.NavigationOrder.Count + 1, visibilityStates.Count);
        Assert.True(visibilityStates[0]);
        Assert.All(
            visibilityStates.Skip(1).Take(PanelInteractionState.NavigationOrder.Count - 1),
            visible => Assert.False(visible));
        Assert.True(visibilityStates[^1]);
    }

    [Fact]
    public void GateFourteenFastestExtractionRowUsesExactReferenceGeometryAndAcceptedTypography()
    {
        var layout = CreateRetainedVisualLayout(2560f, 1440f);
        var panel = layout.OverviewRightPanel;
        var row = layout.OverviewFastestExtractionRow;
        var entry = layout.OverviewFastestExtractionEntry;

        Assert.Equal("OverviewFastestExtractionRow", RetainedOverviewFastestExtractionRowPolicy.Name);
        Assert.Equal(
            RetainedOverviewRightPanelPolicy.ContentName,
            RetainedOverviewFastestExtractionRowPolicy.ParentName);
        Assert.Equal(StatisticsPanelTab.Overview, RetainedOverviewFastestExtractionRowPolicy.OwnerTab);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowPolicy.ContentTopOffsetPixels,
            RetainedOverviewFastestExtractionRowPolicy.TopOffsetPixels);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowPolicy.HeightPixels,
            RetainedOverviewFastestExtractionRowPolicy.HeightPixels);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowPolicy.ContentPaddingPixels,
            RetainedOverviewFastestExtractionRowPolicy.ContentPaddingPixels);
        Assert.True(RetainedOverviewFastestExtractionRowPolicy.HasGraphic);
        Assert.False(RetainedOverviewFastestExtractionRowPolicy.BlocksRaycasts);

        Assert.Equal("OverviewFastestExtractionLabel", RetainedOverviewFastestExtractionEntryPolicy.LabelName);
        Assert.Equal("OverviewFastestExtractionValue", RetainedOverviewFastestExtractionEntryPolicy.ValueName);
        Assert.Equal(
            RetainedOverviewFastestExtractionRowPolicy.Name,
            RetainedOverviewFastestExtractionEntryPolicy.ParentName);
        Assert.Equal(StatisticsPanelTab.Overview, RetainedOverviewFastestExtractionEntryPolicy.OwnerTab);
        Assert.Equal("ui.overview_fastest_extraction", RetainedOverviewFastestExtractionEntryPolicy.LabelTextKey);
        Assert.Equal("Fastest extraction", RetainedOverviewFastestExtractionEntryPolicy.LabelEnglishFallback);
        Assert.Equal(
            RetainedOverviewFastestExtractionEntryPolicy.LabelEnglishFallback,
            UiText.EnglishFallbacks[RetainedOverviewFastestExtractionEntryPolicy.LabelTextKey]);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.FontAssetName,
            RetainedOverviewFastestExtractionEntryPolicy.FontAssetName);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.MaterialName,
            RetainedOverviewFastestExtractionEntryPolicy.MaterialName);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.ReferenceFontSize,
            RetainedOverviewFastestExtractionEntryPolicy.ReferenceFontSize);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.CharacterSpacing,
            RetainedOverviewFastestExtractionEntryPolicy.CharacterSpacing);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.WordSpacing,
            RetainedOverviewFastestExtractionEntryPolicy.WordSpacing);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.LineSpacing,
            RetainedOverviewFastestExtractionEntryPolicy.LineSpacing);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.ParagraphSpacing,
            RetainedOverviewFastestExtractionEntryPolicy.ParagraphSpacing);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.WordWrapping,
            RetainedOverviewFastestExtractionEntryPolicy.WordWrapping);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.AutoSizing,
            RetainedOverviewFastestExtractionEntryPolicy.AutoSizing);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.UsesVisibleOverflow,
            RetainedOverviewFastestExtractionEntryPolicy.UsesVisibleOverflow);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.UsesLeftAlignment,
            RetainedOverviewFastestExtractionEntryPolicy.UsesLeftAlignment);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.UsesVerticalCentering,
            RetainedOverviewFastestExtractionEntryPolicy.UsesVerticalCentering);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.UsesZeroTextMargins,
            RetainedOverviewFastestExtractionEntryPolicy.UsesZeroTextMargins);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.UsesNormalStyle,
            RetainedOverviewFastestExtractionEntryPolicy.UsesNormalStyle);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.UsesRegularWeight,
            RetainedOverviewFastestExtractionEntryPolicy.UsesRegularWeight);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowEntryPolicy.UsesOwnedSubtleShadowMaterial,
            RetainedOverviewFastestExtractionEntryPolicy.UsesOwnedSubtleShadowMaterial);
        Assert.True(RetainedOverviewFastestExtractionEntryPolicy.UsesProjectedShortestExtraction);
        Assert.False(RetainedOverviewFastestExtractionEntryPolicy.BlocksRaycasts);

        Assert.Same(layout.ReferenceTransform, row.ReferenceTransform);
        Assert.Equal(1330f, row.Left);
        Assert.Equal(456f, row.Top);
        Assert.Equal(1115f, row.Width);
        Assert.Equal(66f, row.Height);
        Assert.Equal(1350f, row.ContentLeft);
        Assert.Equal(476f, row.ContentTop);
        Assert.Equal(1075f, row.ContentWidth);
        Assert.Equal(26f, row.ContentHeight);
        Assert.Equal(panel.ContentLeft, row.Left);
        Assert.Equal(panel.ContentTop + 56f, row.Top);
        Assert.Equal(panel.ContentWidth, row.Width);

        Assert.Same(layout.ReferenceTransform, entry.ReferenceTransform);
        Assert.Equal(1350f, entry.LabelLeft);
        Assert.Equal(476f, entry.LabelTop);
        Assert.Equal(440f, entry.LabelWidth);
        Assert.Equal(26f, entry.LabelHeight);
        Assert.Equal(1790f, entry.ValueLeft);
        Assert.Equal(476f, entry.ValueTop);
        Assert.Equal(635f, entry.ValueWidth);
        Assert.Equal(26f, entry.ValueHeight);
        Assert.Equal(29.8f, entry.FontSize);
        Assert.False(entry.HasSecondaryValue);
        Assert.Equal(row.ContentLeft, entry.LabelLeft);
        Assert.Equal(entry.LabelLeft + entry.LabelWidth, entry.ValueLeft);
        Assert.Equal(2425f, entry.ValueLeft + entry.ValueWidth);
        Assert.Equal(row.ContentLeft + row.ContentWidth, entry.ValueLeft + entry.ValueWidth);
    }

    [Theory]
    [InlineData(1280f, 720f, 665f, 228f, 557.5f, 33f, 675f, 895f, 317.5f, 14.9f)]
    [InlineData(1680f, 1050f, 872.8125f, 351.75f, 731.71875f, 43.3125f, 885.9375f, 1174.6875f, 416.71875f, 19.55625f)]
    [InlineData(1920f, 1200f, 997.5f, 402f, 836.25f, 49.5f, 1012.5f, 1342.5f, 476.25f, 22.35f)]
    [InlineData(2560f, 1440f, 1330f, 456f, 1115f, 66f, 1350f, 1790f, 635f, 29.8f)]
    public void GateFourteenFastestExtractionRowUsesTheSharedReferenceTransform(
        float viewportWidth,
        float viewportHeight,
        float expectedLeft,
        float expectedTop,
        float expectedWidth,
        float expectedHeight,
        float expectedLabelLeft,
        float expectedValueLeft,
        float expectedValueWidth,
        float expectedFontSize)
    {
        var layout = CreateRetainedVisualLayout(viewportWidth, viewportHeight);
        var row = layout.OverviewFastestExtractionRow;
        var entry = layout.OverviewFastestExtractionEntry;

        Assert.Same(layout.ReferenceTransform, row.ReferenceTransform);
        Assert.Same(layout.ReferenceTransform, entry.ReferenceTransform);
        Assert.Equal(expectedLeft, row.Left, 5);
        Assert.Equal(expectedTop, row.Top, 5);
        Assert.Equal(expectedWidth, row.Width, 5);
        Assert.Equal(expectedHeight, row.Height, 5);
        Assert.Equal(expectedLabelLeft, entry.LabelLeft, 5);
        Assert.Equal(expectedValueLeft, entry.ValueLeft, 5);
        Assert.Equal(expectedValueWidth, entry.ValueWidth, 5);
        Assert.Equal(expectedFontSize, entry.FontSize, 5);
    }

    [Fact]
    public void GateFourteenPresentationUsesProjectedShortestExtractionWithoutHardcodedMockValues()
    {
        var projectedShortest = new DurationRecordReference
        {
            RunId = "projected-shortest",
            ActiveDurationSeconds = 64.083,
            MapDisplayName = "Ground Zero"
        };
        var projection = new StatisticsPanelProjection
        {
            Runs = new RunStatisticsViewModel
            {
                Records = new RunDurationRecords
                {
                    Extraction = new DurationRecordPair
                    {
                        Shortest = projectedShortest,
                        Longest = new DurationRecordReference
                        {
                            RunId = "projected-longest",
                            ActiveDurationSeconds = 900,
                            MapDisplayName = "Long extraction"
                        }
                    },
                    Death = new DurationRecordPair
                    {
                        Shortest = new DurationRecordReference
                        {
                            RunId = "death-shortest",
                            ActiveDurationSeconds = 2,
                            MapDisplayName = "Death record"
                        }
                    }
                },
                Runs = new[]
                {
                    new RunSummary
                    {
                        RunId = "unprojected-minimum",
                        ActiveDurationSeconds = 1,
                        MapDisplayName = "Run history minimum"
                    }
                }
            }
        };

        var presentation = FastestExtractionHighlightPresentationFactory.Create(
            projection,
            key => UiText.EnglishFallbacks[key]);

        Assert.Equal("Fastest extraction", presentation.Label);
        Assert.Equal("01:04.083 - Ground Zero", presentation.Value);

        projectedShortest.ActiveDurationSeconds = 75.25;
        projectedShortest.MapDisplayName = "Harbor";
        presentation = FastestExtractionHighlightPresentationFactory.Create(
            projection,
            key => UiText.EnglishFallbacks[key]);
        Assert.Equal("01:15.250 - Harbor", presentation.Value);
    }

    [Theory]
    [InlineData(0d, "00:00.000 - Test map")]
    [InlineData(1.2345d, "00:01.235 - Test map")]
    [InlineData(64.0834d, "01:04.083 - Test map")]
    [InlineData(64.0836d, "01:04.084 - Test map")]
    [InlineData(3600d, "1:00:00.000 - Test map")]
    [InlineData(3661.234d, "1:01:01.234 - Test map")]
    public void GateFourteenFormatsProjectedDurationWithInvariantMillisecondPrecision(
        double durationSeconds,
        string expected)
    {
        var projection = CreateFastestExtractionProjection(durationSeconds, "Test map");

        var presentation = FastestExtractionHighlightPresentationFactory.Create(
            projection,
            key => UiText.EnglishFallbacks[key]);

        Assert.Equal(expected, presentation.Value);
    }

    [Fact]
    public void GateFourteenPresentationPreservesEmptyUnavailableAndUnknownMapStates()
    {
        string Resolve(string key) => "loc:" + key;
        var projection = new StatisticsPanelProjection();

        var presentation = FastestExtractionHighlightPresentationFactory.Create(projection, Resolve);

        Assert.Equal("loc:ui.overview_fastest_extraction", presentation.Label);
        Assert.Equal("loc:ui.em_dash", presentation.Value);

        foreach (var invalidDuration in new[] { -1d, double.NaN, double.PositiveInfinity, double.MaxValue })
        {
            projection = CreateFastestExtractionProjection(invalidDuration, "Map");
            presentation = FastestExtractionHighlightPresentationFactory.Create(projection, Resolve);
            Assert.Equal("loc:ui.unavailable", presentation.Value);
        }

        foreach (var invalidMapName in new string?[] { null, string.Empty, "   " })
        {
            projection = CreateFastestExtractionProjection(1d, invalidMapName!);
            presentation = FastestExtractionHighlightPresentationFactory.Create(projection, Resolve);
            Assert.Equal("loc:ui.unavailable", presentation.Value);
        }

        projection = CreateFastestExtractionProjection(1d, MapIdentity.UnknownDisplayName);
        presentation = FastestExtractionHighlightPresentationFactory.Create(projection, Resolve);
        Assert.Equal($"00:01.000 - {MapIdentity.UnknownDisplayName}", presentation.Value);
        Assert.Throws<ArgumentNullException>(
            () => FastestExtractionHighlightPresentationFactory.Create(null!, Resolve));
        Assert.Throws<ArgumentNullException>(
            () => FastestExtractionHighlightPresentationFactory.Create(projection, null!));
    }

    [Fact]
    public void GateFourteenLocalizedAndLongTextCannotRejectTheRow()
    {
        const string localizedLabel = "Schnellste erfolgreiche Extraktion dieses Spielstands";
        const string longMapName = "An unexpectedly long localized map display name retained exactly";
        var projection = CreateFastestExtractionProjection(64.083d, longMapName);
        string Resolve(string key) => key == RetainedOverviewFastestExtractionEntryPolicy.LabelTextKey
            ? localizedLabel
            : "loc:" + key;

        var presentation = FastestExtractionHighlightPresentationFactory.Create(projection, Resolve);

        Assert.Equal(localizedLabel, presentation.Label);
        Assert.Equal($"01:04.083 - {longMapName}", presentation.Value);
        Assert.Equal(1115f, CreateRetainedVisualLayout(2560f, 1440f).OverviewFastestExtractionRow.Width);
    }

    [Fact]
    public void OverviewHighlightRowsRetainTwoTextChildrenAndGainOneNonInteractiveBackground()
    {
        var overviewView = new object();
        var visibilityStates = new List<bool>();
        var visibility = new RetainedTabViewVisibility<object>(
            overviewView,
            RetainedOverviewFastestExtractionRowPolicy.OwnerTab,
            (_, visible) => visibilityStates.Add(visible));

        foreach (var tab in PanelInteractionState.NavigationOrder) visibility.Apply(tab);
        visibility.Apply(StatisticsPanelTab.Overview);

        Assert.Equal(1, RetainedShellCompositionPolicy.OverviewRightPanelChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewHighlightsHeadingChildCount);
        Assert.Equal(2, RetainedShellCompositionPolicy.OverviewFastestExtractionRowChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.OverviewFastestExtractionRowGraphicCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewFastestExtractionLabelChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewFastestExtractionValueChildCount);
        Assert.True(RetainedOverviewFastestExtractionRowPolicy.HasGraphic);
        Assert.Equal(0f, RetainedOverviewFastestExtractionRowPolicy.Red);
        Assert.Equal(0f, RetainedOverviewFastestExtractionRowPolicy.Green);
        Assert.Equal(0f, RetainedOverviewFastestExtractionRowPolicy.Blue);
        Assert.Equal(0.50f, RetainedOverviewFastestExtractionRowPolicy.LayerAlpha);
        Assert.Equal(10f, RetainedOverviewFastestExtractionRowPolicy.CornerRadiusPixels);
        Assert.False(RetainedOverviewFastestExtractionRowPolicy.BlocksRaycasts);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowPolicy.CornerRadiusPixels,
            RetainedOverviewFastestExtractionRowPolicy.CornerRadiusPixels);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowPolicy.LayerAlpha,
            RetainedOverviewFastestExtractionRowPolicy.LayerAlpha);
        Assert.Equal(
            RetainedOverviewFirstStatisticsRowPolicy.BlocksRaycasts,
            RetainedOverviewFastestExtractionRowPolicy.BlocksRaycasts);
        Assert.Equal(StatisticsPanelTab.Overview, visibility.OwnerTab);
        Assert.True(visibilityStates[0]);
        Assert.All(
            visibilityStates.Skip(1).Take(PanelInteractionState.NavigationOrder.Count - 1),
            visible => Assert.False(visible));
        Assert.True(visibilityStates[^1]);
    }

    [Fact]
    public void GateFifteenHighlightsUseExactOrderedReferenceGeometryAndGateFourteenTypography()
    {
        var layout = CreateRetainedVisualLayout(2560f, 1440f);
        var expected = new[]
        {
            (OverviewHighlightMetric.FastestExtraction, "OverviewFastestExtractionRow",
                "ui.overview_fastest_extraction", "Fastest extraction", 456f, 476f),
            (OverviewHighlightMetric.LongestSuccessfulRaid, "OverviewLongestSuccessfulRaidRow",
                "ui.overview_longest_successful_raid", "Longest successful raid", 532f, 552f),
            (OverviewHighlightMetric.MostUsedWeapon, "OverviewMostUsedWeaponRow",
                "ui.overview_most_used_weapon", "Most-used weapon", 608f, 628f),
            (OverviewHighlightMetric.MostUsedConsumable, "OverviewMostUsedConsumableRow",
                "ui.overview_most_used_consumable", "Most-used consumable", 684f, 704f)
        };

        Assert.Equal(4, RetainedOverviewHighlightsRowsPolicy.RowCount);
        Assert.Equal(76f, RetainedOverviewHighlightsRowsPolicy.RowStepPixels);
        Assert.Equal(10f, RetainedOverviewHighlightsRowsPolicy.RowGapPixels);
        Assert.Equal(expected.Length, RetainedOverviewHighlightsRowsPolicy.Specifications.Count);
        Assert.Equal(expected.Length, layout.OverviewHighlightRows.Count);
        Assert.Equal(expected.Length, layout.OverviewHighlightEntries.Count);
        for (var index = 0; index < expected.Length; index++)
        {
            var specification = RetainedOverviewHighlightsRowsPolicy.Specifications[index];
            var row = layout.OverviewHighlightRows[index];
            var entry = layout.OverviewHighlightEntries[index];
            Assert.Equal(expected[index].Item1, specification.Metric);
            Assert.Equal(expected[index].Item2, specification.RowName);
            Assert.Equal(expected[index].Item3, specification.LabelTextKey);
            Assert.Equal(expected[index].Item4, specification.LabelEnglishFallback);
            Assert.Equal(specification.LabelEnglishFallback, UiText.EnglishFallbacks[specification.LabelTextKey]);
            Assert.Same(specification, row.Specification);
            Assert.Same(layout.ReferenceTransform, row.ReferenceTransform);
            Assert.Same(layout.ReferenceTransform, entry.ReferenceTransform);
            Assert.Equal(1330f, row.Left);
            Assert.Equal(expected[index].Item5, row.Top);
            Assert.Equal(1115f, row.Width);
            Assert.Equal(66f, row.Height);
            Assert.Equal(10f, row.CornerRadius);
            Assert.Equal(1350f, row.ContentLeft);
            Assert.Equal(expected[index].Item6, row.ContentTop);
            Assert.Equal(1075f, row.ContentWidth);
            Assert.Equal(26f, row.ContentHeight);
            Assert.Equal(1350f, entry.LabelLeft);
            Assert.Equal(expected[index].Item6, entry.LabelTop);
            Assert.Equal(440f, entry.LabelWidth);
            Assert.Equal(26f, entry.LabelHeight);
            Assert.Equal(1790f, entry.ValueLeft);
            Assert.Equal(expected[index].Item6, entry.ValueTop);
            Assert.Equal(635f, entry.ValueWidth);
            Assert.Equal(26f, entry.ValueHeight);
            Assert.Equal(29.8f, entry.FontSize);
            Assert.False(entry.HasSecondaryValue);
            if (index > 0)
                Assert.Equal(10f, row.Top - (layout.OverviewHighlightRows[index - 1].Top + 66f));
        }

        Assert.Same(layout.OverviewHighlightRows[0], layout.OverviewFastestExtractionRow);
        Assert.Same(layout.OverviewHighlightEntries[0], layout.OverviewFastestExtractionEntry);
        Assert.Equal(750f, layout.OverviewHighlightRows[^1].Top + layout.OverviewHighlightRows[^1].Height);
        Assert.Equal(RetainedOverviewFirstStatisticsRowEntryPolicy.FontAssetName,
            RetainedOverviewFastestExtractionEntryPolicy.FontAssetName);
        Assert.Equal(RetainedOverviewFirstStatisticsRowEntryPolicy.MaterialName,
            RetainedOverviewFastestExtractionEntryPolicy.MaterialName);
        Assert.Equal(RetainedOverviewFirstStatisticsRowEntryPolicy.ReferenceFontSize,
            RetainedOverviewFastestExtractionEntryPolicy.ReferenceFontSize);
        Assert.Equal("firing actions", UiText.EnglishFallbacks["ui.overview_firing_actions_unit"]);
        Assert.Equal("uses", UiText.EnglishFallbacks["ui.overview_uses_unit"]);
        Assert.True(RetainedOverviewFastestExtractionRowPolicy.HasGraphic);
        Assert.False(RetainedOverviewFastestExtractionRowPolicy.BlocksRaycasts);
        Assert.False(RetainedOverviewFastestExtractionEntryPolicy.BlocksRaycasts);
    }

    [Theory]
    [InlineData(1280f, 720f)]
    [InlineData(1680f, 1050f)]
    [InlineData(1920f, 1200f)]
    [InlineData(2560f, 1440f)]
    public void GateFifteenAllRowsUseTheSharedReferenceTransform(float viewportWidth, float viewportHeight)
    {
        var layout = CreateRetainedVisualLayout(viewportWidth, viewportHeight);
        var transform = layout.ReferenceTransform;
        var referenceTops = new[] { 456f, 532f, 608f, 684f };
        var referenceContentTops = new[] { 476f, 552f, 628f, 704f };

        for (var index = 0; index < RetainedOverviewHighlightsRowsPolicy.RowCount; index++)
        {
            var row = layout.OverviewHighlightRows[index];
            var entry = layout.OverviewHighlightEntries[index];
            Assert.Equal(transform.CanvasX(1330f), row.Left, 5);
            Assert.Equal(transform.CanvasY(referenceTops[index]), row.Top, 5);
            Assert.Equal(transform.CanvasLength(1115f), row.Width, 5);
            Assert.Equal(transform.CanvasLength(66f), row.Height, 5);
            Assert.Equal(transform.CanvasLength(10f), row.CornerRadius, 5);
            Assert.Equal(transform.CanvasX(1350f), entry.LabelLeft, 5);
            Assert.Equal(transform.CanvasY(referenceContentTops[index]), entry.LabelTop, 5);
            Assert.Equal(transform.CanvasLength(440f), entry.LabelWidth, 5);
            Assert.Equal(transform.CanvasX(1790f), entry.ValueLeft, 5);
            Assert.Equal(transform.CanvasLength(635f), entry.ValueWidth, 5);
            Assert.Equal(transform.CanvasLength(29.8f), entry.FontSize, 5);
        }
    }

    [Fact]
    public void GateFifteenMockValuesComeFromAllFourAuthoritativeProjections()
    {
        var projection = CreateGateFifteenProjection();

        var presentations = OverviewHighlightsPresentationFactory.Create(
            projection,
            key => UiText.EnglishFallbacks[key]);

        Assert.Collection(
            presentations,
            value =>
            {
                Assert.Equal(OverviewHighlightMetric.FastestExtraction, value.Metric);
                Assert.Equal("01:04.083 - Ground Zero", value.Value);
            },
            value =>
            {
                Assert.Equal(OverviewHighlightMetric.LongestSuccessfulRaid, value.Metric);
                Assert.Equal("10:07.713 - Ground Zero - Farm Town", value.Value);
            },
            value =>
            {
                Assert.Equal(OverviewHighlightMetric.MostUsedWeapon, value.Metric);
                Assert.Equal("Electrified MP7 - 143 firing actions", value.Value);
            },
            value =>
            {
                Assert.Equal(OverviewHighlightMetric.MostUsedConsumable, value.Metric);
                Assert.Equal("Med-Kit (S) - 5 uses", value.Value);
            });
    }

    [Fact]
    public void GateFifteenLongestSuccessfulRaidUsesExtractionLongestAndExactOrderedRoute()
    {
        var projection = CreateGateFifteenProjection();
        projection.Runs.Records.Extraction.Shortest!.ActiveDurationSeconds = 9999d;
        projection.Runs.Records.Death = new DurationRecordPair
        {
            Longest = new DurationRecordReference
            {
                ActiveDurationSeconds = 99999d,
                MapDisplayName = "Death record"
            }
        };
        projection.Runs.Runs = new[]
        {
            projection.Runs.Runs[0],
            new RunSummary
            {
                RunId = "unprojected-run-maximum",
                ActiveDurationSeconds = 999999d,
                MapDisplayName = "Run list maximum"
            }
        };
        var matchingRun = projection.Runs.Runs[0];
        matchingRun.Segments = new List<MapSegmentSummary>
        {
            new() { SegmentIndex = 2, MapDisplayName = "Ground Zero" },
            new() { SegmentIndex = 0, MapDisplayName = "Farm Town" },
            new() { SegmentIndex = 1, MapDisplayName = "Farm Town" }
        };

        var presentation = Highlight(projection, OverviewHighlightMetric.LongestSuccessfulRaid);

        Assert.Equal("10:07.713 - Farm Town - Farm Town - Ground Zero", presentation.Value);
    }

    [Fact]
    public void GateFifteenLongestSuccessfulRaidFallsBackToRecordedMapWhenRouteIsUnproven()
    {
        var projection = CreateGateFifteenProjection();
        projection.Runs.Runs = Array.Empty<RunSummary>();
        Assert.Equal(
            "10:07.713 - Recorded map",
            Highlight(projection, OverviewHighlightMetric.LongestSuccessfulRaid).Value);

        projection = CreateGateFifteenProjection();
        projection.Runs.Runs[0].HistoricalRouteUnavailable = true;
        Assert.Equal(
            "10:07.713 - Recorded map",
            Highlight(projection, OverviewHighlightMetric.LongestSuccessfulRaid).Value);
    }

    [Fact]
    public void GateFifteenLongestSuccessfulRaidPreservesMissingInvalidZeroAndUnknownStates()
    {
        string Resolve(string key) => "loc:" + key;
        var projection = CreateGateFifteenProjection();
        projection.Runs.Records.Extraction.Longest = null;
        Assert.Equal("loc:ui.em_dash",
            Highlight(projection, OverviewHighlightMetric.LongestSuccessfulRaid, Resolve).Value);

        foreach (var duration in new[] { -1d, double.NaN, double.PositiveInfinity })
        {
            projection = CreateGateFifteenProjection();
            projection.Runs.Records.Extraction.Longest!.ActiveDurationSeconds = duration;
            Assert.Equal("loc:ui.unavailable",
                Highlight(projection, OverviewHighlightMetric.LongestSuccessfulRaid, Resolve).Value);
        }

        projection = CreateGateFifteenProjection();
        projection.Runs.Records.Extraction.Longest!.MapDisplayName = " ";
        Assert.Equal("loc:ui.unavailable",
            Highlight(projection, OverviewHighlightMetric.LongestSuccessfulRaid, Resolve).Value);

        projection = CreateGateFifteenProjection();
        projection.Runs.Runs = Array.Empty<RunSummary>();
        projection.Runs.Records.Extraction.Longest = new DurationRecordReference
        {
            ActiveDurationSeconds = 0d,
            MapDisplayName = MapIdentity.UnknownDisplayName
        };
        Assert.Equal($"00:00.000 - {MapIdentity.UnknownDisplayName}",
            Highlight(projection, OverviewHighlightMetric.LongestSuccessfulRaid, Resolve).Value);
    }

    [Fact]
    public void GateFifteenMostUsedWeaponRanksAcceptedFiringActionsOnly()
    {
        var projection = CreateGateFifteenProjection();
        projection.Weapons.Lifetime.Totals.FiringActions = 1240;
        projection.WeaponAmmunitionGroups = new[]
        {
            new WeaponAmmunitionGroupProjection
            {
                WeaponId = "weapon:accepted",
                DisplayName = "Accepted winner",
                TotalFiringActions = 1234,
                CorrelatedFiringActions = 1,
                UncorrelatedFiringActions = 1233
            },
            new WeaponAmmunitionGroupProjection
            {
                WeaponId = "weapon:pairs",
                DisplayName = "Pair-count decoy",
                TotalFiringActions = 6,
                CorrelatedFiringActions = 9999
            }
        };

        var presentation = Highlight(projection, OverviewHighlightMetric.MostUsedWeapon);

        Assert.Equal("Accepted winner - 1,234 firing actions", presentation.Value);
    }

    [Fact]
    public void GateFifteenMostUsedWeaponTieBreakIsOrdinalAndIndependentOfInputOrder()
    {
        var first = CreateWeaponRankingProjection(
            ("weapon:b", "Same name", 10),
            ("weapon:z", "Zulu", 10),
            ("weapon:a", "Same name", 10),
            ("weapon:x", "Alpha", 10));
        var second = CreateWeaponRankingProjection(
            ("weapon:x", "Alpha", 10),
            ("weapon:a", "Same name", 10),
            ("weapon:z", "Zulu", 10),
            ("weapon:b", "Same name", 10));

        Assert.Equal("Alpha - 10 firing actions",
            Highlight(first, OverviewHighlightMetric.MostUsedWeapon).Value);
        Assert.Equal(
            Highlight(first, OverviewHighlightMetric.MostUsedWeapon).Value,
            Highlight(second, OverviewHighlightMetric.MostUsedWeapon).Value);

        first.WeaponAmmunitionGroups = first.WeaponAmmunitionGroups
            .Where(value => value.DisplayName == "Same name")
            .Reverse()
            .ToArray();
        first.Weapons.Lifetime.Totals.FiringActions = 20;
        Assert.Equal("Same name - 10 firing actions",
            Highlight(first, OverviewHighlightMetric.MostUsedWeapon).Value);
        Assert.Equal("weapon:a", first.WeaponAmmunitionGroups
            .OrderBy(value => value.DisplayName, StringComparer.Ordinal)
            .ThenBy(value => value.WeaponId, StringComparer.Ordinal)
            .First().WeaponId);
    }

    [Fact]
    public void GateFifteenMostUsedWeaponPreservesUnsupportedUnavailableAndProvenEmptyStates()
    {
        string Resolve(string key) => "loc:" + key;
        var projection = CreateWeaponRankingProjection();
        projection.Weapons.Capabilities.FiringActions.State = AdapterCapabilityState.DisabledIncompatible;
        Assert.Equal("loc:ui.unsupported",
            Highlight(projection, OverviewHighlightMetric.MostUsedWeapon, Resolve).Value);

        projection = CreateWeaponRankingProjection(("weapon:a", "A", 5));
        projection.Weapons.Capabilities.WeaponIdentity.State = AdapterCapabilityState.DisabledIncompatible;
        Assert.Equal("loc:ui.unavailable",
            Highlight(projection, OverviewHighlightMetric.MostUsedWeapon, Resolve).Value);

        projection = CreateWeaponRankingProjection();
        projection.Weapons.Capabilities.FiringActions.State = AdapterCapabilityState.Experimental;
        Assert.Equal("loc:ui.unavailable",
            Highlight(projection, OverviewHighlightMetric.MostUsedWeapon, Resolve).Value);

        projection = CreateWeaponRankingProjection(("weapon:a", "A", 5), ("weapon:b", "B", 4));
        projection.Weapons.Lifetime.Totals.FiringActions = 10;
        Assert.Equal("loc:ui.unavailable",
            Highlight(projection, OverviewHighlightMetric.MostUsedWeapon, Resolve).Value);

        projection = CreateWeaponRankingProjection(("weapon:a", "A", 6), ("weapon:b", "B", 3));
        projection.Weapons.Lifetime.Totals.FiringActions = 10;
        Assert.Equal("A - 6 loc:ui.overview_firing_actions_unit",
            Highlight(projection, OverviewHighlightMetric.MostUsedWeapon, Resolve).Value);

        projection = CreateWeaponRankingProjection();
        Assert.Equal("loc:ui.em_dash",
            Highlight(projection, OverviewHighlightMetric.MostUsedWeapon, Resolve).Value);

        projection = CreateWeaponRankingProjection(("weapon:a", "A", 1));
        projection.Weapons.Lifetime.WasRepairedFromInvalidState = true;
        Assert.Equal("loc:ui.unavailable",
            Highlight(projection, OverviewHighlightMetric.MostUsedWeapon, Resolve).Value);
    }

    [Fact]
    public void GateFifteenMostUsedConsumableRanksActivationCountAndUsesStableFallback()
    {
        var projection = CreateConsumableRankingProjection(
            ("item:heal", "", 1234, 0d),
            ("item:amount-decoy", "Amount decoy", 2, 99999d));

        var presentation = Highlight(projection, OverviewHighlightMetric.MostUsedConsumable);

        Assert.Equal("Unknown / modded item [item:heal] - 1,234 uses", presentation.Value);
    }

    [Fact]
    public void GateFifteenMostUsedConsumableTieBreakIsOrdinalAndIndependentOfInputOrder()
    {
        var first = CreateConsumableRankingProjection(
            ("item:z", "Zulu", 5, 0d),
            ("item:b", "Same name", 5, 0d),
            ("item:a", "Same name", 5, 0d),
            ("item:x", "Alpha", 5, 0d));
        var second = CreateConsumableRankingProjection(
            ("item:x", "Alpha", 5, 0d),
            ("item:a", "Same name", 5, 0d),
            ("item:b", "Same name", 5, 0d),
            ("item:z", "Zulu", 5, 0d));

        Assert.Equal("Alpha - 5 uses",
            Highlight(first, OverviewHighlightMetric.MostUsedConsumable).Value);
        Assert.Equal(
            Highlight(first, OverviewHighlightMetric.MostUsedConsumable).Value,
            Highlight(second, OverviewHighlightMetric.MostUsedConsumable).Value);

        first = CreateConsumableRankingProjection(
            ("item:b", "Same name", 5, 0d),
            ("item:a", "Same name", 5, 0d));
        Assert.Equal("Same name - 5 uses",
            Highlight(first, OverviewHighlightMetric.MostUsedConsumable).Value);
    }

    [Fact]
    public void GateFifteenMostUsedConsumablePreservesIncompleteRepairedAndProvenEmptyStates()
    {
        string Resolve(string key) => "loc:" + key;
        var projection = CreateConsumableRankingProjection(("item:a", "A", 5, 0d));
        projection.ItemUse.HistoricalUnavailable = true;
        Assert.Equal("loc:ui.unavailable",
            Highlight(projection, OverviewHighlightMetric.MostUsedConsumable, Resolve).Value);

        projection = CreateConsumableRankingProjection(("item:a", "A", 5, 0d));
        projection.ItemUse.WasRepairedFromInvalidState = true;
        Assert.Equal("loc:ui.unavailable",
            Highlight(projection, OverviewHighlightMetric.MostUsedConsumable, Resolve).Value);

        projection = CreateConsumableRankingProjection(("item:a", "A", 5, 0d));
        projection.ItemUse.Overall.ActivationCount = 6;
        Assert.Equal("loc:ui.unavailable",
            Highlight(projection, OverviewHighlightMetric.MostUsedConsumable, Resolve).Value);

        projection = CreateConsumableRankingProjection();
        Assert.Equal("loc:ui.em_dash",
            Highlight(projection, OverviewHighlightMetric.MostUsedConsumable, Resolve).Value);
    }

    [Fact]
    public void GateFifteenCountsAndStableFallbacksRemainInvariantAcrossCurrentCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var weapon = CreateWeaponRankingProjection(("weapon:modded", "", 1234));
            var consumable = CreateConsumableRankingProjection(("item:modded", "", 1234, 0d));

            Assert.Equal("Unknown / modded item [weapon:modded] - 1,234 firing actions",
                Highlight(weapon, OverviewHighlightMetric.MostUsedWeapon).Value);
            Assert.Equal("Unknown / modded item [item:modded] - 1,234 uses",
                Highlight(consumable, OverviewHighlightMetric.MostUsedConsumable).Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void GateFifteenItemUseProjectionCarriesHistoricalAndRepairTruthfulness()
    {
        var profile = Profile("generation-a");
        profile.Statistics.RunTotals.ItemStatistics.HistoricalUnavailable = true;
        profile.Statistics.RunTotals.ItemStatistics.WasRepairedFromInvalidState = true;

        var projection = Create(profile);

        Assert.True(projection.ItemUse.HistoricalUnavailable);
        Assert.True(projection.ItemUse.WasRepairedFromInvalidState);
        Assert.Equal("Unavailable",
            Highlight(projection, OverviewHighlightMetric.MostUsedConsumable).Value);
    }

    [Fact]
    public void GateFifteenLocalizedAndLongTextCannotRejectTheHighlights()
    {
        const string longName = "An unusually long localized display name that remains visible without gating";
        var projection = CreateGateFifteenProjection();
        projection.WeaponAmmunitionGroups[0].DisplayName = longName;
        projection.ItemUse.Items[0].DisplayName = longName;
        string Resolve(string key) => "localized:" + key;

        var presentations = OverviewHighlightsPresentationFactory.Create(projection, Resolve);

        Assert.Equal(4, presentations.Count);
        Assert.All(presentations, value => Assert.StartsWith("localized:ui.overview_", value.Label));
        Assert.Contains(longName, presentations[2].Value, StringComparison.Ordinal);
        Assert.Contains(longName, presentations[3].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewHighlightCardsAddFourGraphicsWithoutChangingRowsTextOrInteraction()
    {
        Assert.Equal(4, RetainedShellCompositionPolicy.OverviewHighlightRowCount);
        Assert.Equal(2, RetainedShellCompositionPolicy.OverviewHighlightRowChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.OverviewHighlightRowGraphicCount);
        Assert.Equal(
            4,
            RetainedShellCompositionPolicy.OverviewHighlightRowCount
            * RetainedShellCompositionPolicy.OverviewHighlightRowGraphicCount);
        Assert.Equal(7, RetainedShellCompositionPolicy.OverviewRightPanelContentChildCount);
        Assert.Equal(5, RetainedShellCompositionPolicy.OverviewRightPanelContentChildCount - 2);
        Assert.Equal(76, RetainedShellCompositionPolicy.GraphicCount);
        Assert.Equal(6, RetainedShellCompositionPolicy.GraphicCount - 70);
        Assert.Equal(10, RetainedShellCompositionPolicy.ButtonCount);
        Assert.False(RetainedOverviewFastestExtractionRowPolicy.BlocksRaycasts);
        Assert.False(RetainedOverviewFastestExtractionEntryPolicy.BlocksRaycasts);
        Assert.Equal(StatisticsPanelTab.Overview, RetainedOverviewFastestExtractionRowPolicy.OwnerTab);
        Assert.Equal(RetainedOverviewRightPanelPolicy.ContentName,
            RetainedOverviewFastestExtractionRowPolicy.ParentName);
    }

    [Fact]
    public void GateFifteenKeepsGateFourteenAndOtherAcceptedTabBehaviorUnchanged()
    {
        var projection = CreateGateFifteenProjection();
        var fastest = FastestExtractionHighlightPresentationFactory.Create(
            projection,
            key => UiText.EnglishFallbacks[key]);
        var all = OverviewHighlightsPresentationFactory.Create(
            projection,
            key => UiText.EnglishFallbacks[key]);

        Assert.Equal(OverviewHighlightMetric.FastestExtraction, fastest.Metric);
        Assert.Equal("Fastest extraction", fastest.Label);
        Assert.Equal("01:04.083 - Ground Zero", fastest.Value);
        Assert.Equal(fastest.Label, all[0].Label);
        Assert.Equal(fastest.Value, all[0].Value);
        Assert.Equal(new[]
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
        }, PanelInteractionState.NavigationOrder);
        Assert.Equal(9, RetainedTabStripPolicy.Specifications.Count);
        Assert.Equal(StatisticsPanelTab.Overview, RetainedOverviewFastestExtractionRowPolicy.OwnerTab);
    }

    [Fact]
    public void GateSixteenLatestRunHeadingUsesAcceptedHighlightsTypographyAndStructuralLayout()
    {
        var layout = CreateRetainedVisualLayout(2560f, 1440f);
        var heading = layout.OverviewLatestRunHeading;
        var finalHighlightRow = layout.OverviewHighlightRows[^1];

        Assert.Equal("OverviewLatestRunHeading", RetainedOverviewLatestRunHeadingPolicy.Name);
        Assert.Equal(
            RetainedOverviewRightPanelPolicy.ContentName,
            RetainedOverviewLatestRunHeadingPolicy.ParentName);
        Assert.Equal(StatisticsPanelTab.Overview, RetainedOverviewLatestRunHeadingPolicy.OwnerTab);
        Assert.Equal("ui.overview_latest_run", RetainedOverviewLatestRunHeadingPolicy.TextKey);
        Assert.Equal("Latest run", RetainedOverviewLatestRunHeadingPolicy.EnglishFallback);
        Assert.Equal(
            RetainedOverviewLatestRunHeadingPolicy.EnglishFallback,
            UiText.EnglishFallbacks[RetainedOverviewLatestRunHeadingPolicy.TextKey]);
        Assert.Equal(40f, RetainedOverviewLatestRunHeadingPolicy.TopMarginPixels);

        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.FontAssetName,
            RetainedOverviewLatestRunHeadingPolicy.FontAssetName);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.SourceMaterialName,
            RetainedOverviewLatestRunHeadingPolicy.SourceMaterialName);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.MaterialName,
            RetainedOverviewLatestRunHeadingPolicy.MaterialName);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.ReferenceFontSize,
            RetainedOverviewLatestRunHeadingPolicy.ReferenceFontSize);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.HeightPixels,
            RetainedOverviewLatestRunHeadingPolicy.HeightPixels);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.ReferenceOpticalOffsetX,
            RetainedOverviewLatestRunHeadingPolicy.ReferenceOpticalOffsetX);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.ReferenceOpticalOffsetY,
            RetainedOverviewLatestRunHeadingPolicy.ReferenceOpticalOffsetY);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.CharacterSpacing,
            RetainedOverviewLatestRunHeadingPolicy.CharacterSpacing);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.WordSpacing,
            RetainedOverviewLatestRunHeadingPolicy.WordSpacing);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.LineSpacing,
            RetainedOverviewLatestRunHeadingPolicy.LineSpacing);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.ParagraphSpacing,
            RetainedOverviewLatestRunHeadingPolicy.ParagraphSpacing);
        Assert.Equal(RetainedOverviewHighlightsHeadingPolicy.Red, RetainedOverviewLatestRunHeadingPolicy.Red);
        Assert.Equal(RetainedOverviewHighlightsHeadingPolicy.Green, RetainedOverviewLatestRunHeadingPolicy.Green);
        Assert.Equal(RetainedOverviewHighlightsHeadingPolicy.Blue, RetainedOverviewLatestRunHeadingPolicy.Blue);
        Assert.Equal(RetainedOverviewHighlightsHeadingPolicy.Alpha, RetainedOverviewLatestRunHeadingPolicy.Alpha);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.WordWrapping,
            RetainedOverviewLatestRunHeadingPolicy.WordWrapping);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.AutoSizing,
            RetainedOverviewLatestRunHeadingPolicy.AutoSizing);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.UsesTopLeftAlignment,
            RetainedOverviewLatestRunHeadingPolicy.UsesTopLeftAlignment);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.UsesOwnedTabLabelMaterial,
            RetainedOverviewLatestRunHeadingPolicy.UsesOwnedTabLabelMaterial);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.UsesZeroTextMargin,
            RetainedOverviewLatestRunHeadingPolicy.UsesZeroTextMargin);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.AdditionalPaddingPixels,
            RetainedOverviewLatestRunHeadingPolicy.AdditionalPaddingPixels);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.UsesFixedOpticalOffset,
            RetainedOverviewLatestRunHeadingPolicy.UsesFixedOpticalOffset);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.UsesHorizontalScaleCompensation,
            RetainedOverviewLatestRunHeadingPolicy.UsesHorizontalScaleCompensation);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.UsesNormalStyle,
            RetainedOverviewLatestRunHeadingPolicy.UsesNormalStyle);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.UsesRegularWeight,
            RetainedOverviewLatestRunHeadingPolicy.UsesRegularWeight);
        Assert.Equal(
            RetainedOverviewHighlightsHeadingPolicy.UsesVisibleOverflow,
            RetainedOverviewLatestRunHeadingPolicy.UsesVisibleOverflow);
        Assert.False(RetainedOverviewLatestRunHeadingPolicy.BlocksRaycasts);

        Assert.Same(layout.ReferenceTransform, heading.ReferenceTransform);
        Assert.Equal(1330f, heading.Left);
        Assert.Equal(790f, heading.Top);
        Assert.Equal(1115f, heading.Width);
        Assert.Equal(60f, heading.Height);
        Assert.Equal(46.3f, heading.FontSize);
        Assert.Equal(-5f, heading.OpticalOffsetX);
        Assert.Equal(19f, heading.OpticalOffsetY);
        Assert.Equal(layout.OverviewRightPanel.ContentLeft, heading.Left);
        Assert.Equal(layout.OverviewRightPanel.ContentWidth, heading.Width);
        Assert.Equal(
            finalHighlightRow.Top + finalHighlightRow.Height + 40f,
            heading.Top);
    }

    [Theory]
    [InlineData(1280f, 720f)]
    [InlineData(1680f, 1050f)]
    [InlineData(1920f, 1200f)]
    [InlineData(2560f, 1440f)]
    public void GateSixteenLatestRunHeadingRelationshipUsesSharedReferenceTransform(
        float viewportWidth,
        float viewportHeight)
    {
        var layout = CreateRetainedVisualLayout(viewportWidth, viewportHeight);
        var heading = layout.OverviewLatestRunHeading;
        var finalHighlightRow = layout.OverviewHighlightRows[^1];

        Assert.Same(layout.ReferenceTransform, heading.ReferenceTransform);
        Assert.Equal(layout.OverviewRightPanel.ContentLeft, heading.Left, 5);
        Assert.Equal(layout.OverviewRightPanel.ContentWidth, heading.Width, 5);
        Assert.Equal(
            layout.ReferenceTransform.CanvasLength(40f),
            heading.Top - finalHighlightRow.Top - finalHighlightRow.Height,
            5);
        Assert.Equal(layout.ReferenceTransform.CanvasLength(60f), heading.Height, 5);
        Assert.Equal(layout.ReferenceTransform.CanvasLength(46.3f), heading.FontSize, 5);
        Assert.Equal(layout.ReferenceTransform.CanvasLength(-5f), heading.OpticalOffsetX, 5);
        Assert.Equal(layout.ReferenceTransform.CanvasLength(19f), heading.OpticalOffsetY, 5);
    }

    [Fact]
    public void GateSixteenLatestRunHeadingRemainsChildlessAndNonInteractive()
    {
        Assert.Equal(14, RetainedShellCompositionPolicy.RootChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewLatestRunHeadingChildCount);
        Assert.Equal(10, RetainedShellCompositionPolicy.ButtonCount);
        Assert.Equal(4, RetainedShellCompositionPolicy.OverviewHighlightRowCount);
        Assert.Equal(2, RetainedShellCompositionPolicy.OverviewHighlightRowChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.OverviewHighlightRowGraphicCount);
        Assert.False(RetainedOverviewLatestRunHeadingPolicy.BlocksRaycasts);
    }

    [Fact]
    public void GateSeventeenLatestRunCardUsesExactStructuralGeometryAndRoundedPanelStyle()
    {
        var layout = CreateRetainedVisualLayout(2560f, 1440f);
        var card = layout.OverviewLatestRunCard;
        var heading = layout.OverviewLatestRunHeading;

        Assert.Equal("OverviewLatestRunCard", RetainedOverviewLatestRunCardPolicy.Name);
        Assert.Equal(
            RetainedOverviewRightPanelPolicy.ContentName,
            RetainedOverviewLatestRunCardPolicy.ParentName);
        Assert.Equal(StatisticsPanelTab.Overview, RetainedOverviewLatestRunCardPolicy.OwnerTab);
        Assert.Equal(56f, RetainedOverviewLatestRunCardPolicy.HeadingTopOffsetPixels);
        Assert.Equal(1330f, RetainedOverviewLatestRunCardPolicy.LeftPixels);
        Assert.Equal(846f, RetainedOverviewLatestRunCardPolicy.TopPixels);
        Assert.Equal(1888f, RetainedOverviewLatestRunCardPolicy.RightExclusivePixels);
        Assert.Equal(1185f, RetainedOverviewLatestRunCardPolicy.BottomExclusivePixels);
        Assert.Equal(558f, RetainedOverviewLatestRunCardPolicy.WidthPixels);
        Assert.Equal(339f, RetainedOverviewLatestRunCardPolicy.HeightPixels);
        Assert.Equal(0f, RetainedOverviewLatestRunCardPolicy.Red);
        Assert.Equal(0f, RetainedOverviewLatestRunCardPolicy.Green);
        Assert.Equal(0f, RetainedOverviewLatestRunCardPolicy.Blue);
        Assert.Equal(0.50f, RetainedOverviewLatestRunCardPolicy.LayerAlpha);
        Assert.Equal(20f, RetainedOverviewLatestRunCardPolicy.CornerRadiusPixels);
        Assert.Equal(0f, RetainedOverviewLatestRunCardPolicy.BorderWidth);
        Assert.False(RetainedOverviewLatestRunCardPolicy.HasSprite);
        Assert.True(RetainedOverviewLatestRunCardPolicy.UsesSimpleImageType);
        Assert.False(RetainedOverviewLatestRunCardPolicy.BlocksRaycasts);
        Assert.False(RetainedOverviewLatestRunCardPolicy.HasInteraction);
        Assert.False(RetainedOverviewLatestRunCardPolicy.HasShadow);
        Assert.Equal(RetainedOverviewPanelStylePolicy.Red, RetainedOverviewLatestRunCardPolicy.Red);
        Assert.Equal(RetainedOverviewPanelStylePolicy.Green, RetainedOverviewLatestRunCardPolicy.Green);
        Assert.Equal(RetainedOverviewPanelStylePolicy.Blue, RetainedOverviewLatestRunCardPolicy.Blue);
        Assert.Equal(RetainedOverviewPanelStylePolicy.LayerAlpha, RetainedOverviewLatestRunCardPolicy.LayerAlpha);
        Assert.Equal(
            RetainedOverviewPanelStylePolicy.CornerRadiusPixels,
            RetainedOverviewLatestRunCardPolicy.CornerRadiusPixels);
        Assert.Equal(
            RetainedOverviewPanelStylePolicy.BlocksRaycasts,
            RetainedOverviewLatestRunCardPolicy.BlocksRaycasts);

        Assert.Same(layout.ReferenceTransform, card.ReferenceTransform);
        Assert.Equal(1330f, card.Left);
        Assert.Equal(846f, card.Top);
        Assert.Equal(558f, card.Width);
        Assert.Equal(339f, card.Height);
        Assert.Equal(20f, card.CornerRadius);
        Assert.Equal(layout.OverviewRightPanel.ContentLeft, card.Left);
        Assert.Equal(heading.Top + 56f, card.Top);
        Assert.Equal(1888f, card.Left + card.Width);
        Assert.Equal(1185f, card.Top + card.Height);
    }

    [Theory]
    [InlineData(1280f, 720f)]
    [InlineData(1680f, 1050f)]
    [InlineData(1920f, 1200f)]
    [InlineData(2560f, 1440f)]
    public void GateSeventeenLatestRunCardUsesSharedReferenceTransform(
        float viewportWidth,
        float viewportHeight)
    {
        var layout = CreateRetainedVisualLayout(viewportWidth, viewportHeight);
        var card = layout.OverviewLatestRunCard;

        Assert.Same(layout.ReferenceTransform, card.ReferenceTransform);
        Assert.Equal(layout.OverviewRightPanel.ContentLeft, card.Left, 5);
        Assert.Equal(
            layout.ReferenceTransform.CanvasLength(56f),
            card.Top - layout.OverviewLatestRunHeading.Top,
            5);
        Assert.Equal(layout.ReferenceTransform.CanvasLength(558f), card.Width, 5);
        Assert.Equal(layout.ReferenceTransform.CanvasLength(339f), card.Height, 5);
        Assert.Equal(layout.ReferenceTransform.CanvasLength(20f), card.CornerRadius, 5);
    }

    [Fact]
    public void GateSeventeenCompositionAddsOnlyOneEmptyNonInteractiveCardGraphic()
    {
        Assert.Equal(14, RetainedShellCompositionPolicy.RootChildCount);
        Assert.Equal(7, RetainedShellCompositionPolicy.OverviewRightPanelContentChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewLatestRunCardChildCount);
        Assert.Equal(76, RetainedShellCompositionPolicy.GraphicCount);
        Assert.Equal(10, RetainedShellCompositionPolicy.ButtonCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewLatestRunHeadingChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewHighlightsHeadingChildCount);
        Assert.Equal(4, RetainedShellCompositionPolicy.OverviewHighlightRowCount);
        Assert.Equal(2, RetainedShellCompositionPolicy.OverviewHighlightRowChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.OverviewHighlightRowGraphicCount);
        Assert.False(RetainedOverviewLatestRunCardPolicy.HasInteraction);
        Assert.False(RetainedOverviewLatestRunCardPolicy.BlocksRaycasts);
    }

    [Fact]
    public void GateEighteenExtractedBadgeMapsExistingTextAndExactVisualPolicy()
    {
        Assert.Equal("ui.extracted_runs", RetainedExtractedRunBadgePolicy.TextKey);
        Assert.Equal("Extracted", RetainedExtractedRunBadgePolicy.EnglishFallback);
        Assert.Equal(
            "Extracted",
            UiText.EnglishFallbacks[RetainedExtractedRunBadgePolicy.TextKey]);
        Assert.Equal("Extracted", UiText.Resolve(RetainedExtractedRunBadgePolicy.TextKey, _ => null));
        Assert.Equal(109f / 255f, RetainedExtractedRunBadgePolicy.BackgroundRed);
        Assert.Equal(197f / 255f, RetainedExtractedRunBadgePolicy.BackgroundGreen);
        Assert.Equal(75f / 255f, RetainedExtractedRunBadgePolicy.BackgroundBlue);
        Assert.Equal(1f, RetainedExtractedRunBadgePolicy.BackgroundAlpha);
        Assert.Equal(30f, RetainedExtractedRunBadgePolicy.HeightPixels);
        Assert.Equal(5f, RetainedExtractedRunBadgePolicy.CornerRadiusPixels);
        Assert.Equal(5f, RetainedExtractedRunBadgePolicy.LeftPaddingPixels);
        Assert.Equal(19f, RetainedExtractedRunBadgePolicy.IconWidthPixels);
        Assert.Equal(14f, RetainedExtractedRunBadgePolicy.IconHeightPixels);
        Assert.Equal(5f, RetainedExtractedRunBadgePolicy.IconTextGapPixels);
        Assert.Equal(5f, RetainedExtractedRunBadgePolicy.RightPaddingPixels);
        Assert.Equal(98f, RetainedExtractedRunBadgePolicy.MockEquivalentPreferredLabelWidthPixels);
        Assert.Equal(132f, RetainedExtractedRunBadgePolicy.MockReferenceWidthPixels);
    }

    [Fact]
    public void GateEighteenExtractedBadgeUsesAcceptedNativeTypographyAndSeparateCheckIcon()
    {
        Assert.Equal(
            RetainedHeaderTitlePolicy.FontAssetName,
            RetainedExtractedRunBadgePolicy.FontAssetName);
        Assert.Equal(
            RetainedHeaderTitlePolicy.MaterialName,
            RetainedExtractedRunBadgePolicy.SourceMaterialName);
        Assert.Equal(
            RetainedTabLabelShadowPolicy.OwnedMaterialName,
            RetainedExtractedRunBadgePolicy.MaterialName);
        Assert.Equal(19.8f, RetainedExtractedRunBadgePolicy.ReferenceFontSize);
        Assert.Equal(0f, RetainedExtractedRunBadgePolicy.CharacterSpacing);
        Assert.Equal(0f, RetainedExtractedRunBadgePolicy.WordSpacing);
        Assert.Equal(0f, RetainedExtractedRunBadgePolicy.LineSpacing);
        Assert.Equal(0f, RetainedExtractedRunBadgePolicy.ParagraphSpacing);
        Assert.False(RetainedExtractedRunBadgePolicy.WordWrapping);
        Assert.False(RetainedExtractedRunBadgePolicy.AutoSizing);
        Assert.True(RetainedExtractedRunBadgePolicy.UsesVisibleOverflow);
        Assert.True(RetainedExtractedRunBadgePolicy.UsesZeroTextMargins);
        Assert.True(RetainedExtractedRunBadgePolicy.UsesLeftAlignment);
        Assert.True(RetainedExtractedRunBadgePolicy.UsesVerticalCentering);
        Assert.True(RetainedExtractedRunBadgePolicy.UsesNormalStyle);
        Assert.True(RetainedExtractedRunBadgePolicy.UsesRegularWeight);
        Assert.True(RetainedExtractedRunBadgePolicy.UsesOwnedSubtleShadowMaterial);
        Assert.True(RetainedExtractedRunBadgePolicy.IconIsLogicallySeparate);
        Assert.Equal("\u2713", RetainedExtractedRunBadgePolicy.PreferredIconGlyph);
        Assert.True(RetainedExtractedRunBadgePolicy.PrefersNativeTmpCheckGlyph);
        Assert.True(RetainedExtractedRunBadgePolicy.HasProceduralCheckMarkFallback);
        Assert.True(RetainedExtractedRunBadgePolicy.IconUsesOwnedSubtleShadowMaterialWhenSupported);
        Assert.Equal(1f, RetainedExtractedRunBadgePolicy.TextRed);
        Assert.Equal(1f, RetainedExtractedRunBadgePolicy.TextGreen);
        Assert.Equal(1f, RetainedExtractedRunBadgePolicy.TextBlue);
        Assert.Equal(1f, RetainedExtractedRunBadgePolicy.TextAlpha);
        Assert.Equal(1f, RetainedExtractedRunBadgePolicy.IconRed);
        Assert.Equal(1f, RetainedExtractedRunBadgePolicy.IconGreen);
        Assert.Equal(1f, RetainedExtractedRunBadgePolicy.IconBlue);
        Assert.Equal(1f, RetainedExtractedRunBadgePolicy.IconAlpha);
    }

    [Fact]
    public void GateEighteenExtractedBadgeWidthIsContentDrivenWithoutReferenceWidthRejection()
    {
        var transform = RetainedReferenceTransformPolicy.Create(2560f, 1440f, 1f);
        var mockEquivalent = RetainedExtractedRunBadgePolicy.CreateCanvasLayout(
            transform,
            RetainedExtractedRunBadgePolicy.MockEquivalentPreferredLabelWidthPixels);
        var shorter = RetainedExtractedRunBadgePolicy.CreateCanvasLayout(transform, 70f);
        var longer = RetainedExtractedRunBadgePolicy.CreateCanvasLayout(transform, 170f);

        Assert.Equal(132f, mockEquivalent.Width, 5);
        Assert.Equal(98f, mockEquivalent.ReferencePreferredLabelWidth);
        Assert.Equal(104f, shorter.Width, 5);
        Assert.Equal(204f, longer.Width, 5);
        Assert.Equal(100f, longer.Width - shorter.Width, 5);
        Assert.Equal(shorter.LabelWidth + 34f, shorter.Width, 5);
        Assert.Equal(longer.LabelWidth + 34f, longer.Width, 5);
    }

    [Theory]
    [InlineData(1280f, 720f)]
    [InlineData(1680f, 1050f)]
    [InlineData(1920f, 1200f)]
    [InlineData(2560f, 1440f)]
    public void GateEighteenExtractedBadgeGeometryUsesSharedReferenceTransform(
        float viewportWidth,
        float viewportHeight)
    {
        var transform = RetainedReferenceTransformPolicy.Create(viewportWidth, viewportHeight, 1f);
        var badge = RetainedExtractedRunBadgePolicy.CreateCanvasLayout(
            transform,
            RetainedExtractedRunBadgePolicy.MockEquivalentPreferredLabelWidthPixels);

        Assert.Same(transform, badge.ReferenceTransform);
        Assert.Equal(transform.CanvasLength(132f), badge.Width, 5);
        Assert.Equal(transform.CanvasLength(30f), badge.Height, 5);
        Assert.Equal(transform.CanvasLength(5f), badge.CornerRadius, 5);
        Assert.Equal(transform.CanvasLength(5f), badge.LeftPadding, 5);
        Assert.Equal(badge.LeftPadding, badge.IconLeft, 5);
        Assert.Equal(transform.CanvasLength(8f), badge.IconTop, 5);
        Assert.Equal(transform.CanvasLength(19f), badge.IconWidth, 5);
        Assert.Equal(transform.CanvasLength(14f), badge.IconHeight, 5);
        Assert.Equal(transform.CanvasLength(5f), badge.IconTextGap, 5);
        Assert.Equal(transform.CanvasLength(29f), badge.LabelLeft, 5);
        Assert.Equal(0f, badge.LabelTop, 5);
        Assert.Equal(transform.CanvasLength(98f), badge.LabelWidth, 5);
        Assert.Equal(badge.Height, badge.LabelHeight, 5);
        Assert.Equal(transform.CanvasLength(5f), badge.RightPadding, 5);
        Assert.Equal(transform.CanvasLength(19.8f), badge.FontSize, 5);
        Assert.Equal(badge.Width, badge.LabelLeft + badge.LabelWidth + badge.RightPadding, 5);
    }

    [Fact]
    public void GateEighteenExtractedBadgeIsNonInteractiveAndDoesNotChangeOverviewComposition()
    {
        Assert.False(RetainedExtractedRunBadgePolicy.BackgroundBlocksRaycasts);
        Assert.False(RetainedExtractedRunBadgePolicy.IconBlocksRaycasts);
        Assert.False(RetainedExtractedRunBadgePolicy.LabelBlocksRaycasts);
        Assert.False(RetainedExtractedRunBadgePolicy.HasInteraction);
        Assert.Equal(7, RetainedShellCompositionPolicy.OverviewRightPanelContentChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewLatestRunCardChildCount);
        Assert.Equal(76, RetainedShellCompositionPolicy.GraphicCount);
        Assert.Equal(10, RetainedShellCompositionPolicy.ButtonCount);
        Assert.DoesNotContain(
            typeof(RetainedVisualCanvasLayout).GetProperties(),
            property => property.Name.Contains("RunBadge", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(RetainedExtractedRunBadgePolicy).Assembly.GetTypes(),
            type => type.Name is "RetainedDiedRunBadgePolicy" or "RetainedUnknownRunBadgePolicy");
    }

    [Fact]
    public void GateTenOverviewProfileSummaryLocalizationNeverDependsOnEnglishWidth()
    {
        const string localized = "Ausführliche Profilzusammenfassung für diesen Spielstand";

        Assert.Equal(
            localized,
            UiText.Resolve(
                RetainedOverviewProfileSummaryHeadingPolicy.TextKey,
                key => key == RetainedOverviewProfileSummaryHeadingPolicy.TextKey ? localized : null));
        Assert.Equal(
            RetainedOverviewProfileSummaryHeadingPolicy.EnglishFallback,
            UiText.Resolve(RetainedOverviewProfileSummaryHeadingPolicy.TextKey, _ => null));
    }

    [Fact]
    public void GateTenOverviewHeadingInheritsTheOverviewOwnedViewVisibility()
    {
        var overviewView = new object();
        var visibilityStates = new List<bool>();
        var visibility = new RetainedTabViewVisibility<object>(
            overviewView,
            RetainedOverviewPanelStylePolicy.OwnerTab,
            (_, visible) => visibilityStates.Add(visible));

        foreach (var tab in PanelInteractionState.NavigationOrder) visibility.Apply(tab);
        visibility.Apply(StatisticsPanelTab.Overview);

        Assert.Equal(RetainedOverviewLeftPanelPolicy.ContentName, RetainedOverviewProfileSummaryHeadingPolicy.ParentName);
        Assert.Equal(StatisticsPanelTab.Overview, visibility.OwnerTab);
        Assert.Equal(PanelInteractionState.NavigationOrder.Count + 1, visibilityStates.Count);
        Assert.True(visibilityStates[0]);
        Assert.All(
            visibilityStates.Skip(1).Take(PanelInteractionState.NavigationOrder.Count - 1),
            visible => Assert.False(visible));
        Assert.True(visibilityStates[^1]);
    }

    [Fact]
    public void GateSevenMeasuredWidthDifferencesPropagateRelationallyWithoutOverlap()
    {
        var originalWidths = RetainedTabStripPolicy.AuditedEnglishPreferredWidths.ToArray();
        var changedWidths = originalWidths.ToArray();
        changedWidths[1] += 0.125f;
        var transform = RetainedReferenceTransformPolicy.Create(2560f, 1440f, 1f);
        var original = RetainedTabStripPolicy.CreateCanvasLayout(transform, originalWidths);
        var changed = RetainedTabStripPolicy.CreateCanvasLayout(transform, changedWidths);

        Assert.Equal(original.Tabs[0].Left, changed.Tabs[0].Left);
        Assert.Equal(original.Tabs[0].Width, changed.Tabs[0].Width);
        Assert.Equal(original.Tabs[1].Left, changed.Tabs[1].Left);
        Assert.Equal(original.Tabs[1].Width + 0.125f, changed.Tabs[1].Width, 3);
        for (var index = 2; index < changed.Tabs.Count; index++)
            Assert.Equal(original.Tabs[index].Left + 0.125f, changed.Tabs[index].Left, 3);
        for (var index = 1; index < changed.Tabs.Count; index++)
            Assert.Equal(changed.Tabs[index - 1].Left + changed.Tabs[index - 1].Width + 10f, changed.Tabs[index].Left, 3);
    }

    [Fact]
    public void GateSevenGeometryUsesSharedTransformAtEveryRequiredViewportAndCanvasScale()
    {
        var viewports = new[]
        {
            (Width: 1280f, Height: 720f),
            (Width: 1680f, Height: 1050f),
            (Width: 1920f, Height: 1080f),
            (Width: 1920f, Height: 1200f),
            (Width: 2560f, Height: 1440f)
        };
        foreach (var viewport in viewports)
        {
            var baseline = RetainedVisualLayoutPolicy.Create(
                RetainedReferenceTransformPolicy.Create(viewport.Width, viewport.Height, 1f),
                RetainedTabStripPolicy.AuditedEnglishPreferredWidths);
            foreach (var canvasScaleFactor in new[] { 1f, 2f, 3f })
            {
                var layout = RetainedVisualLayoutPolicy.Create(
                    RetainedReferenceTransformPolicy.Create(
                        viewport.Width,
                        viewport.Height,
                        canvasScaleFactor),
                    RetainedTabStripPolicy.AuditedEnglishPreferredWidths);
                Assert.Equal(9, layout.TabStrip.Tabs.Count);
                for (var index = 0; index < layout.TabStrip.Tabs.Count; index++)
                {
                    var expected = baseline.TabStrip.Tabs[index];
                    var actual = layout.TabStrip.Tabs[index];
                    Assert.Equal(expected.Left, actual.Left * canvasScaleFactor, 2);
                    Assert.Equal(expected.Top, actual.Top * canvasScaleFactor, 2);
                    Assert.Equal(expected.Width, actual.Width * canvasScaleFactor, 2);
                    Assert.Equal(expected.Height, actual.Height * canvasScaleFactor, 2);
                    Assert.Equal(expected.LeftPadding, actual.LeftPadding * canvasScaleFactor, 2);
                    Assert.Equal(expected.RightPadding, actual.RightPadding * canvasScaleFactor, 2);
                    Assert.Equal(expected.FontSize, actual.FontSize * canvasScaleFactor, 2);
                    if (index > 0)
                    {
                        Assert.Equal(
                            10f * layout.ReferenceTransform.ReferenceScale,
                            (actual.Left - layout.TabStrip.Tabs[index - 1].Left
                             - layout.TabStrip.Tabs[index - 1].Width) * canvasScaleFactor,
                            2);
                    }
                }
            }
        }
    }

    [Fact]
    public void GateSevenEveryActivationSelectsItsSpecifiedTab()
    {
        var interaction = new PanelInteractionState();
        foreach (var specification in RetainedTabStripPolicy.Specifications)
        {
            interaction.SelectTab(StatisticsPanelTab.Overview);
            var observed = new List<StatisticsPanelTab>();
            var activation = new RetainedTabActivation(
                tab => RetainedTabSelectionPolicy.SelectAndSynchronize(
                    interaction,
                    observed.Add,
                    tab),
                specification.Tab);

            activation.Invoke();

            Assert.Equal(specification.Tab, interaction.SelectedTab);
            Assert.Equal(specification.Tab, Assert.Single(observed));
        }
    }

    [Fact]
    public void GateSevenKeyboardCyclingKeepsExactlyOneOfNineBackgroundsSelected()
    {
        var interaction = new PanelInteractionState();
        var targets = RetainedTabStripPolicy.Specifications.Select(_ => new object()).ToArray();
        var colors = new Dictionary<object, RetainedRgbaColor>();
        var states = RetainedTabStripPolicy.Specifications
            .Select((specification, index) => new RetainedTabVisualState<object>(
                targets[index],
                specification.Tab,
                (target, color) => colors[target] = color))
            .ToArray();

        void AssertSynchronized()
        {
            foreach (var state in states) state.Apply(interaction.SelectedTab);
            Assert.Equal(1, colors.Values.Count(color => color.Alpha == 1f));
            for (var index = 0; index < states.Length; index++)
            {
                var expected = RetainedTabVisualStatePolicy.Resolve(
                    interaction.SelectedTab,
                    RetainedTabStripPolicy.Specifications[index].Tab);
                var actual = colors[targets[index]];
                Assert.True(RetainedTabVisualStatePolicy.IsExactColor(
                    expected,
                    actual.Red,
                    actual.Green,
                    actual.Blue,
                    actual.Alpha));
            }
        }

        AssertSynchronized();
        for (var step = 0; step < 9; step++)
        {
            interaction.MoveTab(1);
            AssertSynchronized();
        }
        Assert.Equal(StatisticsPanelTab.Overview, interaction.SelectedTab);
        for (var step = 0; step < 9; step++)
        {
            interaction.MoveTab(-1);
            AssertSynchronized();
        }
        Assert.Equal(StatisticsPanelTab.Overview, interaction.SelectedTab);
    }

    [Fact]
    public void GateSevenSelectionReusesOneMaterialAndListenerLeaseDisposesOnce()
    {
        var createdMaterials = 0;
        var destroyedMaterials = 0;
        var source = new RetainedMaterialProbe(new object(), 1f);
        var material = RetainedOwnedResource<RetainedMaterialProbe>.CreatePrivateClone(
            source,
            original =>
            {
                createdMaterials++;
                return original.Clone(RetainedTabLabelShadowPolicy.Alpha);
            },
            _ => destroyedMaterials++);
        var assignedMaterials = RetainedTabStripPolicy.Specifications
            .Select(_ => material.Resource)
            .ToArray();
        var listenerRemovals = new int[RetainedShellCompositionPolicy.TabCount];
        var listeners = new RetainedListenerLease();
        for (var index = 0; index < listenerRemovals.Length; index++)
        {
            var capturedIndex = index;
            listeners.Register(() => listenerRemovals[capturedIndex]++);
        }

        var interaction = new PanelInteractionState();
        for (var step = 0; step < 50; step++) interaction.MoveTab(1);

        Assert.Equal(1, createdMaterials);
        Assert.All(assignedMaterials, assigned => Assert.Same(material.Resource, assigned));
        Assert.Equal(9, listeners.Count);
        Assert.Equal(0, destroyedMaterials);
        listeners.Dispose();
        listeners.Dispose();
        material.Dispose();
        material.Dispose();

        Assert.True(listeners.IsDisposed);
        Assert.Equal(0, listeners.Count);
        Assert.All(listenerRemovals, removals => Assert.Equal(1, removals));
        Assert.Equal(1, destroyedMaterials);
    }

    [Fact]
    public void GateEightCompositionRetainsTheAcceptedTabStripAndAddsOnlyOverviewContent()
    {
        Assert.Equal(9, RetainedShellCompositionPolicy.TabCount);
        Assert.Equal(14, RetainedShellCompositionPolicy.RootChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.TabChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.TabLabelChildCount);
        Assert.Equal(76, RetainedShellCompositionPolicy.GraphicCount);
        Assert.Equal(10, RetainedShellCompositionPolicy.ButtonCount);
        Assert.Equal(9, RetainedShellCompositionPolicy.OnlyOneEdgeModifierCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.RectMaskCount);
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
    public void GateEightViewportChangeRefreshesAllRetainedVisualsWithoutHierarchyDuplication()
    {
        var baseline = CreateRetainedVisualLayout(2560f, 1440f);
        var resized = CreateRetainedVisualLayout(1280f, 720f);

        Assert.NotSame(baseline.ReferenceTransform, resized.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.Header.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.OverviewTab.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.HeaderBottomBar.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.BackControl.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.HeaderTitle.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.OverviewLeftPanel.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.OverviewRightPanel.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.OverviewProfileSummaryHeading.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.OverviewHighlightsHeading.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.OverviewFastestExtractionRow.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.OverviewFastestExtractionEntry.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.OverviewFirstStatisticsRow.ReferenceTransform);
        Assert.Same(resized.ReferenceTransform, resized.OverviewFirstStatisticsRowEntry.ReferenceTransform);
        Assert.Equal(baseline.Header.Width / 2f, resized.Header.Width);
        Assert.Equal(baseline.OverviewTab.Width / 2f, resized.OverviewTab.Width);
        Assert.Equal(baseline.OverviewTab.Height / 2f, resized.OverviewTab.Height);
        Assert.Equal(baseline.OverviewTab.FontSize / 2f, resized.OverviewTab.FontSize);
        Assert.Equal(baseline.HeaderBottomBar.Width / 2f, resized.HeaderBottomBar.Width);
        Assert.Equal(baseline.HeaderBottomBar.Height / 2f, resized.HeaderBottomBar.Height);
        Assert.Equal(baseline.BackControl.Width / 2f, resized.BackControl.Width);
        Assert.Equal(baseline.HeaderTitle.Width / 2f, resized.HeaderTitle.Width);
        Assert.Equal(baseline.HeaderTitle.FontSize / 2f, resized.HeaderTitle.FontSize);
        Assert.Equal(baseline.OverviewLeftPanel.Width / 2f, resized.OverviewLeftPanel.Width);
        Assert.Equal(baseline.OverviewLeftPanel.Height / 2f, resized.OverviewLeftPanel.Height);
        Assert.Equal(baseline.OverviewRightPanel.Width / 2f, resized.OverviewRightPanel.Width);
        Assert.Equal(baseline.OverviewRightPanel.Height / 2f, resized.OverviewRightPanel.Height);
        Assert.Equal(
            baseline.OverviewProfileSummaryHeading.Width / 2f,
            resized.OverviewProfileSummaryHeading.Width);
        Assert.Equal(
            baseline.OverviewProfileSummaryHeading.FontSize / 2f,
            resized.OverviewProfileSummaryHeading.FontSize);
        Assert.Equal(
            baseline.OverviewHighlightsHeading.Width / 2f,
            resized.OverviewHighlightsHeading.Width);
        Assert.Equal(
            baseline.OverviewHighlightsHeading.FontSize / 2f,
            resized.OverviewHighlightsHeading.FontSize);
        Assert.Equal(
            baseline.OverviewFastestExtractionRow.Width / 2f,
            resized.OverviewFastestExtractionRow.Width);
        Assert.Equal(
            baseline.OverviewFastestExtractionRow.Height / 2f,
            resized.OverviewFastestExtractionRow.Height);
        Assert.Equal(
            baseline.OverviewFastestExtractionEntry.LabelWidth / 2f,
            resized.OverviewFastestExtractionEntry.LabelWidth);
        Assert.Equal(
            baseline.OverviewFastestExtractionEntry.ValueWidth / 2f,
            resized.OverviewFastestExtractionEntry.ValueWidth);
        Assert.Equal(
            baseline.OverviewFastestExtractionEntry.FontSize / 2f,
            resized.OverviewFastestExtractionEntry.FontSize);
        Assert.Equal(
            baseline.OverviewFirstStatisticsRow.Width / 2f,
            resized.OverviewFirstStatisticsRow.Width);
        Assert.Equal(
            baseline.OverviewFirstStatisticsRow.Height / 2f,
            resized.OverviewFirstStatisticsRow.Height);
        Assert.Equal(
            baseline.OverviewFirstStatisticsRow.CornerRadius / 2f,
            resized.OverviewFirstStatisticsRow.CornerRadius);
        Assert.Equal(
            baseline.OverviewFirstStatisticsRow.ContentWidth / 2f,
            resized.OverviewFirstStatisticsRow.ContentWidth);
        Assert.Equal(
            baseline.OverviewFirstStatisticsRow.ContentHeight / 2f,
            resized.OverviewFirstStatisticsRow.ContentHeight);
        Assert.Equal(
            baseline.OverviewFirstStatisticsRowEntry.LabelWidth / 2f,
            resized.OverviewFirstStatisticsRowEntry.LabelWidth);
        Assert.Equal(
            baseline.OverviewFirstStatisticsRowEntry.ValueWidth / 2f,
            resized.OverviewFirstStatisticsRowEntry.ValueWidth);
        Assert.Equal(
            baseline.OverviewFirstStatisticsRowEntry.FontSize / 2f,
            resized.OverviewFirstStatisticsRowEntry.FontSize);
        Assert.Equal(14, RetainedShellCompositionPolicy.RootChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.HeaderChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.OverviewTabChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewTabLabelChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.HeaderBottomBarChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.HeaderBottomBarGraphicChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.HeaderTitleChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.BackButtonChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.BackArrowChildCount);
        Assert.Equal(2, RetainedShellCompositionPolicy.OverviewContentViewChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.OverviewLeftPanelChildCount);
        Assert.Equal(12, RetainedShellCompositionPolicy.OverviewLeftPanelContentChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewProfileSummaryHeadingChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.OverviewFirstStatisticsRowChildCount);
        Assert.Equal(2, RetainedShellCompositionPolicy.OverviewFirstStatisticsRowContentChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewFirstStatisticsRowLabelChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewFirstStatisticsRowValueChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.OverviewRightPanelChildCount);
        Assert.Equal(7, RetainedShellCompositionPolicy.OverviewRightPanelContentChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewHighlightsHeadingChildCount);
        Assert.Equal(2, RetainedShellCompositionPolicy.OverviewFastestExtractionRowChildCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.OverviewFastestExtractionRowGraphicCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewFastestExtractionLabelChildCount);
        Assert.Equal(0, RetainedShellCompositionPolicy.OverviewFastestExtractionValueChildCount);
        Assert.Equal(76, RetainedShellCompositionPolicy.GraphicCount);
        Assert.Equal(10, RetainedShellCompositionPolicy.ButtonCount);
        Assert.Equal(1, RetainedShellCompositionPolicy.RectMaskCount);
        Assert.Equal(9, RetainedShellCompositionPolicy.OnlyOneEdgeModifierCount);
    }

    private static OverviewHighlightPresentation Highlight(
        StatisticsPanelProjection projection,
        OverviewHighlightMetric metric,
        Func<string, string>? text = null) => OverviewHighlightsPresentationFactory.Create(
            projection,
            text ?? (key => UiText.EnglishFallbacks[key]))
        .Single(value => value.Metric == metric);

    private static StatisticsPanelProjection CreateGateFifteenProjection()
    {
        var projection = new StatisticsPanelProjection
        {
            Runs = new RunStatisticsViewModel
            {
                Records = new RunDurationRecords
                {
                    Extraction = new DurationRecordPair
                    {
                        Shortest = new DurationRecordReference
                        {
                            RunId = "shortest",
                            ActiveDurationSeconds = 64.083d,
                            MapDisplayName = "Ground Zero"
                        },
                        Longest = new DurationRecordReference
                        {
                            RunId = "longest",
                            ActiveDurationSeconds = 607.713d,
                            MapDisplayName = "Recorded map"
                        }
                    }
                },
                Runs = new[]
                {
                    new RunSummary
                    {
                        RunId = "longest",
                        Segments = new List<MapSegmentSummary>
                        {
                            new() { SegmentIndex = 1, MapDisplayName = "Farm Town" },
                            new() { SegmentIndex = 0, MapDisplayName = "Ground Zero" }
                        },
                        RouteCapabilities = new RouteMetricCapabilities
                        {
                            OrderedRoute = new MetricAvailability { State = AdapterCapabilityState.Supported },
                            Segments = new MetricAvailability { State = AdapterCapabilityState.Supported }
                        }
                    }
                }
            },
            Weapons = new WeaponStatisticsViewModel
            {
                Lifetime = new WeaponStatisticsAggregate
                {
                    Totals = new WeaponMetricTotals { FiringActions = 143 }
                },
                Capabilities = new WeaponMetricCapabilities
                {
                    FiringActions = new MetricAvailability { State = AdapterCapabilityState.Supported },
                    WeaponIdentity = new MetricAvailability { State = AdapterCapabilityState.Supported }
                }
            },
            WeaponAmmunitionGroups = new[]
            {
                new WeaponAmmunitionGroupProjection
                {
                    WeaponId = "weapon:mp7",
                    DisplayName = "Electrified MP7",
                    TotalFiringActions = 143
                }
            },
            ItemUse = new ItemUsePanelProjection
            {
                Overall = new AggregateTotals { ActivationCount = 5 },
                Items = new[]
                {
                    new ItemUseRowProjection
                    {
                        ItemId = "item:med-kit-s",
                        DisplayName = "Med-Kit (S)",
                        Totals = new AggregateTotals { ActivationCount = 5 }
                    }
                }
            }
        };
        return projection;
    }

    private static StatisticsPanelProjection CreateWeaponRankingProjection(
        params (string Id, string Name, long Count)[] weapons)
    {
        var total = weapons.Sum(value => value.Count);
        return new StatisticsPanelProjection
        {
            Weapons = new WeaponStatisticsViewModel
            {
                Lifetime = new WeaponStatisticsAggregate
                {
                    Totals = new WeaponMetricTotals { FiringActions = total }
                },
                Capabilities = new WeaponMetricCapabilities
                {
                    FiringActions = new MetricAvailability { State = AdapterCapabilityState.Supported },
                    WeaponIdentity = new MetricAvailability { State = AdapterCapabilityState.Supported }
                }
            },
            WeaponAmmunitionGroups = weapons.Select(value => new WeaponAmmunitionGroupProjection
            {
                WeaponId = value.Id,
                DisplayName = value.Name,
                TotalFiringActions = value.Count
            }).ToArray()
        };
    }

    private static StatisticsPanelProjection CreateConsumableRankingProjection(
        params (string Id, string Name, long Count, double Health)[] items)
    {
        var total = items.Sum(value => value.Count);
        return new StatisticsPanelProjection
        {
            ItemUse = new ItemUsePanelProjection
            {
                Overall = new AggregateTotals { ActivationCount = total },
                Items = items.Select(value => new ItemUseRowProjection
                {
                    ItemId = value.Id,
                    DisplayName = value.Name,
                    Totals = new AggregateTotals
                    {
                        ActivationCount = value.Count,
                        ActualHealthRestored = value.Health
                    }
                }).ToArray()
            }
        };
    }

    private static StatisticsPanelProjection CreateFastestExtractionProjection(
        double durationSeconds,
        string mapDisplayName) => new()
    {
        Runs = new RunStatisticsViewModel
        {
            Records = new RunDurationRecords
            {
                Extraction = new DurationRecordPair
                {
                    Shortest = new DurationRecordReference
                    {
                        ActiveDurationSeconds = durationSeconds,
                        MapDisplayName = mapDisplayName
                    }
                }
            }
        }
    };

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

    private sealed class RetainedMaterialProbe
    {
        public RetainedMaterialProbe(
            object atlas,
            float underlayAlpha,
            float underlayOffsetX = RetainedTabLabelShadowPolicy.NativeUnderlayOffsetX,
            float underlayOffsetY = RetainedTabLabelShadowPolicy.NativeUnderlayOffsetY,
            float underlayDilate = RetainedTabLabelShadowPolicy.NativeUnderlayDilate,
            float underlaySoftness = RetainedTabLabelShadowPolicy.NativeUnderlaySoftness,
            float scaleRatioC = RetainedTabLabelShadowPolicy.ExpectedNativeScaleRatioC,
            bool underlayEnabled = true,
            bool ratioBypassEnabled = false)
        {
            Atlas = atlas;
            UnderlayAlpha = underlayAlpha;
            UnderlayOffsetX = underlayOffsetX;
            UnderlayOffsetY = underlayOffsetY;
            UnderlayDilate = underlayDilate;
            UnderlaySoftness = underlaySoftness;
            ScaleRatioC = scaleRatioC;
            UnderlayEnabled = underlayEnabled;
            RatioBypassEnabled = ratioBypassEnabled;
        }

        public object Atlas { get; }
        public float UnderlayAlpha { get; set; }
        public float UnderlayOffsetX { get; }
        public float UnderlayOffsetY { get; }
        public float UnderlayDilate { get; }
        public float UnderlaySoftness { get; }
        public float ScaleRatioC { get; }
        public bool UnderlayEnabled { get; }
        public bool RatioBypassEnabled { get; }

        public RetainedMaterialProbe Clone(float? underlayAlpha = null)
        {
            return new RetainedMaterialProbe(
                Atlas,
                underlayAlpha ?? UnderlayAlpha,
                UnderlayOffsetX,
                UnderlayOffsetY,
                UnderlayDilate,
                UnderlaySoftness,
                ScaleRatioC,
                UnderlayEnabled,
                RatioBypassEnabled);
        }
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

    private sealed class RetainedFeedbackTarget
    {
        public RetainedFeedbackTarget(string name) => Name = name;

        public string Name { get; }
        public bool HasFeedback { get; private set; }
        public bool ThrowOnAttach { get; set; }
        public int AttachCount { get; private set; }

        public void Attach()
        {
            AttachCount++;
            if (ThrowOnAttach) throw new InvalidOperationException("native feedback unavailable");
            HasFeedback = true;
        }
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
