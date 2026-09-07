using Duckov.UI;
using Duckov.UI.Animations;
using TMPro;
using UltimateDuckovStatistics.Core.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

// Selection is handled through the native event system, including controller move/submit.
internal sealed class RunsFocusHandler : MonoBehaviour, IMoveHandler, ISelectHandler
{
    public Action<MoveDirection>? Move { get; set; }
    public Action? Selected { get; set; }
    public void OnMove(AxisEventData eventData) { Move?.Invoke(eventData.moveDir); eventData.Use(); }
    public void OnSelect(BaseEventData eventData)
    {
        Selected?.Invoke();
        for (var parent = transform.parent; parent != null; parent = parent.parent)
        {
            var scroll = parent.GetComponent<ScrollRect>();
            if (scroll == null || !scroll.vertical || scroll.content == null || scroll.viewport == null) continue;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.content, transform);
            var offset = RunsLayoutPolicy.Reveal(scroll.content.anchoredPosition.y, scroll.viewport.rect.height,
                scroll.content.rect.height, -bounds.max.y, Math.Min(bounds.size.y, scroll.viewport.rect.height));
            scroll.StopMovement(); scroll.content.anchoredPosition = new Vector2(0, offset);
        }
    }
}

internal sealed class RunsScrollRect : ScrollRect
{
    public override void OnScroll(PointerEventData data)
    {
        foreach (var row in GetComponentsInChildren<RunsHistoryButton>()) row.Binding.CancelPointer();
        if (content != null && viewport != null && RunsScrollPolicy.Forward(viewport.rect.height,
                content.rect.height, content.anchoredPosition.y, -data.scrollDelta.y))
        {
            var parent = transform.parent?.GetComponentInParent<ScrollRect>();
            if (parent != null) { parent.OnScroll(data); return; }
        }
        base.OnScroll(data);
    }

    public void MoveBy(float amount)
    {
        if (content == null || viewport == null) return;
        if (RunsScrollPolicy.Forward(viewport.rect.height, content.rect.height, content.anchoredPosition.y, amount))
        {
            var parent = transform.parent?.GetComponentInParent<RunsScrollRect>();
            if (parent != null) { parent.MoveBy(amount); return; }
        }
        StopMovement();
        content.anchoredPosition = new Vector2(0, Math.Clamp(content.anchoredPosition.y + amount, 0, Math.Max(0, content.rect.height - viewport.rect.height)));
    }
}

internal sealed class RunsButtonFeedback : MonoBehaviour, ISelectHandler, IDeselectHandler, ISubmitHandler, IPointerEnterHandler, IPointerExitHandler
{
    private bool pointerInside;
    private UniformModifier? buttonShape, highlightShape;
    public void MatchShape(UniformModifier? source, UniformModifier highlight)
    {
        buttonShape = source; highlightShape = highlight;
        LateUpdate();
    }
    private void LateUpdate()
    {
        // Layout can change the button radius after creation (including pill shapes).
        // Keep every interaction state on the same outline without restarting its tint.
        if (buttonShape != null && highlightShape != null && highlightShape.Radius != buttonShape.Radius)
            highlightShape.Radius = buttonShape.Radius;
    }
    public void OnPointerEnter(PointerEventData eventData) => pointerInside = true;
    public void OnPointerExit(PointerEventData eventData) => pointerInside = false;
    public void OnSelect(BaseEventData eventData)
    {
        if (!pointerInside) GetComponent<ButtonAnimation>()?.OnPointerEnter(new PointerEventData(GameManager.EventSystem));
    }
    public void OnDeselect(BaseEventData eventData)
    {
        if (!pointerInside) GetComponent<ButtonAnimation>()?.OnPointerExit(new PointerEventData(GameManager.EventSystem));
    }
    private void OnDisable() => pointerInside = false;
    public void OnSubmit(BaseEventData eventData)
    {
        var button = GetComponent<Button>();
        if (button == null || !button.IsActive() || !button.IsInteractable()) return;
        var animation = GetComponent<ButtonAnimation>();
        var pointer = new PointerEventData(GameManager.EventSystem);
        animation?.OnPointerDown(pointer); animation?.OnPointerUp(pointer);
    }
}

internal sealed partial class RetainedStatisticsShell
{
    private RunsView? runsView;
    public void RefreshRuns(StatisticsPanelProjection? projection, string generation) =>
        runsView?.Refresh(projection == null ? null : RunsPresentationFactory.Create(projection, generation), generation);
    public void InvalidateProjection()
    {
        projectionAvailable = false;
        overviewContentView?.SetActive(false);
        runsView?.Refresh(null, string.Empty);
        recordsView?.Refresh(null);
        combatView?.Refresh(null);
        equipmentView?.Refresh(null);
        economyView?.Refresh(null);
        craftingView?.Refresh(null);
        itemUseView?.Refresh(null);
        diagnosticsView?.Refresh(null);
    }

    private sealed class ScrollRegion : IDisposable
    {
        public RectTransform Rect { get; }
        public RectTransform Content { get; }
        public ScrollRect Scroll { get; }
        private readonly RunsOverflowEdge top;
        private readonly RunsOverflowEdge bottom;
        public float Offset => Math.Max(0, Content.anchoredPosition.y);
        public ScrollRegion(RectTransform parent, string name, RectTransform? contour = null, float radius = 10)
        {
            Rect = Node(parent, name);
            var hit = Rect.gameObject.AddComponent<ProceduralImage>();
            hit.color = Color.clear;
            Rect.gameObject.AddComponent<UniformModifier>().Radius = radius;
            var viewport = Node(Rect, "Viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            Content = Node(viewport, "Content");
            Scroll = Rect.gameObject.AddComponent<RunsScrollRect>();
            Scroll.viewport = viewport; Scroll.content = Content;
            Scroll.horizontal = false; Scroll.vertical = true;
            RunsNativeScrollConfiguration.Apply(Scroll);
            Scroll.onValueChanged.AddListener(_ => Cues());
            top = Edge(contour ?? Rect, name + "Above", true); bottom = Edge(contour ?? Rect, name + "Below", false);
            top.Radius = bottom.Radius = radius;
            var selectable = Rect.gameObject.AddComponent<Selectable>();
            selectable.targetGraphic = hit;
            selectable.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var focus = Rect.gameObject.AddComponent<RunsFocusHandler>();
            focus.Move = direction =>
            {
                if (direction == MoveDirection.Up || direction == MoveDirection.Down)
                    ((RunsScrollRect)Scroll).MoveBy((direction == MoveDirection.Up ? -1 : 1) * Scroll.scrollSensitivity);
                else
                {
                    var next = direction == MoveDirection.Left ? selectable.FindSelectableOnLeft() : selectable.FindSelectableOnRight();
                    if (next != null) GameManager.EventSystem?.SetSelectedGameObject(next.gameObject);
                }
            };
        }
        public void Size(float x, float y, float width, float height, float contentHeight)
        {
            Place(Rect, x, y, width, Math.Max(1, height));
            Content.sizeDelta = new Vector2(width, Math.Max(height, contentHeight));
            Stretch(top.rectTransform); Stretch(bottom.rectTransform);
            top.rectTransform.SetAsLastSibling(); bottom.rectTransform.SetAsLastSibling();
            SetOffset(Offset);
        }
        public void SetOffset(float offset)
        {
            Content.anchoredPosition = new Vector2(0, Math.Clamp(offset, 0, Math.Max(0, Content.rect.height - Rect.rect.height)));
            Scroll.StopMovement(); Cues();
        }
        public void Cues()
        {
            if (Rect.rect.height <= 0) return;
            var state = OverflowCuePolicy.Resolve(Rect.rect.height, Content.rect.height, Offset);
            top.enabled = state.ShowLeading; bottom.enabled = state.ShowTrailing;
        }
        private static RunsOverflowEdge Edge(RectTransform parent, string name, bool top)
        {
            var edge = Node(parent, name).gameObject.AddComponent<RunsOverflowEdge>();
            edge.Top = top; edge.color = new Color(1, 1, 1, .3f); edge.raycastTarget = false; return edge;
        }
        private static RectTransform Node(RectTransform parent, string name)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); return rect;
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        { rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); }
        public void Dispose() => Scroll.onValueChanged.RemoveAllListeners();
    }

    private sealed class RunsView : IDisposable
    {
        private sealed class HistoryControl : IDisposable
        {
            public RectTransform Rect = null!;
            public RunsHistoryButton Button = null!;
            public ProceduralImage Background = null!;
            public TextMeshProUGUI Title = null!;
            public TextMeshProUGUI Metadata = null!;
            public RetainedRunBadgeControl? Badge;
            public string Id = string.Empty;
            public int Index;
            public void Dispose() { Button.onClick.RemoveAllListeners(); Badge?.Dispose(); }
        }

        private readonly RectTransform root;
        private readonly NativeHeaderTitleTypography typography;
        private readonly Material material;
        private readonly Action focusTabs;
        private readonly RunsSelection selection = new();
        private readonly NativeItemIconResolver icons = new();
        private readonly ScrollRegion outer;
        private readonly RectTransform historyPanel;
        private readonly RectTransform detailPanel;
        private readonly ScrollRegion history;
        private readonly RectTransform fixedDetail;
        private readonly ScrollRegion equipmentCombat;
        private readonly ScrollRegion route;
        private readonly RectTransform evidencePanel;
        private readonly ScrollRegion evidence;
        private readonly TextMeshProUGUI evidenceNotice;
        private readonly Image evidenceItemIcon;
        private readonly TextMeshProUGUI evidenceItemFallback;
        private readonly TextMeshProUGUI evidenceItemName;
        private readonly List<(RectTransform Root, Image Icon, TextMeshProUGUI Fallback, TextMeshProUGUI Name, TextMeshProUGUI Slot)> evidenceRows = new();
        private readonly Button evidenceClose;
        private Button? evidenceOwner;
        private readonly TextMeshProUGUI measure;
        private readonly TextMeshProUGUI empty;
        private readonly TextMeshProUGUI title;
        private readonly TextMeshProUGUI metadata;
        private readonly TextMeshProUGUI integrity;
        private readonly TextMeshProUGUI routeHeading;
        private readonly TextMeshProUGUI routeSummary;
        private readonly List<(TextMeshProUGUI Title, TextMeshProUGUI Detail)> segments = new();
        private readonly TextMeshProUGUI equipmentHeading;
        private readonly RectTransform equipmentCard;
        private readonly TextMeshProUGUI combatHeading;
        private readonly RectTransform combatCard;
        private readonly TextMeshProUGUI ranged;
        private readonly TextMeshProUGUI melee;
        private readonly List<(TextMeshProUGUI Label, TextMeshProUGUI Value)> summary = new();
        private readonly RunsControlPool<HistoryControl> rowPool;
        private IReadOnlyList<HistoryControl> Pool => rowPool.Items;
        private sealed class SlotControl
        {
            public RectTransform Root = null!;
            public Image Icon = null!;
            public TextMeshProUGUI Fallback = null!;
            public TooltipsProvider Tooltip = null!;
            public Button Button = null!;
            public readonly List<ProceduralImage> Dots = new();
            public TextMeshProUGUI Partial = null!;
        }
        private readonly List<SlotControl> slots = new();
        private RetainedRunBadgeControl? detailBadge;
        private RetainedRunBadgeControl? routeBadge;
        private float[] rowTops = Array.Empty<float>();
        private float[] rowHeights = Array.Empty<float>();
        private RunsHistoryRowLayout[] rowLayouts = Array.Empty<RunsHistoryRowLayout>();
        private float historyHeight;
        private RunsPresentation? measuredHistory;
        private float measuredHistoryWidth;
        private float width;
        private float height;
        private float lastHistoryOffset = -1;
        private bool stacked;
        private bool dirty = true;
        private bool revealSelected;
        private bool focusSelectedAfterLayout;
        private (string Generation, string? RunId, float History, float Detail, float Route, float Page)? suspendedScroll;
        private bool restoreSuspendedScroll;
        private bool disposed;

        public RunsView(RectTransform parent, NativeHeaderTitleTypography typography, Material material, Action focusTabs)
        {
            this.typography = typography; this.material = material;
            this.focusTabs = focusTabs;
            rowPool = new RunsControlPool<HistoryControl>(NewHistoryControl);
            root = Node(parent, "RunsContentView");
            outer = new ScrollRegion(root, "RunsPage");
            historyPanel = Panel(outer.Content, "RunHistory", 20);
            detailPanel = Panel(outer.Content, "SelectedRun", 20);
            history = new ScrollRegion(historyPanel, "RunHistoryScroll", historyPanel, 20);
            RunsHistoryClipping.Attach(history.Scroll.viewport);
            fixedDetail = Node(detailPanel, "FixedRunHeaderAndSummary");
            equipmentCombat = new ScrollRegion(fixedDetail, "EquipmentCombatScroll");
            equipmentCombat.Rect.GetComponent<ProceduralImage>().color = new Color(0, 0, 0, .12f);
            measure = Text(root, "Measure", 28); measure.enabled = false;
            empty = Text(history.Content, "EmptyHistory", 28);
            title = Text(fixedDetail, "RunTitle", 48);
            metadata = Text(fixedDetail, "RunMetadata", 22);
            integrity = Text(fixedDetail, "RunIntegrity", 22);
            for (var i = 0; i < 10; i++)
            {
                var label = Text(fixedDetail, "SummaryLabel" + i, RunsViewStyle.SummaryLabelSize);
                var value = Text(fixedDetail, "SummaryValue" + i, RunsViewStyle.SummaryValueSize);
                label.color = Muted; label.alignment = value.alignment = TextAlignmentOptions.Top;
                summary.Add((label, value));
            }
            routeHeading = Text(fixedDetail, "RouteHeading", 38);
            var routeCard = Panel(fixedDetail, "RouteCard", 10);
            route = new ScrollRegion(routeCard, "RouteScroll");
            routeSummary = Text(fixedDetail, "RouteSummary", 24); routeSummary.color = Muted;
            equipmentHeading = Text(equipmentCombat.Content, "EquipmentHeading", 38);
            equipmentCard = Panel(equipmentCombat.Content, "TerminalEquipment", 10);
            combatHeading = Text(equipmentCombat.Content, "CombatHeading", 38);
            combatCard = Panel(equipmentCombat.Content, "CombatCard", 10);
            ranged = Text(combatCard, "Ranged", 24); melee = Text(combatCard, "Melee", 24);
            // A bounded focus-detail surface reuses our retained controls. Native Tooltips live on
            // GameplayUICanvas and may be absent or behind this modal on a menu access surface.
            evidencePanel = Panel(root, "CapturedEquipmentDetails", 16);
            evidencePanel.GetComponent<ProceduralImage>().color = new Color(.015f, .035f, .05f, .98f);
            evidencePanel.GetComponent<ProceduralImage>().raycastTarget = true;
            evidenceItemIcon = Node(evidencePanel, "EquipmentHeaderIcon").gameObject.AddComponent<Image>();
            evidenceItemIcon.raycastTarget = false; evidenceItemIcon.preserveAspect = true;
            evidenceItemFallback = Text(evidencePanel, "EquipmentHeaderFallback", 32);
            evidenceItemFallback.alignment = TextAlignmentOptions.Center;
            evidenceItemName = Text(evidencePanel, "EquipmentHeaderName", 24);
            evidence = new ScrollRegion(evidencePanel, "CapturedEquipmentEvidence");
            evidence.Rect.GetComponent<ProceduralImage>().color = new Color(0, 0, 0, .2f);
            evidenceNotice = Text(evidence.Content, "IncompleteEvidence", 20); evidenceNotice.color = Muted;
            var close = Panel(evidencePanel, "CloseEvidence", 10);
            close.GetComponent<ProceduralImage>().raycastTarget = true;
            evidenceClose = close.gameObject.AddComponent<Button>(); evidenceClose.targetGraphic = close.GetComponent<ProceduralImage>();
            close.gameObject.AddComponent<ButtonAnimation>(); AddButtonFeedback(evidenceClose);
            var closeLabel = Text(close, "CloseLabel", 26); closeLabel.text = "×"; closeLabel.alignment = TextAlignmentOptions.Center;
            Stretch(closeLabel.rectTransform);
            evidenceClose.onClick.AddListener(HideEvidence);
            evidencePanel.gameObject.SetActive(false);
        }

        public void Refresh(RunsPresentation? snapshot, string generation)
        {
            var previousId = selection.SelectedId;
            if (snapshot == null && selection.Snapshot != null)
                suspendedScroll = (selection.Snapshot.GenerationId, selection.SelectedId, history.Offset, equipmentCombat.Offset, route.Offset, outer.Offset);
            restoreSuspendedScroll = snapshot != null && suspendedScroll?.Generation == generation;
            if (snapshot != null && selection.Snapshot?.GenerationId != generation && !restoreSuspendedScroll)
            { history.SetOffset(0); equipmentCombat.SetOffset(0); route.SetOffset(0); outer.SetOffset(0); suspendedScroll = null; }
            if (snapshot == null) selection.Invalidate(); else selection.Refresh(snapshot, generation);
            restoreSuspendedScroll &= suspendedScroll?.RunId == selection.SelectedId;
            if (snapshot != null && !restoreSuspendedScroll && previousId != selection.SelectedId)
            { equipmentCombat.SetOffset(0); route.SetOffset(0); }
            if (snapshot == null)
                foreach (var row in Pool) row.Rect.gameObject.SetActive(false);
            UpdateDetails(); dirty = true;
        }
        public void Route(string generation, string id)
        {
            selection.Route(generation, id); UpdateDetails(); dirty = true; revealSelected = true; focusSelectedAfterLayout = true;
            equipmentCombat.SetOffset(0); route.SetOffset(0);
        }
        public void SetVisible(bool visible) { if (!visible) HideEvidence(false); root.gameObject.SetActive(visible); if (visible) dirty = true; }
        public void FocusHistory()
        {
            if (dirty && width > 0) Reflow();
            var rows = selection.Snapshot?.Runs;
            var index = rows == null ? -1 : Array.FindIndex(rows.ToArray(), row => row.Id == selection.SelectedId);
            if (index >= 0) { RevealRow(index); RenderHistory(); }
            var target = Pool.FirstOrDefault(row => row.Rect.gameObject.activeSelf && row.Id == selection.SelectedId);
            GameManager.EventSystem?.SetSelectedGameObject(target?.Button.gameObject ?? history.Rect.gameObject);
        }

        private void UpdateDetails()
        {
            HideEvidence(false);
            var run = selection.Selected;
            title.text = run?.Title ?? UiText.Get(selection.Snapshot == null ? "ui.profile_unavailable"
                : selection.RequestedRunUnavailable ? "ui.runs_requested_unavailable" : "ui.runs_empty");
            foreach (var cell in summary) { cell.Label.gameObject.SetActive(run != null); cell.Value.gameObject.SetActive(run != null); }
            routeHeading.gameObject.SetActive(run != null); routeSummary.gameObject.SetActive(run != null);
            equipmentCombat.Rect.gameObject.SetActive(run != null);
            route.Rect.parent.gameObject.SetActive(run != null);
            equipmentHeading.gameObject.SetActive(run != null); equipmentCard.gameObject.SetActive(run != null);
            combatHeading.gameObject.SetActive(run != null); combatCard.gameObject.SetActive(run != null);
            equipmentCard.GetComponent<ProceduralImage>().color = run?.TerminalState == TerminalLoadoutState.Complete
                ? new Color(0, 0, 0, .5f) : new Color(.15f, .15f, .15f, .5f);
            metadata.text = run?.Metadata ?? string.Empty; integrity.text = run?.Integrity ?? string.Empty;
            for (var i = 0; i < summary.Count; i++)
            {
                summary[i].Label.text = run == null ? string.Empty : RunsViewStyle.Uppercase(run.Summary[i].Key);
                summary[i].Value.text = run == null ? string.Empty : run.Summary[i].Value;
            }
            routeHeading.text = run == null ? string.Empty : UiText.Get("ui.runs_route");
            routeSummary.text = run?.RouteSummary ?? string.Empty;
            while (segments.Count < (run?.Segments.Count ?? 0))
            {
                var primary = Text(route.Content, "SegmentMap" + segments.Count, RunsViewStyle.SegmentTitleSize);
                var secondary = Text(route.Content, "SegmentFacts" + segments.Count, RunsViewStyle.SegmentDetailSize);
                secondary.color = Muted; segments.Add((primary, secondary));
            }
            for (var i = 0; i < segments.Count; i++)
            {
                var active = run != null && i < run.Segments.Count;
                segments[i].Title.gameObject.SetActive(active); segments[i].Detail.gameObject.SetActive(active);
                if (active) { segments[i].Title.text = run!.Segments[i].Key; segments[i].Detail.text = run.Segments[i].Value; }
            }
            equipmentHeading.text = run == null ? string.Empty : UiText.Get("ui.runs_equipment");
            combatHeading.text = run == null ? string.Empty : UiText.Get("ui.runs_combat");
            ranged.text = run == null ? string.Empty : RunsViewStyle.Uppercase(UiText.Get("ui.runs_ranged")) + "\n" + run.Ranged;
            melee.text = run == null ? string.Empty : RunsViewStyle.Uppercase(UiText.Get("ui.runs_melee")) + "\n" + run.Melee;
            ReplaceBadge(ref detailBadge, fixedDetail, run?.Outcome);
            ReplaceBadge(ref routeBadge, route.Content, run?.Outcome);
            // Ten compact positions at baseline. An unreadable remainder is anonymous unavailable
            // evidence, not invented slot IDs or empty slots. Additional modded captured roots survive.
            var count = run == null ? 0 : Math.Max(10, run.Slots.Count);
            while (slots.Count < count)
            {
                var rootSlot = Panel(equipmentCard, "TerminalSlot" + slots.Count, RunsViewStyle.SlotRadius);
                var icon = Node(rootSlot, "Icon").gameObject.AddComponent<Image>(); icon.raycastTarget = false; icon.preserveAspect = true;
                var borderRect = Node(rootSlot, "SlotBorder"); Stretch(borderRect);
                var border = borderRect.gameObject.AddComponent<ProceduralImage>(); border.raycastTarget = false;
                border.color = new Color32(RunsViewStyle.BorderRed, RunsViewStyle.BorderGreen, RunsViewStyle.BorderBlue, 255);
                border.BorderWidth = RunsViewStyle.SlotBorder; border.FalloffDistance = 1;
                borderRect.gameObject.AddComponent<UniformModifier>().Radius = RunsViewStyle.SlotRadius;
                var fallback = Text(rootSlot, "Fallback", 32); fallback.alignment = TextAlignmentOptions.Center;
                var partial = Text(rootSlot, "IncompleteAttachments", 18); partial.color = Muted;
                var control = new SlotControl
                {
                    Root = rootSlot,
                    Icon = icon,
                    Fallback = fallback,
                    Tooltip = AttachTooltip(rootSlot),
                    Partial = partial
                };
                control.Button = rootSlot.GetComponent<Button>();
                control.Button.onClick.AddListener(() => ShowEvidence(control));
                slots.Add(control);
            }
            for (var i = 0; i < slots.Count; i++)
            {
                var control = slots[i]; var item = run != null && i < run.Slots.Count ? run.Slots[i] : null;
                control.Root.gameObject.SetActive(i < count);
                if (i >= count) continue;
                control.Button.interactable = item?.CanOpenDetails == true;
                var tooltipText = SafeTooltip(item == null ? UiText.Get("ui.unavailable") + "\n" + run!.EquipmentState : item.Text + "\n" + run!.EquipmentState);
                if (!string.Equals(control.Tooltip.text, tooltipText, StringComparison.Ordinal))
                { control.Tooltip.OnPointerExit(null!); control.Tooltip.text = tooltipText; }
                var sprite = item == null ? null : RunsItemIconPolicy.Resolve(item, icons.ResolveAvailable);
                control.Icon.sprite = sprite; control.Icon.enabled = sprite != null;
                NativeTotemIconAppearance.Apply(control.Icon, item?.ItemId);
                var emptyIcon = item?.State == EquipmentSlotState.Empty || NativeItemTypeIdPolicy.UseEmptyIcon(item?.ItemId);
                control.Fallback.text = sprite != null ? string.Empty : emptyIcon ? "—" : "?";
                control.Fallback.color = emptyIcon ? Muted : Color.white;
                control.Root.GetComponent<ProceduralImage>().color = item == null || item.State is not (EquipmentSlotState.Occupied or EquipmentSlotState.Empty)
                    ? new Color(.28f, .28f, .28f, .5f) : new Color(0, 0, 0, .5f);
                var attachments = item?.Attachments;
                while (control.Dots.Count < (attachments?.Count ?? 0))
                {
                    var dot = Node(control.Root, "Attachment" + control.Dots.Count).gameObject.AddComponent<ProceduralImage>();
                    dot.raycastTarget = false; dot.gameObject.AddComponent<UniformModifier>().Radius = 4;
                    control.Dots.Add(dot);
                }
                for (var d = 0; d < control.Dots.Count; d++)
                {
                    var dot = control.Dots[d]; dot.gameObject.SetActive(d < (attachments?.Count ?? 0));
                    if (d >= (attachments?.Count ?? 0)) continue;
                    dot.BorderWidth = attachments![d] == EquipmentSlotState.Empty ? 1.5f : 0;
                    dot.color = attachments[d] == EquipmentSlotState.Occupied ? Color.white : Muted;
                }
                control.Partial.text = item?.NestedComplete == false ? "…" : string.Empty;
            }
            dirty = true;
        }

        public void Layout(RetainedVisualCanvasLayout shell, float viewportPixels, float canvasHeight)
        {
            var scale = shell.ReferenceTransform.CanvasLength(1);
            var top = shell.Header.Top + shell.Header.Height + 40 * scale;
            var newWidth = shell.Header.Width / scale;
            var newHeight = Math.Max(200, (canvasHeight - top) / scale - 30);
            var newStacked = RunsLayoutPolicy.Stack(viewportPixels);
            root.localScale = new Vector3(scale, scale, 1);
            Place(root, shell.Header.Left, top, newWidth, newHeight);
            if (width != newWidth || height != newHeight || stacked != newStacked)
            { width = newWidth; height = newHeight; stacked = newStacked; dirty = true; }
            if (dirty && root.gameObject.activeInHierarchy) Reflow();
        }

        private void Reflow(bool forceStacked = false)
        {
            dirty = false;
            var useStacked = stacked || forceStacked;
            var hw = RunsLayoutPolicy.HistoryWidth(width, useStacked);
            var dw = useStacked ? width : width - hw - 40;
            var hh = useStacked ? Math.Min(400, height * .45f) : height;
            var dy = useStacked ? hh + 40 : 0;
            Place(historyPanel, 0, 0, hw, hh);
            var detailWidth = dw - 60;
            var y = Put(title, 0, 0, detailWidth) - CombatNativeTextMeasurement.AlignInkTop(title) + 6;
            y += LayoutMetadata(y, detailWidth) + 24;
            var columns = RunsLayoutPolicy.SummaryColumns(useStacked);
            var cellWidth = (detailWidth - (columns - 1) * 20) / columns;
            for (var start = 0; start < summary.Count; start += columns)
            {
                var valueHeight = 0f;
                for (var i = start; i < Math.Min(start + columns, summary.Count); i++)
                    valueHeight = Math.Max(valueHeight, Put(summary[i].Value, (i - start) * (cellWidth + 20), y, cellWidth));
                var labelHeight = 0f;
                for (var i = start; i < Math.Min(start + columns, summary.Count); i++)
                    labelHeight = Math.Max(labelHeight, Put(summary[i].Label, (i - start) * (cellWidth + 20), y + valueHeight + 4, cellWidth));
                y += labelHeight + 4 + valueHeight + 26;
            }
            // Extremely long localized/stored header content can exhaust a desktop column.
            // Reuse the existing responsive page so every section stays reachable, never reject text.
            if (!useStacked && y + 250 > height) { Reflow(forceStacked: true); return; }
            var lowerTop = y + 10;
            var lowerWidth = useStacked ? detailWidth : (detailWidth - 30) / 2;
            var headingHeight = LayoutRouteHeading(lowerTop, lowerWidth);
            var routeTop = lowerTop + headingHeight + 10;
            var routeHeight = RunsLowerLayout.RouteHeight(useStacked, height, routeTop);
            var routeCard = (RectTransform)route.Rect.parent;
            Place(routeCard, 0, routeTop, lowerWidth, routeHeight);
            var segmentY = 12f;
            for (var i = 0; i < (selection.Selected?.Segments.Count ?? 0); i++)
            {
                segmentY += Put(segments[i].Title, 20, segmentY, lowerWidth - 40);
                segmentY += Put(segments[i].Detail, 56, segmentY, lowerWidth - 76) + 16;
            }
            if (routeBadge != null) BadgeLayout(routeBadge, 20, segmentY, lowerWidth - 40);
            route.Size(0, 0, lowerWidth, routeHeight, segmentY + 12 + (routeBadge?.Rect.rect.height ?? 0));
            var rx = useStacked ? 0 : lowerWidth + 30;
            var rightTop = RunsLowerLayout.EquipmentTop(useStacked, lowerTop, routeTop, routeHeight);
            var ry = Put(equipmentHeading, 0, 0, lowerWidth) + 10;
            var equipmentY = ry;
            var slotSize = RunsViewStyle.SlotSize(lowerWidth);
            var activeSlots = selection.Selected == null ? 0 : Math.Max(10, selection.Selected.Slots.Count);
            var slotRows = (activeSlots + RunsViewStyle.SlotColumns - 1) / RunsViewStyle.SlotColumns;
            for (var i = 0; i < activeSlots; i++)
            {
                var slot = slots[i];
                Place(slot.Root, 10 + i % 5 * (slotSize + 10), 10 + i / 5 * (slotSize + 10), slotSize, slotSize);
                Place(slot.Icon.rectTransform, 6, 6, slotSize - 12, slotSize - 12);
                Place(slot.Fallback.rectTransform, 6, 6, slotSize - 12, slotSize - 12);
                // Captured descendants remain ordered. Long nested evidence wraps dots inside the card.
                var dotCount = selection.Selected != null && i < selection.Selected.Slots.Count ? selection.Selected.Slots[i].Attachments.Count : 0;
                for (var d = 0; d < slot.Dots.Count; d++)
                {
                    var dot = RunsViewStyle.AttachmentDot(slotSize, dotCount, d);
                    Place(slot.Dots[d].rectTransform, dot.X, dot.Y, dot.Size, dot.Size);
                }
                Put(slot.Partial, slotSize - 22, 0, 20);
            }
            var equipmentHeight = 10 + slotRows * (slotSize + 10);
            Place(equipmentCard, 0, equipmentY, lowerWidth, equipmentHeight);
            ry += equipmentHeight + 16;
            ry += Put(combatHeading, 0, ry, lowerWidth) + 10;
            var cw = (lowerWidth - 60) / 2;
            var combatHeight = Math.Max(Put(ranged, 20, 16, cw), Put(melee, 40 + cw, 16, cw)) + 32;
            Place(combatCard, 0, ry, lowerWidth, combatHeight);
            var rightContentHeight = ry + combatHeight;
            var rightHeight = RunsLowerLayout.EquipmentHeight(useStacked, height, rightTop, rightContentHeight);
            equipmentCombat.Size(rx, rightTop, lowerWidth, rightHeight, rightContentHeight);
            var contentHeight = selection.Selected == null ? title.rectTransform.rect.height + 30
                : Math.Max(routeTop + routeHeight, rightTop + rightHeight);
            var dh = useStacked ? contentHeight + 60 : height;
            Place(detailPanel, useStacked ? 0 : hw + 40, dy, dw, dh);
            Place(fixedDetail, 30, 30, detailWidth, dh - 60);
            outer.Size(0, 0, width, height, useStacked ? dy + dh : height);
            outer.Scroll.vertical = useStacked;
            LayoutEvidence();
            if (measuredHistory != selection.Snapshot || measuredHistoryWidth != hw - 60)
            {
                MeasureHistory(hw - 60);
                measuredHistory = selection.Snapshot; measuredHistoryWidth = hw - 60;
            }
            history.Size(30, 30, hw - 60, hh - 60, historyHeight);
            if (restoreSuspendedScroll && suspendedScroll.HasValue)
            {
                var offsets = suspendedScroll.Value;
                history.SetOffset(offsets.History); equipmentCombat.SetOffset(offsets.Detail);
                route.SetOffset(offsets.Route); outer.SetOffset(offsets.Page);
                restoreSuspendedScroll = false; suspendedScroll = null;
            }
            if (revealSelected)
            {
                var runs = selection.Snapshot?.Runs;
                if (runs != null)
                    for (var index = 0; index < runs.Count; index++)
                        if (runs[index].Id == selection.SelectedId) { RevealRow(index); break; }
                revealSelected = false;
            }
            empty.text = selection.Snapshot == null ? UiText.Get("ui.profile_unavailable") : UiText.Get("ui.runs_empty");
            empty.gameObject.SetActive(selection.Snapshot?.Runs.Count is null or 0);
            Put(empty, 10, 20, hw - 80);
            RenderHistory();
            if (focusSelectedAfterLayout)
            {
                focusSelectedAfterLayout = false;
                FocusHistory();
            }
        }

        private void MeasureHistory(float availableWidth)
        {
            var runs = selection.Snapshot?.Runs;
            rowTops = new float[runs?.Count ?? 0]; rowHeights = new float[rowTops.Length]; historyHeight = 0;
            rowLayouts = new RunsHistoryRowLayout[rowTops.Length];
            for (var i = 0; i < rowTops.Length; i++)
            {
                rowTops[i] = historyHeight;
                var spec = RetainedRunBadgePolicy.ResolveSpecification(runs![i].Outcome);
                measure.fontSize = RetainedRunBadgePolicy.ReferenceFontSize;
                var badgeText = UiText.Get(spec.TextKey);
                var labelWidth = Math.Max(1, Math.Min(availableWidth - 40 - RetainedRunBadgePolicy.FixedHorizontalContentPixels,
                    measure.GetPreferredValues(badgeText, float.PositiveInfinity, float.PositiveInfinity).x));
                var badgeWidth = labelWidth + RetainedRunBadgePolicy.FixedHorizontalContentPixels;
                var badgeHeight = Math.Max(30, measure.GetPreferredValues(badgeText, labelWidth, float.PositiveInfinity).y + 6);
                measure.fontSize = 22;
                var metadataHeight = measure.GetPreferredValues(runs[i].Metadata, Math.Max(1, availableWidth - 40), float.PositiveInfinity).y;
                measure.fontSize = 28;
                var runTitle = runs[i].Title;
                rowLayouts[i] = RunsHistoryRowLayout.Create(availableWidth, badgeWidth, badgeHeight,
                    titleWidth => measure.GetPreferredValues(runTitle, titleWidth, float.PositiveInfinity).y, metadataHeight);
                rowHeights[i] = rowLayouts[i].Height; historyHeight += rowHeights[i] + 10;
            }
        }

        private HistoryControl NewHistoryControl()
        {
            var row = new HistoryControl { Rect = Panel(history.Content, "RunRow", 10) };
            row.Background = row.Rect.GetComponent<ProceduralImage>();
            row.Title = Text(row.Rect, "RouteTitle", 28); row.Metadata = Text(row.Rect, "RunMetadata", 22);
            row.Button = row.Rect.gameObject.AddComponent<RunsHistoryButton>();
            row.Button.Configure(row.Background);
            row.Button.onClick.AddListener(() =>
            {
                var changed = selection.SelectedId != row.Id;
                if (!row.Button.Binding.Activate(selection)) return;
                if (changed) { equipmentCombat.SetOffset(0); route.SetOffset(0); }
                UpdateDetails();
            });
            NativeButtonInteractionFeedbackPolicy.AttachIfMissing(row.Rect.gameObject,
                static target => target.GetComponent<ButtonAnimation>() != null,
                static target => _ = target.AddComponent<ButtonAnimation>());
            AddButtonFeedback(row.Button);
            var focus = row.Rect.gameObject.AddComponent<RunsFocusHandler>();
            focus.Selected = () => RevealRow(row.Index);
            focus.Move = direction =>
            {
                if (direction == MoveDirection.Right) { GameManager.EventSystem?.SetSelectedGameObject(equipmentCombat.Rect.gameObject); return; }
                if (direction != MoveDirection.Up && direction != MoveDirection.Down) return;
                if (direction == MoveDirection.Up && row.Index == 0) { focusTabs(); return; }
                var index = Math.Clamp(row.Index + (direction == MoveDirection.Up ? -1 : 1), 0, rowTops.Length - 1);
                RevealRow(index); RenderHistory();
                var target = Pool.FirstOrDefault(control => control.Rect.gameObject.activeSelf && control.Index == index);
                if (target != null) GameManager.EventSystem?.SetSelectedGameObject(target.Button.gameObject);
            };
            return row;
        }

        private void RevealRow(int index)
        {
            if (index < 0 || index >= rowTops.Length) return;
            history.SetOffset(RunsLayoutPolicy.Reveal(history.Offset, history.Rect.rect.height, historyHeight, rowTops[index], rowHeights[index]));
        }

        private void RenderHistory()
        {
            var selectedObject = GameManager.EventSystem?.currentSelectedGameObject;
            var focusedId = Pool.FirstOrDefault(row => row.Button.gameObject == selectedObject)?.Id;
            lastHistoryOffset = history.Offset;
            var runs = selection.Snapshot?.Runs;
            var (first, end) = RunsHistoryWindow.Visible(rowTops, history.Offset, history.Rect.rect.height);
            rowPool.Ensure(end - first);
            for (var p = 0; p < Pool.Count; p++)
            {
                var control = Pool[p]; var index = first + p; var active = index < end;
                control.Rect.gameObject.SetActive(active); if (!active) continue;
                var run = runs![index]; control.Id = run.Id; control.Index = index;
                control.Button.Binding.Bind(selection.Snapshot!.GenerationId, run.Id);
                control.Title.text = run.Title; control.Metadata.text = run.Metadata;
                ReplaceBadge(ref control.Badge, control.Rect, run.Outcome);
                BadgeLayout(control.Badge!, 20, 10, history.Rect.rect.width - 40);
                var geometry = rowLayouts[index];
                Put(control.Title, geometry.TitleLeft, geometry.TitleTop, geometry.TitleWidth);
                Put(control.Metadata, 20, geometry.MetadataTop, history.Rect.rect.width - 40);
                Place(control.Rect, 0, rowTops[index], history.Rect.rect.width, rowHeights[index]);
                control.Background.color = run.Id == selection.SelectedId ? new Color(1, .62f, .18f, 1) : new Color(0, 0, 0, .5f);
            }
            if (focusedId != null)
            {
                var stillVisible = Pool.FirstOrDefault(row => row.Rect.gameObject.activeSelf && row.Id == focusedId);
                var target = stillVisible?.Button.gameObject ?? history.Rect.gameObject;
                if (target != selectedObject) GameManager.EventSystem?.SetSelectedGameObject(target);
            }
        }

        public void Tick()
        {
            if (!root.gameObject.activeInHierarchy) return;
            if (evidencePanel.gameObject.activeSelf)
            {
                var focused = GameManager.EventSystem?.currentSelectedGameObject;
                if (focused == null || !focused.transform.IsChildOf(evidencePanel)) HideEvidence(false);
            }
            if (history.Offset != lastHistoryOffset) RenderHistory();
            history.Cues(); route.Cues(); equipmentCombat.Cues(); outer.Cues(); evidence.Cues();
        }

        private void ReplaceBadge(ref RetainedRunBadgeControl? badge, RectTransform parent, RetainedRunBadgeState? state)
        {
            if (badge?.Presentation.State == state && badge != null) return;
            if (badge != null) { badge.Dispose(); badge.Rect.gameObject.SetActive(false); UnityEngine.Object.Destroy(badge.Rect.gameObject); badge = null; }
            if (!state.HasValue) return;
            var spec = RetainedRunBadgePolicy.ResolveSpecification(state.Value);
            badge = CreateOverviewLatestRunBadge(parent, new RetainedRunBadgePresentation
            { IsVisible = true, State = state, Specification = spec, Label = UiText.Get(spec.TextKey) }, typography, material);
        }

        private static void BadgeLayout(RetainedRunBadgeControl badge, float x, float y, float maximumWidth)
        {
            var transform = RetainedReferenceTransformPolicy.Create(2560, 1440, 1);
            badge.Label.fontSize = RetainedRunBadgePolicy.ReferenceFontSize;
            var w = badge.Label.GetPreferredValues(badge.Label.text, float.PositiveInfinity, float.PositiveInfinity).x;
            var labelWidth = Math.Max(1, Math.Min(w, maximumWidth - RetainedRunBadgePolicy.FixedHorizontalContentPixels));
            var layout = RetainedRunBadgePolicy.CreateCanvasLayout(transform, badge.Presentation.State!.Value, labelWidth);
            badge.Label.enableWordWrapping = true;
            layout.Height = layout.LabelHeight = Math.Max(layout.Height,
                badge.Label.GetPreferredValues(badge.Label.text, labelWidth, float.PositiveInfinity).y + 6);
            layout.IconTop = (layout.Height - layout.IconHeight) / 2;
            Place(badge.Rect, x, y, layout.Width, layout.Height); badge.Modifier.Radius = layout.CornerRadius;
            Place(badge.IconRect, layout.IconLeft, layout.IconTop, layout.IconWidth, layout.IconHeight);
            Place(badge.LabelRect, layout.LabelLeft, layout.LabelTop, layout.LabelWidth, layout.LabelHeight);
            if (badge.IconText != null) badge.IconText.fontSize = layout.FontSize;
        }

        private static Color Muted => new Color32(RunsViewStyle.Muted, RunsViewStyle.Muted, RunsViewStyle.Muted, 255);

        private float LayoutMetadata(float top, float availableWidth)
        {
            var controls = new List<RectTransform>();
            var sizes = new List<(float Width, float Height)>();
            if (detailBadge != null)
            {
                BadgeLayout(detailBadge, 0, top, availableWidth);
                controls.Add(detailBadge.Rect); sizes.Add((detailBadge.Rect.rect.width, detailBadge.Rect.rect.height));
            }
            foreach (var label in new[] { metadata, integrity })
            {
                var w = Math.Min(availableWidth, label.GetPreferredValues(label.text, float.PositiveInfinity, float.PositiveInfinity).x);
                var h = Put(label, 0, top, w); controls.Add(label.rectTransform); sizes.Add((w, h));
            }
            var layout = RunsFlowLayout.Arrange(availableWidth, sizes);
            var height = 0f;
            for (var i = 0; i < controls.Count; i++)
            {
                var box = layout[i]; Place(controls[i], box.X, top + box.Y, box.Width, box.Height);
                height = Math.Max(height, box.Y + box.Height);
            }
            return height;
        }

        private float LayoutRouteHeading(float top, float availableWidth)
        {
            var headingWidth = Math.Min(availableWidth, routeHeading.GetPreferredValues(routeHeading.text, float.PositiveInfinity, float.PositiveInfinity).x);
            var height = Put(routeHeading, 0, top, headingWidth);
            var summaryWidth = routeSummary.GetPreferredValues(routeSummary.text, float.PositiveInfinity, float.PositiveInfinity).x;
            // Both labels use the same native font. Align their first baseline using its ascent metrics.
            var ascent = typography.Font.faceInfo.ascentLine / typography.Font.faceInfo.pointSize;
            var baselineOffset = (routeHeading.fontSize - routeSummary.fontSize) * ascent;
            if (headingWidth + 12 + summaryWidth <= availableWidth)
                return Math.Max(height, baselineOffset + Put(routeSummary, headingWidth + 12, top + baselineOffset, summaryWidth));
            return height + Put(routeSummary, 0, top + height, availableWidth);
        }

        private static TooltipsProvider AttachTooltip(RectTransform rect)
        {
            var hit = rect.GetComponent<Graphic>(); hit.raycastTarget = true;
            var focusable = rect.gameObject.AddComponent<Button>(); focusable.targetGraphic = hit;
            focusable.transition = Selectable.Transition.None;
            rect.gameObject.AddComponent<ButtonAnimation>(); AddButtonFeedback(focusable);
            var provider = rect.gameObject.AddComponent<TooltipsProvider>();
            rect.gameObject.AddComponent<RunsTooltipFocus>();
            rect.gameObject.AddComponent<RunsFocusHandler>();
            return provider;
        }

        private void ShowEvidence(SlotControl slot)
        {
            var index = slots.IndexOf(slot);
            var run = selection.Selected;
            var item = run != null && index >= 0 && index < run.Slots.Count ? run.Slots[index] : null;
            if (item?.CanOpenDetails != true) return;
            evidenceOwner = slot.Button;
            evidenceNotice.text = item.NestedComplete ? string.Empty : UiText.Get("ui.runs_nested_partial");
            evidenceNotice.gameObject.SetActive(!item.NestedComplete);
            var rows = item.Evidence;
            var rootItem = rows[0];
            var rootSprite = RunsItemIconPolicy.Resolve(rootItem, icons.ResolveAvailable);
            evidenceItemIcon.sprite = rootSprite; evidenceItemIcon.enabled = rootSprite != null;
            NativeTotemIconAppearance.Apply(evidenceItemIcon, rootItem.ItemId);
            var rootEmptyIcon = NativeItemTypeIdPolicy.UseEmptyIcon(rootItem.ItemId);
            evidenceItemFallback.text = rootSprite != null ? string.Empty : rootEmptyIcon ? "—" : "?";
            evidenceItemFallback.color = rootEmptyIcon ? Muted : Color.white;
            evidenceItemName.text = rootItem.ItemName;
            var attachmentCount = rows.Count - 1;
            while (evidenceRows.Count < attachmentCount)
            {
                var row = Node(evidence.Content, "CapturedItem" + evidenceRows.Count);
                var icon = Node(row, "Icon").gameObject.AddComponent<Image>();
                icon.raycastTarget = false; icon.preserveAspect = true;
                var fallback = Text(row, "Fallback", 32); fallback.alignment = TextAlignmentOptions.Center;
                var slotName = Text(row, "SlotName", 20); slotName.color = Muted;
                evidenceRows.Add((row, icon, fallback, Text(row, "ItemName", 24), slotName));
            }
            for (var i = 0; i < evidenceRows.Count; i++)
            {
                var row = evidenceRows[i]; row.Root.gameObject.SetActive(i < attachmentCount);
                if (i >= attachmentCount) continue;
                var captured = rows[i + 1];
                var sprite = RunsItemIconPolicy.Resolve(captured, icons.ResolveAvailable);
                row.Icon.sprite = sprite; row.Icon.enabled = sprite != null;
                NativeTotemIconAppearance.Apply(row.Icon, captured.ItemId);
                var emptyIcon = captured.State == EquipmentSlotState.Empty || NativeItemTypeIdPolicy.UseEmptyIcon(captured.ItemId);
                row.Fallback.text = sprite != null ? string.Empty : emptyIcon ? "—" : "?";
                row.Fallback.color = emptyIcon ? Muted : Color.white;
                row.Name.text = captured.ItemName;
                row.Slot.text = RunsViewStyle.Uppercase(captured.SlotName);
            }
            evidencePanel.gameObject.SetActive(true); evidencePanel.SetAsLastSibling();
            LayoutEvidence(); evidence.SetOffset(0);
            GameManager.EventSystem?.SetSelectedGameObject(evidenceClose.gameObject);
        }

        private void LayoutEvidence()
        {
            var w = Math.Max(1, Math.Min(760, width - 40));
            var titleHeight = Put(evidenceItemName, 112, 16, w - 194);
            var y = 12f;
            foreach (var row in evidenceRows)
            {
                if (!row.Root.gameObject.activeSelf) continue;
                var nameHeight = Put(row.Name, 80, 0, w - 144);
                var slotHeight = Put(row.Slot, 80, nameHeight + 4, w - 144);
                var rowHeight = Math.Max(64, nameHeight + 4 + slotHeight);
                Place(row.Root, 16, y, w - 64, rowHeight);
                Place(row.Icon.rectTransform, 0, 0, 64, 64);
                Place(row.Fallback.rectTransform, 0, 0, 64, 64);
                y += rowHeight + 16;
            }
            if (evidenceNotice.gameObject.activeSelf) y += Put(evidenceNotice, 16, y, w - 64) + 12;
            var layout = RunsEvidenceLayout.Measure(height - 40, titleHeight, y);
            Place(evidencePanel, (width - w) / 2, (height - layout.Height) / 2, w, layout.Height);
            Place(evidenceItemIcon.rectTransform, 32, 16 + (layout.HeaderHeight - 64) / 2, 64, 64);
            Place(evidenceItemFallback.rectTransform, 32, 16 + (layout.HeaderHeight - 64) / 2, 64, 64);
            Place(evidenceItemName.rectTransform, 112, 16 + (layout.HeaderHeight - titleHeight) / 2, w - 194, titleHeight);
            Place((RectTransform)evidenceClose.transform, w - 66, 16 + (layout.HeaderHeight - 44) / 2, 50, 44);
            evidence.Size(16, layout.ContentTop, w - 32, layout.ContentHeight, y);
            evidence.Scroll.vertical = y > layout.ContentHeight;
        }

        private void HideEvidence() => HideEvidence(true);
        private void HideEvidence(bool restoreFocus)
        {
            if (!evidencePanel.gameObject.activeSelf) return;
            evidencePanel.gameObject.SetActive(false);
            if (restoreFocus && evidenceOwner != null && evidenceOwner.IsActive()) GameManager.EventSystem?.SetSelectedGameObject(evidenceOwner.gameObject);
            evidenceOwner = null;
        }
        // The native tooltip uses rich TMP text. Prevent captured names from introducing markup.
        private static string SafeTooltip(string text) => "<noparse>" + text.Replace("<", "＜").Replace(">", "＞") + "</noparse>";

        private TextMeshProUGUI Text(RectTransform parent, string name, float size)
        {
            var label = Node(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = typography.Font; label.fontSharedMaterial = material; label.fontSize = size;
            label.fontStyle = FontStyles.Normal; label.fontWeight = FontWeight.Regular;
            label.enableWordWrapping = true; label.enableAutoSizing = false;
            label.richText = false; label.color = Color.white; label.raycastTarget = false;
            label.overflowMode = TextOverflowModes.Overflow; label.alignment = TextAlignmentOptions.TopLeft;
            return label;
        }
        private static float Put(TextMeshProUGUI text, float x, float y, float width)
        {
            var height = string.IsNullOrEmpty(text.text) ? 0 : text.GetPreferredValues(text.text, Math.Max(1, width), float.PositiveInfinity).y;
            Place(text.rectTransform, x, y, Math.Max(1, width), height); return height;
        }
        private static RectTransform Panel(RectTransform parent, string name, float radius)
        {
            var rect = CreateOverviewPanel(parent, name, out var modifier); modifier.Radius = radius; return rect;
        }
        private static RectTransform Node(RectTransform parent, string name)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); return rect;
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        { rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            rowPool.Dispose();
            evidencePanel.gameObject.SetActive(false); evidenceOwner = null;
            evidenceClose.onClick.RemoveAllListeners();
            foreach (var slot in slots) slot.Button.onClick.RemoveAllListeners();
            detailBadge?.Dispose(); routeBadge?.Dispose();
            history.Dispose(); equipmentCombat.Dispose(); route.Dispose(); outer.Dispose(); evidence.Dispose();
        }
    }

    private static void AddButtonFeedback(Button button)
    {
        var buttonShape = button.targetGraphic?.GetComponent<UniformModifier>() ?? button.GetComponent<UniformModifier>();
        var overlay = new GameObject("NativeInteractionHighlight", typeof(RectTransform));
        var rect = (RectTransform)overlay.transform; rect.SetParent(button.transform, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var graphic = overlay.AddComponent<ProceduralImage>(); graphic.raycastTarget = false;
        var highlightShape = overlay.AddComponent<UniformModifier>();
        highlightShape.Radius = buttonShape != null ? buttonShape.Radius : 10;
        button.targetGraphic = graphic;
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor = Color.clear; colors.highlightedColor = new Color(1, 1, 1, .10f);
        colors.pressedColor = new Color(1, 1, 1, .20f); colors.selectedColor = new Color(1, 1, 1, .13f);
        colors.disabledColor = Color.clear; colors.fadeDuration = .1f; button.colors = colors;
        button.gameObject.AddComponent<RunsButtonFeedback>().MatchShape(buttonShape, highlightShape);
    }
}
