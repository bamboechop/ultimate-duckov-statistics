using Duckov.UI.Animations;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    private CombatView? combatView;

    private sealed class CombatView : IDisposable
    {
        private readonly RectTransform root;
        private readonly ScrollRegion outer;
        private readonly CombatViewport selector, primary, ammunition;
        private readonly TextMeshProUGUI unavailable, footer;
        private readonly CombatNativeTextMeasurement measure;
        private readonly NativeHeaderTitleTypography typography;
        private readonly Material material;
        private readonly NativeItemIconResolver icons = new();
        private readonly CombatSelection selection = new();
        private readonly Action focusTabs;
        private float width, height, pixels;
        private bool dirty = true, disposed;
        private CombatViewport? restoreFocus;
        private string? restoreFocusId;
        private readonly string[] pageKeys = { "ui.summary", "ui.enemies", "ui.combat_weapons_ammunition", "ui.incoming_damage" };

        public CombatView(RectTransform parent, NativeHeaderTitleTypography typography, Material material, Action focusTabs)
        {
            this.typography = typography; this.material = material; this.focusTabs = focusTabs;
            root = Node(parent, "CombatContentView");
            outer = new ScrollRegion(root, "CombatOuter", radius: 20); RoundedMask(outer);
            selector = new CombatViewport(this, outer.Content, "Selector", "selector");
            primary = new CombatViewport(this, outer.Content, "Page", "primary");
            ammunition = new CombatViewport(this, outer.Content, "Ammunition", "ammo");
            unavailable = Text(root, "Unavailable", 30); unavailable.text = UiText.Get("ui.profile_unavailable");
            footer = Text(root, "FiringActionContract", 20); footer.text = UiText.Get("ui.combat_firing_footer");
            measure = new CombatNativeTextMeasurement(Text(root, "Measurement", 28));
            outer.Rect.GetComponent<Selectable>().navigation = new Navigation { mode = Navigation.Mode.None };
            outer.Rect.GetComponent<RunsFocusHandler>().Move = d => { if (d == MoveDirection.Up || d == MoveDirection.Left) focusTabs(); else FocusSelector(); };
        }
        private float Measure(string value, float w, float size)
            => measure.HeightWithSectionInk(value, w, size);
        private float MeasureWidth(string value, float size)
            => measure.Width(value, size);
        private CombatDocument Document() => new(Measure, MeasureWidth);
        public void Refresh(CombatPresentation? next)
        {
            if (disposed) return;
            Capture();
            var focused = GameManager.EventSystem?.currentSelectedGameObject;
            restoreFocus = next != null && selection.Snapshot?.GenerationId == next.GenerationId && focused != null
                ? focused.transform.IsChildOf(primary.Panel) ? primary : focused.transform.IsChildOf(ammunition.Panel) ? ammunition
                    : focused.transform.IsChildOf(selector.Panel) ? selector : null : null;
            restoreFocusId = focused == null ? null : restoreFocus?.FocusedRowId(focused);
            if (RetainedRefreshPolicy.RequiresInvalidation(selection.Snapshot?.GenerationId, next?.GenerationId))
                { selector.Clear(); primary.Clear(); ammunition.Clear(); }
            selection.Refresh(next);
            outer.Rect.gameObject.SetActive(next != null); unavailable.gameObject.SetActive(next == null);
            footer.gameObject.SetActive(false);
            if (focused != null && focused.transform.IsChildOf(root) && !focused.activeInHierarchy) focusTabs();
            dirty = true;
        }
        private void Capture()
        {
            if (dirty || selection.Snapshot == null) return;
            selection.Capture("outer", outer.Offset); selector.Capture(); primary.Capture(); ammunition.Capture();
        }
        public void SetVisible(bool visible)
        {
            if (disposed) return;
            if (!visible) Capture();
            root.gameObject.SetActive(visible);
        }
        public void FocusSelector() => selector.Focus(((int)selection.Page).ToString(System.Globalization.CultureInfo.InvariantCulture));
        private void Activate(string region, string generation, string id)
        {
            if (disposed || selection.Snapshot?.GenerationId != generation) return;
            Capture();
            if (region == "selector")
            {
                if (!int.TryParse(id, out var index) || !selection.SelectPage((CombatPanelSection)index)) return;
                primary.Clear(); ammunition.Clear();
            }
            else if (selection.Page == CombatPanelSection.Enemies)
            {
                if (id.StartsWith("sort:", StringComparison.Ordinal) && int.TryParse(id.AsSpan(5), out var enemyColumn)) selection.SortEnemy(generation, enemyColumn);
                else selection.ToggleEnemy(generation, id);
            }
            else if (selection.Page == CombatPanelSection.WeaponsAndAmmunition) selection.SelectWeapon(generation, id);
            else if (selection.Page == CombatPanelSection.IncomingDamage && id.StartsWith("sort:", StringComparison.Ordinal)
                && int.TryParse(id.AsSpan(5), out var column)) selection.SortIncoming(generation, column);
            dirty = true;
        }
        public void Layout(RetainedVisualCanvasLayout shell, float viewportPixels, float canvasHeight)
        {
            if (disposed) return;
            var frame = CombatLayoutPolicy.Frame(shell, canvasHeight);
            var scale = frame.Scale; var top = frame.Top; var w = frame.Width; var h = frame.Height;
            if (w != width || h != height || pixels != viewportPixels)
            { Capture(); width = w; height = h; pixels = viewportPixels; dirty = true; }
            root.localScale = new Vector3(scale, scale, 1); Place(root, shell.Header.Left, top, w, h);
            if (!dirty || !root.gameObject.activeInHierarchy) return;
            dirty = false; Place(unavailable.rectTransform, 30, 30, width - 60, Measure(unavailable.text, width - 60, 30));
            var snapshot = selection.Snapshot; if (snapshot == null) return;
            var stacked = CombatLayoutPolicy.Stack(pixels); var widths = CombatLayoutPolicy.Widths(width, stacked);
            var weapons = selection.Page == CombatPanelSection.WeaponsAndAmmunition;
            footer.gameObject.SetActive(weapons);
            var footerHeight = weapons ? Measure(footer.text, widths.Page, 20) + 12 : 0;
            Place(footer.rectTransform, stacked ? 0 : widths.Selector + 40, height - footerHeight, widths.Page, footerHeight);
            var available = Math.Max(1, height - footerHeight);
            var nav = Document(); float ny = 30;
            for (var i = 0; i < pageKeys.Length; i++)
                ny += nav.Add(new CombatRenderRow
                {
                    Id = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Kind = CombatRowKind.Selector,
                    Cells = new[] { UiText.Get(pageKeys[i]) },
                    Actionable = true,
                    Selected = i == (int)selection.Page
                }, 30, ny, widths.Selector - 60) + 10;
            var navHeight = stacked ? nav.Height : height;
            selector.Bind(nav, 0, 0, widths.Selector, navHeight);
            var x = stacked ? 0 : widths.Selector + 40; var y = stacked ? navHeight + 40 : 0;
            var page = Document(); var ammo = Document(); var pageWidth = weapons && !stacked ? (widths.Page - 40) / 2 : widths.Page;
            switch (selection.Page)
            {
                case CombatPanelSection.Summary: page.Summary(snapshot, pageWidth, stacked); break;
                case CombatPanelSection.Enemies: page.Table(snapshot.Enemies, snapshot.EnemyNotice, pageWidth, selection.EnemyId, false, snapshot, stacked, selection.EnemySort); break;
                case CombatPanelSection.IncomingDamage: page.Table(snapshot.Attackers, snapshot.IncomingNotice, pageWidth, null, true, snapshot, stacked, selection.IncomingSort); break;
                default: page.Items(selection, pageWidth, false); ammo.Items(selection, pageWidth, true); break;
            }
            // Narrow documents keep full-width readable rows and bounded inner tables/columns.
            var ph = stacked ? Math.Min(Math.Max(320, available * .85f), page.Height) : available;
            primary.Bind(page, x, y, pageWidth, Math.Max(1, ph));
            ammunition.Panel.gameObject.SetActive(weapons);
            var bottom = y + ph;
            if (weapons)
            {
                var ay = stacked ? bottom + 40 : y; var ah = stacked ? Math.Min(Math.Max(320, available * .85f), ammo.Height) : available;
                ammunition.Bind(ammo, stacked ? x : x + pageWidth + 40, ay, pageWidth, Math.Max(1, ah));
                bottom = Math.Max(bottom, ay + ah);
            }
            var outerHeight = CombatLayoutPolicy.OuterViewport(stacked, height, footerHeight);
            outer.Size(0, 0, width, outerHeight, Math.Max(navHeight, bottom));
            outer.Scroll.vertical = stacked;
            outer.SetOffset(stacked ? selection.Offset("outer", outerHeight, outer.Content.rect.height) : 0);
            selector.Render(); primary.Render(); if (weapons) ammunition.Render();
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
            selector.Render(); primary.Render(); if (ammunition.Panel.gameObject.activeSelf) ammunition.Render();
            outer.Cues(); if (!dirty) Capture();
        }

        private sealed class CombatViewport : IDisposable
        {
            private readonly CombatView owner;
            private readonly string region;
            public RectTransform Panel { get; }
            private readonly ScrollRegion scroll;
            private readonly CombatControlPool<Control> controls;
            private List<Control> Pool => controls.Items;
            private CombatDocument? document;
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
                public CombatRenderRow? Row;
                public void Dispose()
                { Button.Binding.CancelPointer(); Button.onClick.RemoveAllListeners(); Focus.Move = null; Focus.Selected = null; Row = null; }
            }
            public CombatViewport(CombatView owner, RectTransform parent, string name, string region)
            {
                this.owner = owner; this.region = region;
                Panel = CreateOverviewPanel(parent, "Combat" + name, out var modifier); modifier.Radius = 20;
                scroll = new ScrollRegion(Panel, name + "Scroll", radius: 20); RoundedMask(scroll);
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
                foreach (var c in Pool) { c.Button.Binding.CancelPointer(); c.Rect.gameObject.SetActive(false); c.Row = null; }
                rebuild = true;
            }
            public void Bind(CombatDocument next, float x, float y, float width, float height)
            {
                document = next; Place(Panel, x, y, width, height);
                scroll.Size(0, 0, width, height, next.Height);
                scroll.SetOffset(owner.selection.Offset(region, height, next.Height)); rebuild = true;
            }
            private Control Create()
            {
                var c = new Control { Rect = CreateOverviewPanel(scroll.Content, "CombatRow", out _) };
                c.Rect.GetComponent<UniformModifier>().Radius = 10;
                c.Background = c.Rect.GetComponent<ProceduralImage>();
                c.Button = c.Rect.gameObject.AddComponent<RunsHistoryButton>(); c.Button.Configure(c.Background);
                c.Rect.gameObject.AddComponent<ButtonAnimation>(); AddButtonFeedback(c.Button);
                c.Text = Enumerable.Range(0, 4).Select(i => owner.Text(c.Rect, "Cell" + i, 28)).ToArray();
                c.Detail = owner.Text(c.Rect, "Detail", 22);
                c.Chevron = owner.Text(c.Rect, "Chevron", 28); c.Chevron.text = "›"; c.Chevron.alignment = TextAlignmentOptions.Center;
                c.Fallback = owner.Text(c.Rect, "MissingItem", 32); c.Fallback.text = "?"; c.Fallback.alignment = TextAlignmentOptions.Center;
                c.Icon = Node(c.Rect, "ItemIcon").gameObject.AddComponent<Image>(); c.Icon.raycastTarget = false; c.Icon.preserveAspect = true;
                c.Focus = c.Rect.gameObject.AddComponent<RunsFocusHandler>();
                c.Focus.Move = d => Move(c.Row?.Id, d);
                c.Focus.Selected = () =>
                {
                    focusedId = c.Row?.Id;
                    if (region == "primary" && focusedId != null) owner.selection.Focus(focusedId);
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
                var visible = CombatLayoutPolicy.Visible(document.Rows, scroll.Offset, scroll.Rect.rect.height);
                controls.Ensure(visible.Count);
                var pool = Pool;
                var selected = GameManager.EventSystem?.currentSelectedGameObject;
                var focused = pool.FirstOrDefault(c => c.Rect.gameObject == selected);
                // Keep the focused identity on its original GameObject while it is visible.
                // Wheel scrolling it out of view moves focus to the viewport without revealing it back.
                var focusedSlot = focused?.Row == null ? -1 : CombatLayoutPolicy.FocusSlot(document.Rows, visible, focused.Row.Id);
                if (focused != null && focusedSlot < 0) GameManager.EventSystem?.SetSelectedGameObject(scroll.Rect.gameObject);
                if (focused != null && focusedSlot >= 0)
                {
                    var previous = pool.IndexOf(focused);
                    (pool[previous], pool[focusedSlot]) = (pool[focusedSlot], pool[previous]);
                }
                for (var i = 0; i < pool.Count; i++)
                {
                    var c = pool[i]; c.Rect.gameObject.SetActive(i < visible.Count);
                    if (i >= visible.Count) { c.Button.Binding.CancelPointer(); c.Row = null; continue; }
                    BindControl(c, document.Rows[visible[i]]);
                    // Pools may swap focused controls. Restore document paint order so the
                    // single header band always stays behind its transparent header buttons.
                    c.Rect.SetAsLastSibling();
                }
                scroll.Cues();
            }
            private void BindControl(Control c, CombatRenderRow r)
            {
                c.Row = r; c.Button.Binding.Bind(owner.selection.Snapshot!.GenerationId, r.Id);
                c.Button.BindInteractionOverlay(c.Background, r.Actionable);
                c.Background.color = r.Selected ? new Color32(255, 158, 44, 255)
                    : CombatLayoutPolicy.HasBackground(r.Kind) ? new Color(0, 0, 0, .5f) : Color.clear;
                Place(c.Rect, r.X, r.Y, r.Width, r.Height);
                var inner = Math.Max(1, r.Width - 30); float y = 12;
                for (var i = 0; i < c.Text.Length; i++)
                {
                    var label = c.Text[i]; label.gameObject.SetActive(i < r.Cells.Length); if (i >= r.Cells.Length) continue;
                    label.text = r.Cells[i]; label.color = CombatLayoutPolicy.Muted(r, i) ? Muted : Color.white;
                    label.alignment = r.Kind == CombatRowKind.Card ? TextAlignmentOptions.Top
                        : r.RightAligned || i > 0 && (r.Kind == CombatRowKind.Metric || r.Kind == CombatRowKind.Table && !r.Stacked)
                            ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
                    float x = 15, top = 12, w = inner, size = 28;
                    switch (r.Kind)
                    {
                        case CombatRowKind.Heading:
                            x = 0; size = i == 0 ? 46.3f : 22; top = i == 0 ? 0 : r.SuffixTop;
                            if (i > 0) { x += r.SuffixLeft; w -= r.SuffixLeft; }
                            break;
                        case CombatRowKind.Notice: size = 20; top = 6; break;
                        case CombatRowKind.Selector: size = 32; break;
                        case CombatRowKind.Card: size = i == 0 ? 32 : 20; top = y; break;
                        case CombatRowKind.TableHeader: size = CombatLayoutPolicy.TableHeaderSize; break;
                        case CombatRowKind.Metric: w = inner * (i == 0 ? .6f : .4f) - (i == 0 ? 10 : 0); x += i == 0 ? 0 : inner * .6f; break;
                        case CombatRowKind.Item: x = 115; w = Math.Max(1, inner - 100); top = y; size = i == 0 ? 32 : i == 1 ? 24 : 20; break;
                        case CombatRowKind.Table:
                            if (r.Stacked) { top = y; size = 26; }
                            else if (r.Columns is { Length: 4 }) { w = r.Columns[i] - 30; x += r.Columns.Take(i).Sum(); }
                            else { w = i == 0 ? inner * .48f - 20 : inner * .52f / 3 - 10; x += i == 0 ? 0 : inner * .48f + (i - 1) * inner * .52f / 3; }
                            if (i == 0 && r.Expandable) { x += 28; w -= 28; }
                            break;
                    }
                    label.fontSize = size;
                    var h = owner.Measure(label.text, w, size); Place(label.rectTransform, x, top, w, h);
                    if (r.Kind == CombatRowKind.Heading && i == 0) CombatNativeTextMeasurement.AlignInkTop(label);
                    y = r.Kind is CombatRowKind.Card or CombatRowKind.Item || r.Kind == CombatRowKind.Table && r.Stacked ? top + h + (r.Stacked ? 6 : 0) : Math.Max(y, top + h);
                }
                if (r.Kind == CombatRowKind.Heading && r.Cells.Length > 1 && r.SuffixLeft > 0)
                {
                    var title = c.Text[0]; var suffix = c.Text[1];
                    var titleBottom = CombatNativeTextMeasurement.GlyphBottom(title);
                    var suffixBottom = CombatNativeTextMeasurement.GlyphBottom(suffix);
                    if (titleBottom.HasValue && suffixBottom.HasValue)
                    {
                        var position = suffix.rectTransform.anchoredPosition;
                        position.y = CombatLayoutPolicy.AlignGlyphBottom(title.rectTransform.anchoredPosition.y, titleBottom.Value, suffixBottom.Value);
                        suffix.rectTransform.anchoredPosition = position;
                    }
                }
                c.Chevron.gameObject.SetActive(r.Expandable);
                if (r.Expandable)
                {
                    var chevronHeight = owner.Measure("›", 20, 28);
                    c.Chevron.rectTransform.pivot = new Vector2(.5f, .5f);
                    Place(c.Chevron.rectTransform, 25, 12 + chevronHeight / 2, 20, chevronHeight);
                    c.Chevron.rectTransform.localRotation = Quaternion.Euler(0, 0, r.Selected ? -90 : 0);
                }
                c.Detail.gameObject.SetActive(r.Detail.Length > 0); c.Detail.text = r.Detail;
                if (r.Detail.Length > 0) Place(c.Detail.rectTransform, 15, y + 8, inner, owner.Measure(r.Detail, inner, 22));
                var item = r.Kind == CombatRowKind.Item;
                c.Icon.gameObject.SetActive(item); c.Fallback.gameObject.SetActive(item);
                if (item)
                {
                    var icon = CombatItemIconPolicy.Resolve(r.IconId, owner.icons.ResolveAvailable); c.Icon.sprite = icon; c.Icon.enabled = icon != null; c.Fallback.enabled = icon == null;
                    var emptyIcon = NativeItemTypeIdPolicy.UseEmptyIcon(r.IconId);
                    c.Fallback.text = emptyIcon ? "—" : "?"; c.Fallback.color = emptyIcon ? Muted : Color.white;
                    Place(c.Icon.rectTransform, 15, 15, 80, 80); Place(c.Fallback.rectTransform, 15, 15, 80, 80);
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
                { if (region == "selector") owner.focusTabs(); else if (region == "ammo") owner.primary.Focus(owner.selection.FocusId); else owner.FocusSelector(); return; }
                if (direction == MoveDirection.Right)
                { if (region == "selector") owner.primary.Focus(owner.selection.FocusId); else if (region == "primary" && owner.ammunition.Panel.gameObject.activeSelf) owner.ammunition.Focus(); else owner.FocusSelector(); return; }
                if (direction != MoveDirection.Up && direction != MoveDirection.Down) return;
                if (region == "primary" && owner.selection.Page is CombatPanelSection.IncomingDamage or CombatPanelSection.Enemies && id == null && scroll.Offset > .5f)
                {
                    ((RunsScrollRect)scroll.Scroll).MoveBy((direction == MoveDirection.Up ? -1 : 1) * scroll.Scroll.scrollSensitivity);
                    return;
                }
                var rows = document?.Rows.Where(r => r.Actionable).ToArray() ?? Array.Empty<CombatRenderRow>();
                if (rows.Length == 0)
                {
                    if (CombatLayoutPolicy.ReturnFromScroll(direction == MoveDirection.Up, scroll.Offset))
                    { if (region == "ammo") owner.primary.Focus(owner.selection.FocusId); else owner.FocusSelector(); }
                    else ((RunsScrollRect)scroll.Scroll).MoveBy((direction == MoveDirection.Up ? -1 : 1) * scroll.Scroll.scrollSensitivity);
                    return;
                }
                id ??= focusedId;
                var index = Array.FindIndex(rows, r => r.Id == id);
                var next = CombatLayoutPolicy.Move(index, rows.Length, direction == MoveDirection.Up ? -1 : 1);
                if (next < 0 || direction == MoveDirection.Up && id == null && scroll.Offset <= .5f)
                { if (region == "selector") owner.focusTabs(); else owner.FocusSelector(); }
                else if (rows.Length > 0 && next < rows.Length) Focus(rows[next].Id);
                else if (region == "selector") owner.primary.Focus(owner.selection.FocusId);
                else ((RunsScrollRect)scroll.Scroll).MoveBy((direction == MoveDirection.Up ? -1 : 1) * scroll.Scroll.scrollSensitivity);
            }
            public void Dispose() { controls.Dispose(); document = null; scroll.Dispose(); }
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
            selector.Dispose(); primary.Dispose(); ammunition.Dispose(); outer.Dispose(); selection.Refresh(null);
            root.gameObject.SetActive(false); UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
