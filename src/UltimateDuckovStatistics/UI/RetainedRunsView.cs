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

internal sealed class RunsButtonFeedback : MonoBehaviour, ISelectHandler, ISubmitHandler, IPointerEnterHandler, IPointerExitHandler
{
    private bool pointerInside;
    public void OnPointerEnter(PointerEventData eventData) => pointerInside = true;
    public void OnPointerExit(PointerEventData eventData) => pointerInside = false;
    public void OnSelect(BaseEventData eventData)
    {
        if (!pointerInside) GetComponent<ButtonAnimation>()?.OnPointerEnter(new PointerEventData(GameManager.EventSystem));
    }
    public void OnSubmit(BaseEventData eventData)
    {
        var button = GetComponent<Button>();
        if (button == null || !button.IsInteractable()) return;
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
    }

    private sealed class RunsView : IDisposable
    {
        private sealed class ScrollRegion : IDisposable
        {
            public RectTransform Rect { get; }
            public RectTransform Content { get; }
            public ScrollRect Scroll { get; }
            private readonly Image top;
            private readonly Image bottom;
            public float Offset => Math.Max(0, Content.anchoredPosition.y);
            public ScrollRegion(RectTransform parent, string name)
            {
                Rect = Node(parent, name);
                var hit = Rect.gameObject.AddComponent<Image>();
                hit.color = Color.clear;
                var viewport = Node(Rect, "Viewport");
                Stretch(viewport);
                viewport.gameObject.AddComponent<RectMask2D>();
                Content = Node(viewport, "Content");
                Scroll = Rect.gameObject.AddComponent<RunsScrollRect>();
                Scroll.viewport = viewport; Scroll.content = Content;
                Scroll.horizontal = false; Scroll.vertical = true;
                Scroll.movementType = ScrollRect.MovementType.Clamped;
                Scroll.scrollSensitivity = 60;
                Scroll.onValueChanged.AddListener(_ => Cues());
                top = Edge(Rect, "Above"); bottom = Edge(Rect, "Below");
                var selectable = Rect.gameObject.AddComponent<Selectable>();
                selectable.targetGraphic = hit;
                selectable.navigation = new Navigation { mode = Navigation.Mode.Automatic };
                var focus = Rect.gameObject.AddComponent<RunsFocusHandler>();
                focus.Move = direction =>
                {
                    if (direction == MoveDirection.Up || direction == MoveDirection.Down)
                        ((RunsScrollRect)Scroll).MoveBy(direction == MoveDirection.Up ? -100 : 100);
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
                Place(top.rectTransform, 6, 0, Math.Max(1, width - 12), 1);
                Place(bottom.rectTransform, 6, Math.Max(0, height - 1), Math.Max(1, width - 12), 1);
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
            private static Image Edge(RectTransform parent, string name)
            {
                var image = Node(parent, name).gameObject.AddComponent<Image>();
                image.color = new Color(1, 1, 1, .3f); image.raycastTarget = false; return image;
            }
            public void Dispose() => Scroll.onValueChanged.RemoveAllListeners();
        }

        private sealed class HistoryControl : IDisposable
        {
            public RectTransform Rect = null!;
            public Button Button = null!;
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
        private readonly ScrollRegion detail;
        private readonly ScrollRegion route;
        private readonly TextMeshProUGUI measure;
        private readonly TextMeshProUGUI empty;
        private readonly TextMeshProUGUI title;
        private readonly TextMeshProUGUI metadata;
        private readonly TextMeshProUGUI integrity;
        private readonly TextMeshProUGUI routeHeading;
        private readonly TextMeshProUGUI routeText;
        private readonly TextMeshProUGUI equipmentHeading;
        private readonly TextMeshProUGUI equipmentState;
        private readonly RectTransform equipmentCard;
        private readonly TextMeshProUGUI combatHeading;
        private readonly RectTransform combatCard;
        private readonly TextMeshProUGUI ranged;
        private readonly TextMeshProUGUI melee;
        private readonly List<TextMeshProUGUI> summary = new();
        private readonly RunsControlPool<HistoryControl> rowPool;
        private IReadOnlyList<HistoryControl> Pool => rowPool.Items;
        private readonly List<(RectTransform Root, Image Icon, TextMeshProUGUI Fallback, TextMeshProUGUI Text)> slots = new();
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
        private (string Generation, float History, float Detail, float Route, float Page)? suspendedScroll;
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
            history = new ScrollRegion(historyPanel, "RunHistoryScroll");
            detail = new ScrollRegion(detailPanel, "RunDetailsScroll");
            measure = Text(root, "Measure", 28); measure.enabled = false;
            empty = Text(history.Content, "EmptyHistory", 28);
            title = Text(detail.Content, "RunTitle", 48);
            metadata = Text(detail.Content, "RunMetadata", 22);
            integrity = Text(detail.Content, "RunIntegrity", 22);
            for (var i = 0; i < 10; i++) summary.Add(Text(detail.Content, "Summary" + i, 24));
            routeHeading = Text(detail.Content, "RouteHeading", 38);
            var routeCard = Panel(detail.Content, "RouteCard", 10);
            route = new ScrollRegion(routeCard, "RouteScroll");
            routeText = Text(route.Content, "RecordedSegments", 26);
            equipmentHeading = Text(detail.Content, "EquipmentHeading", 38);
            equipmentCard = Panel(detail.Content, "TerminalEquipment", 10);
            equipmentState = Text(equipmentCard, "TerminalState", 22);
            combatHeading = Text(detail.Content, "CombatHeading", 38);
            combatCard = Panel(detail.Content, "CombatCard", 10);
            ranged = Text(combatCard, "Ranged", 24); melee = Text(combatCard, "Melee", 24);
        }

        public void Refresh(RunsPresentation? snapshot, string generation)
        {
            if (snapshot == null && selection.Snapshot != null)
                suspendedScroll = (selection.Snapshot.GenerationId, history.Offset, detail.Offset, route.Offset, outer.Offset);
            restoreSuspendedScroll = snapshot != null && suspendedScroll?.Generation == generation;
            if (snapshot != null && selection.Snapshot?.GenerationId != generation && !restoreSuspendedScroll)
            { history.SetOffset(0); detail.SetOffset(0); route.SetOffset(0); outer.SetOffset(0); suspendedScroll = null; }
            if (snapshot == null) selection.Invalidate(); else selection.Refresh(snapshot, generation);
            if (snapshot == null)
                foreach (var row in Pool) row.Rect.gameObject.SetActive(false);
            UpdateDetails(); dirty = true;
        }
        public void Route(string generation, string id)
        {
            selection.Route(generation, id); UpdateDetails(); dirty = true; revealSelected = true; focusSelectedAfterLayout = true;
            detail.SetOffset(0); route.SetOffset(0);
        }
        public void SetVisible(bool visible) { root.gameObject.SetActive(visible); if (visible) dirty = true; }
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
            var run = selection.Selected;
            title.text = run?.Title ?? UiText.Get(selection.Snapshot == null ? "ui.profile_unavailable"
                : selection.RequestedRunUnavailable ? "ui.runs_requested_unavailable" : "ui.runs_empty");
            foreach (var cell in summary) cell.gameObject.SetActive(run != null);
            routeHeading.gameObject.SetActive(run != null);
            route.Rect.parent.gameObject.SetActive(run != null);
            equipmentHeading.gameObject.SetActive(run != null); equipmentCard.gameObject.SetActive(run != null);
            combatHeading.gameObject.SetActive(run != null); combatCard.gameObject.SetActive(run != null);
            equipmentCard.GetComponent<ProceduralImage>().color = run?.TerminalState == TerminalLoadoutState.Complete
                ? new Color(0, 0, 0, .5f) : new Color(.15f, .15f, .15f, .5f);
            metadata.text = run?.Metadata ?? string.Empty; integrity.text = run?.Integrity ?? string.Empty;
            for (var i = 0; i < summary.Count; i++) summary[i].text = run == null ? string.Empty : run.Summary[i].Key + "\n" + run.Summary[i].Value;
            routeHeading.text = run == null ? string.Empty : UiText.Get("ui.runs_route") + " " + run.RouteSummary;
            routeText.text = run == null ? string.Empty : string.Join("\n\n", run.Segments.Select(segment => segment.Key + "\n" + segment.Value));
            equipmentHeading.text = run == null ? string.Empty : UiText.Get("ui.runs_equipment");
            equipmentState.text = run?.EquipmentState ?? string.Empty;
            combatHeading.text = run == null ? string.Empty : UiText.Get("ui.runs_combat");
            ranged.text = run == null ? string.Empty : UiText.Get("ui.runs_ranged") + "\n" + run.Ranged;
            melee.text = run == null ? string.Empty : UiText.Get("ui.runs_melee") + "\n" + run.Melee;
            ReplaceBadge(ref detailBadge, detail.Content, run?.Outcome);
            ReplaceBadge(ref routeBadge, route.Content, run?.Outcome);
            // Root slot evidence is bounded by the native capture contract. Reuse controls across selections.
            while (slots.Count < (run?.Slots.Count ?? 0))
            {
                var slot = Panel(equipmentCard, "TerminalSlot" + slots.Count, 10);
                var icon = Node(slot, "Icon").gameObject.AddComponent<Image>(); icon.raycastTarget = false; icon.preserveAspect = true;
                slots.Add((slot, icon, Text(slot, "Fallback", 40), Text(slot, "Identity", 20)));
            }
            for (var i = 0; i < slots.Count; i++)
            {
                var control = slots[i]; var item = run != null && i < run.Slots.Count ? run.Slots[i] : null;
                control.Root.gameObject.SetActive(item != null);
                if (item == null) continue;
                control.Text.text = item.Text;
                var sprite = RunsItemIconPolicy.Resolve(item, icons.Resolve);
                control.Icon.sprite = sprite; control.Icon.enabled = sprite != null;
                control.Fallback.text = sprite != null || item.State == EquipmentSlotState.Empty ? string.Empty : "?";
                control.Root.GetComponent<ProceduralImage>().color = item.State != EquipmentSlotState.Occupied && item.State != EquipmentSlotState.Empty
                    ? new Color(.28f, .28f, .28f, .5f) : new Color(0, 0, 0, .5f);
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

        private void Reflow()
        {
            dirty = false;
            var hw = RunsLayoutPolicy.HistoryWidth(width, stacked);
            var dw = stacked ? width : width - hw - 40;
            var hh = stacked ? Math.Min(400, height * .45f) : height;
            var dy = stacked ? hh + 40 : 0;
            Place(historyPanel, 0, 0, hw, hh);
            var detailWidth = dw - 60;
            var y = Put(title, 0, 0, detailWidth) + 10;
            if (detailBadge != null) { BadgeLayout(detailBadge, 0, y, detailWidth); y += detailBadge.Rect.rect.height + 10; }
            y += Put(metadata, 0, y, detailWidth) + 6;
            y += Put(integrity, 0, y, detailWidth) + 25;
            var columns = RunsLayoutPolicy.SummaryColumns(stacked);
            var cellWidth = (detailWidth - (columns - 1) * 20) / columns;
            for (var start = 0; start < summary.Count; start += columns)
            {
                var rowHeight = 0f;
                for (var i = start; i < Math.Min(start + columns, summary.Count); i++)
                    rowHeight = Math.Max(rowHeight, Put(summary[i], (i - start) * (cellWidth + 20), y, cellWidth));
                y += rowHeight + 25;
            }
            var lowerTop = y + 10;
            var lowerWidth = stacked ? detailWidth : (detailWidth - 30) / 2;
            var headingHeight = Put(routeHeading, 0, lowerTop, lowerWidth);
            var routeTop = lowerTop + headingHeight + 10;
            var routeHeight = stacked ? 440 : Math.Max(320, height - 60 - routeTop);
            var routeCard = (RectTransform)route.Rect.parent;
            Place(routeCard, 0, routeTop, lowerWidth, routeHeight);
            var routeTextHeight = Put(routeText, 20, 20, lowerWidth - 40);
            if (routeBadge != null) BadgeLayout(routeBadge, 20, routeTextHeight + 35, lowerWidth - 40);
            route.Size(0, 0, lowerWidth, routeHeight, routeTextHeight + 55 + (routeBadge?.Rect.rect.height ?? 0));
            var rx = stacked ? 0 : lowerWidth + 30;
            var ry = stacked ? routeTop + routeHeight + 30 : lowerTop;
            ry += Put(equipmentHeading, rx, ry, lowerWidth) + 10;
            var equipmentY = ry;
            var inside = Put(equipmentState, 20, 20, lowerWidth - 40) + 35;
            var slotColumns = Math.Max(1, Math.Min(5, (int)((lowerWidth - 40) / 128)));
            var slotWidth = (lowerWidth - 40 - (slotColumns - 1) * 10) / slotColumns;
            var activeSlots = selection.Selected?.Slots.Count ?? 0;
            for (var start = 0; start < activeSlots; start += slotColumns)
            {
                var rowHeight = 0f;
                for (var i = start; i < Math.Min(start + slotColumns, activeSlots); i++)
                {
                    var slot = slots[i];
                    Place(slot.Icon.rectTransform, (slotWidth - 80) / 2, 8, 80, 80);
                    Place(slot.Fallback.rectTransform, (slotWidth - 80) / 2, 8, 80, 80);
                    var textHeight = Put(slot.Text, 8, 96, slotWidth - 16);
                    var sh = 104 + textHeight; rowHeight = Math.Max(rowHeight, sh);
                    Place(slot.Root, 20 + (i - start) * (slotWidth + 10), inside, slotWidth, sh);
                }
                inside += rowHeight + 10;
            }
            Place(equipmentCard, rx, equipmentY, lowerWidth, inside + 10);
            ry += inside + 30;
            ry += Put(combatHeading, rx, ry, lowerWidth) + 10;
            var cw = (lowerWidth - 60) / 2;
            var combatHeight = Math.Max(Put(ranged, 20, 20, cw), Put(melee, 40 + cw, 20, cw)) + 40;
            Place(combatCard, rx, ry, lowerWidth, combatHeight);
            var contentHeight = selection.Selected == null ? title.rectTransform.rect.height + 30
                : Math.Max(routeTop + routeHeight, ry + combatHeight) + 30;
            var dh = stacked ? contentHeight + 60 : height;
            Place(detailPanel, stacked ? 0 : hw + 40, dy, dw, dh);
            detail.Size(30, 30, detailWidth, dh - 60, contentHeight);
            outer.Size(0, 0, width, height, stacked ? dy + dh : height);
            if (measuredHistory != selection.Snapshot || measuredHistoryWidth != hw - 60)
            {
                MeasureHistory(hw - 60);
                measuredHistory = selection.Snapshot; measuredHistoryWidth = hw - 60;
            }
            history.Size(30, 30, hw - 60, hh - 60, historyHeight);
            if (restoreSuspendedScroll && suspendedScroll.HasValue)
            {
                var offsets = suspendedScroll.Value;
                history.SetOffset(offsets.History); detail.SetOffset(offsets.Detail);
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
            row.Button = row.Rect.gameObject.AddComponent<Button>(); row.Button.targetGraphic = row.Background;
            row.Button.transition = Selectable.Transition.None;
            row.Button.navigation = new Navigation { mode = Navigation.Mode.None };
            row.Button.onClick.AddListener(() =>
            {
                if (!selection.Select(row.Id)) return;
                detail.SetOffset(0); route.SetOffset(0); UpdateDetails();
            });
            NativeButtonInteractionFeedbackPolicy.AttachIfMissing(row.Rect.gameObject,
                static target => target.GetComponent<ButtonAnimation>() != null,
                static target => _ = target.AddComponent<ButtonAnimation>());
            AddButtonFeedback(row.Button);
            var focus = row.Rect.gameObject.AddComponent<RunsFocusHandler>();
            focus.Selected = () => RevealRow(row.Index);
            focus.Move = direction =>
            {
                if (direction == MoveDirection.Right) { GameManager.EventSystem?.SetSelectedGameObject(detail.Rect.gameObject); return; }
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
            if (history.Offset != lastHistoryOffset) RenderHistory();
            history.Cues(); route.Cues(); detail.Cues(); outer.Cues();
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
            detailBadge?.Dispose(); routeBadge?.Dispose();
            history.Dispose(); detail.Dispose(); route.Dispose(); outer.Dispose();
        }
    }

    private static void AddButtonFeedback(Button button)
    {
        var overlay = new GameObject("NativeInteractionHighlight", typeof(RectTransform));
        var rect = (RectTransform)overlay.transform; rect.SetParent(button.transform, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var graphic = overlay.AddComponent<ProceduralImage>(); graphic.raycastTarget = false;
        overlay.AddComponent<UniformModifier>().Radius = 10;
        button.targetGraphic = graphic;
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor = Color.clear; colors.highlightedColor = new Color(1, 1, 1, .10f);
        colors.pressedColor = new Color(1, 1, 1, .20f); colors.selectedColor = new Color(1, 1, 1, .13f);
        colors.disabledColor = Color.clear; colors.fadeDuration = .1f; button.colors = colors;
        button.gameObject.AddComponent<RunsButtonFeedback>();
    }
}
