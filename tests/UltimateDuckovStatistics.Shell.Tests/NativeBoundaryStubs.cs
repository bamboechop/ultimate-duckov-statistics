#pragma warning disable CS9113 // Boundary constructors match production child-view signatures.
using UltimateDuckovStatistics.Core.Diagnostics;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using TMPro;
using UnityEngine;

// Isolated external boundaries. Actual panel orchestration, shared shell,
// overview, projection factories, tab construction/scroll and material ownership
// and native menu discovery are source-linked. Child views and GPU assets are not.
namespace UltimateDuckovStatistics.Adapters
{
    internal sealed class NativeProfileCoordinator(string dataRoot)
    {
        public string DataRoot => dataRoot;
        public ProfileDocument Current { get; set; } = new();
        public string CurrentGenerationId => Current.GenerationId;
        public bool HasPendingProfileTransition { get; set; }
        public EconomyMetricCapabilities CurrentEconomyCapabilities { get; } = new();
        public CraftingMetricCapabilities CurrentCraftingCapabilities { get; } = new();
        public WorldTimeMetricCapabilities CurrentWorldTimeCapabilities { get; } = new();
        public ProfileSaveReceipt? LastSaveReceipt => null;
        public bool HasProfilePersistenceFailure { get; set; }
        public ProfileOpenResult? LastOpenResult => null;
        public string LastOpenStatus => "Isolated current-format fixture";
        public IReadOnlyList<DiagnosticEntry> DiagnosticEntries => Array.Empty<DiagnosticEntry>();
        public NativeUserResetAttempt? LastUserResetAttempt => null;
        public event Action? ProfileChanging;
        public event Action? ProfileChanged;
        public int ProfileListeners => (ProfileChanging?.GetInvocationList().Length ?? 0) + (ProfileChanged?.GetInvocationList().Length ?? 0);
        public List<string> Reports { get; } = new();
        public void ReportUiDiagnostic(string message, string severity = "Info") => Reports.Add(message);
        public Task<ProfileExportResult> BeginExportCurrent() => throw new NotSupportedException("Not an export test");
        public bool ResetCurrent() => throw new NotSupportedException("Not a reset test");
    }
    internal static class NativeRaidContext { public static bool InRaid = false; public static bool IsRaidMap() => InRaid; }
}
namespace UltimateDuckovStatistics.UI
{
    internal sealed class NativeHeaderTitleTypography
    {
        public TMP_FontAsset Font { get; } = new() { name = "Alternative native heading font" };
        public Material Material { get; } = new() { name = "Alternative native material" };
        public string SourceDescription => "Test asset boundary";
    }
    internal static class NativeHeaderTitleTypographyResolver
    {
        public static NativeHeaderTitleTypography Typography = new();
        public static bool Unavailable;
        public static bool TryResolve(Canvas canvas, out NativeHeaderTitleTypography typography, out string? error)
        { typography = Typography; error = Unavailable ? "Typography unavailable" : null; return !Unavailable; }
    }
    internal sealed class RetainedBackArrowAsset : IDisposable
    {
        public Sprite Sprite { get; } = new();
        public static RetainedBackArrowAsset Create() => new();
        public void Dispose() => UnityEngine.Object.Destroy(Sprite);
    }
    internal sealed class RetainedRunBadgeIconAsset : IDisposable
    {
        public Sprite Sprite { get; } = new();
        public static RetainedRunBadgeIconAsset Create(object kind) => new();
        public void Dispose() => UnityEngine.Object.Destroy(Sprite);
    }
    internal static class NativeThrowableIdentity { public static bool IsThrowable(string id) => false; }
    internal sealed class RunsFocusHandler : MonoBehaviour { public Action? Selected; public Action<UnityEngine.EventSystems.MoveDirection>? Move; }
    internal sealed class RunsHistoryButton : UnityEngine.UI.Button { }
    internal sealed class RunsButtonFeedback : MonoBehaviour { }
    internal sealed partial class RetainedStatisticsShell
    {
        private static void AddButtonFeedback(UnityEngine.UI.Button button) { }
        private RunsView? runsView;
        private RecordsView? recordsView;
        private CombatView? combatView;
        private EquipmentView? equipmentView;
        private EconomyView? economyView;
        private CraftingView? craftingView;
        private ItemUseView? itemUseView;
        private DiagnosticsView? diagnosticsView;
        public void RefreshDiagnostics(DiagnosticsPresentation? presentation) => diagnosticsView?.Refresh(presentation);
        public void InvalidateProjection() { }
        private void RefreshRuns(StatisticsPanelProjection projection, string generation) => runsView?.Refresh(RunsPresentationFactory.Create(projection, generation), generation);
        private class BoundaryView : IDisposable
        {
            private readonly GameObject root;
            protected BoundaryView(RectTransform parent) { root = new GameObject(GetType().Name); root.transform.SetParent(parent); }
            public void Refresh(object? value, string? generation = null) { }
            public void SetVisible(bool value) => root.SetActive(value);
            public void Layout(RetainedVisualCanvasLayout layout, params float[] dimensions) { }
            public void Tick() { }
            public void Route(string generation, string id) { }
            public void FocusHistory() { }
            public void FocusPage() { }
            public void FocusSelector() { }
            public void FocusFirst() { }
            public void FocusReset() { }
            public void Dispose() => UnityEngine.Object.Destroy(root);
        }
        private sealed class RunsView(RectTransform parent, NativeHeaderTitleTypography typography, Material material, Action fallback) : BoundaryView(parent);
        private sealed class RecordsView(RectTransform parent, NativeHeaderTitleTypography typography, Material material, Action<string, string> route, Action fallback) : BoundaryView(parent);
        private sealed class CombatView(RectTransform parent, NativeHeaderTitleTypography typography, Material material, Action fallback) : BoundaryView(parent);
        private sealed class EquipmentView(RectTransform parent, NativeHeaderTitleTypography typography, Material material, Action<string, string> route, Action fallback) : BoundaryView(parent);
        private sealed class EconomyView(RectTransform parent, NativeHeaderTitleTypography typography, Material material, Action<string, string> route, Action fallback) : BoundaryView(parent);
        private sealed class CraftingView(RectTransform parent, NativeHeaderTitleTypography typography, Material material, Action fallback) : BoundaryView(parent);
        private sealed class ItemUseView(RectTransform parent, NativeHeaderTitleTypography typography, Material material, Action<string, string> route, Action fallback) : BoundaryView(parent);
        private sealed class DiagnosticsView(RectTransform parent, NativeHeaderTitleTypography typography, Material material, PanelOperationController operations, Action hotkey, Func<bool> copyExport, Func<bool> copyData, Action fallback) : BoundaryView(parent);
    }
}
