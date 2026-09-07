using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    private EconomyView? economyView;

    private sealed class EconomyView : IDisposable
    {
        private readonly RectTransform root;
        private readonly ScrollRegion outer;
        private readonly EconomyViewport primary, recent;
        private readonly NativeHeaderTitleTypography typography;
        private readonly Material material;
        private readonly CombatNativeTextMeasurement measure;
        private readonly NativeItemIconResolver icons = new();
        private readonly Sprite? moneyIcon;
        private readonly TextMeshProUGUI unavailable;
        private readonly EconomySelection selection = new();
        private readonly Action<string, string> route;
        private readonly Action focusTabs;
        private float width, height, pixels;
        private bool dirty = true, disposed;
        private (EconomyViewport View, string Id, EconomyElementKind Kind)? pendingFocus;

        public EconomyView(RectTransform parent, NativeHeaderTitleTypography typography, Material material,
            Action<string, string> route, Action focusTabs)
        {
            this.typography = typography; this.material = material; this.route = route; this.focusTabs = focusTabs;
            moneyIcon = ResolveMoneyIcon();
            root = Node(parent, "EconomyContentView");
            outer = new ScrollRegion(root, "EconomyOuter", radius: 20);
            primary = new EconomyViewport(this, outer.Content, "Primary");
            recent = new EconomyViewport(this, outer.Content, "Recent");
            measure = new CombatNativeTextMeasurement(Text(root, "Measurement", 28));
            unavailable = Text(root, "Unavailable", 30); unavailable.text = UiText.Get("ui.profile_unavailable");
            outer.Rect.GetComponent<Selectable>().navigation = new Navigation { mode = Navigation.Mode.None };
            outer.Rect.GetComponent<RunsFocusHandler>().Move = direction =>
            { if (direction == MoveDirection.Up || direction == MoveDirection.Left) focusTabs(); else FocusPage(); };
        }
        public void Refresh(EconomyPresentation? next)
        {
            if (disposed) return;
            Capture(); RememberFocus();
            if (next == null || next.GenerationId != selection.Snapshot?.GenerationId) pendingFocus = null;
            if (RetainedRefreshPolicy.RequiresInvalidation(selection.Snapshot?.GenerationId, next?.GenerationId))
                { primary.Clear(); recent.Clear(); }
            selection.Refresh(next);
            outer.Rect.gameObject.SetActive(next != null); unavailable.gameObject.SetActive(next == null);
            var focused = GameManager.EventSystem?.currentSelectedGameObject;
            if (focused != null && focused.transform.IsChildOf(root) && !focused.activeInHierarchy) focusTabs();
            dirty = true;
        }
        private void Capture()
        {
            if (dirty || selection.Snapshot == null) return;
            selection.Capture("outer", outer.Offset); primary.Capture(); recent.Capture();
        }
        private void RememberFocus()
        {
            var focused = GameManager.EventSystem?.currentSelectedGameObject;
            if (focused == null) return;
            pendingFocus = primary.Focused(focused) ?? recent.Focused(focused);
        }
        private void Activate(EconomyViewport viewport, EconomyElement element, string generation)
        {
            if (disposed || selection.Snapshot?.CanRoute(generation, element.Id) != true) return;
            if (element.Kind == EconomyElementKind.Route) { route(generation, element.Id); return; }
            Capture();
            if (!selection.Toggle(generation, element.Id)) return;
            pendingFocus = (viewport, element.Id, EconomyElementKind.RunToggle); dirty = true;
        }
        public void SetVisible(bool visible)
        { if (disposed) return; if (!visible) Capture(); root.gameObject.SetActive(visible); }
        public void FocusPage() => primary.FocusViewport();
        public void Layout(RetainedVisualCanvasLayout shell, float viewportPixels, float canvasHeight)
        {
            if (disposed) return;
            var frame = CombatLayoutPolicy.Frame(shell, canvasHeight);
            if (frame.Width != width || frame.Height != height || viewportPixels != pixels)
            { Capture(); RememberFocus(); width = frame.Width; height = frame.Height; pixels = viewportPixels; dirty = true; }
            root.localScale = new Vector3(frame.Scale, frame.Scale, 1); Place(root, shell.Header.Left, frame.Top, width, height);
            if (!dirty || !root.gameObject.activeInHierarchy) return;
            dirty = false;
            Place(unavailable.rectTransform, 30, 30, width - 60, measure.Height(unavailable.text, width - 60, 30));
            if (selection.Snapshot == null) return;
            var stacked = CombatLayoutPolicy.Stack(pixels);
            var columns = EconomyLayoutPolicy.Widths(width, stacked);
            var left = new EconomyDocument(measure.HeightWithSectionInk, measure.Width); left.Primary(selection.Snapshot, columns.Primary, stacked);
            var right = new EconomyDocument(measure.HeightWithSectionInk, measure.Width); right.Recent(selection, columns.Recent);
            var lh = EconomyLayoutPolicy.BoundedHeight(stacked, height, left.Height);
            var rh = EconomyLayoutPolicy.BoundedHeight(stacked, height, right.Height);
            primary.Bind(left, 0, 0, columns.Primary, lh);
            recent.Bind(right, stacked ? 0 : columns.Primary + 40, stacked ? lh + 40 : 0, columns.Recent, rh);
            var content = stacked ? lh + 40 + rh : Math.Max(lh, rh);
            outer.Size(0, 0, width, height, content); outer.Scroll.vertical = stacked;
            outer.SetOffset(selection.Offset("outer", height, content));
            if (pendingFocus.HasValue)
            {
                var focus = pendingFocus.Value; pendingFocus = null;
                focus.View.Focus(focus.Id, focus.Kind, fallbackToFirst: false);
            }
        }
        public void Tick()
        {
            if (disposed || !root.gameObject.activeInHierarchy || selection.Snapshot == null) return;
            outer.Cues(); primary.Cues(); recent.Cues(); Capture();
        }
        private void Reveal(EconomyViewport viewport)
        {
            if (!outer.Scroll.vertical) return;
            var top = -viewport.Panel.anchoredPosition.y;
            outer.SetOffset(RunsLayoutPolicy.Reveal(outer.Offset, outer.Rect.rect.height, outer.Content.rect.height,
                top, Math.Min(outer.Rect.rect.height, viewport.Panel.rect.height)));
        }

        private sealed class EconomyViewport : IDisposable
        {
            private sealed class Control : IDisposable
            {
                public RectTransform Rect = null!;
                public TextMeshProUGUI? Label;
                public RetainedLatestRunViewRunControl? Action;
                public RetainedRunBadgeControl? Badge;
                public RunsFocusHandler? Focus;
                public Image? Icon;
                public EconomyElement? Element;
                public void Dispose()
                {
                    Action?.Button.onClick.RemoveAllListeners();
                    if (Action?.Button is RunsHistoryButton button) button.Binding.CancelPointer();
                    if (Focus != null) { Focus.Move = null; Focus.Selected = null; }
                    Badge?.Dispose(); if (Icon != null) Icon.sprite = null; Element = null;
                }
            }
            private readonly EconomyView owner;
            private readonly string region;
            public RectTransform Panel { get; }
            private readonly ScrollRegion scroll;
            private readonly RectTransform surfaceRoot;
            private readonly List<RectTransform> surfaces = new();
            private readonly Dictionary<EconomyElementKind, List<Control>> pools = new();
            private EconomyDocument? document;
            public EconomyViewport(EconomyView owner, RectTransform parent, string region)
            {
                this.owner = owner; this.region = region;
                Panel = CreateOverviewPanel(parent, "Economy" + region, out var modifier); modifier.Radius = 20;
                scroll = new ScrollRegion(Panel, "Economy" + region + "Scroll", radius: 20);
                var mask = scroll.Scroll.viewport.gameObject.AddComponent<ProceduralImage>(); mask.color = Color.white; mask.raycastTarget = false;
                scroll.Scroll.viewport.gameObject.AddComponent<UniformModifier>().Radius = 20;
                scroll.Scroll.viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                surfaceRoot = Node(scroll.Content, "CardSurfaces"); surfaceRoot.SetAsFirstSibling();
                scroll.Rect.GetComponent<Selectable>().navigation = new Navigation { mode = Navigation.Mode.None };
                scroll.Rect.GetComponent<RunsFocusHandler>().Move = direction => Move(null, direction);
            }
            public void Capture() => owner.selection.Capture(region, scroll.Offset);
            public void Cues() => scroll.Cues();
            public (EconomyViewport, string, EconomyElementKind)? Focused(GameObject focused)
            {
                var control = pools.Values.SelectMany(pool => pool).FirstOrDefault(c => c.Rect.gameObject == focused && c.Element?.Actionable == true);
                if (control?.Element == null) return null;
                return (this, control.Element.Id, control.Element.Kind);
            }
            public void Clear()
            {
                document = null;
                foreach (var control in pools.Values.SelectMany(pool => pool))
                {
                    if (control.Action?.Button is RunsHistoryButton button) button.Binding.CancelPointer();
                    control.Rect.gameObject.SetActive(false); control.Element = null;
                }
                foreach (var surface in surfaces) surface.gameObject.SetActive(false);
            }
            public void Bind(EconomyDocument next, float x, float y, float width, float height)
            {
                document = next; Place(Panel, x, y, width, height);
                scroll.Size(0, 0, width, height, next.Height);
                scroll.SetOffset(owner.selection.Offset(region, height, next.Height));
                while (surfaces.Count < next.Surfaces.Count)
                {
                    var panel = CreateOverviewPanel(surfaceRoot, "Card", out var modifier); modifier.Radius = 10;
                    panel.GetComponent<ProceduralImage>().raycastTarget = false; surfaces.Add(panel);
                }
                for (var i = 0; i < surfaces.Count; i++)
                {
                    surfaces[i].gameObject.SetActive(i < next.Surfaces.Count);
                    if (i >= next.Surfaces.Count) continue;
                    var box = next.Surfaces[i]; Place(surfaces[i], box.X, box.Y, box.Width, box.Height);
                }
                var counts = new Dictionary<EconomyElementKind, int>();
                foreach (var element in next.Elements)
                {
                    if (!pools.TryGetValue(element.Kind, out var pool)) pools.Add(element.Kind, pool = new List<Control>());
                    counts.TryGetValue(element.Kind, out var index);
                    while (pool.Count <= index) pool.Add(Create(element.Kind));
                    var control = pool[index]; counts[element.Kind] = index + 1;
                    Bind(control, element); control.Rect.SetAsLastSibling();
                }
                foreach (var entry in pools)
                {
                    counts.TryGetValue(entry.Key, out var used);
                    for (var i = used; i < entry.Value.Count; i++)
                    {
                        var control = entry.Value[i];
                        if (control.Action?.Button is RunsHistoryButton button) button.Binding.CancelPointer();
                        control.Rect.gameObject.SetActive(false); control.Element = null;
                    }
                }
            }
            private Control Create(EconomyElementKind kind)
            {
                var control = new Control();
                if (kind is EconomyElementKind.Route or EconomyElementKind.RunToggle)
                {
                    control.Action = CreateOverviewLatestRunViewRun(scroll.Content,
                        new RetainedLatestRunViewRunPresentation { IsVisible = true, Label = "" }, owner.typography, owner.material, useIdentityBinding: true);
                    control.Rect = control.Action.Rect;
                    var button = (RunsHistoryButton)control.Action.Button;
                    button.Configure(control.Rect.GetComponent<ProceduralImage>()); AddButtonFeedback(button);
                    control.Focus = control.Rect.gameObject.AddComponent<RunsFocusHandler>();
                    control.Focus.Move = direction => Move(control.Element, direction);
                    control.Focus.Selected = () => owner.Reveal(this);
                    button.onClick.AddListener(() =>
                    { if (control.Element != null) owner.Activate(this, control.Element, button.Binding.Generation); });
                }
                else if (kind is EconomyElementKind.MoneyIcon or EconomyElementKind.CashIcon)
                {
                    control.Rect = Node(scroll.Content, kind.ToString());
                    control.Icon = control.Rect.gameObject.AddComponent<Image>(); control.Icon.raycastTarget = false; control.Icon.preserveAspect = true;
                    control.Icon.sprite = kind == EconomyElementKind.MoneyIcon ? owner.moneyIcon : CombatItemIconPolicy.Resolve("duckov:item:451", owner.icons.ResolveAvailable);
                    control.Label = owner.Text(control.Rect, "MissingIcon", 32); control.Label.text = "?"; control.Label.alignment = TextAlignmentOptions.Center;
                }
                else if (kind == EconomyElementKind.Badge) control.Rect = Node(scroll.Content, "RunOutcome");
                else
                {
                    control.Label = owner.Text(scroll.Content, "EconomyText", 28); control.Rect = control.Label.rectTransform;
                }
                return control;
            }
            private void Bind(Control control, EconomyElement element)
            {
                control.Element = element; control.Rect.gameObject.SetActive(true);
                Place(control.Rect, element.X, element.Y, element.Width, element.Height);
                if (element.Actionable)
                {
                    var action = control.Action!; var button = (RunsHistoryButton)action.Button;
                    // The callback uses the element's exact run identity; binding also includes the role
                    // so a press cannot become a different action after expansion or reflow.
                    button.Binding.Bind(owner.selection.Snapshot!.GenerationId, element.Kind + ":" + element.Id);
                    var background = action.Rect.GetComponent<ProceduralImage>();
                    background.color = element.Kind == EconomyElementKind.Route
                        ? new Color(RetainedOverviewLatestRunViewRunPolicy.BackgroundRed, RetainedOverviewLatestRunViewRunPolicy.BackgroundGreen,
                            RetainedOverviewLatestRunViewRunPolicy.BackgroundBlue, RetainedOverviewLatestRunViewRunPolicy.BackgroundAlpha)
                        : element.Selected ? new Color32(255, 158, 44, 255) : new Color(0, 0, 0, .5f);
                    action.Modifier.Radius = element.Kind == EconomyElementKind.Route ? RetainedOverviewLatestRunViewRunPolicy.CornerRadiusPixels : 10;
                    button.targetGraphic.GetComponent<UniformModifier>().Radius = action.Modifier.Radius;
                    action.Label.gameObject.SetActive(element.Kind == EconomyElementKind.Route);
                    action.Label.text = element.Text; action.Label.enableWordWrapping = true;
                    action.Label.fontSize = RetainedOverviewLatestRunViewRunPolicy.ReferenceFontSize;
                    var inset = RetainedOverviewLatestRunViewRunPolicy.HorizontalLabelPaddingPixels;
                    Place(action.LabelRect, inset, 0, element.Width - 2 * inset, element.Height);
                    button.BindInteractionOverlay(background, true);
                }
                else if (element.Kind == EconomyElementKind.Badge)
                {
                    if (control.Badge?.Presentation.State != element.Outcome)
                    {
                        if (control.Badge != null) { control.Badge.Dispose(); control.Badge.Rect.gameObject.SetActive(false); UnityEngine.Object.Destroy(control.Badge.Rect.gameObject); }
                        var spec = RetainedRunBadgePolicy.ResolveSpecification(element.Outcome);
                        control.Badge = CreateOverviewLatestRunBadge(control.Rect, new RetainedRunBadgePresentation
                        { IsVisible = true, State = element.Outcome, Specification = spec, Label = element.Text }, owner.typography, owner.material);
                    }
                    var badge = control.Badge!;
                    var layout = RetainedRunBadgePolicy.CreateCanvasLayout(RetainedReferenceTransformPolicy.Create(2560, 1440, 1),
                        element.Outcome, Math.Max(1, element.Width - RetainedRunBadgePolicy.FixedHorizontalContentPixels));
                    Place(badge.Rect, 0, 0, element.Width, element.Height); badge.Modifier.Radius = layout.CornerRadius;
                    Place(badge.IconRect, layout.IconLeft, (element.Height - layout.IconHeight) / 2, layout.IconWidth, layout.IconHeight);
                    Place(badge.LabelRect, layout.LabelLeft, 0, layout.LabelWidth, element.Height);
                    badge.Label.enableWordWrapping = true; badge.Label.fontSize = RetainedRunBadgePolicy.ReferenceFontSize;
                    badge.Label.alignment = TextAlignmentOptions.MidlineLeft;
                    if (badge.IconText != null) badge.IconText.fontSize = layout.FontSize;
                }
                else if (element.Kind is EconomyElementKind.MoneyIcon or EconomyElementKind.CashIcon)
                {
                    control.Icon!.enabled = control.Icon.sprite != null; control.Label!.enabled = control.Icon.sprite == null;
                    Place(control.Label.rectTransform, 0, 0, element.Width, element.Height);
                }
                else
                {
                    var label = control.Label!; label.text = element.Text; label.fontSize = element.Size;
                    label.color = element.Muted ? new Color32(177, 177, 177, 255) : Color.white;
                    label.alignment = element.Alignment switch { EconomyTextAlignment.Center => TextAlignmentOptions.Top,
                        EconomyTextAlignment.Right => TextAlignmentOptions.TopRight, EconomyTextAlignment.MiddleLeft => TextAlignmentOptions.MidlineLeft, _ => TextAlignmentOptions.TopLeft };
                    if (element.Size == 46.3f) CombatNativeTextMeasurement.AlignInkTop(label);
                    if (element.Kind == EconomyElementKind.Chevron)
                    {
                        label.alignment = TextAlignmentOptions.Center;
                        control.Rect.pivot = new Vector2(.5f, .5f);
                        Place(control.Rect, element.X + element.Width / 2, element.Y + element.Height / 2, element.Width, element.Height);
                        control.Rect.localRotation = Quaternion.Euler(0, 0, element.Selected ? -90 : 0);
                    }
                }
            }
            public void FocusViewport()
            { owner.Reveal(this); GameManager.EventSystem?.SetSelectedGameObject(scroll.Rect.gameObject); }
            public void Focus(string? id = null, EconomyElementKind kind = EconomyElementKind.RunToggle, bool fallbackToFirst = true)
            {
                if (document == null) { owner.focusTabs(); return; }
                var element = document.Elements.FirstOrDefault(e => e.Actionable && e.Id == id && e.Kind == kind);
                if (element == null && fallbackToFirst) element = document.Elements.FirstOrDefault(e => e.Kind == EconomyElementKind.RunToggle);
                if (element == null) { FocusViewport(); return; }
                owner.Reveal(this);
                scroll.SetOffset(RunsLayoutPolicy.Reveal(scroll.Offset, scroll.Rect.rect.height, document.Height, element.Y, Math.Min(element.Height, scroll.Rect.rect.height)));
                var control = pools[element.Kind].FirstOrDefault(c => ReferenceEquals(c.Element, element));
                if (control != null) GameManager.EventSystem?.SetSelectedGameObject(control.Rect.gameObject);
            }
            private void Move(EconomyElement? element, MoveDirection direction)
            {
                if (direction == MoveDirection.Left)
                { if (this == owner.primary) owner.focusTabs(); else if (element?.Kind == EconomyElementKind.Route) Focus(element.Id); else owner.primary.FocusViewport(); return; }
                if (direction == MoveDirection.Right)
                { if (this == owner.primary) owner.recent.Focus(); else if (element?.Kind == EconomyElementKind.RunToggle) Focus(element.Id, EconomyElementKind.Route, fallbackToFirst: false); return; }
                if (direction != MoveDirection.Up && direction != MoveDirection.Down) return;
                var delta = direction == MoveDirection.Up ? -1 : 1;
                if (element == null)
                {
                    if (direction == MoveDirection.Up && scroll.Offset <= .5f) owner.focusTabs();
                    else ((RunsScrollRect)scroll.Scroll).MoveBy(delta * scroll.Scroll.scrollSensitivity);
                    return;
                }
                var rows = document?.Elements.Where(e => e.Kind == EconomyElementKind.RunToggle).ToArray() ?? Array.Empty<EconomyElement>();
                var next = Array.FindIndex(rows, e => e.Id == element.Id) + delta;
                if (next < 0) owner.focusTabs(); else if (next < rows.Length) Focus(rows[next].Id);
                else FocusViewport();
            }
            public void Dispose()
            {
                foreach (var control in pools.Values.SelectMany(pool => pool)) control.Dispose();
                pools.Clear(); surfaces.Clear(); document = null; scroll.Dispose();
            }
        }
        private TextMeshProUGUI Text(RectTransform parent, string name, float size)
        {
            var label = Node(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = typography.Font; label.fontSharedMaterial = material; label.fontSize = size;
            label.fontStyle = FontStyles.Normal; label.fontWeight = FontWeight.Regular; label.color = Color.white;
            label.enableWordWrapping = true; label.enableAutoSizing = false; label.richText = false;
            label.raycastTarget = false; label.overflowMode = TextOverflowModes.Overflow; label.alignment = TextAlignmentOptions.TopLeft;
            return label;
        }
        private static Sprite? ResolveMoneyIcon()
        {
            // Installed 2.3.30 MoneyDisplay/Money/Image uses the native Sprite named Cash
            // (resources.assets path ID 5970). The sibling Banknote sprite is physical Cash.
            // Borrow only the proven component path; ambiguity or absence changes no metric.
            try
            {
                Sprite? result = null;
                foreach (var display in Resources.FindObjectsOfTypeAll<Duckov.UI.MoneyDisplay>())
                {
                    if (display == null) continue;
                    var graphic = display.transform.Find("Money/Image")?.GetComponent<Image>();
                    var candidate = graphic?.sprite;
                    if (candidate == null || candidate.name != "Cash") continue;
                    if (result != null && result != candidate) return null;
                    result = candidate;
                }
                return result;
            }
            catch { return null; }
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
            primary.Dispose(); recent.Dispose(); outer.Dispose(); selection.Refresh(null);
            root.gameObject.SetActive(false); UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
