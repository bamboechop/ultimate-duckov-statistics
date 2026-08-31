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

    public static bool PreservesProceduralImageState(IEnumerable<string?> typeHierarchy)
    {
        if (typeHierarchy == null) throw new ArgumentNullException(nameof(typeHierarchy));
        return typeHierarchy.Any(typeName =>
            string.Equals(typeName, ProceduralImageModifierTypeName, StringComparison.Ordinal));
    }
}

internal static class NativeShellTemplatePolicy
{
    public static int ScoreHeading(string? path, float fontSize)
    {
        if (string.IsNullOrWhiteSpace(path) || fontSize < 56f) return 0;
        if (path.EndsWith("/MainMenuContainer/Menu/OptionsPanel/Text (TMP)", StringComparison.Ordinal)) return 2000;
        if (path.Contains("/OptionsPanel/", StringComparison.Ordinal) && fontSize >= 80f) return 1500;
        if (path.Contains("/MainTitle/", StringComparison.Ordinal)) return 0;
        return fontSize >= 80f ? 200 : 0;
    }

    public static int ScoreBack(string? path, bool hasIcon)
    {
        if (string.IsNullOrWhiteSpace(path) || !hasIcon) return 0;
        if (path.EndsWith("/MainMenuContainer/Menu/OptionsPanel/Return", StringComparison.Ordinal)) return 2000;
        if (path.EndsWith("/Return", StringComparison.Ordinal)) return 800;
        if (path.EndsWith("/Back", StringComparison.Ordinal)) return 700;
        return 0;
    }

    public static int ScoreTab(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return 0;
        if (path.EndsWith("/MainMenuContainer/Menu/OptionsPanel/Tabs/Common", StringComparison.Ordinal)) return 2000;
        return path.Contains("/OptionsPanel/Tabs/", StringComparison.Ordinal) ? 800 : 0;
    }

    public static int ScoreSurface(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return 0;
        if (path.EndsWith("/MainMenuContainer/Menu/OptionsPanel/ScrollView/Background", StringComparison.Ordinal))
            return 2000;
        return path.EndsWith("/ScrollView/Background", StringComparison.Ordinal) ? 800 : 0;
    }

    public static int ScoreRail(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return 0;
        if (path.EndsWith("/MainMenuContainer/Menu/OptionsPanel/Tabs/Image", StringComparison.Ordinal)) return 2000;
        return path.EndsWith("/Tabs/Image", StringComparison.Ordinal) ? 800 : 0;
    }
}

internal enum NativeTypographyRole
{
    Title,
    Navigation,
    Body,
    Secondary
}

internal enum NativeTypographySource
{
    PublicTextTemplate,
    LiveMenuButton,
    NativeHeading
}

internal static class NativeTypographyRolePolicy
{
    public static NativeTypographySource Resolve(
        NativeTypographyRole role,
        bool hasLiveMenuButton,
        bool hasNativeHeading = false) =>
        role switch
        {
            NativeTypographyRole.Title when hasNativeHeading => NativeTypographySource.NativeHeading,
            NativeTypographyRole.Title or NativeTypographyRole.Navigation when hasLiveMenuButton =>
                NativeTypographySource.LiveMenuButton,
            NativeTypographyRole.Title or NativeTypographyRole.Navigation or NativeTypographyRole.Body
                or NativeTypographyRole.Secondary => NativeTypographySource.PublicTextTemplate,
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };
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

internal sealed class RetainedShellLayout
{
    public float MarginPixels { get; set; }
    public float InnerMarginPixels { get; set; }
    public float HeaderHeightPixels { get; set; }
    public float TabRowHeightPixels { get; set; }
    public float TabWidthPixels { get; set; }
    public float TabHeightPixels { get; set; }
    public float TabSpacingPixels { get; set; }
    public float TabPaddingPixels { get; set; }
    public float TitleFontPixels { get; set; }
    public float NavigationFontPixels { get; set; }
    public float BodyFontPixels { get; set; }
    public float SecondaryFontPixels { get; set; }
    public float BackControlPixels { get; set; }
    public float NavigationRailPixels { get; set; }
    public float TabViewportWidthPixels { get; set; }
    public float TabContentWidthPixels { get; set; }
    public bool TabStripRequiresScrolling { get; set; }
}

internal static class RetainedShellLayoutPolicy
{
    public static RetainedShellLayout Create(float screenWidth, float screenHeight)
    {
        if (screenWidth <= 0f) throw new ArgumentOutOfRangeException(nameof(screenWidth));
        if (screenHeight <= 0f) throw new ArgumentOutOfRangeException(nameof(screenHeight));
        var margin = screenWidth <= 1200f
            ? Math.Clamp(screenWidth * 0.024f, 18f, 30f)
            : Math.Clamp(screenWidth * 0.0332f, 48f, 96f);
        var narrow = screenWidth <= 1200f;
        var innerMargin = narrow ? 18f : 28f;
        var tabWidth = narrow ? 168f : 148f;
        var tabHeight = narrow ? 60f : 62f;
        var tabSpacing = narrow ? 10f : 8f;
        var tabPadding = narrow ? 10f : 0f;
        var tabCount = PanelInteractionState.NavigationOrder.Count;
        var contentWidth = tabPadding * 2f
                           + tabCount * tabWidth
                           + Math.Max(0, tabCount - 1) * tabSpacing;
        var viewportWidth = Math.Max(1f, screenWidth - margin * 2f - innerMargin * 2f);
        return new RetainedShellLayout
        {
            MarginPixels = margin,
            InnerMarginPixels = innerMargin,
            HeaderHeightPixels = narrow ? 100f : 126f,
            TabRowHeightPixels = narrow ? 72f : 76f,
            TabWidthPixels = tabWidth,
            TabHeightPixels = tabHeight,
            TabSpacingPixels = tabSpacing,
            TabPaddingPixels = tabPadding,
            TitleFontPixels = narrow ? 48f : 58f,
            NavigationFontPixels = narrow ? 24f : 27f,
            BodyFontPixels = narrow ? 20f : 22f,
            SecondaryFontPixels = narrow ? 18f : 20f,
            BackControlPixels = narrow ? 56f : 64f,
            NavigationRailPixels = 5f,
            TabViewportWidthPixels = viewportWidth,
            TabContentWidthPixels = contentWidth,
            TabStripRequiresScrolling = contentWidth > viewportWidth
        };
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
}

internal static class RetainedShellLayerPolicy
{
    public const float BlockerOpacity = 0.68f;
    public const float FrameOpacity = 0.82f;
    public const float ContentOpacity = 0.72f;

    public static float BackgroundTransmission(params float[] opacities)
    {
        if (opacities == null) throw new ArgumentNullException(nameof(opacities));
        var transmission = 1f;
        foreach (var opacity in opacities)
        {
            if (opacity < 0f || opacity > 1f) throw new ArgumentOutOfRangeException(nameof(opacities));
            transmission *= 1f - opacity;
        }

        return transmission;
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
