using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.UI;

internal enum StatisticsPanelTab
{
    Overview,
    Runs,
    Records,
    Combat,
    Equipment,
    Economy,
    Crafting,
    ItemUse,
    Diagnostics
}

internal enum CombatPanelSection
{
    Summary,
    Enemies,
    WeaponsAndAmmunition,
    IncomingDamage
}

internal enum EquipmentPanelSection
{
    Loadouts,
    Weapons,
    ArmorAndGear,
    Totems
}

internal enum PanelOperation
{
    None,
    Export,
    Reset
}

internal enum PanelColumnLayout
{
    SideBySide,
    Stacked
}

internal enum PanelAccessSurface
{
    MainMenu,
    BasePauseMenu,
    Hotkey
}

internal sealed class PanelAccessDecision
{
    public bool CanOpen { get; set; }
    public string? RejectionTextKey { get; set; }
}

internal static class StatisticsPanelAccessPolicy
{
    public static PanelAccessDecision Resolve(PanelAccessSurface surface, bool isRaid)
    {
        if (!Enum.IsDefined(typeof(PanelAccessSurface), surface))
            throw new ArgumentOutOfRangeException(nameof(surface));
        return isRaid
            ? new PanelAccessDecision { RejectionTextKey = "ui.raid_unavailable" }
            : new PanelAccessDecision { CanOpen = true };
    }
}

internal static class NativeMenuAnchorPolicy
{
    public static int Score(string? hierarchyName)
    {
        if (string.IsNullOrWhiteSpace(hierarchyName)) return 0;
        var value = hierarchyName.ToLowerInvariant();
        if (value.Contains("settings", StringComparison.Ordinal)) return 300;
        if (value.Contains("setting", StringComparison.Ordinal)) return 280;
        if (value.Contains("options", StringComparison.Ordinal)) return 260;
        if (value.Contains("option", StringComparison.Ordinal)) return 240;
        if (value.Contains("mods", StringComparison.Ordinal)) return 220;
        if (value.Contains("mod", StringComparison.Ordinal)) return 200;
        return 0;
    }
}

internal static class NativeMenuPresentationPolicy
{
    internal const string ProceduralImageModifierTypeName =
        "UnityEngine.UI.ProceduralImage.ProceduralImageModifier";
    internal const string ButtonAnimationTypeName =
        "Duckov.UI.Animations.ButtonAnimation";
    internal const string ToggleAnimationTypeName =
        "Duckov.UI.Animations.ToggleAnimation";
    internal const string ToggleComponentTypeName =
        "Duckov.UI.Animations.ToggleComponent";

    public static bool PreservesProceduralImageState(IEnumerable<string?> typeHierarchy)
    {
        if (typeHierarchy == null) throw new ArgumentNullException(nameof(typeHierarchy));
        return typeHierarchy.Any(typeName =>
            string.Equals(typeName, ProceduralImageModifierTypeName, StringComparison.Ordinal));
    }

    public static bool IsButtonAnimation(IEnumerable<string?> typeHierarchy)
    {
        if (typeHierarchy == null) throw new ArgumentNullException(nameof(typeHierarchy));
        return typeHierarchy.Any(typeName =>
            string.Equals(typeName, ButtonAnimationTypeName, StringComparison.Ordinal));
    }

    public static bool PreservesNativeInteractionDependency(
        PanelAccessSurface surface,
        IEnumerable<string?> typeHierarchy)
    {
        if (typeHierarchy == null) throw new ArgumentNullException(nameof(typeHierarchy));
        if (surface != PanelAccessSurface.MainMenu && surface != PanelAccessSurface.BasePauseMenu) return false;

        return typeHierarchy.Any(typeName =>
            string.Equals(typeName, ToggleAnimationTypeName, StringComparison.Ordinal)
            || string.Equals(typeName, ToggleComponentTypeName, StringComparison.Ordinal));
    }

    public static bool PreservesUsableRootButtonAnimation(
        PanelAccessSurface surface,
        IEnumerable<string?> typeHierarchy,
        bool isPrimaryButtonRoot,
        bool isEnabled,
        bool alreadyPreserved)
    {
        return (surface == PanelAccessSurface.MainMenu || surface == PanelAccessSurface.BasePauseMenu)
               && isPrimaryButtonRoot
               && isEnabled
               && !alreadyPreserved
               && IsButtonAnimation(typeHierarchy);
    }
}

internal sealed class StatisticsPanelLayout
{
    public float Width { get; set; }
    public float Height { get; set; }
    public float ContentHeight { get; set; }
    public float Scale { get; set; }
    public PanelColumnLayout Columns { get; set; }
    public int PageSize { get; set; }
    public bool TabStripRequiresScrolling { get; set; }
}

internal static class StatisticsPanelLayoutPolicy
{
    private const float DesktopColumnThreshold = 1180f;
    private const float EstimatedTabStripWidth = 1120f;

    public static StatisticsPanelLayout Create(float screenWidth, float screenHeight, float uiScale = 1f)
    {
        if (screenWidth <= 0) throw new ArgumentOutOfRangeException(nameof(screenWidth));
        if (screenHeight <= 0) throw new ArgumentOutOfRangeException(nameof(screenHeight));
        if (uiScale <= 0 || float.IsNaN(uiScale) || float.IsInfinity(uiScale))
            throw new ArgumentOutOfRangeException(nameof(uiScale));

        var margin = Math.Max(12f, 24f * uiScale);
        var width = Math.Max(320f, Math.Min(1560f * uiScale, screenWidth - margin * 2f));
        var height = Math.Max(300f, Math.Min(960f * uiScale, screenHeight - margin * 2f));
        var contentHeight = Math.Max(160f, height - 150f * uiScale);
        var estimatedRowHeight = Math.Max(24f, 34f * uiScale);
        return new StatisticsPanelLayout
        {
            Width = width,
            Height = height,
            ContentHeight = contentHeight,
            Scale = uiScale,
            Columns = width / uiScale >= DesktopColumnThreshold
                ? PanelColumnLayout.SideBySide
                : PanelColumnLayout.Stacked,
            PageSize = Math.Clamp((int)Math.Floor(contentHeight / estimatedRowHeight) * 2, 12, 48),
            TabStripRequiresScrolling = width < EstimatedTabStripWidth * uiScale
        };
    }
}

internal static class TabStripScrollPolicy
{
    public static float EnsureVisible(
        float viewportWidth,
        float contentWidth,
        float selectedLeft,
        float selectedWidth,
        float currentOffset)
    {
        if (viewportWidth <= 0f) throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (contentWidth < 0f) throw new ArgumentOutOfRangeException(nameof(contentWidth));
        if (selectedLeft < 0f) throw new ArgumentOutOfRangeException(nameof(selectedLeft));
        if (selectedWidth <= 0f) throw new ArgumentOutOfRangeException(nameof(selectedWidth));
        var maximumOffset = Math.Max(0f, contentWidth - viewportWidth);
        var offset = Math.Clamp(currentOffset, 0f, maximumOffset);
        if (selectedLeft < offset) offset = selectedLeft;
        var selectedRight = selectedLeft + selectedWidth;
        if (selectedRight > offset + viewportWidth) offset = selectedRight - viewportWidth;
        return Math.Clamp(offset, 0f, maximumOffset);
    }
}

internal static class RuntimeTabStripScrollPolicy
{
    public static bool TryEnsureVisible(
        float viewportWidth,
        float contentWidth,
        float selectedLeft,
        float selectedWidth,
        float currentOffset,
        out float targetOffset)
    {
        targetOffset = 0f;
        if (!IsFinite(viewportWidth) || viewportWidth <= 0f
            || !IsFinite(contentWidth) || contentWidth < 0f
            || !IsFinite(selectedLeft)
            || !IsFinite(selectedWidth) || selectedWidth <= 0f)
        {
            return false;
        }

        targetOffset = TabStripScrollPolicy.EnsureVisible(
            viewportWidth,
            contentWidth,
            Math.Max(0f, selectedLeft),
            selectedWidth,
            IsFinite(currentOffset) ? currentOffset : 0f);
        return true;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

internal static class RetainedDimmerPolicy
{
    public const string RootName = "UltimateDuckovStatisticsRetainedShell";
    public const float Red = 0f;
    public const float Green = 0f;
    public const float Blue = 0f;
    public const float VisualAlpha = 0.75f;
    public const bool BlocksRaycasts = true;

    public static bool IsValidGraphic(
        float blockerRed,
        float blockerGreen,
        float blockerBlue,
        float blockerAlpha,
        bool blockerRaycastTarget) =>
        blockerRed == Red
        && blockerGreen == Green
        && blockerBlue == Blue
        && blockerAlpha == VisualAlpha
        && blockerRaycastTarget == BlocksRaycasts;
}

internal sealed class RetainedReferenceTransform
{
    public float ReferenceScale { get; set; }
    public float ReferenceOriginX { get; set; }
    public float ReferenceOriginY { get; set; }
    public float CanvasScaleFactor { get; set; }

    public float CanvasX(float referenceX) =>
        (ReferenceOriginX + referenceX * ReferenceScale) / CanvasScaleFactor;

    public float CanvasY(float referenceY) =>
        (ReferenceOriginY + referenceY * ReferenceScale) / CanvasScaleFactor;

    public float CanvasLength(float referenceLength) =>
        referenceLength * ReferenceScale / CanvasScaleFactor;
}

internal static class RetainedReferenceTransformPolicy
{
    public const float BaselineWidthPixels = 2560f;
    public const float BaselineHeightPixels = 1440f;

    public static RetainedReferenceTransform Create(
        float viewportPixelWidth,
        float viewportPixelHeight,
        float canvasScaleFactor)
    {
        if (!IsPositiveFinite(viewportPixelWidth))
            throw new ArgumentOutOfRangeException(nameof(viewportPixelWidth));
        if (!IsPositiveFinite(viewportPixelHeight))
            throw new ArgumentOutOfRangeException(nameof(viewportPixelHeight));
        if (!IsPositiveFinite(canvasScaleFactor))
            throw new ArgumentOutOfRangeException(nameof(canvasScaleFactor));

        var referenceScale = Math.Min(
            viewportPixelWidth / BaselineWidthPixels,
            viewportPixelHeight / BaselineHeightPixels);
        var referenceOriginX = (viewportPixelWidth - BaselineWidthPixels * referenceScale) / 2f;
        var referenceOriginY = (viewportPixelHeight - BaselineHeightPixels * referenceScale) / 2f;
        if (!IsPositiveFinite(referenceScale)
            || !IsFinite(referenceOriginX)
            || !IsFinite(referenceOriginY))
        {
            throw new InvalidOperationException("The retained reference-space transform produced invalid geometry.");
        }

        return new RetainedReferenceTransform
        {
            ReferenceScale = referenceScale,
            ReferenceOriginX = referenceOriginX,
            ReferenceOriginY = referenceOriginY,
            CanvasScaleFactor = canvasScaleFactor
        };
    }

    private static bool IsPositiveFinite(float value) => value > 0f && IsFinite(value);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

internal sealed class RetainedHeaderCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float CornerRadius { get; set; }
}

internal static class RetainedHeaderPolicy
{
    public const string Name = "HeaderBackground";
    public const float BaselineWidthPixels = RetainedReferenceTransformPolicy.BaselineWidthPixels;
    public const float BaselineHeightPixels = RetainedReferenceTransformPolicy.BaselineHeightPixels;
    public const float LeftPixels = 85f;
    public const float TopPixels = 113f;
    public const float WidthPixels = 2392f;
    public const float HeightPixels = 217f;
    public const float RightExclusivePixels = 2477f;
    public const float BottomExclusivePixels = 330f;
    public const float Red = 0f;
    public const float Green = 0f;
    public const float Blue = 0f;
    public const float VisualAlpha = 0.50f;
    public const float CornerRadiusPixels = 20f;
    public const bool BlocksRaycasts = false;
    public const float EffectiveOpacity = 0.75f;

    public static RetainedHeaderCanvasLayout CreateCanvasLayout(RetainedReferenceTransform referenceTransform)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        return new RetainedHeaderCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Left = referenceTransform.CanvasX(LeftPixels),
            Top = referenceTransform.CanvasY(TopPixels),
            Width = referenceTransform.CanvasLength(WidthPixels),
            Height = referenceTransform.CanvasLength(HeightPixels),
            CornerRadius = referenceTransform.CanvasLength(CornerRadiusPixels)
        };
    }

    public static bool IsValidGraphic(
        float red,
        float green,
        float blue,
        float alpha,
        bool raycastTarget) =>
        red == Red
        && green == Green
        && blue == Blue
        && alpha == VisualAlpha
        && raycastTarget == BlocksRaycasts;
}

internal sealed class RetainedHeaderBottomBarCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float SurfaceLeft { get; set; }
    public float SurfaceTop { get; set; }
    public float SurfaceWidth { get; set; }
    public float SurfaceHeight { get; set; }
    public float SurfaceCornerRadius { get; set; }
}

internal static class RetainedHeaderBottomBarPolicy
{
    public const string Name = "HeaderBottomBar";
    public const string GraphicName = "HeaderBottomBarGraphic";
    public const float LeftPixels = 85f;
    public const float TopPixels = 321f;
    public const float WidthPixels = 2392f;
    public const float HeightPixels = 9f;
    public const float RightExclusivePixels = 2477f;
    public const float BottomExclusivePixels = 330f;
    public const float VisibleLeftPixels = 88f;
    public const float VisibleTopPixels = 321f;
    public const float VisibleWidthPixels = 2386f;
    public const float VisibleHeightPixels = 9f;
    public const float VisibleRightExclusivePixels = 2474f;
    public const float VisibleBottomExclusivePixels = 330f;
    public const float SurfaceLeftPixels = RetainedHeaderPolicy.LeftPixels;
    public const float SurfaceTopPixels = RetainedHeaderPolicy.TopPixels;
    public const float SurfaceWidthPixels = RetainedHeaderPolicy.WidthPixels;
    public const float SurfaceHeightPixels = RetainedHeaderPolicy.HeightPixels;
    public const float SurfaceCornerRadiusPixels = RetainedHeaderPolicy.CornerRadiusPixels;
    public const float MaskPaddingPixels = 0f;
    public const int MaskSoftnessPixels = 0;
    public const bool UsesRectMask2D = true;
    public const bool UsesFilledImage = false;
    public const bool UsesOnlyOneEdgeModifier = false;
    public const float Red = 78f / 255f;
    public const float Green = 189f / 255f;
    public const float Blue = 1f;
    public const float Alpha = 1f;
    public const bool BlocksRaycasts = false;

    public static RetainedHeaderBottomBarCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        return new RetainedHeaderBottomBarCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Left = referenceTransform.CanvasX(LeftPixels),
            Top = referenceTransform.CanvasY(TopPixels),
            Width = referenceTransform.CanvasLength(WidthPixels),
            Height = referenceTransform.CanvasLength(HeightPixels),
            SurfaceLeft = referenceTransform.CanvasX(SurfaceLeftPixels),
            SurfaceTop = referenceTransform.CanvasY(SurfaceTopPixels),
            SurfaceWidth = referenceTransform.CanvasLength(SurfaceWidthPixels),
            SurfaceHeight = referenceTransform.CanvasLength(SurfaceHeightPixels),
            SurfaceCornerRadius = referenceTransform.CanvasLength(SurfaceCornerRadiusPixels)
        };
    }

    public static bool IsValidGraphic(
        float red,
        float green,
        float blue,
        float alpha,
        bool raycastTarget) =>
        red == Red
        && green == Green
        && blue == Blue
        && alpha == Alpha
        && raycastTarget == BlocksRaycasts;
}

internal sealed class RetainedTabSpecification
{
    public RetainedTabSpecification(
        StatisticsPanelTab tab,
        string backgroundName,
        string labelName,
        string textKey,
        string englishFallback,
        float auditedEnglishPreferredWidthPixels,
        bool isAbsolutelyAnchored = false)
    {
        Tab = tab;
        BackgroundName = backgroundName ?? throw new ArgumentNullException(nameof(backgroundName));
        LabelName = labelName ?? throw new ArgumentNullException(nameof(labelName));
        TextKey = textKey ?? throw new ArgumentNullException(nameof(textKey));
        EnglishFallback = englishFallback ?? throw new ArgumentNullException(nameof(englishFallback));
        if (!RetainedTabMeasurementPolicy.IsPositiveFinite(auditedEnglishPreferredWidthPixels))
            throw new ArgumentOutOfRangeException(nameof(auditedEnglishPreferredWidthPixels));
        AuditedEnglishPreferredWidthPixels = auditedEnglishPreferredWidthPixels;
        IsAbsolutelyAnchored = isAbsolutelyAnchored;
    }

    public StatisticsPanelTab Tab { get; }
    public string BackgroundName { get; }
    public string LabelName { get; }
    public string TextKey { get; }
    public string EnglishFallback { get; }
    public float AuditedEnglishPreferredWidthPixels { get; }
    public bool IsAbsolutelyAnchored { get; }
}

internal sealed class RetainedTabCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public RetainedTabSpecification Specification { get; set; } = null!;
    public float ReferencePreferredLabelWidth { get; set; }
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float ExposedHeight { get; set; }
    public float CornerRadius { get; set; }
    public float LeftPadding { get; set; }
    public float RightPadding { get; set; }
    public float TopPadding { get; set; }
    public float BottomPadding { get; set; }
    public float FontSize { get; set; }
    public float PreferredLabelWidth { get; set; }
    public float LabelLeft { get; set; }
    public float LabelTop { get; set; }
    public float LabelWidth { get; set; }
    public float LabelHeight { get; set; }
}

internal sealed class RetainedTabStripCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public IReadOnlyList<RetainedTabCanvasLayout> Tabs { get; set; } = Array.Empty<RetainedTabCanvasLayout>();
}

internal readonly struct RetainedRgbaColor
{
    public RetainedRgbaColor(float red, float green, float blue, float alpha)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    public float Red { get; }
    public float Green { get; }
    public float Blue { get; }
    public float Alpha { get; }
}

internal sealed class RetainedOwnedResource<T> : IDisposable where T : class
{
    private T? resource;
    private Action<T>? destroy;

    private RetainedOwnedResource(T resource, Action<T> destroy)
    {
        this.resource = resource;
        this.destroy = destroy;
    }

    public T Resource => resource ?? throw new ObjectDisposedException(nameof(RetainedOwnedResource<T>));

    public bool IsDisposed => resource == null;

    public static RetainedOwnedResource<T> CreatePrivateClone(
        T source,
        Func<T, T> clone,
        Action<T> destroy)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (clone == null) throw new ArgumentNullException(nameof(clone));
        if (destroy == null) throw new ArgumentNullException(nameof(destroy));

        var privateClone = clone(source)
            ?? throw new InvalidOperationException("The private retained resource clone was null.");
        if (ReferenceEquals(privateClone, source))
            throw new InvalidOperationException("The retained resource factory returned the shared source.");
        return new RetainedOwnedResource<T>(privateClone, destroy);
    }

    public void Dispose()
    {
        var current = resource;
        if (current == null) return;
        resource = null;
        var destroyCurrent = destroy;
        destroy = null;
        destroyCurrent!(current);
    }
}

internal sealed class RetainedListenerLease : IDisposable
{
    private readonly List<Action> removers = new();
    private bool disposed;

    public int Count => removers.Count;
    public bool IsDisposed => disposed;

    public void Register(Action remove)
    {
        if (remove == null) throw new ArgumentNullException(nameof(remove));
        if (disposed) throw new ObjectDisposedException(nameof(RetainedListenerLease));
        removers.Add(remove);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        for (var index = removers.Count - 1; index >= 0; index--) removers[index]();
        removers.Clear();
    }
}

internal static class NativeButtonInteractionFeedbackPolicy
{
    public const string NativeComponentTypeName = "Duckov.UI.Animations.ButtonAnimation";
    public const bool UsesNativePointerHandlers = true;
    public const bool SynthesizesAudio = false;
    public const bool AppliesToLabels = false;
    public const bool AppliesToBackArrow = false;
    public const bool AppliesToRetainedTabs = true;
    public const bool AppliesToBackButton = true;
    public const bool AppliesToMainMenuButton = true;
    public const bool AppliesToLatestRunViewRun = true;
    public const bool AppliesToBasePauseMenuButton = true;
    public const bool UsesDefaultConfigurationForCreatedComponents = true;

    public static void AttachIfMissing<TTarget>(
        TTarget target,
        Func<TTarget, bool> hasFeedback,
        Action<TTarget> attach)
        where TTarget : class
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (hasFeedback == null) throw new ArgumentNullException(nameof(hasFeedback));
        if (attach == null) throw new ArgumentNullException(nameof(attach));

        try
        {
            if (!hasFeedback(target)) attach(target);
        }
        catch (Exception)
        {
            // Native interaction feedback is optional polish. Its absence must
            // never prevent the retained panel or its tab callbacks from working.
        }
    }
}

internal sealed class NativeMenuButtonActivation
{
    public const bool ReplacesInheritedCallbacks = true;
    public const int RegisteredUdsCallbackCount = 1;

    private readonly Action activate;

    public NativeMenuButtonActivation(Action activate)
    {
        this.activate = activate ?? throw new ArgumentNullException(nameof(activate));
    }

    public void Invoke() => activate();
}

internal static class RetainedTabLabelShadowPolicy
{
    public const string OwnedMaterialName = "UltimateDuckovStatistics Retained Tab Label Material";
    public const string UnderlayKeyword = "UNDERLAY_ON";
    public const string RatioBypassKeyword = "RATIOS_OFF";
    public const string MainTextureProperty = "_MainTex";
    public const string UnderlayColorProperty = "_UnderlayColor";
    public const string UnderlayOffsetXProperty = "_UnderlayOffsetX";
    public const string UnderlayOffsetYProperty = "_UnderlayOffsetY";
    public const string UnderlayDilateProperty = "_UnderlayDilate";
    public const string UnderlaySoftnessProperty = "_UnderlaySoftness";
    public const string ScaleRatioCProperty = "_ScaleRatioC";
    public const float Red = 0f;
    public const float Green = 0f;
    public const float Blue = 0f;
    public const float Alpha = 0.57f;
    public const float NativeUnderlayOffsetX = 1f;
    public const float NativeUnderlayOffsetY = -1f;
    public const float NativeUnderlayDilate = -0.25f;
    public const float NativeUnderlaySoftness = 1f;
    public const float ExpectedNativeScaleRatioC = 0.41785714f;
    public const float ScaleRatioTolerance = 0.000001f;
}

internal static class RetainedOverviewTabPolicy
{
    public const string BackgroundName = "OverviewTab";
    public const string LabelName = "OverviewTabLabel";
    public const string TextKey = "ui.overview";
    public const string EnglishFallback = "Overview";
    public const string FontAssetName = RetainedHeaderTitlePolicy.FontAssetName;
    public const string MaterialName = RetainedHeaderTitlePolicy.MaterialName;
    public const string NativeUnderlayKeyword = "UNDERLAY_ON";
    public const float LeftPixels = 115f;
    public const float TopPixels = 251f;
    public const float HeightPixels = 79f;
    public const float BottomExclusivePixels = 330f;
    public const float ExposedHeightPixels = 70f;
    public const float CornerRadiusPixels = 20f;
    public const float TopLeftCornerRadiusPixels = CornerRadiusPixels;
    public const float TopRightCornerRadiusPixels = CornerRadiusPixels;
    public const float BottomLeftCornerRadiusPixels = 0f;
    public const float BottomRightCornerRadiusPixels = 0f;
    public const float LeftPaddingPixels = 30f;
    public const float RightPaddingPixels = 30f;
    public const float TopPaddingPixels = 25f;
    public const float BottomPaddingPixels = 25f;
    public const float NominalLabelHeightPixels = 29f;
    public const float ReferenceFontSize = 36f;
    public const float AuditedNativePreferredWidthPixels = 161.91875f;
    public const float AuditedReferenceTabWidthPixels =
        AuditedNativePreferredWidthPixels + LeftPaddingPixels + RightPaddingPixels;
    public const float LabelRed = 1f;
    public const float LabelGreen = 1f;
    public const float LabelBlue = 1f;
    public const float LabelAlpha = 1f;
    public const bool BackgroundBlocksRaycasts = true;
    public const bool LabelBlocksRaycasts = false;
    public const bool WordWrapping = false;
    public const bool AutoSizing = false;
    public const float CharacterSpacing = 0f;
    public const float WordSpacing = 0f;
    public const float LineSpacing = 0f;
    public const float ParagraphSpacing = 0f;
    public const bool UsesTopEdgeModifier = true;
    public const bool UsesFilledImage = false;
    public const bool UsesHorizontalTypographyCompensation = false;
    public const bool RequiresNativeUnderlay = true;

    public static RetainedTabCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        return RetainedTabStripPolicy.CreateCanvasLayout(
            referenceTransform,
            RetainedTabStripPolicy.AuditedEnglishPreferredWidths).Tabs[0];
    }
}

internal static class RetainedTabMeasurementPolicy
{
    public const bool RequiresActiveHierarchy = true;

    // TMP's preferred-size query can honor the supplied text-container width.
    // Give the measurement host one full reference width so the native label is
    // measured as a single unconstrained line before its relational tab bounds are applied.
    public const float TemporaryLabelWidthPixels = RetainedReferenceTransformPolicy.BaselineWidthPixels;

    public static float NormalizeCanvasWidth(
        float measuredCanvasWidth,
        float canvasScaleFactor,
        float referenceScale)
    {
        if (!IsPositiveFinite(measuredCanvasWidth))
            throw new ArgumentOutOfRangeException(nameof(measuredCanvasWidth));
        if (!IsPositiveFinite(canvasScaleFactor))
            throw new ArgumentOutOfRangeException(nameof(canvasScaleFactor));
        if (!IsPositiveFinite(referenceScale))
            throw new ArgumentOutOfRangeException(nameof(referenceScale));

        var normalized = measuredCanvasWidth * canvasScaleFactor / referenceScale;
        if (!IsPositiveFinite(normalized))
            throw new InvalidOperationException("The native tab-label width could not be normalized.");
        return normalized;
    }

    public static bool IsPositiveFinite(float value) =>
        value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
}

internal static class RetainedTabStripPolicy
{
    private static readonly IReadOnlyList<RetainedTabSpecification> OrderedSpecifications =
        Array.AsReadOnly(new[]
        {
            new RetainedTabSpecification(
                StatisticsPanelTab.Overview,
                "OverviewTab",
                "OverviewTabLabel",
                "ui.overview",
                "Overview",
                161.91875f,
                isAbsolutelyAnchored: true),
            new RetainedTabSpecification(
                StatisticsPanelTab.Runs,
                "RunsTab",
                "RunsTabLabel",
                "ui.runs",
                "Runs",
                85.68125f),
            new RetainedTabSpecification(
                StatisticsPanelTab.Records,
                "RecordsTab",
                "RecordsTabLabel",
                "ui.records",
                "Records",
                139.3625f),
            new RetainedTabSpecification(
                StatisticsPanelTab.Combat,
                "CombatTab",
                "CombatTabLabel",
                "ui.combat",
                "Combat",
                136.875f),
            new RetainedTabSpecification(
                StatisticsPanelTab.Equipment,
                "EquipmentTab",
                "EquipmentTabLabel",
                "ui.equipment",
                "Equipment",
                190.58125f),
            new RetainedTabSpecification(
                StatisticsPanelTab.Economy,
                "EconomyTab",
                "EconomyTabLabel",
                "ui.economy",
                "Economy",
                160.38125f),
            new RetainedTabSpecification(
                StatisticsPanelTab.Crafting,
                "CraftingTab",
                "CraftingTabLabel",
                "ui.crafting",
                "Crafting",
                138.95625f),
            new RetainedTabSpecification(
                StatisticsPanelTab.ItemUse,
                "ItemUseTab",
                "ItemUseTabLabel",
                "ui.item_use",
                "Item Use",
                151.7375f),
            new RetainedTabSpecification(
                StatisticsPanelTab.Diagnostics,
                "DiagnosticsTab",
                "DiagnosticsTabLabel",
                "ui.diagnostics",
                "Diagnostics",
                199.33125f)
        });

    private static readonly IReadOnlyList<float> EnglishWidths =
        Array.AsReadOnly(OrderedSpecifications
            .Select(specification => specification.AuditedEnglishPreferredWidthPixels)
            .ToArray());

    public const float FirstTabLeftPixels = RetainedOverviewTabPolicy.LeftPixels;
    public const float GapPixels = 10f;
    public const float HorizontalPaddingPixels =
        RetainedOverviewTabPolicy.LeftPaddingPixels + RetainedOverviewTabPolicy.RightPaddingPixels;

    public static IReadOnlyList<RetainedTabSpecification> Specifications => OrderedSpecifications;
    public static IReadOnlyList<float> AuditedEnglishPreferredWidths => EnglishWidths;

    public static RetainedTabStripCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        IReadOnlyList<float> preferredReferenceWidths)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (preferredReferenceWidths == null) throw new ArgumentNullException(nameof(preferredReferenceWidths));
        if (preferredReferenceWidths.Count != OrderedSpecifications.Count)
            throw new ArgumentException("Exactly one preferred width is required for each retained tab.", nameof(preferredReferenceWidths));

        var layouts = new RetainedTabCanvasLayout[OrderedSpecifications.Count];
        var referenceLeft = FirstTabLeftPixels;
        for (var index = 0; index < layouts.Length; index++)
        {
            var preferredReferenceWidth = preferredReferenceWidths[index];
            if (!RetainedTabMeasurementPolicy.IsPositiveFinite(preferredReferenceWidth))
                throw new ArgumentOutOfRangeException(nameof(preferredReferenceWidths));

            var referenceWidth = preferredReferenceWidth + HorizontalPaddingPixels;
            var left = referenceTransform.CanvasX(referenceLeft);
            var top = referenceTransform.CanvasY(RetainedOverviewTabPolicy.TopPixels);
            var height = referenceTransform.CanvasLength(RetainedOverviewTabPolicy.HeightPixels);
            var preferredLabelWidth = referenceTransform.CanvasLength(preferredReferenceWidth);
            var leftPadding = referenceTransform.CanvasLength(RetainedOverviewTabPolicy.LeftPaddingPixels);
            var rightPadding = referenceTransform.CanvasLength(RetainedOverviewTabPolicy.RightPaddingPixels);
            var topPadding = referenceTransform.CanvasLength(RetainedOverviewTabPolicy.TopPaddingPixels);
            var bottomPadding = referenceTransform.CanvasLength(RetainedOverviewTabPolicy.BottomPaddingPixels);
            layouts[index] = new RetainedTabCanvasLayout
            {
                ReferenceTransform = referenceTransform,
                Specification = OrderedSpecifications[index],
                ReferencePreferredLabelWidth = preferredReferenceWidth,
                Left = left,
                Top = top,
                Width = preferredLabelWidth + leftPadding + rightPadding,
                Height = height,
                ExposedHeight = referenceTransform.CanvasLength(RetainedOverviewTabPolicy.ExposedHeightPixels),
                CornerRadius = referenceTransform.CanvasLength(RetainedOverviewTabPolicy.CornerRadiusPixels),
                LeftPadding = leftPadding,
                RightPadding = rightPadding,
                TopPadding = topPadding,
                BottomPadding = bottomPadding,
                FontSize = referenceTransform.CanvasLength(RetainedOverviewTabPolicy.ReferenceFontSize),
                PreferredLabelWidth = preferredLabelWidth,
                LabelLeft = referenceTransform.CanvasX(
                    referenceLeft + RetainedOverviewTabPolicy.LeftPaddingPixels),
                LabelTop = referenceTransform.CanvasY(
                    RetainedOverviewTabPolicy.TopPixels + RetainedOverviewTabPolicy.TopPaddingPixels),
                LabelWidth = preferredLabelWidth,
                LabelHeight = height - topPadding - bottomPadding
            };
            referenceLeft += referenceWidth + GapPixels;
        }

        return new RetainedTabStripCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Tabs = Array.AsReadOnly(layouts)
        };
    }
}

internal static class RetainedTabVisualStatePolicy
{
    public const float UnselectedRed = 30f / 255f;
    public const float UnselectedGreen = 66f / 255f;
    public const float UnselectedBlue = 94f / 255f;
    public const float UnselectedAlpha = 0.75f;
    public const float SelectedRed = RetainedHeaderBottomBarPolicy.Red;
    public const float SelectedGreen = RetainedHeaderBottomBarPolicy.Green;
    public const float SelectedBlue = RetainedHeaderBottomBarPolicy.Blue;
    public const float SelectedAlpha = RetainedHeaderBottomBarPolicy.Alpha;

    public static RetainedRgbaColor Resolve(
        StatisticsPanelTab selectedTab,
        StatisticsPanelTab candidateTab)
    {
        if (!PanelInteractionState.NavigationOrder.Contains(selectedTab))
            throw new ArgumentOutOfRangeException(nameof(selectedTab));
        if (!PanelInteractionState.NavigationOrder.Contains(candidateTab))
            throw new ArgumentOutOfRangeException(nameof(candidateTab));

        return selectedTab == candidateTab
            ? new RetainedRgbaColor(SelectedRed, SelectedGreen, SelectedBlue, SelectedAlpha)
            : new RetainedRgbaColor(UnselectedRed, UnselectedGreen, UnselectedBlue, UnselectedAlpha);
    }

    public static bool IsExactColor(
        RetainedRgbaColor expected,
        float red,
        float green,
        float blue,
        float alpha) =>
        red == expected.Red
        && green == expected.Green
        && blue == expected.Blue
        && alpha == expected.Alpha;
}

internal sealed class RetainedTabVisualState<TTarget> where TTarget : class
{
    private readonly TTarget target;
    private readonly StatisticsPanelTab candidateTab;
    private readonly Action<TTarget, RetainedRgbaColor> applyColor;

    public RetainedTabVisualState(
        TTarget target,
        StatisticsPanelTab candidateTab,
        Action<TTarget, RetainedRgbaColor> applyColor)
    {
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        this.applyColor = applyColor ?? throw new ArgumentNullException(nameof(applyColor));
        RetainedTabVisualStatePolicy.Resolve(candidateTab, candidateTab);
        this.candidateTab = candidateTab;
    }

    public TTarget Target => target;

    public void Apply(StatisticsPanelTab selectedTab)
    {
        applyColor(
            target,
            RetainedTabVisualStatePolicy.Resolve(selectedTab, candidateTab));
    }
}

internal sealed class RetainedTabViewVisibility<TTarget> where TTarget : class
{
    private readonly TTarget target;
    private readonly StatisticsPanelTab ownerTab;
    private readonly Action<TTarget, bool> applyVisibility;

    public RetainedTabViewVisibility(
        TTarget target,
        StatisticsPanelTab ownerTab,
        Action<TTarget, bool> applyVisibility)
    {
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        if (!PanelInteractionState.NavigationOrder.Contains(ownerTab))
            throw new ArgumentOutOfRangeException(nameof(ownerTab));
        this.ownerTab = ownerTab;
        this.applyVisibility = applyVisibility ?? throw new ArgumentNullException(nameof(applyVisibility));
    }

    public TTarget Target => target;
    public StatisticsPanelTab OwnerTab => ownerTab;

    public void Apply(StatisticsPanelTab selectedTab) =>
        applyVisibility(target, selectedTab == ownerTab);
}

internal sealed class RetainedOverviewPanelCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float CornerRadius { get; set; }
    public float ContentLeft { get; set; }
    public float ContentTop { get; set; }
    public float ContentWidth { get; set; }
    public float ContentHeight { get; set; }
}

internal static class RetainedOverviewPanelStylePolicy
{
    public const StatisticsPanelTab OwnerTab = StatisticsPanelTab.Overview;
    public const float TopPixels = 370f;
    public const float WidthPixels = 1175f;
    public const float HeightPixels = 1040f;
    public const float RightExclusivePixels = 1260f;
    public const float BottomExclusivePixels = 1410f;
    public const float HeaderGapPixels = 40f;
    public const float ScreenBottomMarginPixels = 30f;
    public const float Red = 0f;
    public const float Green = 0f;
    public const float Blue = 0f;
    public const float LayerAlpha = 0.50f;
    public const float CornerRadiusPixels = 20f;
    public const float ContentPaddingPixels = 30f;
    public const bool BlocksRaycasts = false;

    public static RetainedOverviewPanelCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        float leftPixels)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        return new RetainedOverviewPanelCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Left = referenceTransform.CanvasX(leftPixels),
            Top = referenceTransform.CanvasY(TopPixels),
            Width = referenceTransform.CanvasLength(WidthPixels),
            Height = referenceTransform.CanvasLength(HeightPixels),
            CornerRadius = referenceTransform.CanvasLength(CornerRadiusPixels),
            ContentLeft = referenceTransform.CanvasX(leftPixels + ContentPaddingPixels),
            ContentTop = referenceTransform.CanvasY(TopPixels + ContentPaddingPixels),
            ContentWidth = referenceTransform.CanvasLength(WidthPixels - ContentPaddingPixels * 2f),
            ContentHeight = referenceTransform.CanvasLength(HeightPixels - ContentPaddingPixels * 2f)
        };
    }
}

internal static class RetainedOverviewLeftPanelPolicy
{
    public const string ViewName = "OverviewContentView";
    public const string BackgroundName = "OverviewLeftPanelBackground";
    public const string ContentName = "OverviewLeftPanelContent";
    public const StatisticsPanelTab OwnerTab = RetainedOverviewPanelStylePolicy.OwnerTab;
    public const float LeftPixels = 85f;
    public const float TopPixels = RetainedOverviewPanelStylePolicy.TopPixels;
    public const float WidthPixels = RetainedOverviewPanelStylePolicy.WidthPixels;
    public const float HeightPixels = RetainedOverviewPanelStylePolicy.HeightPixels;
    public const float RightExclusivePixels = 1260f;
    public const float BottomExclusivePixels = RetainedOverviewPanelStylePolicy.BottomExclusivePixels;
    public const float HeaderGapPixels = RetainedOverviewPanelStylePolicy.HeaderGapPixels;
    public const float ScreenBottomMarginPixels = RetainedOverviewPanelStylePolicy.ScreenBottomMarginPixels;
    public const float Red = RetainedOverviewPanelStylePolicy.Red;
    public const float Green = RetainedOverviewPanelStylePolicy.Green;
    public const float Blue = RetainedOverviewPanelStylePolicy.Blue;
    public const float LayerAlpha = RetainedOverviewPanelStylePolicy.LayerAlpha;
    public const float CornerRadiusPixels = RetainedOverviewPanelStylePolicy.CornerRadiusPixels;
    public const float ContentPaddingPixels = RetainedOverviewPanelStylePolicy.ContentPaddingPixels;
    public const bool BlocksRaycasts = RetainedOverviewPanelStylePolicy.BlocksRaycasts;

    public static RetainedOverviewPanelCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform) =>
        RetainedOverviewPanelStylePolicy.CreateCanvasLayout(referenceTransform, LeftPixels);
}

internal sealed class RetainedOverviewProfileSummaryHeadingCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float FontSize { get; set; }
    public float OpticalOffsetX { get; set; }
    public float OpticalOffsetY { get; set; }
}

internal static class RetainedOverviewProfileSummaryHeadingPolicy
{
    public const string Name = "OverviewProfileSummaryHeading";
    public const string ParentName = RetainedOverviewLeftPanelPolicy.ContentName;
    public const string TextKey = "ui.profile_summary";
    public const string EnglishFallback = "Profile Summary";
    public const string FontAssetName = RetainedHeaderTitlePolicy.FontAssetName;
    public const string SourceMaterialName = RetainedHeaderTitlePolicy.MaterialName;
    public const string MaterialName = RetainedTabLabelShadowPolicy.OwnedMaterialName;
    public const float ReferenceFontSize = 46.3f;
    public const float HeightPixels = 60f;
    public const float ReferenceOpticalOffsetX = -5f;
    public const float ReferenceOpticalOffsetY = 19f;
    public const float Red = 1f;
    public const float Green = 1f;
    public const float Blue = 1f;
    public const float Alpha = 1f;
    public const bool BlocksRaycasts = false;
    public const bool WordWrapping = false;
    public const bool AutoSizing = false;
    public const bool UsesTopLeftAlignment = true;
    public const bool UsesOwnedTabLabelMaterial = true;
    public const bool UsesZeroTextMargin = true;
    public const float AdditionalPaddingPixels = 0f;
    public const bool UsesFixedOpticalOffset = true;
    public const bool UsesHorizontalScaleCompensation = false;

    public static RetainedOverviewProfileSummaryHeadingCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewPanelCanvasLayout leftPanel)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (leftPanel == null) throw new ArgumentNullException(nameof(leftPanel));
        return new RetainedOverviewProfileSummaryHeadingCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Left = leftPanel.ContentLeft,
            Top = leftPanel.ContentTop,
            Width = leftPanel.ContentWidth,
            Height = referenceTransform.CanvasLength(HeightPixels),
            FontSize = referenceTransform.CanvasLength(ReferenceFontSize),
            OpticalOffsetX = referenceTransform.CanvasLength(ReferenceOpticalOffsetX),
            OpticalOffsetY = referenceTransform.CanvasLength(ReferenceOpticalOffsetY)
        };
    }
}

internal sealed class RetainedOverviewFirstStatisticsRowCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float CornerRadius { get; set; }
    public float ContentLeft { get; set; }
    public float ContentTop { get; set; }
    public float ContentWidth { get; set; }
    public float ContentHeight { get; set; }
}

internal static class RetainedOverviewFirstStatisticsRowPolicy
{
    public const string BackgroundName = "OverviewFirstStatisticsRowBackground";
    public const string ContentName = "OverviewFirstStatisticsRowContent";
    public const string ParentName = RetainedOverviewLeftPanelPolicy.ContentName;
    public const StatisticsPanelTab OwnerTab = StatisticsPanelTab.Overview;
    public const float LeftPixels = 115f;
    public const float TopPixels = 456f;
    public const float RightExclusivePixels = 1230f;
    public const float BottomExclusivePixels = 522f;
    public const float WidthPixels = 1115f;
    public const float HeightPixels = 66f;
    public const float HeadingToCardMarginPixels = 20f;
    public const float ContentTopOffsetPixels = 56f;
    public const float ContentPaddingPixels = 20f;
    public const float CornerRadiusPixels = 10f;
    public const float Red = 0f;
    public const float Green = 0f;
    public const float Blue = 0f;
    public const float LayerAlpha = 0.50f;
    public const bool BlocksRaycasts = false;

    public static RetainedOverviewFirstStatisticsRowCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewPanelCanvasLayout leftPanel)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (leftPanel == null) throw new ArgumentNullException(nameof(leftPanel));
        var padding = referenceTransform.CanvasLength(ContentPaddingPixels);
        var left = leftPanel.ContentLeft;
        var top = leftPanel.ContentTop + referenceTransform.CanvasLength(ContentTopOffsetPixels);
        return new RetainedOverviewFirstStatisticsRowCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Left = left,
            Top = top,
            Width = leftPanel.ContentWidth,
            Height = referenceTransform.CanvasLength(HeightPixels),
            CornerRadius = referenceTransform.CanvasLength(CornerRadiusPixels),
            ContentLeft = left + padding,
            ContentTop = top + padding,
            ContentWidth = leftPanel.ContentWidth - padding * 2f,
            ContentHeight = referenceTransform.CanvasLength(HeightPixels - ContentPaddingPixels * 2f)
        };
    }
}

internal sealed class RetainedTwoColumnStatisticsRowCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float LabelLeft { get; set; }
    public float LabelTop { get; set; }
    public float LabelWidth { get; set; }
    public float LabelHeight { get; set; }
    public float ValueLeft { get; set; }
    public float ValueTop { get; set; }
    public float ValueWidth { get; set; }
    public float ValueHeight { get; set; }
    public float SecondaryValueLeft { get; set; }
    public float SecondaryValueTop { get; set; }
    public float SecondaryValueWidth { get; set; }
    public float SecondaryValueHeight { get; set; }
    public bool HasSecondaryValue { get; set; }
    public float FontSize { get; set; }
}

internal static class RetainedTwoColumnStatisticsRowLayoutPolicy
{
    public static RetainedTwoColumnStatisticsRowCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewFirstStatisticsRowCanvasLayout row,
        float referenceLabelWidth,
        float referenceFontSize)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (row == null) throw new ArgumentNullException(nameof(row));
        var labelWidth = referenceTransform.CanvasLength(referenceLabelWidth);
        return new RetainedTwoColumnStatisticsRowCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            LabelLeft = row.ContentLeft,
            LabelTop = row.ContentTop,
            LabelWidth = labelWidth,
            LabelHeight = row.ContentHeight,
            ValueLeft = row.ContentLeft + labelWidth,
            ValueTop = row.ContentTop,
            ValueWidth = row.ContentWidth - labelWidth,
            ValueHeight = row.ContentHeight,
            FontSize = referenceTransform.CanvasLength(referenceFontSize)
        };
    }
}

internal static class RetainedThreeColumnStatisticsRowLayoutPolicy
{
    public static RetainedTwoColumnStatisticsRowCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewFirstStatisticsRowCanvasLayout row,
        float referenceLabelWidth,
        float referencePrimaryValueWidth,
        float referenceFontSize)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (row == null) throw new ArgumentNullException(nameof(row));
        var labelWidth = referenceTransform.CanvasLength(referenceLabelWidth);
        var primaryValueWidth = referenceTransform.CanvasLength(referencePrimaryValueWidth);
        return new RetainedTwoColumnStatisticsRowCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            LabelLeft = row.ContentLeft,
            LabelTop = row.ContentTop,
            LabelWidth = labelWidth,
            LabelHeight = row.ContentHeight,
            ValueLeft = row.ContentLeft + labelWidth,
            ValueTop = row.ContentTop,
            ValueWidth = primaryValueWidth,
            ValueHeight = row.ContentHeight,
            SecondaryValueLeft = row.ContentLeft + labelWidth + primaryValueWidth,
            SecondaryValueTop = row.ContentTop,
            SecondaryValueWidth = row.ContentWidth - labelWidth - primaryValueWidth,
            SecondaryValueHeight = row.ContentHeight,
            HasSecondaryValue = true,
            FontSize = referenceTransform.CanvasLength(referenceFontSize)
        };
    }
}

internal static class RetainedOverviewFirstStatisticsRowEntryPolicy
{
    public const string LabelName = "OverviewFirstStatisticsRowLabel";
    public const string ValueName = "OverviewFirstStatisticsRowValue";
    public const string ParentName = RetainedOverviewFirstStatisticsRowPolicy.ContentName;
    public const StatisticsPanelTab OwnerTab = StatisticsPanelTab.Overview;
    public const string LabelTextKey = "ui.overview_total_runs";
    public const string LabelEnglishFallback = "Total runs";
    public const string FontAssetName = RetainedHeaderTitlePolicy.FontAssetName;
    public const string MaterialName = RetainedTabLabelShadowPolicy.OwnedMaterialName;
    public const float ReferenceFontSize = 29.8f;
    public const float LabelColumnLeftPixels = 135f;
    public const float LabelColumnWidthPixels = 465f;
    public const float ValueColumnLeftPixels = 600f;
    public const float ValueColumnRightExclusivePixels = 1210f;
    public const float Red = 1f;
    public const float Green = 1f;
    public const float Blue = 1f;
    public const float Alpha = 1f;
    public const float CharacterSpacing = 0f;
    public const float WordSpacing = 0f;
    public const float LineSpacing = 0f;
    public const float ParagraphSpacing = 0f;
    public const bool BlocksRaycasts = false;
    public const bool WordWrapping = false;
    public const bool AutoSizing = false;
    public const bool UsesVisibleOverflow = true;
    public const bool UsesLeftAlignment = true;
    public const bool UsesVerticalCentering = true;
    public const bool UsesZeroTextMargins = true;
    public const bool UsesNormalStyle = true;
    public const bool UsesRegularWeight = true;
    public const bool UsesOwnedSubtleShadowMaterial = true;
    public const bool UsesProjectionRunsTotalRuns = true;

    public static RetainedTwoColumnStatisticsRowCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewFirstStatisticsRowCanvasLayout row) =>
        RetainedTwoColumnStatisticsRowLayoutPolicy.CreateCanvasLayout(
            referenceTransform,
            row,
            LabelColumnWidthPixels,
            ReferenceFontSize);

    public static string FormatProjectedValue(StatisticsPanelProjection projection)
    {
        if (projection == null) throw new ArgumentNullException(nameof(projection));
        return projection.Runs.TotalRuns.ToString(CultureInfo.InvariantCulture);
    }
}

internal enum ProfileSummaryMetric
{
    TotalRuns,
    ExtractionRate,
    TotalActiveRaidTime,
    TotalDistanceTravelled,
    KillsByYou,
    Deaths,
    DamageDealt,
    DamageTaken,
    HealthRestored,
    UniqueContainersOpened,
    Economy
}

internal sealed class RetainedProfileSummaryRowSpecification
{
    public RetainedProfileSummaryRowSpecification(
        ProfileSummaryMetric metric,
        string nameStem,
        string labelTextKey,
        string labelEnglishFallback,
        bool hasSecondaryValue = false)
    {
        Metric = metric;
        BackgroundName = nameStem + "RowBackground";
        ContentName = nameStem + "RowContent";
        LabelName = nameStem + "RowLabel";
        ValueName = nameStem + "RowValue";
        SecondaryValueName = hasSecondaryValue ? nameStem + "RowSecondaryValue" : null;
        LabelTextKey = labelTextKey;
        LabelEnglishFallback = labelEnglishFallback;
        HasSecondaryValue = hasSecondaryValue;
    }

    public ProfileSummaryMetric Metric { get; }
    public string BackgroundName { get; }
    public string ContentName { get; }
    public string LabelName { get; }
    public string ValueName { get; }
    public string? SecondaryValueName { get; }
    public string LabelTextKey { get; }
    public string LabelEnglishFallback { get; }
    public bool HasSecondaryValue { get; }
}

internal sealed class RetainedProfileSummaryRowCanvasLayout
{
    public RetainedProfileSummaryRowSpecification Specification { get; set; } = null!;
    public RetainedOverviewFirstStatisticsRowCanvasLayout Surface { get; set; } = null!;
    public RetainedTwoColumnStatisticsRowCanvasLayout Entry { get; set; } = null!;
}

internal static class RetainedProfileSummaryRowsPolicy
{
    public const int RowCount = 11;
    public const float RowGapPixels = 10f;
    public const float RowStepPixels = 76f;
    public const float LastRowTopPixels = 1216f;
    public const float LastRowBottomExclusivePixels = 1282f;
    public const float EconomyPrimaryValueWidthPixels = 310f;
    public const float EconomySecondaryValueWidthPixels = 300f;

    private static readonly RetainedProfileSummaryRowSpecification[] Rows =
    {
        new(ProfileSummaryMetric.TotalRuns, "OverviewFirstStatistics", "ui.overview_total_runs", "Total runs"),
        new(ProfileSummaryMetric.ExtractionRate, "OverviewExtractionRate", "ui.overview_extraction_rate", "Extraction rate"),
        new(ProfileSummaryMetric.TotalActiveRaidTime, "OverviewTotalActiveRaidTime", "ui.overview_total_active_raid_time", "Total active raid time"),
        new(ProfileSummaryMetric.TotalDistanceTravelled, "OverviewTotalDistanceTravelled", "ui.overview_total_distance_travelled", "Total distance travelled"),
        new(ProfileSummaryMetric.KillsByYou, "OverviewKillsByYou", "ui.overview_kills_by_you", "Kills by you"),
        new(ProfileSummaryMetric.Deaths, "OverviewDeaths", "ui.overview_deaths", "Deaths"),
        new(ProfileSummaryMetric.DamageDealt, "OverviewDamageDealt", "ui.overview_damage_dealt", "Damage dealt"),
        new(ProfileSummaryMetric.DamageTaken, "OverviewDamageTaken", "ui.overview_damage_taken", "Damage taken"),
        new(ProfileSummaryMetric.HealthRestored, "OverviewHealthRestored", "ui.overview_hp_restored", "HP restored"),
        new(ProfileSummaryMetric.UniqueContainersOpened, "OverviewUniqueContainersOpened", "ui.overview_unique_containers_opened", "Unique containers opened"),
        new(ProfileSummaryMetric.Economy, "OverviewEconomy", "ui.overview_economy", "Economy", hasSecondaryValue: true)
    };

    public static IReadOnlyList<RetainedProfileSummaryRowSpecification> Specifications => Rows;

    public static IReadOnlyList<RetainedProfileSummaryRowCanvasLayout> CreateCanvasLayouts(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewPanelCanvasLayout leftPanel)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (leftPanel == null) throw new ArgumentNullException(nameof(leftPanel));
        var result = new RetainedProfileSummaryRowCanvasLayout[Rows.Length];
        var padding = referenceTransform.CanvasLength(RetainedOverviewFirstStatisticsRowPolicy.ContentPaddingPixels);
        for (var index = 0; index < Rows.Length; index++)
        {
            var left = leftPanel.ContentLeft;
            var top = leftPanel.ContentTop + referenceTransform.CanvasLength(
                RetainedOverviewFirstStatisticsRowPolicy.ContentTopOffsetPixels + RowStepPixels * index);
            var surface = index == 0
                ? RetainedOverviewFirstStatisticsRowPolicy.CreateCanvasLayout(referenceTransform, leftPanel)
                : new RetainedOverviewFirstStatisticsRowCanvasLayout
                {
                    ReferenceTransform = referenceTransform,
                    Left = left,
                    Top = top,
                    Width = leftPanel.ContentWidth,
                    Height = referenceTransform.CanvasLength(RetainedOverviewFirstStatisticsRowPolicy.HeightPixels),
                    CornerRadius = referenceTransform.CanvasLength(RetainedOverviewFirstStatisticsRowPolicy.CornerRadiusPixels),
                    ContentLeft = left + padding,
                    ContentTop = top + padding,
                    ContentWidth = leftPanel.ContentWidth - padding * 2f,
                    ContentHeight = referenceTransform.CanvasLength(
                        RetainedOverviewFirstStatisticsRowPolicy.HeightPixels
                        - RetainedOverviewFirstStatisticsRowPolicy.ContentPaddingPixels * 2f)
                };
            var entry = Rows[index].HasSecondaryValue
                ? RetainedThreeColumnStatisticsRowLayoutPolicy.CreateCanvasLayout(
                    referenceTransform,
                    surface,
                    RetainedOverviewFirstStatisticsRowEntryPolicy.LabelColumnWidthPixels,
                    EconomyPrimaryValueWidthPixels,
                    RetainedOverviewFirstStatisticsRowEntryPolicy.ReferenceFontSize)
                : RetainedTwoColumnStatisticsRowLayoutPolicy.CreateCanvasLayout(
                    referenceTransform,
                    surface,
                    RetainedOverviewFirstStatisticsRowEntryPolicy.LabelColumnWidthPixels,
                    RetainedOverviewFirstStatisticsRowEntryPolicy.ReferenceFontSize);
            result[index] = new RetainedProfileSummaryRowCanvasLayout
            {
                Specification = Rows[index],
                Surface = surface,
                Entry = entry
            };
        }

        return result;
    }
}

internal sealed class ProfileSummaryRowPresentation
{
    public ProfileSummaryMetric Metric { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? SecondaryValue { get; set; }
}

internal static class ProfileSummaryPresentationFactory
{
    public static IReadOnlyList<ProfileSummaryRowPresentation> Create(
        StatisticsPanelProjection projection,
        Func<string, string> text)
    {
        if (projection == null) throw new ArgumentNullException(nameof(projection));
        if (text == null) throw new ArgumentNullException(nameof(text));
        return RetainedProfileSummaryRowsPolicy.Specifications.Select(specification =>
        {
            var values = FormatValues(specification.Metric, projection, text);
            return new ProfileSummaryRowPresentation
            {
                Metric = specification.Metric,
                Label = text(specification.LabelTextKey),
                Value = values.Value,
                SecondaryValue = values.SecondaryValue
            };
        }).ToArray();
    }

    private static (string Value, string? SecondaryValue) FormatValues(
        ProfileSummaryMetric metric,
        StatisticsPanelProjection projection,
        Func<string, string> text) => metric switch
    {
        ProfileSummaryMetric.TotalRuns => (
            RetainedOverviewFirstStatisticsRowEntryPolicy.FormatProjectedValue(projection), null),
        ProfileSummaryMetric.ExtractionRate => (FormatExtractionRate(projection.Runs, text), null),
        ProfileSummaryMetric.TotalActiveRaidTime => (FormatActiveRaidTime(projection.Runs.Runs, text), null),
        ProfileSummaryMetric.TotalDistanceTravelled => (
            FormatDistance(projection.Runs.PhysicalDistance, projection.Runs.MovementSupported, text), null),
        ProfileSummaryMetric.KillsByYou => (
            FormatCapabilityInteger(
                projection.Combat.Lifetime.Totals.KillsByYou,
                projection.Combat.Capabilities.KillsByYou,
                text), null),
        ProfileSummaryMetric.Deaths => (FormatInteger(projection.Runs.DiedRuns), null),
        ProfileSummaryMetric.DamageDealt => (
            FormatCapabilityDecimal(
                projection.Combat.Lifetime.Totals.DamageDealt,
                projection.Combat.Capabilities.DamageDealt,
                text), null),
        ProfileSummaryMetric.DamageTaken => (
            FormatCapabilityDecimal(
                projection.Combat.Lifetime.Totals.DamageReceived,
                projection.Combat.Capabilities.DamageReceived,
                text), null),
        ProfileSummaryMetric.HealthRestored => (
            FormatFiniteDecimal(projection.Profile.Statistics.Overall.ActualHealthRestored, text), null),
        ProfileSummaryMetric.UniqueContainersOpened => (
            UiText.FormatContainers(
                projection.Containers.Lifetime,
                projection.Containers.CurrentCapability,
                text), null),
        ProfileSummaryMetric.Economy => (
            text("ui.overview_money_net") + " " + FormatEconomyNet(
                projection.Economy,
                CurrencyKind.Money,
                projection.Economy.Capabilities.MoneyAmountDirection,
                projection.CurrentEconomyCapabilities.MoneyAmountDirection,
                text),
            text("ui.overview_cash_net") + " " + FormatEconomyNet(
                projection.Economy,
                CurrencyKind.Cash,
                projection.Economy.Capabilities.CashAmountDirection,
                projection.CurrentEconomyCapabilities.CashAmountDirection,
                text)),
        _ => throw new ArgumentOutOfRangeException(nameof(metric))
    };

    private static string FormatExtractionRate(RunStatisticsViewModel runs, Func<string, string> text)
    {
        var extracted = Math.Max(0L, runs.ExtractedRuns);
        var totalRuns = Math.Max(0L, runs.TotalRuns);
        if (totalRuns == 0L) return $"{text("ui.em_dash")} - 0/0";
        var percent = Math.Round(extracted * 100m / totalRuns, 0, MidpointRounding.AwayFromZero);
        return $"{percent.ToString("0", CultureInfo.InvariantCulture)}% - {FormatInteger(extracted)}/{FormatInteger(totalRuns)}";
    }

    private static string FormatActiveRaidTime(IReadOnlyList<RunSummary> runs, Func<string, string> text)
    {
        var totalSeconds = 0d;
        foreach (var run in runs ?? Array.Empty<RunSummary>())
        {
            var seconds = run.ActiveDurationSeconds;
            if (seconds < 0d || double.IsNaN(seconds) || double.IsInfinity(seconds)) continue;
            totalSeconds += seconds;
            if (double.IsInfinity(totalSeconds)) return text("ui.unavailable");
        }

        var totalMillisecondsValue = Math.Round(totalSeconds * 1000d, MidpointRounding.AwayFromZero);
        if (totalMillisecondsValue > long.MaxValue) return text("ui.unavailable");
        var totalMilliseconds = (long)totalMillisecondsValue;
        var milliseconds = totalMilliseconds % 1000;
        var totalSecondsWhole = totalMilliseconds / 1000;
        var secondsPart = totalSecondsWhole % 60;
        var totalMinutes = totalSecondsWhole / 60;
        var minutesPart = totalMinutes % 60;
        var hours = totalMinutes / 60;
        return hours > 0
            ? $"{hours.ToString(CultureInfo.InvariantCulture)}:{minutesPart:00}:{secondsPart:00}.{milliseconds:000}"
            : $"{totalMinutes.ToString(CultureInfo.InvariantCulture)}:{secondsPart:00}.{milliseconds:000}";
    }

    private static string FormatDistance(double meters, bool supported, Func<string, string> text) =>
        !supported
            ? text("ui.unsupported")
            : IsFiniteNonNegative(meters)
                ? $"{meters.ToString("#,0.00", CultureInfo.InvariantCulture)} m"
                : text("ui.unavailable");

    private static string FormatCapabilityInteger(
        long value,
        MetricAvailability availability,
        Func<string, string> text) => availability.State == AdapterCapabilityState.DisabledIncompatible
        ? text("ui.unsupported")
        : FormatInteger(value);

    private static string FormatCapabilityDecimal(
        double value,
        MetricAvailability availability,
        Func<string, string> text) => availability.State == AdapterCapabilityState.DisabledIncompatible
        ? text("ui.unsupported")
        : FormatFiniteDecimal(value, text);

    private static string FormatFiniteDecimal(double value, Func<string, string> text) =>
        IsFiniteNonNegative(value)
            ? value.ToString("#,0.00", CultureInfo.InvariantCulture)
            : text("ui.unavailable");

    private static string FormatEconomyNet(
        EconomyStatisticsAggregate economy,
        CurrencyKind kind,
        MetricAvailability scopeAvailability,
        MetricAvailability currentAvailability,
        Func<string, string> text)
    {
        var key = kind.ToString();
        var hasCurrency = economy.Currencies.TryGetValue(key, out var currency);
        string result;
        if (!hasCurrency && economy.HistoricalUnavailable)
        {
            result = text("ui.unavailable");
        }
        else if (!hasCurrency)
        {
            result = scopeAvailability.State == AdapterCapabilityState.DisabledIncompatible
                ? text("ui.unsupported")
                : "0";
        }
        else
        {
            var netFlow = currency!.Totals.NetFlow;
            result = FormatSignedInteger(netFlow);
            if (scopeAvailability.State == AdapterCapabilityState.DisabledIncompatible)
            {
                if (netFlow == 0)
                {
                    result = text("ui.unsupported");
                }
                else
                {
                    var scope = currentAvailability.State == AdapterCapabilityState.DisabledIncompatible
                        ? text("ui.current_capture_unavailable")
                        : text("ui.scope_capture_partly_unavailable");
                    result = $"{result} ({scope})";
                }
            }
        }

        if (economy.HistoricalUnavailable)
            result = $"{result} ({text("ui.pre_m9_unavailable")})";
        if (economy.WasRepairedFromInvalidState)
            result = $"{result} ({text("ui.repaired_unavailable")})";
        var saturated = kind == CurrencyKind.Money
            ? economy.MoneyArithmeticSaturated
            : economy.CashArithmeticSaturated;
        return saturated ? $"{result} ({text("ui.capture_incomplete")})" : result;
    }

    private static string FormatInteger(long value) => value.ToString("#,0", CultureInfo.InvariantCulture);

    private static string FormatSignedInteger(long value) =>
        value > 0
            ? "+" + FormatInteger(value)
            : FormatInteger(value);

    private static bool IsFiniteNonNegative(double value) =>
        value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
}

internal enum OverviewHighlightMetric
{
    FastestExtraction,
    LongestSuccessfulRaid,
    MostUsedWeapon,
    MostUsedConsumable
}

internal sealed class OverviewHighlightPresentation
{
    public OverviewHighlightMetric Metric { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

internal static class FastestExtractionHighlightPresentationFactory
{
    public static OverviewHighlightPresentation Create(
        StatisticsPanelProjection projection,
        Func<string, string> text) => OverviewHighlightsPresentationFactory.Create(projection, text)[0];
}

internal static class OverviewHighlightsPresentationFactory
{
    public static IReadOnlyList<OverviewHighlightPresentation> Create(
        StatisticsPanelProjection projection,
        Func<string, string> text)
    {
        if (projection == null) throw new ArgumentNullException(nameof(projection));
        if (text == null) throw new ArgumentNullException(nameof(text));

        return RetainedOverviewHighlightsRowsPolicy.Specifications.Select(specification =>
            new OverviewHighlightPresentation
            {
                Metric = specification.Metric,
                Label = text(specification.LabelTextKey),
                Value = specification.Metric switch
                {
                    OverviewHighlightMetric.FastestExtraction => FormatDurationRecord(
                        projection.Runs.Records?.Extraction?.Shortest,
                        routeDisplayName: null,
                        text),
                    OverviewHighlightMetric.LongestSuccessfulRaid => FormatLongestSuccessfulRaid(projection, text),
                    OverviewHighlightMetric.MostUsedWeapon => FormatMostUsedWeapon(projection, text),
                    OverviewHighlightMetric.MostUsedConsumable => FormatMostUsedConsumable(projection, text),
                    _ => text("ui.unavailable")
                }
            }).ToArray();
    }

    private static string FormatLongestSuccessfulRaid(
        StatisticsPanelProjection projection,
        Func<string, string> text)
    {
        var record = projection.Runs.Records?.Extraction?.Longest;
        if (record == null) return text("ui.em_dash");

        string? routeDisplayName = null;
        if (!string.IsNullOrWhiteSpace(record.RunId))
        {
            var run = projection.Runs.Runs.FirstOrDefault(candidate =>
                string.Equals(candidate.RunId, record.RunId, StringComparison.Ordinal));
            if (run != null && UiText.HasAvailableSegments(run))
            {
                var mapDisplayNames = run.Segments
                    .OrderBy(segment => segment.SegmentIndex)
                    .Select(segment => segment.MapDisplayName)
                    .ToArray();
                if (!string.IsNullOrWhiteSpace(mapDisplayNames[0]) && !string.IsNullOrWhiteSpace(mapDisplayNames[mapDisplayNames.Length - 1]))
                    routeDisplayName = mapDisplayNames.Length == 1 ? mapDisplayNames[0]
                        : mapDisplayNames[0] + " → " + mapDisplayNames[mapDisplayNames.Length - 1];
            }
        }

        return FormatDurationRecord(record, routeDisplayName, text);
    }

    private static string FormatDurationRecord(
        DurationRecordReference? record,
        string? routeDisplayName,
        Func<string, string> text)
    {
        if (record == null) return text("ui.em_dash");
        if (!IsFiniteNonNegative(record.ActiveDurationSeconds)
            || string.IsNullOrWhiteSpace(record.MapDisplayName))
        {
            return text("ui.unavailable");
        }

        if (!RetainedRunDurationFormatter.TryFormat(record.ActiveDurationSeconds, out var duration))
            return text("ui.unavailable");
        return $"{duration} - {routeDisplayName ?? record.MapDisplayName}";
    }

    private static string FormatMostUsedWeapon(
        StatisticsPanelProjection projection,
        Func<string, string> text)
    {
        var lifetime = projection.Weapons.Lifetime;
        var capabilities = projection.Weapons.Capabilities;
        var groups = projection.WeaponAmmunitionGroups;
        if (lifetime == null || capabilities == null || groups == null
            || lifetime.Totals == null || capabilities.FiringActions == null
            || capabilities.WeaponIdentity == null || lifetime.WasRepairedFromInvalidState)
        {
            return text("ui.unavailable");
        }

        var candidates = groups
            .Where(value => value != null
                && !string.IsNullOrWhiteSpace(value.WeaponId)
                && value.TotalFiringActions > 0)
            .OrderByDescending(value => value.TotalFiringActions)
            .ThenBy(
                value => StatisticsPanelProjectionFactory.StableDisplayName(value.DisplayName, value.WeaponId),
                StringComparer.Ordinal)
            .ThenBy(value => value.WeaponId, StringComparer.Ordinal)
            .ToArray();
        var firingActionsState = capabilities.FiringActions.State;
        var weaponIdentityState = capabilities.WeaponIdentity.State;
        var requiredFunctionalityUnsupported =
            firingActionsState == AdapterCapabilityState.DisabledIncompatible
            || weaponIdentityState == AdapterCapabilityState.DisabledIncompatible;
        var completeSupportedCapture = firingActionsState == AdapterCapabilityState.Supported
            && weaponIdentityState == AdapterCapabilityState.Supported;
        if (!completeSupportedCapture)
        {
            return candidates.Length == 0 && requiredFunctionalityUnsupported
                ? text("ui.unsupported")
                : text("ui.unavailable");
        }

        if (lifetime.Totals.FiringActions < 0
            || groups.Any(value => value == null || value.TotalFiringActions < 0))
        {
            return text("ui.unavailable");
        }

        long identifiedFiringActions;
        try
        {
            var identifiedWeaponIds = new HashSet<string>(StringComparer.Ordinal);
            identifiedFiringActions = 0;
            foreach (var group in groups.Where(value => value != null && !string.IsNullOrWhiteSpace(value.WeaponId)))
            {
                if (!identifiedWeaponIds.Add(group.WeaponId)) return text("ui.unavailable");
                identifiedFiringActions = checked(identifiedFiringActions + group.TotalFiringActions);
            }
        }
        catch (OverflowException)
        {
            return text("ui.unavailable");
        }

        if (identifiedFiringActions > lifetime.Totals.FiringActions) return text("ui.unavailable");
        var unattributedFiringActions = lifetime.Totals.FiringActions - identifiedFiringActions;
        if (candidates.Length == 0)
        {
            return lifetime.Totals.FiringActions == 0
                ? text("ui.em_dash")
                : text("ui.unavailable");
        }

        if (unattributedFiringActions > 0)
        {
            try
            {
                var nextHighestPossibleTotal = checked(
                    (candidates.Length > 1 ? candidates[1].TotalFiringActions : 0)
                    + unattributedFiringActions);
                if (candidates[0].TotalFiringActions <= nextHighestPossibleTotal)
                    return text("ui.unavailable");
            }
            catch (OverflowException)
            {
                return text("ui.unavailable");
            }
        }

        var winner = candidates[0];
        return $"{StatisticsPanelProjectionFactory.StableDisplayName(winner.DisplayName, winner.WeaponId)}"
            + $" - {FormatInteger(winner.TotalFiringActions)} {text("ui.overview_firing_actions_unit")}";
    }

    private static string FormatMostUsedConsumable(
        StatisticsPanelProjection projection,
        Func<string, string> text)
    {
        var itemUse = projection.ItemUse;
        if (itemUse == null || itemUse.Overall == null || itemUse.Items == null
            || itemUse.HistoricalUnavailable || itemUse.WasRepairedFromInvalidState
            || itemUse.Overall.ActivationCount < 0
            || itemUse.Items.Any(value => value == null
                || value.Totals == null
                || value.Totals.ActivationCount < 0))
        {
            return text("ui.unavailable");
        }

        long attributedActivationCount;
        try
        {
            var identifiedItemIds = new HashSet<string>(StringComparer.Ordinal);
            attributedActivationCount = 0;
            foreach (var item in itemUse.Items)
            {
                if (string.IsNullOrWhiteSpace(item.ItemId) || !identifiedItemIds.Add(item.ItemId))
                    return text("ui.unavailable");
                attributedActivationCount = checked(attributedActivationCount + item.Totals.ActivationCount);
            }
        }
        catch (OverflowException)
        {
            return text("ui.unavailable");
        }

        if (attributedActivationCount != itemUse.Overall.ActivationCount)
            return text("ui.unavailable");
        var winner = itemUse.Items
            .Where(value => value.Totals.ActivationCount > 0)
            .OrderByDescending(value => value.Totals.ActivationCount)
            .ThenBy(
                value => StatisticsPanelProjectionFactory.StableDisplayName(value.DisplayName, value.ItemId),
                StringComparer.Ordinal)
            .ThenBy(value => value.ItemId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (winner == null) return text("ui.em_dash");
        return $"{StatisticsPanelProjectionFactory.StableDisplayName(winner.DisplayName, winner.ItemId)}"
            + $" - {FormatInteger(winner.Totals.ActivationCount)} {text("ui.overview_uses_unit")}";
    }

    private static string FormatInteger(long value) => value.ToString("#,0", CultureInfo.InvariantCulture);

    private static bool IsFiniteNonNegative(double value) =>
        value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
}

internal static class RetainedRunDurationFormatter
{
    public static bool TryFormat(double durationSeconds, out string formatted)
    {
        formatted = string.Empty;
        if (double.IsNaN(durationSeconds)
            || double.IsInfinity(durationSeconds)
            || durationSeconds < 0d)
        {
            return false;
        }

        var totalMillisecondsValue = Math.Round(
            durationSeconds * 1000d,
            MidpointRounding.AwayFromZero);
        if (totalMillisecondsValue > long.MaxValue) return false;
        var totalMilliseconds = (long)totalMillisecondsValue;
        var milliseconds = totalMilliseconds % 1000;
        var totalSeconds = totalMilliseconds / 1000;
        var seconds = totalSeconds % 60;
        var totalMinutes = totalSeconds / 60;
        var minutes = totalMinutes % 60;
        var hours = totalMinutes / 60;
        formatted = hours > 0
            ? string.Format(
                CultureInfo.InvariantCulture,
                "{0}:{1:00}:{2:00}.{3:000}",
                hours,
                minutes,
                seconds,
                milliseconds)
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}.{2:000}",
                totalMinutes,
                seconds,
                milliseconds);
        return true;
    }
}

internal static class RetainedOverviewRightPanelPolicy
{
    public const string BackgroundName = "OverviewRightPanelBackground";
    public const string ContentName = "OverviewRightPanelContent";
    public const StatisticsPanelTab OwnerTab = RetainedOverviewPanelStylePolicy.OwnerTab;
    public const float LeftPixels = 1300f;
    public const float RightExclusivePixels = 2475f;
    public const float PanelGapPixels = 40f;
    public const float ScreenRightMarginPixels = 85f;

    public static RetainedOverviewPanelCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform) =>
        RetainedOverviewPanelStylePolicy.CreateCanvasLayout(referenceTransform, LeftPixels);
}

internal sealed class RetainedOverviewHighlightsHeadingCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float FontSize { get; set; }
    public float OpticalOffsetX { get; set; }
    public float OpticalOffsetY { get; set; }
}

internal static class RetainedOverviewHighlightsHeadingPolicy
{
    public const string Name = "OverviewHighlightsHeading";
    public const string ParentName = RetainedOverviewRightPanelPolicy.ContentName;
    public const string TextKey = "ui.overview_highlights";
    public const string EnglishFallback = "Highlights";
    public const StatisticsPanelTab OwnerTab = RetainedOverviewRightPanelPolicy.OwnerTab;
    public const string FontAssetName = RetainedOverviewProfileSummaryHeadingPolicy.FontAssetName;
    public const string SourceMaterialName = RetainedOverviewProfileSummaryHeadingPolicy.SourceMaterialName;
    public const string MaterialName = RetainedOverviewProfileSummaryHeadingPolicy.MaterialName;
    public const float ReferenceFontSize = RetainedOverviewProfileSummaryHeadingPolicy.ReferenceFontSize;
    public const float HeightPixels = RetainedOverviewProfileSummaryHeadingPolicy.HeightPixels;
    public const float ReferenceOpticalOffsetX = RetainedOverviewProfileSummaryHeadingPolicy.ReferenceOpticalOffsetX;
    public const float ReferenceOpticalOffsetY = RetainedOverviewProfileSummaryHeadingPolicy.ReferenceOpticalOffsetY;
    public const float Red = RetainedOverviewProfileSummaryHeadingPolicy.Red;
    public const float Green = RetainedOverviewProfileSummaryHeadingPolicy.Green;
    public const float Blue = RetainedOverviewProfileSummaryHeadingPolicy.Blue;
    public const float Alpha = RetainedOverviewProfileSummaryHeadingPolicy.Alpha;
    public const bool BlocksRaycasts = RetainedOverviewProfileSummaryHeadingPolicy.BlocksRaycasts;
    public const bool WordWrapping = RetainedOverviewProfileSummaryHeadingPolicy.WordWrapping;
    public const bool AutoSizing = RetainedOverviewProfileSummaryHeadingPolicy.AutoSizing;
    public const bool UsesTopLeftAlignment = RetainedOverviewProfileSummaryHeadingPolicy.UsesTopLeftAlignment;
    public const bool UsesOwnedTabLabelMaterial = RetainedOverviewProfileSummaryHeadingPolicy.UsesOwnedTabLabelMaterial;
    public const bool UsesZeroTextMargin = RetainedOverviewProfileSummaryHeadingPolicy.UsesZeroTextMargin;
    public const float AdditionalPaddingPixels = RetainedOverviewProfileSummaryHeadingPolicy.AdditionalPaddingPixels;
    public const bool UsesFixedOpticalOffset = RetainedOverviewProfileSummaryHeadingPolicy.UsesFixedOpticalOffset;
    public const bool UsesHorizontalScaleCompensation =
        RetainedOverviewProfileSummaryHeadingPolicy.UsesHorizontalScaleCompensation;
    public const float CharacterSpacing = 0f;
    public const float WordSpacing = 0f;
    public const float LineSpacing = 0f;
    public const float ParagraphSpacing = 0f;
    public const bool UsesNormalStyle = true;
    public const bool UsesRegularWeight = true;
    public const bool UsesVisibleOverflow = true;

    public static RetainedOverviewHighlightsHeadingCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewPanelCanvasLayout rightPanel)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (rightPanel == null) throw new ArgumentNullException(nameof(rightPanel));
        return new RetainedOverviewHighlightsHeadingCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Left = rightPanel.ContentLeft,
            Top = rightPanel.ContentTop,
            Width = rightPanel.ContentWidth,
            Height = referenceTransform.CanvasLength(HeightPixels),
            FontSize = referenceTransform.CanvasLength(ReferenceFontSize),
            OpticalOffsetX = referenceTransform.CanvasLength(ReferenceOpticalOffsetX),
            OpticalOffsetY = referenceTransform.CanvasLength(ReferenceOpticalOffsetY)
        };
    }
}

internal sealed class RetainedOverviewFastestExtractionRowCanvasLayout
{
    public RetainedOverviewHighlightRowSpecification Specification { get; set; } = null!;
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float CornerRadius { get; set; }
    public float ContentLeft { get; set; }
    public float ContentTop { get; set; }
    public float ContentWidth { get; set; }
    public float ContentHeight { get; set; }
}

internal sealed class RetainedOverviewHighlightRowSpecification
{
    public OverviewHighlightMetric Metric { get; set; }
    public string RowName { get; set; } = string.Empty;
    public string LabelName { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public string LabelTextKey { get; set; } = string.Empty;
    public string LabelEnglishFallback { get; set; } = string.Empty;
    public float TopOffsetPixels { get; set; }
}

internal static class RetainedOverviewHighlightsRowsPolicy
{
    public const int RowCount = 4;
    public const float RowStepPixels = 76f;
    public const float RowGapPixels = RowStepPixels - RetainedOverviewFastestExtractionRowPolicy.HeightPixels;

    public static IReadOnlyList<RetainedOverviewHighlightRowSpecification> Specifications { get; } =
        new[]
        {
            new RetainedOverviewHighlightRowSpecification
            {
                Metric = OverviewHighlightMetric.FastestExtraction,
                RowName = RetainedOverviewFastestExtractionRowPolicy.Name,
                LabelName = RetainedOverviewFastestExtractionEntryPolicy.LabelName,
                ValueName = RetainedOverviewFastestExtractionEntryPolicy.ValueName,
                LabelTextKey = RetainedOverviewFastestExtractionEntryPolicy.LabelTextKey,
                LabelEnglishFallback = RetainedOverviewFastestExtractionEntryPolicy.LabelEnglishFallback,
                TopOffsetPixels = RetainedOverviewFastestExtractionRowPolicy.TopOffsetPixels
            },
            new RetainedOverviewHighlightRowSpecification
            {
                Metric = OverviewHighlightMetric.LongestSuccessfulRaid,
                RowName = "OverviewLongestSuccessfulRaidRow",
                LabelName = "OverviewLongestSuccessfulRaidLabel",
                ValueName = "OverviewLongestSuccessfulRaidValue",
                LabelTextKey = "ui.overview_longest_successful_raid",
                LabelEnglishFallback = "Longest successful raid",
                TopOffsetPixels = RetainedOverviewFastestExtractionRowPolicy.TopOffsetPixels + RowStepPixels
            },
            new RetainedOverviewHighlightRowSpecification
            {
                Metric = OverviewHighlightMetric.MostUsedWeapon,
                RowName = "OverviewMostUsedWeaponRow",
                LabelName = "OverviewMostUsedWeaponLabel",
                ValueName = "OverviewMostUsedWeaponValue",
                LabelTextKey = "ui.overview_most_used_weapon",
                LabelEnglishFallback = "Most-used weapon",
                TopOffsetPixels = RetainedOverviewFastestExtractionRowPolicy.TopOffsetPixels + RowStepPixels * 2f
            },
            new RetainedOverviewHighlightRowSpecification
            {
                Metric = OverviewHighlightMetric.MostUsedConsumable,
                RowName = "OverviewMostUsedConsumableRow",
                LabelName = "OverviewMostUsedConsumableLabel",
                ValueName = "OverviewMostUsedConsumableValue",
                LabelTextKey = "ui.overview_most_used_consumable",
                LabelEnglishFallback = "Most-used consumable",
                TopOffsetPixels = RetainedOverviewFastestExtractionRowPolicy.TopOffsetPixels + RowStepPixels * 3f
            }
        };

    public static IReadOnlyList<RetainedOverviewFastestExtractionRowCanvasLayout> CreateRowCanvasLayouts(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewPanelCanvasLayout rightPanel) => Specifications
        .Select(specification => RetainedOverviewFastestExtractionRowPolicy.CreateCanvasLayout(
            referenceTransform,
            rightPanel,
            specification))
        .ToArray();

    public static IReadOnlyList<RetainedTwoColumnStatisticsRowCanvasLayout> CreateEntryCanvasLayouts(
        RetainedReferenceTransform referenceTransform,
        IReadOnlyList<RetainedOverviewFastestExtractionRowCanvasLayout> rows)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (rows == null) throw new ArgumentNullException(nameof(rows));
        return rows.Select(row => RetainedOverviewFastestExtractionEntryPolicy.CreateCanvasLayout(
            referenceTransform,
            row)).ToArray();
    }
}

internal static class RetainedOverviewFastestExtractionRowPolicy
{
    public const string Name = "OverviewFastestExtractionRow";
    public const string ParentName = RetainedOverviewRightPanelPolicy.ContentName;
    public const StatisticsPanelTab OwnerTab = RetainedOverviewRightPanelPolicy.OwnerTab;
    public const float TopOffsetPixels = RetainedOverviewFirstStatisticsRowPolicy.ContentTopOffsetPixels;
    public const float HeightPixels = RetainedOverviewFirstStatisticsRowPolicy.HeightPixels;
    public const float ContentPaddingPixels = RetainedOverviewFirstStatisticsRowPolicy.ContentPaddingPixels;
    public const float CornerRadiusPixels = RetainedOverviewFirstStatisticsRowPolicy.CornerRadiusPixels;
    public const float Red = RetainedOverviewFirstStatisticsRowPolicy.Red;
    public const float Green = RetainedOverviewFirstStatisticsRowPolicy.Green;
    public const float Blue = RetainedOverviewFirstStatisticsRowPolicy.Blue;
    public const float LayerAlpha = RetainedOverviewFirstStatisticsRowPolicy.LayerAlpha;
    public const bool HasGraphic = true;
    public const bool BlocksRaycasts = true;

    public static RetainedOverviewFastestExtractionRowCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewPanelCanvasLayout rightPanel) => CreateCanvasLayout(
            referenceTransform,
            rightPanel,
            RetainedOverviewHighlightsRowsPolicy.Specifications[0]);

    internal static RetainedOverviewFastestExtractionRowCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewPanelCanvasLayout rightPanel,
        RetainedOverviewHighlightRowSpecification specification)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (rightPanel == null) throw new ArgumentNullException(nameof(rightPanel));
        if (specification == null) throw new ArgumentNullException(nameof(specification));
        var padding = referenceTransform.CanvasLength(ContentPaddingPixels);
        var left = rightPanel.ContentLeft;
        var top = rightPanel.ContentTop + referenceTransform.CanvasLength(specification.TopOffsetPixels);
        return new RetainedOverviewFastestExtractionRowCanvasLayout
        {
            Specification = specification,
            ReferenceTransform = referenceTransform,
            Left = left,
            Top = top,
            Width = rightPanel.ContentWidth,
            Height = referenceTransform.CanvasLength(HeightPixels),
            CornerRadius = referenceTransform.CanvasLength(CornerRadiusPixels),
            ContentLeft = left + padding,
            ContentTop = top + padding,
            ContentWidth = rightPanel.ContentWidth - padding * 2f,
            ContentHeight = referenceTransform.CanvasLength(HeightPixels - ContentPaddingPixels * 2f)
        };
    }
}

internal static class RetainedOverviewFastestExtractionEntryPolicy
{
    public const string LabelName = "OverviewFastestExtractionLabel";
    public const string ValueName = "OverviewFastestExtractionValue";
    public const string ParentName = RetainedOverviewFastestExtractionRowPolicy.Name;
    public const StatisticsPanelTab OwnerTab = RetainedOverviewFastestExtractionRowPolicy.OwnerTab;
    public const string LabelTextKey = "ui.overview_fastest_extraction";
    public const string LabelEnglishFallback = "Fastest extraction";
    public const string FontAssetName = RetainedOverviewFirstStatisticsRowEntryPolicy.FontAssetName;
    public const string MaterialName = RetainedOverviewFirstStatisticsRowEntryPolicy.MaterialName;
    public const float ReferenceFontSize = RetainedOverviewFirstStatisticsRowEntryPolicy.ReferenceFontSize;
    public const float LabelColumnWidthPixels = 440f;
    public const float CharacterSpacing = RetainedOverviewFirstStatisticsRowEntryPolicy.CharacterSpacing;
    public const float WordSpacing = RetainedOverviewFirstStatisticsRowEntryPolicy.WordSpacing;
    public const float LineSpacing = RetainedOverviewFirstStatisticsRowEntryPolicy.LineSpacing;
    public const float ParagraphSpacing = RetainedOverviewFirstStatisticsRowEntryPolicy.ParagraphSpacing;
    public const float Red = RetainedOverviewFirstStatisticsRowEntryPolicy.Red;
    public const float Green = RetainedOverviewFirstStatisticsRowEntryPolicy.Green;
    public const float Blue = RetainedOverviewFirstStatisticsRowEntryPolicy.Blue;
    public const float Alpha = RetainedOverviewFirstStatisticsRowEntryPolicy.Alpha;
    public const bool BlocksRaycasts = RetainedOverviewFirstStatisticsRowEntryPolicy.BlocksRaycasts;
    public const bool WordWrapping = RetainedOverviewFirstStatisticsRowEntryPolicy.WordWrapping;
    public const bool AutoSizing = RetainedOverviewFirstStatisticsRowEntryPolicy.AutoSizing;
    public const bool UsesVisibleOverflow = false;
    public const bool UsesLeftAlignment = RetainedOverviewFirstStatisticsRowEntryPolicy.UsesLeftAlignment;
    public const bool UsesVerticalCentering = RetainedOverviewFirstStatisticsRowEntryPolicy.UsesVerticalCentering;
    public const bool UsesZeroTextMargins = RetainedOverviewFirstStatisticsRowEntryPolicy.UsesZeroTextMargins;
    public const bool UsesNormalStyle = RetainedOverviewFirstStatisticsRowEntryPolicy.UsesNormalStyle;
    public const bool UsesRegularWeight = RetainedOverviewFirstStatisticsRowEntryPolicy.UsesRegularWeight;
    public const bool UsesOwnedSubtleShadowMaterial =
        RetainedOverviewFirstStatisticsRowEntryPolicy.UsesOwnedSubtleShadowMaterial;
    public const bool UsesProjectedShortestExtraction = true;

    public static RetainedTwoColumnStatisticsRowCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewFastestExtractionRowCanvasLayout row)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (row == null) throw new ArgumentNullException(nameof(row));
        // Ellipsis checks vertical fit too. Use the full row height, preserving its center
        // and horizontal padding, so the native font does not truncate before its first glyph.
        var labelWidth = referenceTransform.CanvasLength(LabelColumnWidthPixels);
        return new RetainedTwoColumnStatisticsRowCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            LabelLeft = row.ContentLeft,
            LabelTop = row.Top,
            LabelWidth = labelWidth,
            LabelHeight = row.Height,
            ValueLeft = row.ContentLeft + labelWidth,
            ValueTop = row.Top,
            ValueWidth = row.ContentWidth - labelWidth,
            ValueHeight = row.Height,
            FontSize = referenceTransform.CanvasLength(ReferenceFontSize)
        };
    }
}

internal sealed class RetainedOverviewLatestRunHeadingCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float FontSize { get; set; }
    public float OpticalOffsetX { get; set; }
    public float OpticalOffsetY { get; set; }
}

internal static class RetainedOverviewLatestRunHeadingPolicy
{
    public const string Name = "OverviewLatestRunHeading";
    public const string ParentName = RetainedOverviewRightPanelPolicy.ContentName;
    public const string TextKey = "ui.overview_latest_run";
    public const string EnglishFallback = "Latest run";
    public const StatisticsPanelTab OwnerTab = RetainedOverviewRightPanelPolicy.OwnerTab;
    public const float TopMarginPixels = 40f;
    public const string FontAssetName = RetainedOverviewHighlightsHeadingPolicy.FontAssetName;
    public const string SourceMaterialName = RetainedOverviewHighlightsHeadingPolicy.SourceMaterialName;
    public const string MaterialName = RetainedOverviewHighlightsHeadingPolicy.MaterialName;
    public const float ReferenceFontSize = RetainedOverviewHighlightsHeadingPolicy.ReferenceFontSize;
    public const float HeightPixels = RetainedOverviewHighlightsHeadingPolicy.HeightPixels;
    public const float ReferenceOpticalOffsetX = RetainedOverviewHighlightsHeadingPolicy.ReferenceOpticalOffsetX;
    public const float ReferenceOpticalOffsetY = RetainedOverviewHighlightsHeadingPolicy.ReferenceOpticalOffsetY;
    public const float Red = RetainedOverviewHighlightsHeadingPolicy.Red;
    public const float Green = RetainedOverviewHighlightsHeadingPolicy.Green;
    public const float Blue = RetainedOverviewHighlightsHeadingPolicy.Blue;
    public const float Alpha = RetainedOverviewHighlightsHeadingPolicy.Alpha;
    public const float CharacterSpacing = RetainedOverviewHighlightsHeadingPolicy.CharacterSpacing;
    public const float WordSpacing = RetainedOverviewHighlightsHeadingPolicy.WordSpacing;
    public const float LineSpacing = RetainedOverviewHighlightsHeadingPolicy.LineSpacing;
    public const float ParagraphSpacing = RetainedOverviewHighlightsHeadingPolicy.ParagraphSpacing;
    public const bool BlocksRaycasts = RetainedOverviewHighlightsHeadingPolicy.BlocksRaycasts;
    public const bool WordWrapping = RetainedOverviewHighlightsHeadingPolicy.WordWrapping;
    public const bool AutoSizing = RetainedOverviewHighlightsHeadingPolicy.AutoSizing;
    public const bool UsesTopLeftAlignment = RetainedOverviewHighlightsHeadingPolicy.UsesTopLeftAlignment;
    public const bool UsesOwnedTabLabelMaterial = RetainedOverviewHighlightsHeadingPolicy.UsesOwnedTabLabelMaterial;
    public const bool UsesZeroTextMargin = RetainedOverviewHighlightsHeadingPolicy.UsesZeroTextMargin;
    public const float AdditionalPaddingPixels = RetainedOverviewHighlightsHeadingPolicy.AdditionalPaddingPixels;
    public const bool UsesFixedOpticalOffset = RetainedOverviewHighlightsHeadingPolicy.UsesFixedOpticalOffset;
    public const bool UsesHorizontalScaleCompensation =
        RetainedOverviewHighlightsHeadingPolicy.UsesHorizontalScaleCompensation;
    public const bool UsesNormalStyle = RetainedOverviewHighlightsHeadingPolicy.UsesNormalStyle;
    public const bool UsesRegularWeight = RetainedOverviewHighlightsHeadingPolicy.UsesRegularWeight;
    public const bool UsesVisibleOverflow = RetainedOverviewHighlightsHeadingPolicy.UsesVisibleOverflow;

    public static RetainedOverviewLatestRunHeadingCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewPanelCanvasLayout rightPanel,
        RetainedOverviewFastestExtractionRowCanvasLayout finalHighlightRow)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (rightPanel == null) throw new ArgumentNullException(nameof(rightPanel));
        if (finalHighlightRow == null) throw new ArgumentNullException(nameof(finalHighlightRow));
        return new RetainedOverviewLatestRunHeadingCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Left = rightPanel.ContentLeft,
            Top = finalHighlightRow.Top
                  + finalHighlightRow.Height
                  + referenceTransform.CanvasLength(TopMarginPixels),
            Width = rightPanel.ContentWidth,
            Height = referenceTransform.CanvasLength(HeightPixels),
            FontSize = referenceTransform.CanvasLength(ReferenceFontSize),
            OpticalOffsetX = referenceTransform.CanvasLength(ReferenceOpticalOffsetX),
            OpticalOffsetY = referenceTransform.CanvasLength(ReferenceOpticalOffsetY)
        };
    }
}

internal sealed class RetainedOverviewLatestRunCardCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float CornerRadius { get; set; }
}

internal static class RetainedOverviewLatestRunCardPolicy
{
    public const string Name = "OverviewLatestRunCard";
    public const string ParentName = RetainedOverviewRightPanelPolicy.ContentName;
    public const StatisticsPanelTab OwnerTab = RetainedOverviewRightPanelPolicy.OwnerTab;
    public const float HeadingTopOffsetPixels = 56f;
    public const float LeftPixels = 1330f;
    public const float TopPixels = 846f;
    public const float RightExclusivePixels = 1888f;
    public const float BottomExclusivePixels = 1195f;
    public const float WidthPixels = 558f;
    public const float HeightPixels = 349f;
    public const float Red = RetainedOverviewPanelStylePolicy.Red;
    public const float Green = RetainedOverviewPanelStylePolicy.Green;
    public const float Blue = RetainedOverviewPanelStylePolicy.Blue;
    public const float LayerAlpha = RetainedOverviewPanelStylePolicy.LayerAlpha;
    public const float CornerRadiusPixels = RetainedOverviewPanelStylePolicy.CornerRadiusPixels;
    public const float BorderWidth = 0f;
    public const bool HasSprite = false;
    public const bool UsesSimpleImageType = true;
    public const bool BlocksRaycasts = RetainedOverviewPanelStylePolicy.BlocksRaycasts;
    public const bool HasInteraction = false;
    public const bool HasShadow = false;

    public static RetainedOverviewLatestRunCardCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewPanelCanvasLayout rightPanel,
        RetainedOverviewLatestRunHeadingCanvasLayout latestRunHeading)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (rightPanel == null) throw new ArgumentNullException(nameof(rightPanel));
        if (latestRunHeading == null) throw new ArgumentNullException(nameof(latestRunHeading));
        return new RetainedOverviewLatestRunCardCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Left = rightPanel.ContentLeft,
            Top = latestRunHeading.Top + referenceTransform.CanvasLength(HeadingTopOffsetPixels),
            Width = referenceTransform.CanvasLength(WidthPixels),
            Height = referenceTransform.CanvasLength(HeightPixels),
            CornerRadius = referenceTransform.CanvasLength(CornerRadiusPixels)
        };
    }
}

internal enum RetainedRunBadgeState
{
    Extracted,
    Died,
    Unknown
}

internal enum RetainedRunBadgeIconKind
{
    Check,
    Skull,
    QuestionMark
}

internal sealed class RetainedRunBadgeVariantSpecification
{
    public RetainedRunBadgeVariantSpecification(
        RetainedRunBadgeState state,
        string textKey,
        string englishFallback,
        RetainedRunBadgeIconKind iconKind,
        string preferredIconGlyph,
        float backgroundRed,
        float backgroundGreen,
        float backgroundBlue,
        float visibleIconWidthPixels,
        float visibleIconHeightPixels,
        float mockEquivalentPreferredLabelWidthPixels)
    {
        State = state;
        TextKey = textKey ?? throw new ArgumentNullException(nameof(textKey));
        EnglishFallback = englishFallback ?? throw new ArgumentNullException(nameof(englishFallback));
        IconKind = iconKind;
        PreferredIconGlyph = preferredIconGlyph ?? throw new ArgumentNullException(nameof(preferredIconGlyph));
        BackgroundColor = new RetainedRgbaColor(
            backgroundRed,
            backgroundGreen,
            backgroundBlue,
            RetainedRunBadgePolicy.BackgroundAlpha);
        VisibleIconWidthPixels = visibleIconWidthPixels;
        VisibleIconHeightPixels = visibleIconHeightPixels;
        MockEquivalentPreferredLabelWidthPixels = mockEquivalentPreferredLabelWidthPixels;
        MockReferenceWidthPixels =
            RetainedRunBadgePolicy.FixedHorizontalContentPixels
            + mockEquivalentPreferredLabelWidthPixels;
    }

    public RetainedRunBadgeState State { get; }
    public string TextKey { get; }
    public string EnglishFallback { get; }
    public RetainedRunBadgeIconKind IconKind { get; }
    public string PreferredIconGlyph { get; }
    public RetainedRgbaColor BackgroundColor { get; }
    public float VisibleIconWidthPixels { get; }
    public float VisibleIconHeightPixels { get; }
    public float MockEquivalentPreferredLabelWidthPixels { get; }
    public float MockReferenceWidthPixels { get; }
}

internal sealed class RetainedRunBadgeCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public RetainedRunBadgeVariantSpecification Specification { get; set; } = null!;
    public float ReferencePreferredLabelWidth { get; set; }
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float CornerRadius { get; set; }
    public float LeftPadding { get; set; }
    public float IconSlotLeft { get; set; }
    public float IconSlotWidth { get; set; }
    public float IconLeft { get; set; }
    public float IconTop { get; set; }
    public float IconWidth { get; set; }
    public float IconHeight { get; set; }
    public float IconTextGap { get; set; }
    public float LabelLeft { get; set; }
    public float LabelTop { get; set; }
    public float LabelWidth { get; set; }
    public float LabelHeight { get; set; }
    public float RightPadding { get; set; }
    public float FontSize { get; set; }
}

internal static class RetainedRunBadgePolicy
{
    private static readonly RetainedRunBadgeVariantSpecification ExtractedSpecification =
        new RetainedRunBadgeVariantSpecification(
            RetainedRunBadgeState.Extracted,
            "ui.extracted_runs",
            "Extracted",
            RetainedRunBadgeIconKind.Check,
            "\u2713",
            109f / 255f,
            197f / 255f,
            75f / 255f,
            19f,
            14f,
            98f);

    private static readonly RetainedRunBadgeVariantSpecification DiedSpecification =
        new RetainedRunBadgeVariantSpecification(
            RetainedRunBadgeState.Died,
            "ui.died_runs",
            "Died",
            RetainedRunBadgeIconKind.Skull,
            "\u2620",
            246f / 255f,
            84f / 255f,
            101f / 255f,
            18f,
            21f,
            45f);

    private static readonly RetainedRunBadgeVariantSpecification UnknownSpecification =
        new RetainedRunBadgeVariantSpecification(
            RetainedRunBadgeState.Unknown,
            "ui.overview_run_badge_unknown",
            "Unknown",
            RetainedRunBadgeIconKind.QuestionMark,
            "?",
            133f / 255f,
            133f / 255f,
            133f / 255f,
            10f,
            16f,
            98f);

    private static readonly IReadOnlyList<RetainedRunBadgeVariantSpecification> OrderedSpecifications =
        Array.AsReadOnly(new[]
        {
            ExtractedSpecification,
            DiedSpecification,
            UnknownSpecification
        });

    public const string FontAssetName = RetainedHeaderTitlePolicy.FontAssetName;
    public const string SourceMaterialName = RetainedHeaderTitlePolicy.MaterialName;
    public const string MaterialName = RetainedTabLabelShadowPolicy.OwnedMaterialName;
    public const float HeightPixels = 30f;
    public const float CornerRadiusPixels = 5f;
    public const float LeftPaddingPixels = 5f;
    public const float IconSlotWidthPixels = 19f;
    public const float IconTextGapPixels = 5f;
    public const float LabelLeftPixels = LeftPaddingPixels + IconSlotWidthPixels + IconTextGapPixels;
    public const float RightPaddingPixels = 5f;
    public const float FixedHorizontalContentPixels = LabelLeftPixels + RightPaddingPixels;
    public const float ReferenceFontSize = 19.8f;
    public const float BackgroundAlpha = 1f;
    public const float TextRed = 1f;
    public const float TextGreen = 1f;
    public const float TextBlue = 1f;
    public const float TextAlpha = 1f;
    public const float IconRed = 1f;
    public const float IconGreen = 1f;
    public const float IconBlue = 1f;
    public const float IconAlpha = 1f;
    public const float CharacterSpacing = 0f;
    public const float WordSpacing = 0f;
    public const float LineSpacing = 0f;
    public const float ParagraphSpacing = 0f;
    public const bool BackgroundBlocksRaycasts = false;
    public const bool IconBlocksRaycasts = false;
    public const bool LabelBlocksRaycasts = false;
    public const bool HasInteraction = false;
    public const bool WordWrapping = false;
    public const bool AutoSizing = false;
    public const bool UsesVisibleOverflow = true;
    public const bool UsesZeroTextMargins = true;
    public const bool UsesLeftAlignment = true;
    public const bool UsesVerticalCentering = true;
    public const bool UsesNormalStyle = true;
    public const bool UsesRegularWeight = true;
    public const bool UsesOwnedSubtleShadowMaterial = true;
    public const bool IconIsLogicallySeparate = true;
    public const bool PrefersNativeTmpIconGlyph = true;
    public const bool HasProceduralIconFallback = true;
    public const bool IconUsesOwnedSubtleShadowMaterialWhenSupported = true;

    public static IReadOnlyList<RetainedRunBadgeVariantSpecification> Specifications =>
        OrderedSpecifications;

    public static RetainedRunBadgeVariantSpecification ResolveSpecification(
        RetainedRunBadgeState state) => state switch
    {
        RetainedRunBadgeState.Extracted => ExtractedSpecification,
        RetainedRunBadgeState.Died => DiedSpecification,
        RetainedRunBadgeState.Unknown => UnknownSpecification,
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    public static RetainedRunBadgeCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedRunBadgeState state,
        float preferredLabelWidthPixels)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        var specification = ResolveSpecification(state);
        if (!IsPositiveFinite(preferredLabelWidthPixels))
            throw new ArgumentOutOfRangeException(nameof(preferredLabelWidthPixels));

        var height = referenceTransform.CanvasLength(HeightPixels);
        var leftPadding = referenceTransform.CanvasLength(LeftPaddingPixels);
        var iconSlotWidth = referenceTransform.CanvasLength(IconSlotWidthPixels);
        var iconWidth = referenceTransform.CanvasLength(specification.VisibleIconWidthPixels);
        var iconHeight = referenceTransform.CanvasLength(specification.VisibleIconHeightPixels);
        var iconTextGap = referenceTransform.CanvasLength(IconTextGapPixels);
        var labelWidth = referenceTransform.CanvasLength(preferredLabelWidthPixels);
        var rightPadding = referenceTransform.CanvasLength(RightPaddingPixels);
        return new RetainedRunBadgeCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Specification = specification,
            ReferencePreferredLabelWidth = preferredLabelWidthPixels,
            Width = leftPadding + iconSlotWidth + iconTextGap + labelWidth + rightPadding,
            Height = height,
            CornerRadius = referenceTransform.CanvasLength(CornerRadiusPixels),
            LeftPadding = leftPadding,
            IconSlotLeft = leftPadding,
            IconSlotWidth = iconSlotWidth,
            IconLeft = leftPadding + (iconSlotWidth - iconWidth) / 2f,
            IconTop = (height - iconHeight) / 2f,
            IconWidth = iconWidth,
            IconHeight = iconHeight,
            IconTextGap = iconTextGap,
            LabelLeft = leftPadding + iconSlotWidth + iconTextGap,
            LabelTop = 0f,
            LabelWidth = labelWidth,
            LabelHeight = height,
            RightPadding = rightPadding,
            FontSize = referenceTransform.CanvasLength(ReferenceFontSize)
        };
    }

    private static bool IsPositiveFinite(float value) =>
        value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
}

internal sealed class RetainedRunBadgePresentation
{
    public bool IsVisible { get; set; }
    public RunSummary? LatestRun { get; set; }
    public RetainedRunBadgeState? State { get; set; }
    public RetainedRunBadgeVariantSpecification? Specification { get; set; }
    public string Label { get; set; } = string.Empty;
}

internal static class RetainedRunBadgePresentationFactory
{
    public static RetainedRunBadgePresentation Create(
        StatisticsPanelProjection projection,
        Func<string, string> text)
    {
        if (projection == null) throw new ArgumentNullException(nameof(projection));
        if (text == null) throw new ArgumentNullException(nameof(text));

        var runs = projection.Runs.Runs;
        if (runs.Count == 0)
            return new RetainedRunBadgePresentation { IsVisible = false };

        var latestRun = runs[0];
        var state = MapOutcome(latestRun.Outcome);
        var specification = RetainedRunBadgePolicy.ResolveSpecification(state);
        return new RetainedRunBadgePresentation
        {
            IsVisible = true,
            LatestRun = latestRun,
            State = state,
            Specification = specification,
            Label = text(specification.TextKey)
        };
    }

    public static RetainedRunBadgeState MapOutcome(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Extracted => RetainedRunBadgeState.Extracted,
        RunOutcome.Died => RetainedRunBadgeState.Died,
        RunOutcome.Interrupted => RetainedRunBadgeState.Unknown,
        _ => RetainedRunBadgeState.Unknown
    };
}

internal static class RetainedActiveMeasurementPolicy
{
    public static T Measure<T>(bool wasActive, Action<bool> setActive, Func<T> measure)
    {
        // TMP's Awake establishes screen-space font metrics; ignoreActiveState does not bypass Awake.
        if (!wasActive) setActive(true);
        try { return measure(); }
        finally { if (!wasActive) setActive(false); }
    }
}

internal static class RetainedRunBadgeMeasurementPolicy
{
    public const float TemporaryLabelWidthPixels = RetainedReferenceTransformPolicy.BaselineWidthPixels;

    public static float NormalizeOrFallback(
        float measuredCanvasWidth,
        float canvasScaleFactor,
        float referenceScale,
        float fallbackReferenceWidth)
    {
        if (!IsPositiveFinite(fallbackReferenceWidth))
            throw new ArgumentOutOfRangeException(nameof(fallbackReferenceWidth));
        if (!IsPositiveFinite(measuredCanvasWidth)
            || !IsPositiveFinite(canvasScaleFactor)
            || !IsPositiveFinite(referenceScale))
        {
            return fallbackReferenceWidth;
        }

        var normalizedWidth = measuredCanvasWidth * canvasScaleFactor / referenceScale;
        return IsPositiveFinite(normalizedWidth) ? normalizedWidth : fallbackReferenceWidth;
    }

    private static bool IsPositiveFinite(float value) =>
        value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
}

internal static class RetainedOverviewLatestRunBadgePolicy
{
    public const string Name = "OverviewLatestRunBadge";
    public const string ParentName = RetainedOverviewLatestRunCardPolicy.Name;
    public const string IconName = "OverviewLatestRunBadgeIcon";
    public const string LabelName = "OverviewLatestRunBadgeLabel";
    public const float LeftMarginPixels = 20f;
    public const float TopMarginPixels = 20f;
    public const float BorderWidth = 0f;
    public const bool HasSprite = false;
    public const bool UsesSimpleImageType = true;
    public const bool UsesSingleStateDrivenControl = true;
    public const bool HasButton = false;
    public const bool UsesButtonAnimation = false;
    public const bool HasLaterGateContent = false;

    public static RetainedRunBadgeCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewLatestRunCardCanvasLayout card,
        RetainedRunBadgeState state,
        float preferredLabelWidthPixels)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (card == null) throw new ArgumentNullException(nameof(card));
        var layout = RetainedRunBadgePolicy.CreateCanvasLayout(
            referenceTransform,
            state,
            preferredLabelWidthPixels);
        layout.Left = card.Left + referenceTransform.CanvasLength(LeftMarginPixels);
        layout.Top = card.Top + referenceTransform.CanvasLength(TopMarginPixels);
        return layout;
    }
}

internal sealed class RetainedLatestRunMapPresentation
{
    public bool IsVisible { get; set; }
    public RunSummary? LatestRun { get; set; }
    public string MapName { get; set; } = string.Empty;
}

internal static class RetainedLatestRunMapPresentationFactory
{
    public static RetainedLatestRunMapPresentation Create(
        RetainedRunBadgePresentation runBadgePresentation,
        Func<string, string> text)
    {
        if (runBadgePresentation == null) throw new ArgumentNullException(nameof(runBadgePresentation));
        if (text == null) throw new ArgumentNullException(nameof(text));
        if (!runBadgePresentation.IsVisible || runBadgePresentation.LatestRun == null)
            return new RetainedLatestRunMapPresentation { IsVisible = false };

        var latestRun = runBadgePresentation.LatestRun;
        return new RetainedLatestRunMapPresentation
        {
            IsVisible = true,
            LatestRun = latestRun,
            MapName = ResolveMapName(latestRun, text)
        };
    }

    private static string ResolveMapName(RunSummary run, Func<string, string> text)
    {
        if (HasKnownDisplayName(run.StartingMapKnown, run.StartingMapDisplayName))
            return run.StartingMapDisplayName;
        if (HasKnownDisplayName(run.MapKnown, run.MapDisplayName))
            return run.MapDisplayName;
        return text(RetainedOverviewLatestRunMapNamePolicy.UnknownMapTextKey);
    }

    private static bool HasKnownDisplayName(bool isKnown, string? displayName) =>
        isKnown
        && !string.IsNullOrWhiteSpace(displayName)
        && !string.Equals(
            displayName.Trim(),
            MapIdentity.UnknownDisplayName,
            StringComparison.OrdinalIgnoreCase);
}

internal sealed class RetainedLatestRunStatisticsPresentation
{
    public bool IsVisible { get; set; }
    public RunSummary? LatestRun { get; set; }
    public IReadOnlyList<string> DisplayLines { get; set; } = Array.Empty<string>();
    public string Text => string.Join("\n", DisplayLines);
}

internal static class RetainedLatestRunStatisticsPresentationFactory
{
    public static RetainedLatestRunStatisticsPresentation Create(
        RetainedRunBadgePresentation runBadgePresentation,
        Func<string, string> text,
        Func<DateTime, DateTime>? convertToLocalTime = null)
    {
        if (runBadgePresentation == null) throw new ArgumentNullException(nameof(runBadgePresentation));
        if (text == null) throw new ArgumentNullException(nameof(text));
        if (!runBadgePresentation.IsVisible || runBadgePresentation.LatestRun == null)
            return new RetainedLatestRunStatisticsPresentation { IsVisible = false };

        var latestRun = runBadgePresentation.LatestRun;
        var combat = latestRun.CombatStatistics;
        var combatTotals = combat?.Totals;
        var combatCapabilities = combat?.Capabilities;
        var containers = latestRun.ContainerStatistics;
        var containerCapability = containers?.Capabilities?.UniqueContainersLooted;
        var unavailable = text("ui.unavailable");
        var lines = new[]
        {
            FormatStartedLocal(latestRun.StartedUtc, convertToLocalTime, unavailable),
            $"{text(RetainedOverviewLatestRunStatisticsPolicy.ActiveTimeTextKey)}: "
                + FormatActiveDuration(latestRun.ActiveDurationSeconds, unavailable),
            $"{text(RetainedOverviewLatestRunStatisticsPolicy.DistanceTextKey)}: "
                + FormatDistance(latestRun.PhysicalDistance, latestRun.MovementCapability, unavailable),
            $"{text("ui.overview_kills_by_you")}: "
                + FormatCombatMetric(
                    combatTotals?.KillsByYou,
                    combatCapabilities?.KillsByYou,
                    combat?.WasRepairedFromInvalidState == true,
                    unavailable),
            $"{text("ui.overview_damage_dealt")}: "
                + FormatCombatMetric(
                    combatTotals?.DamageDealt,
                    combatCapabilities?.DamageDealt,
                    combat?.WasRepairedFromInvalidState == true,
                    unavailable),
            $"{text("ui.overview_damage_taken")}: "
                + FormatCombatMetric(
                    combatTotals?.DamageReceived,
                    combatCapabilities?.DamageReceived,
                    combat?.WasRepairedFromInvalidState == true,
                    unavailable),
            $"{text(RetainedOverviewLatestRunStatisticsPolicy.ContainersOpenedTextKey)}: "
                + FormatContainers(containers, containerCapability, unavailable, text)
        };

        return new RetainedLatestRunStatisticsPresentation
        {
            IsVisible = true,
            LatestRun = latestRun,
            DisplayLines = lines
        };
    }

    private static string FormatStartedLocal(
        DateTime startedUtc,
        Func<DateTime, DateTime>? convertToLocalTime,
        string unavailable)
    {
        if (startedUtc == default) return unavailable;
        try
        {
            var local = (convertToLocalTime ?? (value => value.ToLocalTime()))(startedUtc);
            return local == default
                ? unavailable
                : local.ToString("dd.MM.yyyy - HH:mm", CultureInfo.InvariantCulture);
        }
        catch
        {
            return unavailable;
        }
    }

    private static string FormatActiveDuration(double durationSeconds, string unavailable) =>
        RetainedRunDurationFormatter.TryFormat(durationSeconds, out var formatted)
            ? formatted
            : unavailable;

    private static string FormatDistance(
        double physicalDistance,
        AdapterCapabilityState movementCapability,
        string unavailable)
    {
        if (movementCapability != AdapterCapabilityState.Supported)
            return UiText.Get("ui.unsupported");
        return IsFiniteNonNegative(physicalDistance)
            ? $"{physicalDistance.ToString("#,0.00", CultureInfo.InvariantCulture)}m"
            : unavailable;
    }

    private static string FormatCombatMetric(
        long? value,
        MetricAvailability? availability,
        bool wasRepaired,
        string unavailable)
    {
        if (availability == null) return unavailable;
        if (availability.State == AdapterCapabilityState.DisabledIncompatible)
            return UiText.FormatMetric(value ?? 0L, availability.State);
        if (wasRepaired || !value.HasValue || value.Value < 0L) return unavailable;
        return UiText.FormatMetric(value.Value, availability.State);
    }

    private static string FormatCombatMetric(
        double? value,
        MetricAvailability? availability,
        bool wasRepaired,
        string unavailable)
    {
        if (availability == null) return unavailable;
        if (availability.State == AdapterCapabilityState.DisabledIncompatible)
            return UiText.FormatMetric(value ?? 0d, availability.State);
        if (wasRepaired || !value.HasValue || !IsFiniteNonNegative(value.Value)) return unavailable;
        return UiText.FormatMetric(value.Value, availability.State);
    }

    private static string FormatContainers(
        ContainerStatisticsAggregate? containers,
        MetricAvailability? availability,
        string unavailable,
        Func<string, string> text)
    {
        if (containers == null || availability == null) return unavailable;
        if (containers.UniqueContainersLooted < 0
            && !containers.WasRepairedFromInvalidState
            && !containers.HistoricalUnavailable
            && availability.State == AdapterCapabilityState.Supported)
        {
            return unavailable;
        }

        return UiText.FormatContainers(containers, availability.State, text);
    }

    private static bool IsFiniteNonNegative(double value) =>
        value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
}

internal sealed class RetainedLatestRunViewRunPresentation
{
    public bool IsVisible { get; set; }
    public RunSummary? LatestRun { get; set; }
    public string Label { get; set; } = string.Empty;
}

internal static class RetainedLatestRunViewRunPresentationFactory
{
    public static RetainedLatestRunViewRunPresentation Create(
        RetainedRunBadgePresentation runBadgePresentation,
        Func<string, string> text)
    {
        if (runBadgePresentation == null) throw new ArgumentNullException(nameof(runBadgePresentation));
        if (text == null) throw new ArgumentNullException(nameof(text));
        if (!runBadgePresentation.IsVisible || runBadgePresentation.LatestRun == null)
            return new RetainedLatestRunViewRunPresentation { IsVisible = false };

        return new RetainedLatestRunViewRunPresentation
        {
            IsVisible = true,
            LatestRun = runBadgePresentation.LatestRun,
            Label = text(RetainedOverviewLatestRunViewRunPolicy.TextKey)
        };
    }
}

internal sealed class RetainedOverviewLatestRunMapNameCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float FontSize { get; set; }
}

internal static class RetainedOverviewLatestRunMapNamePolicy
{
    public const string Name = "OverviewLatestRunMapName";
    public const string ParentName = RetainedOverviewLatestRunCardPolicy.Name;
    public const string UnknownMapTextKey = "ui.overview_latest_run_unknown_map";
    public const string UnknownMapEnglishFallback = MapIdentity.UnknownDisplayName;
    public const string FontAssetName = RetainedOverviewFirstStatisticsRowEntryPolicy.FontAssetName;
    public const string MaterialName = RetainedOverviewFirstStatisticsRowEntryPolicy.MaterialName;
    public const float GapAfterBadgePixels = 20f;
    public const float RightInsetPixels = 20f;
    public const float HeightPixels = 30f;
    public const float ReferenceFontSize = RetainedOverviewFirstStatisticsRowEntryPolicy.ReferenceFontSize;
    public const float Red = RetainedOverviewFirstStatisticsRowEntryPolicy.Red;
    public const float Green = RetainedOverviewFirstStatisticsRowEntryPolicy.Green;
    public const float Blue = RetainedOverviewFirstStatisticsRowEntryPolicy.Blue;
    public const float Alpha = RetainedOverviewFirstStatisticsRowEntryPolicy.Alpha;
    public const float CharacterSpacing = RetainedOverviewFirstStatisticsRowEntryPolicy.CharacterSpacing;
    public const float WordSpacing = RetainedOverviewFirstStatisticsRowEntryPolicy.WordSpacing;
    public const float LineSpacing = RetainedOverviewFirstStatisticsRowEntryPolicy.LineSpacing;
    public const float ParagraphSpacing = RetainedOverviewFirstStatisticsRowEntryPolicy.ParagraphSpacing;
    public const float HorizontalScale = 1f;
    public const bool BlocksRaycasts = false;
    public const bool WordWrapping = false;
    public const bool AutoSizing = false;
    public const bool UsesVisibleOverflow = true;
    public const bool UsesZeroTextMargins = true;
    public const bool UsesLeftAlignment = true;
    public const bool UsesVerticalCentering = true;
    public const bool UsesNormalStyle = true;
    public const bool UsesRegularWeight = true;
    public const bool UsesNativeHorizontalMetrics = true;
    public const bool UsesOwnedSubtleShadowMaterial = true;
    public const bool HasInteraction = false;
    public const bool HasButton = false;
    public const bool UsesButtonAnimation = false;
    public const bool HasLaterGateContent = false;

    public static RetainedOverviewLatestRunMapNameCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewLatestRunCardCanvasLayout card,
        RetainedRunBadgeCanvasLayout badge)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (badge == null) throw new ArgumentNullException(nameof(badge));

        var left = badge.Left + badge.Width + referenceTransform.CanvasLength(GapAfterBadgePixels);
        var right = card.Left + card.Width - referenceTransform.CanvasLength(RightInsetPixels);
        return new RetainedOverviewLatestRunMapNameCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Left = left,
            Top = badge.Top,
            Width = Math.Max(0f, right - left),
            Height = referenceTransform.CanvasLength(HeightPixels),
            FontSize = referenceTransform.CanvasLength(ReferenceFontSize)
        };
    }
}

internal sealed class RetainedOverviewLatestRunStatisticsCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float FontSize { get; set; }
    public float LineStep { get; set; }
}

internal static class RetainedOverviewLatestRunStatisticsPolicy
{
    public const string Name = "OverviewLatestRunStatistics";
    public const string ParentName = RetainedOverviewLatestRunCardPolicy.Name;
    public const string ActiveTimeTextKey = "ui.overview_latest_run_active_time";
    public const string DistanceTextKey = "ui.overview_latest_run_distance";
    public const string ContainersOpenedTextKey = "ui.overview_latest_run_containers_opened";
    public const string FontAssetName = RetainedOverviewFirstStatisticsRowEntryPolicy.FontAssetName;
    public const string MaterialName = RetainedOverviewFirstStatisticsRowEntryPolicy.MaterialName;
    public const float LeftInsetPixels = 20f;
    public const float RightInsetPixels = 20f;
    public const float GapBelowBadgeRowPixels = 20f;
    public const int LineCount = 7;
    public const float LineStepPixels = 29f;
    public const float HeightPixels = LineCount * LineStepPixels;
    public const float ReferenceFontSize = 19.8f;
    public const float Red = 1f;
    public const float Green = 1f;
    public const float Blue = 1f;
    public const float Alpha = 1f;
    public const float CharacterSpacing = 0f;
    public const float WordSpacing = 0f;
    public const float LineSpacing = 0f;
    public const float ParagraphSpacing = 0f;
    public const float HorizontalScale = 1f;
    public const bool BlocksRaycasts = false;
    public const bool WordWrapping = false;
    public const bool AutoSizing = false;
    public const bool UsesVisibleOverflow = true;
    public const bool UsesZeroTextMargins = true;
    public const bool UsesTopLeftAlignment = true;
    public const bool UsesNormalStyle = true;
    public const bool UsesRegularWeight = true;
    public const bool UsesNativeHorizontalMetrics = true;
    public const bool UsesOwnedSubtleShadowMaterial = true;
    public const bool HasInteraction = false;
    public const bool HasButton = false;
    public const bool UsesButtonAnimation = false;
    public const bool HasGateTwentyFourContent = false;

    public static RetainedOverviewLatestRunStatisticsCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewLatestRunCardCanvasLayout card,
        RetainedRunBadgeCanvasLayout badge,
        RetainedOverviewLatestRunMapNameCanvasLayout mapName)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (badge == null) throw new ArgumentNullException(nameof(badge));
        if (mapName == null) throw new ArgumentNullException(nameof(mapName));

        var left = card.Left + referenceTransform.CanvasLength(LeftInsetPixels);
        var right = card.Left + card.Width - referenceTransform.CanvasLength(RightInsetPixels);
        var badgeRowBottom = Math.Max(badge.Top + badge.Height, mapName.Top + mapName.Height);
        return new RetainedOverviewLatestRunStatisticsCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Left = left,
            Top = badgeRowBottom + referenceTransform.CanvasLength(GapBelowBadgeRowPixels),
            Width = Math.Max(0f, right - left),
            Height = referenceTransform.CanvasLength(HeightPixels),
            FontSize = referenceTransform.CanvasLength(ReferenceFontSize),
            LineStep = referenceTransform.CanvasLength(LineStepPixels)
        };
    }
}

internal sealed class RetainedOverviewLatestRunViewRunCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float ReferencePreferredLabelWidth { get; set; }
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float CornerRadius { get; set; }
    public float LabelLeft { get; set; }
    public float LabelTop { get; set; }
    public float LabelWidth { get; set; }
    public float LabelHeight { get; set; }
    public float FontSize { get; set; }
}

internal static class RetainedOverviewLatestRunViewRunPolicy
{
    public const string Name = "OverviewLatestRunViewRun";
    public const string ParentName = RetainedOverviewLatestRunCardPolicy.Name;
    public const string LabelName = "OverviewLatestRunViewRunLabel";
    public const string TextKey = "ui.overview_latest_run_view_run";
    public const string EnglishFallback = "View run";
    public const string FontAssetName = RetainedOverviewFirstStatisticsRowEntryPolicy.FontAssetName;
    public const string MaterialName = RetainedOverviewFirstStatisticsRowEntryPolicy.MaterialName;
    public const float LeftInsetPixels = 20f;
    public const float BottomInsetPixels = 20f;
    public const float HeightPixels = 50f;
    public const float CornerRadiusPixels = 25f;
    public const float HorizontalLabelPaddingPixels = 20f;
    public const float AuditedEnglishPreferredLabelWidthPixels = 95f;
    public const float ReferenceFontSize = 29.8f;
    public const float BackgroundRed = 96f / 255f;
    public const float BackgroundGreen = 203f / 255f;
    public const float BackgroundBlue = 249f / 255f;
    public const float BackgroundAlpha = 1f;
    public const float BorderWidth = 0f;
    public const float TextRed = 1f;
    public const float TextGreen = 1f;
    public const float TextBlue = 1f;
    public const float TextAlpha = 1f;
    public const float CharacterSpacing = 0f;
    public const float WordSpacing = 0f;
    public const float LineSpacing = 0f;
    public const float ParagraphSpacing = 0f;
    public const float HorizontalScale = 1f;
    public const bool HasSprite = false;
    public const bool UsesSimpleImageType = true;
    public const bool HasBackgroundShadow = false;
    public const bool BackgroundBlocksRaycasts = true;
    public const bool LabelBlocksRaycasts = false;
    public const bool WordWrapping = false;
    public const bool AutoSizing = false;
    public const bool UsesVisibleOverflow = true;
    public const bool UsesZeroTextMargins = true;
    public const bool UsesCenteredAlignment = true;
    public const bool UsesNormalStyle = true;
    public const bool UsesRegularWeight = true;
    public const bool UsesNativeHorizontalMetrics = true;
    public const bool UsesOwnedSubtleShadowMaterial = true;
    public const bool HasInteraction = true;
    public const bool HasButton = true;
    public const bool UsesButtonAnimation = true;
    public const bool IsInteractable = true;
    public const bool TargetsExistingProceduralImage = true;
    public const bool UsesTransitionNone = true;
    public const bool NavigationIsDisabled = true;
    public const bool UsesFreshButtonClickedEvent = true;
    public const bool UsesSharedSafeNativeFeedbackPolicy = true;
    public const bool RegistersListener = false;
    public const int RegisteredUdsCallbackCount = 0;
    public const bool HasSelectableTransition = false;
    public const bool HasFunctionalActivation = false;
    public const bool PreservesExactLatestRunReference = true;
    public const bool IntendedForLaterActivation = true;
    public const bool HasGateTwentyFiveContent = false;

    public static RetainedOverviewLatestRunViewRunCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewLatestRunCardCanvasLayout card,
        float preferredLabelWidthPixels)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (preferredLabelWidthPixels <= 0f
            || float.IsNaN(preferredLabelWidthPixels)
            || float.IsInfinity(preferredLabelWidthPixels))
        {
            throw new ArgumentOutOfRangeException(nameof(preferredLabelWidthPixels));
        }

        var leftInset = referenceTransform.CanvasLength(LeftInsetPixels);
        var bottomInset = referenceTransform.CanvasLength(BottomInsetPixels);
        var height = referenceTransform.CanvasLength(HeightPixels);
        var labelPadding = referenceTransform.CanvasLength(HorizontalLabelPaddingPixels);
        var cardInnerWidth = Math.Max(0f, card.Width - leftInset * 2f);
        var preferredWidth = referenceTransform.CanvasLength(
            preferredLabelWidthPixels + HorizontalLabelPaddingPixels * 2f);
        var width = Math.Min(cardInnerWidth, preferredWidth);
        return new RetainedOverviewLatestRunViewRunCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            ReferencePreferredLabelWidth = preferredLabelWidthPixels,
            Left = card.Left + leftInset,
            Top = card.Top + card.Height - bottomInset - height,
            Width = width,
            Height = height,
            CornerRadius = referenceTransform.CanvasLength(CornerRadiusPixels),
            LabelLeft = labelPadding,
            LabelTop = 0f,
            LabelWidth = Math.Max(0f, width - labelPadding * 2f),
            LabelHeight = height,
            FontSize = referenceTransform.CanvasLength(ReferenceFontSize)
        };
    }
}

internal static class RetainedLatestRunViewRunMeasurementPolicy
{
    public const float TemporaryLabelWidthPixels = RetainedReferenceTransformPolicy.BaselineWidthPixels;

    public static float NormalizeOrFallback(
        float measuredCanvasWidth,
        float canvasScaleFactor,
        float referenceScale,
        float fallbackReferenceWidth =
            RetainedOverviewLatestRunViewRunPolicy.AuditedEnglishPreferredLabelWidthPixels) =>
        RetainedRunBadgeMeasurementPolicy.NormalizeOrFallback(
            measuredCanvasWidth,
            canvasScaleFactor,
            referenceScale,
            fallbackReferenceWidth);
}

internal sealed class RetainedOverviewWorldTimeCardCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float CornerRadius { get; set; }
    public float ContentLeft { get; set; }
    public float ContentTop { get; set; }
    public float ContentWidth { get; set; }
    public float ContentHeight { get; set; }
}

internal static class RetainedOverviewWorldTimeCardPolicy
{
    public const string Name = "OverviewWorldTimeCard";
    public const string ParentName = RetainedOverviewRightPanelPolicy.ContentName;
    public const StatisticsPanelTab OwnerTab = RetainedOverviewRightPanelPolicy.OwnerTab;
    public const float HorizontalGapPixels = 30f;
    public const float HeightPixels = 121f;
    public const float ContentInsetPixels = 20f;
    public const float Red = RetainedOverviewPanelStylePolicy.Red;
    public const float Green = RetainedOverviewPanelStylePolicy.Green;
    public const float Blue = RetainedOverviewPanelStylePolicy.Blue;
    public const float LayerAlpha = RetainedOverviewPanelStylePolicy.LayerAlpha;
    public const float CornerRadiusPixels = 10f;
    public const float BorderWidth = 0f;
    public const bool HasSprite = false;
    public const bool UsesSimpleImageType = true;
    public const bool BlocksRaycasts = false;
    public const bool HasInteraction = false;
    public const bool HasShadow = false;

    public static RetainedOverviewWorldTimeCardCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewPanelCanvasLayout rightPanel,
        RetainedOverviewLatestRunCardCanvasLayout latestRunCard)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (rightPanel == null) throw new ArgumentNullException(nameof(rightPanel));
        if (latestRunCard == null) throw new ArgumentNullException(nameof(latestRunCard));

        var gap = referenceTransform.CanvasLength(HorizontalGapPixels);
        var inset = referenceTransform.CanvasLength(ContentInsetPixels);
        var left = latestRunCard.Left + latestRunCard.Width + gap;
        var right = rightPanel.ContentLeft + rightPanel.ContentWidth;
        var width = Math.Max(0f, right - left);
        var height = referenceTransform.CanvasLength(HeightPixels);
        return new RetainedOverviewWorldTimeCardCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Left = left,
            Top = latestRunCard.Top,
            Width = width,
            Height = height,
            CornerRadius = referenceTransform.CanvasLength(CornerRadiusPixels),
            ContentLeft = left + inset,
            ContentTop = latestRunCard.Top + inset,
            ContentWidth = Math.Max(0f, width - inset * 2f),
            ContentHeight = Math.Max(0f, height - inset * 2f)
        };
    }
}

internal sealed class RetainedOverviewWorldTimeHeadingCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float FontSize { get; set; }
    public float OpticalOffsetX { get; set; }
    public float OpticalOffsetY { get; set; }
}

internal static class RetainedOverviewWorldTimeHeadingPolicy
{
    public const string Name = "OverviewWorldTimeHeading";
    public const string ParentName = RetainedOverviewRightPanelPolicy.ContentName;
    public const string TextKey = "ui.overview_world_time";
    public const string EnglishFallback = "World time";
    public const StatisticsPanelTab OwnerTab = RetainedOverviewRightPanelPolicy.OwnerTab;
    public const string FontAssetName = RetainedOverviewLatestRunHeadingPolicy.FontAssetName;
    public const string SourceMaterialName = RetainedOverviewLatestRunHeadingPolicy.SourceMaterialName;
    public const string MaterialName = RetainedOverviewLatestRunHeadingPolicy.MaterialName;
    public const float ReferenceFontSize = RetainedOverviewLatestRunHeadingPolicy.ReferenceFontSize;
    public const float HeightPixels = RetainedOverviewLatestRunHeadingPolicy.HeightPixels;
    public const float ReferenceOpticalOffsetX = RetainedOverviewLatestRunHeadingPolicy.ReferenceOpticalOffsetX;
    public const float ReferenceOpticalOffsetY = RetainedOverviewLatestRunHeadingPolicy.ReferenceOpticalOffsetY;
    public const float Red = RetainedOverviewLatestRunHeadingPolicy.Red;
    public const float Green = RetainedOverviewLatestRunHeadingPolicy.Green;
    public const float Blue = RetainedOverviewLatestRunHeadingPolicy.Blue;
    public const float Alpha = RetainedOverviewLatestRunHeadingPolicy.Alpha;
    public const float CharacterSpacing = RetainedOverviewLatestRunHeadingPolicy.CharacterSpacing;
    public const float WordSpacing = RetainedOverviewLatestRunHeadingPolicy.WordSpacing;
    public const float LineSpacing = RetainedOverviewLatestRunHeadingPolicy.LineSpacing;
    public const float ParagraphSpacing = RetainedOverviewLatestRunHeadingPolicy.ParagraphSpacing;
    public const bool BlocksRaycasts = false;
    public const bool WordWrapping = RetainedOverviewLatestRunHeadingPolicy.WordWrapping;
    public const bool AutoSizing = RetainedOverviewLatestRunHeadingPolicy.AutoSizing;
    public const bool UsesTopLeftAlignment = RetainedOverviewLatestRunHeadingPolicy.UsesTopLeftAlignment;
    public const bool UsesOwnedTabLabelMaterial = RetainedOverviewLatestRunHeadingPolicy.UsesOwnedTabLabelMaterial;
    public const bool UsesZeroTextMargin = RetainedOverviewLatestRunHeadingPolicy.UsesZeroTextMargin;
    public const bool UsesFixedOpticalOffset = RetainedOverviewLatestRunHeadingPolicy.UsesFixedOpticalOffset;
    public const bool UsesHorizontalScaleCompensation = false;
    public const bool UsesNormalStyle = true;
    public const bool UsesRegularWeight = true;
    public const bool UsesVisibleOverflow = true;
    public const bool HasInteraction = false;

    public static RetainedOverviewWorldTimeHeadingCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewWorldTimeCardCanvasLayout card,
        RetainedOverviewLatestRunHeadingCanvasLayout latestRunHeading)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (latestRunHeading == null) throw new ArgumentNullException(nameof(latestRunHeading));
        return new RetainedOverviewWorldTimeHeadingCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Left = card.ContentLeft,
            Top = latestRunHeading.Top,
            Width = card.ContentWidth,
            Height = referenceTransform.CanvasLength(HeightPixels),
            FontSize = referenceTransform.CanvasLength(ReferenceFontSize),
            OpticalOffsetX = referenceTransform.CanvasLength(ReferenceOpticalOffsetX),
            OpticalOffsetY = referenceTransform.CanvasLength(ReferenceOpticalOffsetY)
        };
    }
}

internal sealed class RetainedWorldTimeStatisticsPresentation
{
    public bool IsVisible { get; set; } = true;
    public WorldTimeStatisticsAggregate Statistics { get; set; } = new();
    public WorldTimeMetricCapabilities Capabilities { get; set; } = new();
    public IReadOnlyList<string> DisplayLines { get; set; } = Array.Empty<string>();
    public string Text => string.Join("\n", DisplayLines);
}

internal static class RetainedWorldTimeStatisticsPresentationFactory
{
    public static RetainedWorldTimeStatisticsPresentation Create(
        StatisticsPanelProjection projection,
        Func<string, string> text)
    {
        if (projection == null) throw new ArgumentNullException(nameof(projection));
        if (text == null) throw new ArgumentNullException(nameof(text));

        var statistics = projection.WorldTime;
        var capabilities = projection.WorldTimeCapabilities;
        return new RetainedWorldTimeStatisticsPresentation
        {
            IsVisible = true,
            Statistics = statistics,
            Capabilities = capabilities,
            DisplayLines = new[]
            {
                $"{text(RetainedOverviewWorldTimeStatisticsPolicy.CalendarDaysTextKey)}: "
                    + UiText.FormatWorldTimeCount(
                        statistics.CalendarDaysAdvanced,
                        capabilities.CalendarDays),
                $"{text(RetainedOverviewWorldTimeStatisticsPolicy.SleepSessionsTextKey)}: "
                    + UiText.FormatWorldTimeCount(
                        statistics.CompletedSleepSessions,
                        capabilities.CompletedSleepSessions),
                $"{text(RetainedOverviewWorldTimeStatisticsPolicy.SleepAdvancedTimeTextKey)}: "
                    + UiText.FormatWorldTimeDuration(
                        statistics.SleepAdvancedTimeTicks,
                        capabilities.SleepAdvancedTime)
            }
        };
    }
}

internal sealed class RetainedOverviewWorldTimeStatisticsCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float FontSize { get; set; }
    public float LineStep { get; set; }
}

internal static class RetainedOverviewWorldTimeStatisticsPolicy
{
    public const string Name = "OverviewWorldTimeStatistics";
    public const string ParentName = RetainedOverviewWorldTimeCardPolicy.Name;
    public const string CalendarDaysTextKey = "ui.calendar_days_advanced";
    public const string SleepSessionsTextKey = "ui.overview_sleep_sessions";
    public const string SleepAdvancedTimeTextKey = "ui.overview_sleep_advanced_time";
    public const string FontAssetName = RetainedOverviewLatestRunStatisticsPolicy.FontAssetName;
    public const string MaterialName = RetainedOverviewLatestRunStatisticsPolicy.MaterialName;
    public const int LineCount = 3;
    public const float LineStepPixels = 29f;
    public const float HeightPixels = LineCount * LineStepPixels;
    public const float ReferenceFontSize = 19.8f;
    public const float Red = 1f;
    public const float Green = 1f;
    public const float Blue = 1f;
    public const float Alpha = 1f;
    public const float CharacterSpacing = 0f;
    public const float WordSpacing = 0f;
    public const float LineSpacing = 0f;
    public const float ParagraphSpacing = 0f;
    public const float HorizontalScale = 1f;
    public const bool BlocksRaycasts = false;
    public const bool WordWrapping = false;
    public const bool AutoSizing = false;
    public const bool UsesVisibleOverflow = true;
    public const bool UsesZeroTextMargins = true;
    public const bool UsesTopLeftAlignment = true;
    public const bool UsesNormalStyle = true;
    public const bool UsesRegularWeight = true;
    public const bool UsesNativeHorizontalMetrics = true;
    public const bool UsesOwnedSubtleShadowMaterial = true;
    public const bool HasInteraction = false;
    public const bool HasButton = false;
    public const bool UsesButtonAnimation = false;
    public const bool RegistersListener = false;
    public const bool HasActivation = false;
    public const bool IncludesObservedWorldTime = false;
    public const bool HasGateTwentyEightContent = false;
    public const bool HasProductionVisualRejection = false;

    public static RetainedOverviewWorldTimeStatisticsCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform,
        RetainedOverviewWorldTimeCardCanvasLayout card)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        if (card == null) throw new ArgumentNullException(nameof(card));
        return new RetainedOverviewWorldTimeStatisticsCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Left = card.ContentLeft,
            Top = card.ContentTop,
            Width = card.ContentWidth,
            Height = referenceTransform.CanvasLength(HeightPixels),
            FontSize = referenceTransform.CanvasLength(ReferenceFontSize),
            LineStep = referenceTransform.CanvasLength(LineStepPixels)
        };
    }
}

internal static class RetainedRunBadgeProceduralIconPolicy
{
    public static int GetTextureWidth(RetainedRunBadgeIconKind iconKind) => iconKind switch
    {
        RetainedRunBadgeIconKind.Check => 38,
        RetainedRunBadgeIconKind.Skull => 36,
        RetainedRunBadgeIconKind.QuestionMark => 20,
        _ => throw new ArgumentOutOfRangeException(nameof(iconKind))
    };

    public static int GetTextureHeight(RetainedRunBadgeIconKind iconKind) => iconKind switch
    {
        RetainedRunBadgeIconKind.Check => 28,
        RetainedRunBadgeIconKind.Skull => 42,
        RetainedRunBadgeIconKind.QuestionMark => 32,
        _ => throw new ArgumentOutOfRangeException(nameof(iconKind))
    };

    public static byte[] CreateTopDownAlpha(RetainedRunBadgeIconKind iconKind)
    {
        var width = GetTextureWidth(iconKind);
        var height = GetTextureHeight(iconKind);
        var alpha = new byte[width * height];
        switch (iconKind)
        {
            case RetainedRunBadgeIconKind.Check:
                PaintLine(alpha, width, height, 3f, 15f, 13f, 25f, 2.6f);
                PaintLine(alpha, width, height, 13f, 25f, 35f, 3f, 2.6f);
                break;
            case RetainedRunBadgeIconKind.Skull:
                PaintEllipse(alpha, width, height, 17.5f, 15.5f, 14.5f, 14.5f, 255);
                PaintRectangle(alpha, width, height, 8, 17, 27, 34, 255);
                PaintEllipse(alpha, width, height, 11.5f, 16f, 4f, 5f, 0);
                PaintEllipse(alpha, width, height, 23.5f, 16f, 4f, 5f, 0);
                PaintEllipse(alpha, width, height, 17.5f, 24f, 2.5f, 3.5f, 0);
                PaintRectangle(alpha, width, height, 11, 31, 13, 40, 0);
                PaintRectangle(alpha, width, height, 17, 31, 19, 40, 0);
                PaintRectangle(alpha, width, height, 23, 31, 25, 40, 0);
                break;
            case RetainedRunBadgeIconKind.QuestionMark:
                PaintLine(alpha, width, height, 3f, 8f, 7f, 3f, 2.1f);
                PaintLine(alpha, width, height, 7f, 3f, 14f, 3f, 2.1f);
                PaintLine(alpha, width, height, 14f, 3f, 18f, 7f, 2.1f);
                PaintLine(alpha, width, height, 18f, 7f, 18f, 12f, 2.1f);
                PaintLine(alpha, width, height, 18f, 12f, 10f, 19f, 2.1f);
                PaintLine(alpha, width, height, 10f, 19f, 10f, 22f, 2.1f);
                PaintEllipse(alpha, width, height, 10f, 28f, 2.2f, 2.2f, 255);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(iconKind));
        }

        return alpha;
    }

    private static void PaintLine(
        byte[] alpha,
        int width,
        int height,
        float startX,
        float startY,
        float endX,
        float endY,
        float radius)
    {
        var deltaX = endX - startX;
        var deltaY = endY - startY;
        var squaredLength = deltaX * deltaX + deltaY * deltaY;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var projection = squaredLength == 0f
                ? 0f
                : Math.Max(0f, Math.Min(1f,
                    ((x - startX) * deltaX + (y - startY) * deltaY) / squaredLength));
            var distanceX = x - (startX + projection * deltaX);
            var distanceY = y - (startY + projection * deltaY);
            if (distanceX * distanceX + distanceY * distanceY <= radius * radius)
                alpha[y * width + x] = 255;
        }
    }

    private static void PaintEllipse(
        byte[] alpha,
        int width,
        int height,
        float centerX,
        float centerY,
        float radiusX,
        float radiusY,
        byte value)
    {
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var normalizedX = (x - centerX) / radiusX;
            var normalizedY = (y - centerY) / radiusY;
            if (normalizedX * normalizedX + normalizedY * normalizedY <= 1f)
                alpha[y * width + x] = value;
        }
    }

    private static void PaintRectangle(
        byte[] alpha,
        int width,
        int height,
        int left,
        int top,
        int right,
        int bottom,
        byte value)
    {
        for (var y = Math.Max(0, top); y <= Math.Min(height - 1, bottom); y++)
        for (var x = Math.Max(0, left); x <= Math.Min(width - 1, right); x++)
            alpha[y * width + x] = value;
    }
}

internal sealed class RetainedHeaderTitleCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float FontSize { get; set; }
    public float PrincipalLeft { get; set; }
    public float PrincipalTop { get; set; }
    public float PrincipalWidth { get; set; }
    public float PrincipalHeight { get; set; }
}

internal static class RetainedHeaderTitlePolicy
{
    public const string Name = "HeaderTitle";
    public const string Text = "Ultimate Duckov Statistics";
    public const string NativeSourcePath = "Canvas/MainMenuContainer/Menu/OptionsPanel/Text (TMP)";
    public const string NativePauseSourcePath = "OptionsPanel/Text (TMP)";
    public const string FontAssetName = "ResourceHanRoundedCN-Medium SDF";
    public const string MaterialName = "ResourceHanRoundedCN-Medium Atlas Material Shadow";
    public const float ReferenceFontSize = 85f;
    public const float LeftPixels = 113f;
    public const float TopPixels = 141f;
    public const float WidthPixels = 921f;
    public const float HeightPixels = 83f;
    public const float RightExclusivePixels = 1034f;
    public const float BottomExclusivePixels = 224f;
    public const float PrincipalLeftPixels = 114f;
    public const float PrincipalTopPixels = 144f;
    public const float PrincipalWidthPixels = 905f;
    public const float PrincipalHeightPixels = 68f;
    public const float PrincipalRightExclusivePixels = 1019f;
    public const float PrincipalBottomExclusivePixels = 212f;
    public const float Red = 1f;
    public const float Green = 1f;
    public const float Blue = 1f;
    public const float Alpha = 1f;
    public const bool BlocksRaycasts = false;
    public const bool WordWrapping = false;
    public const bool AutoSizing = false;

    public static RetainedHeaderTitleCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        return new RetainedHeaderTitleCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Left = referenceTransform.CanvasX(LeftPixels),
            Top = referenceTransform.CanvasY(TopPixels),
            Width = referenceTransform.CanvasLength(WidthPixels),
            Height = referenceTransform.CanvasLength(HeightPixels),
            FontSize = referenceTransform.CanvasLength(ReferenceFontSize),
            PrincipalLeft = referenceTransform.CanvasX(PrincipalLeftPixels),
            PrincipalTop = referenceTransform.CanvasY(PrincipalTopPixels),
            PrincipalWidth = referenceTransform.CanvasLength(PrincipalWidthPixels),
            PrincipalHeight = referenceTransform.CanvasLength(PrincipalHeightPixels)
        };
    }
}

internal sealed class RetainedBackControlCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public float Left { get; set; }
    public float Top { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float CornerRadius { get; set; }
    public float ArrowLeft { get; set; }
    public float ArrowTop { get; set; }
    public float ArrowWidth { get; set; }
    public float ArrowHeight { get; set; }
}

internal static class RetainedBackControlPolicy
{
    public const string ButtonName = "BackButton";
    public const string ArrowName = "BackArrow";
    public const float LeftPixels = 80f;
    public const float TopPixels = 41f;
    public const float WidthPixels = 68f;
    public const float HeightPixels = 68f;
    public const float RightExclusivePixels = 148f;
    public const float BottomExclusivePixels = 109f;
    public const float CenterXPixels = 114f;
    public const float CenterYPixels = 75f;
    public const float CornerRadiusPixels = 34f;
    public const float ArrowLeftPixels = 97f;
    public const float ArrowTopPixels = 58f;
    public const float ArrowWidthPixels = 34f;
    public const float ArrowHeightPixels = 34f;
    public const float ArrowRightExclusivePixels = 131f;
    public const float ArrowBottomExclusivePixels = 92f;
    public const float BackgroundRed = 0f;
    public const float BackgroundGreen = 0f;
    public const float BackgroundBlue = 0f;
    public const float BackgroundAlpha = 0.50f;
    public const bool BackgroundBlocksRaycasts = true;
    public const float ArrowRed = 1f;
    public const float ArrowGreen = 1f;
    public const float ArrowBlue = 1f;
    public const float ArrowAlpha = 1f;
    public const bool ArrowBlocksRaycasts = false;
    public const bool PreserveArrowAspect = true;
    public const float EffectiveBackgroundOpacity = 0.75f;

    public static RetainedBackControlCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        return new RetainedBackControlCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Left = referenceTransform.CanvasX(LeftPixels),
            Top = referenceTransform.CanvasY(TopPixels),
            Width = referenceTransform.CanvasLength(WidthPixels),
            Height = referenceTransform.CanvasLength(HeightPixels),
            CornerRadius = referenceTransform.CanvasLength(CornerRadiusPixels),
            ArrowLeft = referenceTransform.CanvasX(ArrowLeftPixels),
            ArrowTop = referenceTransform.CanvasY(ArrowTopPixels),
            ArrowWidth = referenceTransform.CanvasLength(ArrowWidthPixels),
            ArrowHeight = referenceTransform.CanvasLength(ArrowHeightPixels)
        };
    }

    public static bool IsValidBackgroundGraphic(
        float red,
        float green,
        float blue,
        float alpha,
        bool raycastTarget) =>
        red == BackgroundRed
        && green == BackgroundGreen
        && blue == BackgroundBlue
        && alpha == BackgroundAlpha
        && raycastTarget == BackgroundBlocksRaycasts;

    public static bool IsValidArrowGraphic(
        float red,
        float green,
        float blue,
        float alpha,
        bool raycastTarget,
        bool preserveAspect) =>
        red == ArrowRed
        && green == ArrowGreen
        && blue == ArrowBlue
        && alpha == ArrowAlpha
        && raycastTarget == ArrowBlocksRaycasts
        && preserveAspect == PreserveArrowAspect;
}

internal sealed class RetainedVisualCanvasLayout
{
    public RetainedReferenceTransform ReferenceTransform { get; set; } = null!;
    public RetainedHeaderCanvasLayout Header { get; set; } = null!;
    public RetainedTabStripCanvasLayout TabStrip { get; set; } = null!;
    public RetainedTabCanvasLayout OverviewTab => TabStrip.Tabs[0];
    public RetainedHeaderBottomBarCanvasLayout HeaderBottomBar { get; set; } = null!;
    public RetainedHeaderTitleCanvasLayout HeaderTitle { get; set; } = null!;
    public RetainedBackControlCanvasLayout BackControl { get; set; } = null!;
    public RetainedOverviewPanelCanvasLayout OverviewLeftPanel { get; set; } = null!;
    public RetainedOverviewPanelCanvasLayout OverviewRightPanel { get; set; } = null!;
    public RetainedOverviewProfileSummaryHeadingCanvasLayout OverviewProfileSummaryHeading { get; set; } = null!;
    public RetainedOverviewHighlightsHeadingCanvasLayout OverviewHighlightsHeading { get; set; } = null!;
    public RetainedOverviewLatestRunHeadingCanvasLayout OverviewLatestRunHeading { get; set; } = null!;
    public RetainedOverviewLatestRunCardCanvasLayout OverviewLatestRunCard { get; set; } = null!;
    public RetainedRunBadgeCanvasLayout? OverviewLatestRunBadge { get; set; }
    public RetainedOverviewLatestRunMapNameCanvasLayout? OverviewLatestRunMapName { get; set; }
    public RetainedOverviewLatestRunStatisticsCanvasLayout? OverviewLatestRunStatistics { get; set; }
    public RetainedOverviewLatestRunViewRunCanvasLayout? OverviewLatestRunViewRun { get; set; }
    public RetainedOverviewWorldTimeHeadingCanvasLayout OverviewWorldTimeHeading { get; set; } = null!;
    public RetainedOverviewWorldTimeCardCanvasLayout OverviewWorldTimeCard { get; set; } = null!;
    public RetainedOverviewWorldTimeStatisticsCanvasLayout OverviewWorldTimeStatistics { get; set; } = null!;
    public IReadOnlyList<RetainedOverviewFastestExtractionRowCanvasLayout> OverviewHighlightRows { get; set; } =
        Array.Empty<RetainedOverviewFastestExtractionRowCanvasLayout>();
    public IReadOnlyList<RetainedTwoColumnStatisticsRowCanvasLayout> OverviewHighlightEntries { get; set; } =
        Array.Empty<RetainedTwoColumnStatisticsRowCanvasLayout>();
    public RetainedOverviewFastestExtractionRowCanvasLayout OverviewFastestExtractionRow { get; set; } = null!;
    public RetainedTwoColumnStatisticsRowCanvasLayout OverviewFastestExtractionEntry { get; set; } = null!;
    public RetainedOverviewFirstStatisticsRowCanvasLayout OverviewFirstStatisticsRow { get; set; } = null!;
    public RetainedTwoColumnStatisticsRowCanvasLayout OverviewFirstStatisticsRowEntry { get; set; } = null!;
    public IReadOnlyList<RetainedProfileSummaryRowCanvasLayout> OverviewProfileSummaryRows { get; set; } =
        Array.Empty<RetainedProfileSummaryRowCanvasLayout>();
}

internal static class RetainedVisualLayoutPolicy
{
    public static RetainedVisualCanvasLayout Create(
        float viewportPixelWidth,
        float viewportPixelHeight,
        float canvasScaleFactor)
    {
        var referenceTransform = RetainedReferenceTransformPolicy.Create(
            viewportPixelWidth,
            viewportPixelHeight,
            canvasScaleFactor);
        return Create(referenceTransform);
    }

    public static RetainedVisualCanvasLayout Create(
        RetainedReferenceTransform referenceTransform)
    {
        return Create(referenceTransform, RetainedTabStripPolicy.AuditedEnglishPreferredWidths);
    }

    public static RetainedVisualCanvasLayout Create(
        RetainedReferenceTransform referenceTransform,
        IReadOnlyList<float> preferredTabWidths)
    {
        return Create(
            referenceTransform,
            preferredTabWidths,
            badgeState: null,
            preferredBadgeLabelWidth: 0f,
            preferredViewRunLabelWidth:
                RetainedOverviewLatestRunViewRunPolicy.AuditedEnglishPreferredLabelWidthPixels);
    }

    public static RetainedVisualCanvasLayout Create(
        RetainedReferenceTransform referenceTransform,
        IReadOnlyList<float> preferredTabWidths,
        RetainedRunBadgeState badgeState,
        float preferredBadgeLabelWidth)
    {
        return Create(
            referenceTransform,
            preferredTabWidths,
            (RetainedRunBadgeState?)badgeState,
            preferredBadgeLabelWidth,
            RetainedOverviewLatestRunViewRunPolicy.AuditedEnglishPreferredLabelWidthPixels);
    }

    internal static RetainedVisualCanvasLayout Create(
        RetainedReferenceTransform referenceTransform,
        IReadOnlyList<float> preferredTabWidths,
        RetainedRunBadgeState badgeState,
        float preferredBadgeLabelWidth,
        float preferredViewRunLabelWidth)
    {
        return Create(
            referenceTransform,
            preferredTabWidths,
            (RetainedRunBadgeState?)badgeState,
            preferredBadgeLabelWidth,
            preferredViewRunLabelWidth);
    }

    private static RetainedVisualCanvasLayout Create(
        RetainedReferenceTransform referenceTransform,
        IReadOnlyList<float> preferredTabWidths,
        RetainedRunBadgeState? badgeState,
        float preferredBadgeLabelWidth,
        float preferredViewRunLabelWidth)
    {
        if (referenceTransform == null) throw new ArgumentNullException(nameof(referenceTransform));
        var header = RetainedHeaderPolicy.CreateCanvasLayout(referenceTransform);
        var tabStrip = RetainedTabStripPolicy.CreateCanvasLayout(referenceTransform, preferredTabWidths);
        var headerBottomBar = RetainedHeaderBottomBarPolicy.CreateCanvasLayout(referenceTransform);
        var headerTitle = RetainedHeaderTitlePolicy.CreateCanvasLayout(referenceTransform);
        var backControl = RetainedBackControlPolicy.CreateCanvasLayout(referenceTransform);
        var overviewLeftPanel = RetainedOverviewLeftPanelPolicy.CreateCanvasLayout(referenceTransform);
        var overviewRightPanel = RetainedOverviewRightPanelPolicy.CreateCanvasLayout(referenceTransform);
        var overviewProfileSummaryHeading = RetainedOverviewProfileSummaryHeadingPolicy.CreateCanvasLayout(
            referenceTransform,
            overviewLeftPanel);
        var overviewHighlightsHeading = RetainedOverviewHighlightsHeadingPolicy.CreateCanvasLayout(
            referenceTransform,
            overviewRightPanel);
        var overviewHighlightRows = RetainedOverviewHighlightsRowsPolicy.CreateRowCanvasLayouts(
            referenceTransform,
            overviewRightPanel);
        var overviewHighlightEntries = RetainedOverviewHighlightsRowsPolicy.CreateEntryCanvasLayouts(
            referenceTransform,
            overviewHighlightRows);
        var overviewLatestRunHeading = RetainedOverviewLatestRunHeadingPolicy.CreateCanvasLayout(
            referenceTransform,
            overviewRightPanel,
            overviewHighlightRows[^1]);
        var overviewLatestRunCard = RetainedOverviewLatestRunCardPolicy.CreateCanvasLayout(
            referenceTransform,
            overviewRightPanel,
            overviewLatestRunHeading);
        var overviewLatestRunBadge = badgeState.HasValue
            ? RetainedOverviewLatestRunBadgePolicy.CreateCanvasLayout(
                referenceTransform,
                overviewLatestRunCard,
                badgeState.Value,
                preferredBadgeLabelWidth)
            : null;
        var overviewLatestRunMapName = overviewLatestRunBadge == null
            ? null
            : RetainedOverviewLatestRunMapNamePolicy.CreateCanvasLayout(
                referenceTransform,
                overviewLatestRunCard,
                overviewLatestRunBadge);
        var overviewLatestRunStatistics = overviewLatestRunBadge == null || overviewLatestRunMapName == null
            ? null
            : RetainedOverviewLatestRunStatisticsPolicy.CreateCanvasLayout(
                referenceTransform,
                overviewLatestRunCard,
                overviewLatestRunBadge,
                overviewLatestRunMapName);
        var overviewLatestRunViewRun = overviewLatestRunBadge == null
            ? null
            : RetainedOverviewLatestRunViewRunPolicy.CreateCanvasLayout(
                referenceTransform,
                overviewLatestRunCard,
                preferredViewRunLabelWidth);
        var overviewWorldTimeCard = RetainedOverviewWorldTimeCardPolicy.CreateCanvasLayout(
            referenceTransform,
            overviewRightPanel,
            overviewLatestRunCard);
        var overviewWorldTimeHeading = RetainedOverviewWorldTimeHeadingPolicy.CreateCanvasLayout(
            referenceTransform,
            overviewWorldTimeCard,
            overviewLatestRunHeading);
        var overviewWorldTimeStatistics = RetainedOverviewWorldTimeStatisticsPolicy.CreateCanvasLayout(
            referenceTransform,
            overviewWorldTimeCard);
        var overviewFastestExtractionRow = overviewHighlightRows[0];
        var overviewFastestExtractionEntry = overviewHighlightEntries[0];
        var overviewProfileSummaryRows = RetainedProfileSummaryRowsPolicy.CreateCanvasLayouts(
            referenceTransform,
            overviewLeftPanel);
        var overviewFirstStatisticsRow = overviewProfileSummaryRows[0].Surface;
        var overviewFirstStatisticsRowEntry = overviewProfileSummaryRows[0].Entry;
        if (!IsFinite(header.Left)
            || !IsFinite(header.Top)
            || !IsPositiveFinite(header.Width)
            || !IsPositiveFinite(header.Height)
            || !IsPositiveFinite(header.CornerRadius)
            || tabStrip.Tabs.Count != RetainedTabStripPolicy.Specifications.Count
            || tabStrip.Tabs.Any(tab =>
                !IsFinite(tab.Left)
                || !IsFinite(tab.Top)
                || !IsPositiveFinite(tab.Width)
                || !IsPositiveFinite(tab.Height)
                || !IsPositiveFinite(tab.ExposedHeight)
                || !IsPositiveFinite(tab.CornerRadius)
                || !IsPositiveFinite(tab.LeftPadding)
                || !IsPositiveFinite(tab.RightPadding)
                || !IsPositiveFinite(tab.TopPadding)
                || !IsPositiveFinite(tab.BottomPadding)
                || !IsPositiveFinite(tab.FontSize)
                || !IsPositiveFinite(tab.ReferencePreferredLabelWidth)
                || !IsPositiveFinite(tab.PreferredLabelWidth)
                || !IsFinite(tab.LabelLeft)
                || !IsFinite(tab.LabelTop)
                || !IsPositiveFinite(tab.LabelWidth)
                || !IsPositiveFinite(tab.LabelHeight))
            || !IsFinite(headerBottomBar.Left)
            || !IsFinite(headerBottomBar.Top)
            || !IsPositiveFinite(headerBottomBar.Width)
            || !IsPositiveFinite(headerBottomBar.Height)
            || !IsFinite(headerBottomBar.SurfaceLeft)
            || !IsFinite(headerBottomBar.SurfaceTop)
            || !IsPositiveFinite(headerBottomBar.SurfaceWidth)
            || !IsPositiveFinite(headerBottomBar.SurfaceHeight)
            || !IsPositiveFinite(headerBottomBar.SurfaceCornerRadius)
            || !IsFinite(headerTitle.Left)
            || !IsFinite(headerTitle.Top)
            || !IsPositiveFinite(headerTitle.Width)
            || !IsPositiveFinite(headerTitle.Height)
            || !IsPositiveFinite(headerTitle.FontSize)
            || !IsFinite(headerTitle.PrincipalLeft)
            || !IsFinite(headerTitle.PrincipalTop)
            || !IsPositiveFinite(headerTitle.PrincipalWidth)
            || !IsPositiveFinite(headerTitle.PrincipalHeight)
            || !IsFinite(backControl.Left)
            || !IsFinite(backControl.Top)
            || !IsPositiveFinite(backControl.Width)
            || !IsPositiveFinite(backControl.Height)
            || !IsPositiveFinite(backControl.CornerRadius)
            || !IsFinite(backControl.ArrowLeft)
            || !IsFinite(backControl.ArrowTop)
            || !IsPositiveFinite(backControl.ArrowWidth)
            || !IsPositiveFinite(backControl.ArrowHeight))
        {
            throw new InvalidOperationException("The retained visual transform produced invalid Canvas geometry.");
        }

        return new RetainedVisualCanvasLayout
        {
            ReferenceTransform = referenceTransform,
            Header = header,
            TabStrip = tabStrip,
            HeaderBottomBar = headerBottomBar,
            HeaderTitle = headerTitle,
            BackControl = backControl,
            OverviewLeftPanel = overviewLeftPanel,
            OverviewRightPanel = overviewRightPanel,
            OverviewProfileSummaryHeading = overviewProfileSummaryHeading,
            OverviewHighlightsHeading = overviewHighlightsHeading,
            OverviewLatestRunHeading = overviewLatestRunHeading,
            OverviewLatestRunCard = overviewLatestRunCard,
            OverviewLatestRunBadge = overviewLatestRunBadge,
            OverviewLatestRunMapName = overviewLatestRunMapName,
            OverviewLatestRunStatistics = overviewLatestRunStatistics,
            OverviewLatestRunViewRun = overviewLatestRunViewRun,
            OverviewWorldTimeHeading = overviewWorldTimeHeading,
            OverviewWorldTimeCard = overviewWorldTimeCard,
            OverviewWorldTimeStatistics = overviewWorldTimeStatistics,
            OverviewHighlightRows = overviewHighlightRows,
            OverviewHighlightEntries = overviewHighlightEntries,
            OverviewFastestExtractionRow = overviewFastestExtractionRow,
            OverviewFastestExtractionEntry = overviewFastestExtractionEntry,
            OverviewFirstStatisticsRow = overviewFirstStatisticsRow,
            OverviewFirstStatisticsRowEntry = overviewFirstStatisticsRowEntry,
            OverviewProfileSummaryRows = overviewProfileSummaryRows
        };
    }

    private static bool IsPositiveFinite(float value) => value > 0f && IsFinite(value);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

internal static class RetainedShellCompositionPolicy
{
    public const int TabCount = 9;
    public const int RootChildCount = 14;
    public const int HeaderChildCount = 0;
    public const int TabChildCount = 1;
    public const int TabLabelChildCount = 0;
    public const int OverviewTabChildCount = TabChildCount;
    public const int OverviewTabLabelChildCount = TabLabelChildCount;
    public const int HeaderBottomBarChildCount = 1;
    public const int HeaderBottomBarGraphicChildCount = 0;
    public const int HeaderTitleChildCount = 0;
    public const int BackButtonChildCount = 1;
    public const int BackArrowChildCount = 0;
    public const int OverviewContentViewChildCount = 2;
    public const int OverviewLeftPanelChildCount = 1;
    public const int OverviewLeftPanelContentChildCount = 12;
    public const int OverviewProfileSummaryHeadingChildCount = 0;
    public const int OverviewFirstStatisticsRowChildCount = 1;
    public const int OverviewFirstStatisticsRowContentChildCount = 2;
    public const int OverviewFirstStatisticsRowLabelChildCount = 0;
    public const int OverviewFirstStatisticsRowValueChildCount = 0;
    public const int ProfileSummaryRowCount = 11;
    public const int ProfileSummaryStandardRowContentChildCount = 2;
    public const int ProfileSummaryEconomyRowContentChildCount = 3;
    public const int OverviewRightPanelChildCount = 1;
    public const int OverviewRightPanelContentChildCount = 9;
    public const int OverviewHighlightsHeadingChildCount = 0;
    public const int OverviewLatestRunHeadingChildCount = 0;
    public const int OverviewLatestRunCardChildCount = 4;
    public const int OverviewLatestRunBadgeChildCount = 2;
    public const int OverviewLatestRunBadgeIconChildCount = 0;
    public const int OverviewLatestRunBadgeLabelChildCount = 0;
    public const int OverviewLatestRunBadgeGraphicCount = 3;
    public const int OverviewLatestRunMapNameChildCount = 0;
    public const int OverviewLatestRunMapNameGraphicCount = 1;
    public const int OverviewLatestRunStatisticsChildCount = 0;
    public const int OverviewLatestRunStatisticsGraphicCount = 1;
    public const int OverviewLatestRunViewRunChildCount = 1;
    public const int OverviewLatestRunViewRunLabelChildCount = 0;
    public const int OverviewLatestRunViewRunGraphicCount = 2;
    public const int OverviewWorldTimeHeadingChildCount = 0;
    public const int OverviewWorldTimeCardChildCount = 1;
    public const int OverviewWorldTimeStatisticsChildCount = 0;
    public const int OverviewWorldTimeGraphicCount = 3;
    public const int OverviewHighlightRowCount = 4;
    public const int OverviewHighlightRowChildCount = 2;
    public const int OverviewHighlightRowGraphicCount = 1;
    public const int OverviewFastestExtractionRowChildCount = 2;
    public const int OverviewFastestExtractionRowGraphicCount = OverviewHighlightRowGraphicCount;
    public const int OverviewFastestExtractionLabelChildCount = 0;
    public const int OverviewFastestExtractionValueChildCount = 0;
    public const int GraphicCount = 86;
    public const int ButtonCount = 11;
    public const int RectMaskCount = 1;
    public const int OnlyOneEdgeModifierCount = 9;
}

internal sealed class RetainedBackControlActivation
{
    private readonly Action close;

    public RetainedBackControlActivation(Action close)
    {
        this.close = close ?? throw new ArgumentNullException(nameof(close));
    }

    public void Invoke() => close();
}

internal sealed class RetainedTabActivation
{
    private readonly Action<StatisticsPanelTab> selectTab;
    private readonly StatisticsPanelTab tab;

    public RetainedTabActivation(
        Action<StatisticsPanelTab> selectTab,
        StatisticsPanelTab tab)
    {
        this.selectTab = selectTab ?? throw new ArgumentNullException(nameof(selectTab));
        if (!PanelInteractionState.NavigationOrder.Contains(tab))
            throw new ArgumentOutOfRangeException(nameof(tab));
        this.tab = tab;
    }

    public void Invoke() => selectTab(tab);
}

internal static class RetainedBackArrowAssetPolicy
{
    public const string TextureName = "UltimateDuckovStatisticsBackArrowTexture";
    public const string SpriteName = "UltimateDuckovStatisticsBackArrow";
    public const int WidthPixels = 34;
    public const int HeightPixels = 34;
    public const int VisibleLeftPixels = 0;
    public const int VisibleTopPixels = 0;
    public const int VisibleWidthPixels = 34;
    public const int VisibleHeightPixels = 34;
    public const float PixelsPerUnit = 34f;
    public const string TopDownAlphaSha256 = "37cc15ff97b8dadbcd65b0a5e0e39e1db91e97d8d5d492a1cd6e1b8c73181943";

    // This is only the mock-matched 34x34 white-arrow alpha plane, stored top row first.
    // It is not a crop containing the circle, header, dimmer, or any other mockup pixels.
    private const string TopDownAlphaBase64 =
        "AAAAAAAAAAAAAAAAAAAAE3ZtEgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEtz//88AAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAE83////+BwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAHc7/////xQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAHN3/////yxYAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFuD/////yw4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFtX/" +
        "////yw4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIdX/////2w4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAI+X/////3BYA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAGOX/////zRgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAGNf/////zRAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAGdv/////3BAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAN+b/////zRAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAJ+b/////zRgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAG+j/////2ykaGhoaGhoaGhoaGhoaGhoaGhoa" +
        "GhoaGAIAH+v////////////////////////////////////////LE47/////////////////////////////////////////" +
        "/3eG//////////////////////////////////////////9zKvP////////////////////////////////////////cGwAn" +
        "7//////LMSYmJiYmJiYmJiYmJiYmJiYmJiYmJiYkAgAAADjv/////7cQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJdv/" +
        "////3BgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAl5v/////NCwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACPl////" +
        "/7cLAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAI+7/////tw4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAj5f/////b" +
        "FgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADLi/////8sKAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIeL/////tAoA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAh6/////+0CgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB7d/////8kUAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAALN//////vgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAc3f////8FAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAABzm///bAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAHaGbHQAAAAAAAAAAAAAA" +
        "AAAAAA==";

    public static byte[] DecodeTopDownAlpha()
    {
        var alpha = Convert.FromBase64String(TopDownAlphaBase64);
        if (alpha.Length != WidthPixels * HeightPixels || !HasExactVisibleBounds(alpha))
            throw new InvalidOperationException("The retained back-arrow alpha asset is invalid.");
        return alpha;
    }

    public static bool HasExactVisibleBounds(IReadOnlyList<byte>? alpha)
    {
        if (alpha == null || alpha.Count != WidthPixels * HeightPixels) return false;

        var left = WidthPixels;
        var top = HeightPixels;
        var rightExclusive = 0;
        var bottomExclusive = 0;
        for (var y = 0; y < HeightPixels; y++)
        for (var x = 0; x < WidthPixels; x++)
        {
            if (alpha[y * WidthPixels + x] == 0) continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            rightExclusive = Math.Max(rightExclusive, x + 1);
            bottomExclusive = Math.Max(bottomExclusive, y + 1);
        }

        return left == VisibleLeftPixels
               && top == VisibleTopPixels
               && rightExclusive - left == VisibleWidthPixels
               && bottomExclusive - top == VisibleHeightPixels;
    }
}

internal static class RetainedTabWidthPolicy
{
    public static float Resolve(float minimumWidthPixels, float preferredTextWidthPixels, float horizontalPaddingPixels)
    {
        if (minimumWidthPixels <= 0f) throw new ArgumentOutOfRangeException(nameof(minimumWidthPixels));
        if (preferredTextWidthPixels < 0f) throw new ArgumentOutOfRangeException(nameof(preferredTextWidthPixels));
        if (horizontalPaddingPixels < 0f) throw new ArgumentOutOfRangeException(nameof(horizontalPaddingPixels));
        return Math.Max(minimumWidthPixels, preferredTextWidthPixels + horizontalPaddingPixels);
    }
}

internal sealed class RetainedTabGeometry
{
    public IReadOnlyList<float> Widths { get; set; } = Array.Empty<float>();
    public float ContentWidth { get; set; }
    public bool RequiresScrolling { get; set; }
}

internal static class RetainedTabGeometryPolicy
{
    public static RetainedTabGeometry Create(
        float viewportWidthPixels,
        float minimumWidthPixels,
        float spacingPixels,
        float outerPaddingPixels,
        float labelPaddingPixels,
        IReadOnlyList<float> preferredTextWidthsPixels)
    {
        if (viewportWidthPixels <= 0f) throw new ArgumentOutOfRangeException(nameof(viewportWidthPixels));
        if (preferredTextWidthsPixels == null) throw new ArgumentNullException(nameof(preferredTextWidthsPixels));
        var widths = preferredTextWidthsPixels
            .Select(width => RetainedTabWidthPolicy.Resolve(minimumWidthPixels, width, labelPaddingPixels))
            .ToArray();
        var contentWidth = outerPaddingPixels * 2f
                           + widths.Sum()
                           + Math.Max(0, widths.Length - 1) * spacingPixels;
        return new RetainedTabGeometry
        {
            Widths = widths,
            ContentWidth = contentWidth,
            RequiresScrolling = contentWidth > viewportWidthPixels + 0.5f
        };
    }
}

internal static class RetainedTabSelectionPolicy
{
    public static bool IsSelected(StatisticsPanelTab candidate, StatisticsPanelTab selected)
    {
        if (!PanelInteractionState.NavigationOrder.Contains(candidate))
            throw new ArgumentOutOfRangeException(nameof(candidate));
        if (!PanelInteractionState.NavigationOrder.Contains(selected))
            throw new ArgumentOutOfRangeException(nameof(selected));
        return candidate == selected;
    }

    public static void SelectAndSynchronize(
        PanelInteractionState interaction,
        Action<StatisticsPanelTab> synchronize,
        StatisticsPanelTab selected)
    {
        if (interaction == null) throw new ArgumentNullException(nameof(interaction));
        if (synchronize == null) throw new ArgumentNullException(nameof(synchronize));
        interaction.SelectTab(selected);
        synchronize(interaction.SelectedTab);
    }
}

internal static class PanelFocusRestorePolicy
{
    public static bool ShouldRestore(bool snapshotCaptured, bool priorObjectExists, bool priorObjectActive) =>
        snapshotCaptured && priorObjectExists && priorObjectActive;
}

internal sealed class OverflowCueState
{
    public bool ShowLeading { get; set; }
    public bool ShowTrailing { get; set; }
}

internal static class OverflowCuePolicy
{
    public static OverflowCueState Resolve(float viewportExtent, float contentExtent, float offsetFromStart)
    {
        if (viewportExtent <= 0f) throw new ArgumentOutOfRangeException(nameof(viewportExtent));
        if (contentExtent < 0f) throw new ArgumentOutOfRangeException(nameof(contentExtent));
        var maximumOffset = Math.Max(0f, contentExtent - viewportExtent);
        if (maximumOffset <= 0.5f) return new OverflowCueState();
        var offset = Math.Clamp(offsetFromStart, 0f, maximumOffset);
        return new OverflowCueState
        {
            ShowLeading = offset > 0.5f,
            ShowTrailing = offset < maximumOffset - 0.5f
        };
    }
}

internal sealed class RetainedShellLifecycleState
{
    public bool IsOpen { get; private set; }
    public bool IsDisposed { get; private set; }

    public bool TryOpen()
    {
        if (IsDisposed || IsOpen) return false;
        IsOpen = true;
        return true;
    }

    public bool Close()
    {
        if (!IsOpen) return false;
        IsOpen = false;
        return true;
    }

    public void Dispose()
    {
        IsOpen = false;
        IsDisposed = true;
    }
}

internal sealed class BoundedPage<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int PageIndex { get; set; }
    public int PageCount { get; set; }
    public int TotalCount { get; set; }
}

internal static class BoundedPageFactory
{
    public static BoundedPage<T> Create<T>(IReadOnlyList<T> source, int requestedPage, int pageSize)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (pageSize < 1 || pageSize > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var pageCount = Math.Max(1, (source.Count + pageSize - 1) / pageSize);
        var pageIndex = Math.Clamp(requestedPage, 0, pageCount - 1);
        return new BoundedPage<T>
        {
            Items = source.Skip(pageIndex * pageSize).Take(pageSize).ToArray(),
            PageIndex = pageIndex,
            PageCount = pageCount,
            TotalCount = source.Count
        };
    }
}

internal sealed class PanelOperationGate
{
    public PanelOperation Current { get; private set; }

    public bool TryBegin(PanelOperation operation)
    {
        if (operation == PanelOperation.None) throw new ArgumentOutOfRangeException(nameof(operation));
        if (Current != PanelOperation.None) return false;
        Current = operation;
        return true;
    }

    public void Complete(PanelOperation operation)
    {
        if (operation == PanelOperation.None || Current != operation)
            throw new InvalidOperationException("The completed panel operation is not active.");
        Current = PanelOperation.None;
    }
}

internal sealed class PanelInteractionState
{
    private static readonly StatisticsPanelTab[] Tabs =
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
    };

    public StatisticsPanelTab SelectedTab { get; private set; }
    public CombatPanelSection CombatSection { get; set; }
    public EquipmentPanelSection EquipmentSection { get; set; }
    public bool ResetConfirmationVisible { get; private set; }
    public bool ResetCancelHasInitialFocus { get; private set; }

    public static IReadOnlyList<StatisticsPanelTab> NavigationOrder => Tabs;

    public void SelectTab(StatisticsPanelTab tab)
    {
        if (!Tabs.Contains(tab)) throw new ArgumentOutOfRangeException(nameof(tab));
        SelectedTab = tab;
        ResetConfirmationVisible = false;
        ResetCancelHasInitialFocus = false;
    }

    public void MoveTab(int delta)
    {
        if (delta == 0) return;
        var index = Array.IndexOf(Tabs, SelectedTab);
        SelectTab(Tabs[(index + delta % Tabs.Length + Tabs.Length) % Tabs.Length]);
    }

    public void ShowResetConfirmation()
    {
        ResetConfirmationVisible = true;
        ResetCancelHasInitialFocus = true;
    }

    public void ConsumeInitialResetFocus() => ResetCancelHasInitialFocus = false;

    public bool CancelModal()
    {
        if (!ResetConfirmationVisible) return false;
        ResetConfirmationVisible = false;
        ResetCancelHasInitialFocus = false;
        return true;
    }
}

internal sealed class StatisticsPanelProjection
{
    internal CombatProjectionBinding? CombatBinding { get; set; }
    internal EquipmentProjectionBinding? EquipmentBinding { get; set; }
    internal EconomyProjectionBinding? EconomyBinding { get; set; }
    internal CraftingProjectionBinding? CraftingBinding { get; set; }
    internal ItemUseProjectionBinding? ItemUseBinding { get; set; }
    public ProfileDocument Profile { get; set; } = new();
    public RunStatisticsViewModel Runs { get; set; } = new();
    public CombatStatisticsViewModel Combat { get; set; } = new();
    public WeaponStatisticsViewModel Weapons { get; set; } = new();
    public EquipmentStatisticsViewModel Equipment { get; set; } = new();
    public IReadOnlyList<EquipmentDurationAggregate> RecurringLoadouts { get; set; } = Array.Empty<EquipmentDurationAggregate>();
    public IReadOnlyList<EquipmentDurationAggregate> TotemStates { get; set; } = Array.Empty<EquipmentDurationAggregate>();
    public IReadOnlyList<EquipmentDurationAggregate> TotemSets { get; set; } = Array.Empty<EquipmentDurationAggregate>();
    public IReadOnlyList<RunSummary> RecentEquipmentRuns { get; set; } = Array.Empty<RunSummary>();
    public ContainerStatisticsViewModel Containers { get; set; } = new();
    public EconomyHoldingsProjection Holdings { get; set; } = new();
    public EconomyStatisticsAggregate Economy { get; set; } = new();
    public EconomyMetricCapabilities CurrentEconomyCapabilities { get; set; } = new();
    public IReadOnlyList<RunSummary> RecentEconomyRuns { get; set; } = Array.Empty<RunSummary>();
    public WorldTimeStatisticsAggregate WorldTime { get; set; } = new();
    public WorldTimeMetricCapabilities WorldTimeCapabilities { get; set; } = new();
    public CraftingStatisticsAggregate Crafting { get; set; } = new();
    public CraftingMetricCapabilities CraftingCapabilities { get; set; } = new();
    public ItemUsePanelProjection ItemUse { get; set; } = new();
    public IReadOnlyList<WeaponAmmunitionGroupProjection> WeaponAmmunitionGroups { get; set; } =
        Array.Empty<WeaponAmmunitionGroupProjection>();
    public IReadOnlyList<CraftingOutputProjection> CraftingOutputs { get; set; } =
        Array.Empty<CraftingOutputProjection>();
    public IReadOnlyList<CraftingResourceProjection> CraftingResources { get; set; } =
        Array.Empty<CraftingResourceProjection>();
}

internal sealed class WeaponAmmunitionGroupProjection
{
    public string WeaponId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long TotalFiringActions { get; set; }
    public long CorrelatedFiringActions { get; set; }
    public long UncorrelatedFiringActions { get; set; }
    public bool HistoricalPairingUnavailable { get; set; }
    public IReadOnlyList<WeaponAmmunitionPairView> Ammunition { get; set; } =
        Array.Empty<WeaponAmmunitionPairView>();
}

internal sealed class ItemUsePanelProjection
{
    public AggregateTotals Overall { get; set; } = new();
    public IReadOnlyList<ItemUseRowProjection> Items { get; set; } = Array.Empty<ItemUseRowProjection>();
    public IReadOnlyList<ItemUseGroupProjection> Groups { get; set; } = Array.Empty<ItemUseGroupProjection>();
    public IReadOnlyList<RunSummary> RecentRuns { get; set; } = Array.Empty<RunSummary>();
    public bool HistoricalUnavailable { get; set; }
    public bool WasRepairedFromInvalidState { get; set; }
}

internal sealed class ItemUseRowProjection
{
    public string ItemId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public CanonicalItemGroup Group { get; set; }
    public IReadOnlyList<ItemEffectTag> EffectTags { get; set; } = Array.Empty<ItemEffectTag>();
    public AggregateTotals Totals { get; set; } = new();
}

internal sealed class ItemUseGroupProjection
{
    public CanonicalItemGroup Group { get; set; }
    public long Uses { get; set; }
}

internal sealed class CraftingOutputProjection
{
    public CraftedOutputAggregate Output { get; set; } = new();
    public IReadOnlyList<CraftingOutputResourceProjection> Resources { get; set; } =
        Array.Empty<CraftingOutputResourceProjection>();
}

internal sealed class CraftingOutputResourceProjection
{
    public string ResourceItemId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long ConsumedQuantity { get; set; }
}

internal sealed class CraftingResourceProjection
{
    public CraftingResourceAggregate Resource { get; set; } = new();
    public IReadOnlyList<CraftingResourceOutputProjection> Outputs { get; set; } =
        Array.Empty<CraftingResourceOutputProjection>();
}

internal sealed class CraftingResourceOutputProjection
{
    public string OutputItemId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long ProducedQuantity { get; set; }
    public long ConsumedQuantity { get; set; }
}

internal static class StatisticsPanelProjectionFactory
{
    public static bool HasProvableGeneration(ProfileDocument? profile, string currentGenerationId) =>
        profile != null
        && !string.IsNullOrWhiteSpace(currentGenerationId)
        && string.Equals(profile.GenerationId, currentGenerationId, StringComparison.Ordinal)
        && string.Equals(profile.Statistics.SaveGenerationId, currentGenerationId, StringComparison.Ordinal);

    public static StatisticsPanelProjection Create(
        ProfileDocument profile,
        EconomyMetricCapabilities currentEconomyCapabilities,
        CraftingMetricCapabilities currentCraftingCapabilities,
        WorldTimeMetricCapabilities currentWorldTimeCapabilities)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (currentEconomyCapabilities == null)
            throw new ArgumentNullException(nameof(currentEconomyCapabilities));
        if (currentCraftingCapabilities == null)
            throw new ArgumentNullException(nameof(currentCraftingCapabilities));
        if (currentWorldTimeCapabilities == null)
            throw new ArgumentNullException(nameof(currentWorldTimeCapabilities));
        if (!HasProvableGeneration(profile, profile.GenerationId))
            throw new InvalidOperationException("The active UDS generation cannot be proven for UI projection.");

        var weapons = WeaponStatisticsViewModelFactory.Create(profile);
        var equipment = EquipmentStatisticsViewModelFactory.Create(profile);
        var projection = new StatisticsPanelProjection
        {
            Profile = profile,
            Runs = RunStatisticsViewModelFactory.Create(profile),
            Combat = CombatStatisticsViewModelFactory.Create(profile),
            Weapons = weapons,
            Equipment = equipment,
            RecurringLoadouts = equipment.Lifetime.Loadouts.Values
                .Where(value => value.RunOccurrences >= 2)
                .OrderByDescending(value => value.ActiveDurationSeconds)
                .ThenBy(value => value.Id, StringComparer.Ordinal)
                .ToArray(),
            TotemStates = equipment.Lifetime.TotemStates.Values
                .OrderByDescending(value => value.ActiveDurationSeconds)
                .ThenBy(value => value.Id, StringComparer.Ordinal)
                .ToArray(),
            TotemSets = equipment.Lifetime.TotemSets.Values
                .OrderByDescending(value => value.ActiveDurationSeconds)
                .ThenBy(value => value.Id, StringComparer.Ordinal)
                .ToArray(),
            RecentEquipmentRuns = profile.Statistics.Runs
                .OrderByDescending(value => value.EndedUtc)
                .ThenBy(value => value.RunId, StringComparer.Ordinal)
                .ToArray(),
            Containers = ContainerStatisticsViewModelFactory.Create(profile),
            Holdings = EconomyHoldingsReducer.Project(profile.Statistics.Holdings),
            Economy = profile.Statistics.Economy,
            CurrentEconomyCapabilities = EconomyStatisticsReducer.CloneCapabilities(currentEconomyCapabilities),
            RecentEconomyRuns = profile.Statistics.Runs
                .OrderByDescending(value => value.EndedUtc)
                .ThenBy(value => value.RunId, StringComparer.Ordinal)
                .Take(12)
                .ToArray(),
            WorldTime = profile.Statistics.WorldTime,
            WorldTimeCapabilities = WorldTimeStatisticsReducer.RestrictWithCurrent(
                profile.Statistics.WorldTime.Capabilities,
                currentWorldTimeCapabilities),
            Crafting = profile.Statistics.Crafting,
            CraftingCapabilities = CraftingStatisticsReducer.RestrictWithCurrent(
                profile.Statistics.Crafting.Capabilities,
                currentCraftingCapabilities),
            ItemUse = CreateItemUse(profile),
            WeaponAmmunitionGroups = CreateWeaponAmmunitionGroups(weapons),
            CraftingOutputs = CreateCraftingOutputs(profile.Statistics.Crafting),
            CraftingResources = CreateCraftingResources(profile.Statistics.Crafting)
        };
        projection.CombatBinding = new CombatProjectionBinding(projection);
        projection.EquipmentBinding = new EquipmentProjectionBinding(projection);
        projection.EconomyBinding = new EconomyProjectionBinding(projection);
        projection.CraftingBinding = new CraftingProjectionBinding(projection);
        projection.ItemUseBinding = new ItemUseProjectionBinding(projection);
        return projection;
    }

    private static ItemUsePanelProjection CreateItemUse(ProfileDocument profile)
    {
        var groups = Enum.GetValues(typeof(CanonicalItemGroup)).Cast<CanonicalItemGroup>()
            .Select(group => new ItemUseGroupProjection
            {
                Group = group,
                Uses = profile.Statistics.Groups.TryGetValue(group.ToString(), out var totals)
                    ? totals.ActivationCount
                    : 0
            })
            .ToArray();
        return new ItemUsePanelProjection
        {
            Overall = profile.Statistics.Overall,
            HistoricalUnavailable = profile.Statistics.RunTotals.ItemStatistics.HistoricalUnavailable,
            WasRepairedFromInvalidState =
                profile.Statistics.RunTotals.ItemStatistics.WasRepairedFromInvalidState,
            Items = profile.Statistics.Items.Values
                .OrderByDescending(value => value.Totals.ActivationCount)
                .ThenBy(value => StableDisplayName(value.DisplayName, value.ItemId), StringComparer.Ordinal)
                .ThenBy(value => value.ItemId, StringComparer.Ordinal)
                .Select(value => new ItemUseRowProjection
                {
                    ItemId = value.ItemId,
                    DisplayName = StableDisplayName(value.DisplayName, value.ItemId),
                    Group = value.Group,
                    EffectTags = value.EffectTags.OrderBy(tag => tag).ToArray(),
                    Totals = value.Totals
                })
                .ToArray(),
            Groups = groups,
            RecentRuns = profile.Statistics.Runs
                .OrderByDescending(value => value.EndedUtc)
                .ThenBy(value => value.RunId, StringComparer.Ordinal)
                .Take(12)
                .ToArray()
        };
    }

    private static WeaponAmmunitionGroupProjection[] CreateWeaponAmmunitionGroups(
        WeaponStatisticsViewModel weapons)
    {
        var pairs = weapons.WeaponAmmunitionPairs
            .GroupBy(value => value.Pair.WeaponId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        return weapons.Weapons.Select(weapon =>
        {
            pairs.TryGetValue(weapon.WeaponId, out var ammunition);
            ammunition ??= Array.Empty<WeaponAmmunitionPairView>();
            weapons.Lifetime.UncorrelatedWeaponFiringActions.TryGetValue(
                weapon.WeaponId,
                out var uncorrelated);
            return new WeaponAmmunitionGroupProjection
            {
                WeaponId = weapon.WeaponId,
                DisplayName = StableDisplayName(weapon.DisplayName, weapon.WeaponId),
                TotalFiringActions = weapon.Totals.FiringActions,
                CorrelatedFiringActions = ammunition.Sum(value => value.Pair.FiringActions),
                UncorrelatedFiringActions = uncorrelated,
                HistoricalPairingUnavailable = weapons.Lifetime.HistoricalPairingUnavailable,
                Ammunition = ammunition
            };
        }).ToArray();
    }

    private static CraftingOutputProjection[] CreateCraftingOutputs(
        CraftingStatisticsAggregate crafting) => crafting.Outputs.Values
        .OrderByDescending(value => value.CompletionActions)
        .ThenBy(value => StableDisplayName(value.DisplayName, value.OutputItemId), StringComparer.Ordinal)
        .ThenBy(value => value.OutputItemId, StringComparer.Ordinal)
        .Select(output => new CraftingOutputProjection
        {
            Output = output,
            Resources = output.Recipes.Values
                .SelectMany(recipe => recipe.Resources.Values)
                .GroupBy(value => value.ResourceItemId, StringComparer.Ordinal)
                .Select(group => new CraftingOutputResourceProjection
                {
                    ResourceItemId = group.Key,
                    DisplayName = StableDisplayName(
                        group.Select(value => value.DisplayName).FirstOrDefault(value =>
                            !string.IsNullOrWhiteSpace(value)),
                        group.Key),
                    ConsumedQuantity = SaturatingSum(group.Select(value => value.ConsumedQuantity))
                })
                .OrderByDescending(value => value.ConsumedQuantity)
                .ThenBy(value => value.DisplayName, StringComparer.Ordinal)
                .ThenBy(value => value.ResourceItemId, StringComparer.Ordinal)
                .ToArray()
        }).ToArray();

    private static CraftingResourceProjection[] CreateCraftingResources(
        CraftingStatisticsAggregate crafting) => crafting.Resources.Values
        .OrderByDescending(value => value.ConsumedQuantity)
        .ThenBy(value => StableDisplayName(value.DisplayName, value.ResourceItemId), StringComparer.Ordinal)
        .ThenBy(value => value.ResourceItemId, StringComparer.Ordinal)
        .Select(resource => new CraftingResourceProjection
        {
            Resource = resource,
            Outputs = crafting.Outputs.Values.Select(output =>
                {
                    var recipes = output.Recipes.Values
                        .Where(recipe => recipe.Resources.ContainsKey(resource.ResourceItemId))
                        .ToArray();
                    return new CraftingResourceOutputProjection
                    {
                        OutputItemId = output.OutputItemId,
                        DisplayName = StableDisplayName(output.DisplayName, output.OutputItemId),
                        ProducedQuantity = SaturatingSum(recipes.Select(value => value.ProducedQuantity)),
                        ConsumedQuantity = SaturatingSum(recipes.Select(value =>
                            value.Resources[resource.ResourceItemId].ConsumedQuantity))
                    };
                })
                .Where(value => value.ConsumedQuantity > 0)
                .OrderByDescending(value => value.ConsumedQuantity)
                .ThenBy(value => value.DisplayName, StringComparer.Ordinal)
                .ThenBy(value => value.OutputItemId, StringComparer.Ordinal)
                .ToArray()
        }).ToArray();

    internal static string StableDisplayName(string? displayName, string stableId) =>
        !string.IsNullOrWhiteSpace(displayName)
            ? displayName.Trim()
            : string.IsNullOrWhiteSpace(stableId)
                ? "Unknown / modded item"
                : $"Unknown / modded item [{stableId}]";

    private static long SaturatingSum(IEnumerable<long> values)
    {
        var result = 0L;
        foreach (var value in values)
        {
            if (value <= 0) continue;
            result = result > long.MaxValue - value ? long.MaxValue : result + value;
        }
        return result;
    }
}
