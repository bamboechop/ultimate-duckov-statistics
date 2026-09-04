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
        if (surface != PanelAccessSurface.MainMenu) return false;

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
        return surface == PanelAccessSurface.MainMenu
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
    public const float VisualAlpha = 0.50f;
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
    public const bool AppliesToBasePauseMenuButton = false;
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
    public const bool ContainsVisibleContent = false;

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

internal static class RetainedOverviewRightPanelPolicy
{
    public const string BackgroundName = "OverviewRightPanelBackground";
    public const float LeftPixels = 1300f;
    public const float RightExclusivePixels = 2475f;
    public const float PanelGapPixels = 40f;
    public const float ScreenRightMarginPixels = 85f;

    public static RetainedOverviewPanelCanvasLayout CreateCanvasLayout(
        RetainedReferenceTransform referenceTransform) =>
        RetainedOverviewPanelStylePolicy.CreateCanvasLayout(referenceTransform, LeftPixels);
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
    public RetainedOverviewFirstStatisticsRowCanvasLayout OverviewFirstStatisticsRow { get; set; } = null!;
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
        var overviewFirstStatisticsRow = RetainedOverviewFirstStatisticsRowPolicy.CreateCanvasLayout(
            referenceTransform,
            overviewLeftPanel);
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
            OverviewFirstStatisticsRow = overviewFirstStatisticsRow
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
    public const int OverviewLeftPanelContentChildCount = 2;
    public const int OverviewProfileSummaryHeadingChildCount = 0;
    public const int OverviewFirstStatisticsRowChildCount = 1;
    public const int OverviewFirstStatisticsRowContentChildCount = 0;
    public const int OverviewRightPanelChildCount = 0;
    public const int GraphicCount = 28;
    public const int ButtonCount = 10;
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
        CraftingMetricCapabilities currentCraftingCapabilities)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (currentEconomyCapabilities == null)
            throw new ArgumentNullException(nameof(currentEconomyCapabilities));
        if (currentCraftingCapabilities == null)
            throw new ArgumentNullException(nameof(currentCraftingCapabilities));
        if (!HasProvableGeneration(profile, profile.GenerationId))
            throw new InvalidOperationException("The active UDS generation cannot be proven for UI projection.");

        var weapons = WeaponStatisticsViewModelFactory.Create(profile);
        var equipment = EquipmentStatisticsViewModelFactory.Create(profile);
        return new StatisticsPanelProjection
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
                .Take(5)
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
            Crafting = profile.Statistics.Crafting,
            CraftingCapabilities = CraftingStatisticsReducer.RestrictWithCurrent(
                profile.Statistics.Crafting.Capabilities,
                currentCraftingCapabilities),
            ItemUse = CreateItemUse(profile),
            WeaponAmmunitionGroups = CreateWeaponAmmunitionGroups(weapons),
            CraftingOutputs = CreateCraftingOutputs(profile.Statistics.Crafting),
            CraftingResources = CreateCraftingResources(profile.Statistics.Crafting)
        };
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
