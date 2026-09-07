using Duckov.UI.Animations;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    private ItemUseView? itemUseView;

    private sealed class ItemUseView : IDisposable
    {
        private readonly RectTransform root, emptyPanel;
        private readonly ScrollRegion outer;
        private readonly ItemUseViewport left, right;
        private readonly TextMeshProUGUI unavailable, empty;
        private readonly NativeHeaderTitleTypography typography;
        private readonly Material material;
        private readonly CombatNativeTextMeasurement measure;
        private readonly NativeItemIconResolver icons = new();
        private readonly ItemUseSelection selection = new();
        private readonly Action<string, string> route;
        private readonly Action focusTabs;
        private ItemUseViewport? restoreFocus;
        private string? restoreFocusId;
        private float width, height, pixels;
        private bool dirty = true, disposed;

        public ItemUseView(RectTransform parent, NativeHeaderTitleTypography typography, Material material,
            Action<string, string> route, Action focusTabs)
        {
            this.typography = typography; this.material = material; this.route = route; this.focusTabs = focusTabs;
            root = Node(parent, "ItemUseContentView");
            outer = new ScrollRegion(root, "ItemUseOuter", radius: 20); RoundedMask(outer);
            left = new ItemUseViewport(this, outer.Content, "left"); right = new ItemUseViewport(this, outer.Content, "right");
            emptyPanel = CreateOverviewPanel(root, "NoItemUses", out var modifier); modifier.Radius = 20;
            empty = Text(emptyPanel, "Empty", 36); empty.text = UiText.Get("ui.item_use_empty"); empty.alignment = TextAlignmentOptions.Center;
            unavailable = Text(root, "Unavailable", 30); unavailable.text = UiText.Get("ui.profile_unavailable");
            measure = new CombatNativeTextMeasurement(Text(root, "Measurement", 28));
            outer.Rect.GetComponent<Selectable>().navigation = new Navigation { mode = Navigation.Mode.None };
            outer.Rect.GetComponent<RunsFocusHandler>().Move = direction =>
            { if (direction == MoveDirection.Left || direction == MoveDirection.Up) focusTabs(); else FocusPage(); };
        }
        public void Refresh(ItemUsePresentation? next)
        {
            if (disposed) return; Capture();
            var focused = GameManager.EventSystem?.currentSelectedGameObject;
            restoreFocus = next != null && selection.Snapshot?.GenerationId == next.GenerationId && focused != null
                ? focused.transform.IsChildOf(left.Panel) ? left : focused.transform.IsChildOf(right.Panel) ? right : null : null;
            restoreFocusId = focused == null ? null : restoreFocus?.FocusedRowId(focused);
            selection.Refresh(next); left.Clear(); right.Clear();
            outer.Rect.gameObject.SetActive(next != null && !next.Empty);
            emptyPanel.gameObject.SetActive(next?.Empty == true); unavailable.gameObject.SetActive(next == null);
            if (focused != null && focused.transform.IsChildOf(root) && !focused.activeInHierarchy) focusTabs();
            dirty = true;
        }
        private void Capture()
        {
            if (dirty || selection.Snapshot == null) return;
            selection.Capture("outer", outer.Offset); left.Capture(); right.Capture();
        }
        public void SetVisible(bool visible) { if (disposed) return; if (!visible) Capture(); root.gameObject.SetActive(visible); }
        public void FocusPage()
        { if (selection.Snapshot?.Empty != false) focusTabs(); else left.Focus(selection.FocusId("left")); }
        private void Activate(string region, string generation, string id)
        {
            if (disposed || selection.Snapshot?.GenerationId != generation) return;
            Capture();
            if (id.StartsWith("route:", StringComparison.Ordinal))
            { var runId = id.Substring(6); if (selection.Snapshot.CanRoute(generation, runId)) route(generation, runId); return; }
            if (id.StartsWith("filter:", StringComparison.Ordinal))
            {
                var value = id.Substring(7);
                if (value == "all") selection.SelectFilter(generation, null);
                else if (int.TryParse(value, out var group)) selection.SelectFilter(generation, (UltimateDuckovStatistics.Core.Domain.CanonicalItemGroup)group);
                else return;
            }
            else if (!selection.Toggle(generation, id)) return;
            restoreFocus = region == "left" ? left : right; restoreFocusId = id;
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
            dirty = false;
            Place(unavailable.rectTransform, 30, 30, width - 60, measure.Height(unavailable.text, width - 60, 30));
            Place(emptyPanel, 0, 0, width, height); Place(empty.rectTransform, 40, 30, width - 80, height - 60);
            var snapshot = selection.Snapshot; if (snapshot == null || snapshot.Empty) return;
            var stacked = CombatLayoutPolicy.Stack(pixels); var columnWidth = ItemUseLayoutPolicy.ColumnWidth(width, stacked);
            var leftDocument = new ItemUseDocument(measure.HeightWithSectionInk, measure.Width); leftDocument.Left(selection, columnWidth);
            var rightDocument = new ItemUseDocument(measure.HeightWithSectionInk, measure.Width); rightDocument.Right(selection, columnWidth);
            var lh = ItemUseLayoutPolicy.BoundedHeight(stacked, height, leftDocument.Height);
            var rh = ItemUseLayoutPolicy.BoundedHeight(stacked, height, rightDocument.Height);
            left.Bind(leftDocument, 0, 0, columnWidth, lh);
            var ry = stacked ? lh + ItemUseLayoutPolicy.Gap : 0;
            right.Bind(rightDocument, stacked ? 0 : columnWidth + ItemUseLayoutPolicy.Gap, ry, columnWidth, rh);
            outer.Size(0, 0, width, height, Math.Max(lh, ry + rh)); outer.Scroll.vertical = stacked;
            outer.SetOffset(stacked ? selection.Offset("outer", height, outer.Content.rect.height) : 0);
            left.Render(); right.Render();
            if (restoreFocus != null)
            { var target = restoreFocus; restoreFocus = null; if (restoreFocusId == null) target.FocusViewport(); else target.Focus(restoreFocusId); restoreFocusId = null; }
        }
        public void Tick()
        {
            if (disposed || !root.gameObject.activeInHierarchy || selection.Snapshot?.Empty != false) return;
            left.Render(); right.Render(); outer.Cues(); if (!dirty) Capture();
        }

        private sealed class ItemUseViewport : IDisposable
        {
            private readonly ItemUseView owner;
            private readonly string region;
            public RectTransform Panel { get; }
            private readonly ScrollRegion scroll;
            private readonly RectTransform surfaceRoot;
            private readonly List<RectTransform> surfaces = new();
            private readonly CombatControlPool<Control> controls;
            private ItemUseDocument? document;
            private float lastOffset = -1;
            private bool rebuild = true;
            private sealed class Control : IDisposable
            {
                public RectTransform Rect = null!;
                public ProceduralImage Background = null!;
                public RunsHistoryButton Button = null!;
                public RunsFocusHandler Focus = null!;
                public TextMeshProUGUI Name = null!, Value = null!, Caption = null!, Chevron = null!, Fallback = null!;
                public Image Icon = null!;
                public RetainedLatestRunViewRunControl Route = null!;
                public RetainedRunBadgeControl? Badge;
                public ItemUseRenderRow? Row;
                public void Dispose()
                {
                    Button.Binding.CancelPointer(); Button.onClick.RemoveAllListeners(); Focus.Move = null; Focus.Selected = null;
                    Row = null; Icon.sprite = null; NativeTotemIconAppearance.Clear(Icon); Badge?.Dispose();
                }
            }
            public ItemUseViewport(ItemUseView owner, RectTransform parent, string region)
            {
                this.owner = owner; this.region = region;
                Panel = Node(parent, "ItemUse" + region);
                scroll = new ScrollRegion(Panel, region + "Scroll", radius: 20); RoundedMask(scroll);
                surfaceRoot = Node(scroll.Content, "ItemUseCardSurfaces"); surfaceRoot.SetAsFirstSibling();
                scroll.Rect.GetComponent<Selectable>().navigation = new Navigation { mode = Navigation.Mode.None };
                scroll.Rect.GetComponent<RunsFocusHandler>().Move = direction => Move(null, direction);
                controls = new CombatControlPool<Control>(Create);
            }
            public void Capture() => owner.selection.Capture(region, scroll.Offset);
            public string? FocusedRowId(GameObject focused) => controls.Items.FirstOrDefault(control => control.Rect.gameObject == focused)?.Row?.Id;
            public void FocusViewport() => GameManager.EventSystem?.SetSelectedGameObject(scroll.Rect.gameObject);
            public void Clear()
            {
                document = null;
                foreach (var control in controls.Items)
                { control.Button.Binding.CancelPointer(); control.Rect.gameObject.SetActive(false); control.Row = null;
                    control.Icon.sprite = null; NativeTotemIconAppearance.Clear(control.Icon); }
                foreach (var surface in surfaces) surface.gameObject.SetActive(false);
                rebuild = true;
            }
            public void Bind(ItemUseDocument next, float x, float y, float width, float height)
            {
                document = next; Place(Panel, x, y, width, height);
                scroll.Size(0, 0, width, height, next.Height); scroll.SetOffset(owner.selection.Offset(region, height, next.Height)); rebuild = true;
            }
            private Control Create()
            {
                var shared = CreateOverviewLatestRunViewRun(scroll.Content, new RetainedLatestRunViewRunPresentation
                    { IsVisible = true, Label = UiText.Get(RetainedOverviewLatestRunViewRunPolicy.TextKey) }, owner.typography, owner.material, useIdentityBinding: true);
                var c = new Control { Rect = shared.Rect, Route = shared };
                c.Rect.gameObject.name = "ItemUseRow"; c.Background = c.Rect.GetComponent<ProceduralImage>();
                c.Button = (RunsHistoryButton)shared.Button; c.Button.Configure(c.Background); AddButtonFeedback(c.Button);
                c.Name = owner.Text(c.Rect, "Name", 32); c.Value = owner.Text(c.Rect, "Value", 32); c.Caption = owner.Text(c.Rect, "Caption", 20);
                c.Chevron = owner.Text(c.Rect, "Chevron", 28); c.Chevron.text = "›"; c.Chevron.alignment = TextAlignmentOptions.Center;
                c.Fallback = owner.Text(c.Rect, "MissingItem", 32); c.Fallback.alignment = TextAlignmentOptions.Center;
                c.Icon = Node(c.Rect, "ItemIcon").gameObject.AddComponent<Image>(); c.Icon.raycastTarget = false; c.Icon.preserveAspect = true;
                c.Focus = c.Rect.gameObject.AddComponent<RunsFocusHandler>(); c.Focus.Move = direction => Move(c.Row?.Id, direction);
                c.Focus.Selected = () => { if (c.Row?.Actionable == true) owner.selection.Focus(region, c.Row.Id); };
                c.Button.onClick.AddListener(() => { if (c.Row?.Actionable == true) owner.Activate(region, c.Button.Binding.Generation, c.Button.Binding.Id); });
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
                    var surface = CreateOverviewPanel(surfaceRoot, "ItemUseCard", out var modifier);
                    modifier.Radius = 10; surface.GetComponent<ProceduralImage>().raycastTarget = false; surfaces.Add(surface);
                }
                for (var i = 0; i < surfaces.Count; i++)
                {
                    surfaces[i].gameObject.SetActive(i < visibleSurfaces.Length); if (i >= visibleSurfaces.Length) continue;
                    var box = visibleSurfaces[i]; Place(surfaces[i], box.X, box.Y, box.Width, box.Height);
                    surfaces[i].GetComponent<UniformModifier>().Radius = box.Radius; surfaces[i].SetAsLastSibling();
                }
                controls.Ensure(visible.Count); var pool = controls.Items;
                var focusedObject = GameManager.EventSystem?.currentSelectedGameObject;
                var focused = pool.FirstOrDefault(c => c.Rect.gameObject == focusedObject);
                var focusedSlot = focused?.Row == null ? -1 : visible.ToList().FindIndex(i => document.Rows[i].Actionable && document.Rows[i].Id == focused.Row.Id);
                if (focused != null && focusedSlot < 0) FocusViewport();
                if (focused != null && focusedSlot >= 0)
                { var previous = pool.IndexOf(focused); (pool[previous], pool[focusedSlot]) = (pool[focusedSlot], pool[previous]); }
                for (var i = 0; i < pool.Count; i++)
                {
                    var c = pool[i]; c.Rect.gameObject.SetActive(i < visible.Count);
                    if (i >= visible.Count) { c.Button.Binding.CancelPointer(); c.Row = null; c.Icon.sprite = null; NativeTotemIconAppearance.Clear(c.Icon); continue; }
                    BindControl(c, document.Rows[visible[i]]); c.Rect.SetAsLastSibling();
                }
                scroll.Cues();
            }
            private void BindControl(Control c, ItemUseRenderRow row)
            {
                c.Row = row; c.Button.Binding.Bind(owner.selection.Snapshot!.GenerationId, row.Id);
                c.Button.BindInteractionOverlay(c.Background, row.Actionable);
                c.Rect.GetComponent<ButtonAnimation>().enabled = row.Actionable;
                c.Rect.GetComponent<RunsButtonFeedback>().enabled = row.Actionable;
                var plain = row.Kind is ItemUseRowKind.Heading or ItemUseRowKind.Notice or ItemUseRowKind.Group;
                c.Background.color = row.Selected ? new Color32(255, 158, 44, 255) : plain ? Color.clear : new Color(0, 0, 0, .5f);
                c.Rect.GetComponent<UniformModifier>().Radius = 10; c.Button.targetGraphic.GetComponent<UniformModifier>().Radius = 10;
                Place(c.Rect, row.X, row.Y, row.Width, row.Height);
                c.Route.Label.gameObject.SetActive(row.Kind == ItemUseRowKind.Route);
                c.Name.gameObject.SetActive(row.Kind != ItemUseRowKind.Route);
                c.Value.gameObject.SetActive(row.Value.Length > 0); c.Caption.gameObject.SetActive(row.Caption.Length > 0);
                c.Icon.gameObject.SetActive(row.HasIcon); c.Fallback.gameObject.SetActive(row.HasIcon); c.Chevron.gameObject.SetActive(row.Expandable);
                ReplaceBadge(c, row.Outcome);
                if (row.Kind == ItemUseRowKind.Route)
                {
                    c.Background.color = new Color(RetainedOverviewLatestRunViewRunPolicy.BackgroundRed, RetainedOverviewLatestRunViewRunPolicy.BackgroundGreen,
                        RetainedOverviewLatestRunViewRunPolicy.BackgroundBlue, RetainedOverviewLatestRunViewRunPolicy.BackgroundAlpha);
                    c.Route.Modifier.Radius = RetainedOverviewLatestRunViewRunPolicy.CornerRadiusPixels;
                    c.Button.targetGraphic.GetComponent<UniformModifier>().Radius = c.Route.Modifier.Radius;
                    c.Route.Label.text = row.Name; c.Route.Label.fontSize = RetainedOverviewLatestRunViewRunPolicy.ReferenceFontSize;
                    c.Route.Label.enableWordWrapping = true;
                    var padding = RetainedOverviewLatestRunViewRunPolicy.HorizontalLabelPaddingPixels;
                    Place(c.Route.LabelRect, padding, 0, row.Width - 2 * padding, row.Height);
                }
                else
                {
                    c.Name.text = row.Name; c.Name.fontSize = row.Size;
                    c.Name.color = row.Kind is ItemUseRowKind.Notice or ItemUseRowKind.Statistic ? Muted : Color.white;
                    c.Name.alignment = row.Kind is ItemUseRowKind.Statistic or ItemUseRowKind.Filter ? TextAlignmentOptions.Top : TextAlignmentOptions.TopLeft;
                    Place(c.Name.rectTransform, row.NameLeft, row.NameTop, row.NameWidth, row.NameHeight);
                    if (row.Kind == ItemUseRowKind.Heading) CombatNativeTextMeasurement.AlignInkTop(c.Name);
                    c.Value.text = row.Value; c.Value.color = Color.white;
                    c.Value.alignment = row.Kind == ItemUseRowKind.Statistic ? TextAlignmentOptions.Top : TextAlignmentOptions.TopRight;
                    Place(c.Value.rectTransform, row.ValueLeft, row.ValueTop, row.ValueWidth, row.ValueHeight);
                    c.Caption.text = row.Caption; c.Caption.color = row.Selected ? Color.white : Muted;
                    var captionLeft = row.Kind == ItemUseRowKind.Item ? row.NameLeft : 20;
                    Place(c.Caption.rectTransform, captionLeft, row.CaptionTop, row.Width - captionLeft - 20, row.CaptionHeight);
                    if (c.Badge != null) BadgeLayout(c.Badge, 50, row.BadgeTop, row.BadgeWidth);
                }
                if (row.HasIcon)
                {
                    var icon = CombatItemIconPolicy.Resolve(row.IconId, owner.icons.ResolveAvailable);
                    c.Icon.sprite = icon; c.Icon.enabled = icon != null; NativeTotemIconAppearance.Apply(c.Icon, row.IconId);
                    c.Fallback.enabled = icon == null; c.Fallback.text = row.IconFallback; c.Fallback.color = row.EmptyIcon ? Muted : Color.white;
                    var ix = row.Expandable ? 50 : 20; var iy = (Math.Max(100, row.Height) - 76) / 2;
                    Place(c.Icon.rectTransform, ix, iy, 70, 76); Place(c.Fallback.rectTransform, ix, iy, 70, 76);
                }
                else { c.Icon.sprite = null; NativeTotemIconAppearance.Clear(c.Icon); }
                if (row.Expandable)
                {
                    c.Chevron.rectTransform.pivot = new Vector2(.5f, .5f);
                    Place(c.Chevron.rectTransform, 30, row.NameTop + row.NameHeight / 2, 20, row.NameHeight);
                    c.Chevron.rectTransform.localRotation = Quaternion.Euler(0, 0, row.Selected ? -90 : 0);
                }
            }
            private void ReplaceBadge(Control c, RetainedRunBadgeState? state)
            {
                if (c.Badge?.Presentation.State == state && c.Badge != null) return;
                if (c.Badge != null)
                { c.Badge.Dispose(); c.Badge.Rect.gameObject.SetActive(false); UnityEngine.Object.Destroy(c.Badge.Rect.gameObject); c.Badge = null; }
                if (!state.HasValue) return;
                var specification = RetainedRunBadgePolicy.ResolveSpecification(state.Value);
                c.Badge = CreateOverviewLatestRunBadge(c.Rect, new RetainedRunBadgePresentation
                    { IsVisible = true, State = state, Specification = specification, Label = UiText.Get(specification.TextKey) }, owner.typography, owner.material);
            }
            private static void BadgeLayout(RetainedRunBadgeControl badge, float x, float y, float width)
            {
                var transform = RetainedReferenceTransformPolicy.Create(2560, 1440, 1);
                var layout = RetainedRunBadgePolicy.CreateCanvasLayout(transform, badge.Presentation.State!.Value,
                    Math.Max(1, width - RetainedRunBadgePolicy.FixedHorizontalContentPixels));
                badge.Label.fontSize = RetainedRunBadgePolicy.ReferenceFontSize; badge.Label.enableWordWrapping = true;
                layout.Height = layout.LabelHeight = Math.Max(layout.Height,
                    badge.Label.GetPreferredValues(badge.Label.text, layout.LabelWidth, float.PositiveInfinity).y + 6);
                layout.IconTop = (layout.Height - layout.IconHeight) / 2;
                Place(badge.Rect, x, y, layout.Width, layout.Height); badge.Modifier.Radius = layout.CornerRadius;
                Place(badge.IconRect, layout.IconLeft, layout.IconTop, layout.IconWidth, layout.IconHeight);
                Place(badge.LabelRect, layout.LabelLeft, layout.LabelTop, layout.LabelWidth, layout.LabelHeight);
                if (badge.IconText != null) badge.IconText.fontSize = layout.FontSize;
            }
            public void Focus(string? id = null)
            {
                if (document == null) return;
                var row = document.Rows.FirstOrDefault(candidate => candidate.Actionable && candidate.Id == id)
                    ?? document.Rows.FirstOrDefault(candidate => candidate.Actionable);
                if (row == null) { FocusViewport(); return; }
                scroll.SetOffset(RunsLayoutPolicy.Reveal(scroll.Offset, scroll.Rect.rect.height, document.Height, row.Y, Math.Min(row.Height, scroll.Rect.rect.height)));
                rebuild = true; Render();
                var control = controls.Items.FirstOrDefault(candidate => ReferenceEquals(candidate.Row, row));
                if (control != null) GameManager.EventSystem?.SetSelectedGameObject(control.Rect.gameObject);
            }
            private void Move(string? id, MoveDirection direction)
            {
                if (direction == MoveDirection.Left)
                { if (region == "right") owner.left.Focus(owner.selection.FocusId("left")); else owner.focusTabs(); return; }
                if (direction == MoveDirection.Right)
                { if (region == "left") owner.right.Focus(owner.selection.FocusId("right")); else FocusFirstFromViewport(id); return; }
                if (direction != MoveDirection.Up && direction != MoveDirection.Down) return;
                var rows = document?.Rows.Where(row => row.Actionable).ToArray() ?? Array.Empty<ItemUseRenderRow>();
                if (id == null || rows.Length == 0)
                {
                    if (direction == MoveDirection.Up && scroll.Offset <= .5f) owner.focusTabs();
                    else ((RunsScrollRect)scroll.Scroll).MoveBy((direction == MoveDirection.Up ? -1 : 1) * scroll.Scroll.scrollSensitivity);
                    return;
                }
                var index = Array.FindIndex(rows, row => row.Id == id); var next = index + (direction == MoveDirection.Up ? -1 : 1);
                if (next < 0) owner.focusTabs();
                else if (next < rows.Length) Focus(rows[next].Id);
                else { FocusViewport(); ((RunsScrollRect)scroll.Scroll).MoveBy(scroll.Scroll.scrollSensitivity); }
            }
            private void FocusFirstFromViewport(string? id) { if (id == null) Focus(owner.selection.FocusId(region)); }
            public void Dispose() { controls.Dispose(); surfaces.Clear(); document = null; scroll.Dispose(); }
        }
        private static Color Muted => new Color32(177, 177, 177, 255);
        private TextMeshProUGUI Text(RectTransform parent, string name, float size)
        {
            var label = Node(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = typography.Font; label.fontSharedMaterial = material; label.fontSize = size;
            label.fontStyle = FontStyles.Normal; label.fontWeight = FontWeight.Regular; label.color = Color.white;
            label.enableWordWrapping = true; label.enableAutoSizing = false; label.richText = false; label.raycastTarget = false;
            label.overflowMode = TextOverflowModes.Overflow; label.alignment = TextAlignmentOptions.TopLeft; return label;
        }
        private static void RoundedMask(ScrollRegion region)
        {
            var viewport = region.Scroll.viewport;
            var mask = viewport.gameObject.AddComponent<ProceduralImage>(); mask.color = Color.white; mask.raycastTarget = false;
            viewport.gameObject.AddComponent<UniformModifier>().Radius = 20; viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
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
            if (disposed) return; disposed = true; left.Dispose(); right.Dispose(); outer.Dispose(); selection.Refresh(null);
            root.gameObject.SetActive(false); UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
