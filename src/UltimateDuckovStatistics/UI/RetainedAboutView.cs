using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    private AboutView? aboutView;

    // Static content owns no profile, projection, adapter or per-frame update.
    private sealed class AboutView : IDisposable
    {
        private const string Author = "bamboechop";
        private readonly RectTransform root, card;
        private readonly ScrollRegion scroll;
        private readonly TextMeshProUGUI title, description, author, invitation;
        private readonly NativeHeaderTitleTypography typography;
        private readonly Material material;
        private float width, height, scale;
        private bool dirty = true, disposed;

        public AboutView(RectTransform parent, NativeHeaderTitleTypography typography, Material material, Action focusTabs)
        {
            this.typography = typography; this.material = material;
            root = Node(parent, "AboutContentView");
            card = CreateOverviewPanel(root, "AboutCard", out var modifier); modifier.Radius = 20;
            scroll = new ScrollRegion(card, "AboutScroll", radius: 20);
            var viewport = scroll.Scroll.viewport;
            var mask = viewport.gameObject.AddComponent<ProceduralImage>(); mask.color = Color.white; mask.raycastTarget = false;
            viewport.gameObject.AddComponent<UniformModifier>().Radius = 20;
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            scroll.Rect.GetComponent<Selectable>().navigation = new Navigation { mode = Navigation.Mode.None };
            title = Text("AboutTitle", 40); description = Text("AboutDescription", 28);
            author = Text("AboutAuthor", 28); invitation = Text("AboutInvitation", 28);
            scroll.Rect.GetComponent<RunsFocusHandler>().Move = d =>
            {
                if (d == MoveDirection.Left || d == MoveDirection.Up && scroll.Offset <= .5f) focusTabs();
                else if (d == MoveDirection.Up || d == MoveDirection.Down)
                    scroll.SetOffset(scroll.Offset + (d == MoveDirection.Up ? -1 : 1) * 60);
            };
            RefreshText();
        }

        public void RefreshText()
        {
            if (disposed) return;
            title.text = "Ultimate Duckov Statistics";
            description.text = UiText.Get("ui.about_description");
            author.text = string.Format(CultureInfo.CurrentCulture, UiText.Get("ui.about_author"), Author);
            invitation.text = UiText.Get("ui.about_translation");
            dirty = true;
        }

        public void SetVisible(bool visible) { if (!disposed) root.gameObject.SetActive(visible); }
        public void FocusFirst() => GameManager.EventSystem?.SetSelectedGameObject(scroll.Rect.gameObject);

        public void Layout(RetainedVisualCanvasLayout shell, float canvasHeight)
        {
            if (disposed) return;
            var frame = CombatLayoutPolicy.Frame(shell, canvasHeight);
            if (frame.Width != width || frame.Height != height || frame.Scale != scale) { width = frame.Width; height = frame.Height; scale = frame.Scale; dirty = true; }
            if (!dirty || !root.gameObject.activeInHierarchy) return;
            dirty = false;
            root.localScale = new Vector3(frame.Scale, frame.Scale, 1);
            Place(root, frame.Left, frame.Top, width, height); Place(card, 0, 0, width, height);
            var textWidth = Math.Max(1, width - 60); float y = 30;
            y += Put(title, 30, y, textWidth) + 20;
            y += Put(description, 30, y, textWidth) + 20;
            y += Put(author, 30, y, textWidth) + 20;
            y += Put(invitation, 30, y, textWidth) + 20;
            scroll.Size(0, 0, width, height, y + 30);
        }
        private TextMeshProUGUI Text(string name, float size)
        {
            var label = Node(scroll.Content, name).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = typography.Font; label.fontSharedMaterial = material; label.fontSize = size;
            label.fontStyle = FontStyles.Normal; label.fontWeight = FontWeight.Regular; label.enableWordWrapping = true;
            label.enableAutoSizing = false; label.richText = false; label.color = Color.white; label.raycastTarget = false;
            label.overflowMode = TextOverflowModes.Overflow; label.alignment = TextAlignmentOptions.TopLeft; return label;
        }
        private static float Put(TextMeshProUGUI text, float x, float y, float width)
        { var h = Math.Max(1, text.GetPreferredValues(text.text, width, float.PositiveInfinity).y); Place(text.rectTransform, x, y, width, h); return h; }
        private static RectTransform Node(RectTransform parent, string name)
        { var r = (RectTransform)new GameObject(name, typeof(RectTransform)).transform; r.SetParent(parent, false); r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1); return r; }
        private static void Place(RectTransform rect, float x, float y, float w, float h)
        { rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(Math.Max(1, w), Math.Max(1, h)); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            scroll.Rect.GetComponent<RunsFocusHandler>().Move = null;
            scroll.Dispose(); root.gameObject.SetActive(false); UnityEngine.Object.Destroy(root.gameObject);
        }
    }
}
