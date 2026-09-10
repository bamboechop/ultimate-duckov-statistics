using Duckov.UI.Animations;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    private CraftingView? craftingView;
    private sealed class CraftingView : IDisposable
    {
        private readonly RectTransform root;
        private readonly ScrollRegion outer;
        private readonly CraftingViewport outputs, resources;
        private readonly TextMeshProUGUI unavailable;
        private readonly CombatNativeTextMeasurement measure;
        private readonly NativeHeaderTitleTypography typography;
        private readonly Material material;
        private readonly NativeItemIconResolver icons = new();
        private readonly CraftingSelection selection = new();
        private readonly Action focusTabs;
        private float width, height, pixels;
        private bool dirty = true, disposed;
        private CraftingViewport? restoreFocus;
        private string? restoreFocusId;
        public CraftingView(RectTransform parent, NativeHeaderTitleTypography typography, Material material, Action focusTabs)
        {
            this.typography = typography; this.material = material; this.focusTabs = focusTabs;
            root = Node(parent, "CraftingContentView");
            outer = new ScrollRegion(root, "CraftingOuter", radius: 20); RoundedMask(outer);
            outputs = new CraftingViewport(this, outer.Content, "Outputs", "outputs");
            resources = new CraftingViewport(this, outer.Content, "Resources", "resources");
            unavailable = Text(root, "Unavailable", 30); unavailable.text = UiText.Get("ui.profile_unavailable");
            measure = new CombatNativeTextMeasurement(Text(root, "Measurement", 28));
            outer.Rect.GetComponent<Selectable>().navigation = new Navigation { mode = Navigation.Mode.None };
            outer.Rect.GetComponent<RunsFocusHandler>().Move = d =>
            { if (d == MoveDirection.Up || d == MoveDirection.Left) focusTabs(); else FocusFirst(); };
        }
        private float Measure(string value, float w, float size) => size == 40
            ? measure.SectionHeight(value, Math.Max(1, w), size) : measure.Height(value, Math.Max(1, w), size);
        public void Refresh(CraftingPresentation? next)
        {
            if (disposed) return; Capture();
            var focused = GameManager.EventSystem?.currentSelectedGameObject;
            restoreFocus = next != null && selection.Snapshot?.GenerationId == next.GenerationId && focused != null
                ? focused.transform.IsChildOf(outputs.Panel) ? outputs : focused.transform.IsChildOf(resources.Panel) ? resources : null : null;
            restoreFocusId = focused == null ? null : restoreFocus?.FocusedRowId(focused);
            if (RetainedRefreshPolicy.RequiresInvalidation(selection.Snapshot?.GenerationId, next?.GenerationId))
            { outputs.Clear(); resources.Clear(); }
            selection.Refresh(next);
            outer.Rect.gameObject.SetActive(next != null); unavailable.gameObject.SetActive(next == null);
            if (focused != null && focused.transform.IsChildOf(root) && !focused.activeInHierarchy) focusTabs();
            dirty = true;
        }
        private void Capture()
        {
            if (dirty || selection.Snapshot == null) return;
            selection.Capture("outer", outer.Offset); outputs.Capture(); resources.Capture();
        }
        public void SetVisible(bool visible) { if (disposed) return; if (!visible) Capture(); root.gameObject.SetActive(visible); }
        public void FocusFirst() => outputs.Focus(selection.FocusId("outputs"));
        private void Activate(string generation, string id)
        {
            if (disposed) return; Capture();
            if (selection.Toggle(generation, id)) dirty = true;
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
            var stacked = CombatLayoutPolicy.Stack(pixels); var cw = CraftingLayoutPolicy.ColumnWidth(width, stacked);
            var left = CraftingDocument.Create(selection, false, cw, Measure);
            var right = CraftingDocument.Create(selection, true, cw, Measure);
            var lh = CraftingLayoutPolicy.ColumnHeight(height, left.Height, stacked);
            var rh = CraftingLayoutPolicy.ColumnHeight(height, right.Height, stacked);
            outputs.Bind(left, 0, 0, cw, lh);
            var ry = stacked ? lh + CombatLayoutPolicy.Gap : 0;
            resources.Bind(right, stacked ? 0 : cw + CombatLayoutPolicy.Gap, ry, cw, rh);
            outer.Size(0, 0, width, height, Math.Max(lh, ry + rh)); outer.Scroll.vertical = stacked;
            outer.SetOffset(selection.Offset("outer", height, outer.Content.rect.height));
            outputs.Render(); resources.Render();
            if (restoreFocus != null)
            {
                var target = restoreFocus; restoreFocus = null;
                if (restoreFocusId == null) target.FocusViewport(); else target.Focus(restoreFocusId);
                restoreFocusId = null;
            }
        }
        public void Tick()
        {
            if (disposed || !root.gameObject.activeInHierarchy || selection.Snapshot == null) return;
            outputs.Render(); resources.Render(); outer.Cues(); Capture();
        }
        private sealed class CraftingViewport : IDisposable
        {
            private readonly CraftingView owner;
            private readonly string region;
            public RectTransform Panel { get; }
            private readonly ScrollRegion scroll;
            private readonly CombatControlPool<Control> controls;
            private readonly RectTransform surfaceRoot;
            private readonly List<RectTransform> surfaces = new();
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
                public TextMeshProUGUI Name = null!, Value = null!, Caption = null!, Fallback = null!, Chevron = null!;
                public Image Icon = null!;
                public EquipmentRenderRow? Row;
                public void Dispose()
                {
                    Button.Binding.CancelPointer(); Button.onClick.RemoveAllListeners(); Focus.Move = null; Focus.Selected = null;
                    Row = null; Icon.sprite = null; NativeItemIconAppearance.Clear(Icon);
                }
            }
            public CraftingViewport(CraftingView owner, RectTransform parent, string name, string region)
            {
                this.owner = owner; this.region = region;
                Panel = CreateOverviewPanel(parent, "Crafting" + name, out var modifier); modifier.Radius = 20;
                scroll = new ScrollRegion(Panel, name + "Scroll", radius: 20); RoundedMask(scroll);
                surfaceRoot = Node(scroll.Content, "CraftingCardSurfaces"); surfaceRoot.SetAsFirstSibling();
                scroll.Rect.GetComponent<Selectable>().navigation = new Navigation { mode = Navigation.Mode.None };
                scroll.Rect.GetComponent<RunsFocusHandler>().Move = d => Move(null, d);
                controls = new CombatControlPool<Control>(Create);
            }
            public void Capture() => owner.selection.Capture(region, scroll.Offset);
            public string? FocusedRowId(GameObject focused) => controls.Items.FirstOrDefault(c => c.Rect.gameObject == focused)?.Row?.Id;
            public void FocusViewport() => GameManager.EventSystem?.SetSelectedGameObject(scroll.Rect.gameObject);
            public void Clear()
            {
                document = null; focusedId = null;
                foreach (var c in controls.Items)
                { c.Button.Binding.CancelPointer(); c.Rect.gameObject.SetActive(false); c.Row = null; c.Icon.sprite = null; NativeItemIconAppearance.Clear(c.Icon); }
                foreach (var surface in surfaces) surface.gameObject.SetActive(false);
                rebuild = true;
            }
            public void Bind(EquipmentDocument next, float x, float y, float width, float height)
            {
                document = next; Place(Panel, x, y, width, height); scroll.Size(0, 0, width, height, next.Height);
                scroll.SetOffset(owner.selection.Offset(region, height, next.Height)); rebuild = true;
            }
            private Control Create()
            {
                var shared = CreateOverviewLatestRunViewRun(scroll.Content, new RetainedLatestRunViewRunPresentation
                { IsVisible = true, Label = "" }, owner.typography, owner.material, useIdentityBinding: true);
                shared.Label.gameObject.SetActive(false);
                var c = new Control { Rect = shared.Rect, Background = shared.Rect.GetComponent<ProceduralImage>(), Button = (RunsHistoryButton)shared.Button };
                c.Rect.gameObject.name = "CraftingRow"; c.Rect.GetComponent<UniformModifier>().Radius = 10;
                c.Button.Configure(c.Background); AddButtonFeedback(c.Button);
                c.Name = owner.Text(c.Rect, "Name", 28); c.Value = owner.Text(c.Rect, "Value", 28); c.Value.alignment = TextAlignmentOptions.TopRight;
                c.Caption = owner.Text(c.Rect, "Caption", 22);
                c.Chevron = owner.Text(c.Rect, "Chevron", 28); c.Chevron.text = "›"; c.Chevron.alignment = TextAlignmentOptions.Center;
                c.Fallback = owner.Text(c.Rect, "MissingItem", 32); c.Fallback.alignment = TextAlignmentOptions.Center;
                c.Icon = Node(c.Rect, "ItemIcon").gameObject.AddComponent<Image>(); c.Icon.raycastTarget = false; c.Icon.preserveAspect = true;
                c.Focus = c.Rect.gameObject.AddComponent<RunsFocusHandler>(); c.Focus.Move = d => Move(c.Row?.Id, d);
                c.Focus.Selected = () => { focusedId = c.Row?.Id; if (focusedId != null) owner.selection.Focus(region, focusedId); };
                c.Button.onClick.AddListener(() => { if (c.Row?.Actionable == true) owner.Activate(c.Button.Binding.Generation, c.Button.Binding.Id); });
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
                    var surface = CreateOverviewPanel(surfaceRoot, "CraftingCard", out var modifier); modifier.Radius = 10;
                    surface.GetComponent<ProceduralImage>().raycastTarget = false; surface.GetComponent<ProceduralImage>().color = new Color(0, 0, 0, .5f); surfaces.Add(surface);
                }
                for (var i = 0; i < surfaces.Count; i++)
                {
                    surfaces[i].gameObject.SetActive(i < visibleSurfaces.Length); if (i >= visibleSurfaces.Length) continue;
                    var box = visibleSurfaces[i]; Place(surfaces[i], box.X, box.Y, box.Width, box.Height);
                }
                controls.Ensure(visible.Count); var pool = controls.Items;
                var selected = GameManager.EventSystem?.currentSelectedGameObject;
                var focused = pool.FirstOrDefault(c => c.Rect.gameObject == selected);
                var focusedSlot = -1;
                if (focused?.Row != null)
                    for (var i = 0; i < visible.Count; i++)
                        if (document.Rows[visible[i]].Actionable && document.Rows[visible[i]].Id == focused.Row.Id) focusedSlot = i;
                if (focused != null && focusedSlot < 0) FocusViewport();
                else if (focused != null)
                { var previous = pool.IndexOf(focused); (pool[previous], pool[focusedSlot]) = (pool[focusedSlot], pool[previous]); }
                for (var i = 0; i < pool.Count; i++)
                {
                    var c = pool[i]; c.Rect.gameObject.SetActive(i < visible.Count);
                    if (i >= visible.Count) { c.Button.Binding.CancelPointer(); c.Row = null; c.Icon.sprite = null; NativeItemIconAppearance.Clear(c.Icon); continue; }
                    BindControl(c, document.Rows[visible[i]]); c.Rect.SetAsLastSibling();
                }
                scroll.Cues();
            }
            private void BindControl(Control c, EquipmentRenderRow r)
            {
                c.Row = r; c.Button.Binding.Bind(owner.selection.Snapshot!.GenerationId, r.Id);
                c.Button.BindInteractionOverlay(c.Background, r.Actionable);
                c.Rect.GetComponent<ButtonAnimation>().enabled = r.Actionable; c.Rect.GetComponent<RunsButtonFeedback>().enabled = r.Actionable;
                c.Background.color = r.Selected ? new Color32(255, 158, 44, 255) : Color.clear;
                Place(c.Rect, r.X, r.Y, r.Width, r.Height);
                c.Name.text = r.Name; c.Name.fontSize = r.Kind == EquipmentRowKind.Heading ? 40 : r.Kind == EquipmentRowKind.Notice ? 22 : 28;
                c.Name.color = r.Kind == EquipmentRowKind.Notice ? Muted : Color.white;
                var centerText = r.HasIcon && r.Caption.Length == 0;
                Place(c.Name.rectTransform, r.TextLeft, centerText ? (r.Height - r.NameHeight) / 2 : r.TextTop, r.NameWidth, r.NameHeight);
                if (r.SectionHeading) CombatNativeTextMeasurement.AlignInkTop(c.Name);
                c.Value.gameObject.SetActive(r.Value.Length > 0);
                if (r.Value.Length > 0)
                { c.Value.text = r.Value; Place(c.Value.rectTransform, r.TextLeft + r.NameWidth + 10, centerText ? (r.Height - r.ValueHeight) / 2 : 12, r.Width - r.TextLeft - r.NameWidth - 25, r.ValueHeight); }
                c.Caption.gameObject.SetActive(r.Caption.Length > 0);
                if (r.Caption.Length > 0)
                {
                    c.Caption.text = r.Caption; c.Caption.color = Muted;
                    Place(c.Caption.rectTransform, r.TextLeft, r.CaptionTop, r.Width - r.TextLeft - 15, owner.Measure(r.Caption, r.Width - r.TextLeft - 15, 22));
                }
                c.Icon.gameObject.SetActive(r.HasIcon); c.Fallback.gameObject.SetActive(r.HasIcon);
                if (r.HasIcon)
                {
                    var icon = CombatItemIconPolicy.Resolve(r.IconId, owner.icons.ResolveAvailable);
                    c.Icon.sprite = icon; c.Icon.enabled = icon != null; NativeItemIconAppearance.Apply(c.Icon, r.IconId);
                    c.Fallback.enabled = icon == null; c.Fallback.text = r.IconFallback; c.Fallback.color = r.EmptyIcon ? Muted : Color.white;
                    var inset = r.Expandable ? 40 : 15;
                    Place(c.Icon.rectTransform, inset, (r.Height - 60) / 2, 60, 60); Place(c.Fallback.rectTransform, inset, (r.Height - 60) / 2, 60, 60);
                }
                else { c.Icon.sprite = null; NativeItemIconAppearance.Clear(c.Icon); }
                c.Chevron.gameObject.SetActive(r.Expandable);
                if (r.Expandable)
                {
                    c.Chevron.rectTransform.pivot = new Vector2(.5f, .5f);
                    Place(c.Chevron.rectTransform, 25, r.Height / 2, 20, 30);
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
                var control = controls.Items.FirstOrDefault(c => ReferenceEquals(c.Row, row));
                if (control != null) GameManager.EventSystem?.SetSelectedGameObject(control.Rect.gameObject);
            }
            private void Move(string? id, MoveDirection direction)
            {
                if (direction == MoveDirection.Left)
                { if (region == "outputs") owner.focusTabs(); else owner.outputs.Focus(owner.selection.FocusId("outputs")); return; }
                if (direction == MoveDirection.Right)
                { if (region == "outputs") owner.resources.Focus(owner.selection.FocusId("resources")); return; }
                if (direction != MoveDirection.Up && direction != MoveDirection.Down) return;
                var rows = document?.Rows.Where(r => r.Actionable).ToArray() ?? Array.Empty<EquipmentRenderRow>();
                if (rows.Length == 0 || id == null)
                {
                    if (direction == MoveDirection.Up && scroll.Offset <= .5f) owner.focusTabs();
                    else ((RunsScrollRect)scroll.Scroll).MoveBy((direction == MoveDirection.Up ? -1 : 1) * scroll.Scroll.scrollSensitivity);
                    return;
                }
                var index = Array.FindIndex(rows, r => r.Id == id);
                var next = CombatLayoutPolicy.Move(index, rows.Length, direction == MoveDirection.Up ? -1 : 1);
                if (next < 0) owner.focusTabs();
                else if (next < rows.Length) Focus(rows[next].Id);
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
            label.raycastTarget = false; label.overflowMode = TextOverflowModes.Overflow; label.alignment = TextAlignmentOptions.TopLeft; return label;
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
            if (disposed) return; disposed = true;
            outputs.Dispose(); resources.Dispose(); outer.Dispose(); selection.Refresh(null);
            root.gameObject.SetActive(false); UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
