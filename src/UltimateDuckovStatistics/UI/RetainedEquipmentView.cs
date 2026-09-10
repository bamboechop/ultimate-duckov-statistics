using Duckov.UI.Animations;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    private EquipmentView? equipmentView;
    private sealed class EquipmentView : IDisposable
    {
        private readonly RectTransform root;
        private readonly RectTransform pagePanel;
        private readonly ScrollRegion outer;
        private readonly EquipmentViewport selector, primary, secondary;
        private readonly TextMeshProUGUI unavailable;
        private readonly CombatNativeTextMeasurement measure;
        private readonly NativeHeaderTitleTypography typography;
        private readonly Material material;
        private readonly NativeItemIconResolver icons = new();
        private readonly EquipmentSelection selection = new();
        private readonly Action focusTabs;
        private readonly Action<string, string> route;
        private float width, height, pixels;
        private bool dirty = true, disposed;
        private EquipmentViewport? restoreFocus;
        private string? restoreFocusId;
        private static readonly string[] PageKeys = { "ui.loadouts", "ui.weapons", "ui.armor_and_gear", "ui.totems" };
        public EquipmentView(RectTransform parent, NativeHeaderTitleTypography typography, Material material, Action<string, string> route, Action focusTabs)
        {
            this.typography = typography; this.material = material; this.route = route; this.focusTabs = focusTabs;
            root = Node(parent, "EquipmentContentView");
            outer = new ScrollRegion(root, "EquipmentOuter", radius: 20); RoundedMask(outer);
            selector = new EquipmentViewport(this, outer.Content, "Selector", "selector");
            pagePanel = CreateOverviewPanel(outer.Content, "EquipmentPage", out var pageModifier); pageModifier.Radius = 20;
            primary = new EquipmentViewport(this, pagePanel, "Left", "primary");
            secondary = new EquipmentViewport(this, pagePanel, "Right", "secondary");
            primary.Panel.GetComponent<ProceduralImage>().color = Color.clear;
            secondary.Panel.GetComponent<ProceduralImage>().color = Color.clear;
            unavailable = Text(root, "Unavailable", 30); unavailable.text = UiText.Get("ui.profile_unavailable");
            measure = new CombatNativeTextMeasurement(Text(root, "Measurement", 28));
            outer.Rect.GetComponent<Selectable>().navigation = new Navigation { mode = Navigation.Mode.None };
            outer.Rect.GetComponent<RunsFocusHandler>().Move = d => { if (d == MoveDirection.Up || d == MoveDirection.Left) focusTabs(); else FocusSelector(); };
        }
        private float Measure(string value, float w, float size) => size is 40 or 30
            ? measure.SectionHeight(value, Math.Max(1, w), size) : measure.Height(value, Math.Max(1, w), size);
        public void Refresh(EquipmentPresentation? next)
        {
            if (disposed) return; Capture();
            var focused = GameManager.EventSystem?.currentSelectedGameObject;
            restoreFocus = next != null && selection.Snapshot?.GenerationId == next.GenerationId && focused != null
                ? focused.transform.IsChildOf(primary.Panel) ? primary : focused.transform.IsChildOf(secondary.Panel) ? secondary
                    : focused.transform.IsChildOf(selector.Panel) ? selector : null : null;
            restoreFocusId = focused == null ? null : restoreFocus?.FocusedRowId(focused);
            if (RetainedRefreshPolicy.RequiresInvalidation(selection.Snapshot?.GenerationId, next?.GenerationId))
            { selector.Clear(); primary.Clear(); secondary.Clear(); }
            selection.Refresh(next);
            outer.Rect.gameObject.SetActive(next != null); unavailable.gameObject.SetActive(next == null);
            if (focused != null && focused.transform.IsChildOf(root) && !focused.activeInHierarchy) focusTabs();
            dirty = true;
        }
        private void Capture()
        {
            if (dirty || selection.Snapshot == null) return;
            selection.Capture("outer", outer.Offset); selector.Capture(); primary.Capture();
            if (secondary.Panel.gameObject.activeSelf) secondary.Capture();
        }
        public void SetVisible(bool visible) { if (disposed) return; if (!visible) Capture(); root.gameObject.SetActive(visible); }
        public void FocusSelector() => selector.Focus(((int)selection.Page).ToString(System.Globalization.CultureInfo.InvariantCulture));
        private void Activate(string region, string generation, string id)
        {
            if (disposed || selection.Snapshot?.GenerationId != generation) return; Capture();
            if (region == "selector")
            {
                if (!int.TryParse(id, out var index) || !selection.SelectPage((EquipmentPanelSection)index)) return;
                primary.Clear(); secondary.Clear();
            }
            else if (id.StartsWith("route:", StringComparison.Ordinal))
            { var runId = id.Substring(6); if (selection.Snapshot.CanRoute(generation, runId)) route(generation, runId); return; }
            else if (id.StartsWith("inspect:", StringComparison.Ordinal)) { if (!selection.Inspect(generation, id)) return; }
            else if (!selection.Toggle(generation, id)) return;
            dirty = true;
        }
        public void Layout(RetainedVisualCanvasLayout shell, float viewportPixels, float canvasHeight)
        {
            if (disposed) return;
            var frame = CombatLayoutPolicy.Frame(shell, canvasHeight);
            if (frame.Width != width || frame.Height != height || pixels != viewportPixels)
            { Capture(); width = frame.Width; height = frame.Height; pixels = viewportPixels; dirty = true; }
            root.localScale = new Vector3(frame.Scale, frame.Scale, 1); Place(root, shell.Header.Left, frame.Top, width, height);
            if (!dirty || !root.gameObject.activeInHierarchy) return;
            dirty = false; Place(unavailable.rectTransform, 30, 30, width - 60, Measure(unavailable.text, width - 60, 30));
            if (selection.Snapshot == null) return;
            var stacked = CombatLayoutPolicy.Stack(pixels); var widths = CombatLayoutPolicy.Widths(width, stacked);
            var nav = new EquipmentDocument(Measure, measureWidth: measure.Width); float ny = 30;
            for (var i = 0; i < PageKeys.Length; i++) ny += nav.Add(new EquipmentRenderRow
            {
                Id = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Kind = EquipmentRowKind.Selector,
                Name = UiText.Get(PageKeys[i]),
                Actionable = true,
                Selected = i == (int)selection.Page
            }, 30, ny, widths.Selector - 60);
            nav.Seal(); var nh = stacked ? Math.Min(nav.Height, Math.Max(240, height * .6f)) : height;
            selector.Bind(nav, 0, 0, widths.Selector, nh);
            var two = EquipmentLayoutPolicy.TwoColumns(selection.Page);
            var columnWidth = two && !stacked ? (widths.Page - 10) / 2 : widths.Page;
            var left = new EquipmentDocument(Measure, measureWidth: measure.Width); left.Page(selection, false, columnWidth, stacked);
            var right = new EquipmentDocument(Measure, measureWidth: measure.Width); if (two) right.Page(selection, true, columnWidth, stacked);
            var x = stacked ? 0 : widths.Selector + 40; var y = stacked ? nh + 40 : 0;
            var lh = EquipmentLayoutPolicy.BoundedHeight(stacked, height, left.Height);
            primary.Bind(left, 0, 0, columnWidth, lh); var bottom = lh;
            secondary.Panel.gameObject.SetActive(two);
            if (two)
            {
                var ry = stacked ? bottom + 10 : 0; var rh = EquipmentLayoutPolicy.BoundedHeight(stacked, height, right.Height);
                secondary.Bind(right, stacked ? 0 : columnWidth + 10, ry, columnWidth, rh); bottom = Math.Max(bottom, ry + rh);
            }
            Place(pagePanel, x, y, widths.Page, bottom);
            outer.Size(0, 0, width, height, Math.Max(nh, y + bottom)); outer.Scroll.vertical = stacked;
            outer.SetOffset(selection.Offset("outer", height, outer.Content.rect.height));
            selector.Render(); primary.Render(); if (two) secondary.Render();
            if (restoreFocus != null)
            { var target = restoreFocus; restoreFocus = null; if (restoreFocusId == null) target.FocusViewport(); else target.Focus(restoreFocusId); restoreFocusId = null; }
        }
        public void Tick()
        {
            if (disposed || !root.gameObject.activeInHierarchy || selection.Snapshot == null) return;
            selector.Render(); primary.Render(); if (secondary.Panel.gameObject.activeSelf) secondary.Render(); outer.Cues(); Capture();
        }
        private sealed class EquipmentViewport : IDisposable
        {
            private readonly EquipmentView owner;
            private readonly string region;
            public RectTransform Panel { get; }
            private readonly ScrollRegion scroll;
            private readonly CombatControlPool<Control> controls;
            private readonly RectTransform surfaceRoot;
            private readonly List<RectTransform> surfaces = new();
            private List<Control> Pool => controls.Items;
            private EquipmentDocument? document;
            private float lastOffset = -1;
            private bool rebuild = true;
            private string? focusedId;
            private sealed class Control : IDisposable
            {
                public RectTransform Rect = null!;
                public ProceduralImage Background = null!;
                public RunsHistoryButton Button = null!;
                public RunsFocusHandler Focus = null!;
                public TextMeshProUGUI[] Text = null!;
                public TextMeshProUGUI Detail = null!, Fallback = null!, Chevron = null!;
                public Image Icon = null!;
                public EquipmentRenderRow? Row;
                public Duckov.UI.TooltipsProvider Tooltip = null!;
                public ProceduralImage Border = null!;
                public RetainedLatestRunViewRunControl Route = null!;
                public readonly List<ProceduralImage> Dots = new();
                public void Dispose()
                { Button.Binding.CancelPointer(); Button.onClick.RemoveAllListeners(); Focus.Move = null; Focus.Selected = null; Row = null; Icon.sprite = null; NativeItemIconAppearance.Clear(Icon); Tooltip.text = string.Empty; }
            }
            public EquipmentViewport(EquipmentView owner, RectTransform parent, string name, string region)
            {
                this.owner = owner; this.region = region;
                Panel = CreateOverviewPanel(parent, "Equipment" + name, out var modifier); modifier.Radius = 20;
                scroll = new ScrollRegion(Panel, name + "Scroll", radius: 20); RoundedMask(scroll);
                surfaceRoot = Node(scroll.Content, "EquipmentCardSurfaces");
                surfaceRoot.SetAsFirstSibling();
                scroll.Rect.GetComponent<Selectable>().navigation = new Navigation { mode = Navigation.Mode.None };
                scroll.Rect.GetComponent<RunsFocusHandler>().Move = d => Move(null, d);
                controls = new CombatControlPool<Control>(Create);
            }
            public void Capture() => owner.selection.Capture(region, scroll.Offset);
            public string? FocusedRowId(GameObject focused) => Pool.FirstOrDefault(c => c.Rect.gameObject == focused)?.Row?.Id;
            public void FocusViewport() => GameManager.EventSystem?.SetSelectedGameObject(scroll.Rect.gameObject);
            public void Clear()
            {
                document = null; focusedId = null;
                foreach (var c in Pool) { c.Button.Binding.CancelPointer(); c.Rect.gameObject.SetActive(false); c.Row = null; c.Icon.sprite = null; NativeItemIconAppearance.Clear(c.Icon); c.Tooltip.text = string.Empty; }
                foreach (var surface in surfaces) surface.gameObject.SetActive(false);
                rebuild = true;
            }
            public void Bind(EquipmentDocument next, float x, float y, float width, float height)
            {
                document = next; Place(Panel, x, y, width, height);
                scroll.Size(0, 0, width, height, next.Height);
                scroll.SetOffset(owner.selection.Offset(region, height, next.Height)); rebuild = true;
            }
            private Control Create()
            {
                var shared = CreateOverviewLatestRunViewRun(scroll.Content, new RetainedLatestRunViewRunPresentation
                { IsVisible = true, Label = UiText.Get(RetainedOverviewLatestRunViewRunPolicy.TextKey) }, owner.typography, owner.material, useIdentityBinding: true);
                var c = new Control { Rect = shared.Rect, Route = shared };
                c.Rect.gameObject.name = "EquipmentRow";
                c.Rect.GetComponent<UniformModifier>().Radius = 10;
                c.Background = c.Rect.GetComponent<ProceduralImage>();
                c.Button = (RunsHistoryButton)shared.Button; c.Button.Configure(c.Background);
                AddButtonFeedback(c.Button);
                c.Text = Enumerable.Range(0, 4).Select(i => owner.Text(c.Rect, "Cell" + i, 28)).ToArray();
                c.Detail = owner.Text(c.Rect, "Detail", 22);
                c.Chevron = owner.Text(c.Rect, "Chevron", 28); c.Chevron.text = "›"; c.Chevron.alignment = TextAlignmentOptions.Center;
                c.Fallback = owner.Text(c.Rect, "MissingItem", 32); c.Fallback.text = "?"; c.Fallback.alignment = TextAlignmentOptions.Center;
                c.Icon = Node(c.Rect, "ItemIcon").gameObject.AddComponent<Image>(); c.Icon.raycastTarget = false; c.Icon.preserveAspect = true;
                var border = Node(c.Rect, "SlotBorder"); Stretch(border);
                c.Border = border.gameObject.AddComponent<ProceduralImage>(); c.Border.raycastTarget = false;
                border.gameObject.AddComponent<UniformModifier>().Radius = RunsViewStyle.SlotRadius;
                c.Border.BorderWidth = RunsViewStyle.SlotBorder;
                c.Border.FalloffDistance = 1;
                c.Border.color = new Color32(146, 152, 164, 255);
                c.Tooltip = c.Rect.gameObject.AddComponent<Duckov.UI.TooltipsProvider>();
                c.Rect.gameObject.AddComponent<RunsTooltipFocus>();
                c.Focus = c.Rect.gameObject.AddComponent<RunsFocusHandler>();
                c.Focus.Move = d => Move(c.Row?.Id, d);
                c.Focus.Selected = () =>
                {
                    focusedId = c.Row?.Id;
                    if (focusedId != null) owner.selection.Focus(region, focusedId);
                };
                c.Button.onClick.AddListener(() =>
                {
                    if (c.Row?.Actionable == true) owner.Activate(region, c.Button.Binding.Generation, c.Button.Binding.Id);
                });
                return c;
            }
            public void Render()
            {
                if (document == null || !Panel.gameObject.activeInHierarchy || !rebuild && Math.Abs(lastOffset - scroll.Offset) < .1f) return;
                rebuild = false; lastOffset = scroll.Offset;
                var visible = document.Visible(scroll.Offset, scroll.Rect.rect.height);
                var visibleSurfaces = document.VisibleSurfaces(scroll.Offset, scroll.Rect.rect.height).ToArray();
                while (surfaces.Count < visibleSurfaces.Length)
                {
                    var surface = CreateOverviewPanel(surfaceRoot, "EquipmentCard", out var modifier);
                    modifier.Radius = 10; surface.GetComponent<ProceduralImage>().raycastTarget = false;
                    surfaces.Add(surface);
                }
                for (var i = 0; i < surfaces.Count; i++)
                {
                    surfaces[i].gameObject.SetActive(i < visibleSurfaces.Length);
                    if (i >= visibleSurfaces.Length) continue;
                    var box = visibleSurfaces[i]; Place(surfaces[i], box.X, box.Y, box.Width, box.Height);
                }
                controls.Ensure(visible.Count);
                var pool = Pool;
                var selected = GameManager.EventSystem?.currentSelectedGameObject;
                var focused = pool.FirstOrDefault(c => c.Rect.gameObject == selected);
                // Keep the focused identity on its original GameObject while it is visible.
                // Wheel scrolling it out of view moves focus to the viewport without revealing it back.
                var focusedSlot = focused?.Row == null ? -1 : visible.ToList().FindIndex(i => document.Rows[i].Actionable && document.Rows[i].Id == focused.Row.Id);
                if (focused != null && focusedSlot < 0) GameManager.EventSystem?.SetSelectedGameObject(scroll.Rect.gameObject);
                if (focused != null && focusedSlot >= 0)
                {
                    var previous = pool.IndexOf(focused);
                    (pool[previous], pool[focusedSlot]) = (pool[focusedSlot], pool[previous]);
                }
                for (var i = 0; i < pool.Count; i++)
                {
                    var c = pool[i]; c.Rect.gameObject.SetActive(i < visible.Count);
                    if (i >= visible.Count) { c.Button.Binding.CancelPointer(); c.Row = null; c.Icon.sprite = null; NativeItemIconAppearance.Clear(c.Icon); c.Tooltip.text = string.Empty; continue; }
                    BindControl(c, document.Rows[visible[i]]);
                    // Pools may swap focused controls. Restore document paint order above
                    // the separately pooled card surfaces.
                    c.Rect.SetAsLastSibling();
                }
                scroll.Cues();
            }
            private void BindControl(Control c, EquipmentRenderRow r)
            {
                var tooltipText = r.Kind == EquipmentRowKind.Slot
                    ? (r.Slot?.Text ?? UiText.Get("ui.unavailable")).Replace("<", "‹").Replace(">", "›") : string.Empty;
                if (!string.Equals(c.Tooltip.text, tooltipText, StringComparison.Ordinal))
                { c.Tooltip.OnPointerExit(null!); c.Tooltip.text = tooltipText; }
                c.Row = r; c.Button.Binding.Bind(owner.selection.Snapshot!.GenerationId, r.Id);
                c.Button.BindInteractionOverlay(c.Background, r.Actionable);
                c.Rect.GetComponent<ButtonAnimation>().enabled = r.Actionable;
                c.Rect.GetComponent<RunsButtonFeedback>().enabled = r.Actionable;
                c.Background.color = r.Selected ? new Color32(255, 158, 44, 255)
                    : r.Plain || r.Kind is EquipmentRowKind.Heading or EquipmentRowKind.Notice or EquipmentRowKind.Footer or EquipmentRowKind.SlotDuration ? Color.clear : new Color(0, 0, 0, .5f);
                Place(c.Rect, r.X, r.Y, r.Width, r.Height);
                c.Border.gameObject.SetActive(r.Kind == EquipmentRowKind.Slot);
                c.Rect.GetComponent<UniformModifier>().Radius = r.Kind == EquipmentRowKind.Slot ? RunsViewStyle.SlotRadius : 10;
                c.Button.targetGraphic.GetComponent<UniformModifier>().Radius = c.Rect.GetComponent<UniformModifier>().Radius;
                c.Route.Label.gameObject.SetActive(false);
                foreach (var label in c.Text) label.gameObject.SetActive(false);
                c.Detail.gameObject.SetActive(false); c.Chevron.gameObject.SetActive(false);
                var isSlot = r.Kind == EquipmentRowKind.Slot;
                var showIcon = r.HasIcon;
                c.Icon.gameObject.SetActive(showIcon); c.Fallback.gameObject.SetActive(showIcon);
                c.Tooltip.enabled = isSlot;
                c.Rect.GetComponent<RunsTooltipFocus>().enabled = isSlot && r.Actionable;
                foreach (var dot in c.Dots) dot.gameObject.SetActive(false);
                if (r.Kind == EquipmentRowKind.Route)
                {
                    c.Background.color = new Color(RetainedOverviewLatestRunViewRunPolicy.BackgroundRed, RetainedOverviewLatestRunViewRunPolicy.BackgroundGreen,
                        RetainedOverviewLatestRunViewRunPolicy.BackgroundBlue, RetainedOverviewLatestRunViewRunPolicy.BackgroundAlpha);
                    c.Route.Modifier.Radius = RetainedOverviewLatestRunViewRunPolicy.CornerRadiusPixels;
                    c.Button.targetGraphic.GetComponent<UniformModifier>().Radius = c.Route.Modifier.Radius;
                    c.Route.Label.gameObject.SetActive(true); c.Route.Label.text = r.Name;
                    c.Route.Label.fontSize = RetainedOverviewLatestRunViewRunPolicy.ReferenceFontSize;
                    c.Route.Label.enableWordWrapping = true;
                    var padding = RetainedOverviewLatestRunViewRunPolicy.HorizontalLabelPaddingPixels;
                    Place(c.Route.LabelRect, padding, 0, r.Width - 2 * padding, r.Height);
                    c.Icon.sprite = null;
                    NativeItemIconAppearance.Clear(c.Icon);
                    return;
                }
                if (showIcon)
                {
                    var icon = CombatItemIconPolicy.Resolve(r.IconId, owner.icons.ResolveAvailable);
                    c.Icon.sprite = icon; c.Icon.enabled = icon != null;
                    NativeItemIconAppearance.Apply(c.Icon, r.IconId);
                    c.Fallback.enabled = icon == null; c.Fallback.text = r.IconFallback;
                    c.Fallback.color = r.EmptyIcon ? Muted : Color.white;
                    var size = isSlot ? r.Width - 12 : r.Compact ? 48 : 60; var inset = isSlot ? 6 : r.Expandable ? 40 : 15;
                    Place(c.Icon.rectTransform, inset, isSlot ? 6 : r.TextTop, size, size); Place(c.Fallback.rectTransform, inset, isSlot ? 6 : r.TextTop, size, size);
                }
                else { c.Icon.sprite = null; NativeItemIconAppearance.Clear(c.Icon); }
                if (isSlot)
                {
                    c.Background.raycastTarget = true;
                    var count = r.Slot?.Attachments.Count ?? 0;
                    while (c.Dots.Count < count)
                    {
                        var dot = Node(c.Rect, "AttachmentDot").gameObject.AddComponent<ProceduralImage>(); dot.raycastTarget = false;
                        dot.gameObject.AddComponent<UniformModifier>().Radius = 8; c.Dots.Add(dot);
                    }
                    for (var i = 0; i < count; i++)
                    {
                        var dot = c.Dots[i]; dot.gameObject.SetActive(true);
                        var occupied = r.Slot!.Attachments[i] == UltimateDuckovStatistics.Core.Domain.EquipmentSlotState.Occupied;
                        dot.color = occupied ? Color.white : Muted; dot.BorderWidth = occupied ? 0 : 1.2f;
                        var geometry = RunsViewStyle.AttachmentDot(r.Width, count, i); Place(dot.rectTransform, geometry.X, geometry.Y, geometry.Size, geometry.Size);
                    }
                    if (r.Slot?.NestedComplete == false)
                    { c.Detail.gameObject.SetActive(true); c.Detail.text = "…"; c.Detail.color = Muted; Place(c.Detail.rectTransform, r.Width - 24, 0, 24, 28); }
                    return;
                }
                var sizeText = r.NameSize;
                float x = r.TextLeft;
                var name = c.Text[0]; name.gameObject.SetActive(true); name.text = r.Name; name.fontSize = sizeText;
                name.color = r.Kind == EquipmentRowKind.Notice ? Muted : Color.white;
                Place(name.rectTransform, x, r.TextTop, r.NameWidth, r.NameHeight);
                if (r.Kind == EquipmentRowKind.Heading) CombatNativeTextMeasurement.AlignInkTop(name);
                if (r.Value.Length > 0)
                {
                    var value = c.Text[1]; value.gameObject.SetActive(true); value.text = r.Value; value.fontSize = r.Kind == EquipmentRowKind.Footer ? 22 : r.ValueSize;
                    value.color = Color.white; value.alignment = TextAlignmentOptions.TopRight;
                    Place(value.rectTransform, x + r.NameWidth + 10, r.TextTop, r.Width - x - r.NameWidth - 25, r.ValueHeight);
                }
                if (r.Caption.Length > 0)
                {
                    c.Detail.gameObject.SetActive(true); c.Detail.text = r.Caption; c.Detail.color = r.Selected ? Color.white : Muted;
                    Place(c.Detail.rectTransform, x, r.CaptionTop, r.Width - x - 15, owner.Measure(r.Caption, r.Width - x - 15, 22));
                }
                c.Chevron.gameObject.SetActive(r.Expandable);
                if (r.Expandable)
                {
                    c.Chevron.rectTransform.pivot = new Vector2(.5f, .5f);
                    Place(c.Chevron.rectTransform, 25, 12 + r.NameHeight / 2, 20, r.NameHeight);
                    c.Chevron.rectTransform.localRotation = Quaternion.Euler(0, 0, r.Selected ? -90 : 0);
                }
            }
            public void Focus(string? id = null)
            {
                if (document == null) return;
                var row = document.Rows.FirstOrDefault(r => r.Actionable && r.Id == id) ?? document.Rows.FirstOrDefault(r => r.Actionable);
                if (row == null) { FocusViewport(); return; }
                focusedId = row.Id;
                scroll.SetOffset(RunsLayoutPolicy.Reveal(scroll.Offset, scroll.Rect.rect.height, document.Height, row.Y, Math.Min(row.Height, scroll.Rect.rect.height)));
                rebuild = true; Render();
                var c = Pool.FirstOrDefault(c => ReferenceEquals(c.Row, row));
                if (c != null) GameManager.EventSystem?.SetSelectedGameObject(c.Rect.gameObject);
            }
            private void Move(string? id, MoveDirection direction)
            {
                if (direction == MoveDirection.Left)
                { if (region == "selector") owner.focusTabs(); else owner.FocusSelector(); return; }
                if (direction == MoveDirection.Right)
                { if (region == "selector") owner.primary.Focus(owner.selection.FocusId("primary")); else if (region == "primary" && owner.secondary.Panel.gameObject.activeSelf) owner.secondary.Focus(owner.selection.FocusId("secondary")); return; }
                if (direction != MoveDirection.Up && direction != MoveDirection.Down) return;
                var rows = document?.Rows.Where(r => r.Actionable).ToArray() ?? Array.Empty<EquipmentRenderRow>();
                if (rows.Length == 0 || id == null && region != "selector")
                {
                    if (direction == MoveDirection.Up && scroll.Offset <= .5f) owner.FocusSelector();
                    else ((RunsScrollRect)scroll.Scroll).MoveBy((direction == MoveDirection.Up ? -1 : 1) * scroll.Scroll.scrollSensitivity);
                    return;
                }
                var index = Array.FindIndex(rows, r => r.Id == id);
                var next = EquipmentLayoutPolicy.Move(index, rows.Length, direction == MoveDirection.Up ? -1 : 1);
                if (next < 0) { if (region == "selector") owner.focusTabs(); else owner.FocusSelector(); }
                else if (next < rows.Length) Focus(rows[next].Id);
                else if (region == "selector") owner.primary.Focus(owner.selection.FocusId("primary"));
                else ((RunsScrollRect)scroll.Scroll).MoveBy(scroll.Scroll.scrollSensitivity);
            }
            public void Dispose() { controls.Dispose(); surfaces.Clear(); document = null; scroll.Dispose(); }
        }
        private static Color Muted => new Color32(177, 177, 177, 255);
        private TextMeshProUGUI Text(RectTransform parent, string name, float size)
        {
            var label = Node(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = typography.Font; label.fontSharedMaterial = material; label.fontSize = size;
            label.fontStyle = FontStyles.Normal; label.fontWeight = FontWeight.Regular; label.color = Color.white;
            label.enableWordWrapping = true; label.enableAutoSizing = false; label.richText = false;
            label.raycastTarget = false; label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.TopLeft; return label;
        }
        private static void RoundedMask(ScrollRegion region)
        {
            var viewport = region.Scroll.viewport;
            var mask = viewport.gameObject.AddComponent<ProceduralImage>(); mask.color = Color.white; mask.raycastTarget = false;
            viewport.gameObject.AddComponent<UniformModifier>().Radius = 20;
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        }
        private static RectTransform Node(RectTransform parent, string name)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); return rect;
        }
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        { rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(Math.Max(1, width), Math.Max(1, height)); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            selector.Dispose(); primary.Dispose(); secondary.Dispose(); outer.Dispose(); selection.Refresh(null);
            root.gameObject.SetActive(false); UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
