using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    private RecordsView? recordsView;

    private sealed class RecordsView : IDisposable
    {
        private sealed class Row
        {
            public RectTransform Root = null!;
            public TextMeshProUGUI Label = null!, Value = null!;
        }
        private sealed class Card : IDisposable
        {
            public RectTransform Root = null!;
            public TextMeshProUGUI Heading = null!, Notice = null!;
            public RetainedLatestRunViewRunControl? Button;
            public RecordsCard? Data;
            public readonly List<Row> Rows = new();
            public void Dispose() => Button?.Button.onClick.RemoveAllListeners();
        }
        private readonly RectTransform root, overall, maps;
        private readonly ScrollRegion page;
        private readonly NativeHeaderTitleTypography typography;
        private readonly Material material;
        private readonly Action<string, string> route;
        private readonly Action focusTabs;
        private readonly TextMeshProUGUI overallHeading, mapsHeading, unavailable, noMaps;
        private readonly List<Card> overallCards = new(), mapCards = new();
        private readonly RecordsScrollState scroll = new();
        private RecordsPresentation? snapshot;
        private float width, height;
        private bool dirty = true, disposed;

        public RecordsView(RectTransform parent, NativeHeaderTitleTypography typography, Material material, Action<string, string> route, Action focusTabs)
        {
            this.typography = typography; this.material = material; this.route = route; this.focusTabs = focusTabs;
            root = Node(parent, "RecordsContentView");
            page = new ScrollRegion(root, "RecordsPage", radius: 20);
            page.Rect.GetComponent<Selectable>().navigation = new Navigation { mode = Navigation.Mode.None };
            page.Rect.GetComponent<RunsFocusHandler>().Move = direction =>
            {
                if (direction == MoveDirection.Left || direction == MoveDirection.Up && page.Offset <= 0) focusTabs();
                else if (direction == MoveDirection.Right) FocusFirstButton();
                else if (direction == MoveDirection.Up || direction == MoveDirection.Down)
                    ((RunsScrollRect)page.Scroll).MoveBy((direction == MoveDirection.Up ? -1 : 1) * page.Scroll.scrollSensitivity);
            };
            // Rounded stencil clipping includes TMP fallback submeshes at the viewport corners.
            var maskImage = page.Scroll.viewport.gameObject.AddComponent<UnityEngine.UI.ProceduralImage.ProceduralImage>();
            maskImage.color = Color.white; maskImage.raycastTarget = false;
            page.Scroll.viewport.gameObject.AddComponent<UniformModifier>().Radius = 20;
            page.Scroll.viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            overall = Panel(page.Content, "Overall", 20); maps = Panel(page.Content, "PerStartingMap", 20);
            overallHeading = Text(overall, "Heading", 46.3f, UiText.Get("ui.records_overall"));
            mapsHeading = Text(maps, "Heading", 46.3f, UiText.Get("ui.records_per_map"));
            unavailable = Text(root, "Unavailable", 30, UiText.Get("ui.profile_unavailable"));
            noMaps = Text(maps, "NoMaps", 20, UiText.Get("ui.records_no_maps"));
        }

        public void Refresh(RecordsPresentation? next)
        {
            scroll.Capture(page.Offset); scroll.Refresh(next?.GenerationId);
            snapshot = next;
            if (next != null) page.SetOffset(scroll.Offset);
            // Invalidation hides the whole old document immediately, including its controls.
            page.Rect.gameObject.SetActive(next != null); unavailable.gameObject.SetActive(next == null);
            if (next != null)
            {
                Bind(overallCards, overall, next.Overall, true);
                Bind(mapCards, maps, next.Maps, false);
                noMaps.gameObject.SetActive(next.Maps.Count == 0);
            }
            var focused = GameManager.EventSystem?.currentSelectedGameObject;
            if (focused != null && focused.transform.IsChildOf(root) && !focused.activeInHierarchy) focusTabs();
            dirty = true;
        }
        private void Bind(List<Card> pool, RectTransform parent, IReadOnlyList<RecordsCard> cards, bool buttons)
        {
            RecordsControlPool.Synchronize(pool, cards.Count, index => CreateCard(parent, buttons, index), Release);
            for (var i = 0; i < cards.Count; i++)
            {
                var control = pool[i]; var data = cards[i]; control.Data = data;
                control.Heading.text = data.Heading; control.Notice.text = data.Notice;
                if (control.Button != null)
                {
                    control.Button.Rect.gameObject.SetActive(data.RunId != null);
                    control.Button.Button.interactable = data.RunId != null;
                }
                while (control.Rows.Count < data.Rows.Count)
                {
                    var rowRoot = Panel(control.Root, "ValueRow" + control.Rows.Count, 10);
                    control.Rows.Add(new Row { Root = rowRoot, Label = Text(rowRoot, "Label", 20, ""), Value = Text(rowRoot, "Value", 20, "") });
                }
                for (var r = 0; r < control.Rows.Count; r++)
                {
                    var row = control.Rows[r]; row.Root.gameObject.SetActive(r < data.Rows.Count);
                    row.Label.text = r < data.Rows.Count ? data.Rows[r].Key : "";
                    row.Value.text = r < data.Rows.Count ? data.Rows[r].Value : "";
                }
            }
        }
        private Card CreateCard(RectTransform parent, bool buttons, int index)
        {
            var card = new Card { Root = Panel(parent, "Card" + index, 10) };
            card.Heading = Text(card.Root, "Heading", 34, ""); card.Notice = Text(card.Root, "Notice", 20, "");
            card.Notice.color = new Color32(177, 177, 177, 255);
            if (buttons)
            {
                card.Button = CreateOverviewLatestRunViewRun(card.Root, new RetainedLatestRunViewRunPresentation
                { IsVisible = true, Label = UiText.Get(RetainedOverviewLatestRunViewRunPolicy.TextKey) }, typography, material);
                card.Button.Button.navigation = new Navigation { mode = Navigation.Mode.None };
                AddButtonFeedback(card.Button.Button);
                card.Button.Button.gameObject.AddComponent<RunsFocusHandler>().Move = direction => MoveFocus(card, direction);
                card.Button.Button.onClick.AddListener(() =>
                {
                    if (snapshot != null && card.Data?.RunId is string id) route(snapshot.GenerationId, id);
                });
            }
            return card;
        }
        private static void Release(Card card)
        {
            card.Dispose(); card.Root.gameObject.SetActive(false); UnityEngine.Object.Destroy(card.Root.gameObject);
        }
        public void SetVisible(bool visible)
        {
            if (!visible) scroll.Capture(page.Offset);
            root.gameObject.SetActive(visible);
        }
        public void FocusPage() => GameManager.EventSystem?.SetSelectedGameObject(page.Rect.gameObject);
        private void FocusFirstButton()
        {
            var first = overallCards.FirstOrDefault(card => card.Data?.RunId != null);
            if (first?.Button != null) GameManager.EventSystem?.SetSelectedGameObject(first.Button.Button.gameObject);
        }
        private void MoveFocus(Card card, MoveDirection direction)
        {
            if (direction == MoveDirection.Left) { focusTabs(); return; }
            if (direction == MoveDirection.Right) { FocusPage(); return; }
            var buttons = overallCards.Where(candidate => candidate.Data?.RunId != null).ToList();
            var index = buttons.IndexOf(card) + (direction == MoveDirection.Up ? -1 : 1);
            if (index < 0) focusTabs();
            else if (index >= buttons.Count) FocusPage();
            else GameManager.EventSystem?.SetSelectedGameObject(buttons[index].Button!.Button.gameObject);
        }
        public void Layout(RetainedVisualCanvasLayout shell, float canvasHeight)
        {
            var scale = shell.ReferenceTransform.CanvasLength(1);
            var top = shell.Header.Top + shell.Header.Height + 40 * scale;
            var w = shell.Header.Width / scale; var h = Math.Max(1, (canvasHeight - top) / scale - 30);
            root.localScale = new Vector3(scale, scale, 1); Place(root, shell.Header.Left, top, w, h);
            if (w != width || h != height) { scroll.Capture(page.Offset); width = w; height = h; dirty = true; }
            if (!dirty || !root.gameObject.activeInHierarchy) return;
            dirty = false; Put(unavailable, 30, 30, width - 60);
            if (snapshot == null) return;
            var overallHeight = Section(overall, overallHeading, overallCards, 0);
            var mapHeight = Section(maps, mapsHeading, mapCards, overallHeight + RecordsLayoutPolicy.SectionGap);
            page.Size(0, 0, width, height, RecordsLayoutPolicy.DocumentHeight(overallHeight, mapHeight));
            page.SetOffset(scroll.Offset); scroll.Capture(page.Offset);
        }
        private float Section(RectTransform section, TextMeshProUGUI heading, List<Card> cards, float top)
        {
            var inner = Math.Max(1, width - 60);
            var y = 30 + Put(heading, 30, 30, inner) - CombatNativeTextMeasurement.AlignInkTop(heading) + 10;
            foreach (var card in cards) y += ArrangeCard(card, 30, y, inner) + RecordsLayoutPolicy.CardGap;
            if (section == maps && cards.Count == 0) y += Put(noMaps, 30, y, inner) + 20;
            var total = y + 10; Place(section, 0, top, width, total); return total;
        }
        private static float ArrangeCard(Card card, float x, float y, float w)
        {
            var inner = Math.Max(1, w - 40);
            var titleWidth = Math.Min(inner, Preferred(card.Heading));
            var titleHeight = Put(card.Heading, 20, 10, titleWidth);
            var bodyTop = 10 + titleHeight;
            if (card.Button?.Rect.gameObject.activeSelf == true)
            {
                var button = card.Button;
                button.Label.fontSize = RetainedOverviewLatestRunViewRunPolicy.ReferenceFontSize;
                button.Label.enableWordWrapping = true; button.Label.richText = false;
                var bw = Math.Min(inner, Preferred(button.Label) + 40);
                var bh = Math.Max(50, button.Label.GetPreferredValues(button.Label.text, Math.Max(1, bw - 40), float.PositiveInfinity).y + 12);
                var boxes = RunsFlowLayout.Arrange(inner, new[] { (titleWidth, titleHeight), (bw, bh) }, 20);
                Place(card.Heading.rectTransform, 20 + boxes[0].X, 10 + boxes[0].Y, boxes[0].Width, boxes[0].Height);
                Place(button.Rect, 20 + boxes[1].X, 10 + boxes[1].Y, bw, bh);
                Place(button.LabelRect, 20, 0, Math.Max(1, bw - 40), bh); button.Modifier.Radius = 25;
                bodyTop = 10 + Math.Max(boxes[0].Y + titleHeight, boxes[1].Y + bh);
            }
            var rowTop = bodyTop + 10;
            var preferred = card.Rows.Where(row => row.Root.gameObject.activeSelf).Select(row => Preferred(row.Label)).DefaultIfEmpty(0).Max();
            var columns = RecordsLayoutPolicy.Columns(Math.Max(1, inner - 24), preferred);
            foreach (var row in card.Rows.Where(row => row.Root.gameObject.activeSelf))
            {
                var lh = Put(row.Label, 12, 6, columns.LabelWidth);
                var vh = Put(row.Value, 12 + columns.ValueLeft, columns.Stacked ? 12 + lh : 6, columns.ValueWidth);
                var rh = RecordsLayoutPolicy.RowHeight(lh, vh, columns.Stacked);
                Place(row.Root, 20, rowTop, inner, rh); rowTop += rh + 6;
            }
            if (card.Notice.text.Length > 0) rowTop += Put(card.Notice, 20, rowTop + 4, inner) + 8;
            var total = rowTop + 14; Place(card.Root, x, y, w, total); return total;
        }
        public void Tick() { if (root.gameObject.activeInHierarchy && snapshot != null) { page.Cues(); if (!dirty) scroll.Capture(page.Offset); } }
        private TextMeshProUGUI Text(RectTransform parent, string name, float size, string value)
        {
            var label = Node(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = typography.Font; label.fontSharedMaterial = material; label.fontSize = size;
            label.fontStyle = FontStyles.Normal; label.fontWeight = FontWeight.Regular;
            label.enableWordWrapping = true; label.enableAutoSizing = false; label.richText = false;
            label.color = Color.white; label.raycastTarget = false; label.text = value;
            label.overflowMode = TextOverflowModes.Overflow; label.alignment = TextAlignmentOptions.TopLeft; return label;
        }
        private static float Preferred(TextMeshProUGUI label) => label.GetPreferredValues(label.text, float.PositiveInfinity, float.PositiveInfinity).x;
        private static float Put(TextMeshProUGUI label, float x, float y, float w)
        {
            w = Math.Max(1, w); var h = label.GetPreferredValues(label.text, w, float.PositiveInfinity).y;
            Place(label.rectTransform, x, y, w, h); return h;
        }
        private static RectTransform Panel(RectTransform parent, string name, float radius)
        { var rect = CreateOverviewPanel(parent, name, out var modifier); modifier.Radius = radius; return rect; }
        private static RectTransform Node(RectTransform parent, string name)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); return rect;
        }
        private static void Place(RectTransform rect, float x, float y, float w, float h)
        { rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            RecordsControlPool.Synchronize(overallCards, 0, _ => throw new InvalidOperationException(), Release);
            RecordsControlPool.Synchronize(mapCards, 0, _ => throw new InvalidOperationException(), Release);
            page.Dispose(); snapshot = null;
        }
    }
}
